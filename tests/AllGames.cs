using PKHeX.Core;
using Editrainer;
using System.Text.RegularExpressions;

/// <summary>
/// Runs every game through the whole path a real save takes: a file on disk is opened, edited,
/// saved, reopened and compared. Saves are generated here, so nothing of the user's is touched.
/// </summary>
static class AllGames
{
    static int passed, failed;
    static readonly List<string> failures = [];

    static readonly (GameVersion Game, string Label)[] Games =
    [
        (GameVersion.RD, "Rojo"), (GameVersion.BU, "Azul"), (GameVersion.YW, "Amarillo"),
        (GameVersion.GD, "Oro"), (GameVersion.SI, "Plata"), (GameVersion.C, "Cristal"),
        (GameVersion.R, "Rubí"), (GameVersion.S, "Zafiro"), (GameVersion.E, "Esmeralda"), (GameVersion.FR, "Rojo Fuego"), (GameVersion.LG, "Verde Hoja"),
        (GameVersion.D, "Diamante"), (GameVersion.P, "Perla"), (GameVersion.Pt, "Platino"), (GameVersion.HG, "HeartGold"), (GameVersion.SS, "SoulSilver"),
        (GameVersion.B, "Negro"), (GameVersion.W, "Blanco"), (GameVersion.B2, "Negro 2"), (GameVersion.W2, "Blanco 2"),
        (GameVersion.X, "X"), (GameVersion.Y, "Y"), (GameVersion.OR, "Rubí Omega"), (GameVersion.AS, "Zafiro Alfa"),
        (GameVersion.SN, "Sol"), (GameVersion.MN, "Luna"), (GameVersion.US, "Ultrasol"), (GameVersion.UM, "Ultraluna"),
        (GameVersion.GP, "Let's Go Pikachu"), (GameVersion.GE, "Let's Go Eevee"),
        (GameVersion.SW, "Espada"), (GameVersion.SH, "Escudo"), (GameVersion.BD, "Diamante Brillante"), (GameVersion.SP, "Perla Reluciente"),
        (GameVersion.PLA, "Leyendas: Arceus"), (GameVersion.SL, "Escarlata"), (GameVersion.VL, "Púrpura"), (GameVersion.ZA, "Leyendas: Z-A"),
    ];

    static void Check(string game, bool ok, string what, string detail = "")
    {
        if (ok) { passed++; return; }
        failed++; failures.Add($"{game}: {what} {detail}");
    }

    // A species every game has: Pikachu exists from Gen 1 to Z-A, and so does a Gen 1 starter or Gen 1 Eevee.
    static ushort SpeciesFor(SaveFile sav, ushort wanted)
    {
        if (sav.Personal.IsPresentInGame(wanted, 0)) return wanted;
        return (ushort)Enumerable.Range(1, sav.MaxSpeciesID).First(i => sav.Personal.IsPresentInGame((ushort)i, 0));
    }

    static PKM Make(SaveFile sav, ushort species, GameVersion version)
    {
        var pk = sav.BlankPKM;
        species = SpeciesFor(sav, species);
        pk.Species = species;
        pk.Language = sav.Language;
        pk.Nickname = SpeciesName.GetSpeciesName(species, sav.Language);
        pk.IsNicknamed = false;
        pk.OriginalTrainerName = sav.OT;
        pk.OriginalTrainerGender = sav.Gender;
        pk.ID32 = sav.ID32;
        pk.Version = pk.Format <= 2 ? GameVersion.RD : version;
        pk.PID = 0x12345678;
        pk.EncryptionConstant = 0x1357ACE1;
        pk.Gender = pk.PersonalInfo.RandomGender();
        pk.CurrentLevel = 12;
        pk.SetIVs([5, 6, 7, 8, 9, 9]);
        pk.Move1 = 1;
        pk.HealPP();
        pk.SetStats(pk.GetStats(pk.PersonalInfo));
        pk.CurrentFriendship = 70;
        pk.RefreshChecksum();
        return pk;
    }

    static void Run(GameVersion version, string label, string work)
    {
        var blank = TestSaves.Start(version);
        // Let's Go keeps its party as pointers into the box, so a test team goes in the box there.
        bool boxOnly = blank is SAV7b;
        if (!boxOnly)
        {
            blank.SetPartySlotAtIndex(Make(blank, 25, version), 0, EntityImportSettings.None);
            blank.SetPartySlotAtIndex(Make(blank, 133, version), 1, EntityImportSettings.None);
        }
        else
        {
            blank.SetBoxSlotAtIndex(Make(blank, 25, version), 1, EntityImportSettings.None);
            blank.SetBoxSlotAtIndex(Make(blank, 133, version), 2, EntityImportSettings.None);
        }
        blank.SetBoxSlotAtIndex(Make(blank, 1, version), 0, EntityImportSettings.None);
        blank.Money = 3000;
        var path = Path.Combine(work, version + ".sav");
        File.WriteAllBytes(path, TestSaves.Finish(blank));

        var doc = SaveDocument.Open(path);
        var rules = doc.Rules;
        Check(label, doc.Save.GetType() == blank.GetType(), "reconoce el juego", doc.Save.GetType().Name);
        Check(label, rules.ShortName.Length > 0, "tiene nombre", doc.Save.Version.ToString());
        Check(label, doc.Pokemon.Count == 3, "lee equipo y cajas", doc.Pokemon.Count.ToString());
        Check(label, doc.Money == 3000, "lee el dinero", doc.Money.ToString());
        Check(label, doc.BuildOutput().SequenceEqual(doc.OriginalBytes), "sin cambios no altera el archivo");
        Check(label, doc.TeamSlots().Count == SaveDocument.TeamSize, "el equipo tiene seis lugares", doc.TeamSlots().Count.ToString());
        Check(label, doc.BoxCount > 0 && doc.OpeningBox >= 0 && doc.OpeningBox < doc.BoxCount, "abre una caja que existe", $"{doc.OpeningBox} de {doc.BoxCount}");
        var everyBox = Enumerable.Range(0, doc.BoxCount).Select(doc.BoxSlots).ToList();
        Check(label, everyBox.All(b => b.Count == doc.Save.BoxSlotCount), "cada caja muestra todos sus lugares");
        var placed = doc.TeamSlots().Concat(everyBox.SelectMany(b => b)).OfType<PokemonEntry>().ToList();
        Check(label, placed.Count == doc.Pokemon.Count && placed.Distinct().Count() == placed.Count, "cada Pokémon ocupa un solo lugar", placed.Count.ToString());
        Check(label, doc.Pokemon.Where(p => !p.IsParty).All(p => doc.BoxSlots(doc.BoxOf(p))[p.Index % doc.Save.BoxSlotCount] == p), "cada Pokémon está en el lugar de su caja");
        Check(label, Enumerable.Range(0, doc.BoxCount).All(b => rules.BoxName(b).Length > 0 && (Strings.Current.Code == "en" || !rules.BoxName(b).StartsWith("Box "))), "nombra las cajas", rules.BoxName(0));
        Check(label, doc.BoxLabel(0).Contains($"/ {doc.Save.BoxSlotCount}"), "la lista de cajas cuenta sus Pokémon", doc.BoxLabel(0));
        foreach (var e in doc.Pokemon)
        {
            Check(label, Catalog.GetMoves(e).Count > 1, "lista movimientos de " + e.SpeciesName);
            Check(label, Catalog.GetFinalForms(e).Count > 0, "calcula evoluciones de " + e.SpeciesName);
            Check(label, Catalog.TypeName(e.Original.PersonalInfo.Type1).Length > 0, "nombra el tipo");
            Check(label, e.Location.Length > 0, "ubica el Pokémon");
        }

        var first = doc.Pokemon[0];
        var boxed = doc.Pokemon[2];
        ushort firstSpecies = first.Original.Species;
        var moves = Catalog.GetMoves(first).Where(m => m.Id != 0 && m.Id != first.Edit.Moves[0]).Take(1).ToList();
        ushort newMove = moves.Count != 0 ? (ushort)moves[0].Id : (ushort)0;
        if (newMove != 0) first.Edit.Moves[1] = newMove;
        first.Edit.IVs[1] = rules.MaxIV; first.Edit.IVs[3] = rules.MaxIV - 1;
        first.Edit.EVs[1] = Math.Min(100, rules.MaxEV);
        boxed.Edit.EVs[3] = Math.Min(50, rules.MaxEV);
        if (rules.HasNature) first.Edit.Nature = Nature.Timid;
        if (rules.HasFriendship) first.Edit.Friendship = 255;
        if (rules.HasShiny) Catalog.SetShiny(boxed, true);
        var error = doc.ValidateEdits();
        Check(label, error == null, "acepta las ediciones", error ?? "");
        doc.Money = rules.MaxMoney;

        // An item that exists in this game's main pouch, then one held by a Pokémon.
        var item = doc.Items.FirstOrDefault(i => i.Pouch != InventoryType.TMHMs && i.MaxCount >= 5);
        var held = rules.HasHeldItems ? doc.Items.FirstOrDefault(i => rules.CanHold(i.Id) && i.Id != item?.Id && i.MaxCount >= 2) : null;
        Check(label, item != null || doc.Items.Count == 0 && rules.Generation == 3, "ofrece objetos", doc.Items.Count.ToString());
        if (item != null) doc.SetItemCount(item.Id, 5);
        if (held != null) { doc.SetItemCount(held.Id, 2); doc.EquipItem(first, held.Id); }

        var edited = Path.Combine(work, version + ".edited.sav");
        File.WriteAllBytes(edited, doc.BuildOutput());
        var reopened = SaveDocument.Open(edited);
        var r = reopened.Pokemon[0];
        var rb = reopened.Pokemon[2];
        Check(label, reopened.Pokemon.Count == 3, "conserva los Pokémon");
        Check(label, reopened.Save.Money == rules.MaxMoney, "guarda el dinero", reopened.Save.Money.ToString());
        Check(label, r.Original.IV_ATK == rules.MaxIV, "guarda los " + rules.IVName, r.Original.IV_ATK.ToString());
        Check(label, r.Original.IV_SPE == rules.MaxIV - 1, "guarda la velocidad", r.Original.IV_SPE.ToString());
        Check(label, r.Edit.EVs[1] == Math.Min(100, rules.MaxEV), "guarda los " + rules.EVName, r.Edit.EVs[1].ToString());
        Check(label, rb.Edit.EVs[3] == Math.Min(50, rules.MaxEV), "guarda el Pokémon de la caja", rb.Edit.EVs[3].ToString());
        if (newMove != 0) Check(label, r.Original.Move2 == newMove, "guarda el movimiento", r.Original.Move2.ToString());
        if (rules.HasNature) Check(label, r.Edit.Nature == Nature.Timid, "guarda la naturaleza", r.Edit.Nature.ToString());
        if (rules.HasFriendship) Check(label, r.Original.CurrentFriendship == 255, "guarda la amistad", r.Original.CurrentFriendship.ToString());
        if (rules.HasShiny) Check(label, rb.Original.IsShiny, "guarda el shiny");
        if (item != null) Check(label, reopened.GetItemCount(item.Id) == 5, "guarda " + item.Name, reopened.GetItemCount(item.Id).ToString());
        if (held != null) Check(label, r.Original.HeldItem == held.Id && reopened.GetItemCount(held.Id) == 1, "equipa " + held.Name,
            $"lleva {r.Original.HeldItem}, quedan {reopened.GetItemCount(held.Id)}");
        Check(label, r.Original.Species == doc.Pokemon[0].Original.Species && r.Original.CurrentLevel == 12, "no cambia especie ni nivel");
        Check(label, reopened.Pokemon[1].Original.Species == doc.Pokemon[1].Original.Species, "no toca al resto del equipo");

        // Shiny and nature together are the tricky case up to Gen 5, where the PID decides both.
        if (rules.HasShiny && !rules.IsGameBoy)
        {
            Catalog.SetShiny(r, true);
            if (rules.HasNature) r.Edit.Nature = Nature.Modest;
            var preview = r.Preview();
            Check(label, preview.IsShiny, "shiny y naturaleza a la vez");
            if (rules.HasNature) Check(label, preview.StatAlignment == Nature.Modest, "conserva la naturaleza siendo shiny", preview.StatAlignment.ToString());
            Check(label, preview.PID == r.Preview().PID, "el PID no cambia entre vistas previas");
            Check(label, preview.Gender == r.Original.Gender, "el shiny no cambia el género");
        }

        // Taking everything out again must leave nothing behind.
        reopened.ResetPokemon(r);
        if (item != null) reopened.SetItemCount(item.Id, 0);
        if (item != null)
        {
            var emptied = Path.Combine(work, version + ".emptied.sav");
            File.WriteAllBytes(emptied, reopened.BuildOutput());
            Check(label, SaveDocument.Open(emptied).GetItemCount(item.Id) == 0, "retira el objeto");
        }

        // The whole writer, backup included, on a throwaway copy.
        var copy = Path.Combine(work, version + ".copy.sav");
        File.Copy(path, copy, true);
        var writer = SaveDocument.Open(copy);
        writer.Money = 1234;
        var backup = writer.Write(copy, Path.Combine(work, "Backups"), _ => null);
        Check(label, File.Exists(Path.Combine(backup, Path.GetFileName(copy) + ".original")), "crea el respaldo");
        Check(label, SaveDocument.Open(copy).Save.Money == 1234, "el guardado real funciona");
    }

