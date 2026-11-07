using PKHeX.Core;
using System.Buffers.Binary;
using System.Globalization;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>Renamed boxes, as pending changes. They reach a save only through <see cref="WriteTo"/>.</summary>
public sealed class BoxNameEditor(GameRules rules)
{
    // What each game's own keyboard lets you type, which is less than the field could hold.
    const int ClassicLength = 8, HandheldLength = 14, SwitchLength = 16;
    readonly SaveFile save = rules.Save;
    readonly Dictionary<int, string> changes = [];
    SaveFile? scratch;

    // The first games fix their names, Let's Go has none, and the rest have no test file to prove a write on.
    public static bool IsKept(SaveFile save) => save is IBoxDetailName and (SAV2 or SAV3 or SAV4 or SAV5 or SAV6 or SAV7 or SAV8SWSH or SAV8BS or SAV8LA or SAV9SV or SAV9ZA);

    public int MaxLength => save.Generation switch { 6 or 7 => HandheldLength, >= 8 => SwitchLength, _ => ClassicLength };
    public int BoxCount => save.BoxCount;
    public int ChangeCount => changes.Count;
    public IEnumerable<int> ChangedBoxes => changes.Keys.Order();
    public string Original(int box) => rules.BoxName(box);
    public string? Pending(int box) => changes.GetValueOrDefault(box);
    public string Get(int box) => Pending(box) ?? Original(box);
    public void Reset() => changes.Clear();

    public void Set(int box, string name)
    {
        name = name.Trim();
        if (name == Original(box)) changes.Remove(box);
        else changes[box] = name;
    }

    public string? Validate()
    {
        foreach (int box in ChangedBoxes)
        {
            string name = changes[box];
            if (name.Length == 0) return T("El nombre de una caja no puede quedar vacío.");
            if (name.Length > MaxLength) return T("El nombre de una caja admite hasta {0} caracteres.", MaxLength);
            if (!Fits(box, name)) return T("Ese nombre tiene caracteres que este juego no admite.");
        }
        return null;
    }

    // Each game has its own alphabet, and whatever it can't store just disappears.
    bool Fits(int box, string name)
    {
        scratch ??= save.Clone();
        var names = (IBoxDetailName)scratch;
        names.SetBoxName(box, name);
        return names.GetBoxName(box) == name;
    }

    internal void WriteTo(SaveFile output)
    {
        foreach (int box in ChangedBoxes) ((IBoxDetailName)output).SetBoxName(box, changes[box]);
    }

    /// <summary>The first box that doesn't read back as expected, if any.</summary>
    internal int? FirstDifference(SaveFile reopened)
    {
        var before = (IBoxDetailName)save; var after = (IBoxDetailName)reopened;
        for (int box = 0; box < BoxCount; box++)
            if (after.GetBoxName(box) != (Pending(box) ?? before.GetBoxName(box))) return box;
        return null;
    }
}

/// <summary>A Pokémon left in a daycare. Some games have two daycares, each with two places.</summary>
public sealed record DaycareGuest(int Daycare, int Slot, PKM Pokemon, uint Experience)
{
    public bool IsReadable => Pokemon.Species != 0 && Pokemon.ChecksumValid;
}

/// <summary>
/// Who is in the daycare, and who leaves it when the game is saved. A Pokémon that leaves is a new arrival in
/// the boxes, so box undo takes it back here, and its place is only emptied while it is still in the boxes.
/// </summary>
public sealed class DaycareEditor
{
    const byte MonsCompatible = 1 << 5;   // Gen 2 keeps this in the day-care man's byte, and clears it when either Pokémon leaves
    readonly SaveDocument document;
    readonly SaveFile save;
    readonly Dictionary<DaycareGuest, PokemonEntry> entries = [];

    internal DaycareEditor(SaveDocument document)
    {
        this.document = document; save = document.Save;
        var places = Places(save);
        DaycareCount = places.Count;
        Guests = places.SelectMany((place, daycare) => Enumerable.Range(0, place.DaycareSlotCount).Where(place.IsDaycareOccupied)
            .Select(slot => new DaycareGuest(daycare, slot, Read(save, place, slot), Experience(place, slot)))).ToList();
    }

