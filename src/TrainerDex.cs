using PKHeX.Core;
using System.Numerics;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>The trainer card as the game shows it: IDs in its own format, and each badge as one bit (or, in Sword and Shield, a count).</summary>
public sealed record TrainerData(string Name, byte Gender, uint Tid, uint Sid, int Hours, int Minutes, int Seconds, int Badges);

/// <summary>The trainer's pending changes. They reach a save only through <see cref="WriteTo"/>.</summary>
public sealed class TrainerEditor
{
    // At 255 hours the Game Boy clock stops for good, so that value is left to the game.
    const int GameBoyClock = 254, HandheldClock = 999, SwitchClock = 9999;
    const int BdspFirstBadgeFlag = 124, GymsInAGame = 8;
    const uint LargestShortId = ushort.MaxValue, LargestTid7 = 999_999, LargestSid7 = 4294;
    readonly SaveFile save;
    SaveFile? scratch;
    string testedName = "";
    bool testedNameFits;

    public TrainerEditor(SaveFile save)
    {
        this.save = save;
        Original = Read(save);
        Edit = Original;
    }

    public TrainerData Original { get; }
    public TrainerData Edit { get; set; }
    public bool Changed => Edit != Original;
    public void Reset() => Edit = Original;

    // Colosseum, XD and the box-only saves have other layouts, and no test file to prove a write on.
    public bool IsSupported => save is SAV1 or SAV2 or SAV3 or SAV4 or SAV5 or SAV6 or SAV7 or SAV7b or SAV8SWSH or SAV8BS or SAV8LA or SAV9SV or SAV9ZA;
    public bool HasGender => save is not SAV1 && (save is not SAV2 || save.Version == GameVersion.C);
    public bool HasSecretId => save.Generation >= 3;
    bool SixDigits => save.TrainerIDDisplayFormat == TrainerIDFormat.SixDigit;
    public uint MaxTid => SixDigits ? LargestTid7 : LargestShortId;
    public uint MaxSid => SixDigits ? LargestSid7 : LargestShortId;
    public string TidFormat => save.TrainerIDDisplayFormat.GetTrainerIDFormatStringTID();
    public string SidFormat => save.TrainerIDDisplayFormat.GetTrainerIDFormatStringSID();
    public int MaxNameLength => save.MaxStringLengthTrainer;
    public int MaxHours => save is SAV1 ? GameBoyClock : save is SAV7b or SAV8SWSH or SAV8BS or SAV8LA or SAV9SV or SAV9ZA ? SwitchClock : HandheldClock;
    /// <summary>From Gen 6 the character is a model dressed from its own data, which the gender doesn't update.</summary>
    public bool GenderLeavesClothes => save.Generation >= 6;

    /// <summary>How many badges have a place of their own. Sword and Shield only keep how many there are.</summary>
    public int BadgeFlags => save switch {
        SAV2 or SAV4HGSS => GymsInAGame * 2,
        SAV1 or SAV3 or SAV4 or SAV5 or SAV6 or SAV8BS => GymsInAGame,
        _ => 0,
    };
    public int MaxBadgeCount => save is SAV8SWSH ? GymsInAGame : 0;
    public bool HasBadges => BadgeFlags != 0 || MaxBadgeCount != 0;
    public bool ChangesStatus => Edit.Name != Original.Name || Edit.Gender != Original.Gender || Edit.Tid != Original.Tid || Edit.Sid != Original.Sid;
    public bool ChangesClock => Edit.Hours != Original.Hours || Edit.Minutes != Original.Minutes || Edit.Seconds != Original.Seconds;

    static TrainerData Read(SaveFile save) => new(save.OT, save.Gender, save.DisplayTID, save.DisplaySID,
        save.PlayedHours, save.PlayedMinutes, save.PlayedSeconds, ReadBadges(save));

    static int ReadBadges(SaveFile save) => save switch {
        SAV1 s => s.Badges, SAV2 s => s.Badges, SAV3 s => s.Badges,
        SAV4HGSS s => s.Badges | s.Badges16 << GymsInAGame, SAV4 s => s.Badges,
        SAV5 s => s.Misc.Badges, SAV6 s => s.Badges, SAV8SWSH s => s.Badges,
        SAV8BS s => Enumerable.Range(0, GymsInAGame).Sum(i => s.FlagWork.GetSystemFlag(BdspFirstBadgeFlag + i) ? 1 << i : 0),
        _ => 0,
    };

