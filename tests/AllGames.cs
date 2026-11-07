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

    public static void Run(string work)
    {
        // Colosseum and XD are left out: their saves live in an encrypted GameCube container
        // that PKHeX can read and write but cannot create from nothing, so there is no file to test with.
        Directory.CreateDirectory(work);
        foreach (var (game, label) in Games)
        {
            int before = failed;
            try { Run(game, label, work); FormsOnDisk(game, label, work); StuckForms(game, label); Abilities(game, label, work); AbilityWithForms(game, label); LanguageSwitch(game, label, work); InEveryLanguage(game, label, work); }
            catch (Exception ex) { failed++; failures.Add($"{label}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}  {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}"); }
            Console.WriteLine($"{(failed == before ? "ok   " : "FALLA")} {label}");
        }
        Console.WriteLine();
        foreach (var f in failures) Console.WriteLine("  " + f);
        Console.WriteLine($"\nRESULTADO: {Games.Length} juegos, {formsTried} formas probadas, {passed} comprobaciones correctas, {failed} fallidas");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