    // Species whose forms look different, tried first when a test team has to be picked.
    static readonly ushort[] Showcase = [666, 869, 774, 479, 676, 720, 585, 422, 493, 386, 201, 741, 931, 978];
    static int formsTried;

    static PokemonEntry Entry(SaveFile sav, GameRules rules, ushort species, GameVersion version)
    {
        var pk = Make(sav, species, version);
        if (species == (ushort)Species.Minior && rules.HasStoredForm) pk.Form = 7;   // stored as its core, never as a meteor
        if (rules.HasStoredForm) pk.RefreshAbility(0);   // a played Pokémon always has one
        pk.RefreshChecksum();
        return new PokemonEntry(rules, false, 0, pk);
    }

    // Every species with forms in this game, every form, with no file involved.
    static void EveryForm(SaveFile sav, GameRules rules, string label, GameVersion version)
    {
        for (ushort species = 1; species <= sav.MaxSpeciesID; species++)
        {
            if (!sav.Personal.IsPresentInGame(species, 0)) continue;
            var entry = Entry(sav, rules, species, version);
            var forms = Catalog.GetForms(entry);
            string name = Catalog.Names.specieslist[species];
            if (rules.Generation == 1) Check(label, forms.Count == 0, "no hay formas en Gen 1", name);
            foreach (var choice in forms)
            {
                formsTried++;
                string what = $"{name} {choice.Label}";
                Catalog.SetForm(entry, choice);
                var preview = entry.Preview();
                Check(label, preview.Form == choice.Form, "aplica la forma", $"{what}: {preview.Form}");
                Check(label, preview.ChecksumValid, "suma válida", what);
                Check(label, choice.Label.Trim().Length > 0, "tiene nombre de forma", what);
                Check(label, !FormInfo.IsBattleOnlyForm(species, choice.Form, (byte)rules.Generation) && !FormInfo.IsFusedForm(species, choice.Form, (byte)rules.Generation),
                    "no ofrece formas de combate ni fusiones", what);
                Check(label, preview.Stat_HPMax > 0 || !preview.PartyStatsPresent, "calcula estadísticas", what);
                var pi = sav.Personal[species, choice.Form];
                Check(label, preview.PersonalInfo.Type1 == pi.Type1 && preview.PersonalInfo.Type2 == pi.Type2, "usa los tipos de la forma", what);
                if (rules.HasStoredForm) Check(label, preview.Ability != 0 && preview.PersonalInfo.GetIndexOfAbility(preview.Ability) >= 0, "tiene una habilidad de la forma", $"{what}: {preview.Ability}");
                if (rules.HasStoredForm) Check(label, preview.Ability == entry.Edit.Ability, "el borrador sigue la habilidad de la forma", $"{what}: {preview.Ability} / {entry.Edit.Ability}");
                var moves = Catalog.GetMoves(entry);
                Check(label, moves.Count > 1, "lista movimientos de la forma", what);
                Check(label, Catalog.GetFinalForms(entry).Count > 0, "calcula evoluciones de la forma", what);
                var error = Catalog.Validate(entry);
                Check(label, error == null, "acepta la forma", $"{what}: {error}");
                // Leaves a move of this form behind, for the next one to drop if it can't use it.
                if (moves.LastOrDefault(m => m.Id != 0 && !entry.Edit.Moves.Contains((ushort)m.Id)) is { } extra) entry.Edit.Moves[1] = (ushort)extra.Id;
            }
            if (forms.Count != 0)
            {
                entry.Edit = Draft.From(entry.Original);
                Check(label, !entry.Dirty, "deshacer restaura la forma", name);
            }
        }
    }

    static void GameBoyUnown(SaveFile sav, GameRules rules, string label, GameVersion version)
    {
        var entry = Entry(sav, rules, (ushort)Species.Unown, version);
        var forms = Catalog.GetForms(entry);
        Check(label, forms.Count == 26, "Unown tiene 26 letras", forms.Count.ToString());
        Check(label, forms[0].Label == "A" && forms[25].Label == "Z", "nombra las letras", forms[0].Label);
        Catalog.SetForm(entry, forms[16]);
        Check(label, entry.Preview().Form == 16, "la letra pasa a los DV", entry.Preview().Form.ToString());
        Catalog.Optimize(entry);
        Check(label, entry.Preview().Form == 16, "optimizar conserva la letra", entry.Preview().Form.ToString());
        Catalog.SetShiny(entry, true);
        Catalog.SyncForm(entry);
        Check(label, entry.Preview().IsShiny && entry.Edit.Form is 8 or 21, "un Unown shiny solo es I o V", entry.Edit.Form.ToString());
        Catalog.SetForm(entry, forms[3]);
        Check(label, !entry.Edit.IsShiny && !entry.Preview().IsShiny && entry.Preview().Form == 3, "otra letra deja de ser shiny");
    }

    static void PidUnown(SaveFile sav, GameRules rules, string label, GameVersion version)
    {
        var entry = Entry(sav, rules, (ushort)Species.Unown, version);
        var forms = Catalog.GetForms(entry);
        Check(label, forms.Count == 28, "Unown tiene 28 letras", forms.Count.ToString());
        Catalog.SetForm(entry, forms[26]);
        entry.Edit.Nature = Nature.Modest;
        var preview = entry.Preview();
        Check(label, preview.Form == 26 && preview.Nature == Nature.Modest, "letra y naturaleza a la vez", $"{preview.Form} {preview.Nature}");
        Check(label, preview.PID == entry.Preview().PID, "la letra no cambia entre vistas previas");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Catalog.SetShiny(entry, true);
        preview = entry.Preview();
        Check(label, preview.Form == 26 && preview.IsShiny && preview.Nature == Nature.Modest, "letra, naturaleza y shiny a la vez");
        Check(label, clock.ElapsedMilliseconds < 1500, "la búsqueda no congela la ventana", clock.ElapsedMilliseconds + " ms");
    }

    static void HeldForm(SaveFile sav, GameRules rules, string label, GameVersion version, string work)
    {
        ushort arceus = (ushort)Species.Arceus;
        if (!rules.HasStoredForm || rules.Context == EntityContext.Gen8a || arceus > sav.MaxSpeciesID || !sav.Personal.IsPresentInGame(arceus, 0)) return;
        var forms = Catalog.GetForms(Entry(sav, rules, arceus, version));
        if (forms.Count == 0) return;
        var fire = forms.First(c => c.Label == "Fuego");
        ushort plate = rules.ItemForForm(arceus, fire.Form);
        Check(label, rules.ItemName(plate).StartsWith("Tabla"), "la forma pide su tabla", rules.ItemName(plate));
        sav.SetBoxSlotAtIndex(Make(sav, arceus, version), 3, EntityImportSettings.None);
        var path = Path.Combine(work, version + ".arceus.sav");
        File.WriteAllBytes(path, TestSaves.Finish(sav));
        var doc = SaveDocument.Open(path);
        var e = doc.Pokemon.First(p => p.Original.Species == arceus);
        string warning = doc.SetForm(e, fire);
        Check(label, e.Edit.HeldItem == 0 && warning.Contains(rules.ItemName(plate)), "avisa si falta la tabla", warning);
        e.Edit = Draft.From(e.Original);
        if (doc.Items.Any(i => i.Id == plate))
        {
            doc.SetItemCount(plate, 1);
            doc.SetForm(e, fire);
            Check(label, e.Edit.HeldItem == plate && doc.GetItemCount(plate) == 0, "equipa la tabla con la forma", e.Edit.HeldItem.ToString());
            doc.SetForm(e, forms.First(c => c.Form == 0));
            Check(label, e.Edit.HeldItem == 0 && doc.GetItemCount(plate) == 1, "devuelve la tabla al volver a Normal", e.Edit.HeldItem.ToString());
        }
    }