    static void WriteBadges(SaveFile save, int badges)
    {
        switch (save)
        {
            case SAV1 s: s.Badges = badges; break;
            case SAV2 s: s.Badges = badges; break;
            case SAV3 s: s.Badges = badges; break;
            case SAV4HGSS s: s.Badges = (byte)badges; s.Badges16 = badges >> GymsInAGame; break;
            case SAV4 s: s.Badges = (byte)badges; break;
            case SAV5 s: s.Misc.Badges = badges; break;
            case SAV6 s: s.Badges = badges; break;
            case SAV8SWSH s: s.Badges = badges; break;
            case SAV8BS s:
                for (int i = 0; i < GymsInAGame; i++) s.FlagWork.SetSystemFlag(BdspFirstBadgeFlag + i, (badges >> i & 1) != 0);
                s.MyStatus.BadgeCount = (byte)BitOperations.PopCount((uint)badges);
                break;
        }
    }

    public string? Validate()
    {
        var d = Edit;
        if (d == Original) return null;
        if (d.Name != Original.Name)
        {
            if (d.Name.Trim().Length == 0) return T("El nombre del entrenador no puede quedar vacío.");
            if (!NameFits(d.Name)) return T("Ese nombre tiene caracteres que este juego no admite.");
        }
        if (d.Gender != Original.Gender && (!HasGender || d.Gender > 1)) return T("Selecciona un género válido.");
        if (d.Tid != Original.Tid && d.Tid > MaxTid) return T("El ID de entrenador debe estar entre 0 y {0}.", MaxTid);
        if (d.Sid != Original.Sid && (!HasSecretId || d.Sid > MaxSid)) return T("El ID secreto debe estar entre 0 y {0}.", MaxSid);
        if ((d.Tid != Original.Tid || d.Sid != Original.Sid) && SixDigits && !save.IsValidTrainerID7(d.Sid, d.Tid)) return T("Esa combinación de ID no cabe en este juego.");
        if (ChangesClock && (d.Hours is < 0 || d.Hours > Math.Max(MaxHours, Original.Hours) || d.Minutes is < 0 or > 59 || d.Seconds is < 0 or > 59))
            return T("El tiempo de juego llega hasta {0} horas, con minutos y segundos entre 0 y 59.", MaxHours);
        int topBadge = BadgeFlags != 0 ? (1 << BadgeFlags) - 1 : MaxBadgeCount;
        if (d.Badges != Original.Badges && (d.Badges < 0 || d.Badges > topBadge)) return T("Las medallas no coinciden con este juego.");
        return null;
    }

    // Each game has its own alphabet and lengths, and whatever it can't store just disappears.
    bool NameFits(string name)
    {
        if (name.Length > MaxNameLength) return false;   // the Game Boy text writers don't check
        if (name == testedName) return testedNameFits;
        scratch ??= save.Clone();
        scratch.OT = name;
        testedName = name; testedNameFits = scratch.OT == name;
        return testedNameFits;
    }

    public void WriteTo(SaveFile output)
    {
        var (from, to) = (Original, Edit);
        if (to.Name != from.Name) output.OT = to.Name;
        if (to.Gender != from.Gender) output.Gender = to.Gender;
        if (to.Tid != from.Tid || to.Sid != from.Sid) output.SetDisplayID(to.Tid, to.Sid);
        if (to.Hours != from.Hours) output.PlayedHours = to.Hours;
        if (to.Minutes != from.Minutes) output.PlayedMinutes = to.Minutes;
        if (to.Seconds != from.Seconds) output.PlayedSeconds = to.Seconds;
        if (to.Badges != from.Badges) WriteBadges(output, to.Badges);
    }

    public bool Matches(SaveFile reopened) => Read(reopened) == Edit;
}

public sealed record DexState(bool Seen, bool Caught);

public sealed record DexChange(ushort Species, DexState Before, DexState After);

/// <summary>A species and the number one Pokédex gives it.</summary>
public readonly record struct DexEntry(int Number, ushort Species);

/// <summary>
/// One of a game's Pokédexes, in its order. Marks belong to the species, so choosing one only changes what is looked at.
/// <see cref="Name"/> is the Spanish key of its text.
/// </summary>
public sealed record DexList(string Name, IReadOnlyList<DexEntry> Entries)
{
    /// <summary>A second column of national numbers would repeat the first.</summary>
    public bool HasNationalNumbers => Entries.All(entry => entry.Number == entry.Species);
}

