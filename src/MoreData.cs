using PKHeX.Core;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Serialization;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A date as a game keeps it. A Pokémon that was never given one holds zeros, which read as 2000-00-00.</summary>
public readonly record struct GameDate(int Year, int Month, int Day)
{
    public const int FirstYear = 2000;
    public static GameDate From(byte year, byte month, byte day) => new(FirstYear + year, month, day);
    [JsonIgnore] public bool Exists => DateUtil.IsValidDate(Year, Month, Day);
    public override string ToString() => $"{Year:0000}-{Month:00}-{Day:00}";
}

/// <summary>A ribbon or mark of this game. Most are had or not; the counted ones go up to their maximum.</summary>
public sealed record RibbonChoice(string Property, string Label, int Max)
{
    const string MarkPrefix = "RibbonMark";
    public bool IsMark => Property.StartsWith(MarkPrefix, StringComparison.Ordinal);
    public bool IsCounted => Max > 1;
    public override string ToString() => Label;
}

/// <summary>PP Ups belong to a move: a new move in that slot starts without them, and the old one gets them back.</summary>
public sealed record PPBoost(ushort Move, int Count);

/// <summary>What a Pokémon keeps beyond its stats and moves. Each game stores only part of it: <see cref="GameRules"/> says which.</summary>
public sealed class MoreData
{
    public const int StatCount = 6, MoveCount = 4, MaxPPUps = 3, MaxDynamaxLevel = 10, MaxScalar = byte.MaxValue, MaxContest = byte.MaxValue;

    public int Game { get; set; }
    public int Language { get; set; }
    public int TrainerGender { get; set; }
    public int MetLocation { get; set; }
    public int MetLevel { get; set; }
    public GameDate MetDate { get; set; }
    public int EggLocation { get; set; }
    public GameDate EggDate { get; set; }
    /// <summary>Only what it has, by property name: 1 for a plain ribbon, and its count for a counted one.</summary>
    public Dictionary<string, int> Ribbons { get; set; } = [];
    public int[] Contest { get; set; } = new int[StatCount];
    public int PokerusStrain { get; set; }
    public int PokerusDays { get; set; }
    public PPBoost[] PPUps { get; set; } = [new(0, 0), new(0, 0), new(0, 0), new(0, 0)];
    public ushort[] Relearn { get; set; } = new ushort[MoveCount];
    public bool[] HyperTraining { get; set; } = new bool[StatCount];
    public int TeraType { get; set; }
    public int DynamaxLevel { get; set; }
    public bool Gigantamax { get; set; }
    public int Height { get; set; }
    public int Weight { get; set; }
    public int Scale { get; set; }
    public bool Alpha { get; set; }
    public int[] GVs { get; set; } = new int[StatCount];
    public int[] Markings { get; set; } = [];

    static readonly ConcurrentDictionary<Type, PropertyInfo[]> RibbonTables = new();

    // PKHeX's own list also has the totals Gen 3 only works out, which nothing can write. Backups read this on another thread.
    internal static PropertyInfo[] RibbonProperties(PKM p) => RibbonTables.GetOrAdd(p.GetType(), type => type.GetProperties()
        .Where(x => x.Name.StartsWith(RibbonInfo.PropertyPrefix, StringComparison.Ordinal) && x.CanWrite && (x.PropertyType == typeof(bool) || x.PropertyType == typeof(byte)))
        .ToArray());