    // Forms written to a real file: the part a game actually reads back.
    static void FormsOnDisk(GameVersion version, string label, string work)
    {
        var sav = TestSaves.Start(version);
        var rules = new GameRules(sav);
        EveryForm(sav, rules, label, version);
        if (rules.Generation == 1) return;
        if (rules.Generation == 2) GameBoyUnown(sav, rules, label, version);
        if (rules.Generation == 3) PidUnown(sav, rules, label, version);
        var picks = Showcase.Concat(Enumerable.Range(1, sav.MaxSpeciesID).Select(i => (ushort)i)).Distinct()
            .Where(s => s <= sav.MaxSpeciesID && sav.Personal.IsPresentInGame(s, 0))
            .Where(s => Catalog.GetForms(Entry(sav, rules, s, version)).Count > 1).Take(6).ToList();
        Check(label, picks.Count != 0, "hay Pokémon con formas para probar");
        for (int i = 0; i < picks.Count; i++) sav.SetBoxSlotAtIndex(Entry(sav, rules, picks[i], version).Original, i, EntityImportSettings.None);
        sav.Money = 3000;
        if (rules.IsGameBoy) sav.SetPartySlotAtIndex(Make(sav, 25, version), 0, EntityImportSettings.None);   // an empty party doesn't read as a save
        var path = Path.Combine(work, version + ".forms.sav");
        File.WriteAllBytes(path, TestSaves.Finish(sav));
        var doc = SaveDocument.Open(path);
        var wanted = new Dictionary<int, FormChoice>();
        foreach (var e in doc.Pokemon.Where(p => !p.IsParty))
        {
            var forms = Catalog.GetForms(e);
            Check(label, forms.Count > 1, "lee las formas del archivo", e.SpeciesName);
            wanted[e.Index] = forms[^1];
            doc.SetForm(e, forms[^1]);
        }
        var edited = Path.Combine(work, version + ".forms.edited.sav");
        File.WriteAllBytes(edited, doc.BuildOutput());
        var reopened = SaveDocument.Open(edited);
        Check(label, reopened.Pokemon.Count == doc.Pokemon.Count, "conserva los Pokémon con formas");
        foreach (var e in reopened.Pokemon.Where(p => !p.IsParty))
        {
            var choice = wanted[e.Index];
            Check(label, e.Original.Form == choice.Form, "guarda la forma", $"{e.SpeciesName}: {e.Original.Form}");
            Check(label, e.Original.Species == picks[e.Index], "no cambia la especie", e.SpeciesName);
            Check(label, Catalog.GetForms(e).Any(c => c.Form == choice.Form), "la forma guardada sigue en la lista", e.SpeciesName);
        }
        var fresh = TestSaves.Start(version);
        HeldForm(fresh, new GameRules(fresh), label, version, work);
    }

    // What the game reads back: the ability itself, and in older games the PID bit or hidden flag behind it.
    static void CheckAbility(string label, PKM actual, AbilityChoice choice, Draft before, string what)
    {
        Check(label, actual.ChecksumValid, "suma válida con la habilidad", what);
        Check(label, actual.Ability == choice.Ability, "guarda la habilidad", $"{what}: {actual.Ability}, esperaba {choice.Ability}");
        Check(label, actual.AbilityNumber == 1 << choice.Slot, "guarda el espacio de la habilidad", $"{what}: {actual.AbilityNumber}");
        if (actual.Format <= 5 && choice.Slot < 2) Check(label, actual.PIDAbility == choice.Slot, "el PID sigue a la habilidad", $"{what}: {actual.PID:X8}");
        if (actual is PK5 pk5) Check(label, pk5.HiddenAbility == (choice.Slot == 2), "marca la habilidad oculta", what);
        Check(label, actual.IsShiny == before.IsShiny, "la habilidad no cambia el shiny", what);
        Check(label, actual.StatAlignment == before.Nature, "la habilidad no cambia la naturaleza", what);
    }

    // Every ability of a Pokémon that has them all, written to a real file and read back.
    static void Abilities(GameVersion version, string label, string work)
    {
        var sav = TestSaves.Start(version);
        var rules = new GameRules(sav);
        var sample = Entry(sav, rules, 25, version);
        if (!rules.HasAbility)
        {
            Check(label, Catalog.GetAbilities(sample).Count == 0, "este juego no tiene habilidades");
            Check(label, Catalog.Validate(sample) == null, "aun así acepta el Pokémon");
            return;
        }
        // Gen 3 and 4 have no hidden slot, so the fullest list is two abilities there.
        int wanted = rules.Generation <= 4 ? 2 : 3;
        var species = Enumerable.Range(1, sav.MaxSpeciesID).Select(i => (ushort)i).Where(s => sav.Personal.IsPresentInGame(s, 0))
            .Where(s => !(rules.HasFormInPid && s == (ushort)Species.Unown)).First(s => Catalog.GetAbilities(Entry(sav, rules, s, version)).Count == wanted);
        string name = Catalog.Names.specieslist[species];
        var offered = Catalog.GetAbilities(Entry(sav, rules, species, version));
        Check(label, offered[0].Slot == 0 && offered[0].Label.EndsWith("(principal)"), "ofrece la principal", offered[0].Label);
        Check(label, offered[1].Slot == 1 && offered[1].Label.EndsWith("(secundaria)"), "ofrece la secundaria", offered[1].Label);
        if (wanted == 3) Check(label, offered[2].Slot == 2 && offered[2].Label.EndsWith("(oculta)"), "ofrece la oculta", offered[2].Label);

        for (int i = 0; i < offered.Count; i++)
        {
            var pk = Entry(sav, rules, species, version).Original;
            pk.PID = i % 2 == 0 ? 0x12345678u : 0x12345679u;   // both values of the ability bit
            pk.RefreshAbility(0);
            pk.RefreshChecksum();
            sav.SetBoxSlotAtIndex(pk, i, EntityImportSettings.None);
        }
        var shinyPk = Entry(sav, rules, species, version).Original;
        sav.SetBoxSlotAtIndex(shinyPk, offered.Count, EntityImportSettings.None);
        var path = Path.Combine(work, version + ".abilities.sav");
        sav.Money = 3000;
        File.WriteAllBytes(path, TestSaves.Finish(sav));

        var doc = SaveDocument.Open(path);
        var boxed = doc.Pokemon.Where(p => !p.IsParty).OrderBy(p => p.Index).ToList();
        Check(label, boxed.Count == offered.Count + 1, "lee los Pokémon de prueba", boxed.Count.ToString());
        var before = boxed.Select(e => e.Baseline).ToList();
        for (int i = 0; i < offered.Count; i++)
        {
            Catalog.SetAbility(boxed[i], offered[i]);
            var preview = boxed[i].Preview();
            CheckAbility(label, preview, offered[i], before[i], $"{name} {offered[i].Label} (vista previa)");
            Check(label, preview.PID == boxed[i].Preview().PID, "el PID de la habilidad no cambia entre vistas previas", name);
        }
        // Shiny and ability at once, the same PID search as shiny and nature.
        var both = boxed[offered.Count];
        if (rules.HasShiny) Catalog.SetShiny(both, true);
        if (rules.HasNature) both.Edit.Nature = Nature.Modest;
        Catalog.SetAbility(both, offered[1]);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var mixed = both.Preview();
        Check(label, clock.ElapsedMilliseconds < 1500, "la búsqueda con habilidad no congela la ventana", clock.ElapsedMilliseconds + " ms");
        CheckAbility(label, mixed, offered[1], both.Edit, $"{name} con shiny y naturaleza");
        Check(label, mixed.Gender == both.Original.Gender, "la habilidad no cambia el género", name);
        var error = doc.ValidateEdits();
        Check(label, error == null, "acepta las habilidades", error ?? "");

        var edited = Path.Combine(work, version + ".abilities.edited.sav");
        File.WriteAllBytes(edited, doc.BuildOutput());
        var reopened = SaveDocument.Open(edited);
        var read = reopened.Pokemon.Where(p => !p.IsParty).OrderBy(p => p.Index).ToList();
        for (int i = 0; i < offered.Count; i++)
        {
            CheckAbility(label, read[i].Original, offered[i], before[i], $"{name} {offered[i].Label}");
            Check(label, !read[i].Dirty && Catalog.GetAbilities(read[i]).Count == offered.Count, "la habilidad guardada sigue en la lista", name);
            Check(label, read[i].Edit.Ability == offered[i].Ability && read[i].Edit.AbilitySlot == offered[i].Slot, "el borrador lee la habilidad guardada", name);
        }
        CheckAbility(label, read[offered.Count].Original, offered[1], both.Edit, $"{name} guardado con shiny y naturaleza");

        // Going back and forth leaves nothing pending, and the file as it was.
        var undone = SaveDocument.Open(edited);
        var entry = undone.Pokemon.First(p => !p.IsParty);
        var other = Catalog.GetAbilities(entry).First(c => c.Ability != entry.Baseline.Ability);
        Catalog.SetAbility(entry, other);
        Check(label, entry.Dirty, "cambiar la habilidad queda pendiente");
        Catalog.SetAbility(entry, Catalog.GetAbilities(entry).First(c => c.Ability == entry.Baseline.Ability));
        Check(label, !entry.Dirty && undone.BuildOutput().SequenceEqual(undone.OriginalBytes), "volver a la habilidad original no altera el archivo");

        // Pikachu's first two slots hold the same ability. In older games its PID can point at the second.
        if (rules.Personal.IsPresentInGame(25, 0) && rules.Generation >= 3)
        {
            var twin = Make(sav, 25, version);
            twin.PID = rules.Generation == 5 ? 0x12350000u : 0x12345679u;
            twin.RefreshAbility(1);
            twin.RefreshChecksum();
            var pikachu = new PokemonEntry(rules, false, 0, twin);
            var list = Catalog.GetAbilities(pikachu);
            Check(label, list.Count(c => c.Ability == twin.Ability) == 1, "no repite una habilidad en dos espacios", list.Count.ToString());
            if (list.Count > 1)
            {
                Catalog.SetAbility(pikachu, list.First(c => c.Ability != twin.Ability));
                Catalog.SetAbility(pikachu, list.First(c => c.Ability == twin.Ability));
                Check(label, !pikachu.Dirty, "volver a una habilidad repetida no deja cambios");
            }
        }

        // An ability from outside the list, as older Pokémon can carry: it stays until someone changes it.
        if (rules.Generation >= 4)
        {
            var odd = Entry(sav, rules, species, version).Original;
            int foreign = Enumerable.Range(1, odd.MaxAbilityID).First(a => odd.PersonalInfo.GetIndexOfAbility(a) < 0);
            odd.Ability = foreign;
            odd.RefreshChecksum();
            var kept = new PokemonEntry(rules, false, 0, odd);
            var list = Catalog.GetAbilities(kept);
            Check(label, list[^1].Ability == foreign && list[^1].Label.EndsWith("(actual)"), "conserva la habilidad fuera de la lista", list[^1].Label);
            kept.Edit.Friendship = rules.HasFriendship ? 200 : kept.Edit.Friendship;
            Check(label, kept.Preview().Ability == foreign, "otra edición no cambia esa habilidad", kept.Preview().Ability.ToString());
            Catalog.SetAbility(kept, list[0]);
            Check(label, kept.Preview().Ability == list[0].Ability, "se puede cambiar esa habilidad", kept.Preview().Ability.ToString());
            Catalog.SetAbility(kept, list[^1]);
            Check(label, kept.Preview().Ability == foreign && kept.Edit.Ability == kept.Baseline.Ability, "se puede recuperar esa habilidad");
        }
    }