/// <summary>
/// Seen and caught, per species, as pending changes. <see cref="SaveFile.SetSeen"/> and <see cref="SaveFile.SetCaught"/>
/// do nothing from Gen 4 on, so each game gets here what PKHeX or the game itself would write there.
/// </summary>
public sealed class PokedexEditor
{
    public const string National = "Nacional", AllSpecies = "Todas las especies";
    const int LumioseDexSize = 232, KantoDexSize = 151;
    const uint NotShinyPid = 0x12345678;
    readonly SaveFile save;
    readonly Dictionary<ushort, DexState> originals = [];
    readonly Dictionary<ushort, DexState> changes = [];

    public PokedexEditor(SaveFile save)
    {
        this.save = save;
        Entries = save.HasPokeDex ? SpeciesIn(save).Order().ToList() : [];
        foreach (var species in Entries) originals[species] = Read(save, species);
        Lists = Entries.Count == 0 ? [] : ListsIn(save, Entries);
    }

    /// <summary>Every species this save's Pokédex has room for, in national order.</summary>
    public IReadOnlyList<ushort> Entries { get; }
    /// <summary>The Pokédexes this save's game shows, the regional ones first.</summary>
    public IReadOnlyList<DexList> Lists { get; }
    /// <summary>The game has one list, with the same numbers as the National Pokédex.</summary>
    public bool RegionalIsNational => Lists.Count == 1 && Lists[0].HasNationalNumbers;
    public bool HasAllSpeciesList => Lists.Any(list => list.Name == AllSpecies);
    public bool CanView => Entries.Count != 0;
    // Scarlet and Violet moved their Pokédex in 2.0.1, and no test file can have the older one.
    public bool CanEdit => CanView && (save is not SAV9SV sv || sv.Zukan.GetRevision() != 0);
    // Legends: Arceus keeps no way back from "seen": what shows it is research, not a flag of its own.
    public bool CanClear => CanEdit && save is not SAV8LA;
    public int ChangeCount => changes.Count;
    public int SeenCount => Entries.Count(species => Get(species).Seen);
    public int CaughtCount => Entries.Count(species => Get(species).Caught);
    public int SeenIn(DexList list) => list.Entries.Count(entry => Get(entry.Species).Seen);
    public int CaughtIn(DexList list) => list.Entries.Count(entry => Get(entry.Species).Caught);
    public IEnumerable<DexChange> Changes => changes.OrderBy(c => c.Key).Select(c => new DexChange(c.Key, originals[c.Key], c.Value));
    public DexState Get(ushort species) => changes.GetValueOrDefault(species) ?? originals[species];
    public DexState Original(ushort species) => originals[species];

    /// <summary>A caught Pokémon has been seen, so one follows the other.</summary>
    public void Set(ushort species, bool seen, bool caught)
    {
        if (!CanEdit) return;
        var original = originals[species];
        if (!CanClear) { seen |= original.Seen; caught |= original.Caught; }
        var wanted = new DexState(seen || caught, caught);
        if (wanted == original) changes.Remove(species);
        else changes[species] = wanted;
    }

    public void MarkAll(bool caught) => Mark(Entries, caught);

    public void MarkAll(DexList list, bool caught) => Mark(list.Entries.Select(entry => entry.Species), caught);

    void Mark(IEnumerable<ushort> species, bool caught)
    {
        foreach (var one in species) Set(one, true, caught || Get(one).Caught);
    }

    public void Reset() => changes.Clear();

    static IEnumerable<ushort> SpeciesIn(SaveFile save)
    {
        var inGame = Enumerable.Range(1, save.MaxSpeciesID).Select(i => (ushort)i).Where(save.Personal.IsSpeciesInGame);
        return save switch {
            SAV8SWSH s => s.Zukan.DexLookup.Keys,
            SAV8LA => inGame.Where(species => PokedexSave8a.GetDexIndex(PokedexType8a.Hisui, species) != 0),
            SAV9SV s => inGame.Where(species => s.Zukan.GetDexIndex(species).Index != 0 && Forms(s, species).Any()),
            // Without the DLC only the Lumiose Pokédex exists; its entries come first.
            SAV9ZA { SaveRevision: 0 } s => inGame.Where(species => s.Personal[species].DexIndex <= LumioseDexSize),
            _ => inGame,
        };
    }

