using PKHeX.Core;
using PKHeX.Core.AutoMod;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A Pokémon made for this save, ready to be placed. When it couldn't be made, Pokemon is null and Problem says why.</summary>
public sealed record Generated(ushort Species, byte Form, PKM? Pokemon, string? Problem);

/// <summary>What a pass through the Pokédex left. Untried counts the species that had no room, or that a stop left out.</summary>
public sealed record DexRun(IReadOnlyList<Generated> Results, int Owned, int Untried, bool Stopped)
{
    public IEnumerable<PKM> Ready => Results.Where(r => r.Pokemon != null).Select(r => r.Pokemon!);
    public int ReadyCount => Results.Count(r => r.Pokemon != null);
    public int Skipped => Results.Count(r => r.Pokemon == null);
}

/// <summary>
/// Makes legal Pokémon with the Auto-Legality engine. This reads the save and changes nothing: what it makes goes
/// to the document to be placed, after being judged here as it would be saved. It can run off the interface's thread.
/// </summary>
public sealed class LegalGenerator
{
    const int SecondsPerPokemon = 15, MillionInId = 1_000_000, FirstGenerationWithLongIds = 7, ShownLines = 3;
    readonly SaveDocument document;
    readonly GameRules rules;
    readonly SimpleTrainerInfo trainer;
    readonly string trainerLines;

    // The engine's settings are global, and this app only ever wants these.
    static LegalGenerator()
    {
        APILegality.EnableDevMode = true;              // otherwise every search first asks GitHub for the latest version
        APILegality.AllowTrainerOverride = true;       // the save's trainer reaches the engine in the set's own lines
        APILegality.AllowBatchCommands = false;        // a pasted set stays a set, not a script over the Pokémon's fields
        APILegality.ForceLevel100for50 = false;        // a set at level 50 stays at level 50
        APILegality.SetAllLegalRibbons = false;
        APILegality.UseMarkings = false;
        APILegality.GameVersionPriority = GameVersionPriorityType.PriorityOrder;
        APILegality.Timeout = SecondsPerPokemon;
    }

    public LegalGenerator(SaveDocument document)
    {
        this.document = document;
        rules = document.Rules;
        // A save can know only its pair of games, and the engine needs the one.
        trainer = new SimpleTrainerInfo(document.Save, rules.Version);
        trainerLines = TrainerLines(trainer);
        // This game first, so a Pokémon comes from another one only when it has to.
        APILegality.PriorityOrder = SettingsUtil.SanitizePriorityOrder([rules.Version]).Where(IsSearchable).ToList();
        DexSpecies = Enumerable.Range(1, rules.Personal.MaxSpeciesID).Select(s => (ushort)s).Where(s => Forms(s).Any()).ToList();
    }

    /// <summary>Every species this game admits, in Pokédex order.</summary>
    public IReadOnlyList<ushort> DexSpecies { get; }

    // PKHeX stops with an error on a version that belongs to no generation, instead of passing over it.
    static bool IsSearchable(GameVersion version) => version == GameVersion.GO || version.Context != EntityContext.None;

    // Left to itself the engine signs what it finds from other games with a trainer of its own.
    static string TrainerLines(SimpleTrainerInfo trainer)
    {
        bool longIds = trainer.Generation >= FirstGenerationWithLongIds;
        uint tid = longIds ? trainer.ID32 % MillionInId : trainer.TID16;
        uint sid = longIds ? trainer.ID32 / MillionInId : trainer.SID16;
        string name = trainer.OT.Length == 0 ? "" : $"\nOT: {trainer.OT}";
        return $"{name}\nOTGender: {(trainer.Gender == 1 ? "Female" : "Male")}\nTID: {tid}\nSID: {sid}\nLanguage: {(LanguageID)trainer.Language}";
    }

    /// <summary>Makes what a Showdown set describes. Text that isn't a set, or names what this game lacks, is refused.</summary>
    public Generated FromSet(string text, CancellationToken cancel = default)
    {
        var parsed = new ShowdownSet((text + trainerLines).AsSpan(), LocalizationOf(text));
        if (parsed.Species == 0) throw new InvalidOperationException(T("Ese texto no es un set de Showdown."));
        var set = new RegenTemplate(parsed, (byte)rules.Generation);
        // The engine takes the lines it knows, so what is still here is what nobody understood.
        if (parsed.InvalidLines.Count != 0)
            throw new InvalidOperationException(T("El set tiene líneas que no se entienden: {0}", string.Join(", ", parsed.InvalidLines.Select(l => l.Value).Take(ShownLines))));
        string name = NameOf(set.Species);
        if (set.Species > rules.Personal.MaxSpeciesID || !Forms(set.Species).Any()) throw new InvalidOperationException(T("{0} no existe en {1}.", name, rules.FullName));
        if (rules.HasStoredForm && !IsKept(set.Species, set.Form)) throw new InvalidOperationException(T("Esa forma de {0} no se puede llevar a {1}.", name, rules.FullName));
        return Search(set, cancel);
    }