    // The slot survives a form change, and comes back with the original form.
    static void AbilityWithForms(GameVersion version, string label)
    {
        var sav = TestSaves.Start(version);
        var rules = new GameRules(sav);
        if (!rules.HasStoredForm) return;
        foreach (ushort species in Enumerable.Range(1, sav.MaxSpeciesID).Select(i => (ushort)i).Where(s => sav.Personal.IsPresentInGame(s, 0)))
        {
            var e = Entry(sav, rules, species, version);
            var forms = Catalog.GetForms(e);
            var abilities = Catalog.GetAbilities(e);
            if (forms.Count < 2 || abilities.Count < 2) continue;
            var pick = abilities[^1];
            Catalog.SetAbility(e, pick);
            var start = forms.First(c => c.Form == e.Edit.Form && c.Argument == e.Edit.FormArgument);
            foreach (var form in forms)
            {
                Catalog.SetForm(e, form);
                var list = Catalog.GetAbilities(e);
                string what = $"{Catalog.Names.specieslist[species]} {form.Label}";
                Check(label, list.Any(c => c.Ability == e.Edit.Ability), "la forma ofrece la habilidad elegida", what);
                Check(label, e.Preview().Ability == e.Edit.Ability, "la forma aplica la habilidad elegida", what);
            }
            Catalog.SetForm(e, start);
            Check(label, e.Preview().Ability == e.Edit.Ability, "volver a la forma aplica su habilidad", Catalog.Names.specieslist[species]);
        }
    }

    // Changing language halfway must not change one byte of what gets saved.
    // Level, names and ball written to a real file and read back, plus a verdict and a Showdown round trip.
    static void Details(GameVersion version, string label, string work)
    {
        var sav = TestSaves.Start(version);
        var rules = new GameRules(sav);
        if (sav is not SAV7b) sav.SetPartySlotAtIndex(Make(sav, 133, version), 0, EntityImportSettings.None);   // Game Boy saves need a valid team list
        foreach (int slot in new[] { 0, 1 }) sav.SetBoxSlotAtIndex(Make(sav, 25, version), slot, EntityImportSettings.None);
        var path = Path.Combine(work, version + ".details.sav");
        sav.Money = 3000;
        File.WriteAllBytes(path, TestSaves.Finish(sav));
        var doc = SaveDocument.Open(path);
        var entry = doc.Pokemon.First(p => !p.IsParty);
        var other = doc.Pokemon.Last(p => !p.IsParty);
        Check(label, Catalog.CheckLegality(entry).Report.Length != 0, "da un veredicto de legalidad");
        // Japanese Blue has no Latin letters.
        bool kana = entry.Original.Japanese && rules.IsGameBoy;
        string shortName = kana ? "アイウ" : rules.IsGameBoy ? "ABC" : "Abc", trainerName = kana ? "アイ" : "ANA";
        entry.Edit.Level = 42; entry.Edit.Nickname = shortName; entry.Edit.Trainer = trainerName;
        var balls = Catalog.GetBalls(entry);
        Check(label, rules.HasBall == balls.Count > 1, "ofrece Poké Balls solo donde existen", balls.Count.ToString());
        if (rules.HasBall) entry.Edit.Ball = balls.Last().Id;
        Check(label, Catalog.Validate(entry) == null, "acepta nivel, apodo y entrenador", Catalog.Validate(entry) ?? "");
        var preview = entry.Preview();
        Check(label, preview.ChecksumValid && preview.CurrentLevel == 42 && preview.Stat_Level == 42, "cambia el nivel", preview.CurrentLevel.ToString());
        Check(label, preview.Stat_HPMax == preview.GetStats(preview.PersonalInfo)[0], "recalcula las estadísticas con el nivel");
        Check(label, preview.Nickname == shortName && preview.IsNicknamed, "cambia el apodo", preview.Nickname);
        Check(label, preview.OriginalTrainerName == trainerName, "cambia el entrenador", preview.OriginalTrainerName);
        var cleared = other.Edit.Nickname; other.Edit.Nickname = shortName; other.Edit.Nickname = "";
        Check(label, other.Preview().Nickname.Equals(cleared, StringComparison.OrdinalIgnoreCase) && !other.Preview().IsNicknamed, "vaciar el apodo vuelve al nombre de la especie", other.Preview().Nickname);
        other.Edit.Nickname = new string('A', other.Original.MaxStringLengthNickname + 1);
        Check(label, Catalog.Validate(other) != null, "rechaza un apodo demasiado largo");
        other.Edit.Nickname = cleared; other.Edit.Trainer = " ";
        Check(label, Catalog.Validate(other) != null, "rechaza un entrenador vacío");
        other.Edit.Trainer = other.Baseline.Trainer;
        var edited = Path.Combine(work, version + ".details.edited.sav");
        File.WriteAllBytes(edited, doc.BuildOutput());
        var read = SaveDocument.Open(edited);
        var saved = read.Pokemon.First(p => p.Index == entry.Index && !p.IsParty);
        Check(label, saved.Original.CurrentLevel == 42 && saved.Original.Nickname == shortName && saved.Original.OriginalTrainerName == trainerName, "el nivel y los nombres se guardan");
        if (rules.HasBall) Check(label, saved.Original.Ball == balls.Last().Id, "la Poké Ball se guarda", saved.Original.Ball.ToString());
        Check(label, !saved.Dirty, "lo guardado queda sin cambios pendientes");

        var fresh = read.Pokemon.First(p => p.Index == other.Index && !p.IsParty);
        var message = read.ImportShowdown(fresh, Catalog.ShowdownText(saved));
        Check(label, message.Length != 0 && fresh.Edit.Level == 42, "pega un set de Showdown", message);
        Check(label, fresh.Edit.Moves.SequenceEqual(saved.Edit.Moves) && fresh.Edit.Nickname == saved.Edit.Nickname, "pegar conserva movimientos y apodo", fresh.Edit.Nickname);
        Check(label, Catalog.Validate(fresh) == null, "el set pegado es válido", Catalog.Validate(fresh) ?? "");
        if (rules.HasNature) Check(label, fresh.Edit.Nature == saved.Edit.Nature, "pegar conserva la naturaleza");
        if (!rules.IsGameBoy) Check(label, fresh.Edit.IVs.SequenceEqual(saved.Edit.IVs), "pegar conserva los IV", string.Join(' ', fresh.Edit.IVs));
        bool refused = false;
        try { read.ImportShowdown(fresh, Catalog.Names.specieslist[1] == "" ? "" : "Bulbasaur\nLevel: 5"); }
        catch (InvalidOperationException) { refused = true; }
        Check(label, refused, "rechaza el set de otra especie");
    }

    static void Refused(string label, Action action, string what)
    {
        try { action(); }
        catch (InvalidOperationException ex) { Check(label, ex.Message.Length != 0, "explica por qué rechaza", what); return; }
        Check(label, false, "rechaza", what);
    }

    // The level tells them apart, since a game without the species asked for repeats another.
    static PKM Tagged(SaveFile sav, ushort species, GameVersion version, int level, int item = 0)
    {
        var pk = Make(sav, species, version);
        pk.CurrentLevel = (byte)level;
        pk.HeldItem = item;
        pk.SetStats(pk.GetStats(pk.PersonalInfo));
        pk.RefreshChecksum();
        return pk;
    }

    static readonly ushort[] BoxSpecies = [25, 133, 1, 4, 7, 150, 151, 143];

    static TeamIndexes8? BattleTeams(SaveFile sav) => sav switch {
        SAV9ZA za => za.TeamIndexes, SAV9SV sv => sv.TeamIndexes, SAV8SWSH sw => sw.TeamIndexes, _ => null,
    };
    const int NoPointer = 1001;

    // Three on the team, four in the first box, one in the second. Let's Go has no team but pointers into its list,
    // which has no gaps: its second place is the team's and the starter's, and its fourth place the team's.
    static string BoxSave(GameVersion version, string work, string name, Action<SaveFile, GameRules>? extra = null)
    {
        var sav = TestSaves.Start(version);
        var rules = new GameRules(sav);
        if (sav is not SAV7b)
            for (int i = 0; i < 3; i++) sav.SetPartySlotAtIndex(Tagged(sav, BoxSpecies[i], version, 10 + i), i, EntityImportSettings.None);
        for (int i = 0; i < 4; i++) sav.SetBoxSlotAtIndex(Tagged(sav, BoxSpecies[3 + i], version, 20 + i), i, EntityImportSettings.None);
        sav.SetBoxSlotAtIndex(Tagged(sav, BoxSpecies[7], version, 30), sav is SAV7b ? 4 : sav.BoxSlotCount, EntityImportSettings.None);
        if (sav is SAV7b letsGo)
        {
            var header = letsGo.Storage.Data;
            int[] pointers = [1, 3, NoPointer, NoPointer, NoPointer, NoPointer, 1];
            for (int i = 0; i < pointers.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header[(i * 2)..], (ushort)pointers[i]);
            letsGo.Storage.Count = 5;
        }
        // A blank save has zeros there, which read as every battle team sitting in the first place.
        if (BattleTeams(sav) is { } teams) { teams.ClearBattleTeams(); teams.SaveBattleTeams(); }
        extra?.Invoke(sav, rules);
        sav.Money = 3000;
        var path = Path.Combine(work, $"{version}.{name}.sav");
        File.WriteAllBytes(path, TestSaves.Finish(sav));
        return path;
    }

    static PokemonEntry Boxed(SaveDocument doc, int level) => doc.Pokemon.Single(p => p.Edit.Level == level);

    // A real write and reopen: everyone is where the document says, intact, and nobody else appeared.
    static SaveDocument Reopened(string label, SaveDocument doc, string path, string what)
    {
        var error = doc.ValidateEdits();
        Check(label, error == null, what + ": acepta los cambios", error ?? "");
        var edited = path + "." + what.Replace(' ', '-') + ".out";
        File.WriteAllBytes(edited, doc.BuildOutput());
        var read = SaveDocument.Open(edited);
        Check(label, read.Pokemon.Count == doc.Pokemon.Count, what + ": conserva cuántos hay", $"{read.Pokemon.Count} de {doc.Pokemon.Count}");
        foreach (var e in doc.Pokemon)
        {
            var saved = read.At(e.Place);
            Check(label, saved != null && saved.Original.Species == e.Original.Species && saved.Edit.Level == e.Edit.Level && saved.Original.PID == e.Preview().PID, what + ": cada uno en su lugar", e.Location);
            if (saved == null || doc.Rules.IsGameBoy) continue;
            Check(label, saved.Original.ChecksumValid && (e.Dirty || saved.Original.Data[..e.Original.SIZE_STORED].SequenceEqual(e.Original.Data[..e.Original.SIZE_STORED])), what + ": llega intacto", e.Location);
        }
        Check(label, !read.HasChanges, what + ": no queda nada pendiente");
        return read;
    }

    static void NoGaps(string label, SaveDocument doc, string what)
    {
        if (doc.Save is SAV7b) Check(label, doc.Pokemon.Select(p => p.Index).Order().SequenceEqual(Enumerable.Range(0, doc.Pokemon.Count)), what + ": la lista no deja huecos");
        if (!doc.Rules.IsGameBoy) return;
        foreach (var box in doc.Pokemon.Where(p => !p.IsParty).GroupBy(doc.BoxOf))
            Check(label, box.Select(p => p.Index - box.Key * doc.Save.BoxSlotCount).Order().SequenceEqual(Enumerable.Range(0, box.Count())), what + ": la caja no deja huecos");
    }