    /// <summary>Every name a list can have, for measuring them in each language.</summary>
    public static IEnumerable<string> ListNames =>
    [
        "Kanto", "Johto", "Hoenn", "Sinnoh", "Teselia", "Kalos Centro", "Kalos Costa", "Kalos Montaña", "Alola", "Melemele", "Akala", "Ula-Ula", "Poni",
        "Galar", "Isla de la Armadura", "Nieves de la Corona", "Hisui", "Paldea", "Noroteo", "Academia Arándano", "Luminalia", "Luminalia Dimensional", National, AllSpecies,
    ];

    // Gen 7 has no National Pokédex, yet it keeps marks for what arrives from elsewhere: the last list leaves none of them out of reach.
    static List<DexList> ListsIn(SaveFile save, IReadOnlyList<ushort> entries)
    {
        var known = entries.ToHashSet();
        var national = new DexList(National, entries.Select(species => new DexEntry(species, species)).ToList());
        DexList Table(string name, ushort[] species, int first = 1) => new(name, species.Select((one, i) => new DexEntry(i + first, one)).Where(entry => known.Contains(entry.Species)).ToList());
        DexList Indexed(string name, Func<ushort, int> number) => new(name, entries.Select(species => new DexEntry(number(species), species)).Where(entry => entry.Number != 0).OrderBy(entry => entry.Number).ToList());
        DexList Kanto() => new("Kanto", entries.Take(KantoDexSize).Select(species => new DexEntry(species, species)).ToList());
        List<DexList> lists = save switch {
            SAV1 => [Kanto()],
            SAV2 => [Table("Johto", DexTables.JohtoGS), national],
            SAV3RS or SAV3E => [Table("Hoenn", DexTables.HoennRS), national],
            SAV3FRLG => [Kanto(), national],
            SAV4DP => [Table("Sinnoh", DexTables.SinnohDP), national],
            SAV4Pt => [Table("Sinnoh", DexTables.SinnohPt), national],
            SAV4HGSS => [Table("Johto", DexTables.JohtoHGSS), national],
            // Unova is the one region that numbers from zero: Victini comes before the first partner.
            SAV5BW => [Table("Teselia", DexTables.UnovaBW, 0), national],
            SAV5B2W2 => [Table("Teselia", DexTables.UnovaB2W2, 0), national],
            SAV6XY => [Table("Kalos Centro", DexTables.KalosCentral), Table("Kalos Costa", DexTables.KalosCoastal), Table("Kalos Montaña", DexTables.KalosMountain), national],
            SAV6AO => [Table("Hoenn", DexTables.HoennORAS), national],
            SAV7SM => [Table("Alola", DexTables.AlolaSM), Table("Melemele", DexTables.MelemeleSM), Table("Akala", DexTables.AkalaSM), Table("Ula-Ula", DexTables.UlaUlaSM), Table("Poni", DexTables.PoniSM)],
            SAV7USUM => [Table("Alola", DexTables.AlolaUSUM), Table("Melemele", DexTables.MelemeleUSUM), Table("Akala", DexTables.AkalaUSUM), Table("Ula-Ula", DexTables.UlaUlaUSUM), Table("Poni", DexTables.PoniUSUM)],
            // Meltan and Melmetal close the list.
            SAV7b => [new("Kanto", entries.Select((species, i) => new DexEntry(i + 1, species)).ToList())],
            SAV8SWSH s => Galar(s, Indexed),
            SAV8BS s => [Indexed("Sinnoh", species => s.Personal[species].PokeDexIndex), national],
            SAV8LA => [Indexed("Hisui", species => PokedexSave8a.GetDexIndex(PokedexType8a.Hisui, species))],
            SAV9SV s => Paldea(s, Indexed),
            SAV9ZA s => s.SaveRevision == 0 ? [Lumiose(s, Indexed)] : [Lumiose(s, Indexed), Hyperspace(s, Indexed)],
            _ => [national],
        };
        var listed = lists.SelectMany(list => list.Entries).Select(entry => entry.Species).ToHashSet();
        if (!known.SetEquals(listed)) lists.Add(new(AllSpecies, national.Entries));
        return lists;
    }