    // Colosseum and XD keep a single place of their own, and no test file can prove a write on them.
    public static bool IsKept(SaveFile save) => Places(save).Count != 0;

    public int DaycareCount { get; }
    public IReadOnlyList<DaycareGuest> Guests { get; }
    /// <summary>Sword and Shield don't say whether an egg is waiting, and PKHeX has no way to empty their places or Let's Go's.</summary>
    public bool CanWithdraw => save is not (SAV8SWSH or SAV7b) && document.CanArrange;
    public bool HasEggState => DaycareCount != 0 && Places(save)[0] is IDaycareEggState;
    public bool HasEgg(int daycare) => Places(save)[daycare] is IDaycareEggState { IsEggAvailable: true };
    /// <summary>Gen 2 is the only one that keeps the egg itself rather than a mark for it.</summary>
    public PKM? WaitingEgg => save is SAV2 two && two.IsEggAvailable ? two.GetStoredSlot(two.GetDaycareEgg().Span) : null;
    public IEnumerable<DaycareGuest> Leaving => Guests.Where(IsLeaving);
    public int LeavingCount => Leaving.Count();
    public bool IsLeaving(DaycareGuest guest) => EntryOf(guest) != null;
    /// <summary>Where it went, as long as it is still there.</summary>
    public PokemonEntry? EntryOf(DaycareGuest guest) => entries.TryGetValue(guest, out var entry) && document.Pokemon.Contains(entry) ? entry : null;
    internal IEnumerable<PokemonEntry> Withdrawn => entries.Values.Where(document.Pokemon.Contains);

    // Each daycare of a save in PKHeX's shape. Sword and Shield and Let's Go keep theirs in one of their own.
    static List<IDaycareStorage> Places(SaveFile save) => save switch {
        SAV8SWSH s => [new GalarDaycare(s, 0), new GalarDaycare(s, 1)],
        SAV7b s => [new KantoDaycare(s.Daycare)],
        SAV6AO s => [s[0], s[1]],
        SAV1 or SAV2 or SAV3 or SAV4 or SAV5 or SAV6XY or SAV7 or SAV8BS => [(IDaycareStorage)save],
        _ => [],
    };

    // Let's Go's own reader expects a whole box slot, which its daycare is too short for.
    static PKM Read(SaveFile save, IDaycareStorage place, int slot) => save is SAV7b ? new PB7(place.GetDaycareSlot(slot).ToArray()) : save.GetStoredSlot(place.GetDaycareSlot(slot).Span);

    static uint Experience(IDaycareStorage place, int slot) => (place as IDaycareExperience)?.GetDaycareEXP(slot) ?? 0;

    /// <summary>What keeps a Pokémon in the daycare, if anything.</summary>
    public string? WhyStays(DaycareGuest guest)
    {
        if (!CanWithdraw) return save is SAV8SWSH or SAV7b ? T("En este juego la guardería solo se puede consultar: PKHeX no sabe vaciar sus lugares.") : T("Esta partida no permite reorganizar sus Pokémon.");
        if (!guest.IsReadable) return T("Ese Pokémon tiene datos dañados.");
        // The game gives no way to take a parent back while an egg waits, and it reads the parents to make it.
        if (HasEgg(guest.Daycare)) return T("Hay un huevo esperando. Recógelo en el juego antes de sacar a sus padres.");
        // The letter stays behind in the daycare's own data, so neither one can go without it, nor be moved down.
        foreach (var other in Guests.Where(g => g.Daycare == guest.Daycare && (g == guest || guest.Slot == 0)))
            if (document.Rules.IsMail(other.Pokemon.HeldItem))
                return T("{0} lleva una carta. Quítasela en el juego antes de cambiarlo de lugar.", Name(other));
        return null;
    }

    public string Name(DaycareGuest guest) => guest.Pokemon.IsNicknamed && guest.Pokemon.Nickname.Length != 0 ? guest.Pokemon.Nickname : SpeciesName(guest.Pokemon.Species);

    public static string SpeciesName(ushort species) => species < Catalog.Names.specieslist.Length ? Catalog.Names.specieslist[species] : species.ToString();

