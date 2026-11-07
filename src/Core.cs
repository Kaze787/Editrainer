using PKHeX.Core;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using static Editrainer.Strings;

namespace Editrainer;

public record Choice(int Id, string Label)
{
    public override string ToString() => Label;
}
public record EvolutionTarget(ushort Species, byte Form, string Label)
{
    public override string ToString() => Label;
}

public record FormChoice(byte Form, uint Argument, string Label)
{
    public override string ToString() => Label;
}

public record AbilityChoice(int Slot, int Ability, string Label)
{
    public override string ToString() => Label;
}

public sealed record BagItem(ushort Id, string Name, InventoryType Pouch, string Category, int OriginalCount, int MaxCount, bool IsHeld)
{
    public bool IsExperience => Category == GameRules.ExperienceCategory;
}

/// <summary>PKHeX's verdict on a Pokémon as it would be saved.</summary>
public sealed record LegalityCheck(bool Valid, string Report);

public sealed class Draft
{
    public int[] IVs { get; set; } = new int[6];
    public int[] EVs { get; set; } = new int[6];
    public Nature Nature { get; set; }
    public bool IsShiny { get; set; }
    public int HeldItem { get; set; }
    public int Friendship { get; set; }
    public ushort[] Moves { get; set; } = new ushort[4];
    public byte Form { get; set; }
    public uint FormArgument { get; set; }
    public int Ability { get; set; }
    public int AbilitySlot { get; set; }
    public int Level { get; set; }
    public string Nickname { get; set; } = "";
    public string Trainer { get; set; } = "";
    public int Ball { get; set; }
    public static Draft From(PKM p)
    {
        var d = new Draft { Nature = p.StatAlignment, IsShiny = p.IsShiny, HeldItem = p.HeldItem, Friendship = p.CurrentFriendship,
            Form = p.Form, FormArgument = (p as IFormArgument)?.FormArgument ?? 0,
            Ability = p.Ability, AbilitySlot = Math.Clamp(p.AbilityNumber >> 1, 0, 2),
            Level = p.CurrentLevel, Nickname = p.Nickname, Trainer = p.OriginalTrainerName, Ball = p.Ball };
        p.GetIVs(d.IVs); p.GetMoves(d.Moves);
        if (p is IAwakened a) d.EVs = [a.AV_HP, a.AV_ATK, a.AV_DEF, a.AV_SPE, a.AV_SPA, a.AV_SPD];
        else p.GetEVs(d.EVs);
        return d;
    }
    public bool SameAs(Draft other) => Nature == other.Nature && IsShiny == other.IsShiny
        && HeldItem == other.HeldItem && Friendship == other.Friendship && Form == other.Form
        && FormArgument == other.FormArgument && Ability == other.Ability && AbilitySlot == other.AbilitySlot
        && Level == other.Level && Nickname == other.Nickname && Trainer == other.Trainer && Ball == other.Ball && IVs.SequenceEqual(other.IVs)
        && EVs.SequenceEqual(other.EVs) && Moves.SequenceEqual(other.Moves);
}

/// <summary>What each game allows: its limits, its data tables and the names it uses.</summary>
public sealed class GameRules
{
    public const string ExperienceCategory = "Experiencia";
    const int ShinyDV = 10, GameBoyUnownLetters = 26, UnownLetters = 28, UnownDVBits = 6, UnownDVStep = 10;
    const ushort GriseousOrb = 112, AdamantCrystal = 1777, LustrousGlobe = 1778, GriseousCore = 1779;
    readonly HashSet<ushort> heldItems;
    readonly string[] spanishItemNames;
    string[] itemNames;
    ILearnSource? learnSource;

    public GameRules(SaveFile save)
    {
        Save = save;
        Version = SingleVersion(save);
        itemNames = Catalog.Names.GetItemStrings(save.Context, save.Version);
        spanishItemNames = GameInfo.GetStrings("es").GetItemStrings(save.Context, save.Version);
        heldItems = save is SAV9ZA ? ItemStorage9ZA.GetAllHeld().ToHashSet() : save.HeldItems.ToArray().ToHashSet();
    }

    public SaveFile Save { get; }
    public GameVersion Version { get; }
    public IPersonalTable Personal => Save.Personal;
    public EntityContext Context => Save.Context;
    public int Generation => Save.Generation;
    public bool IsZA => Save is SAV9ZA;
    public bool IsGameBoy => Generation <= 2;
    public bool HasNature => Generation >= 3;
    public bool HasHeldItems => Generation >= 2;
    public bool HasFriendship => Generation >= 2;
    public bool HasShiny => Generation >= 2;
    public bool HasAwakening => Context == EntityContext.Gen7b;
    public bool SharesSpecial => IsGameBoy;
    public bool HasPidNature => Generation is 3 or 4;
    public bool HasFormInPid => Generation == 3;
    public bool HasStoredForm => Generation >= 4;
    public bool HasAbility => Generation >= 3;
    public bool HasBall => Generation >= 3;
    // Let's Go and both Legends keep the ability in each Pokémon, but battles ignore it.
    public bool UsesAbility => HasAbility && Context is not (EntityContext.Gen7b or EntityContext.Gen8a or EntityContext.Gen9a);
    public int MaxIV => IsGameBoy ? 15 : 31;
    // Brilliant Diamond and Shining Pearl cap money lower than PKHeX's general figure for them.
    public long MaxMoney => Save is SAV8BS ? 999_999 : Save.MaxMoney;
    public int MaxEV => IsGameBoy ? ushort.MaxValue : HasAwakening ? 200 : Generation <= 5 ? 255 : 252;
    public int MaxEVTotal => IsGameBoy || HasAwakening ? 0 : 510;
    // The oldest games keep 20 in a box and Let's Go pages its storage in 25s: neither fits six across.
    public int BoxColumns => Save.BoxSlotCount % 6 == 0 ? 6 : 5;
    public string IVName => IsGameBoy ? T("DV") : T("IV");
    public string EVName => HasAwakening ? T("AV") : T("EV");
    public string ShortName => T(GameTitle(Save.Version == GameVersion.Any ? Version : Save.Version));
    public string FullName => T("Pokémon") + " " + (IsZA ? T("Leyendas: Z-A") : ShortName);
    public void RefreshNames() => itemNames = Catalog.Names.GetItemStrings(Save.Context, Save.Version);
    // A box nobody named reads as blank, or as "Box 1" whatever the game's language.
    public string BoxName(int box)
    {
        var name = (Save as IBoxDetailName)?.GetBoxName(box)?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.Equals($"Box {box + 1}", StringComparison.OrdinalIgnoreCase) ? T("Caja {0}", box + 1) : name;
    }
    public string StatName(int stat) => Generation == 1 && stat == 4 ? T("Especial") : Catalog.Stats[stat];
    public bool IsStatEditable(int stat, bool iv) => !(SharesSpecial && stat == 5) && !(IsGameBoy && iv && stat == 0);
    public bool CanHold(int id) => id == 0 || id is > 0 and <= ushort.MaxValue && heldItems.Contains((ushort)id);
    public string ItemName(int id) => id >= 0 && id < itemNames.Length ? itemNames[id] : "";
    public ILearnSource LearnSource => learnSource ??= GameData.GetLearnSource(Version);
    // PKHeX 26.10.18 overruns its special tutor data for these three; every other source works.
    public MoveSourceType MoveSources => Version is GameVersion.D or GameVersion.P or GameVersion.Pt
        ? MoveSourceType.All & ~MoveSourceType.SpecialTutor : MoveSourceType.All;

    public string PouchName(InventoryType type) => type switch {
        InventoryType.Medicine => "Medicina", InventoryType.Balls => "Poké Balls", InventoryType.Berries => "Bayas",
        InventoryType.TMHMs => "MT", InventoryType.MegaStones => "Megapiedras", InventoryType.Treasure => "Tesoros",
        InventoryType.BattleItems => "Combate", InventoryType.MailItems => "Cartas", InventoryType.ZCrystals => "Cristales Z",
        InventoryType.Candy => "Caramelos", InventoryType.Ingredients => "Ingredientes", _ => "Objetos",
    };