    public static MoreData From(PKM p)
    {
        var d = new MoreData {
            Game = (int)p.Version, Language = p.Format >= 3 ? p.Language : 0, TrainerGender = p.OriginalTrainerGender,
            MetLocation = p.MetLocation, MetLevel = p.MetLevel, MetDate = GameDate.From(p.MetYear, p.MetMonth, p.MetDay),
            EggLocation = p.EggLocation, EggDate = GameDate.From(p.EggYear, p.EggMonth, p.EggDay),
            PokerusStrain = p.PokerusStrain, PokerusDays = p.PokerusDays, Relearn = p.RelearnMoves,
            PPUps = Enumerable.Range(0, MoveCount).Select(i => new PPBoost(p.GetMove(i), PPUpsIn(p, i))).ToArray(),
            Ribbons = ReadRibbons(p), Markings = ReadMarkings(p),
        };
        if (p is IContestStats c) d.Contest = [c.ContestCool, c.ContestBeauty, c.ContestCute, c.ContestSmart, c.ContestTough, c.ContestSheen];
        if (p is IHyperTrain h) d.HyperTraining = Enumerable.Range(0, StatCount).Select(h.IsHyperTrained).ToArray();
        if (p is ITeraType t) d.TeraType = (int)t.GetTeraType();
        if (p is IDynamaxLevel dynamax) d.DynamaxLevel = dynamax.DynamaxLevel;
        if (p is IGigantamax gigantamax) d.Gigantamax = gigantamax.CanGigantamax;
        if (p is IScaledSize size) { d.Height = size.HeightScalar; d.Weight = size.WeightScalar; }
        if (p is IScaledSize3 scaled) d.Scale = scaled.Scale;
        if (p is IAlpha alpha) d.Alpha = alpha.IsAlpha;
        if (p is IGanbaru g) d.GVs = Enumerable.Range(0, StatCount).Select(i => (int)g.GetGV(i)).ToArray();
        return d;
    }

    /// <summary>Everything except the PP Ups, which are compared with the moves they go with.</summary>
    public bool SameAs(MoreData other) => Game == other.Game && Language == other.Language && TrainerGender == other.TrainerGender
        && MetLocation == other.MetLocation && MetLevel == other.MetLevel && MetDate == other.MetDate
        && EggLocation == other.EggLocation && EggDate == other.EggDate
        && Ribbons.Count == other.Ribbons.Count && Ribbons.All(r => other.Ribbons.GetValueOrDefault(r.Key) == r.Value)
        && Contest.SequenceEqual(other.Contest) && PokerusStrain == other.PokerusStrain && PokerusDays == other.PokerusDays
        && Relearn.SequenceEqual(other.Relearn) && HyperTraining.SequenceEqual(other.HyperTraining)
        && TeraType == other.TeraType && DynamaxLevel == other.DynamaxLevel && Gigantamax == other.Gigantamax
        && Height == other.Height && Weight == other.Weight && Scale == other.Scale && Alpha == other.Alpha
        && GVs.SequenceEqual(other.GVs) && Markings.SequenceEqual(other.Markings);

    public bool HasRibbon(string property) => Ribbons.ContainsKey(property);

    public void SetRibbon(string property, int value)
    {
        if (value == 0) Ribbons.Remove(property);
        else Ribbons[property] = value;
    }

    internal static int PPUpsIn(PKM p, int slot) => slot switch {
        0 => p.Move1_PPUps, 1 => p.Move2_PPUps, 2 => p.Move3_PPUps, _ => p.Move4_PPUps,
    };

    internal static void SetPPUps(PKM p, int slot, int count)
    {
        switch (slot) {
            case 0: p.Move1_PPUps = count; break;
            case 1: p.Move2_PPUps = count; break;
            case 2: p.Move3_PPUps = count; break;
            case 3: p.Move4_PPUps = count; break;
        }
    }

    static Dictionary<string, int> ReadRibbons(PKM p)
    {
        var held = new Dictionary<string, int>();
        foreach (var ribbon in RibbonProperties(p))
        {
            int value = ribbon.GetValue(p) switch { true => 1, byte count => count, _ => 0 };
            if (value != 0) held[ribbon.Name] = value;
        }
        return held;
    }

    // Up to Gen 6 a marking is on or off. From Gen 7 it can also be blue or pink.
    static int[] ReadMarkings(PKM p) => p switch {
        IAppliedMarkings<bool> plain => Enumerable.Range(0, plain.MarkingCount).Select(i => plain.GetMarking(i) ? 1 : 0).ToArray(),
        IAppliedMarkings<MarkingColor> colored => Enumerable.Range(0, colored.MarkingCount).Select(i => (int)colored.GetMarking(i)).ToArray(),
        _ => [],
    };
}