    // The same object each time, so that box undo can bring it back after it went back and left again.
    internal PokemonEntry Entry(DaycareGuest guest, SlotPlace place)
    {
        if (entries.TryGetValue(guest, out var entry)) { entry.IsParty = place.IsParty; entry.Index = place.Index; return entry; }
        var pokemon = guest.Pokemon.Clone();
        pokemon.ForcePartyData();   // Let's Go and Brilliant Diamond keep these numbers in the boxes too
        pokemon.RefreshChecksum();
        return entries[guest] = new PokemonEntry(document.Rules, place.IsParty, place.Index, pokemon, isNew: true);
    }

    // The games read a lone Pokémon from the first place, so whoever stays moves there. In Gen 2 each place is one person's.
    int SlotAfter(DaycareGuest guest) => guest.Slot == 1 && save is not SAV2 && Leaving.Any(g => g.Daycare == guest.Daycare && g.Slot == 0) ? 0 : guest.Slot;

    internal void WriteTo(SaveFile output)
    {
        var places = Places(output);
        foreach (var guest in Leaving) Vacate(output, places[guest.Daycare], guest.Slot);
        foreach (var guest in Guests.Where(g => !IsLeaving(g) && SlotAfter(g) != g.Slot)) MoveDown(output, places[guest.Daycare]);
    }

    static void Vacate(SaveFile output, IDaycareStorage place, int slot)
    {
        place.SetDaycareOccupied(slot, false);
        if (output is SAV2 two) two.DaycareFlagByte(0) &= unchecked((byte)~MonsCompatible);
        if (output is SAV1 or SAV2) return;   // these two only look at the flag, and pack what is left into a list
        place.GetDaycareSlot(slot).Span.Clear();
        (place as IDaycareExperience)?.SetDaycareEXP(slot, 0);
    }

    static void MoveDown(SaveFile output, IDaycareStorage place)
    {
        place.GetDaycareSlot(1).Span.CopyTo(place.GetDaycareSlot(0).Span);
        place.SetDaycareOccupied(0, true);
        if (place is IDaycareExperience counter) counter.SetDaycareEXP(0, counter.GetDaycareEXP(1));
        Vacate(output, place, 1);
    }

    internal bool Matches(SaveFile reopened)
    {
        var after = Places(reopened);
        for (int daycare = 0; daycare < after.Count; daycare++)
        {
            var place = after[daycare];
            if (place is IDaycareEggState egg && egg.IsEggAvailable != HasEgg(daycare)) return false;
            var staying = Guests.Where(g => g.Daycare == daycare && !IsLeaving(g)).ToDictionary(SlotAfter);
            for (int slot = 0; slot < place.DaycareSlotCount; slot++)
            {
                if (place.IsDaycareOccupied(slot) != staying.TryGetValue(slot, out var guest)) return false;
                if (guest != null && (!Read(reopened, place, slot).Data.SequenceEqual(guest.Pokemon.Data) || Experience(place, slot) != guest.Experience)) return false;
            }
        }
        return true;
    }

    /// <summary>One of Galar's two daycares, seen through PKHeX's interface. Nothing here can empty a place.</summary>
    sealed class GalarDaycare(SAV8SWSH save, int daycare) : IDaycareStorage
    {
        public int DaycareSlotCount => 2;
        public Memory<byte> GetDaycareSlot(int index) => save.Daycare[daycare * 2 + index][..save.SIZE_STORED];
        public bool IsDaycareOccupied(int index) => daycare == 0 ? save.Daycare.GetDaycare1SlotOccupied(index) : save.Daycare.GetDaycare2SlotOccupied(index);
        public void SetDaycareOccupied(int index, bool occupied) => throw new InvalidOperationException();
    }

    /// <summary>Let's Go has one place and no eggs. PKHeX doesn't read its flag, so someone being there is what counts.</summary>
    sealed class KantoDaycare(Daycare7b block) : IDaycareStorage
    {
        public int DaycareSlotCount => 1;
        public Memory<byte> GetDaycareSlot(int index) => block.Stored;
        public bool IsDaycareOccupied(int index) => new PB7(block.Stored.ToArray()).Species != 0;
        public void SetDaycareOccupied(int index, bool occupied) => throw new InvalidOperationException();
    }
}