    // What a save holds depends on the DLC it has played, so each list is there only if the save has room for it.
    static List<DexList> Galar(SAV8SWSH save, Func<string, Func<ushort, int>, DexList> indexed)
    {
        int revision = save.Zukan.GetRevision();
        var lists = new List<DexList> { indexed("Galar", species => save.Personal[species].PokeDexIndex) };
        if (revision >= 1) lists.Add(indexed("Isla de la Armadura", species => save.Personal[species].ArmorDexIndex));
        if (revision >= 2) lists.Add(indexed("Nieves de la Corona", species => save.Personal[species].CrownDexIndex));
        return lists;
    }

    static List<DexList> Paldea(SAV9SV save, Func<string, Func<ushort, int>, DexList> indexed)
    {
        var lists = new List<DexList> { indexed("Paldea", species => FormIndex(save, species, info => info.DexPaldea)) };
        if (save.SaveRevision >= 1) lists.Add(indexed("Noroteo", species => FormIndex(save, species, info => info.DexKitakami)));
        if (save.SaveRevision >= 2) lists.Add(indexed("Academia Arándano", species => FormIndex(save, species, info => info.DexBlueberry)));
        return lists;
    }

    // A regional form can hold the number its species' first form lacks.
    static int FormIndex(SAV9SV save, ushort species, Func<PersonalInfo9SV, int> index)
    {
        byte forms = save.Personal.GetFormEntry(species, 0).FormCount;
        for (byte form = 0; form <= forms; form++)
            if (index(save.Personal.GetFormEntry(species, form)) is var number and not 0) return number;
        return 0;
    }

    // The two share one run of numbers in the data, and the game starts the second over at 1.
    static DexList Lumiose(SAV9ZA save, Func<string, Func<ushort, int>, DexList> indexed) =>
        indexed("Luminalia", species => save.Personal[species].DexIndex is var index and <= LumioseDexSize ? index : 0);

    static DexList Hyperspace(SAV9ZA save, Func<string, Func<ushort, int>, DexList> indexed) =>
        indexed("Luminalia Dimensional", species => Math.Max(save.Personal[species].DexIndex - LumioseDexSize, 0));

    // The forms a save can register. Noble Pokémon don't count for research.
    static IEnumerable<byte> Forms(SaveFile save, ushort species)
    {
        for (byte form = 0; form < Math.Max((int)save.Personal[species, 0].FormCount, 1); form++)
        {
            if (!save.Personal.IsPresentInGame(species, form)) continue;
            if (save is SAV8LA && PokedexSave8a.IsNoblePokemon(species, form)) continue;
            if (save is SAV9SV sv && sv.Personal.GetFormEntry(species, form).DexGroup > sv.SaveRevision + 1) continue;
            yield return form;
        }
    }

    static byte FirstForm(SaveFile save, ushort species) => Forms(save, species).FirstOrDefault();

    // Gender 0 stands for both when a species has the two.
    static byte GenderOf(IGenderDetail personal) => personal.Genderless ? (byte)2 : personal.OnlyFemale ? (byte)1 : (byte)0;

    static DexState Read(SaveFile save, ushort species) => new(save.GetSeen(species), save.GetCaught(species)
        || save is SAV6XY xy && species <= (ushort)Species.Genesect && xy.Zukan.GetForeignFlag(species));

    /// <summary>The first species that doesn't read back as expected, if any.</summary>
    public ushort? FirstDifference(SaveFile reopened)
    {
        foreach (var species in Entries)
            if (Read(reopened, species) != Get(species)) return species;
        return null;
    }

    public void WriteTo(SaveFile output)
    {
        foreach (var (species, had, wanted) in Changes)
        {
            if (had.Seen && !wanted.Seen) { ClearEntry(output, species); continue; }
            if (had.Caught && !wanted.Caught) ClearCaught(output, species);
            if (wanted.Seen && !had.Seen) MarkSeen(output, species);
            if (wanted.Caught && !had.Caught) MarkCaught(output, species);
        }
    }

    // PKHeX registers a Pokémon the way the game does, so a plain one stands for the species.
    static PKM Sample(SaveFile save, ushort species)
    {
        byte form = FirstForm(save, species);
        var pk = save.BlankPKM;
        pk.Species = species; pk.Form = form; pk.Gender = GenderOf(save.Personal[species, form]);
        pk.Language = save.Language; pk.Version = save.Version; pk.PID = NotShinyPid;
        if (pk is PA8 hisui) { hisui.HeightScalar = hisui.WeightScalar = 128; hisui.ResetHeight(); hisui.ResetWeight(); }
        return pk;
    }