/// <summary>What each game keeps of <see cref="MoreData"/>.</summary>
public sealed partial class GameRules
{
    const int DSLastYear = 2099, N3DSLastYear = 2050, SwitchLastYear = 2060, CrystalMaxMetLevel = 63, PickyPokerusStrains = 8, MaxPokerusStrainValue = 15;
    PKM? sample;

    /// <summary>An empty Pokémon of this game, to ask what its format stores.</summary>
    public PKM Sample => sample ??= Save.BlankPKM;
    public bool HasOriginGame => Generation >= 3;
    public bool LanguageSetsAlphabet => Generation == 3;
    // Of the Game Boy games, only Crystal writes down where a Pokémon was caught.
    public bool HasMetPlace => HasOriginGame || Version == GameVersion.C;
    public bool HasMetLevel => HasMetPlace;
    public bool HasTrainerGender => HasMetPlace;
    public bool HasDates => Generation >= 4;
    public bool HasEggData => Generation >= 4;
    // Let's Go carries the bytes but has no ribbons, and neither Game Boy generation has them.
    public bool HasRibbons => HasOriginGame && Context != EntityContext.Gen7b;
    public bool HasContest => Generation is >= 3 and <= 6 || Context == EntityContext.Gen8b;
    // These games have no Pokérus. Legends: Arceus can still receive a Pokémon that had it.
    public bool HasPokerus => Generation >= 2 && Context is not (EntityContext.Gen7b or EntityContext.Gen9 or EntityContext.Gen9a);
    public bool HasPPUps => Legal.IsPPUpAvailable(Sample);
    public bool HasRelearn => Generation >= 6;
    public bool HasHyperTraining => Sample is IHyperTrain;
    public bool HasTera => Context == EntityContext.Gen9;
    public bool HasDynamax => Context == EntityContext.Gen8;
    // Let's Go keeps these two as well, and shows the size they give.
    public bool HasSize => Sample is IScaledSize && (Generation >= 8 || Context == EntityContext.Gen7b);
    public bool HasScale => Sample is IScaledSize3;
    public bool HasAlpha => Sample is IAlpha;
    public bool HasGVs => Sample is IGanbaru;
    public bool HasMarkings => Sample is IAppliedMarkings;
    public int MarkingCount => (Sample as IAppliedMarkings)?.MarkingCount ?? 0;
    public bool HasColoredMarkings => Sample is IAppliedMarkings7;
    public int MaxMarking => HasColoredMarkings ? (int)MarkingColor.Pink : 1;
    public int MaxMetLevel => IsGameBoy ? CrystalMaxMetLevel : Experience.MaxLevel;
    public int MaxPokerusStrain => Generation == 2 ? PickyPokerusStrains : MaxPokerusStrainValue;
    public int LastYear => Generation <= 5 ? DSLastYear : Context is EntityContext.Gen6 or EntityContext.Gen7 ? N3DSLastYear : SwitchLastYear;
    public bool IsPossibleDate(GameDate date) => date.Exists && date.Year is >= GameDate.FirstYear && date.Year <= LastYear;
}

public static partial class Catalog
{
    const string MemoryContest = nameof(IRibbonSetMemory6.RibbonCountMemoryContest), MemoryBattle = nameof(IRibbonSetMemory6.RibbonCountMemoryBattle);
    const int MaxMemoryContest = 40, MaxMemoryBattle = 8, MaxContestCount = 4;
    static FilteredGameDataSource? filtered;
    static SaveFile? filteredSave;

    static GameDataSource Sources => sources ??= new GameDataSource(Names);

    static FilteredGameDataSource Filtered(GameRules rules)
    {
        if (filtered?.Source != Sources || filteredSave != rules.Save) { filtered = new(rules.Save, Sources); filteredSave = rules.Save; }
        return filtered;
    }

    // What the Pokémon already has stays on the list, even when it is not one of the game's.
    static List<Choice> KeepingCurrent(List<Choice> choices, int current)
    {
        if (choices.All(c => c.Id != current)) choices.Insert(0, new(current, T("{0} (actual)", current)));
        return choices;
    }