public enum GiftKind { Unknown, Pokemon, Item, UndergroundItem, BattlePoints, Clothing, Money }

/// <summary>A gift a save remembers. A negative slot is a mark of having received it, with no record left.</summary>
public sealed record GiftRecord(int Slot, int CardId, DateTime? Received, GiftKind Kind, ushort Species = 0, bool IsEgg = false, IReadOnlyList<(int Id, int Count)>? Items = null, uint Amount = 0)
{
    public bool HasRecord => Slot >= 0;
}

/// <summary>
/// The list of gifts a Switch game has received, and which ones to forget. PKHeX reads Brilliant Diamond and Shining
/// Pearl's list. The other three keep it in a block whose key PKHeX holds private, in a layout from community research,
/// so those are only touched when the block has the exact size of a retail save.
/// </summary>
public sealed class GiftEditor(GameRules rules)
{
    const uint GalarKey = 0x112D5141, TrimmedKey = 0x99E1625E;
    const int GalarSize = 0x17C8, TrimmedSize = 0x7EB0, GalarSlot = 0x68, TrimmedSlot = 0x278, SlotCount = 50, ScarletSlots = 32;
    const int BrilliantOffset = 0xE9BE8, BrilliantItems = 7, GiftItems = 6, RecordItemStep = 4, BlankTime = 4;
    const int CardType8a = 0x0F, CardType9 = 0x11, CardBody = 0x18, RecordBody = 0x10, RecordId = 0x08, RecordType = 0x0E;
    readonly SaveFile save = rules.Save;
    readonly HashSet<GiftRecord> forgetting = [];
    List<GiftRecord>? records;

    public static bool IsKept(SaveFile save) => Area(save).Length != 0;

    /// <summary>False where the layout doesn't come from PKHeX, which the window has to say.</summary>
    public bool IsDocumented => save is SAV8BS;
    public IReadOnlyList<GiftRecord> Records => records ??= Read();
    public int ChangeCount => forgetting.Count;
    public IEnumerable<GiftRecord> Forgetting => forgetting;
    public bool IsForgetting(GiftRecord gift) => forgetting.Contains(gift);
    public void Reset() => forgetting.Clear();

    public void SetForgetting(GiftRecord gift, bool forget)
    {
        if (forget) forgetting.Add(gift);
        else forgetting.Remove(gift);
    }

    // Where a save keeps its list, or nothing. A short Brilliant Diamond save gets a loose copy from PKHeX, which would write nowhere.
    static Memory<byte> Area(SaveFile save) => save switch {
        SAV8BS s when s.Buffer.Length >= BrilliantOffset + MysteryBlock8b.MinSize => s.Buffer.Slice(BrilliantOffset, MysteryBlock8b.OFS_ONEDAY),
        SAV8SWSH s => Block(s.Blocks, GalarKey, GalarSize),
        SAV8LA s => Block(s.Blocks, TrimmedKey, TrimmedSize),
        SAV9SV s => Block(s.Blocks, TrimmedKey, TrimmedSize),
        _ => default,
    };

    static Memory<byte> Block(SCBlockAccessor blocks, uint key, int size) => blocks.TryGetBlock(key, out var block) && block.Raw.Length == size ? block.Raw : default;

    int SlotSize => save switch { SAV8BS => RecvData8b.SIZE, SAV8SWSH => GalarSlot, _ => TrimmedSlot };
    int Slots => save is SAV9SV ? ScarletSlots : SlotCount;

    bool IsBlank(ReadOnlySpan<byte> slot) => save switch {
        // Unused records have 0xFF in the upper half of their date.
        SAV8SWSH => BinaryPrimitives.ReadUInt32LittleEndian(slot[BlankTime..]) == uint.MaxValue || !slot.ContainsAnyExcept((byte)0),
        SAV8BS => BinaryPrimitives.ReadInt64LittleEndian(slot) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(slot[RecordId..]) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(slot[RecordBody..]) == 0,
        _ => !slot.ContainsAnyExcept((byte)0),
    };