    // Showdown writes in English, but a set typed by hand may be in the language on screen.
    static BattleTemplateLocalization? LocalizationOf(string text)
    {
        var english = new ShowdownSet(text.AsSpan());
        if (english.Species != 0 && english.InvalidLines.Count == 0) return null;
        var local = BattleTemplateLocalization.GetLocalization(Strings.Current.GameData);
        var other = new ShowdownSet(text.AsSpan(), local);
        return other.Species != 0 && (english.Species == 0 || other.InvalidLines.Count < english.InvalidLines.Count) ? local : null;
    }

    /// <summary>One legal Pokémon of a species, in its first form that can be had.</summary>
    public Generated FromSpecies(ushort species, CancellationToken cancel = default)
    {
        Generated? first = null;
        foreach (byte form in Forms(species))
        {
            var made = Search(BareSet(species, form), cancel);
            if (made.Pokemon != null) return made;
            first ??= made;
        }
        return first ?? new(species, 0, null, T("Ningún encuentro legal da este Pokémon."));
    }

    /// <summary>One of each species the save lacks, until the room runs out. Stopping keeps what was made.</summary>
    public DexRun LivingDex(IReadOnlySet<ushort> owned, int room, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var wanted = DexSpecies.Where(s => !owned.Contains(s)).ToList();
        var results = new List<Generated>();
        int ready = 0;
        bool stopped = false;
        try
        {
            foreach (ushort species in wanted)
            {
                if (ready == room) break;
                cancel.ThrowIfCancellationRequested();
                var made = FromSpecies(species, cancel);
                results.Add(made);
                if (made.Pokemon != null) ready++;
                progress?.Report(results.Count);
            }
        }
        catch (OperationCanceledException) { stopped = true; }
        return new(results, DexSpecies.Count - wanted.Count, wanted.Count - results.Count, stopped);
    }

    /// <summary>A species with its form, as the game names it.</summary>
    public string NameOf(Generated made) => NameOf(made.Species) + (made.Form == 0 || !rules.HasStoredForm ? "" : $" · {Catalog.FormName(rules, made.Species, made.Form)}");

    static string NameOf(ushort species) => species < Catalog.Names.specieslist.Length ? Catalog.Names.specieslist[species] : species.ToString();

    IEnumerable<byte> Forms(ushort species) => Enumerable.Range(0, Math.Max(1, (int)rules.Personal[species].FormCount)).Select(f => (byte)f).Where(f => IsKept(species, f));

    // Forms that only last a battle, or that take two Pokémon, are never in a box.
    bool IsKept(ushort species, byte form) => rules.Personal.IsPresentInGame(species, form)
        && !FormInfo.IsBattleOnlyForm(species, form, (byte)rules.Generation) && !FormInfo.IsFusedForm(species, form, (byte)rules.Generation)
        && !FormInfo.IsTotemForm(species, form, rules.Context) && !FormInfo.IsLordForm(species, form, rules.Context);

    // Only the first line: the rest of a blank Pokémon's set would ask for level 1 with no moves, which nobody can satisfy.
    RegenTemplate BareSet(ushort species, byte form)
    {
        var blank = rules.Save.BlankPKM;
        blank.Species = species; blank.Form = form; blank.Gender = blank.GetSaneGender();
        string line = new ShowdownSet(blank).Text.ReplaceLineEndings("\n").Split('\n')[0];
        return new RegenTemplate(new ShowdownSet((line + trainerLines).AsSpan()), (byte)rules.Generation) { Nickname = "", Gender = null };
    }