    /// <summary>The games a Pokémon of this save can come from. Those that have no places of their own are left out.</summary>
    public static List<Choice> GetGames(PokemonEntry e)
    {
        var rules = e.Rules;
        var reachable = GameUtil.GetVersionsWithinRange(rules.Save, rules.Context).ToHashSet();
        return KeepingCurrent(Sources.VersionDataSource.Where(g => g.Value != 0 && reachable.Contains((GameVersion)g.Value))
            .Where(g => Sources.Met.GetLocationList((GameVersion)g.Value, rules.Context).Count > 1)
            .Select(g => new Choice(g.Value, g.Text)).ToList(), e.Baseline.More.Game);
    }

    public static List<Choice> GetLanguages(PokemonEntry e) => KeepingCurrent(Sources.LanguageDataSource(e.Rules.Save.Generation, e.Rules.Context)
        .Select(l => new Choice(l.Value, l.Text)).ToList(), e.Baseline.More.Language);

    /// <summary>The places of the game of origin being edited, for the encounter or for the egg.</summary>
    public static List<Choice> GetLocations(PokemonEntry e, bool egg)
    {
        var d = e.Edit.More; var start = e.Baseline.More;
        var places = Sources.Met.GetLocationList((GameVersion)d.Game, e.Rules.Context, egg)
            .Where(l => l.Text.Trim().Length != 0).Select(l => new Choice(l.Value, l.Text)).ToList();
        return d.Game == start.Game ? KeepingCurrent(places, egg ? start.EggLocation : start.MetLocation) : places;
    }

    /// <summary>Selects the game of origin. A place that game doesn't have gives way to its first one, and comes back with the game.</summary>
    public static void SetGame(PokemonEntry e, int game)
    {
        var d = e.Edit.More; var start = e.Baseline.More;
        d.Game = game;
        int Keep(int place, int original, List<Choice> places) => places.Any(c => c.Id == place) ? place : game == start.Game ? original : places[0].Id;
        d.MetLocation = Keep(d.MetLocation, start.MetLocation, GetLocations(e, false));
        if (e.Rules.HasEggData) d.EggLocation = Keep(d.EggLocation, start.EggLocation, GetLocations(e, true));
    }

    /// <summary>Selects the language. A Pokémon with no nickname goes by its species' name in that language.</summary>
    public static void SetLanguage(PokemonEntry e, int language)
    {
        e.Edit.More.Language = language;
        if (e.Original.IsNicknamed || e.Edit.Nickname.Length != 0 && e.Edit.Nickname != e.Baseline.Nickname) return;
        e.Edit.Nickname = language == e.Baseline.More.Language ? e.Baseline.Nickname : "";
    }

    public static List<Choice> GetRelearnMoves(PokemonEntry e)
    {
        var moves = Filtered(e.Rules).Relearn.Select(m => (ushort)m.Value).Concat(e.Baseline.More.Relearn).Where(m => m != 0).Distinct();
        return new[] { new Choice(0, T("— Sin movimiento —")) }.Concat(moves
            .OrderBy(m => Names.movelist[m], StringComparer.CurrentCultureIgnoreCase).Select(m => new Choice(m, Names.movelist[m]))).ToList();
    }

    public static List<Choice> GetTeraTypes() => Enumerable.Range(0, TeraTypeUtil.MaxType + 1).Select(i => new Choice(i, Names.Types[i]))
        .Append(new(TeraTypeUtil.Stellar, Names.Types[TeraTypeUtil.StellarTypeDisplayStringIndex])).ToList();

    public static List<RibbonChoice> GetRibbons(GameRules rules) => !rules.HasRibbons ? []
        : MoreData.RibbonProperties(rules.Sample).Select(x => new RibbonChoice(x.Name, RibbonName(x.Name), MaxRibbon(x))).ToList();

    static string RibbonName(string property) => Names.Ribbons.GetNameSafe(property, out var name) ? name : property[RibbonInfo.PropertyPrefix.Length..];