    static void MarkSeen(SaveFile output, ushort species)
    {
        byte form = FirstForm(output, species);
        var personal = output.Personal[species, form];
        byte gender = GenderOf(personal);
        switch (output)
        {
            case SAV1 or SAV3: output.SetSeen(species, true); break;
            case SAV2:
                output.SetSeen(species, true);
                // Its Pokédex page crashes without letters, and only catching one records them.
                if (species == (ushort)Species.Unown) { output.SetCaught(species, true); output.SetCaught(species, false); }
                break;
            case SAV4 s: s.Dex.ModifyAll(species, Zukan4.SetDexArgs.SeenAll | Zukan4.SetDexArgs.SetAllForms); break;
            case SAV5 s: s.Zukan.CompleteSeen(species, false, personal); break;
            case SAV6XY s: s.Zukan.CompleteSeen(species, false, personal); break;
            case SAV6AO s: s.Zukan.CompleteSeen(species, false, personal); break;
            case SAV7 s: MarkSeen(s.Zukan, species, gender); break;
            case SAV7b s: MarkSeen(s.Blocks.Zukan, species, gender); break;
            case SAV8SWSH s:
                foreach (int region in personal.IsDualGender ? new[] { 0, 1 } : [gender & 1]) s.Zukan.SetSeenRegion(species, form, region);
                break;
            case SAV8BS s:
                s.Zukan.GetGenderFlags(species, out _, out _, out var maleShiny, out var femaleShiny);
                s.Zukan.SetState(species, ZukanState8b.Seen);
                s.Zukan.SetGenderFlags(species, !personal.OnlyFemale, !personal.OnlyMale, maleShiny, femaleShiny);
                for (byte f = 0; f < Zukan8b.GetFormCount(species); f++) s.Zukan.SetHasFormFlag(species, f, false, true);
                break;
            case SAV8LA s: s.PokedexSave.SetPokeSeenInWild(Sample(s, species)); break;
            case SAV9SV s:
                var paldea = s.Zukan.DexKitakami.Get(species);
                paldea.SetIsModelSeen(false, true); paldea.SetSeenForm(form, true); paldea.SetHeardForm(form, true); paldea.SetIsGenderSeen(gender, true);
                break;
            case SAV9ZA s:
                // What a real save holds after a sighting.
                var lumiose = s.Zukan.GetEntry(species);
                lumiose.SetIsGenderSeen(gender, true); lumiose.SetIsFormSeen(form, true);
                lumiose.DisplayForm = form; lumiose.SetDisplayGender((Gender)gender, species, form);
                break;
        }
    }

    // PKHeX draws the gender first shown at random unless one is there already, and every build must give the same bytes.
    static void MarkSeen(Zukan7 zukan, ushort species, byte gender)
    {
        zukan.SetSeen(species, gender & 1, true);
        zukan.SetSeenSingle(species);
    }

    static void MarkCaught(SaveFile output, ushort species)
    {
        switch (output)
        {
            case SAV1 or SAV2 or SAV3: output.SetCaught(species, true); break;
            case SAV4 s: s.Dex.SetDex(Sample(s, species)); break;
            case SAV5 s: s.Zukan.SetDex(Sample(s, species)); break;
            case SAV6XY s: s.Zukan.SetDex(Sample(s, species)); break;
            case SAV6AO s: s.Zukan.SetDex(Sample(s, species)); break;
            case SAV7 s: s.Zukan.SetDex(Sample(s, species)); break;
            // Registering also writes a size record, which a plain Pokémon would ruin.
            case SAV7b s: s.Blocks.Zukan.SetCaughtSingle(species); break;
            case SAV8SWSH s: s.Zukan.SetDex(Sample(s, species)); break;
            case SAV8BS s: s.Zukan.SetDex(Sample(s, species)); break;
            case SAV8LA s: s.PokedexSave.OnPokeGet_TradeWithoutEvolution(Sample(s, species)); break;
            case SAV9SV s:
                byte form = FirstForm(s, species);
                var personal = s.Personal.GetFormEntry(species, form);
                var entry = s.Zukan.DexKitakami.Get(species);
                entry.SetObtainedForm(form, true); entry.SetLocalStates(personal, form, GenderOf(personal), false); entry.SetLanguageFlag(s.Language, true);
                break;
            case SAV9ZA s: s.Zukan.SetDex(Sample(s, species)); break;
        }
    }

