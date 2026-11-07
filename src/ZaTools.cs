using PKHeX.Core;
using System.Buffers.Binary;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>A number Z-A keeps in a block of its own, such as Royale points.</summary>
public sealed class ZaCounter(uint key, string name, uint original)
{
    public uint Key { get; } = key;
    public string Name { get; } = name;
    public uint Original { get; } = original;
    public uint Value { get; set; } = original;
    public bool Changed => Value != Original;
}

/// <summary>One kind of clothing: what the save has on record and how much of it is owned.</summary>
public sealed record ZaClothing(uint Key, string Name, int Known, int Owned)
{
    public int Missing => Known - Owned;
}

public sealed record ZaDonut(int Slot, string Name, int Stars, string Powers);

public sealed record ZaPower(ulong Hash, string Label)
{
    public override string ToString() => Label;
}

/// <summary>What the Z-A tools have prepared. Nothing here reaches the save until it is written, and each part is checked after reopening it.</summary>
public sealed class ZaEdits
{
    public const int MaxPoints = 9_999_999, PocketSize = DonutPocket9a.MaxCount, PowersPerDonut = 3;
    // The figure PKHeX's own ColorfulScrew9a.CollectScrews stops at. Its bag gives the screw no maximum of its own.
    const int MostScrews = 100;
    const int FieldItemSize = 16, ShinyTypes = 19, FirstShinyPower = 3, FirstCatchingPower = 4;
    const string TopLevel = "_lv3";
    // PKHeX keeps these two keys to itself, so each one has to prove it is the block PKHeX reads.
    internal const uint DonutBlock = 0xBE007476, FieldItemBlock = 0x2482AD60;

    static readonly (uint Key, string Name)[] Counters = [
        (SaveBlockAccessor9ZA.KTicketPointsZARoyale, "Puntos de Royale"),
        (SaveBlockAccessor9ZA.KTicketPointsZARoyaleInfinite, "Puntos de Royale Infinita"),
        (SaveBlockAccessor9ZA.KHyperspaceSurveyPoints, "Puntos de investigación del hiperespacio"),
    ];
    static readonly (uint Key, string Name)[] Wardrobe = [
        (SaveBlockAccessor9ZA.KFashionTops, "Camisetas"), (SaveBlockAccessor9ZA.KFashionBottoms, "Pantalones y faldas"),
        (SaveBlockAccessor9ZA.KFashionAllInOne, "Conjuntos"), (SaveBlockAccessor9ZA.KFashionHeadwear, "Sombreros"),
        (SaveBlockAccessor9ZA.KFashionEyewear, "Gafas"), (SaveBlockAccessor9ZA.KFashionGloves, "Guantes"),
        (SaveBlockAccessor9ZA.KFashionLegwear, "Calcetines"), (SaveBlockAccessor9ZA.KFashionFootwear, "Calzado"),
        (SaveBlockAccessor9ZA.KFashionSatchels, "Bandoleras"), (SaveBlockAccessor9ZA.KFashionEarrings, "Pendientes"),
    ];
    static readonly Dictionary<string, ulong> PowerHashes = DonutInfo.Flavors.ToDictionary(f => f.Name, f => f.Hash);

    readonly SaveDocument document;
    readonly SAV9ZA save, stage;
    readonly List<ZaCounter> counters;
    readonly List<ZaClothing> clothing;
    readonly HashSet<uint> unlocking = [];
    readonly List<string> screwsLeft;
    readonly List<(string FieldItem, ushort Item)> machinesLeft;
    readonly Dictionary<ushort, int> machinesBefore = [];
    readonly Random random;
    readonly bool pocketFound, pocketKnown, fieldItemsKnown;

    internal ZaEdits(SaveDocument document, SAV9ZA save, Random? random = null, uint donutBlock = DonutBlock, uint fieldItemBlock = FieldItemBlock)
    {
        this.document = document; this.save = save; this.random = random ?? new Random(); stage = (SAV9ZA)save.Clone();
        pocketFound = save.Donuts.Data.Length >= PocketSize * Donut9a.Size;
        pocketKnown = IsBlock(save, donutBlock, save.Donuts);
        fieldItemsKnown = IsBlock(save, fieldItemBlock, save.Blocks.FieldItems);
        counters = Counters.Where(c => save.Blocks.TryGetBlock(c.Key, out var block) && block.Type == SCTypeCode.UInt32)
            .Select(c => new ZaCounter(c.Key, c.Name, save.GetValue<uint>(c.Key))).ToList();
        clothing = Wardrobe.Where(w => save.Blocks.TryGetBlock(w.Key, out _)).Select(w => {
            var known = FashionItem9a.GetArray(save.Blocks.GetBlock(w.Key).Data).Where(i => i.Value != FashionItem9a.None).ToList();
            return new ZaClothing(w.Key, w.Name, known.Count, known.Count(i => i.IsOwned));
        }).ToList();
        // A save only lists the field items of the places its player has been to, so these are rarely all of them.
        screwsLeft = fieldItemsKnown ? ColorfulScrew9a.GetScrewLocations(save, false).Select(s => s.FieldItem).ToList() : [];
        machinesLeft = !fieldItemsKnown ? [] : TechnicalMachine9a.TechnicalMachines.Where(m => IsWaiting(m.FieldItem) && document.Items.Any(i => i.Id == m.ItemID))
            .Select(m => (m.FieldItem, m.ItemID)).ToList();
    }