    static int MaxRibbon(PropertyInfo ribbon) => ribbon.PropertyType == typeof(bool) ? 1
        : ribbon.Name == MemoryContest ? MaxMemoryContest : ribbon.Name == MemoryBattle ? MaxMemoryBattle : MaxContestCount;

    /// <summary>How high a GV can go: the IV already gives the first steps.</summary>
    public static int MaxGV(PokemonEntry e, int stat) => GanbaruExtensions.TrueMax - GanbaruExtensions.GetBias(e.Edit.IVs[stat]);

    public static bool CanEditTera(PokemonEntry e) => TeraTypeUtil.CanChangeTeraType(e.Original.Species);
    public static bool CanHaveDynamax(PokemonEntry e) => e.Original is IDynamaxLevel d && d.CanHaveDynamaxLevel(e.Original);
    public static bool CanGigantamax(PokemonEntry e) => Gigantamax.CanToggle(e.Original.Species, e.Edit.Form) || e.Baseline.More.Gigantamax;

    static void ApplyOrigin(PKM p, PokemonEntry entry)
    {
        var d = entry.Edit.More; var start = entry.Baseline.More;
        bool gameChanged = d.Game != start.Game;
        if (gameChanged) p.Version = (GameVersion)d.Game;
        // Gen 4 files a place according to the game, so a new game means filing both again.
        if (gameChanged || d.MetLocation != start.MetLocation) p.MetLocation = (ushort)d.MetLocation;
        if (gameChanged && entry.Rules.HasEggData || d.EggLocation != start.EggLocation) p.EggLocation = (ushort)d.EggLocation;
        if (d.MetLevel != start.MetLevel) p.MetLevel = (byte)d.MetLevel;
        if (d.TrainerGender != start.TrainerGender) p.OriginalTrainerGender = (byte)d.TrainerGender;
        if (d.MetDate != start.MetDate) { p.MetYear = (byte)(d.MetDate.Year - GameDate.FirstYear); p.MetMonth = (byte)d.MetDate.Month; p.MetDay = (byte)d.MetDate.Day; }
        if (d.EggDate != start.EggDate) { p.EggYear = (byte)(d.EggDate.Year - GameDate.FirstYear); p.EggMonth = (byte)d.EggDate.Month; p.EggDay = (byte)d.EggDate.Day; }
        if (d.Language == start.Language) return;
        // In Gen 3 the language decides the alphabet names are read with, so they are written again in the new one.
        string nickname = p.Nickname, trainer = p.OriginalTrainerName;
        p.Language = d.Language;
        if (!entry.Rules.LanguageSetsAlphabet) return;
        p.Nickname = nickname; p.OriginalTrainerName = trainer;
    }

    static void ApplyRibbons(PKM p, MoreData d, MoreData start)
    {
        if (d.Ribbons.Count == start.Ribbons.Count && d.Ribbons.All(r => start.Ribbons.GetValueOrDefault(r.Key) == r.Value)) return;
        foreach (var ribbon in MoreData.RibbonProperties(p))
        {
            int value = d.Ribbons.GetValueOrDefault(ribbon.Name);
            if (value != start.Ribbons.GetValueOrDefault(ribbon.Name))
                ribbon.SetValue(p, ribbon.PropertyType == typeof(bool) ? value != 0 : (byte)value);
        }
        // Up to Gen 7 a memory ribbon is a count and a flag, and only the newer formats keep them together.
        if (p is IRibbonSetMemory6 memory)
        {
            if (d.Ribbons.GetValueOrDefault(MemoryContest) != start.Ribbons.GetValueOrDefault(MemoryContest)) memory.HasContestMemoryRibbon = memory.RibbonCountMemoryContest != 0;
            if (d.Ribbons.GetValueOrDefault(MemoryBattle) != start.Ribbons.GetValueOrDefault(MemoryBattle)) memory.HasBattleMemoryRibbon = memory.RibbonCountMemoryBattle != 0;
        }
        // A Pokémon can't show off a ribbon it no longer has.
        if (p is IRibbonSetAffixed { AffixedRibbon: not AffixedRibbon.None } shown && p is IRibbonIndex index && !index.GetRibbon(shown.AffixedRibbon))
            shown.AffixedRibbon = AffixedRibbon.None;
    }