    // Read from the Spanish names whatever the language on screen, so a category never depends on it.
    public string CategoryOf(ushort id, InventoryType type)
    {
        string name = id < spanishItemNames.Length ? spanishItemNames[id] : "";
        bool experience = IsZA ? id is 50 or >= 1124 and <= 1128
            : name.StartsWith("Caramelo Raro", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Caramelo Exp", StringComparison.OrdinalIgnoreCase);
        return experience ? ExperienceCategory : PouchName(type);
    }

    /// <summary>A Game Boy Pokémon is shiny because of its DVs, so toggling it means rewriting them.</summary>
    public static void SetShinyDVs(Draft d, bool shiny)
    {
        if (shiny)
        {
            d.IVs[1] = (d.IVs[1] & 2) != 0 ? d.IVs[1] : d.IVs[1] | 2;
            d.IVs[2] = d.IVs[3] = d.IVs[4] = d.IVs[5] = ShinyDV;
        }
        else d.IVs[3] = ShinyDV + 1;
    }

    /// <summary>A Game Boy Unown's letter comes from the middle bits of its four DVs.</summary>
    public static void SetUnownDVs(Draft d, byte letter)
    {
        int bits = letter * UnownDVStep;
        d.IVs[1] = d.IVs[1] & ~UnownDVBits | bits >> 5 & UnownDVBits;
        d.IVs[2] = d.IVs[2] & ~UnownDVBits | bits >> 3 & UnownDVBits;
        d.IVs[3] = d.IVs[3] & ~UnownDVBits | bits >> 1 & UnownDVBits;
        d.IVs[4] = d.IVs[5] = d.IVs[4] & ~UnownDVBits | bits << 1 & UnownDVBits;
    }

    /// <summary>Raising DVs also rewrites what they encode, so that is put back afterwards.</summary>
    public static void KeepDVLooks(Draft d, ushort species)
    {
        if (d.IsShiny) SetShinyDVs(d, true);
        else if (species == (ushort)Species.Unown) SetUnownDVs(d, d.Form);
    }

    /// <summary>Whether a form can stay on a stored Pokémon. Up to Gen 3 only Unown keeps one, inside its DVs or PID.</summary>
    public bool CanChooseForm(ushort species, byte form) => Generation switch {
        1 => false,
        2 => species == (ushort)Species.Unown && form < GameBoyUnownLetters,
        3 => species == (ushort)Species.Unown && form < UnownLetters,
        _ => Personal.IsPresentInGame(species, form) && !IsTiedForm(species, form)
            && !FormInfo.IsBattleOnlyForm(species, form, (byte)Generation) && !FormInfo.IsFusedForm(species, form, (byte)Generation)
            && !FormInfo.IsTotemForm(species, form, Context) && !FormInfo.IsLordForm(species, form, Context),
    };

    // These belong to the story's own Pokémon, or follow the gender, which this editor does not change.
    static bool IsTiedForm(ushort species, byte form) => (Species)species switch {
        Species.Pikachu => form == 8,
        Species.Eevee => form == 1,
        Species.Koraidon or Species.Miraidon => form != 0,
        Species.Meowstic or Species.Indeedee or Species.Basculegion or Species.Oinkologne => true,
        _ => false,
    };

    /// <summary>The item a form lasts only while holding. Legends: Arceus has no held items.</summary>
    public ushort ItemForForm(ushort species, byte form) => Context == EntityContext.Gen8a ? (ushort)0 : (Species)species switch {
        Species.Arceus => FormItem.GetItemArceus(form, (byte)Generation),
        Species.Giratina when form == 1 => Generation >= 9 ? GriseousCore : GriseousOrb,
        Species.Dialga when form == 1 => AdamantCrystal,
        Species.Palkia when form == 1 => LustrousGlobe,
        _ => FormItem.GetItem(species, form),
    };

    // Tables exist per game, but a save may only know its pair (say, Ruby or Sapphire).
    static GameVersion SingleVersion(SaveFile save) => save.Version switch {
        GameVersion.Any => save switch {
            SAV3RS => GameVersion.R, SAV3E => GameVersion.E, SAV3FRLG => GameVersion.FR,
            SAV4DP => GameVersion.D, SAV4Pt => GameVersion.Pt, SAV4HGSS => GameVersion.HG, _ => save.Version,
        },
        GameVersion.RB or GameVersion.RBY => GameVersion.RD, GameVersion.GS or GameVersion.GSC => GameVersion.GD,
        GameVersion.RS => GameVersion.R, GameVersion.FRLG => GameVersion.FR,
        GameVersion.DP => GameVersion.D, GameVersion.HGSS => GameVersion.HG,
        GameVersion.BW => GameVersion.B, GameVersion.B2W2 => GameVersion.B2,
        GameVersion.XY => GameVersion.X, GameVersion.ORAS => GameVersion.OR,
        GameVersion.SM => GameVersion.SN, GameVersion.USUM => GameVersion.US,
        GameVersion.GG => GameVersion.GP, GameVersion.SWSH => GameVersion.SW,
        GameVersion.BDSP => GameVersion.BD, GameVersion.SV => GameVersion.SL, _ => save.Version,
    };

    public static string GameTitle(GameVersion v) => v switch {
        GameVersion.RD => "Rojo", GameVersion.GN => "Verde", GameVersion.BU => "Azul", GameVersion.YW => "Amarillo",
        GameVersion.RB or GameVersion.RBY => "Rojo / Azul", GameVersion.GD => "Oro", GameVersion.SI => "Plata", GameVersion.C => "Cristal",
        GameVersion.GS or GameVersion.GSC => "Oro / Plata", GameVersion.R => "Rubí", GameVersion.S => "Zafiro", GameVersion.RS => "Rubí / Zafiro",
        GameVersion.E => "Esmeralda", GameVersion.FR => "Rojo Fuego", GameVersion.LG => "Verde Hoja", GameVersion.FRLG => "Rojo Fuego / Verde Hoja",
        GameVersion.CXD => "Colosseum / XD", GameVersion.COLO => "Colosseum", GameVersion.XD => "XD",
        GameVersion.D => "Diamante", GameVersion.P => "Perla", GameVersion.Pt => "Platino", GameVersion.DP => "Diamante / Perla",
        GameVersion.HG => "HeartGold", GameVersion.SS => "SoulSilver", GameVersion.HGSS => "HeartGold / SoulSilver",
        GameVersion.B => "Negro", GameVersion.W => "Blanco", GameVersion.BW => "Negro / Blanco",
        GameVersion.B2 => "Negro 2", GameVersion.W2 => "Blanco 2", GameVersion.B2W2 => "Negro 2 / Blanco 2",
        GameVersion.X => "X", GameVersion.Y => "Y", GameVersion.XY => "X / Y",
        GameVersion.OR => "Rubí Omega", GameVersion.AS => "Zafiro Alfa", GameVersion.ORAS => "Rubí Omega / Zafiro Alfa",
        GameVersion.SN => "Sol", GameVersion.MN => "Luna", GameVersion.SM => "Sol / Luna",
        GameVersion.US => "Ultrasol", GameVersion.UM => "Ultraluna", GameVersion.USUM => "Ultrasol / Ultraluna",
        GameVersion.GP => "Let's Go Pikachu", GameVersion.GE => "Let's Go Eevee", GameVersion.GG => "Let's Go",
        GameVersion.SW => "Espada", GameVersion.SH => "Escudo", GameVersion.SWSH => "Espada / Escudo",
        GameVersion.BD => "Diamante Brillante", GameVersion.SP => "Perla Reluciente", GameVersion.BDSP => "Diamante Brillante / Perla Reluciente",
        GameVersion.PLA => "Leyendas: Arceus", GameVersion.SL => "Escarlata", GameVersion.VL => "Púrpura", GameVersion.SV => "Escarlata / Púrpura",
        GameVersion.ZA => "Z-A", _ => "",
    };
}

public sealed class PokemonEntry(GameRules rules, bool party, int index, PKM pokemon)
{
    public GameRules Rules { get; } = rules;
    public bool IsParty { get; } = party;
    public int Index { get; } = index;
    public PKM Original { get; } = pokemon;
    public Draft Baseline { get; } = Draft.From(pokemon);
    public Draft Edit { get; set; } = Draft.From(pokemon);
    public bool Dirty => !Edit.SameAs(Baseline);
    public string SpeciesName => Catalog.Names.specieslist[Original.Species];
    public string Location => IsParty ? T("Equipo · {0}", Index + 1) : T("Caja {0} · {1}", Index / Rules.Save.BoxSlotCount + 1, Index % Rules.Save.BoxSlotCount + 1);
    public string DisplayName => string.IsNullOrWhiteSpace(Edit.Nickname) ? SpeciesName : Edit.Nickname;
    public string ImageVariant => Catalog.ImageVariant(Rules, Original.Species, Edit.Form, Edit.FormArgument);
    public override string ToString() => $"{DisplayName} {SpeciesName} {Location}";
    public PKM Preview() => Catalog.Apply(this);
}

public static class Catalog
{
    const int Gen1TypeOffset = 11, FirstGen1SpecialType = 20, LongestMoveTable = 1024;
    public static GameStrings Names { get; private set; } = GameInfo.GetStrings(Strings.Current.GameData);
    public static string[] Stats => [T("PS"), T("Ataque"), T("Defensa"), T("Velocidad"), T("At. Especial"), T("Def. Especial")];
    static readonly string[] Genders = ["♂", "♀", "-"];
    static string[] AbilitySlots => [T("principal"), T("secundaria"), T("oculta")];
    static GameDataSource? sources;
    static string[] AlcremieCreams => [T("Crema de Vainilla"), T("Crema Rosa"), T("Crema de Té"), T("Crema de Menta"), T("Crema de Limón"), T("Crema Salada"), T("Mezcla Rosa"), T("Mezcla Caramelo"), T("Mezcla Tres Sabores")];
    static string[] AlcremieSweets => [T("Fresa"), T("Baya"), T("Corazón"), T("Estrella"), T("Trébol"), T("Flor"), T("Lazo")];
    public static void UseLanguage(string code)
    {
        Strings.Use(code);
        Names = GameInfo.GetStrings(Strings.Current.GameData);
        sources = null;
    }

    public static List<Choice> GetBalls(PokemonEntry e)
    {
        sources ??= new GameDataSource(Names);
        var balls = sources.BallDataSource.Where(b => b.Value > 0 && b.Value <= e.Rules.Save.MaxBallID).Select(b => new Choice(b.Value, b.Text)).ToList();
        if (balls.All(b => b.Id != e.Baseline.Ball)) balls.Insert(0, new Choice(e.Baseline.Ball, T("{0} (actual)", e.Baseline.Ball)));
        return balls;
    }

    public static LegalityCheck CheckLegality(PokemonEntry e)
    {
        var analysis = new LegalityAnalysis(e.Preview(), e.Rules.Personal, e.IsParty ? StorageSlotType.Party : StorageSlotType.Box);
        return new(analysis.Valid, analysis.Report(Strings.Current.GameData));
    }

    public static string ShowdownText(PokemonEntry e) => new ShowdownSet(e.Preview()).Text;

    public static IEnumerable<int> SpeciesIds => Enumerable.Range(1, PersonalTable.ZA.MaxSpeciesID)
        .Where(i => PersonalTable.ZA[(ushort)i, 0].IsPresentInGame);