    static void TeamWithoutGaps(string label, SaveDocument doc, string what)
    {
        var team = doc.Pokemon.Where(p => p.IsParty).Select(p => p.Index).Order();
        Check(label, team.SequenceEqual(Enumerable.Range(0, team.Count())), what + ": el equipo no deja huecos");
    }

    static void MovingAround(GameVersion version, string label, string work)
    {
        var path = BoxSave(version, work, "boxes");
        var doc = SaveDocument.Open(path);
        var rules = doc.Rules;
        bool team = doc.Save.HasParty;
        var first = Boxed(doc, 20); var third = Boxed(doc, 22); var other = Boxed(doc, 30);
        Check(label, doc.Move(first, first.Place) == "" && !doc.HasChanges, "mover a su propio lugar no hace nada");
        doc.Move(first, new(false, 7));
        Check(label, doc.HasChanges && doc.BoxChangeCount != 0 && first.Moved && doc.DirtyCount == 0, "mover queda pendiente", doc.BoxChangeCount.ToString());
        Check(label, !rules.IsGameBoy && doc.Save is not SAV7b ? first.Index == 7 : first.Index < 7, "llega a su lugar, o al final de la lista", first.Index.ToString());
        NoGaps(label, doc, "mover");
        doc.Move(other, third.Place);
        Check(label, other.Place != third.Place, "intercambiar deja a cada uno en un lugar");
        NoGaps(label, doc, "intercambiar");
        if (team)
        {
            var leader = Boxed(doc, 10); var second = Boxed(doc, 11); var last = Boxed(doc, 12);
            doc.Move(leader, last.Place);
            Check(label, leader.Index == 2 && last.Index == 0, "cambia el orden del equipo");
            doc.Move(first, leader.Place);
            Check(label, first.IsParty && first.Index == 2 && !leader.IsParty, "un Pokémon de caja cambia de lugar con uno del equipo");
            doc.Move(second, new(false, 9));
            Check(label, !second.IsParty && doc.TeamSlots().Count(p => p != null) == 2, "un Pokémon del equipo pasa a la caja");
            TeamWithoutGaps(label, doc, "sacar del equipo");
            doc.Move(second, new(true, 5));
            Check(label, second.IsParty && second.Index == 2, "un lugar vacío del equipo se llena sin huecos", second.Index.ToString());
            TeamWithoutGaps(label, doc, "volver al equipo");
        }
        var read = Reopened(label, doc, path, "mover");
        NoGaps(label, read, "mover y guardar");
        if (read.Save is SAV7b written)
        {
            int partner = Boxed(doc, 21).Index, teammate = Boxed(doc, 23).Index;
            Check(label, written.GetBoxSlotFlags(partner).HasFlag(StorageSlotSource.Starter) && written.GetBoxSlotFlags(partner).HasFlag(StorageSlotSource.Party1), "los punteros siguen al inicial", partner.ToString());
            Check(label, written.GetBoxSlotFlags(teammate).HasFlag(StorageSlotSource.Party2), "los punteros siguen al equipo", teammate.ToString());
            Check(label, written.Storage.Count == read.Pokemon.Count, "la lista cuenta a todos", written.Storage.Count.ToString());
        }
        doc.ResetLayout();
        Check(label, !doc.HasChanges && !doc.CanUndoLayout, "deshacer todo no deja nada pendiente");
        Check(label, doc.BuildOutput().SequenceEqual(doc.OriginalBytes), "deshacer todo deja el archivo como estaba");
        // Moved again from the saved file, nothing depends on the first file's layout.
        var again = Boxed(read, 22);
        read.Move(again, read.FreePlace(0)!.Value);
        Reopened(label, read, path, "mover otra vez");
        Check(label, read.Pokemon.Count == doc.Pokemon.Count, "mover no cambia cuántos hay");
    }

    static void DeletingAndCloning(GameVersion version, string label, string work)
    {
        var path = BoxSave(version, work, "clones");
        var doc = SaveDocument.Open(path);
        bool team = doc.Save.HasParty;
        var gone = Boxed(doc, 22); var source = Boxed(doc, 20);
        var freed = gone.Place;
        var deleted = doc.Delete(gone);
        Check(label, deleted.Contains(gone.DisplayName) && doc.Pokemon.Count == 7 - (team ? 0 : 3) && doc.HasChanges, "borrar queda pendiente", doc.Pokemon.Count.ToString());
        NoGaps(label, doc, "borrar");
        Check(label, doc.At(freed) == null || doc.Rules.IsGameBoy || doc.Save is SAV7b, "un lugar borrado queda libre, o la lista lo cierra");
        doc.UndoLayout();
        Check(label, !doc.HasChanges && doc.BuildOutput().SequenceEqual(doc.OriginalBytes), "deshacer el borrado deja el archivo como estaba");
        doc.Delete(gone);
        var place = doc.FreePlace(0)!.Value;
        var clone = doc.Clone(source, place).Entry;
        Check(label, clone.IsNew && clone.Moved && clone.Edit.Level == 20 && !ReferenceEquals(clone.Original, source.Original), "clonar crea otro Pokémon");
        Check(label, doc.At(clone.Place) == clone, "el clon ocupa un lugar");
        NoGaps(label, doc, "clonar");
        Refused(label, () => doc.Clone(source, source.Place), "clonar sobre un lugar ocupado");
        var edited = Boxed(doc, 30);
        if (doc.Rules.HasFriendship) edited.Edit.Friendship = 200;
        var editedClone = doc.Clone(edited, doc.FreePlace(0)!.Value).Entry;
        if (doc.Rules.HasFriendship) Check(label, editedClone.Original.CurrentFriendship == 200, "el clon lleva lo que estaba sin guardar");
        if (team)
        {
            var member = doc.Clone(source, new(true, 5)).Entry;
            Check(label, member.IsParty && member.Index == 3, "clonar al equipo no deja huecos", member.Index.ToString());
            doc.Delete(Boxed(doc, 11) );
            TeamWithoutGaps(label, doc, "borrar del equipo");
        }
        Reopened(label, doc, path, "borrar y clonar");

        if (team)
        {
            var small = SaveDocument.Open(path);
            small.Delete(Boxed(small, 10)); small.Delete(Boxed(small, 11));
            Refused(label, () => small.Delete(Boxed(small, 12)), "borrar al último del equipo");
            Refused(label, () => small.Move(Boxed(small, 12), new(false, 7)), "dejar el equipo vacío");
            Check(label, small.TeamSlots()[0] == Boxed(small, 12), "el rechazo deja el equipo como estaba");
            Reopened(label, small, path, "equipo de uno");
        }
        doc = SaveDocument.Open(path);
        Check(label, doc.FreePlace(doc.BoxCount - 1) is { Index: var wrap } && wrap >= (doc.BoxCount - 1) * doc.Save.BoxSlotCount, "busca lugar desde la caja pedida", doc.FreePlace(doc.BoxCount - 1)?.ToString() ?? "ninguno");
    }

    static void Sorting(GameVersion version, string label, string work)
    {
        var path = BoxSave(version, work, "sorted");
        var doc = SaveDocument.Open(path);
        doc.SortBox(0, BoxOrder.Level);
        var levels = doc.BoxSlots(0).OfType<PokemonEntry>().Where(p => doc.Save is not SAV7b || p.Edit.Level != 21).Select(p => p.Edit.Level).ToList();
        Check(label, levels.SequenceEqual(levels.OrderDescending()) && doc.HasChanges, "ordena por nivel", string.Join(' ', levels));
        if (doc.Save is SAV7b) Check(label, Boxed(doc, 21).Index == 1, "el inicial no se mueve de la lista");
        NoGaps(label, doc, "ordenar");
        var read = Reopened(label, doc, path, "ordenar");
        read.SortBox(0, BoxOrder.Pokedex);
        var species = read.BoxSlots(0).OfType<PokemonEntry>().Where(p => read.Save is not SAV7b || p.Edit.Level != 21).Select(p => (int)p.Original.Species).ToList();
        Check(label, species.SequenceEqual(species.Order()), "ordena por Pokédex", string.Join(' ', species));
        read = Reopened(label, read, path, "ordenar por pokedex");
        read.SortBox(0, BoxOrder.Name);
        var names = read.BoxSlots(0).OfType<PokemonEntry>().Where(p => read.Save is not SAV7b || p.Edit.Level != 21).Select(p => p.DisplayName).ToList();
        Check(label, names.SequenceEqual(names.Order(StringComparer.CurrentCultureIgnoreCase)), "ordena por nombre", string.Join(' ', names));
        Reopened(label, read, path, "ordenar por nombre");
        var same = SaveDocument.Open(path);
        same.SortBox(same.BoxCount - 1, BoxOrder.Level);
        Check(label, !same.HasChanges && !same.CanUndoLayout, "ordenar una caja vacía no cambia nada");
    }

    static void HeldItems(GameVersion version, string label, string work)
    {
        var path = BoxSave(version, work, "items");
        var doc = SaveDocument.Open(path);
        var rules = doc.Rules;
        var item = rules.HasHeldItems ? doc.Items.FirstOrDefault(i => rules.CanHold(i.Id) && !doc.IsUniqueHeldItem(i.Id) && !rules.IsMail(i.Id) && i.MaxCount >= 3) : null;
        if (item == null) return;
        var holder = Boxed(doc, 20);
        doc.SetItemCount(item.Id, 3);
        doc.EquipItem(holder, item.Id);
        Check(label, doc.GetItemCount(item.Id) == 2, "equipar toma una unidad", doc.GetItemCount(item.Id).ToString());
        var clone = doc.Clone(holder, doc.FreePlace(0)!.Value).Entry;
        Check(label, clone.Edit.HeldItem == item.Id && doc.GetItemCount(item.Id) == 1, "el clon toma su propia unidad", doc.GetItemCount(item.Id).ToString());
        doc.UndoLayout();
        Check(label, doc.GetItemCount(item.Id) == 2, "deshacer el clon devuelve su unidad", doc.GetItemCount(item.Id).ToString());
        clone = doc.Clone(holder, doc.FreePlace(0)!.Value).Entry;
        var saved = Reopened(label, doc, path, "clonar con objeto");
        Check(label, saved.At(clone.Place)!.Original.HeldItem == item.Id && saved.At(holder.Place)!.Original.HeldItem == item.Id, "los dos llevan su objeto");
        Check(label, saved.GetItemCount(item.Id) == 1, "la bolsa cuenta el objeto del clon", saved.GetItemCount(item.Id).ToString());

        var returning = saved.At(holder.Place)!;
        string message = saved.Delete(returning);
        Check(label, message.Contains(rules.ItemName(item.Id)) && saved.GetItemCount(item.Id) == 2, "borrar devuelve el objeto a la bolsa", message);
        saved.UndoLayout();
        Check(label, saved.GetItemCount(item.Id) == 1 && !saved.HasChanges, "deshacer el borrado vuelve a equiparlo", saved.GetItemCount(item.Id).ToString());
        saved.Delete(returning);
        var after = Reopened(label, saved, path, "borrar con objeto");
        Check(label, after.GetItemCount(item.Id) == 2, "el objeto del borrado se guarda en la bolsa", after.GetItemCount(item.Id).ToString());

        var again = after.Pokemon.First(p => p.Edit.HeldItem == item.Id);
        after.SetItemCount(item.Id, 0);
        var plain = after.Clone(again, after.FreePlace(0)!.Value);
        Check(label, plain.Entry.Edit.HeldItem == 0 && plain.Message.Contains(rules.ItemName(item.Id)), "sin unidades el clon llega sin objeto y lo dice", plain.Message);
        after.SetItemCount(item.Id, item.MaxCount);
        Refused(label, () => after.Delete(again), "borrar si la bolsa no tiene espacio para su objeto");
        Check(label, after.Pokemon.Contains(again), "el rechazo conserva al Pokémon");
        after.SetItemCount(item.Id, 0);
        after.Delete(again);
        after.SetItemCount(item.Id, 0);
        // The unit that came back has gone, so the Pokémon can't take it again.
        Refused(label, () => after.UndoLayout(), "deshacer si su objeto ya no está");
        Check(label, !after.Pokemon.Contains(again), "el rechazo no recupera al Pokémon sin su objeto");
    }