    Generated Search(RegenTemplate set, CancellationToken cancel)
    {
        byte form = set.Form;
        Generated Failed(string why) => new(set.Species, form, null, why);
        try
        {
            // The engine can't be interrupted, only left behind: it gives up by itself when its time is up.
            var search = Task.Run(() => Find(set), CancellationToken.None);
            search.Wait(cancel);
            var found = search.Result;
            if (found.Status == LegalizationResult.Timeout) return Failed(T("Se agotó el tiempo de búsqueda."));
            if (found.Status != LegalizationResult.Regenerated || found.Created.Species != set.Species) return Failed(T("Ningún encuentro legal da este Pokémon."));
            var pokemon = document.Adopt(set.Moves.All(m => m == 0) ? WithMoves(found.Created) : found.Created);
            var verdict = Catalog.CheckLegality(new PokemonEntry(rules, false, 0, pokemon, isNew: true), pokemon);
            return verdict.Valid ? new(set.Species, pokemon.Form, pokemon, null) : Failed(T("El resultado no es legal: {0}", FirstLine(verdict.Report)));
        }
        catch (OperationCanceledException) { throw; }
        // A set can reach any corner of the engine, and whatever happens there must not cost the rest of the run.
        catch (Exception e) { return Failed(T("No se pudo generar: {0}", (e as AggregateException)?.Flatten().InnerException?.Message ?? e.Message)); }
    }

    // In the type of this very save: a Japanese Game Boy Pokémon isn't the same kind of file as any other.
    APILegality.AsyncLegalizationResult Find(RegenTemplate set)
    {
        var template = rules.Save.BlankPKM;
        template.Version = trainer.Version;
        EncounterMovesetGenerator.OptimizeCriteria(template, trainer);
        template.ApplySetDetails(set);
        return trainer.TryAPIConvert(set, template);
    }

    // A set with no moves leaves only the few an encounter comes with, whatever the level.
    PKM WithMoves(PKM pokemon)
    {
        var fuller = pokemon.Clone();
        fuller.SetMoveset();
        fuller.HealPP();
        fuller.RefreshChecksum();
        return new LegalityAnalysis(fuller, rules.Personal, StorageSlotType.Box).Valid ? fuller : pokemon;
    }

    static string FirstLine(string text) => text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
}

/// <summary>Generated Pokémon arrive like clones and imports: pending, undoable and checked after saving.</summary>
public sealed partial class SaveDocument
{
    /// <summary>A Pokémon as this save will hold it, through the same door as a file. It reads the save and changes nothing.</summary>
    internal PKM Adopt(PKM created)
    {
        var pokemon = ForThisGame(created, new SlotPlace(false, 0));
        if (pokemon is PB7 letsGo) { letsGo.ResetCalculatedValues(); pokemon.RefreshChecksum(); }
        return pokemon;
    }

    /// <summary>The species the save already has. An egg doesn't count yet.</summary>
    public HashSet<ushort> OwnedSpecies() => Pokemon.Where(p => !p.Original.IsEgg).Select(p => p.Original.Species).ToHashSet();

    /// <summary>How many places of the open boxes are free.</summary>
    public int FreePlaces => OpenBoxes * Save.BoxSlotCount - TakenPlaces().Count(i => i < OpenBoxes * Save.BoxSlotCount);

    /// <summary>Places a generated Pokémon. Its item comes from the bag, and without one it stays out unless it needs it to be legal.</summary>
    public Arrival PlaceGenerated(PKM pokemon, SlotPlace target)
    {
        RequireFree(target);
        var (entry, missing) = Arrive(pokemon.Clone(), target);
        if (missing == null) return new(entry, T("{0} generado en {1}. Guarda para aplicarlo.", entry.DisplayName, entry.Location));
        if (!Catalog.CheckLegality(entry).Valid)
        {
            UndoLayout();
            throw new InvalidOperationException(T("{0} solo es legal con {1}, y no hay en la bolsa.", entry.DisplayName, missing));
        }
        return new(entry, T("{0} generado en {1}.", entry.DisplayName, entry.Location) + " " + T("Llega sin {0}: no hay en la bolsa. Guarda para aplicarlo.", missing));
    }

    /// <summary>Places many at once from this box on, as one change that one undo takes back. Returns how many fit.</summary>
    public int PlaceAll(IEnumerable<PKM> pokemon, int box)
    {
        RequireArrange();
        int placed = 0;
        Change(() => {
            foreach (var one in pokemon)
            {
                if (FreePlace(box) is not { } place) break;
                var copy = one.Clone();
                if (Rules.HasHeldItems && copy.HeldItem != 0) { copy.HeldItem = 0; copy.RefreshChecksum(); }
                Pokemon.Add(new PokemonEntry(Rules, false, place.Index, copy, isNew: true));
                placed++;
            }
        });
        return placed;
    }
}