    // Game Boy data numbers the special types from 20.
    public static string TypeName(int type) => Names.Types[type >= FirstGen1SpecialType ? type - Gen1TypeOffset : type];

    public static string FormName(GameRules rules, ushort species, byte form)
    {
        if (species == (ushort)Species.Alcremie && form < AlcremieCreams.Length) return AlcremieCreams[form];
        var names = FormConverter.GetFormList(species, Names.Types, Names.forms, Genders, rules.Context);
        var name = form < names.Length ? names[form] : "";
        return species == (ushort)Species.Minior ? name.Replace(T("N-"), T("Núcleo ")) : name;
    }

    /// <summary>What names a form's picture. Pikachu and Arceus number their forms differently in older games.</summary>
    public static string ImageVariant(GameRules rules, ushort species, byte form, uint argument) => (Species)species switch {
        Species.Alcremie => $"{form}-{argument}",
        Species.Pikachu when rules.Generation == 6 && form != 0 => $"cosplay-{form}",
        Species.Arceus when rules.Generation == 4 && form > 9 => (form - 1).ToString(),
        _ => form.ToString(),
    };

    /// <summary>The forms this Pokémon can take. Empty when there is nothing to choose, or its current form must stay.</summary>
    public static List<FormChoice> GetForms(PokemonEntry e)
    {
        var rules = e.Rules;
        ushort species = e.Original.Species;
        if (e.Original.IsEgg || !rules.CanChooseForm(species, e.Baseline.Form)) return [];
        var names = FormConverter.GetFormList(species, Names.Types, Names.forms, Genders, rules.Context);
        var forms = Enumerable.Range(0, names.Length).Select(f => (byte)f).Where(f => rules.CanChooseForm(species, f)).ToList();
        if (forms.Count < 2) return [];
        if (species == (ushort)Species.Alcremie)
            return forms.SelectMany(f => AlcremieSweets.Select((sweet, i) => new FormChoice(f, (uint)i, $"{FormName(rules, species, f)} · {sweet}"))).ToList();
        // Furfrou's trim and Hoopa's unbound form run on a day counter, which starts full.
        bool timed = species is (ushort)Species.Furfrou or (ushort)Species.Hoopa;
        uint Argument(byte form) => timed && form != e.Baseline.Form ? FormArgumentUtil.GetFormArgumentMax(species, form, rules.Context) : e.Baseline.FormArgument;
        return forms.Select(f => new FormChoice(f, Argument(f), FormName(rules, species, f))).ToList();
    }

    /// <summary>Selects a form. A move only the previous form could use leaves with it.</summary>
    public static void SetForm(PokemonEntry entry, FormChoice choice)
    {
        var d = entry.Edit;
        d.Form = choice.Form; d.FormArgument = choice.Argument;
        if (entry.Rules.IsGameBoy)
        {
            GameRules.SetUnownDVs(d, choice.Form);
            d.IsShiny = Apply(entry).IsShiny;
        }
        var allowed = GetMoves(entry).Select(m => (ushort)m.Id).ToHashSet();
        var kept = d.Moves.Where(m => m != 0 && allowed.Contains(m)).ToArray();
        d.Moves = kept.Concat(new ushort[d.Moves.Length - kept.Length]).ToArray();
        if (entry.Rules.HasStoredForm) KeepAbilitySlot(entry);
    }

    // The ability is kept by its slot, because each form can have its own set.
    static void KeepAbilitySlot(PokemonEntry entry)
    {
        var d = entry.Edit;
        var pi = InDraftForm(entry).PersonalInfo;
        d.AbilitySlot = d.AbilitySlot < pi.AbilityCount && pi.GetAbilityAtIndex(d.AbilitySlot) != 0 ? d.AbilitySlot : 0;
        bool original = d.Form == entry.Baseline.Form && d.AbilitySlot == entry.Baseline.AbilitySlot;
        d.Ability = original ? entry.Baseline.Ability : pi.GetAbilityAtIndex(d.AbilitySlot);
    }

    /// <summary>The abilities to choose from, each once. One the Pokémon already has outside its list stays available.</summary>
    public static List<AbilityChoice> GetAbilities(PokemonEntry e)
    {
        if (!e.Rules.HasAbility) return [];
        var pi = InDraftForm(e).PersonalInfo;
        var choices = new List<AbilityChoice>();
        for (int slot = 0; slot < pi.AbilityCount; slot++)
        {
            int ability = pi.GetAbilityAtIndex(slot);
            if (ability != 0 && choices.All(c => c.Ability != ability)) choices.Add(new(slot, ability, $"{AbilityName(ability)} ({AbilitySlots[slot]})"));
        }
        int current = e.Baseline.Ability;
        if (e.Edit.Form == e.Baseline.Form && choices.All(c => c.Ability != current))
            choices.Add(new(e.Baseline.AbilitySlot, current, T("{0} (actual)", AbilityName(current))));
        return choices;
    }

    static string AbilityName(int id) => id >= 0 && id < Names.abilitylist.Length ? Names.abilitylist[id] : id.ToString();

    /// <summary>Selects an ability. Two slots can hold the same one, so going back to the original restores its slot too.</summary>
    public static void SetAbility(PokemonEntry entry, AbilityChoice choice)
    {
        var d = entry.Edit;
        bool original = d.Form == entry.Baseline.Form && choice.Ability == entry.Baseline.Ability;
        d.Ability = choice.Ability;
        d.AbilitySlot = original ? entry.Baseline.AbilitySlot : choice.Slot;
    }

    /// <summary>A Game Boy Unown's letter follows its DVs, so editing them can change it.</summary>
    public static void SyncForm(PokemonEntry entry)
    {
        if (entry.Rules.IsGameBoy) entry.Edit.Form = Apply(entry).Form;
    }

    // The Pokémon in the form being edited, for what depends on the form.
    static PKM InDraftForm(PokemonEntry e)
    {
        var p = e.Original;
        if (e.Rules.HasStoredForm && p.Form != e.Edit.Form) { p = p.Clone(); p.Form = e.Edit.Form; }
        return p;
    }

    public static List<Choice> GetMoves(PokemonEntry e)
    {
        var p = InDraftForm(e);
        var possible = new bool[Math.Max(p.MaxMoveID + 1, LongestMoveTable)];
        e.Rules.LearnSource.GetAllMoves(possible, p, new EvoCriteria {
            Species = p.Species, Form = p.Form, LevelMax = p.CurrentLevel, LevelMin = 1,
        }, e.Rules.MoveSources);
        var learned = Enumerable.Range(1, p.MaxMoveID).Where(i => possible[i]).Select(i => (ushort)i)
            .Concat(p.Moves.Where(m => m != 0)).Distinct();
        return new[] { new Choice(0, T("— Sin movimiento —")) }.Concat(learned
            .OrderBy(m => Names.movelist[m], StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new Choice(m, Names.movelist[m]))).ToList();
    }

    public static List<EvolutionTarget> GetFinalForms(PokemonEntry e)
    {
        var p = e.Original;
        var personal = e.Rules.Personal;
        var result = new List<EvolutionTarget>();
        var visited = new HashSet<(ushort, byte)>();
        var tree = EvolutionTree.GetEvolutionTree(e.Rules.Context);
        // EvolutionNetwork exposes its graph through an IEvolutionForward member.
        var type = tree.GetType();
        var forward = type.GetProperties().Where(x => typeof(IEvolutionForward).IsAssignableFrom(x.PropertyType))
            .Select(x => x.GetValue(tree)).OfType<IEvolutionForward>().FirstOrDefault()
            ?? type.GetFields().Where(x => typeof(IEvolutionForward).IsAssignableFrom(x.FieldType))
            .Select(x => x.GetValue(tree)).OfType<IEvolutionForward>().FirstOrDefault()
            ?? throw new InvalidOperationException(T("No se pudo consultar la tabla de evoluciones."));
        void Visit(ushort species, byte form)
        {
            if (!visited.Add((species, form))) return;
            var next = forward.GetForward(species, form).ToArray()
                .Select(x => (x.Species, Form: x.GetDestinationForm(form)))
                .Where(x => x.Species <= personal.MaxSpeciesID && personal.IsPresentInGame(x.Species, x.Form))
                .Distinct().ToList();
            if (next.Count == 0)
                result.Add(new(species, form, Names.specieslist[species] + (form == 0 ? "" : $" · {FormName(e.Rules, species, form)}")));
            else foreach (var n in next) Visit(n.Species, n.Form);
        }
        Visit(p.Species, e.Edit.Form);
        return result.OrderByDescending(x => {
            var pi = personal[x.Species, x.Form]; return Math.Max(pi.ATK, pi.SPA);
        }).ToList();
    }

    public static string Optimize(PokemonEntry entry, EvolutionTarget? target = null)
    {
        var rules = entry.Rules;
        target ??= GetFinalForms(entry).First();
        var pi = rules.Personal[target.Species, target.Form];
        bool physical = pi.ATK > pi.SPA;
        bool bulky = pi.SPE < 70 || pi.SPE <= 85 && pi.DEF + pi.SPD >= 180;
        entry.Edit.IVs = Enumerable.Repeat(rules.MaxIV, 6).ToArray();
        if (rules.HasNature) entry.Edit.Nature = physical ? Nature.Adamant : Nature.Modest;
        if (rules.MaxEVTotal == 0)
        {
            // No shared limit in these games: every stat can go to the top.
            entry.Edit.EVs = Enumerable.Repeat(rules.MaxEV, 6).ToArray();
            if (rules.IsGameBoy) GameRules.KeepDVLooks(entry.Edit, entry.Original.Species);
            return T("{0}: todas las estadísticas al máximo.", target.Label);
        }
        var evs = new int[6]; evs[physical ? 1 : 4] = 252;
        if (bulky) { evs[0] = 252; evs[2] = 4; }
        else { evs[3] = 252; evs[0] = 4; }
        entry.Edit.EVs = evs;
        return $"{target.Label}: {Stats[physical ? 1 : 4]} + {Stats[bulky ? 0 : 3]}.";
    }