    // Each format, once. Whatever comes in is converted or clearly refused, never half done.
    static readonly Dictionary<string, string> Files = [];
    static readonly GameVersion[] FileGames = [GameVersion.RD, GameVersion.C, GameVersion.E, GameVersion.Pt, GameVersion.B2, GameVersion.OR, GameVersion.US, GameVersion.GP,
        GameVersion.SW, GameVersion.BD, GameVersion.PLA, GameVersion.SL, GameVersion.ZA];

    static string SourceFile(GameVersion game, string work)
    {
        if (Files.TryGetValue(game + work, out var path)) return path;
        var sav = TestSaves.Start(game);
        var entry = Entry(sav, new GameRules(sav), 25, game);
        path = Path.Combine(work, $"{game}.source.{entry.Original.Extension}");
        File.WriteAllBytes(path, SaveDocument.PokemonFile(entry));
        return Files[game + work] = path;
    }

    static void PokemonFiles(GameVersion version, string label, string work)
    {
        var path = BoxSave(version, work, "files");
        var doc = SaveDocument.Open(path);
        var rules = doc.Rules;
        var pick = Boxed(doc, 23);
        pick.Edit.Friendship = rules.HasFriendship ? 123 : pick.Edit.Friendship;
        var file = Path.Combine(work, SaveDocument.PokemonFileName(pick));
        Check(label, Path.GetExtension(file) == "." + doc.Save.PKMType.Name.ToLowerInvariant(), "exporta con la extensión del juego", file);
        File.WriteAllBytes(file, SaveDocument.PokemonFile(pick));
        var place = doc.FreePlace(0)!.Value;
        var came = doc.Import(file, place);
        Check(label, came.Message.Contains(came.Entry.DisplayName) && came.Entry.IsNew && came.Entry.Original.ChecksumValid, "importa su propio archivo", came.Message);
        Check(label, came.Entry.Edit.Level == 23 && came.Entry.Original.PID == pick.Preview().PID && came.Entry.Original.Species == pick.Original.Species, "el archivo conserva al Pokémon");
        if (rules.HasFriendship) Check(label, came.Entry.Original.CurrentFriendship == 123, "el archivo lleva lo que estaba sin guardar", came.Entry.Original.CurrentFriendship.ToString());
        NoGaps(label, doc, "importar");
        Refused(label, () => doc.Import(file, pick.Place), "importar sobre un lugar ocupado");
        var junk = Path.Combine(work, "basura.pk3"); File.WriteAllBytes(junk, new byte[17]);
        Refused(label, () => doc.Import(junk, doc.FreePlace(0)!.Value), "importar un archivo que no es un Pokémon");
        Refused(label, () => doc.Import(Path.Combine(work, "no-existe.pk3"), doc.FreePlace(0)!.Value), "importar un archivo que no existe");
        var zeros = Path.Combine(work, "ceros.pk7"); File.WriteAllBytes(zeros, new byte[232]);
        Refused(label, () => doc.Import(zeros, doc.FreePlace(0)!.Value), "importar un archivo vacío");
        if (doc.Save.HasParty)
        {
            var member = doc.Import(file, new(true, 4)).Entry;
            Check(label, member.Index == 3 && member.Preview().PartyStatsPresent, "importar al equipo no deja huecos", member.Index.ToString());
        }
        Reopened(label, doc, path, "importar");

        // Every format into this game.
        var mixed = SaveDocument.Open(path);
        int imported = 0;
        foreach (var game in FileGames.Append(version))
        {
            var source = SourceFile(game, work);
            try
            {
                var entry = mixed.Import(source, mixed.FreePlace(0)!.Value).Entry;
                imported++;
                Check(label, entry.Original.GetType() == mixed.Save.PKMType && entry.Original.ChecksumValid && entry.Original.Species != 0, "convierte al formato del juego", source);
                Check(label, entry.Original.PartyStatsPresent, "el convertido tiene sus estadísticas", source);
            }
            catch (InvalidOperationException ex) { Check(label, ex.Message.Length != 0, "explica por qué no convierte", source); }
        }
        Check(label, imported != 0, "convierte al menos su propio formato");
        Reopened(label, mixed, path, "importar de otros juegos");
        var older = SourceFile(GameVersion.E, work);
        if (rules.Generation is >= 4 and <= 7 && rules.Context != EntityContext.Gen7b)
            Check(label, SaveDocument.Open(path).Import(older, new(false, 8)).Entry.Original.Generation == 3, "sube un Pokémon de un juego anterior");
        if (rules.Generation == 3) Refused(label, () => SaveDocument.Open(path).Import(SourceFile(GameVersion.ZA, work), new(false, 8)), "no baja un Pokémon de Z-A a Gen 3");
        if (rules.IsGameBoy)
        {
            // Azul only ever came out in Japan, so what doesn't belong there is the rest of the world's.
            bool foreign = !doc.Save.BlankPKM.Japanese;
            var stranger = rules.Generation == 1 ? (PKM)new PK1(foreign) { Species = 25, CurrentLevel = 5 } : new PK2(foreign) { Species = 25, CurrentLevel = 5 };
            var strangerFile = Path.Combine(work, $"otro-idioma.{version}.{stranger.Extension}");
            var data = new byte[stranger.SIZE_PARTY]; stranger.WriteDecryptedDataParty(data); File.WriteAllBytes(strangerFile, data);
            Refused(label, () => SaveDocument.Open(path).Import(strangerFile, new(false, 8)), "no mezcla partidas japonesas de Game Boy");
        }
    }

    static int boxesClosedTried;

    static int BoxesOpen(SaveFile sav)
    {
        try { return sav.BoxesUnlocked; }
        catch (Exception e) when (e is ArgumentException or InvalidCastException) { return -1; }
    }

    static void StayingPut(GameVersion version, string label, string work)
    {
        // A team kept in the boxes: its Pokémon can't be moved or lost from under it.
        var path = BoxSave(version, work, "pinned", (sav, _) => {
            if (BattleTeams(sav) is { } teams) { teams.TeamSlots[0] = 1; teams.SaveBattleTeams(); }
        });
        var doc = SaveDocument.Open(path);
        if (doc.Save is SAV9ZA or SAV9SV or SAV8SWSH or SAV7b)
        {
            var kept = Boxed(doc, 21);
            Check(label, doc.Save.IsBoxSlotOverwriteProtected(kept.Index), "el juego protege ese lugar");
            Refused(label, () => doc.Move(kept, new(false, 9)), "mover a un Pokémon protegido");
            Refused(label, () => doc.Delete(kept), "borrar a un Pokémon protegido");
            Refused(label, () => doc.Move(Boxed(doc, 20), kept.Place), "cambiar de lugar con uno protegido");
            doc.SortBox(0, BoxOrder.Level);
            Check(label, kept.Index == 1, "ordenar no lo mueve", kept.Index.ToString());
            var clone = doc.Clone(kept, doc.FreePlace(0)!.Value).Entry;
            Check(label, clone.IsNew, "se puede clonar a un Pokémon protegido");
            Reopened(label, doc, path, "protegido");
        }
        var narrow = BoxSave(version, work, "closed", (sav, _) => {
            try { sav.BoxesUnlocked = 2; } catch (Exception e) when (e is ArgumentException or InvalidCastException or KeyNotFoundException) { }
        });
        var closed = SaveDocument.Open(narrow);
        if (BoxesOpen(closed.Save) == 2)
        {
            boxesClosedTried++;
            var place = new SlotPlace(false, closed.Save.BoxSlotCount * 2 + 1);
            Refused(label, () => closed.Move(Boxed(closed, 20), place), "mover a una caja que el juego no ha abierto");
            Refused(label, () => closed.Clone(Boxed(closed, 20), place), "clonar a una caja que el juego no ha abierto");
            Check(label, closed.FreePlace(1)!.Value.Index < closed.Save.BoxSlotCount * 2, "busca lugar solo en las cajas abiertas");
            Check(label, closed.FreePlace(30 % closed.BoxCount)!.Value.Index < closed.Save.BoxSlotCount * 2, "desde una caja cerrada vuelve a las abiertas");
        }

        if (doc.Save is SAV7b)
        {
            var fresh = SaveDocument.Open(path);
            Refused(label, () => fresh.Delete(Boxed(fresh, 23)), "borrar a uno del equipo de Let's Go");
            Refused(label, () => fresh.Move(Boxed(fresh, 20), new(true, 0)), "Let's Go no tiene lugares de equipo");
        }

        if (doc.Rules.Generation >= 3 && doc.Save.HasParty)
        {
            var eggs = BoxSave(version, work, "eggs", (sav, _) => {
                foreach (int i in new[] { 1, 2 })
                {
                    var egg = sav.GetPartySlotAtIndex(i);
                    egg.IsEgg = true; egg.RefreshChecksum();
                    sav.SetPartySlotAtIndex(egg, i, EntityImportSettings.None);
                }
            });
            var hatched = SaveDocument.Open(eggs);
            Check(label, hatched.Pokemon.Count(p => p.Original.IsEgg) == 2, "lee los huevos del equipo");
            Refused(label, () => hatched.Delete(Boxed(hatched, 10)), "dejar un equipo solo de huevos");
            Refused(label, () => hatched.Move(Boxed(hatched, 10), new(false, 7)), "sacar al único que no es un huevo");
            hatched.Move(Boxed(hatched, 20), Boxed(hatched, 11).Place);
            hatched.Delete(Boxed(hatched, 10));
            Reopened(label, hatched, eggs, "huevos");
        }

        if (doc.Rules.Generation is >= 2 and <= 5)
        {
            int mail = Enumerable.Range(1, 400).First(doc.Rules.IsMail);
            var letters = BoxSave(version, work, "mail", (sav, _) => sav.SetPartySlotAtIndex(Tagged(sav, 25, version, 12, mail), 2, EntityImportSettings.None));
            var writing = SaveDocument.Open(letters);
            var postman = Boxed(writing, 12);
            Check(label, postman.Edit.HeldItem == mail, "lee la carta", postman.Edit.HeldItem.ToString());
            Refused(label, () => writing.Move(postman, new(false, 7)), "mover a quien lleva una carta");
            Refused(label, () => writing.Delete(postman), "borrar a quien lleva una carta");
            Refused(label, () => writing.Clone(postman, new(false, 7)), "clonar a quien lleva una carta");
        }
    }

