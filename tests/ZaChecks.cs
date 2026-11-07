using Editrainer;
using PKHeX.Core;
using System.Reflection;
using System.Text.Json;

/// <summary>Each Z-A edit through a real write and reopen, always on copies of the save.</summary>
static class ZaChecks
{
    const int RoyaleLike = 1234, OrdinaryPowers = 92, Seed = 20261106, Assorted = 40;
    static int passed;
    static string source = "", work = "", sourceHash = "";

    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        passed++; Console.WriteLine("PASS: " + description);
    }

    static void Reject(Action action, string description)
    {
        try { action(); }
        catch (InvalidOperationException) { Check(true, description); return; }
        throw new Exception("FAIL: expected rejection: " + description);
    }

    static SAV9ZA Read(string path) => (SAV9ZA)SaveDocument.OpenSave(File.ReadAllBytes(path))!;

    static void Refused(Action action, string message, string description)
    {
        try { action(); }
        catch (InvalidOperationException e) when (e.Message == message) { Check(true, description); return; }
        throw new Exception("FAIL: expected that refusal: " + description);
    }
    static SaveDocument Fresh(string name, Random? random = null)
    {
        var copy = Path.Combine(work, name);
        File.Copy(source, copy, true);
        return SaveDocument.Open(copy, random);
    }

    static bool Unchanged(SaveDocument doc) => !doc.HasChanges && doc.BuildOutput().SequenceEqual(doc.OriginalBytes);

    // The save written by the app, through the same door as the Save button.
    static (SaveDocument Document, string Backup) Written(SaveDocument edited)
    {
        var backup = edited.Write(edited.FilePath, Path.Combine(work, "Backups"), _ => null);
        return (SaveDocument.Open(edited.FilePath), backup);
    }

    static List<uint> Differences(SAV9ZA before, SAV9ZA after) => before.AllBlocks.Zip(after.AllBlocks)
        .Where(pair => !pair.First.Data.SequenceEqual(pair.Second.Data)).Select(pair => pair.First.Key).Order().ToList();

    static bool Same(IEnumerable<uint> actual, params uint[] expected) => actual.Order().SequenceEqual(expected.Order());

    static int FieldChanges(SAV9ZA before, SAV9ZA after)
    {
        ReadOnlySpan<byte> a = before.Blocks.FieldItems.Data, b = after.Blocks.FieldItems.Data;
        int changes = 0;
        for (int offset = 0; offset < a.Length; offset += 16)
            if (!a.Slice(offset, 16).SequenceEqual(b.Slice(offset, 16))) changes++;
        return changes;
    }

    public static void Run(string saveFile, string workFolder)
    {
        source = Path.GetFullPath(saveFile); work = Path.GetFullPath(workFolder);
        Directory.CreateDirectory(work);
        sourceHash = SaveDocument.Hash(File.ReadAllBytes(source));
        CheckBlocks(); CheckPoints(); CheckDonuts(); CheckClothing(); CheckScrews(); CheckMachines(); CheckAllTogether(); CheckVerification(); CheckOlderSave(); CheckBaseMode(); CheckLanguages();
        Check(SaveDocument.Hash(File.ReadAllBytes(source)) == sourceHash, "The save given to the tests is untouched");
        Console.WriteLine($"{passed} Z-A checks passed.");
    }

    static void CheckBlocks()
    {
        var save = Read(source);
        Check(save.Blocks.GetBlock(ZaEdits.DonutBlock).Data.SequenceEqual(save.Donuts.Data) && save.Donuts.Data.Length == ZaEdits.PocketSize * Donut9a.Size, "The donut key opens the donut pocket");
        Check(save.Blocks.GetBlock(ZaEdits.FieldItemBlock).Data.SequenceEqual(save.Blocks.FieldItems.Data), "The field item key opens the field items");
        Check(ZaEdits.IsBlock(save, ZaEdits.DonutBlock, save.Donuts) && ZaEdits.IsBlock(save, ZaEdits.FieldItemBlock, save.Blocks.FieldItems), "Both keys are the very blocks PKHeX reads");
        Check(!ZaEdits.IsBlock(save, ZaEdits.FieldItemBlock, save.Donuts) && !ZaEdits.IsBlock(save, 0, save.Donuts), "Another key or a missing one is not taken for them");
        var doc = Fresh("blocks");
        Check(doc.Za != null && !doc.Za.HasChanges && !doc.HasChanges, "Opening a Z-A save prepares nothing");
        Check(doc.Za!.DonutProblem == null && doc.Za.CollectionProblem == null, "A real save has both tools available");
        Check(Unchanged(doc), "A Z-A save without edits comes out identical");

        var blocked = new ZaEdits(doc, doc.ZaSave!, donutBlock: ZaEdits.FieldItemBlock, fieldItemBlock: ZaEdits.DonutBlock);
        Check(!blocked.HasDonutPocket && blocked.DonutProblem != null && blocked.Donuts().Count == 0, "A key that isn't the pocket turns the donut tools off and says why");
        Reject(() => blocked.AddDonuts([ZaEdits.TopPowers()[0].Hash], 1), "Without the right key no donuts can be added");
        Reject(() => blocked.AddShinyAssortment(1), "Without the right key no assortment can be added");
        Reject(() => blocked.RemoveDonut(0), "Without the right key no donut can be removed");
        Check(blocked.CollectionProblem != null && blocked.ScrewsLeft == 0 && blocked.MachinesLeft == 0, "A key that isn't the field items turns collecting off and says why");
        blocked.SetCollectingScrews(true); blocked.SetCollectingMachines(true);
        Check(!blocked.HasChanges && !blocked.CollectingScrews && !blocked.CollectingMachines, "With the wrong key there is nothing to collect");
    }

    static void CheckPoints()
    {
        var doc = Fresh("points");
        Check(doc.Za!.Points.Count == 3, "All three kinds of points are in a real save");
        foreach (var counter in doc.Za.Points) counter.Value = counter.Original + RoyaleLike;
        Check(doc.HasChanges && doc.Za.ChangeCount == 3, "Changing the points is a pending change for each kind");
        var (saved, _) = Written(doc);
        foreach (var counter in saved.Za!.Points)
            Check(counter.Original == doc.Za.Points.First(c => c.Key == counter.Key).Value, $"{counter.Name} survive the write");
        Check(Same(Differences(Read(source), saved.ZaSave!), [.. doc.Za.Points.Select(c => c.Key)]), "Writing points touches only their blocks");
        Check(!saved.HasChanges, "A reopened save has nothing pending");

        var tooMany = Fresh("points-limit");
        tooMany.Za!.Points[0].Value = ZaEdits.MaxPoints + 1u;
        Check(tooMany.ValidateEdits() != null, "Too many points are refused before saving");
        Reject(() => tooMany.BuildOutput(), "Too many points can't be written");
        tooMany.Za.Points[0].Value = ZaEdits.MaxPoints;
        Check(tooMany.ValidateEdits() == null, "The most points allowed are accepted");
        tooMany.Za.Reset();
        Check(Unchanged(tooMany), "Undoing the points restores the exact save");
    }

    static void CheckDonuts()
    {
        var powers = ZaEdits.TopPowers();
        ulong one = powers[0].Hash, two = powers[1].Hash, three = powers[2].Hash;
        var doc = Fresh("donuts");
        int start = doc.Za!.DonutCount;
        Check(doc.Za.HasDonutPocket, "A real save has a donut pocket");
        Reject(() => doc.Za.AddDonuts([], 1), "A donut needs a power");
        Reject(() => doc.Za.AddDonuts([one, two, three, powers[3].Hash], 1), "A donut has three powers at most");
        Reject(() => doc.Za.AddDonuts([one, one], 1), "A donut can't repeat a power");
        Reject(() => doc.Za.AddDonuts([one], 0), "Adding no donuts is refused");
        Check(!doc.HasChanges, "Refused donuts leave nothing prepared");
        Check(doc.Za.AddDonuts([one, two, three], 2) == 2 && doc.Za.AddDonuts([one, 0, two], 1) == 1 && doc.Za.AddDonuts([three], 1) == 1, "Donuts are added with one to three powers");
        Check(doc.HasChanges && doc.Za.DonutCount == start + 4, "Added donuts stay pending");
        var (saved, backup) = Written(doc);
        Check(Same(Differences(Read(source), saved.ZaSave!), ZaEdits.DonutBlock), "Writing donuts touches only the pocket");
        Check(saved.Za!.DonutCount == start + 4 && saved.Za.Donuts().All(d => d.Stars == 5 && d.Name.Length != 0 && d.Powers.Length != 0), "Donuts survive the write and read back with name and powers");
        var pocket = saved.ZaSave!.Donuts;
        var first = pocket.GetDonut(start);
        Check(first.Flavor0 == one && first.Flavor1 == two && first.Flavor2 == three && first.FlavorCount == 3, "The powers keep their order");
        Check(first.Stars == 5 && first.LevelBoost == 84 && first.Calories == 3600 && first.Donut == 150 && first.BerryName == 2676
            && first.GetBerries().ToArray().All(b => b == 2676) && first.Reserved == 0, "A new donut is PKHeX's five-star template");
        Check(Math.Abs((first.DateTime1900.Timestamp - DateTime.Now).TotalDays) < 1 && first.MillisecondsSince1970 > 0, "A new donut carries today's date in both of its fields");
        var second = pocket.GetDonut(start + 2);
        Check(second.Flavor0 == one && second.Flavor1 == two && second.Flavor2 == 0 && second.FlavorCount == 2, "An empty power slot closes the list");
        var single = pocket.GetDonut(start + 3);
        Check(single.Flavor0 == three && single.Flavor1 == 0 && single.Flavor2 == 0 && single.FlavorCount == 1, "One power leaves the other two empty");
        Check(Enumerable.Range(0, start + 4).Select(i => pocket.GetDonut(i).MillisecondsSince1970).Distinct().Count() == start + 4, "Each donut has its own date");
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(backup, "cambios.json"))))
            Check(report.RootElement.GetProperty("za").GetProperty("donuts").GetProperty("after").GetInt32() == start + 4, "The backup's record tells about the donuts");
        Check(SaveDocument.Hash(File.ReadAllBytes(Directory.GetFiles(backup, "*.original").Single())) == sourceHash, "The backup is the save as it was");

        var kept = pocket.GetDonut(start).Data.ToArray();
        saved.Za.AddDonuts([two], 1);
        var (again, _) = Written(saved);
        Check(again.Za!.DonutCount == start + 5 && again.ZaSave!.Donuts.GetDonut(start).Data.SequenceEqual(kept), "Adding more leaves the other donuts alone");

        var removal = Fresh("donuts-remove");
        removal.Za!.AddDonuts([one], 1); removal.Za.AddDonuts([two], 1); removal.Za.AddDonuts([three], 1);
        Reject(() => removal.Za.RemoveDonut(ZaEdits.PocketSize - 1), "An empty slot has no donut to remove");
        Reject(() => removal.Za.RemoveDonut(-1), "A slot outside the pocket is refused");
        removal.Za.RemoveDonut(start + 1);
        var (compact, _) = Written(removal);
        var left = compact.ZaSave!.Donuts;
        Check(compact.Za!.DonutCount == start + 2 && left.GetDonut(start).Flavor0 == one && left.GetDonut(start + 1).Flavor0 == three
            && left.GetDonut(start + 2).Data.ToArray().All(b => b == 0), "Removing a donut closes the gap and clears the end of the pocket");

        var shiny = Fresh("donuts-shiny", new Random(Seed));
        Check(shiny.Za!.AddShinyAssortment(Assorted) == Assorted, "The shiny assortment adds what was asked");
        var names = DonutInfo.Flavors.ToDictionary(f => f.Hash, f => f.Name);
        var (assorted, _) = Written(shiny);
        foreach (int slot in Enumerable.Range(start, Assorted))
        {
            var donut = assorted.ZaSave!.Donuts.GetDonut(slot);
            Check(donut.FlavorCount == 3 && donut.GetFlavors().ToArray().All(p => names[p].EndsWith("_lv3")), $"The shiny donut in slot {slot} has three powers at their top level");
            int sparkling = int.Parse(names[donut.Flavor0][6..8]), catching = int.Parse(names[donut.Flavor2][6..8]);
            Check(names[donut.Flavor0].StartsWith("sweet_") && names[donut.Flavor2].StartsWith("fresh_") && catching == sparkling + 1
                && names[donut.Flavor1] is "fresh_01_lv3" or "fresh_02_lv3" or "sweet_01_lv3", $"The shiny donut in slot {slot} matches its shiny and catching powers");
        }
        static List<string> Powers(SaveDocument doc) => doc.Za!.Donuts().Select(d => d.Powers).ToList();
        var same = Fresh("donuts-seed", new Random(Seed)); same.Za!.AddShinyAssortment(Assorted);
        Check(Powers(same).SequenceEqual(Powers(assorted)), "The same seed gives the same assortment");

        var full = Fresh("donuts-full");
        Check(full.Za!.AddDonuts([one], ZaEdits.PocketSize * 2) == ZaEdits.PocketSize - start && full.Za.DonutCount == ZaEdits.PocketSize, "Adding stops when the pocket is full");
        Reject(() => full.Za.AddDonuts([one], 1), "A full pocket takes no more");
        Reject(() => full.Za.AddShinyAssortment(1), "A full pocket takes no assortment");
        var (filled, _) = Written(full);
        Check(filled.Za!.DonutCount == ZaEdits.PocketSize, "A full pocket survives the write");

        var undone = Fresh("donuts-undo");
        undone.Za!.AddDonuts([one], 3); undone.Za.Reset();
        Check(Unchanged(undone), "Undoing donuts restores the exact save");
    }

    static void CheckClothing()
    {
        var plain = Fresh("clothing-count");
        Check(plain.Za!.Clothing.Count == 10 && plain.Za.Clothing.All(k => k.Known > 0 == (k.Owned > 0 || k.Missing > 0)), "Every kind of clothing is counted");
        foreach (var kind in plain.Za.Clothing.Where(k => k.Missing == 0))
        {
            plain.Za.SetUnlocking(kind.Key, true);
            Check(!plain.Za.IsUnlocking(kind.Key), $"{kind.Name}: nothing to buy, so nothing is prepared");
        }
        var missing = plain.Za.Clothing.Where(k => k.Missing != 0).ToList();
        if (missing.Count == 0) { Console.WriteLine("(this save owns all the clothing it knows: the unlocking runs on the other one)"); return; }

        var original = Read(source);
        foreach (var kind in missing)
        {
            var doc = Fresh("clothing");
            doc.Za!.SetUnlocking(kind.Key, true);
            Check(doc.HasChanges && doc.Za.ChangeCount == 1, $"{kind.Name}: unlocking is a pending change");
            var (saved, _) = Written(doc);
            var written = saved.Za!.Clothing.First(k => k.Key == kind.Key);
            Check(written.Known == kind.Known && written.Owned == kind.Known, $"{kind.Name}: all {kind.Known} known are owned after the write");
            Check(Same(Differences(original, saved.ZaSave!), kind.Key), $"{kind.Name}: only this block changes");
            var before = FashionItem9a.GetArray(original.Blocks.GetBlock(kind.Key).Data);
            var after = FashionItem9a.GetArray(saved.ZaSave!.Blocks.GetBlock(kind.Key).Data);
            Check(before.Zip(after).All(p => p.First.Value == p.Second.Value && p.Second.Flags == (p.First.Value == FashionItem9a.None ? p.First.Flags : p.First.Flags | 0x10)),
                $"{kind.Name}: only the owned mark changes, and never on an empty record");
        }
        var both = Fresh("clothing-all");
        foreach (var kind in missing) both.Za!.SetUnlocking(kind.Key, true);
        both.Za!.SetUnlocking(missing[0].Key, false);
        Check(both.Za.ChangeCount == missing.Count - 1, "A kind can be left out again");
        var (mixed, _) = Written(both);
        Check(mixed.Za!.Clothing.First(k => k.Key == missing[0].Key).Missing == missing[0].Missing, "The kind left out stays as it was");
        Check(mixed.Za.Clothing.Skip(1).All(k => k.Missing == 0 || k.Key == missing[0].Key), "The rest are all owned");
    }

    static void CheckScrews()
    {
        var doc = Fresh("screws");
        var original = doc.ZaSave!;
        int held = doc.Za!.ScrewsHeld, left = doc.Za.ScrewsLeft;
        Check(left > 0 && doc.Za.ScrewsAfter == Math.Min(100, held + left), "The screws waiting in visited places are counted");
        doc.Za.SetCollectingScrews(true);
        Check(doc.HasChanges && doc.Za.PendingScrews == held + left, "Collecting the screws stays pending");
        var (saved, backup) = Written(doc);
        var save = saved.ZaSave!;
        Check(save.Items.GetItemQuantity(ColorfulScrew9a.ColorfulScrewItemIndex) == held + left, "The screws are in the bag after the write");
        Check(saved.Za!.ScrewsLeft == 0 && !ColorfulScrew9a.GetScrewLocations(save, false).Any(), "The map has no screws left in visited places");
        Check(ColorfulScrew9a.GetScrewLocations(save, true).Count() == ColorfulScrew9a.GetScrewLocations(original, true).Count() + left, "Each screw counted was taken");
        Check(FieldChanges(original, save) == left, "Only the screws' records change on the map");
        Check(Differences(original, save).All(key => key == ZaEdits.FieldItemBlock || key == 0x21C9BD44), "Collecting the screws touches only the map and the bag");
        var screw = save.Items.GetItem(ColorfulScrew9a.ColorfulScrewItemIndex);
        Check(screw.Pouch == MyItem9a.GetPouchIndex(MyItem9a.GetType(ColorfulScrew9a.ColorfulScrewItemIndex)) && !screw.IsNewNotify, "The screw goes in its own pouch");
        var gameScrew = original.Items.GetItem(ColorfulScrew9a.ColorfulScrewItemIndex);
        Check(held == 0 || screw.Pouch == gameScrew.Pouch && screw.Flags == gameScrew.Flags, "A screw the game already wrote keeps its pouch and marks");
        Check(saved.Items.All(i => i.Id != ColorfulScrew9a.ColorfulScrewItemIndex), "The bag tab still leaves the screw out");
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(backup, "cambios.json"))))
            Check(report.RootElement.GetProperty("za").GetProperty("screws").GetProperty("after").GetInt32() == held + left, "The backup's record tells about the screws");

        var undone = Fresh("screws-undo");
        undone.Za!.SetCollectingScrews(true); undone.Za.SetCollectingScrews(false);
        Check(Unchanged(undone), "Not collecting the screws restores the exact save");
        var none = saved.Za;
        none.SetCollectingScrews(true);
        Check(!none.CollectingScrews && !saved.HasChanges, "With no screws left there is nothing to collect");

        const int TooMany = 120;
        var inflated = Read(source);
        inflated.Items.SetItemQuantity(ColorfulScrew9a.ColorfulScrewItemIndex, TooMany);
        var path = Path.Combine(work, "screws-limit");
        File.WriteAllBytes(path, inflated.Write().ToArray());
        var limit = SaveDocument.Open(path);
        Check(limit.ValidateEdits() == null && limit.Za!.ScrewsAfter == TooMany, "Collecting never lowers what a save already has");
        limit.Za!.SetCollectingScrews(true);
        Check(limit.ValidateEdits() != null, "Going over the screw limit is refused before saving");
        Reject(() => limit.BuildOutput(), "Going over the screw limit can't be written");
        limit.Za.SetCollectingScrews(false);
        Check(limit.ValidateEdits() == null && !limit.HasChanges, "Leaving the screws out clears the refusal");
    }

    static void CheckMachines()
    {
        var doc = Fresh("machines");
        var original = doc.ZaSave!;
        var items = doc.Za!.MachineItems.ToList();
        Check(items.Count > 0 && items.Count == doc.Za.MachinesLeft, "The machines waiting in visited places are counted");
        Check(items.All(id => doc.GetItemCount(id) == 0), "Those machines aren't in the bag yet");
        doc.Za.SetCollectingMachines(true);
        Check(doc.HasChanges && items.All(id => doc.GetItemCount(id) == 1), "Collecting the machines shows them in the bag, pending");
        var (saved, backup) = Written(doc);
        var save = saved.ZaSave!;
        Check(items.All(id => save.Items.GetItemQuantity(id) == 1), "The machines are in the bag after the write");
        Check(saved.Za!.MachinesLeft == 0, "The map has no machines left in visited places");
        Check(FieldChanges(original, save) == items.Count, "Only the machines' records change on the map");
        Check(Differences(original, save).All(key => key == ZaEdits.FieldItemBlock || key == 0x21C9BD44), "Collecting the machines touches only the map and the bag");
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(backup, "cambios.json"))))
            Check(report.RootElement.GetProperty("za").GetProperty("technicalMachines").GetInt32() == items.Count, "The backup's record tells about the machines");

        var undone = Fresh("machines-undo");
        undone.Za!.SetCollectingMachines(true); undone.Za.SetCollectingMachines(false);
        Check(Unchanged(undone), "Not collecting the machines restores the exact save");

        var bagUndo = Fresh("machines-bag");
        bagUndo.Za!.SetCollectingMachines(true); bagUndo.ResetResources();
        Check(!bagUndo.Za.CollectingMachines && Unchanged(bagUndo), "The bag tab's undo also forgets the machines");
        bagUndo.Za.SetCollectingMachines(true);
        Check(bagUndo.Za.CollectingMachines && items.All(id => bagUndo.GetItemCount(id) == 1), "The machines can be collected again after that");
        bagUndo.Za.Reset();
        Check(Unchanged(bagUndo), "Undoing them a second way also restores the exact save");

        var others = Fresh("machines-others");
        others.Money = others.Save.Money == 4321 ? 1234 : 4321; others.SetItemCount(1128, 5);
        others.Za!.SetCollectingMachines(true); others.Za.SetCollectingMachines(false);
        Check(others.HasChanges && others.GetItemCount(1128) == 5 && items.All(id => others.GetItemCount(id) == 0), "Not collecting the machines leaves the other bag edits alone");
        others.ResetResources();
        Check(Unchanged(others), "With the bag undone as well, the save is exact again");

        var pending = Fresh("machines-screws");
        pending.Za!.SetCollectingScrews(true); pending.Za.SetCollectingMachines(true); pending.ResetResources();
        Check(pending.Za.CollectingScrews && !pending.Za.CollectingMachines && pending.HasChanges, "The bag's undo leaves the screws prepared");
        pending.Za.SetCollectingScrews(false);
        Check(Unchanged(pending), "Leaving the screws out too makes the save exact again");

        var bagWins = Fresh("machines-count");
        bagWins.Za!.SetCollectingMachines(true); bagWins.SetItemCount(items[0], 0);
        var (changed, _) = Written(bagWins);
        Check(changed.ZaSave!.Items.GetItemQuantity(items[0]) == 0 && changed.Za!.MachinesLeft == 0, "A count set in the bag afterwards is the one that stays");
    }

    static void CheckAllTogether()
    {
        var doc = Fresh("everything");
        doc.Za!.Points[0].Value = 777;
        doc.Za.AddDonuts([ZaEdits.TopPowers()[0].Hash], 2);
        foreach (var kind in doc.Za.Clothing) doc.Za.SetUnlocking(kind.Key, true);
        doc.Za.SetCollectingScrews(true); doc.Za.SetCollectingMachines(true);
        doc.Money = doc.Save.Money == 4321 ? 1234 : 4321; doc.SetItemCount(1128, 5);
        doc.Pokemon[0].Edit.IVs[0] = doc.Pokemon[0].Baseline.IVs[0] == 12 ? 13 : 12;
        var (saved, backup) = Written(doc);
        Check(saved.Za!.Points[0].Original == 777 && saved.Za.DonutCount == doc.Za.DonutCount && saved.Za.ScrewsLeft == 0 && saved.Za.MachinesLeft == 0
            && saved.Za.Clothing.All(k => k.Missing == 0), "Every Z-A tool is written together");
        Check(saved.Save.Money == doc.Money && saved.GetItemCount(1128) == 5 && saved.Pokemon[0].Original.IV_HP == doc.Pokemon[0].Edit.IVs[0], "The Z-A tools share a save with the other edits");
        Check(!saved.HasChanges && SaveDocument.Hash(saved.BuildOutput()) == SaveDocument.Hash(File.ReadAllBytes(saved.FilePath)), "A written save reopens with nothing left to do");
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(backup, "cambios.json")));
        var za = report.RootElement.GetProperty("za");
        Check(za.GetProperty("points").GetArrayLength() == 1 && za.GetProperty("points")[0].GetProperty("after").GetUInt32() == 777, "The backup's record tells about the points");
        Check(za.GetProperty("clothing").GetArrayLength() == doc.Za.Clothing.Count(k => k.Missing != 0), "The backup's record tells about the clothing");
    }

    static void CheckVerification()
    {
        var doc = Fresh("verify");
        var kinds = doc.Za!.Clothing;
        doc.Za.Points[0].Value = doc.Za.Points[0].Original + 1;
        doc.Za.AddDonuts([ZaEdits.TopPowers()[0].Hash], 1);
        doc.Za.SetCollectingScrews(true);
        var bytes = doc.BuildOutput();
        SAV9ZA Reread() => (SAV9ZA)SaveUtil.GetSaveFile(bytes.ToArray())!;
        doc.Za.Verify(Reread());
        Check(true, "The write passes its own verification");

        var points = Reread(); points.SetValue(doc.Za.Points[0].Key, doc.Za.Points[0].Value + 1);
        Reject(() => doc.Za.Verify(points), "Verification notices the wrong points");
        var pocket = Reread(); pocket.Donuts.Data[0] ^= 1;
        Reject(() => doc.Za.Verify(pocket), "Verification notices a changed donut");
        var clothes = Reread(); clothes.Blocks.GetBlock(kinds[0].Key).Data[4] ^= 0x10;
        Reject(() => doc.Za.Verify(clothes), "Verification notices clothing nobody touched");
        var map = Reread(); var field = map.Blocks.FieldItems;
        int bystander = Enumerable.Range(0, field.Count).First(i => field.GetValue(i) == false && field.GetKey(i) != 0
            && !ColorfulScrew9a.GetScrewLocations(Read(source), false).Any(s => FnvHash.HashFnv1a_64(s.FieldItem) == field.GetKey(i)));
        field.SetValue(bystander, true);
        Reject(() => doc.Za.Verify(map), "Verification notices another item taken from the map");
        var missed = Reread(); var first = ColorfulScrew9a.GetScrewLocations(Read(source), false).First();
        missed.Blocks.FieldItems.SetValue(missed.Blocks.FieldItems.GetIndex(FnvHash.HashFnv1a_64(first.FieldItem)), false);
        Reject(() => doc.Za.Verify(missed), "Verification notices a screw left on the map");
        Check(Same(doc.Za.ChangedBlocks(), doc.Za.Points[0].Key, ZaEdits.DonutBlock, ZaEdits.FieldItemBlock), "The block comparison only lets through what was edited");
    }

    // The base game, before the DLC update, has neither the donut pocket nor hyperspace points.
    static void CheckOlderSave()
    {
        var blocks = Read(source).AllBlocks.Where(b => b.Key is not (ZaEdits.DonutBlock or SaveBlockAccessor9ZA.KHyperspaceSurveyPoints)).ToList();
        var path = Path.Combine(work, "older");
        File.WriteAllBytes(path, TestSaves.Finish(new SAV9ZA(SwishCrypto.Encrypt(blocks))));
        var doc = SaveDocument.Open(path);
        Check(!doc.Za!.HasDonutPocket && doc.Za.DonutCount == 0 && doc.Za.Donuts().Count == 0, "A save without the pocket has no donuts to show");
        Check(doc.Za.Points.Count == 2 && doc.Za.Points.All(c => c.Key != SaveBlockAccessor9ZA.KHyperspaceSurveyPoints), "Points a save doesn't have are left out");
        Reject(() => doc.Za.AddDonuts([ZaEdits.TopPowers()[0].Hash], 1), "Without a pocket no donuts can be added");
        Reject(() => doc.Za.AddShinyAssortment(1), "Without a pocket no assortment can be added");
        Reject(() => doc.Za.RemoveDonut(0), "Without a pocket no donut can be removed");
        doc.Za.Points[0].Value++;
        var (saved, _) = Written(doc);
        Check(saved.Za!.Points[0].Original == doc.Za.Points[0].Value, "A save without the pocket can still be written");
    }

    const uint ShrinkingBlock = 0xE3AFBE89;   // the game writes this block eight bytes shorter without the Mega Dimension
    const ushort HiddenSpecies = 316;         // Gulpin: only in the Mega Dimension Pokédex

    // A save the game wrote without the Mega Dimension: revision 0, a size PKHeX doesn't list, and Pokémon of that Pokédex the game shows as eggs.
    static void CheckBaseMode()
    {
        var withDlc = Fresh("dlc");
        Check(withDlc.OpeningWarning == null && !withDlc.Rules.HidesMegaDimension && withDlc.Rules.CanShow(HiddenSpecies), "A save written with the Mega Dimension shows everything and warns of nothing");
        var blocks = SwishCrypto.Decrypt(File.ReadAllBytes(source)).ToList();
        blocks.First(b => b.Key == SaveBlockAccessor9ZA.KSaveRevision).SetValue(0UL);
        int at = blocks.FindIndex(b => b.Key == ShrinkingBlock);
        var old = blocks[at];
        // PKHeX keeps the block constructors to itself, and nothing public changes a block's length.
        var make = typeof(SCBlock).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(uint), typeof(SCTypeCode), typeof(Memory<byte>)])!;
        blocks[at] = (SCBlock)make.Invoke([old.Key, old.Type, (Memory<byte>)old.Data[..^8].ToArray()]);
        var shorter = new SAV9ZA(SwishCrypto.Encrypt(blocks));
        var free = Enumerable.Range(0, shorter.SlotCount).Where(i => shorter.GetBoxSlotAtIndex(i).Species == 0).Take(2).ToArray();
        var hidden = shorter.GetBoxSlotAtIndex(Enumerable.Range(0, shorter.SlotCount).First(i => shorter.GetBoxSlotAtIndex(i).Species != 0)).Clone();
        hidden.Species = HiddenSpecies; hidden.Form = 0; hidden.ClearNickname(); hidden.RefreshChecksum();
        shorter.SetBoxSlotAtIndex(hidden, free[0], EntityImportSettings.None);
        var bytes = shorter.Write().ToArray();
        bool listed = SaveUtil.GetSaveFile(bytes.ToArray()) != null;
        Check(bytes.Length == new FileInfo(source).Length - 8 && SwishCrypto.GetIsHashValid(bytes), "The base-mode save is eight bytes shorter and still signed");
        Check(SaveDocument.OpenSave(bytes) is SAV9ZA { SaveRevision: 0 }, $"A base-mode save of a size PKHeX {(listed ? "lists" : "doesn't list")} opens as Legends: Z-A");
        Check(SaveDocument.OpenSave(bytes[..^1]) == null && SaveDocument.OpenSave(new byte[bytes.Length]) == null, "A damaged or empty file of that size is still refused");
        var folder = Path.Combine(work, "base-mode"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "main"); File.WriteAllBytes(path, bytes);
        var doc = SaveDocument.Open(path);
        Check(doc.Rules.IsZA && doc.Rules.HidesMegaDimension && doc.ZaSave!.SaveRevision == 0, "The document knows the save was written without the Mega Dimension");
        int count = withDlc.Pokemon.Count + 1;
        Check(doc.Pokemon.Count == count && doc.OpeningWarning == Strings.T("Esta partida se guardó sin la Dimensión Mega y tiene {0} Pokémon de esa Pokédex: el juego los muestra como huevos hasta que la cargues con el DLC.", 1), "Opening it counts the Pokémon the game shows as eggs");
        Check(Unchanged(doc), "Without edits it comes out byte for byte");
        Check(doc.Pokedex.Lists.Count == 1 && doc.Pokedex.Lists[0].Name == "Luminalia", "Only the Lumiose Pokédex is offered");
        var generator = new LegalGenerator(doc);
        Check(generator.DexSpecies.Count <= GameRules.LumioseDexSize && generator.DexSpecies.All(doc.Rules.CanShow) && !generator.DexSpecies.Contains(HiddenSpecies), "The generators only know the species the game can show");
        string refusal = doc.Rules.HiddenReason(Catalog.Names.specieslist[HiddenSpecies]);
        Refused(() => generator.FromSet("Gulpin"), refusal, "A Showdown set of a Mega Dimension species is refused with the reason");
        var hiddenFile = Path.Combine(folder, "hidden.pa9");
        File.WriteAllBytes(hiddenFile, SaveDocument.PokemonFile(new PokemonEntry(doc.Rules, false, free[0], hidden)));
        Refused(() => doc.Import(hiddenFile, new(false, free[1])), refusal, "Importing one is refused with the reason");
        // A species with nowhere shown to go is its own final form, hidden or not.
        Check(doc.Pokemon.All(e => Catalog.GetFinalForms(e).All(t => t.Species == e.Original.Species || doc.Rules.CanShow(t.Species))), "Evolutions never reach a species the game hides");
        var shownFile = Path.Combine(folder, "shown.pa9");
        File.WriteAllBytes(shownFile, SaveDocument.PokemonFile(doc.Pokemon.First(e => e.IsParty)));
        doc.Import(shownFile, new(false, free[1]));
        doc.Pokemon.First(e => e.Original.Species == HiddenSpecies).Edit.Level = 50;
        var (saved, _) = Written(doc);
        Check(saved.ZaSave!.SaveRevision == 0 && saved.OriginalBytes.Length == bytes.Length && saved.Pokemon.Count == count + 1
            && saved.Pokemon.Any(e => e.Original.Species == HiddenSpecies && e.Original.CurrentLevel == 50), "Edits and imports are written and verified on a base-mode save");
        Check(saved.OpeningWarning != null && saved.Rules.HidesMegaDimension, "The written save still says what the game hides");
    }

    static void CheckLanguages()
    {
        var doc = Fresh("languages");
        doc.Za!.AddDonuts([ZaEdits.TopPowers()[0].Hash], 1);
        foreach (var language in Strings.Languages)
        {
            Catalog.UseLanguage(language.Code);
            var powers = ZaEdits.TopPowers();
            Check(powers.Count == OrdinaryPowers && powers.Select(p => p.Label).Distinct().Count() == OrdinaryPowers && powers.All(p => p.Label.Length != 0 && !p.Label.Contains("lv3")), $"Powers have their own names in {language.Code}");
            var donut = doc.Za.Donuts()[^1];
            Check(donut.Name != Strings.T("Dónut {0}", 150) && donut.Powers != Strings.T("Poder desconocido"), $"Donuts have their game names in {language.Code}");
        }
        Catalog.UseLanguage("es");
    }
}