    public static PKM Apply(PokemonEntry entry)
    {
        var rules = entry.Rules;
        var p = entry.Original.Clone();
        var d = entry.Edit;
        var ivs = d.IVs.ToArray(); var evs = d.EVs.ToArray();
        if (rules.SharesSpecial) { ivs[5] = ivs[4]; evs[5] = evs[4]; }
        bool formChanged = d.Form != entry.Baseline.Form || d.FormArgument != entry.Baseline.FormArgument;
        bool abilityChanged = d.Ability != entry.Baseline.Ability || d.AbilitySlot != entry.Baseline.AbilitySlot;
        bool statsChanged = formChanged || !ivs.SequenceEqual(entry.Baseline.IVs) || !evs.SequenceEqual(entry.Baseline.EVs)
            || d.Nature != entry.Baseline.Nature || d.Level != entry.Baseline.Level;
        p.SetIVs(ivs);
        if (p is IAwakened a) SetAwakening(a, evs); else p.SetEVs(evs);
        if (formChanged && rules.HasStoredForm) ChangeForm(p, d);
        if (abilityChanged) p.RefreshAbility(d.AbilitySlot);
        if (rules.HasHeldItems && d.HeldItem != entry.Baseline.HeldItem) p.HeldItem = d.HeldItem;
        if (rules.HasFriendship && d.Friendship != entry.Baseline.Friendship) p.CurrentFriendship = (byte)d.Friendship;
        if (d.Level != entry.Baseline.Level) p.CurrentLevel = (byte)d.Level;
        if (d.Nickname != entry.Baseline.Nickname) p.SetNickname(d.Nickname.Trim().Length == 0 ? "" : d.Nickname);
        if (d.Trainer != entry.Baseline.Trainer) p.OriginalTrainerName = d.Trainer;
        if (rules.HasBall && d.Ball != entry.Baseline.Ball) p.Ball = (byte)d.Ball;
        if (rules.HasNature && !rules.HasPidNature && d.Nature != entry.Baseline.Nature) p.Nature = p.StatAlignment = d.Nature;
        for (int i = 0; i < 4; i++)
        {
            if (p.GetMove(i) == d.Moves[i]) continue;
            p.SetMove(i, d.Moves[i]);
            switch (i) {
                case 0: p.Move1_PPUps = 0; break;
                case 1: p.Move2_PPUps = 0; break;
                case 2: p.Move3_PPUps = 0; break;
                case 3: p.Move4_PPUps = 0; break;
            }
            p.HealPPIndex(i);
        }
        ApplyPid(p, rules, d, abilityChanged);
        if (statsChanged)
        {
            var damage = Math.Max(0, p.Stat_HPMax - p.Stat_HPCurrent);
            bool fainted = p.PartyStatsPresent && p.Stat_HPCurrent == 0;
            p.SetStats(p.GetStats(p.PersonalInfo));
            p.Stat_HPCurrent = fainted ? 0 : Math.Max(1, p.Stat_HPMax - damage);
            p.Stat_Level = p.CurrentLevel;
        }
        if (p is PB7 letsGo) letsGo.ResetCalculatedValues();   // CP depends on what was just edited
        p.RefreshChecksum();
        return p;
    }

    static void ChangeForm(PKM p, Draft d)
    {
        p.Form = d.Form;
        if (p is IFormArgument) p.ChangeFormArgument(d.FormArgument);
    }

    static void SetAwakening(IAwakened a, int[] values)
    {
        a.AV_HP = (byte)values[0]; a.AV_ATK = (byte)values[1]; a.AV_DEF = (byte)values[2];
        a.AV_SPE = (byte)values[3]; a.AV_SPA = (byte)values[4]; a.AV_SPD = (byte)values[5];
    }

    static void ApplyPid(PKM p, GameRules rules, Draft d, bool abilityChanged)
    {
        if (!rules.HasNature && !rules.HasShiny || rules.IsGameBoy) return;
        bool natureWrong = rules.HasPidNature && p.Nature != d.Nature;
        bool formWrong = rules.HasFormInPid && p.Form != d.Form;
        // Up to Gen 5 a regular ability answers to one PID bit. The hidden one doesn't.
        bool abilityWrong = abilityChanged && p.Format <= 5 && d.AbilitySlot < 2 && p.PersonalInfo.GetAbilityAtIndex(p.PIDAbility) != d.Ability;
        if (p.IsShiny == d.IsShiny && !natureWrong && !formWrong && !abilityWrong) return;
        if (p.Format >= 6)
        {
            // Shininess depends on PID and trainer IDs. Keep trainer IDs and encryption
            // constant intact, and derive a stable PID for every preview/save.
            if (d.IsShiny)
            {
                uint low = p.PID & 0xFFFF;
                uint high = low ^ p.TID16 ^ p.SID16;
                p.PID = low | (high << 16);
            }
            else p.PID ^= 0x10000000;
            return;
        }
        // Up to Gen 5 the PID also decides gender, ability slot, (Gen 3-4) nature and (Gen 3) Unown's
        // letter. Search for one that keeps them all, seeded so that every preview and the save agree.
        var nature = rules.HasPidNature ? d.Nature : p.Nature;
        byte form = rules.HasFormInPid ? d.Form : p.Form;
        byte gender = p.Gender;   // Gen 3 reads it from the PID, which is about to change
        if (abilityWrong) p.PID ^= p.Generation == 5 ? 1u << 16 : 1u;
        var random = new Random(HashCode.Combine(p.PID, (int)nature, d.IsShiny, form));
        // PKHeX solves Unown's letter only for FireRed and LeafGreen, the games that have Unown. Otherwise it
        // keeps the ability bit, which is also the letter's lowest bit, and half the alphabet can't be reached.
        var origin = rules.HasFormInPid && p.Species == (ushort)Species.Unown ? GameVersion.FR : p.Version;
        do p.PID = EntityPID.GetRandomPID(random, p.Species, gender, origin, nature, form, p.PID);
        while (p.IsShiny != d.IsShiny);
    }

    public static string? Validate(PokemonEntry e)
    {
        var d = e.Edit;
        var rules = e.Rules;
        if (d.IVs.Any(v => v < 0 || v > rules.MaxIV)) return T("Cada {0} debe estar entre 0 y {1}.", rules.IVName, rules.MaxIV);
        if (d.EVs.Any(v => v < 0 || v > rules.MaxEV)) return T("Cada {0} debe estar entre 0 y {1}.", rules.EVName, rules.MaxEV);
        if (rules.MaxEVTotal != 0 && d.EVs.Sum() > rules.MaxEVTotal) return T("{0}: los EV suman {1}. El máximo es {2}.", e.DisplayName, d.EVs.Sum(), rules.MaxEVTotal);
        if ((uint)d.Nature > 24) return T("Selecciona una naturaleza válida.");
        if (d.Friendship is < 0 or > 255) return T("La amistad debe estar entre 0 y 255.");
        if (d.HeldItem != e.Baseline.HeldItem && (!rules.CanHold(d.HeldItem) || e.Original.IsEgg && d.HeldItem != 0))
            return T("Ese objeto no se puede equipar en este Pokémon.");
        if (rules.HasAbility && GetAbilities(e).All(c => c.Ability != d.Ability)) return T("Esa habilidad no está disponible para este Pokémon.");
        if (d.Moves.All(m => m == 0)) return T("{0} debe conservar al menos un movimiento.", e.DisplayName);
        if (d.Moves[0] == 0 || Enumerable.Range(1, 3).Any(i => d.Moves[i] != 0 && d.Moves[i - 1] == 0))
            return T("Coloca los movimientos en orden, sin espacios vacíos entre ellos.");
        var set = d.Moves.Where(m => m != 0).ToList();
        if (set.Count != set.Distinct().Count()) return T("No se puede repetir un movimiento.");
        var allowed = GetMoves(e).Select(m => (ushort)m.Id).ToHashSet();
        if (d.Moves.Any(m => !allowed.Contains(m))) return T("Hay un movimiento incompatible con este Pokémon.");
        if (d.Level != e.Baseline.Level && d.Level is < 1 or > Experience.MaxLevel) return T("El nivel debe estar entre 1 y {0}.", Experience.MaxLevel);
        if (d.Trainer != e.Baseline.Trainer && d.Trainer.Trim().Length == 0) return T("El nombre del entrenador no puede quedar vacío.");
        if (d.Ball != e.Baseline.Ball && GetBalls(e).All(b => b.Id != d.Ball)) return T("Esa Poké Ball no existe en este juego.");
        return LosesText(e) ? T("Ese nombre tiene caracteres que este juego no admite.") : null;
    }

    // Each game has its own alphabet and lengths, and whatever it can't store just disappears.
    static bool LosesText(PokemonEntry e)
    {
        var d = e.Edit;
        if (d.Nickname == e.Baseline.Nickname && d.Trainer == e.Baseline.Trainer) return false;
        // Game Boy names can fill their field and lose the end mark, so the length is checked apart.
        if (d.Nickname.Length > e.Original.MaxStringLengthNickname || d.Trainer.Length > e.Original.MaxStringLengthTrainer) return true;
        var preview = e.Preview();
        return d.Nickname != e.Baseline.Nickname && d.Nickname.Trim().Length != 0 && Loses(d.Nickname, preview.Nickname, e.Original.MaxStringLengthNickname)
            || d.Trainer != e.Baseline.Trainer && Loses(d.Trainer, preview.OriginalTrainerName, e.Original.MaxStringLengthTrainer);
    }