    // A write that came out wrong has to be caught: one that changes a place nobody asked for, loses a move, or touches other data.
    static void CatchingMistakes(GameVersion version, string label, string work)
    {
        var path = BoxSave(version, work, "tamper");
        var doc = SaveDocument.Open(path);
        var moved = Boxed(doc, 20);
        var destination = doc.FreePlace(0)!.Value;
        doc.Move(moved, destination);
        var bytes = doc.BuildOutput();
        SaveFile Tampered() => SaveUtil.GetSaveFile(bytes.ToArray(), path)!;
        var clean = Tampered();
        doc.VerifyStorage(clean);
        Check(label, true, "lo escrito pasa la verificación");
        var stranger = Tagged(clean, 25, version, 44);
        var second = Tampered(); second.SetBoxSlotAtIndex(stranger, doc.BoxCount * doc.Save.BoxSlotCount - 1, EntityImportSettings.None);
        Refused(label, () => doc.VerifyStorage(second), "detecta a alguien en un lugar vacío ajeno");
        var third = Tampered(); third.SetBoxSlotAtIndex(stranger, Boxed(doc, 22).Index, EntityImportSettings.None);
        Refused(label, () => doc.VerifyStorage(third), "detecta otro Pokémon en un lugar que no cambió");
        var fourth = Tampered(); fourth.SetBoxSlotAtIndex(fourth.BlankPKM, moved.Index, EntityImportSettings.None);
        Refused(label, () => doc.VerifyStorage(fourth), "detecta un Pokémon perdido por el camino");
        if (doc.Save.HasParty)
        {
            var fifth = Tampered(); fifth.SetPartySlotAtIndex(stranger, 2, EntityImportSettings.None);
            Refused(label, () => doc.VerifyStorage(fifth), "detecta un cambio en el equipo");
            var sixth = Tampered(); sixth.SetPartySlotAtIndex(stranger, 3, EntityImportSettings.None);
            Refused(label, () => doc.VerifyStorage(sixth), "detecta a alguien de más en el equipo");
        }
        if (doc.Save is SAV7b)
        {
            var seventh = (SAV7b)Tampered(); seventh.Storage.Count = 3;
            Refused(label, () => doc.VerifyStorage(seventh), "detecta una lista mal contada");
        }
        if (Tampered() is SAV9ZA za)
        {
            doc.VerifyBlocks(za);
            za.TeamIndexes.SetIsTeamLocked(0, true);
            Refused(label, () => doc.VerifyBlocks(za), "detecta un cambio en otro bloque de la partida");
            var footer = (SAV9ZA)Tampered(); footer.BoxInfo.Data[footer.SIZE_BOXSLOT * 3 + footer.SIZE_PARTY] ^= 1;
            Refused(label, () => doc.VerifyStorage(footer), "detecta un cambio junto a un Pokémon que no se movió");
            var mega = SaveDocument.Open(path);
            var holder = Boxed(mega, 22);
            mega.SetItemCount(658, 1); mega.EquipItem(holder, 658);
            Refused(label, () => mega.Clone(holder, mega.FreePlace(0)!.Value), "no clona a quien lleva una megapiedra");
            mega.SetItemCount(534, 1); mega.EquipItem(Boxed(mega, 23), 534);
            Refused(label, () => mega.Clone(Boxed(mega, 23), mega.FreePlace(0)!.Value), "no clona a quien lleva un orbe");
            var kept = Reopened(label, mega, path, "megapiedra");
            Check(label, kept.ZaSave!.Items.GetItem(658).IsHeld, "la megapiedra queda asignada");
            kept.Delete(Boxed(kept, 22));
            var freed = Reopened(label, kept, path, "borrar megapiedra");
            Check(label, !freed.ZaSave!.Items.GetItem(658).IsHeld && freed.GetItemCount(658) == 1 && !freed.IsItemHeld(658), "borrar a quien la lleva la deja libre en la bolsa");
            freed.EquipItem(Boxed(freed, 20), 658);
            Reopened(label, freed, path, "reasignar megapiedra");
        }

        // The whole writer, backup and change log included, on a throwaway copy.
        var copy = Path.Combine(work, version + ".boxes.copy.sav");
        File.Copy(path, copy, true);
        var writer = SaveDocument.Open(copy);
        writer.Move(Boxed(writer, 20), writer.FreePlace(0)!.Value);
        writer.Delete(Boxed(writer, 22));
        writer.Clone(Boxed(writer, 23), writer.FreePlace(0)!.Value);
        var backup = writer.Write(copy, Path.Combine(work, "Backups"), _ => null);
        var log = File.ReadAllText(Path.Combine(backup, "cambios.json"));
        Check(label, log.Contains("\"moved\"") && log.Contains("\"added\"") && log.Contains("\"removed\""), "el registro del respaldo cuenta los cambios de cajas");
        Check(label, File.ReadAllBytes(Path.Combine(backup, Path.GetFileName(copy) + ".original")).SequenceEqual(File.ReadAllBytes(path)), "el respaldo conserva el archivo anterior");
        Check(label, SaveDocument.Open(copy).Pokemon.Count == writer.Pokemon.Count, "el guardado real aplica los cambios de cajas");
    }

    static void BoxTools(GameVersion version, string label, string work)
    {
        MovingAround(version, label, work);
        DeletingAndCloning(version, label, work);
        Sorting(version, label, work);
        HeldItems(version, label, work);
        PokemonFiles(version, label, work);
        StayingPut(version, label, work);
        CatchingMistakes(version, label, work);
    }

    static void LanguageSwitch(GameVersion version, string label, string work)
    {
        var doc = SaveDocument.Open(Path.Combine(work, version + ".sav"));
        var first = doc.Pokemon[0];
        first.Edit.IVs[1] = doc.Rules.MaxIV;
        if (doc.Rules.HasNature) first.Edit.Nature = Nature.Modest;
        if (doc.Items.FirstOrDefault(i => i.MaxCount >= 5) is { } item) doc.SetItemCount(item.Id, 5);
        var expected = doc.BuildOutput();
        var categories = doc.Items.OrderBy(i => i.Id).Select(i => (i.Id, i.Category, i.MaxCount)).ToList();
        foreach (var language in Strings.Languages.Skip(1).Append(Strings.Languages[0]))
        {
            Catalog.UseLanguage(language.Code);
            doc.RefreshNames();
            Check(label, doc.BuildOutput().SequenceEqual(expected), "el idioma no cambia lo que se guarda", language.Code);
            Check(label, doc.Items.OrderBy(i => i.Id).Select(i => (i.Id, i.Category, i.MaxCount)).SequenceEqual(categories), "el idioma no cambia el catálogo de objetos", language.Code);
            Check(label, doc.Items.All(i => i.Name.Trim().Length > 0), "cada objeto tiene nombre", language.Code);
        }
    }

    // The whole path again in each language, and everything the app composes from the game's names.
    static void InEveryLanguage(GameVersion game, string label, string work)
    {
        foreach (var language in Strings.Languages.Skip(1))
        {
            string name = $"{label} ({language.Code})";
            Catalog.UseLanguage(language.Code);
            try
            {
                var folder = Path.Combine(work, language.Code);
                Directory.CreateDirectory(folder);
                Run(game, name, folder);
                var sav = TestSaves.Start(game);
                var rules = new GameRules(sav);
                EveryForm(sav, rules, name, game);
                Check(name, rules.FullName.Trim().Length > rules.ShortName.Length, "nombra el juego", rules.FullName);
                foreach (var pouch in Enum.GetValues<InventoryType>())
                    Check(name, Strings.Has(rules.PouchName(pouch)), "traduce el bolsillo", rules.PouchName(pouch));
                Check(name, Enumerable.Range(0, 18).All(t => Catalog.TypeName(t).Trim().Length > 0), "nombra los tipos");
                for (ushort species = 1; species <= sav.MaxSpeciesID; species++)
                {
                    if (!sav.Personal.IsPresentInGame(species, 0)) continue;
                    var entry = Entry(sav, rules, species, game);
                    Check(name, Catalog.GetAbilities(entry).All(a => a.Label.Trim().Length > 3), "nombra las habilidades", species.ToString());
                    Check(name, entry.SpeciesName.Trim().Length > 0 && entry.Location.Length > 0, "nombra al Pokémon", species.ToString());
                }
            }
            finally { Catalog.UseLanguage("es"); }
        }
    }