    List<GiftRecord> Read()
    {
        var list = new List<GiftRecord>();
        var area = Area(save);
        for (int i = 0; i < Slots; i++)
        {
            var slot = area.Slice(i * SlotSize, SlotSize);
            if (IsBlank(slot.Span)) continue;
            list.Add(save switch {
                SAV8BS => ReadBrilliant(slot, i),
                SAV8SWSH => ReadGalar(slot.Span, i),
                _ => ReadTrimmed(slot.Span, i),
            });
        }
        if (save is not SAV8BS) return list;
        var flags = area.Span.Slice(MysteryBlock8b.OFS_RECVFLAG, MysteryBlock8b.FlagSize);
        var known = list.Select(g => g.CardId).ToHashSet();
        for (int id = 0; id < MysteryBlock8b.FlagMax; id++)
            if (HasFlag(flags, id) && !known.Contains(id)) list.Add(new(-1, id, null, GiftKind.Unknown));
        return list;
    }

    // PKHeX's own flag accessors look at the wrong byte, so these go straight to the bits.
    static bool HasFlag(ReadOnlySpan<byte> flags, int id) => (flags[id >> 3] & 1 << (id & 7)) != 0;

    static GiftRecord ReadBrilliant(Memory<byte> slot, int index)
    {
        var r = new RecvData8b(slot);
        ushort[] items = [r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, r.Item7];
        ushort[] counts = [r.Item1Count, r.Item2Count, r.Item3Count, r.Item4Count, r.Item5Count, r.Item6Count, r.Item7Count];
        var kind = (WB8.GiftType)r.DataType switch {
            WB8.GiftType.Pokemon => GiftKind.Pokemon, WB8.GiftType.Item => GiftKind.Item, WB8.GiftType.UnderGroundItem => GiftKind.UndergroundItem,
            WB8.GiftType.BP => GiftKind.BattlePoints, WB8.GiftType.Clothing => GiftKind.Clothing, WB8.GiftType.Money => GiftKind.Money,
            _ => GiftKind.Unknown,
        };
        DateTime? date = null;
        try { if (r.Ticks != 0) date = r.LocalTimestamp; } catch (ArgumentOutOfRangeException) { }
        return new(index, r.DeliveryID, date, kind, r.Species, r.IsEgg == 1,
            Enumerable.Range(0, BrilliantItems).Where(i => items[i] != 0).Select(i => ((int)items[i], (int)counts[i])).ToList(), r.MoneyData);
    }

    static GiftRecord ReadGalar(ReadOnlySpan<byte> d, int index)
    {
        var kind = d[0x0C] switch { 1 => GiftKind.Pokemon, 2 => GiftKind.Item, 3 => GiftKind.BattlePoints, 4 => GiftKind.Clothing, 5 => GiftKind.Money, _ => GiftKind.Unknown };
        var items = new List<(int, int)>();
        if (kind == GiftKind.Item)
            for (int i = 0; i < Math.Min((int)d[0x0D], GiftItems); i++)
            {
                var item = d[(0x30 + i * RecordItemStep)..];
                if (BinaryPrimitives.ReadUInt16LittleEndian(item) is not 0 and var id) items.Add((id, BinaryPrimitives.ReadUInt16LittleEndian(item[2..])));
            }
        return new(index, BinaryPrimitives.ReadUInt16LittleEndian(d[RecordId..]), PackedDate(BinaryPrimitives.ReadUInt64LittleEndian(d)), kind,
            kind == GiftKind.Pokemon ? BinaryPrimitives.ReadUInt16LittleEndian(d[0x30..]) : (ushort)0, kind == GiftKind.Pokemon && d[0x12] == 1, items,
            kind is GiftKind.BattlePoints or GiftKind.Money ? BinaryPrimitives.ReadUInt16LittleEndian(d[0x46..]) : 0u);
    }