    // Game Boy games keep room for the end mark, so one letter too many is stored and then runs into the next field.
    static bool Loses(string wanted, string stored, int limit) => wanted.Length > limit || stored != wanted;

    public static string NatureEffect(Nature nature)
    {
        // Nature indices use HP/Atk/Def/Spe/SpA/SpD with the HP row omitted.
        int up = (int)nature / 5 + 1, down = (int)nature % 5 + 1;
        return up == down ? T("Naturaleza neutra: no modifica estadísticas.") : $"↑ {Stats[up]}  ·  ↓ {Stats[down]}";
    }

    /// <summary>Toggles shininess; on Game Boy games this rewrites the DVs, so the screen must refresh them.</summary>
    public static void SetShiny(PokemonEntry entry, bool shiny)
    {
        entry.Edit.IsShiny = shiny;
        if (entry.Rules.IsGameBoy) GameRules.SetShinyDVs(entry.Edit, shiny);
    }
}

public sealed class SaveDocument
{
    const int SmallestSave = 0x2000, LargestSave = 64_000_000;

    // Emulators lock or rewrite the save of the console they run, so only those block a save.
    static readonly string[] Everywhere = ["retroarch", "emuhawk"];
    static readonly string[] GameBoyEmulators = ["mgba", "visualboyadvance", "vba", "bgb", "sameboy", "gambatte", "mesen", "no$gba"];
    static readonly string[] DsEmulators = ["desmume", "melonds", "no$gba"];
    static readonly string[] GameCubeEmulators = ["dolphin"];
    static readonly string[] N3dsEmulators = ["citra", "azahar", "lime3ds", "mandarine", "borked3ds", "panda3ds"];
    static readonly string[] SwitchEmulators = ["eden", "yuzu", "ryujinx", "suyu", "sudachi", "citron", "torzu"];

    public string FilePath { get; private set; } = "";
    public byte[] OriginalBytes { get; private set; } = [];
    public string OriginalHash { get; private set; } = "";
    public SaveFile Save { get; private set; } = null!;
    public GameRules Rules { get; private set; } = null!;
    public SAV9ZA? ZaSave => Save as SAV9ZA;
    public List<PokemonEntry> Pokemon { get; private set; } = [];
    public List<BagItem> Items { get; private set; } = [];
    readonly Dictionary<ushort, int> itemChanges = [];
    public IReadOnlyDictionary<ushort, int> ItemChanges => itemChanges;
    public long Money { get; set; }
    public bool MoneyChanged => Money != Save.Money;
    public bool HasChanges => DirtyCount != 0 || MoneyChanged || itemChanges.Count != 0;
    public int ResourceChangeCount => (MoneyChanged ? 1 : 0) + itemChanges.Count;
    public int DirtyCount => Pokemon.Count(p => p.Dirty);
    public const int TeamSize = 6;
    public int BoxCount => Save.HasBox ? Save.BoxCount : 0;
    public int OpeningBox
    {
        get
        {
            // Only a convenience, so a save that can't answer it still opens.
            try { return BoxCount == 0 ? 0 : Math.Clamp(Save.CurrentBox, 0, BoxCount - 1); }
            catch (ArgumentException) { return 0; }
        }
    }
    public int BoxOf(PokemonEntry entry) => entry.Index / Save.BoxSlotCount;
    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>Backups are folders named after the moment they were made, so they sort by date.</summary>
    public const string BackupStamp = "yyyy-MM-dd_HHmmss_fff";
    public static string NewBackupFolder(string root) => Path.Combine(root,
        DateTime.Now.ToString(BackupStamp, System.Globalization.CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..6]);

    /// <summary>The team's six places, in order. A place with nobody in it is null.</summary>
    public List<PokemonEntry?> TeamSlots()
    {
        var team = Pokemon.Where(p => p.IsParty).ToDictionary(p => p.Index);
        return Enumerable.Range(0, TeamSize).Select(i => team.GetValueOrDefault(i)).ToList();
    }

    /// <summary>Every place of a box, in the game's order. A place with nobody in it is null.</summary>
    public List<PokemonEntry?> BoxSlots(int box)
    {
        var boxed = Pokemon.Where(p => !p.IsParty).ToDictionary(p => p.Index);
        return Enumerable.Range(box * Save.BoxSlotCount, Save.BoxSlotCount).Select(i => boxed.GetValueOrDefault(i)).ToList();
    }

    public string BoxLabel(int box) => $"{Rules.BoxName(box)}  ·  {BoxSlots(box).Count(p => p != null)} / {Save.BoxSlotCount}";

    bool IsMegaStone(ushort id) => Rules.IsZA && ItemStorage9ZA.IsMegaStone(id);
    bool IsOrb(ushort id) => Rules.IsZA && ItemStorage9ZA.IsOrb(id);
    public bool IsUniqueHeldItem(ushort id) => Rules.IsZA && ItemStorage9ZA.IsUniqueHeldItem(id);
    public bool IsMegaStoneItem(ushort id) => IsMegaStone(id);

    /// <summary>Item names follow the language on screen; everything else in the document stays as it is.</summary>
    public void RefreshNames()
    {
        Rules.RefreshNames();
        Items = Items.Select(i => i with { Name = Rules.ItemName(i.Id) is { Length: > 0 } name ? name : i.Name })
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Opens a save for editing. The legality rules and the Showdown text then follow this one.</summary>
    public static SaveDocument Open(string path)
    {
        var document = Read(path);
        document.Activate();
        return document;
    }

    /// <summary>Reads a save without making it the open one, for what only looks at it.</summary>
    public static SaveDocument Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < SmallestSave or > LargestSave)
            throw new InvalidOperationException(T("Selecciona el archivo de guardado de una partida de Pokémon."));
        var bytes = File.ReadAllBytes(path);
        if (SaveUtil.GetSaveFile(bytes.ToArray(), path) is not { } save)
            throw new InvalidOperationException(T("No reconozco ese archivo como una partida de Pokémon. Debe ser el guardado del juego (.sav, .srm, .dsv, main…), no un estado guardado del emulador."));
        if (save is SAV9ZA && !SwishCrypto.GetIsHashValid(bytes)) throw new InvalidOperationException(T("El archivo no tiene una firma de partida válida."));
        var rules = new GameRules(save);
        var party = save.HasParty ? Enumerable.Range(0, save.PartyCount).Select(i => new PokemonEntry(rules, true, i, save.GetPartySlotAtIndex(i))) : [];
        var boxed = save.HasBox ? save.BoxData.Select((p, i) => (p, i)).Where(x => x.p.Species != 0).Select(x => new PokemonEntry(rules, false, x.i, x.p)) : [];
        var entries = party.Concat(boxed).ToList();
        if (save is SAV9ZA && entries.Any(e => !e.Original.ChecksumValid)) throw new InvalidOperationException(T("La partida contiene un Pokémon con datos dañados."));
        entries = entries.Where(e => e.Original.ChecksumValid).ToList();   // older games can hold "bad eggs": leave those alone
        var items = save is SAV9ZA za ? ReadBagZA(za, rules) : ReadBag(save, rules);
        return new SaveDocument { FilePath = Path.GetFullPath(path), OriginalBytes = bytes, OriginalHash = Hash(bytes), Save = save, Rules = rules,
            Pokemon = entries, Money = save.Money, Items = items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList() };
    }

    // The legality rules and the Showdown text ask PKHeX which save is open.
    void Activate()
    {
        ParseSettings.InitFromSaveFileData(Save);
        RecentTrainerCache.SetRecentTrainer(Save);
    }

    static List<BagItem> ReadBagZA(SAV9ZA save, GameRules rules)
    {
        var bag = save.Inventory;
        var items = new List<BagItem>();
        foreach (var pouch in bag.Pouches.Where(p => p.Type != InventoryType.KeyItems))
        {
            foreach (var item in pouch.Items)
            {
                ushort id = (ushort)item.Index;
                if (id == 0 || id > save.MaxItemID || id == 2619 || !bag.IsLegal(pouch.Type, id, 1)) continue;
                if (save.SaveRevision == 0 && pouch.Type == InventoryType.TMHMs
                    && ItemStorage9ZA.TM.IndexOf(id) >= 107) continue;
                int max = bag.GetMaxCount(pouch.Type, id);
                if (ItemStorage9ZA.IsUniqueHeldItem(id)) max = Math.Min(max, 1);
                if (max <= 0) continue;
                string name = rules.ItemName(id);
                items.Add(new(id, name, pouch.Type, rules.CategoryOf(id, pouch.Type), item.Count, max, item.IsHeld));
            }
        }
        return items;
    }

    // Every other game keeps a list of occupied slots, so the catalog comes from what the pouch may hold.
    static List<BagItem> ReadBag(SaveFile save, GameRules rules)
    {
        var bag = save.Inventory;
        var items = new Dictionary<ushort, BagItem>();
        foreach (var pouch in bag.Pouches.Where(p => p.Type is not (InventoryType.KeyItems or InventoryType.PCItems or InventoryType.FreeSpace)))
        {
            var counts = new Dictionary<ushort, int>();
            foreach (var slot in pouch.Items.Where(s => s.Index != 0))
                counts[(ushort)slot.Index] = counts.GetValueOrDefault((ushort)slot.Index) + slot.Count;
            foreach (ushort id in pouch.GetAllItems().ToArray().Concat(counts.Keys).Distinct())
            {
                string name = rules.ItemName(id);
                if (id == 0 || items.ContainsKey(id) || string.IsNullOrWhiteSpace(name)) continue;
                int count = counts.GetValueOrDefault(id), max = bag.GetMaxCount(pouch.Type, id);
                if (max <= 0 || count == 0 && !bag.IsLegal(pouch.Type, id, 1)) continue;
                items[id] = new(id, name, pouch.Type, rules.CategoryOf(id, pouch.Type), count, Math.Max(max, count), false);
            }
        }
        return items.Values.ToList();
    }