    static void ApplyMarkings(PKM p, MoreData d, MoreData start)
    {
        for (int i = 0; i < d.Markings.Length; i++)
        {
            if (d.Markings[i] == start.Markings[i]) continue;
            if (p is IAppliedMarkings<bool> plain) plain.SetMarking(i, d.Markings[i] != 0);
            else if (p is IAppliedMarkings<MarkingColor> colored) colored.SetMarking(i, (MarkingColor)d.Markings[i]);
        }
    }

    // After the moves, because a new move has just lost the PP Ups of the one it replaced.
    static void ApplyMore(PKM p, PokemonEntry entry)
    {
        var d = entry.Edit.More; var start = entry.Baseline.More;
        ApplyRibbons(p, d, start);
        ApplyMarkings(p, d, start);
        if (p is IContestStats c && !d.Contest.SequenceEqual(start.Contest))
        {
            c.ContestCool = (byte)d.Contest[0]; c.ContestBeauty = (byte)d.Contest[1]; c.ContestCute = (byte)d.Contest[2];
            c.ContestSmart = (byte)d.Contest[3]; c.ContestTough = (byte)d.Contest[4]; c.ContestSheen = (byte)d.Contest[5];
        }
        if (d.PokerusStrain != start.PokerusStrain) p.PokerusStrain = d.PokerusStrain;
        if (d.PokerusDays != start.PokerusDays) p.PokerusDays = d.PokerusDays;
        for (int i = 0; i < MoreData.MoveCount; i++)
        {
            int ups = entry.Edit.PPUpsOf(i);
            if (ups != MoreData.PPUpsIn(p, i)) { MoreData.SetPPUps(p, i, ups); p.HealPPIndex(i); }
            if (d.Relearn[i] != start.Relearn[i]) p.SetRelearnMove(i, d.Relearn[i]);
        }
        if (p is IHyperTrain h)
            for (int i = 0; i < MoreData.StatCount; i++) if (h.IsHyperTrained(i) != d.HyperTraining[i]) h.HyperTrainInvert(i);
        if (p is IGanbaru g)
            for (int i = 0; i < MoreData.StatCount; i++) if (d.GVs[i] != start.GVs[i]) g.SetGV(i, (byte)d.GVs[i]);
        if (p is ITeraType t && d.TeraType != start.TeraType) t.SetTeraType((MoveType)d.TeraType);
        if (p is IDynamaxLevel dynamax && d.DynamaxLevel != start.DynamaxLevel) dynamax.DynamaxLevel = (byte)d.DynamaxLevel;
        if (p is IGigantamax gigantamax && d.Gigantamax != start.Gigantamax) gigantamax.CanGigantamax = d.Gigantamax;
        if (p is IAlpha alpha && d.Alpha != start.Alpha) alpha.IsAlpha = d.Alpha;
        if (p is IScaledSize3 scaled && d.Scale != start.Scale) scaled.Scale = (byte)d.Scale;
        if (p is not IScaledSize size || d.Height == start.Height && d.Weight == start.Weight) return;
        size.HeightScalar = (byte)d.Height; size.WeightScalar = (byte)d.Weight;
        if (p is IScaledSizeValue absolute) { absolute.ResetHeight(); absolute.ResetWeight(); }
    }