    static void ClearCaught(SaveFile output, ushort species)
    {
        switch (output)
        {
            case SAV1 or SAV2 or SAV3: output.SetCaught(species, false); break;
            case SAV4 s: s.Dex.ModifyAll(species, Zukan4.SetDexArgs.CaughtNone); break;
            case SAV5 s: s.Zukan.SetCaught(species, false); s.Zukan.SetAllLanguage(species, false); break;
            case SAV6XY s:
                s.Zukan.SetCaught(species, false); s.Zukan.SetAllLanguage(species, false);
                if (species <= (ushort)Species.Genesect) s.Zukan.SetForeignFlag(species, false);
                break;
            case SAV6AO s: s.Zukan.SetCaught(species, false); s.Zukan.SetAllLanguage(species, false); break;
            case SAV7 s: s.Zukan.SetCaughtSingle(species, false); break;
            case SAV7b s: s.Blocks.Zukan.SetCaughtSingle(species, false); break;
            case SAV8SWSH s: s.Zukan.SetCaughtFlags32(species, 0); s.Zukan.SetUnk1Count(species, 0); s.Zukan.SetUnk2Count(species, 0); break;
            case SAV8BS s: s.Zukan.SetState(species, ZukanState8b.Seen); s.Zukan.SetLanguageFlags(species, 0); break;
            case SAV9SV s: s.Zukan.DexKitakami.Get(species).ClearCaught(); break;
            case SAV9ZA s:
                var entry = s.Zukan.GetEntry(species);
                entry.ClearCaught(); entry.SetDisplayIsNew(false);
                // A seen entry keeps showing what was seen.
                for (byte form = 0; form < s.Personal[species].FormCount; form++)
                {
                    if (!entry.GetIsFormSeen(form)) continue;
                    entry.DisplayForm = form; entry.SetDisplayGender((Gender)GenderOf(s.Personal[species, form]), species, form);
                    break;
                }
                break;
        }
    }

    static void ClearEntry(SaveFile output, ushort species)
    {
        switch (output)
        {
            case SAV1 or SAV2 or SAV3: output.SetCaught(species, false); output.SetSeen(species, false); break;
            case SAV4 s: s.Dex.ModifyAll(species, Zukan4.SetDexArgs.None); break;
            case SAV5 s: Clear(s.Zukan, species); break;
            case SAV6XY s: Clear(s.Zukan, species); break;
            case SAV6AO s: Clear(s.Zukan, species); break;
            case SAV7 s: Clear(s.Zukan, species); break;
            case SAV7b s: Clear(s.Blocks.Zukan, species); break;
            case SAV8SWSH s: s.Zukan.ClearDexEntryAll(species); break;
            case SAV8BS s: s.Zukan.ClearDexEntryAll(species); break;
            case SAV9SV s: s.Zukan.ClearDexEntryAll(species); break;
            case SAV9ZA s: s.Zukan.ClearDexEntryAll(species); break;
        }
    }

    static void Clear(Zukan7 zukan, ushort species)
    {
        zukan.SetSeenSingle(species, false, shinyToo: true);
        zukan.SetCaughtSingle(species, false);
    }

    // Gen 5 and Gen 6 lay this out the same way, in two classes with nothing in common.
    static void Clear(Zukan5 zukan, ushort species)
    {
        zukan.SetCaught(species, false); zukan.SetAllLanguage(species, false);
        zukan.ClearSeen(species); zukan.ClearDisplayed(species);
        var (index, count) = zukan.GetFormIndex(species);
        for (int form = index; form < index + count; form++)
            foreach (bool shiny in new[] { false, true }) { zukan.SetFormSeen(form, shiny, false); zukan.SetFormDisplayed(form, shiny, false); }
    }

    static void Clear(Zukan6 zukan, ushort species)
    {
        zukan.SetCaught(species, false); zukan.SetAllLanguage(species, false);
        zukan.ClearSeen(species); zukan.ClearDisplayed(species);
        var (index, count) = zukan.GetFormIndex(species);
        for (int form = index; form < index + count; form++)
            foreach (bool shiny in new[] { false, true }) { zukan.SetFormSeen(form, shiny, false); zukan.SetFormDisplayed(form, shiny, false); }
    }
}