    int HeldCount(ushort id, bool original = false) => Pokemon.Count(p => (original ? p.Baseline.HeldItem : p.Edit.HeldItem) == id);
    int OriginalOwned(ushort id) => (Items.FirstOrDefault(i => i.Id == id)?.OriginalCount ?? 0)
        + (IsMegaStone(id) ? 0 : HeldCount(id, true));

    public int GetItemCount(ushort id)
    {
        int owned = itemChanges.TryGetValue(id, out var value) ? value : OriginalOwned(id);
        return owned - (IsMegaStone(id) ? 0 : HeldCount(id));
    }

    public bool IsItemHeld(ushort id) => IsUniqueHeldItem(id) && HeldCount(id) != 0;
    public PokemonEntry? ItemHolder(ushort id) => Pokemon.FirstOrDefault(p => p.Edit.HeldItem == id);
    public List<Choice> GetHeldChoices() => new[] { new Choice(0, T("— Sin objeto —")) }
        .Concat(Items.Where(i => Rules.CanHold(i.Id)).Select(i => new Choice(i.Id, i.Name))).ToList();

    public void EquipItem(PokemonEntry entry, int id)
    {
        if (!Pokemon.Contains(entry)) throw new InvalidOperationException(T("Selecciona un Pokémon de esta partida."));
        if (entry.Edit.HeldItem == id) return;
        if (!Rules.CanHold(id) || id != 0 && !Items.Any(i => i.Id == id))
            throw new InvalidOperationException(T("Ese objeto no se puede equipar en esta partida."));
        if (id != 0)
        {
            var holder = ItemHolder((ushort)id);
            if (IsUniqueHeldItem((ushort)id) && holder != null && holder != entry)
                throw new InvalidOperationException(T("{0} ya está equipado en {1}. Retíralo primero.", Rules.ItemName(id), holder.DisplayName));
            if (GetItemCount((ushort)id) < 1) throw new InvalidOperationException(T("No tienes unidades de ese objeto en la bolsa. Agrégalo en «Dinero y objetos»."));
        }
        int previous = entry.Edit.HeldItem; entry.Edit.HeldItem = id;
        if (ValidateSupply() is { } error) { entry.Edit.HeldItem = previous; throw new InvalidOperationException(error); }
    }

    /// <summary>Selects a form along with the item that keeps it, and returns what happened.</summary>
    public string SetForm(PokemonEntry entry, FormChoice choice)
    {
        ushort species = entry.Original.Species;
        ushort previous = Rules.ItemForForm(species, entry.Edit.Form), needed = Rules.ItemForForm(species, choice.Form);
        Catalog.SetForm(entry, choice);
        string ready = T("Forma {0} lista. Guarda para aplicarla.", choice.Label);
        bool holdsPrevious = previous != 0 && entry.Edit.HeldItem == previous;
        if (needed == entry.Edit.HeldItem || needed == 0 && !holdsPrevious) return ready;
        try { EquipItem(entry, needed); }
        catch (InvalidOperationException ex)
        {
            return needed == 0 ? $"{ready} {ex.Message}" : $"{ready} {T("El juego la conserva solo si lleva {0}.", Rules.ItemName(needed))} {ex.Message}";
        }
        return needed == 0 ? ready : T("Forma {0} lista con {1} equipado. Guarda para aplicarla.", choice.Label, Rules.ItemName(needed));
    }

    /// <summary>Takes from a Showdown set what this Pokémon can use, and returns what happened.</summary>
    public string ImportShowdown(PokemonEntry entry, string text)
    {
        var set = new ShowdownSet(text.AsSpan());
        if (set.Species == 0) throw new InvalidOperationException(T("El portapapeles no tiene un set de Showdown."));
        if (set.Species != entry.Original.Species)
            throw new InvalidOperationException(T("Ese set es de {0}, no de {1}.", Catalog.Names.specieslist[set.Species], entry.SpeciesName));
        var d = entry.Edit;
        var left = new List<string>();
        d.Level = set.Level;
        if (set.Nickname.Length != 0) d.Nickname = set.Nickname;
        if (Rules.HasNature && set.Nature != Nature.Random) d.Nature = set.Nature;
        d.IVs = set.IVs.Select(v => Math.Min(v, Rules.MaxIV)).ToArray();
        if (!Rules.HasAwakening) d.EVs = set.EVs.Select(v => Math.Min(v, Rules.MaxEV)).ToArray();
        if (Rules.IsGameBoy) GameRules.KeepDVLooks(d, entry.Original.Species);
        if (Rules.HasShiny) Catalog.SetShiny(entry, set.Shiny);
        if (Rules.HasAbility && set.Ability >= 0)
        {
            if (Catalog.GetAbilities(entry).FirstOrDefault(c => c.Ability == set.Ability) is { } ability) Catalog.SetAbility(entry, ability);
            else left.Add(Catalog.Names.abilitylist[set.Ability]);
        }
        var allowed = Catalog.GetMoves(entry).Select(m => (ushort)m.Id).ToHashSet();
        var moves = set.Moves.Where(m => m != 0).ToList();
        left.AddRange(moves.Where(m => !allowed.Contains(m)).Select(m => Catalog.Names.movelist[m]));
        var usable = moves.Where(allowed.Contains).Distinct().Take(d.Moves.Length).ToList();
        if (usable.Count != 0) d.Moves = usable.Concat(Enumerable.Repeat((ushort)0, d.Moves.Length - usable.Count)).ToArray();
        if (Rules.HasHeldItems && set.HeldItem != 0)
        {
            try { EquipItem(entry, set.HeldItem); }
            catch (InvalidOperationException) { left.Add(Rules.ItemName(set.HeldItem)); }
        }
        if (Rules.HasStoredForm && set.Form != d.Form) left.Add(T("Forma"));
        return left.Count == 0 ? T("Set aplicado a {0}. Revísalo antes de guardar.", entry.DisplayName)
            : T("Set aplicado a {0}. No se aplicó: {1}.", entry.DisplayName, string.Join(", ", left));
    }

    public void ResetPokemon(PokemonEntry entry)
    {
        var previous = entry.Edit; entry.Edit = Draft.From(entry.Original);
        if (ValidateSupply() is { } error) { entry.Edit = previous; throw new InvalidOperationException(error); }
    }

    public void SetItemCount(ushort id, int count)
    {
        var item = Items.FirstOrDefault(i => i.Id == id) ?? throw new InvalidOperationException(T("Ese objeto no está disponible para esta partida."));
        if (count < 0 || count > item.MaxCount) throw new InvalidOperationException(T("{0}: la cantidad debe estar entre 0 y {1}.", item.Name, item.MaxCount));
        if (IsItemHeld(id) && (IsMegaStone(id) ? count != 1 : count != 0))
            throw new InvalidOperationException(T("Este objeto está equipado. Retíralo desde «Estadísticas» antes de cambiar su cantidad."));
        int owned = count + (IsMegaStone(id) ? 0 : HeldCount(id));
        if (IsUniqueHeldItem(id) && owned > 1) throw new InvalidOperationException(T("Solo puedes tener una unidad de ese objeto único."));
        int previous = itemChanges.TryGetValue(id, out var value) ? value : -1;
        if (owned == OriginalOwned(id)) itemChanges.Remove(id);
        else itemChanges[id] = owned;
        if (BagSpaceError() is not { } full) return;
        if (previous < 0) itemChanges.Remove(id); else itemChanges[id] = previous;
        throw new InvalidOperationException(full);
    }

    public int AddItem(ushort id, int amount)
    {
        var item = Items.FirstOrDefault(i => i.Id == id) ?? throw new InvalidOperationException(T("Selecciona un objeto."));
        if (amount < 0) throw new InvalidOperationException(T("La cantidad que agregas no puede ser negativa."));
        int current = GetItemCount(id);
        int total = (int)Math.Min(item.MaxCount, (long)current + amount);
        SetItemCount(id, total);
        return total - current;
    }

    public void ResetResources()
    {
        var previous = itemChanges.ToArray(); long previousMoney = Money; Money = Save.Money; itemChanges.Clear();
        if (ValidateSupply() is not { } error) return;
        Money = previousMoney; foreach (var entry in previous) itemChanges[entry.Key] = entry.Value;
        throw new InvalidOperationException(T("Retira los objetos que acabas de equipar antes de deshacer la bolsa.") + " " + error);
    }

    string? ValidateSupply()
    {
        foreach (var item in Items)
        {
            int count = GetItemCount(item.Id);
            if (count < 0) return T("No hay suficientes unidades de {0} para los Pokémon que lo llevan equipado.", item.Name);
            if (count > item.MaxCount) return T("No hay espacio para devolver {0} a la bolsa. Reduce su cantidad antes de retirarlo.", item.Name);
            if (!IsUniqueHeldItem(item.Id)) continue;
            int held = HeldCount(item.Id);
            if (held > 1) return T("{0} solo se puede equipar en un Pokémon a la vez.", item.Name);
            if (IsMegaStone(item.Id) && held != 0 && count != 1) return T("La bolsa debe conservar una unidad de {0}.", item.Name);
            if (IsOrb(item.Id) && count + held > 1) return T("Solo puedes tener una unidad de {0}.", item.Name);
        }
        return BagSpaceError();
    }