    // A change no control offers in this game can only come from a stale or hand-made draft.
    static bool ChangesWhatGameLacks(GameRules rules, MoreData d, MoreData start) =>
        !rules.HasOriginGame && (d.Game != start.Game || d.Language != start.Language)
        || !rules.HasMetPlace && (d.MetLocation != start.MetLocation || d.MetLevel != start.MetLevel || d.TrainerGender != start.TrainerGender)
        || !rules.HasDates && (d.MetDate != start.MetDate || d.EggDate != start.EggDate)
        || !rules.HasEggData && d.EggLocation != start.EggLocation
        || !rules.HasContest && !d.Contest.SequenceEqual(start.Contest)
        || !rules.HasPokerus && (d.PokerusStrain != start.PokerusStrain || d.PokerusDays != start.PokerusDays)
        || !rules.HasPPUps && !d.PPUps.SequenceEqual(start.PPUps)
        || !rules.HasRelearn && !d.Relearn.SequenceEqual(start.Relearn)
        || !rules.HasHyperTraining && !d.HyperTraining.SequenceEqual(start.HyperTraining)
        || !rules.HasTera && d.TeraType != start.TeraType
        || !rules.HasDynamax && (d.DynamaxLevel != start.DynamaxLevel || d.Gigantamax != start.Gigantamax)
        || !rules.HasSize && (d.Height != start.Height || d.Weight != start.Weight)
        || !rules.HasScale && d.Scale != start.Scale
        || !rules.HasAlpha && d.Alpha != start.Alpha
        || !rules.HasGVs && !d.GVs.SequenceEqual(start.GVs)
        || !rules.HasMarkings && !d.Markings.SequenceEqual(start.Markings);

    static string? ValidateOrigin(PokemonEntry e)
    {
        var d = e.Edit.More; var start = e.Baseline.More; var rules = e.Rules;
        bool gameChanged = d.Game != start.Game;
        if (gameChanged && GetGames(e).All(c => c.Id != d.Game)) return T("Un Pokémon de esta partida no puede venir de ese juego.");
        if (d.Language != start.Language && GetLanguages(e).All(c => c.Id != d.Language)) return T("Ese idioma no existe en este juego.");
        if ((gameChanged || d.MetLocation != start.MetLocation) && GetLocations(e, false).All(c => c.Id != d.MetLocation)) return T("Ese lugar no existe en el juego de origen.");
        if (rules.HasEggData && (gameChanged || d.EggLocation != start.EggLocation) && GetLocations(e, true).All(c => c.Id != d.EggLocation)) return T("Ese lugar no existe en el juego de origen.");
        if (d.MetLevel != start.MetLevel && (d.MetLevel < 0 || d.MetLevel > rules.MaxMetLevel)) return T("El nivel de encuentro debe estar entre 0 y {0}.", rules.MaxMetLevel);
        if (d.TrainerGender != start.TrainerGender && d.TrainerGender is not (0 or 1)) return T("Selecciona el género del entrenador.");
        if (d.MetDate != start.MetDate && !rules.IsPossibleDate(d.MetDate)) return T("La fecha {0} no existe en este juego. Elige un día real entre {1} y {2}.", d.MetDate, GameDate.FirstYear, rules.LastYear);
        return d.EggDate != start.EggDate && !rules.IsPossibleDate(d.EggDate) ? T("La fecha {0} no existe en este juego. Elige un día real entre {1} y {2}.", d.EggDate, GameDate.FirstYear, rules.LastYear) : null;
    }

    static string? ValidateRibbons(PokemonEntry e)
    {
        var d = e.Edit.More; var start = e.Baseline.More;
        var known = GetRibbons(e.Rules).ToDictionary(r => r.Property);
        foreach (var (property, value) in d.Ribbons.Where(r => start.Ribbons.GetValueOrDefault(r.Key) != r.Value))
        {
            if (!known.TryGetValue(property, out var ribbon)) return T("Ese Pokémon lleva una cinta que no existe en este juego.");
            if (value < 0 || value > ribbon.Max) return T("{0}: la cantidad debe estar entre 0 y {1}.", ribbon.Label, ribbon.Max);
        }
        return null;
    }

    static string? ValidateMoves(PokemonEntry e)
    {
        var d = e.Edit.More;
        for (int i = 0; i < MoreData.MoveCount; i++)
        {
            int ups = e.Edit.PPUpsOf(i);
            if (ups == e.Baseline.PPUpsOf(i)) continue;
            if (ups is < 0 or > MoreData.MaxPPUps) return T("Los PP extra de un movimiento van de 0 a {0}.", MoreData.MaxPPUps);
            if (ups != 0 && !Legal.IsPPUpAvailable(e.Edit.Moves[i])) return T("{0} no admite PP extra.", Names.movelist[e.Edit.Moves[i]]);
        }
        if (d.Relearn.SequenceEqual(e.Baseline.More.Relearn)) return null;
        var set = d.Relearn.Where(m => m != 0).ToList();
        if (set.Count != set.Distinct().Count()) return T("No se puede repetir un movimiento recordable.");
        if (Enumerable.Range(1, MoreData.MoveCount - 1).Any(i => d.Relearn[i] != 0 && d.Relearn[i - 1] == 0)) return T("Coloca los movimientos recordables en orden, sin espacios vacíos entre ellos.");
        var allowed = GetRelearnMoves(e).Select(m => (ushort)m.Id).ToHashSet();
        return d.Relearn.All(allowed.Contains) ? null : T("Ese movimiento recordable no existe en este juego.");
    }

