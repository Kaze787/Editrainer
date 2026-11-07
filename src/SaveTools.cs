using PKHeX.Core;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A place in a list. It sorts by position, so box 10 comes after box 2.</summary>
public sealed record Place(int Order, string Text) : IComparable
{
    public int CompareTo(object? other) => Order.CompareTo((other as Place)?.Order ?? int.MaxValue);
    public override string ToString() => Text;
}

/// <summary>A Pokémon as it stood when a scan began. When that couldn't be worked out, Preview is null and Problem says why.</summary>
public sealed record ScanItem(PokemonEntry Entry, PKM? Preview, string? Problem);

/// <summary>One Pokémon as it would be saved, pending edits included.</summary>
public sealed record PokemonRow(PokemonEntry Entry, Place Place, string Species, string Nickname, int Level, string Nature, string Ability,
    string IVs, string EVs, string Moves, string Item, bool Shiny, bool Legal, string Report)
{
    public string Name => Nickname.Length == 0 ? Species : $"{Nickname} ({Species})";
    public string Reasons => string.Join("; ", Report.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
}

public sealed record ReportColumn(string Header, Func<PokemonRow, object> Value);

/// <summary>One thing that differs between two saves.</summary>
public sealed record SaveChange(string Kind, Place What, string First, string Second, string Detail);

/// <summary>A folder in Backups and what its cambios.json says about it.</summary>
public sealed record BackupInfo(string Folder, DateTime Date, string Game, string Summary, IReadOnlyList<string> Changes, string? Target, string? Payload, string? ExpectedHash)
{
    public bool CanRestore => Target != null && Payload != null;
}

/// <summary>What looks at a whole save. Only restoring a backup writes anything.</summary>
public static class SaveTools
{
    const string ChangesFile = "cambios.json", OverwrittenFile = "destino.anterior", BackupPattern = "*.original";
    static readonly JsonSerializerOptions Lenient = new() { PropertyNameCaseInsensitive = true };

    // What cambios.json holds. Any part can be missing from an old or damaged backup.
    sealed record BackupRecord(string? Game, string? Source, string? Destination, string? OriginalHash, MoneyRecord? Money,
        List<ItemRecord>? Items, List<PokemonRecord>? Pokemon, string? RestoredFrom);
    sealed record MoneyRecord(long Before, long After);
    sealed record ItemRecord(string? Name, int Before, int After);
    sealed record PokemonRecord(string? DisplayName, string? SpeciesName, string? Location, Draft? Before, Draft? After);

    /// <summary>
    /// Takes every Pokémon as it would be saved. This reads the pending edits, so it belongs on the thread that makes them;
    /// what it returns is the scan's own and can go to another thread while the editor carries on.
    /// </summary>
    public static List<ScanItem> Freeze(SaveDocument document) => document.Pokemon.Select(Freeze).ToList();

    static ScanItem Freeze(PokemonEntry entry)
    {
        try { return new(entry, entry.Preview(), null); }
        catch (Exception e) { return new(entry, null, e.Message); }
    }

    /// <summary>Judges what was frozen. It can take a while, so it reports how many are done and can be stopped.</summary>
    public static List<PokemonRow> Scan(IReadOnlyList<ScanItem> items, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var rows = new List<PokemonRow>(items.Count);
        foreach (var item in items)
        {
            cancel.ThrowIfCancellationRequested();
            rows.Add(Describe(item));
            progress?.Report(rows.Count);
        }
        return rows;
    }

    static int Slot(PokemonEntry entry) => entry.IsParty ? entry.Index : SaveDocument.TeamSize + entry.Index;

    static string NameIn(string[] names, int id) => id >= 0 && id < names.Length ? names[id] : id.ToString();

    static string ItemName(GameRules rules, int id) => rules.ItemName(id) is { Length: > 0 } name ? name : id.ToString();

    static string MoveNames(IEnumerable<ushort> moves) => string.Join(", ", moves.Where(m => m != 0).Select(m => NameIn(Catalog.Names.movelist, m)));

    static PokemonRow Describe(ScanItem item)
    {
        var entry = item.Entry;
        var rules = entry.Rules;
        var place = new Place(Slot(entry), entry.Location);
        // A save can hold anything, and one odd Pokémon must not cost the list of all the others.
        PokemonRow Failed(string? why) => new(entry, place, entry.SpeciesName, "", entry.Baseline.Level, "", "", "", "", "", "", false, false, T("No se pudo comprobar: {0}", why));
        if (item.Preview is not { } preview) return Failed(item.Problem);
        try
        {
            var legality = Catalog.CheckLegality(entry, preview);
            var saved = Draft.From(preview);
            return new(entry, place, entry.SpeciesName, preview.IsNicknamed ? preview.Nickname : "", preview.CurrentLevel,
                rules.HasNature ? NameIn(Catalog.Names.natures, (int)saved.Nature) : "",
                rules.HasAbility ? NameIn(Catalog.Names.abilitylist, saved.Ability) : "",
                string.Join('/', saved.IVs), string.Join('/', saved.EVs), MoveNames(saved.Moves),
                rules.HasHeldItems && saved.HeldItem != 0 ? ItemName(rules, saved.HeldItem) : "", preview.IsShiny, legality.Valid, legality.Report);
        }
        catch (Exception e) when (e is not OperationCanceledException) { return Failed(e.Message); }
    }

    /// <summary>The columns of the report: what this game has, in the same order in the window and in the file.</summary>
    public static List<ReportColumn> ReportColumns(GameRules rules)
    {
        var columns = new List<ReportColumn> {
            new(T("Lugar"), r => r.Place), new(T("Especie"), r => r.Species), new(T("Apodo"), r => r.Nickname), new(T("Nivel"), r => r.Level),
        };
        if (rules.HasNature) columns.Add(new(T("Naturaleza"), r => r.Nature));
        if (rules.HasAbility) columns.Add(new(T("Habilidad"), r => r.Ability));
        columns.Add(new(rules.IVName, r => r.IVs));
        columns.Add(new(rules.EVName, r => r.EVs));
        columns.Add(new(T("Movimientos"), r => r.Moves));
        if (rules.HasHeldItems) columns.Add(new(T("Objeto"), r => r.Item));
        if (rules.HasShiny) columns.Add(new(T("Shiny"), r => r.Shiny ? T("Sí") : T("No")));
        columns.Add(new(T("¿Legal?"), r => r.Legal ? T("Legal") : T("No legal")));
        return columns;
    }

    /// <summary>The report as text. The separator defaults to the system's, which is what lets a spreadsheet split the columns on open.</summary>
    public static string ToCsv(IEnumerable<PokemonRow> rows, IReadOnlyList<ReportColumn> columns, string? separator = null)
    {
        separator ??= CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        var csv = new StringBuilder();
        void Line(IEnumerable<string> cells) => csv.Append(string.Join(separator, cells.Select(c => CsvCell(c, separator)))).Append("\r\n");
        Line(columns.Select(c => c.Header));
        foreach (var row in rows) Line(columns.Select(c => c.Value(row).ToString() ?? ""));
        return csv.ToString();
    }

    static string CsvCell(string text, string separator)
    {
        // A spreadsheet runs what starts like a formula, and a nickname can come from someone else's trade.
        if (text.Length != 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.Contains(separator) || text.Contains('"') || text.Contains('\n') || text.Contains('\r') ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    /// <summary>With the mark that tells spreadsheets the file is UTF-8, or names lose their accents and Chinese.</summary>
    public static void ExportCsv(string path, IEnumerable<PokemonRow> rows, IReadOnlyList<ReportColumn> columns) => File.WriteAllText(path, ToCsv(rows, columns), new UTF8Encoding(true));

    public static bool SameGame(SaveFile a, SaveFile b) => a.GetType() == b.GetType() && a.Version == b.Version;

    /// <summary>What differs between two saves of one game, place by place: a Pokémon that moved shows up in both places.</summary>
    public static List<SaveChange> Compare(SaveDocument first, SaveDocument second)
    {
        if (!SameGame(first.Save, second.Save))
            throw new InvalidOperationException(T("Esas partidas son de juegos distintos: {0} y {1}.", first.Rules.FullName, second.Rules.FullName));
        var changes = ComparePokemon(first, second);
        if (first.Save.Money != second.Save.Money)
            changes.Add(new(T("Dinero"), new(changes.Count, ""), first.Save.Money.ToString("N0"), second.Save.Money.ToString("N0"), ""));
        changes.AddRange(CompareItems(first, second, changes.Count));
        return changes;
    }

    static List<SaveChange> ComparePokemon(SaveDocument first, SaveDocument second)
    {
        var before = first.Pokemon.ToDictionary(Slot);
        var after = second.Pokemon.ToDictionary(Slot);
        var changes = new List<SaveChange>();
        foreach (int slot in before.Keys.Union(after.Keys).Order())
        {
            var a = before.GetValueOrDefault(slot);
            var b = after.GetValueOrDefault(slot);
            if (a != null && b != null && a.Original.Data.SequenceEqual(b.Original.Data)) continue;
            bool sameSpecies = a?.Original.Species == b?.Original.Species;
            string detail = sameSpecies ? string.Join("; ", Differences(a!.Baseline, b!.Baseline, first.Rules).DefaultIfEmpty(T("Otros datos"))) : "";
            changes.Add(new(T("Pokémon"), new(changes.Count, (a ?? b)!.Location), Label(a), Label(b), detail));
        }
        return changes;
    }

    static string Label(PokemonEntry? entry)
    {
        if (entry == null) return T("(vacío)");
        string name = entry.DisplayName == entry.SpeciesName ? entry.SpeciesName : $"{entry.DisplayName} ({entry.SpeciesName})";
        return $"{name} · {T("Nv. {0}", entry.Baseline.Level)}{(entry.Baseline.IsShiny ? " ✦" : "")}";
    }

    static IEnumerable<SaveChange> CompareItems(SaveDocument first, SaveDocument second, int start)
    {
        var before = Bag(first.Save);
        var after = Bag(second.Save);
        return before.Keys.Union(after.Keys).Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k))
            .Select(k => (Key: k, Name: ItemName(first.Rules, k.Id)))
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select((x, i) => new SaveChange(PouchName(first.Rules, x.Key.Pouch), new(start + i, x.Name),
                before.GetValueOrDefault(x.Key).ToString(), after.GetValueOrDefault(x.Key).ToString(), ""));
    }

    // Older games can split one item over several slots. Key items are here too: they are progress, though nobody edits them.
    static Dictionary<(InventoryType Pouch, int Id), int> Bag(SaveFile save)
    {
        var counts = new Dictionary<(InventoryType, int), int>();
        foreach (var pouch in save.Inventory.Pouches)
            foreach (var item in pouch.Items.Where(i => i.Index != 0 && i.Count != 0))
                counts[(pouch.Type, item.Index)] = counts.GetValueOrDefault((pouch.Type, item.Index)) + item.Count;
        return counts;
    }

    static string PouchName(GameRules rules, InventoryType pouch) => pouch switch {
        InventoryType.KeyItems => T("Objetos clave"), InventoryType.PCItems => T("Objetos del PC"), _ => T(rules.PouchName(pouch)),
    };

    // A backup doesn't say which game's item numbers it used, so without the rules the item is only named as changed.
    static IEnumerable<string> Differences(Draft a, Draft b, GameRules? rules)
    {
        string Change(string what, object before, object after) => $"{what}: {before} → {after}";
        if (a.Level != b.Level) yield return Change(T("Nivel"), a.Level, b.Level);
        if (a.Nickname != b.Nickname) yield return Change(T("Apodo"), a.Nickname, b.Nickname);
        if (a.Trainer != b.Trainer) yield return Change(T("Entrenador original"), a.Trainer, b.Trainer);
        if (a.Nature != b.Nature && rules?.HasNature != false)
            yield return Change(T("Naturaleza"), NameIn(Catalog.Names.natures, (int)a.Nature), NameIn(Catalog.Names.natures, (int)b.Nature));
        if (a.Ability != b.Ability || a.AbilitySlot != b.AbilitySlot)
            yield return Change(T("Habilidad"), NameIn(Catalog.Names.abilitylist, a.Ability), NameIn(Catalog.Names.abilitylist, b.Ability));
        if (!a.IVs.SequenceEqual(b.IVs)) yield return Change(rules?.IVName ?? T("IV"), string.Join('/', a.IVs), string.Join('/', b.IVs));
        if (!a.EVs.SequenceEqual(b.EVs)) yield return Change(rules?.EVName ?? T("EV"), string.Join('/', a.EVs), string.Join('/', b.EVs));
        if (!a.Moves.SequenceEqual(b.Moves)) yield return Change(T("Movimientos"), MoveNames(a.Moves), MoveNames(b.Moves));
        if (a.HeldItem != b.HeldItem)
            yield return rules == null ? T("Objeto") : Change(T("Objeto"), a.HeldItem == 0 ? "—" : ItemName(rules, a.HeldItem), b.HeldItem == 0 ? "—" : ItemName(rules, b.HeldItem));
        if (a.IsShiny != b.IsShiny) yield return Change(T("Shiny"), a.IsShiny ? T("Sí") : T("No"), b.IsShiny ? T("Sí") : T("No"));
        if (a.Form != b.Form || a.FormArgument != b.FormArgument) yield return T("Forma");
        if (a.Ball != b.Ball) yield return T("Poké Ball");
        if (a.Friendship != b.Friendship) yield return Change(T("Amistad"), a.Friendship, b.Friendship);
    }

    /// <summary>Every backup, newest first. Reading never creates the folder, and a damaged backup is still listed.</summary>
    public static List<BackupInfo> ListBackups(string root) => !Directory.Exists(root) ? []
        : Directory.GetDirectories(root).Select(ReadBackup).OrderByDescending(b => b.Date).ToList();

    static DateTime BackupDate(string folder)
    {
        string name = Path.GetFileName(folder), stamp = SaveDocument.BackupStamp;
        return name.Length >= stamp.Length && DateTime.TryParseExact(name[..stamp.Length], stamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date : Directory.GetCreationTime(folder);
    }

    static BackupRecord? ReadRecord(string folder)
    {
        try { return JsonSerializer.Deserialize<BackupRecord>(File.ReadAllText(Path.Combine(folder, ChangesFile)), Lenient); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static BackupInfo ReadBackup(string folder)
    {
        var record = ReadRecord(folder);
        var original = Directory.GetFiles(folder, BackupPattern).FirstOrDefault();
        var overwritten = Path.Combine(folder, OverwrittenFile);
        // When another file took the edit, what was lost is that file's old content, not the save it was copied from.
        bool otherFile = File.Exists(overwritten);
        var (summary, changes) = Describe(record);
        return new(folder, BackupDate(folder), record?.Game ?? "", summary, changes, record?.Destination ?? record?.Source,
            otherFile ? overwritten : original, otherFile ? null : record?.OriginalHash);
    }

    static (string Summary, List<string> Changes) Describe(BackupRecord? record)
    {
        if (record == null) return (T("Sin detalles"), []);
        if (record.RestoredFrom is { } restored) return (T("Copia hecha antes de restaurar otro respaldo"), [T("La partida tal como estaba antes de restaurar {0}.", restored)]);
        var changes = new List<string>();
        var money = record.Money is { } m && m.Before != m.After ? m : null;
        if (money != null) changes.Add($"{T("Dinero")}: {money.Before:N0} → {money.After:N0}");
        var items = record.Items ?? [];
        changes.AddRange(items.Select(i => $"{i.Name}: {i.Before} → {i.After}"));
        var pokemon = record.Pokemon ?? [];
        changes.AddRange(pokemon.Select(p => {
            string edits = p.Before != null && p.After != null ? string.Join("; ", Differences(p.Before, p.After, null)) : "";
            string name = $"{p.DisplayName} ({p.Location})";
            return edits.Length == 0 ? name : $"{name}: {edits}";
        }));
        var parts = new List<string>();
        if (pokemon.Count != 0) parts.Add(T("{0} Pokémon", pokemon.Count));
        if (money != null) parts.Add(T("Dinero"));
        if (items.Count != 0) parts.Add(items.Count == 1 ? T("1 objeto") : T("{0} objetos", items.Count));
        return (parts.Count == 0 ? T("Sin cambios") : string.Join(" · ", parts), changes);
    }

    /// <summary>
    /// Puts a backup back over the file it came from, after keeping a copy of that file, and checks the result byte by byte.
    /// Returns the copy's folder, or null when there was no file left to keep.
    /// </summary>
    public static string? RestoreBackup(BackupInfo backup, string backupRoot, Func<SaveFile, string?>? findEmulator = null)
    {
        findEmulator ??= SaveDocument.RunningEmulator;
        if (!backup.CanRestore) throw new InvalidOperationException(T("Este respaldo no dice a qué archivo pertenece. Ábrelo y usa «Guardar como…»."));
        var target = Path.GetFullPath(backup.Target!);
        var restored = SaveDocument.Read(backup.Payload!);
        if (backup.ExpectedHash != null && restored.OriginalHash != backup.ExpectedHash) throw new InvalidOperationException(T("Este respaldo está dañado: ya no coincide con la partida que se guardó."));
        if (findEmulator(restored.Save) is { } emulator) throw new InvalidOperationException(T("Cierra {0} antes de restaurar. Después vuelve a intentarlo.", emulator));
        if (!Directory.Exists(Path.GetDirectoryName(target))) throw new InvalidOperationException(T("La carpeta de esa partida ya no existe: {0}", Path.GetDirectoryName(target)));
        var bytes = restored.OriginalBytes;
        var current = File.Exists(target) ? File.ReadAllBytes(target) : null;
        if (current != null && current.SequenceEqual(bytes)) throw new InvalidOperationException(T("La partida ya es igual a este respaldo."));
        // A file that no longer reads as a save is the usual reason to restore, so only another game stops it.
        // The kind of save decides, because some are told apart by size alone and damage leaves their version blank.
        if (current != null && TryRead(current, target) is { } existing && existing.GetType() != restored.Save.GetType())
            throw new InvalidOperationException(T("Ese archivo ahora es de otro juego: {0}.", new GameRules(existing).FullName));
        var safetyCopy = current == null ? null : KeepCopy(target, current, bytes, restored, backup, backupRoot);
        var temp = target + ".editrainer-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            // Check again immediately before the atomic replacement.
            bool emulatorOpened = findEmulator(restored.Save) != null;
            var now = File.Exists(target) ? File.ReadAllBytes(target) : null;
            if (emulatorOpened || (now == null) != (current == null) || now != null && !now.SequenceEqual(current!))
                throw new InvalidOperationException(T("La partida o el emulador cambió mientras se restauraba. No se reemplazó el archivo."));
            if (current != null) File.Replace(temp, target, null);
            else File.Move(temp, target);
            if (!File.ReadAllBytes(target).SequenceEqual(bytes)) throw new IOException(T("La verificación final no coincide. Conserva el respaldo."));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return safetyCopy;
    }

    // Damaged data can fail in any way at all, and each of them means the same here.
    static SaveFile? TryRead(byte[] data, string path)
    {
        try { return SaveUtil.GetSaveFile(data.ToArray(), path); }
        catch (Exception) { return null; }
    }

    // In the same form as any other backup, so that this one can be listed and restored as well.
    static string KeepCopy(string target, byte[] current, byte[] replacement, SaveDocument restored, BackupInfo backup, string backupRoot)
    {
        var folder = SaveDocument.NewBackupFolder(backupRoot);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Path.GetFileName(target) + ".original");
        File.WriteAllBytes(file, current);
        File.WriteAllText(Path.Combine(folder, ChangesFile), JsonSerializer.Serialize(new {
            game = restored.Rules.FullName, source = target, destination = target, originalHash = SaveDocument.Hash(current),
            editedHash = SaveDocument.Hash(replacement), restoredFrom = Path.GetFileName(backup.Folder),
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!File.ReadAllBytes(file).SequenceEqual(current)) throw new IOException(T("No se pudo verificar el respaldo."));
        return folder;
    }
}