    // Older games have a fixed number of slots per pouch, so a new item may not fit.
    string? BagSpaceError()
    {
        if (Rules.IsZA) return null;
        foreach (var pouch in Save.Inventory.Pouches)
        {
            var mine = Items.Where(i => i.Pouch == pouch.Type).ToList();
            if (mine.Count == 0) continue;
            int others = pouch.Items.Count(s => s.Index != 0 && s.Count != 0 && !mine.Any(i => i.Id == s.Index));
            if (mine.Count(i => GetItemCount(i.Id) != 0) + others > pouch.Items.Length)
                return T("El bolsillo {0} solo tiene {1} espacios. Retira otro objeto primero.", T(Rules.PouchName(pouch.Type)), pouch.Items.Length);
        }
        return null;
    }

    Dictionary<ushort, int> EffectiveItemChanges()
    {
        var touched = itemChanges.Keys.Concat(Pokemon.Where(p => p.Edit.HeldItem != p.Baseline.HeldItem)
            .SelectMany(p => new[] { p.Edit.HeldItem, p.Baseline.HeldItem }).Where(id => id != 0).Select(id => (ushort)id)).ToHashSet();
        return touched.ToDictionary(id => id, GetItemCount);
    }

    public string? ValidateEdits()
    {
        if (Money < 0 || Money > Rules.MaxMoney) return T("El dinero debe estar entre 0 y {0:N0}.", Rules.MaxMoney);
        if (Pokemon.Any(p => p.Edit.HeldItem != p.Baseline.HeldItem && p.Edit.HeldItem != 0 && !Items.Any(i => i.Id == p.Edit.HeldItem)))
            return T("Un Pokémon tiene preparado un objeto que no está disponible en esta partida.");
        return Pokemon.Where(p => p.Dirty).Select(Catalog.Validate).FirstOrDefault(error => error != null) ?? ValidateSupply();
    }

    public byte[] BuildOutput() => ZaSave is { } za ? BuildZA(za) : Build();

    SaveFile Reopen(byte[] bytes) => SaveUtil.GetSaveFile(bytes.ToArray(), FilePath)
        ?? throw new InvalidOperationException(T("No se pudo volver a abrir la partida editada."));

    byte[] Build()
    {
        if (ValidateEdits() is { } error) throw new InvalidOperationException(error);
        if (!HasChanges && Pokemon.All(p => !p.Dirty)) return OriginalBytes.ToArray();
        var output = Reopen(OriginalBytes);
        var effectiveItems = EffectiveItemChanges();
        if (MoneyChanged) output.Money = (uint)Money;
        if (effectiveItems.Count != 0) WriteBag(output, effectiveItems);
        foreach (var e in Pokemon.Where(x => x.Dirty))
        {
            var p = Catalog.Apply(e);
            if (e.IsParty) output.SetPartySlotAtIndex(p, e.Index, EntityImportSettings.None);
            else output.SetBoxSlotAtIndex(p, e.Index, EntityImportSettings.None);
        }
        var bytes = output.Write().ToArray();
        if (bytes.Length != OriginalBytes.Length) throw new InvalidOperationException(T("Cambió el tamaño de la partida."));
        var reopened = Reopen(bytes);
        if (reopened.GetType() != Save.GetType() || reopened.PartyCount != Save.PartyCount)
            throw new InvalidOperationException(T("Cambió la estructura de la partida."));
        if (reopened.Money != (uint)Money) throw new InvalidOperationException(T("El dinero guardado no coincide con el solicitado."));
        foreach (var e in Pokemon)
        {
            var actual = e.IsParty ? reopened.GetPartySlotAtIndex(e.Index) : reopened.GetBoxSlotAtIndex(e.Index);
            if (!SamePokemon(actual, e.Dirty ? Catalog.Apply(e) : e.Original))
                throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", e.DisplayName));
        }
        var bag = reopened.Inventory;
        foreach (var item in Items)
        {
            int actual = bag.GetPouch(item.Pouch).Items.Where(s => s.Index == item.Id).Sum(s => s.Count);
            int expected = effectiveItems.TryGetValue(item.Id, out var count) ? count : item.OriginalCount;
            if (actual != expected) throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", item.Name));
        }
        return bytes;
    }

    void WriteBag(SaveFile output, Dictionary<ushort, int> changes)
    {
        var bag = output.Inventory;
        var edited = new HashSet<InventoryPouch>();
        foreach (var (id, count) in changes)
        {
            // Items outside the catalog (mail, say) leave with the Pokémon that held them.
            if (Items.FirstOrDefault(i => i.Id == id) is not { } item) continue;
            var pouch = bag.GetPouch(item.Pouch);
            var slot = pouch.Items.FirstOrDefault(s => s.Index == id);
            if (slot == null && count == 0) continue;
            edited.Add(pouch);
            if (slot != null) slot.Count = count;
            else if (pouch.GiveItem(bag, id, count) < 0) throw new InvalidOperationException(T("No hay espacio para {0} en la bolsa.", item.Name));
        }
        // Slot-based pouches can't have holes; from Gen 8 an item keeps its record at count 0.
        if (Rules.Generation <= 7) foreach (var pouch in edited) pouch.ClearCount0();
        bag.CopyTo(output);
    }

    static bool SamePokemon(PKM a, PKM b)
    {
        Span<int> ivsA = stackalloc int[6], ivsB = stackalloc int[6];
        a.GetIVs(ivsA); b.GetIVs(ivsB);
        return a.Species == b.Species && a.PID == b.PID && a.Form == b.Form && a.HeldItem == b.HeldItem && a.Nickname == b.Nickname
            && a.IsNicknamed == b.IsNicknamed && a.OriginalTrainerName == b.OriginalTrainerName && a.Ball == b.Ball
            && a.Ability == b.Ability && a.AbilityNumber == b.AbilityNumber
            && a.Move1 == b.Move1 && a.Move2 == b.Move2 && a.Move3 == b.Move3 && a.Move4 == b.Move4 && a.CurrentFriendship == b.CurrentFriendship
            && a.EV_HP == b.EV_HP && a.EV_ATK == b.EV_ATK && a.EV_DEF == b.EV_DEF && a.EV_SPE == b.EV_SPE && a.EV_SPA == b.EV_SPA
            && a.CurrentLevel == b.CurrentLevel && ivsA.SequenceEqual(ivsB);
    }

    byte[] BuildZA(SAV9ZA original)
    {
        if (ValidateEdits() is { } error) throw new InvalidOperationException(error);
        var output = (SAV9ZA)SaveUtil.GetSaveFile(OriginalBytes.ToArray())!;
        var effectiveItems = EffectiveItemChanges();
        if (MoneyChanged) output.Money = (uint)Money;
        foreach (var change in effectiveItems)
        {
            output.Items.SetItemQuantity(change.Key, change.Value);
            if (!ItemStorage9ZA.IsUniqueHeldItem(change.Key)) continue;
            var item = output.Items.GetItem(change.Key);
            item.IsHeld = ItemStorage9ZA.IsMegaStone(change.Key) && HeldCount(change.Key) != 0;
            if (ItemStorage9ZA.IsOrb(change.Key) && HeldCount(change.Key) != 0) { item.IsNew = false; item.IsNewNotify = false; }
            item.Write(InventoryPouch9a.GetItemSpan(output.Items.Data, change.Key));
        }
        foreach (var e in Pokemon.Where(x => x.Dirty))
        {
            var p = Catalog.Apply(e);
            if (e.IsParty) output.SetPartySlotAtIndex(p, e.Index, EntityImportSettings.None);
            else output.SetBoxSlotAtIndex(p, e.Index, EntityImportSettings.None);
        }
        var bytes = output.Write().ToArray();
        if (bytes.Length != OriginalBytes.Length || !SwishCrypto.GetIsHashValid(bytes))
            throw new InvalidOperationException(T("No se pudo verificar la integridad de la partida editada."));
        var reopened = (SAV9ZA?)SaveUtil.GetSaveFile(bytes.ToArray()) ?? throw new InvalidOperationException(T("No se pudo volver a abrir la partida editada."));
        if (reopened.PartyCount != original.PartyCount || reopened.AllBlocks.Count != original.AllBlocks.Count)
            throw new InvalidOperationException(T("Cambió la estructura de la partida."));
        var allowedBlocks = new HashSet<uint>();
        void AllowBlock(ReadOnlySpan<byte> data)
        {
            foreach (var block in original.AllBlocks) if (block.Data.SequenceEqual(data)) allowedBlocks.Add(block.Key);
        }
        if (Pokemon.Any(p => p.Dirty && p.IsParty)) AllowBlock(original.PartyInfo.Data);
        if (Pokemon.Any(p => p.Dirty && !p.IsParty)) AllowBlock(original.BoxInfo.Data);
        if (MoneyChanged) allowedBlocks.Add(SaveBlockAccessor9ZA.KMoney);
        if (effectiveItems.Count != 0) AllowBlock(original.Items.Data);
        for (int i = 0; i < original.AllBlocks.Count; i++)
        {
            var a = original.AllBlocks[i]; var b = reopened.AllBlocks[i];
            if (a.Key != b.Key || a.Type != b.Type || a.SubType != b.SubType || a.Data.Length != b.Data.Length || !a.Data.SequenceEqual(b.Data) && !allowedBlocks.Contains(a.Key))
                throw new InvalidOperationException(T("Se detectó un cambio fuera de los datos que editaste."));
        }
        if (reopened.Money != (uint)Money) throw new InvalidOperationException(T("El dinero guardado no coincide con el solicitado."));
        var oldItems = original.Items.Data; var newItems = reopened.Items.Data;
        for (int offset = 0; offset < oldItems.Length; offset++)
            if (oldItems[offset] != newItems[offset] && !effectiveItems.ContainsKey((ushort)(offset / InventoryItem9a.SIZE)))
                throw new InvalidOperationException(T("Se modificó un objeto que no editaste."));
        foreach (var change in effectiveItems)
        {
            var oldItem = original.Items.GetItem(change.Key); var actual = reopened.Items.GetItem(change.Key);
            if (actual.Count != change.Value || actual.Pouch != MyItem9a.GetPouchIndex(MyItem9a.GetType(change.Key))
                || actual.Padding != oldItem.Padding || ((actual.Flags ^ oldItem.Flags) & ~(ItemStorage9ZA.IsUniqueHeldItem(change.Key) ? 0x15u : 0x5u)) != 0)
                throw new InvalidOperationException(T("No se pudo verificar la cantidad o los datos de un objeto."));
            if (ItemStorage9ZA.IsMegaStone(change.Key) && actual.IsHeld != (HeldCount(change.Key) != 0))
                throw new InvalidOperationException(T("No se pudo verificar la asignación de una megapiedra."));
        }
        foreach (var e in Pokemon)
        {
            var actual = e.IsParty ? reopened.GetPartySlotAtIndex(e.Index) : reopened.GetBoxSlotAtIndex(e.Index);
            var expected = e.Dirty ? Catalog.Apply(e) : e.Original;
            if (!actual.ChecksumValid || !actual.Data.SequenceEqual(expected.Data))
                throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", e.DisplayName));
        }
        // Check every empty box slot as well, so editing never adds or removes Pokémon.
        var beforeBoxes = original.BoxData; var afterBoxes = reopened.BoxData;
        for (int i = 0; i < beforeBoxes.Count; i++)
            if (beforeBoxes[i].Species == 0 && !beforeBoxes[i].Data.SequenceEqual(afterBoxes[i].Data))
                throw new InvalidOperationException(T("Se modificó un espacio vacío de las cajas."));
        return bytes;
    }