    // Writing through a key that isn't this block would only come to light as a rejected save.
    internal static bool IsBlock(SAV9ZA save, uint key, IDataIndirect storage) => save.Blocks.TryGetBlock(key, out var block) && storage.Equals(block.Raw);

    bool IsWaiting(string fieldItem)
    {
        var field = save.Blocks.FieldItems;
        int index = field.GetIndex(FnvHash.HashFnv1a_64(fieldItem));
        return index >= 0 && !field.GetValue(index);
    }

    public IReadOnlyList<ZaCounter> Points => counters;
    public IReadOnlyList<ZaClothing> Clothing => clothing;
    public bool HasChanges => ChangeCount != 0;
    public int ChangeCount => counters.Count(c => c.Changed) + (DonutsChanged ? 1 : 0) + unlocking.Count
        + (CollectingScrews ? 1 : 0) + (CollectingMachines ? 1 : 0);

    public void Reset()
    {
        foreach (var counter in counters) counter.Value = counter.Original;
        save.Donuts.Data.CopyTo(stage.Donuts.Data);
        unlocking.Clear(); CollectingScrews = false; SetCollectingMachines(false);
    }

    #region Donuts
    public bool HasDonutPocket => pocketFound && pocketKnown;
    public string? DonutProblem => HasDonutPocket ? null : pocketFound
        ? T("No reconozco dónde guarda esta partida los dónuts, así que no se pueden editar.") : T("Esta partida no tiene bolsillo de dónuts.");
    public bool DonutsChanged => !stage.Donuts.Data.SequenceEqual(save.Donuts.Data);
    public int DonutCount => HasDonutPocket ? UsedSlots(stage).Count() : 0;

    // PKHeX's Donut9a.IsEmpty has this the wrong way round, which also breaks its Compress.
    static bool IsUsed(Donut9a donut) => donut.MillisecondsSince1970 != 0;
    static IEnumerable<int> UsedSlots(SAV9ZA source) => Enumerable.Range(0, PocketSize).Where(i => IsUsed(source.Donuts.GetDonut(i)));

    public List<ZaDonut> Donuts() => !HasDonutPocket ? [] : UsedSlots(stage).Select(i => {
        var donut = stage.Donuts.GetDonut(i);
        var powers = donut.GetFlavors().ToArray().Where(p => p != 0).Select(PowerName);
        return new ZaDonut(i, DonutName(donut.Donut), donut.Stars, string.Join(" · ", powers));
    }).ToList();

    static string DonutName(ushort id) => id < Catalog.Names.donutName.Length ? Catalog.Names.donutName[id] : T("Dónut {0}", id);

    static string PowerName(ulong hash)
    {
        int index = DonutInfo.Flavors.ToList().FindIndex(f => f.Hash == hash);
        return index >= 0 && index < Catalog.Names.donutFlavor.Length ? Catalog.Names.donutFlavor[index] : T("Poder desconocido");
    }