    // Year, month, day, hour, minute and second, from the top bits down.
    static DateTime? PackedDate(ulong value)
    {
        int year = (int)(value >> 26 & 0xFFF), month = (int)(value >> 22 & 0xF), day = (int)(value >> 17 & 0x1F);
        int hour = (int)(value >> 12 & 0x1F), minute = (int)(value >> 6 & 0x3F), second = (int)(value & 0x3F);
        return DateTime.TryParse($"{year:0000}-{month:00}-{day:00}T{hour:00}:{minute:00}:{second:00}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    // A record is a Wonder Card with its header trimmed, so it reads as one once the header is back.
    GiftRecord ReadTrimmed(ReadOnlySpan<byte> d, int index)
    {
        var raw = new byte[WC9.Size];
        d.Slice(RecordId, sizeof(uint)).CopyTo(raw.AsSpan(RecordId));
        d[RecordBody..].CopyTo(raw.AsSpan(CardBody));
        raw[save is SAV8LA ? CardType8a : CardType9] = d[RecordType];
        uint seconds = BinaryPrimitives.ReadUInt32LittleEndian(d);
        DateTime? date = seconds == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
        if (save is SAV8LA)
        {
            var card = new WA8(raw);
            var kind = card.CardType switch { WA8.GiftType.Pokemon => GiftKind.Pokemon, WA8.GiftType.Item => GiftKind.Item, WA8.GiftType.Clothing => GiftKind.Clothing, _ => GiftKind.Unknown };
            return new(index, card.CardID, date, kind, kind == GiftKind.Pokemon ? card.Species : (ushort)0, kind == GiftKind.Pokemon && card.IsEgg,
                kind == GiftKind.Item ? Items(card.GetItem, card.GetQuantity) : []);
        }
        else
        {
            var card = new WC9(raw);
            var kind = card.CardType switch { WC9.GiftType.Pokemon => GiftKind.Pokemon, WC9.GiftType.Item => GiftKind.Item, WC9.GiftType.BP => GiftKind.BattlePoints, WC9.GiftType.Clothing => GiftKind.Clothing, _ => GiftKind.Unknown };
            return new(index, card.CardID, date, kind, kind == GiftKind.Pokemon ? card.Species : (ushort)0, kind == GiftKind.Pokemon && card.IsEgg,
                kind == GiftKind.Item ? Items(card.GetItem, card.GetQuantity) : []);
        }
    }

    static List<(int, int)> Items(Func<int, int> item, Func<int, int> quantity) => Enumerable.Range(0, GiftItems).Where(i => item(i) != 0).Select(i => (item(i), quantity(i))).ToList();

    /// <summary>What a gift gave, in words.</summary>
    public string Contents(GiftRecord gift) => gift.Kind switch {
        GiftKind.Pokemon => gift.IsEgg ? T("Huevo de {0}", DaycareEditor.SpeciesName(gift.Species)) : DaycareEditor.SpeciesName(gift.Species),
        GiftKind.Item when gift.Items is { Count: > 0 } items => string.Join(", ", items.Select(i => $"{rules.ItemName(i.Id)} × {i.Count}")),
        GiftKind.Item => T("Objeto"),
        GiftKind.UndergroundItem => T("Objeto del Subsuelo"),
        GiftKind.BattlePoints => gift.Amount != 0 ? T("{0} PB", gift.Amount) : T("Puntos de batalla"),
        GiftKind.Clothing => T("Ropa"),
        GiftKind.Money => T("{0} de dinero", gift.Amount),
        _ => gift.HasRecord ? T("Regalo desconocido") : T("Solo queda la marca de recibido"),
    };

    // The list closes up behind what leaves, and what it frees looks like the free records this save already has.
    byte[] Updated()
    {
        var area = Area(save).ToArray();
        int size = SlotSize;
        var last = area.AsSpan((Slots - 1) * size, size);
        var blank = IsBlank(last) ? last.ToArray() : new byte[size];
        var gone = forgetting.Where(g => g.HasRecord).Select(g => g.Slot).ToHashSet();
        int write = 0;
        for (int read = 0; read < Slots; read++)
        {
            if (gone.Contains(read)) continue;
            if (read != write) area.AsSpan(read * size, size).CopyTo(area.AsSpan(write * size));
            write++;
        }
        for (; write < Slots; write++) blank.CopyTo(area.AsSpan(write * size));
        if (save is not SAV8BS) return area;
        var flags = area.AsSpan(MysteryBlock8b.OFS_RECVFLAG, MysteryBlock8b.FlagSize);
        var kept = Records.Except(forgetting).Select(g => g.CardId).ToHashSet();
        foreach (var gift in forgetting.Where(g => g.CardId < MysteryBlock8b.FlagMax && !kept.Contains(g.CardId)))
            flags[gift.CardId >> 3] &= (byte)~(1 << (gift.CardId & 7));
        return area;
    }

    internal void WriteTo(SaveFile output)
    {
        if (ChangeCount != 0) Updated().CopyTo(Area(output));
    }

    internal bool Matches(SaveFile reopened) => ChangeCount == 0 || Area(reopened).Span.SequenceEqual(Updated());
}

public sealed record FameMember(ushort Species, string Nickname, int Level, bool IsShiny);

/// <summary>One visit to the Hall of Fame. Some games only keep part of it.</summary>
public sealed record FameEntry(string Title, DateOnly? Date, IReadOnlyList<FameMember> Team);

/// <summary>The Hall of Fame of the games where PKHeX can read it. Nothing here writes.</summary>
public static class FameRecords
{
    const int TeamSize = 6, FirstYear = 2000;

    public static bool IsKept(SaveFile save) => save is SAV1 or SAV3 { IsFullSaveFile: true } or SAV6XY or SAV6AO or SAV7 or SAV8SWSH;

    public static List<FameEntry> Read(SaveFile save) => save switch {
        SAV1 s => Read1(s),
        SAV3 s => Read3(s),
        SAV6XY s => Read6(s.HallOfFame, s),
        SAV6AO s => Read6(s.HallOfFame, s),
        SAV7 s => Read7(s),
        SAV8SWSH s => Read8(s),
        _ => [],
    };

    // A game's memory can hold anything until it first writes here.
    static bool Plausible(SaveFile save, ushort species, int level) => species != 0 && species <= save.MaxSpeciesID && level <= Experience.MaxLevel;

    static string Numbered(int number) => T("Entrada {0}", number);

    static List<FameEntry> Read1(SAV1 save)
    {
        var entries = new List<FameEntry>();
        var fame = save.HallOfFame;
        // Past fifty visits the oldest ones are gone, and the count goes on.
        int stored = Math.Min((int)save.HallOfFameCount, HallOfFameReader1.TeamCount), first = save.HallOfFameCount - stored;
        for (int team = 0; team < stored; team++)
        {
            var members = new List<FameMember>();
            for (int slot = 0; slot < fame.GetTeamMemberCount(team); slot++)
            {
                var member = fame.GetEntity(team, slot);
                if (Plausible(save, member.Species, member.Level)) members.Add(new(member.Species, member.Nickname, member.Level, false));
            }
            if (members.Count != 0) entries.Add(new(Numbered(first + team + 1), null, members));
        }
        return entries;
    }

    static List<FameEntry> Read3(SAV3 save)
    {
        var entries = new List<FameEntry>();
        var teams = HallFame3Entry.GetEntries(save);
        for (int i = 0; i < teams.Length; i++)
        {
            var members = teams[i].Team.Where(m => Plausible(save, m.Species, m.Level)).Select(m => new FameMember(m.Species, m.Nickname, m.Level, m.IsShiny)).ToList();
            if (members.Count != 0) entries.Add(new(Numbered(i + 1), null, members));
        }
        return entries;
    }

    static List<FameEntry> Read6(HallOfFame6 fame, SaveFile save)
    {
        var entries = new List<FameEntry>();
        for (int i = 0; i < HallOfFame6.Entries; i++)
        {
            var index = new HallFame6Index(fame.GetEntry(i)[^HallFame6Index.SIZE..]);
            if (!index.HasData) continue;
            var members = new List<FameMember>();
            for (int slot = 0; slot < HallOfFame6.PokeCount; slot++)
            {
                var member = new HallFame6Entity(fame.GetEntity(i, slot), save.Language);
                if (Plausible(save, member.Species, (int)member.Level)) members.Add(new(member.Species, member.Nickname, (int)member.Level, member.IsShiny));
            }
            entries.Add(new(Numbered((int)index.ClearIndex + 1), Date(FirstYear + (int)index.Year, (int)index.Month, (int)index.Day), members));
        }
        return entries;
    }

    static DateOnly? Date(int year, int month, int day) => DateOnly.TryParseExact($"{year:0000}-{month:00}-{day:00}", "yyyy-MM-dd", out var date) ? date : null;

    // Alola keeps only the species: the team of the first visit, then the latest.
    static List<FameEntry> Read7(SAV7 save)
    {
        var fame = save.EventWork.Fame;
        var entries = new List<FameEntry>();
        foreach (var (title, first) in new[] { (T("Primera vez"), 0), (T("Última vez"), TeamSize) })
        {
            var members = Enumerable.Range(first, TeamSize).Select(fame.GetEntry).Where(s => Plausible(save, s, 0)).Select(s => new FameMember(s, "", 0, false)).ToList();
            if (members.Count != 0) entries.Add(new(title, null, members));
        }
        return entries;
    }

    // Galar keeps only the day.
    static List<FameEntry> Read8(SAV8SWSH save)
    {
        var time = save.Blocks.FameTime;
        return time.TimeStamp <= 0 ? [] : [new(T("Primera vez"), DateOnly.FromDateTime(time.Date), [])];
    }
}

/// <summary>The tools that belong to one game or another, and how a save reaches them.</summary>
public sealed partial class SaveDocument
{
    BoxNameEditor? boxNames;
    DaycareEditor? daycare;
    GiftEditor? gifts;

    public BoxNameEditor BoxNames => boxNames ??= new(Rules);
    public DaycareEditor Daycare => daycare ??= new(this);
    public GiftEditor Gifts => gifts ??= new(Rules);
    /// <summary>A Pokémon leaving the daycare already counts as a change of the boxes.</summary>
    public int ExtraChangeCount => (boxNames?.ChangeCount ?? 0) + (gifts?.ChangeCount ?? 0);
    IEnumerable<PokemonEntry> Withdrawn => daycare?.Withdrawn ?? [];

    /// <summary>The name a box will have once saved.</summary>
    public string BoxName(int box) => boxNames?.Pending(box) is { Length: > 0 } name ? name : Rules.BoxName(box);

    /// <summary>Takes a Pokémon out of the daycare, to the first free place from this box on. It keeps what it holds.</summary>
    public Arrival Withdraw(DaycareGuest guest, int box)
    {
        var editor = Daycare;
        if (editor.IsLeaving(guest)) throw new InvalidOperationException(T("{0} ya sale de la guardería.", editor.Name(guest)));
        if (editor.WhyStays(guest) is { } reason) throw new InvalidOperationException(reason);
        if (FreePlace(box) is not { } place) throw new InvalidOperationException(T("Las cajas están llenas."));
        RequireFree(place);
        var entry = editor.Entry(guest, place);
        Change(() => Pokemon.Add(entry));
        return new(entry, T("{0} sale de la guardería hacia {1}. Guarda para aplicarlo.", entry.DisplayName, entry.Location));
    }

    /// <summary>Leaves a Pokémon in the daycare after all.</summary>
    public void ReturnToDaycare(DaycareGuest guest)
    {
        if (Daycare.EntryOf(guest) is { } entry) Change(() => Pokemon.Remove(entry));
    }

    string? ValidateExtras() => boxNames?.Validate();

    void WriteExtras(SaveFile output)
    {
        boxNames?.WriteTo(output); daycare?.WriteTo(output); gifts?.WriteTo(output);
    }

    void CheckExtras(SaveFile reopened)
    {
        if (boxNames?.ChangeCount > 0 && boxNames.FirstDifference(reopened) is { } box) throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", BoxName(box)));
        if (daycare?.Matches(reopened) == false) throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", T("Guardería")));
        if (gifts?.Matches(reopened) == false) throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", T("Regalos recibidos")));
    }

    /// <summary>For the record kept with each backup: a line for each thing that isn't a Pokémon, an item or the money.</summary>
    IEnumerable<string> ExtraSummary()
    {
        if (boxNames != null)
            foreach (int box in boxNames.ChangedBoxes) yield return $"{T("Caja {0}", box + 1)}: {boxNames.Original(box)} → {boxNames.Get(box)}";
        if (daycare != null)
            foreach (var guest in daycare.Leaving) yield return $"{T("Guardería")}: {daycare.Name(guest)} → {daycare.EntryOf(guest)!.Location}";
        if (gifts != null)
            foreach (var gift in gifts.Forgetting) yield return $"{T("Regalos recibidos")}: {gift.CardId} ({gifts.Contents(gift)})";
    }
}