    public string Write(string destination, string backupRoot, Func<SaveFile, string?>? findEmulator = null)
    {
        findEmulator ??= RunningEmulator;
        if (findEmulator(Save) is { } emulator) throw new InvalidOperationException(T("Cierra {0} antes de guardar. Después vuelve a intentarlo.", emulator));
        if (!File.Exists(FilePath) || Hash(File.ReadAllBytes(FilePath)) != OriginalHash)
            throw new InvalidOperationException(T("La partida cambió fuera de la app. Ábrela de nuevo para cargar el progreso más reciente."));
        var bytes = BuildOutput();
        destination = Path.GetFullPath(destination);
        var existing = File.Exists(destination) ? File.ReadAllBytes(destination) : null;
        var backupDir = NewBackupFolder(backupRoot);
        Directory.CreateDirectory(backupDir);
        var backupFile = Path.Combine(backupDir, Path.GetFileName(FilePath) + ".original");
        File.WriteAllBytes(backupFile, OriginalBytes);
        if (existing != null && Hash(existing) != OriginalHash) File.WriteAllBytes(Path.Combine(backupDir, "destino.anterior"), existing);
        File.WriteAllText(Path.Combine(backupDir, "cambios.json"), JsonSerializer.Serialize(new {
            game = Rules.FullName, source = FilePath, destination, originalHash = OriginalHash, editedHash = Hash(bytes),
            money = new { before = Save.Money, after = Money },
            items = EffectiveItemChanges().Select(change => new { id = change.Key, name = Rules.ItemName(change.Key),
                before = Items.FirstOrDefault(i => i.Id == change.Key)?.OriginalCount ?? 0, after = change.Value }),
            pokemon = Pokemon.Where(x => x.Dirty).Select(e => new {
                e.DisplayName, e.SpeciesName, e.Location, before = e.Baseline, after = e.Edit,
            }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (Hash(File.ReadAllBytes(backupFile)) != OriginalHash) throw new IOException(T("No se pudo verificar el respaldo."));
        var temp = destination + ".editrainer-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            // Check again immediately before the atomic replacement.
            if (findEmulator(Save) != null || Hash(File.ReadAllBytes(FilePath)) != OriginalHash)
                throw new InvalidOperationException(T("La partida o el emulador cambió mientras se guardaba. No se reemplazó el archivo."));
            var now = File.Exists(destination) ? File.ReadAllBytes(destination) : null;
            if ((existing == null) != (now == null) || existing != null && now != null && Hash(existing) != Hash(now))
                throw new InvalidOperationException(T("El archivo de destino cambió mientras se guardaba. No se reemplazó."));
            if (existing != null) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
            if (Hash(File.ReadAllBytes(destination)) != Hash(bytes)) throw new IOException(T("La verificación final no coincide. Conserva el respaldo."));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return backupDir;
    }

    // The program that could overwrite this save, if one is open.
    public static string? RunningEmulator(SaveFile save)
    {
        var names = (save.Generation, save.Context) switch {
            (_, EntityContext.Gen7b) or ( >= 8, _) => SwitchEmulators,
            ( >= 6, _) => N3dsEmulators,
            ( >= 4, _) => DsEmulators,
            _ when save is SAV3Colosseum or SAV3XD => GameCubeEmulators,
            _ => GameBoyEmulators,
        };
        var processes = Process.GetProcesses();
        try
        {
            return processes.Select(p => {
                try { return p.ProcessName; } catch (InvalidOperationException) { return ""; }
            }).FirstOrDefault(n => names.Concat(Everywhere).Any(e => n.StartsWith(e, StringComparison.OrdinalIgnoreCase)));
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }
}

public sealed record FoundSave(string Path, string Game, string Trainer, DateTime LastWrite)
{
    public override string ToString() => $"{Game}  ·  {Trainer}  ·  {LastWrite:g}";
}

/// <summary>Looks for saves where emulators keep them.</summary>
public static class SaveLocator
{
    const int DeepSearchDepth = 7, LargestSave = 64_000_000, SmallestSave = 0x2000;
    static readonly string[] SwitchTitles = ["0100F43008C44000", "0100A3D008C5C000", "01008F6008C5E000", "01001F5010DFA000", "0100ABF008968000",
        "01008DB008C2C000", "0100000011D90000", "010018E011D92000", "010003F003A34000", "0100187003A36000"];
    static readonly string[] SwitchFiles = ["main", "savedata.bin", "SaveData.bin"];
    static readonly string[] Extensions = [".sav", ".srm", ".dsv", ".sa1", ".sa2", ".sa3", ".gci"];
    static readonly string[] SkippedFolders = ["Backups", "Editrainer", "PokeEditorZA", "RPCS3", ".pokemon-tools", "shader", "load", "dump"];

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    static IEnumerable<string> Files(string root, string pattern, int depth = 12)
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, pattern, new EnumerationOptions {
            RecurseSubdirectories = true, MaxRecursionDepth = depth, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive,
        });
    }

    static IEnumerable<string> SwitchSaves(string parent)
    {
        var folders = new[] { "eden", "yuzu", "suyu", "citron", "sudachi", "torzu" }.Select(n => Path.Combine(AppData, n, "nand", "user", "save"))
            .Prepend(Path.Combine(parent, "Eden", "user", "nand", "user", "save"));
        var byTitle = folders.SelectMany(root => SwitchFiles.SelectMany(name => Files(root, name)))
            .Where(p => SwitchTitles.Any(t => p.Contains(t, StringComparison.OrdinalIgnoreCase)));
        return byTitle.Concat(SwitchFiles.SelectMany(name => Files(Path.Combine(AppData, "Ryujinx", "bis", "user", "save"), name)));
    }

    static IEnumerable<string> OtherEmulatorSaves()
    {
        string[] folders = [Path.Combine(AppData, "RetroArch", "saves"), Path.Combine(AppData, "mGBA"), Path.Combine(AppData, "visualboyadvance-m"),
            Path.Combine(AppData, "melonDS"), Path.Combine(Documents, "DeSmuME"), Path.Combine(Documents, "Dolphin Emulator", "GC")];
        var carts = folders.SelectMany(root => Extensions.SelectMany(ext => Files(root, "*" + ext)));
        var n3ds = new[] { "Citra", "Azahar", "Lime3DS", "Mandarine" }.SelectMany(n => Files(Path.Combine(AppData, n, "sdmc"), "main"));
        return carts.Concat(n3ds);
    }

    static IEnumerable<string> NearbySaves(string parent) => Extensions.SelectMany(ext => Files(parent, "*" + ext, DeepSearchDepth))
        .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase)));

    static string ParentOf(string appRoot) => Directory.GetParent(appRoot.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? appRoot;

    static FoundSave? Identify(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length is < SmallestSave or > LargestSave) return null;
            if (SaveUtil.GetSaveFile(File.ReadAllBytes(path), path) is not { } save) return null;
            return new(path, new GameRules(save).FullName, save.OT, info.LastWriteTime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return null; }
    }

    /// <summary>The newest save in the emulators' own folders; quick enough to run at startup.</summary>
    public static string? FindNewest(string appRoot) => SwitchSaves(ParentOf(appRoot)).Concat(OtherEmulatorSaves()).Distinct()
        .OrderByDescending(File.GetLastWriteTimeUtc).Select(Identify).FirstOrDefault(found => found != null)?.Path;

    /// <summary>Every save found, also searching the folder that holds the app.</summary>
    public static List<FoundSave> FindAll(string appRoot)
    {
        string parent = ParentOf(appRoot);
        return SwitchSaves(parent).Concat(OtherEmulatorSaves()).Concat(NearbySaves(parent)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Identify).OfType<FoundSave>().OrderByDescending(f => f.LastWrite).ToList();
    }
}