    /// <summary>Nothing goes untranslated, and no translation loses a value it has to show.</summary>
    public static void CheckStrings(string source)
    {
        const string where = "textos";
        var code = Directory.GetFiles(source, "*.cs").Where(f => Path.GetFileName(f) != "Strings.cs").Select(File.ReadAllText).ToList();
        var used = code.SelectMany(text => Regex.Matches(text, "\\bT\\(\"((?:[^\"\\\\]|\\\\.)*)\"").Select(m => Regex.Unescape(m.Groups[1].Value))).Distinct().ToList();
        foreach (var text in used) Check(where, Strings.Has(text), "está en la tabla", text);
        var titles = Enum.GetValues<GameVersion>().Select(GameRules.GameTitle).Distinct().Where(t => t is not ("" or "X" or "Y" or "X / Y" or "XD" or "Z-A" or "Let's Go"));
        foreach (var title in titles) Check(where, Strings.Has(title), "el juego está en la tabla", title);
        foreach (var row in Strings.AllTexts)
        {
            var values = Regex.Matches(row[0], "\\{\\d+(:\\w+)?\\}").Select(m => m.Value).Order().ToList();
            foreach (var text in row.Skip(1))
            {
                Check(where, text.Length > 0, "tiene traducción", row[0]);
                Check(where, Regex.Matches(text, "\\{\\d+(:\\w+)?\\}").Select(m => m.Value).Order().SequenceEqual(values), "conserva sus valores", text);
            }
            string literal = "\"" + row[0].Replace("\n", "\\n") + "\"";
            Check(where, code.Any(c => c.Contains(literal)), "se usa en la app", row[0]);
            Check(where, row[0] == "N-" || row[2] != row[0] || !row[0].Any(char.IsLetter) || row[0].Length <= 2, "está en chino", row[0]);
        }
        Console.WriteLine();
        foreach (var f in failures) Console.WriteLine("  " + f);
        Console.WriteLine($"RESULTADO: {used.Count} textos usados, {Strings.AllTexts.Count()} en la tabla, {passed} comprobaciones correctas, {failed} fallidas");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    static void Stuck(string label, SaveFile sav, GameRules rules, GameVersion version, Species species, byte form)
    {
        if ((ushort)species > sav.MaxSpeciesID || !sav.Personal.IsPresentInGame((ushort)species, form)) return;
        var pk = Make(sav, (ushort)species, version);
        pk.Form = form;
        pk.RefreshChecksum();
        Check(label, Catalog.GetForms(new PokemonEntry(rules, false, 0, pk)).Count == 0, "no toca una forma que debe quedarse", species + " " + form);
    }

    // A fused or mega Pokémon that is already in the save keeps what it is.
    static void StuckForms(GameVersion version, string label)
    {
        var sav = TestSaves.Start(version);
        var rules = new GameRules(sav);
        if (!rules.HasStoredForm) return;
        Stuck(label, sav, rules, version, Species.Kyurem, 2);
        Stuck(label, sav, rules, version, Species.Necrozma, 1);
        Stuck(label, sav, rules, version, Species.Calyrex, 1);
        Stuck(label, sav, rules, version, Species.Charizard, 1);
        Stuck(label, sav, rules, version, Species.Meowstic, 1);
        Stuck(label, sav, rules, version, Species.Koraidon, 1);
    }

    /// <summary>Every form any game offers, which build-tools\Map-FormSprites.py pairs with its image.</summary>
    public static void ListForms(string path)
    {
        var english = GameInfo.GetStrings("en");
        var rows = new SortedDictionary<string, object>();
        foreach (var (game, _) in Games)
        {
            var sav = TestSaves.Start(game);
            var rules = new GameRules(sav);
            for (ushort species = 1; species <= sav.MaxSpeciesID; species++)
            {
                if (!sav.Personal.IsPresentInGame(species, 0)) continue;
                foreach (var choice in Catalog.GetForms(Entry(sav, rules, species, game)))
                {
                    var names = FormConverter.GetFormList(species, english.Types, english.forms, ["M", "F", "-"], rules.Context);
                    var variant = Catalog.ImageVariant(rules, species, choice.Form, choice.Argument);
                    rows.TryAdd($"{species}-{variant}", new { species, choice.Form, choice.Argument, variant, name = names[choice.Form], choice.Label });
                }
            }
        }
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(rows.Values, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{rows.Count} formas");
    }

    // The bytes of a real Z-A save, not just what they read as: only the places involved may differ, and only as the game keeps them.
    static List<int> DifferentSlots(Span<byte> before, Span<byte> after, int size)
    {
        var different = new List<int>();
        for (int i = 0; i * size < before.Length; i++)
            if (!before.Slice(i * size, size).SequenceEqual(after.Slice(i * size, size))) different.Add(i);
        return different;
    }

    static SAV9ZA Written(SaveDocument doc, string work, string name)
    {
        var path = Path.Combine(work, "real." + name);
        File.WriteAllBytes(path, doc.BuildOutput());
        return SaveDocument.Open(path).ZaSave!;
    }

    /// <summary>Box operations on a played Z-A save, for any such save. Works on copies in memory and in the work folder.</summary>
    public static void RealSave(string source, string work)
    {
        const string label = "partida real";
        Directory.CreateDirectory(work);
        string hash = SaveDocument.Hash(File.ReadAllBytes(source));
        var doc = SaveDocument.Open(source);
        if (doc.ZaSave is not { } save)
        {
            Console.WriteLine($"Se omiten las comprobaciones byte a byte: son para Z-A y esta partida es de {doc.Rules.FullName}. Las de cada juego están en --boxes.");
            return;
        }
        int slot = save.SIZE_BOXSLOT, teamSlot = Party9a.SlotSize;
        var boxes = save.BoxInfo.Data.ToArray(); var party = save.PartyInfo.Data.ToArray();
        int count = doc.Pokemon.Count, teamCount = save.PartyCount;
        var first = doc.Pokemon.First(p => !p.IsParty);
        int empty = doc.FreePlace(0)!.Value.Index, origin = first.Index;

        doc.Move(first, new(false, empty));
        var moved = Written(doc, work, "moved");
        var changed = DifferentSlots(boxes, moved.BoxInfo.Data, slot);
        Check(label, changed.Order().SequenceEqual(new[] { origin, empty }.Order()), "mover cambia solo sus dos lugares", string.Join(',', changed));
        Check(label, DifferentSlots(party, moved.PartyInfo.Data, teamSlot).Count == 0, "mover en las cajas no toca el equipo");
        Check(label, moved.BoxInfo.Data.Slice(origin * slot, slot).SequenceEqual(boxes.AsSpan(empty * slot, slot)), "el lugar liberado queda igual que uno vacío del juego");
        Check(label, moved.BoxInfo.Data.Slice(empty * slot, save.SIZE_PARTY).SequenceEqual(boxes.AsSpan(origin * slot, save.SIZE_PARTY)), "el Pokémon llega byte por byte");
        doc.UndoLayout();
        Check(label, SaveDocument.Hash(doc.BuildOutput()) == hash, "deshacer deja la partida idéntica");
        if (save.BoxesUnlocked < save.BoxCount)
        {
            Refused(label, () => doc.Move(first, new(false, save.BoxesUnlocked * save.BoxSlotCount)), "mover a una caja que el juego no ha abierto");
            Check(label, doc.FreePlace(save.BoxCount - 1)!.Value.Index < save.BoxesUnlocked * save.BoxSlotCount, "busca lugar solo en las cajas abiertas");
        }

        var leader = doc.TeamSlots()[0]!;
        doc.Move(first, leader.Place);
        var swapped = Written(doc, work, "swapped");
        Check(label, DifferentSlots(party, swapped.PartyInfo.Data, teamSlot).SequenceEqual([0]), "cambiar con el equipo toca solo ese lugar del equipo");
        Check(label, DifferentSlots(boxes, swapped.BoxInfo.Data, slot).SequenceEqual([origin]), "cambiar con el equipo toca solo ese lugar de la caja");
        Check(label, swapped.PartyCount == teamCount && swapped.PartyInfo.Data.Slice(0, save.SIZE_PARTY).SequenceEqual(boxes.AsSpan(origin * slot, save.SIZE_PARTY)), "el de la caja llega al equipo byte por byte");
        doc.UndoLayout();

        if (teamCount > 1)
        {
            doc.Delete(doc.TeamSlots()[0]!);
            var shorter = Written(doc, work, "shorter");
            Check(label, shorter.PartyCount == teamCount - 1, "borrar acorta el equipo", shorter.PartyCount.ToString());
            for (int i = 0; i < teamCount - 1; i++)
                Check(label, shorter.PartyInfo.GetSlot(i).Span.SequenceEqual(party.AsSpan((i + 1) * teamSlot, teamSlot)), "los demás suben un lugar byte por byte", i.ToString());
            Check(label, shorter.GetPartySlotAtIndex(teamCount - 1).Species == 0, "el último lugar queda vacío");
            Check(label, DifferentSlots(boxes, shorter.BoxInfo.Data, slot).Count == 0, "borrar del equipo no toca las cajas");
            doc.UndoLayout();
        }

        var clone = doc.Clone(first, new(false, empty)).Entry;
        var cloned = Written(doc, work, "cloned");
        Check(label, DifferentSlots(boxes, cloned.BoxInfo.Data, slot).SequenceEqual([empty]), "clonar toca solo el lugar nuevo");
        Check(label, cloned.GetBoxSlotAtIndex(empty).PID == first.Original.PID, "el clon es el mismo Pokémon");
        doc.UndoLayout();

        doc.Delete(first);
        var deleted = Written(doc, work, "deleted");
        Check(label, DifferentSlots(boxes, deleted.BoxInfo.Data, slot).SequenceEqual([origin]), "borrar toca solo su lugar");
        Check(label, deleted.BoxInfo.Data.Slice(origin * slot, slot).SequenceEqual(boxes.AsSpan(empty * slot, slot)), "lo borrado queda igual que un lugar vacío del juego");
        Check(label, SaveDocument.Open(Path.Combine(work, "real.deleted")).Pokemon.Count == count - 1, "borrar quita a uno");
        doc.UndoLayout();

        var file = Path.Combine(work, "real." + first.Original.Extension);
        File.WriteAllBytes(file, SaveDocument.PokemonFile(first));
        var imported = doc.Import(file, new(false, empty)).Entry;
        Check(label, imported.Original.Data.SequenceEqual(first.Original.Data) || imported.Original.Data[..first.Original.SIZE_STORED].SequenceEqual(first.Original.Data[..first.Original.SIZE_STORED]), "un archivo trae al Pokémon entero");
        var withImport = Written(doc, work, "imported");
        Check(label, DifferentSlots(boxes, withImport.BoxInfo.Data, slot).SequenceEqual([empty]), "importar toca solo su lugar");
        doc.ResetLayout();

        if (doc.Pokemon.Count(p => !p.IsParty && doc.BoxOf(p) == 0) > 1)
        {
            doc.SortBox(0, BoxOrder.Pokedex);
            var sorted = Written(doc, work, "sorted");
            Check(label, DifferentSlots(boxes, sorted.BoxInfo.Data, slot).All(i => i < save.BoxSlotCount), "ordenar toca solo esa caja");
            doc.ResetLayout();
        }
        Check(label, !doc.HasChanges && SaveDocument.Hash(doc.BuildOutput()) == hash, "deshecho todo, la partida queda idéntica");
        Check(label, SaveDocument.Hash(File.ReadAllBytes(source)) == hash, "el archivo de origen no se toca");
        Console.WriteLine();
        foreach (var f in failures) Console.WriteLine("  " + f);
        Console.WriteLine($"RESULTADO: {count} Pokémon, {passed} comprobaciones correctas, {failed} fallidas");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    /// <summary>Only the box tools, in every game: the quick way to see those alone.</summary>
    public static void RunBoxes(string work, string? only = null)
    {
        Directory.CreateDirectory(work);
        foreach (var (game, label) in Games.Where(g => only == null || g.Label == only))
        {
            int before = failed;
            try { BoxTools(game, label, work); }
            catch (Exception ex) { failed++; failures.Add($"{label}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}  {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
            Console.WriteLine($"{(failed == before ? "ok   " : "FALLA")} {label}");
        }
        Console.WriteLine();
        foreach (var f in failures) Console.WriteLine("  " + f);
        Console.WriteLine($"\nRESULTADO: {Games.Length} juegos, {boxesClosedTried} con cajas cerradas, {passed} comprobaciones correctas, {failed} fallidas");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    public static void Run(string work)
    {
        // Colosseum and XD are left out: their saves live in an encrypted GameCube container
        // that PKHeX can read and write but cannot create from nothing, so there is no file to test with.
        Directory.CreateDirectory(work);
        foreach (var (game, label) in Games)
        {
            int before = failed;
            try { Run(game, label, work); FormsOnDisk(game, label, work); StuckForms(game, label); Abilities(game, label, work); Details(game, label, work); BoxTools(game, label, work); AbilityWithForms(game, label); LanguageSwitch(game, label, work); InEveryLanguage(game, label, work); }
            catch (Exception ex) { failed++; failures.Add($"{label}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}  {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
            Console.WriteLine($"{(failed == before ? "ok   " : "FALLA")} {label}");
        }
        Console.WriteLine();
        foreach (var f in failures) Console.WriteLine("  " + f);
        Console.WriteLine($"\nRESULTADO: {Games.Length} juegos, {formsTried} formas probadas, {passed} comprobaciones correctas, {failed} fallidas");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