    /// <summary>The strongest level of each ordinary power, by name. The game keeps the special powers for its own events.</summary>
    public static List<ZaPower> TopPowers() => DonutInfo.Flavors.Where(f => f.Name.EndsWith(TopLevel, StringComparison.Ordinal))
        .Select(f => new ZaPower(f.Hash, PowerName(f.Hash))).OrderBy(p => p.Label, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Adds donuts with these powers in the free slots, and returns how many fit.</summary>
    public int AddDonuts(IEnumerable<ulong> powers, int amount)
    {
        var chosen = powers.Where(p => p != 0).ToArray();
        if (chosen.Length is 0 or > PowersPerDonut) throw new InvalidOperationException(T("Elige entre 1 y {0} poderes.", PowersPerDonut));
        if (chosen.Distinct().Count() != chosen.Length) throw new InvalidOperationException(T("Un dónut no puede repetir un poder."));
        return Fill(amount, () => chosen);
    }

    /// <summary>Adds donuts for hunting shiny Pokémon: of one type, or of all of them, each with a size or Alpha power.</summary>
    public int AddShinyAssortment(int amount) => Fill(amount, () => {
        int type = random.Next(ShinyTypes);
        string extra = random.Next(3) switch { 0 => "fresh_01", 1 => "fresh_02", _ => "sweet_01" };
        return [Top($"sweet_{type + FirstShinyPower:00}"), Top(extra), Top($"fresh_{type + FirstCatchingPower:00}")];
    });

    static ulong Top(string power) => PowerHashes[power + TopLevel];

    // PKHeX's template is a real five-star donut, so only its powers and its date change.
    int Fill(int amount, Func<ulong[]> powers)
    {
        if (DonutProblem is { } problem) throw new InvalidOperationException(problem);
        if (amount < 1) throw new InvalidOperationException(T("Indica cuántos dónuts quieres agregar."));
        var free = Enumerable.Range(0, PocketSize).Where(i => !IsUsed(stage.Donuts.GetDonut(i))).Take(amount).ToList();
        if (free.Count == 0) throw new InvalidOperationException(T("El bolsillo de dónuts está lleno."));
        foreach (int slot in free)
        {
            var chosen = powers();
            stage.Donuts.SetRandomShinyTemplateRange(chosen, slot, slot + 1);
            var donut = stage.Donuts.GetDonut(slot);
            donut.Flavor0 = chosen[0]; donut.Flavor1 = chosen.ElementAtOrDefault(1); donut.Flavor2 = chosen.ElementAtOrDefault(2);
        }
        return free.Count;
    }

    /// <summary>Takes a donut out and closes the gap, so the pocket stays in one piece.</summary>
    public void RemoveDonut(int slot)
    {
        if (DonutProblem is { } problem) throw new InvalidOperationException(problem);
        if (slot is < 0 or >= PocketSize || !IsUsed(stage.Donuts.GetDonut(slot))) throw new InvalidOperationException(T("Selecciona un dónut."));
        stage.Donuts.GetDonut(slot).Clear();
        int write = 0;
        foreach (int read in UsedSlots(stage).ToList())
        {
            if (read != write)
            {
                var donut = stage.Donuts.GetDonut(read);
                donut.CopyTo(stage.Donuts.GetDonut(write)); donut.Clear();
            }
            write++;
        }
    }
    #endregion

    #region Clothing
    public bool IsUnlocking(uint key) => unlocking.Contains(key);

    /// <summary>Marks a kind of clothing as bought, all of what the save has on record.</summary>
    public void SetUnlocking(uint key, bool unlock)
    {
        if (!unlock) { unlocking.Remove(key); return; }
        if (clothing.First(c => c.Key == key).Missing != 0) unlocking.Add(key);
    }
    #endregion

    #region Collection
    public string? CollectionProblem => fieldItemsKnown ? null : T("No reconozco dónde guarda esta partida los objetos del mapa, así que no se pueden recoger.");
    public int ScrewsHeld => (int)save.Items.GetItemQuantity(ColorfulScrew9a.ColorfulScrewItemIndex);
    public int ScrewsLeft => screwsLeft.Count;
    public int ScrewsAfter => Math.Max(ScrewsHeld, Math.Min(MostScrews, ScrewsHeld + ScrewsLeft));
    public bool CollectingScrews { get; private set; }
    public int? PendingScrews => CollectingScrews ? ScrewsAfter : null;
    public void SetCollectingScrews(bool collect) => CollectingScrews = collect && ScrewsLeft != 0;

    public int MachinesLeft => machinesLeft.Count;
    public bool CollectingMachines { get; private set; }
    public IEnumerable<ushort> MachineItems => machinesLeft.Select(m => m.Item);

    /// <summary>Machines go in the bag as any other item, so they show up there and can be changed.</summary>
    public void SetCollectingMachines(bool collect)
    {
        if (collect == CollectingMachines || collect && MachinesLeft == 0) return;
        if (collect)
        {
            foreach (var (_, item) in machinesLeft) { machinesBefore[item] = document.GetItemCount(item); document.SetItemCount(item, 1); }
        }
        else
        {
            foreach (var (item, count) in machinesBefore) document.SetItemCount(item, count);
            machinesBefore.Clear();
        }
        CollectingMachines = collect;
    }

    // The bag's own undo has already put the machines back.
    internal void ForgetMachines() { machinesBefore.Clear(); CollectingMachines = false; }

    IEnumerable<ulong> FieldItemsToTake() => (CollectingScrews ? screwsLeft : []).Concat(CollectingMachines ? machinesLeft.Select(m => m.FieldItem) : [])
        .Select(name => FnvHash.HashFnv1a_64(name));
    #endregion

    #region Writing
    internal string? Validate()
    {
        if (counters.Any(c => c.Changed && c.Value > MaxPoints)) return T("Los puntos deben estar entre 0 y {0:N0}.", MaxPoints);
        // The bag tab leaves the screw out, so this is the only place that looks at how many there are.
        return CollectingScrews && ScrewsAfter > MostScrews
            ? T("{0}: la cantidad debe estar entre 0 y {1}.", document.Rules.ItemName(ColorfulScrew9a.ColorfulScrewItemIndex), MostScrews) : null;
    }

    internal IEnumerable<uint> ChangedBlocks()
    {
        foreach (var counter in counters.Where(c => c.Changed)) yield return counter.Key;
        if (DonutsChanged) yield return DonutBlock;
        foreach (uint key in unlocking) yield return key;
        if (CollectingScrews || CollectingMachines) yield return FieldItemBlock;
    }

    internal void ApplyTo(SAV9ZA output)
    {
        foreach (var counter in counters.Where(c => c.Changed)) output.SetValue(counter.Key, counter.Value);
        if (DonutsChanged) stage.Donuts.Data.CopyTo(output.Donuts.Data);
        foreach (uint key in unlocking) FashionItem9a.ModifyAll(output.Blocks.GetBlock(key).Data, item => item.IsOwned = true);
        var field = output.Blocks.FieldItems;
        foreach (ulong hash in FieldItemsToTake()) field.SetValue(field.GetIndex(hash), true);
    }

    /// <summary>Goes through what is in the written save. Anything else that changed is stopped by the block comparison.</summary>
    internal void Verify(SAV9ZA reopened)
    {
        static void Require(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException(T("No se pudo comprobar {0} después de guardar.", what));
        }
        foreach (var counter in counters) Require(reopened.GetValue<uint>(counter.Key) == counter.Value, T(counter.Name));
        if (HasDonutPocket) Require(reopened.Donuts.Data.SequenceEqual(stage.Donuts.Data), T("Dónuts"));
        foreach (var kind in clothing)
        {
            var before = FashionItem9a.GetArray(save.Blocks.GetBlock(kind.Key).Data);
            var after = FashionItem9a.GetArray(reopened.Blocks.GetBlock(kind.Key).Data);
            bool bought = unlocking.Contains(kind.Key);
            for (int i = 0; i < before.Length; i++)
            {
                var expected = new FashionItem9a { Value = before[i].Value, Flags = before[i].Flags };
                if (bought && expected.Value != FashionItem9a.None) expected.IsOwned = true;
                Require(after[i].Value == expected.Value && after[i].Flags == expected.Flags, T(kind.Name));
            }
        }
        var taken = FieldItemsToTake().ToHashSet();
        ReadOnlySpan<byte> oldField = save.Blocks.FieldItems.Data, newField = reopened.Blocks.FieldItems.Data;
        for (int offset = 0; offset < oldField.Length; offset += FieldItemSize)
        {
            ulong key = BinaryPrimitives.ReadUInt64LittleEndian(oldField[offset..]);
            ulong oldValue = BinaryPrimitives.ReadUInt64LittleEndian(oldField[(offset + 8)..]), newValue = BinaryPrimitives.ReadUInt64LittleEndian(newField[(offset + 8)..]);
            bool mustTake = taken.Remove(key);
            Require(BinaryPrimitives.ReadUInt64LittleEndian(newField[offset..]) == key && newValue == (mustTake ? 1 : oldValue), T("Puntos y colección"));
        }
        Require(taken.Count == 0, T("Puntos y colección"));
    }

    /// <summary>For the record kept with each backup.</summary>
    internal object Summary() => new {
        points = counters.Where(c => c.Changed).Select(c => new { c.Name, before = c.Original, after = c.Value }),
        donuts = new { before = HasDonutPocket ? UsedSlots(save).Count() : 0, after = DonutCount },
        clothing = clothing.Where(c => unlocking.Contains(c.Key)).Select(c => new { c.Name, c.Known, ownedBefore = c.Owned }),
        screws = new { before = ScrewsHeld, after = PendingScrews ?? ScrewsHeld },
        technicalMachines = CollectingMachines ? MachinesLeft : 0,
    };
    #endregion
}