    static string? ValidateBattleData(PokemonEntry e)
    {
        var d = e.Edit.More; var start = e.Baseline.More; var rules = e.Rules;
        if (!d.Contest.SequenceEqual(start.Contest) && d.Contest.Any(v => v is < 0 or > MoreData.MaxContest)) return T("Cada estadística de concurso debe estar entre 0 y {0}.", MoreData.MaxContest);
        if (d.PokerusStrain != start.PokerusStrain || d.PokerusDays != start.PokerusDays)
        {
            if (d.PokerusStrain < 0 || d.PokerusStrain > rules.MaxPokerusStrain) return T("La cepa del Pokérus debe estar entre 0 y {0}.", rules.MaxPokerusStrain);
            if (!Pokerus.IsDurationValid(d.PokerusStrain, d.PokerusDays, out int days)) return T("Esa cepa del Pokérus dura {0} días como máximo.", days);
            if (d.PokerusStrain == 0 && d.PokerusDays != 0) return T("Un Pokémon sin cepa de Pokérus no puede tener días de contagio.");
        }
        if (d.TeraType != start.TeraType)
        {
            if (!TeraTypeUtil.IsOverrideValid((byte)d.TeraType)) return T("Ese teratipo no existe.");
            if (!CanEditTera(e)) return T("El teratipo de {0} no se puede cambiar.", e.SpeciesName);
        }
        if (d.DynamaxLevel != start.DynamaxLevel)
        {
            if (d.DynamaxLevel is < 0 or > MoreData.MaxDynamaxLevel) return T("El nivel Dinamax debe estar entre 0 y {0}.", MoreData.MaxDynamaxLevel);
            if (d.DynamaxLevel != 0 && !CanHaveDynamax(e)) return T("{0} no puede dinamaxizarse.", e.SpeciesName);
        }
        if (d.Gigantamax && !start.Gigantamax && !CanGigantamax(e)) return T("{0} no tiene forma Gigamax.", e.SpeciesName);
        if (new[] { d.Height, d.Weight, d.Scale }.Any(v => v is < 0 or > MoreData.MaxScalar)) return T("Altura, peso y escala deben estar entre 0 y {0}.", MoreData.MaxScalar);
        if (!d.GVs.SequenceEqual(start.GVs) && Enumerable.Range(0, MoreData.StatCount).Any(i => d.GVs[i] != start.GVs[i] && (d.GVs[i] < 0 || d.GVs[i] > MaxGV(e, i))))
            return T("Con ese IV, ese GV no se puede alcanzar.");
        if (d.Markings.Length != start.Markings.Length || d.Markings.Where((v, i) => v != start.Markings[i]).Any(v => v < 0 || v > rules.MaxMarking)) return T("Esa marca no existe en este juego.");
        return null;
    }

    static string? ValidateMore(PokemonEntry e)
    {
        var d = e.Edit.More; var start = e.Baseline.More;
        bool boosted = !d.PPUps.SequenceEqual(start.PPUps);
        if (d.SameAs(start) && !boosted) return null;
        if (e.Original.IsEgg) return T("Un huevo no guarda estos datos hasta que eclosiona.");
        if (ChangesWhatGameLacks(e.Rules, d, start)) return T("Este juego no guarda ese dato.");
        return ValidateOrigin(e) ?? ValidateRibbons(e) ?? ValidateMoves(e) ?? ValidateBattleData(e);
    }
}
