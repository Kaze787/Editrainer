using Editrainer;
using PKHeX.Core;
using System.Text.Json;

if (args[0] == "--all-games")
{
    AllGames.Run(args[1]);
    return;
}
if (args[0] == "--tools")
{
    AllGames.RunTools(args[1]);
    return;
}
if (args[0] == "--boxes")
{
    AllGames.RunBoxes(args[1], args.ElementAtOrDefault(2));
    return;
}
if (args[0] == "--extras")
{
    AllGames.RunExtras(args[1], args.ElementAtOrDefault(2));
    return;
}
if (args[0] == "--box-save")
{
    AllGames.RealSave(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
    return;
}
if (args[0] == "--strings")
{
    AllGames.CheckStrings(args[1]);
    return;
}
if (args[0] == "--forms")
{
    AllGames.ListForms(args[1]);
    return;
}
if (args[0] == "--za-tools")
{
    ZaChecks.Run(args[1], args[2]);
    return;
}
if (args[0] == "--catalog")
{
    File.WriteAllText(args[1], JsonSerializer.Serialize(Catalog.SpeciesIds));
    Console.WriteLine($"Especies en Z-A: {Catalog.SpeciesIds.Count()}");
    return;
}
string source = Path.GetFullPath(args[0]), work = Path.GetFullPath(args[1]);
Directory.CreateDirectory(work);
string protectedHash = SaveDocument.Hash(File.ReadAllBytes(source));
var results = new List<string>();
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    results.Add("PASS: " + description); Console.WriteLine(results[^1]);
}
void Reject(Action action, string label)
{
    try { action(); } catch (InvalidOperationException) { Check(true, label); return; }
    throw new Exception("FAIL: expected rejection: " + label);
}
var doc = SaveDocument.Open(source);
Check(doc.Pokemon.Count == 7 && doc.Save.PartyCount == 6, "Loads party and boxes");
Check(doc.DirtyCount == 0, "Opening does not stage changes");
Check(doc.TeamSlots().Count(p => p != null) == 6 && doc.BoxSlots(0).Count == 30 && doc.BoxCount == 32, "Lays out the team and the boxes");
Check(doc.Rules.BoxName(1) == "Caja 2" && doc.BoxLabel(0).StartsWith("Caja 1"), "Unnamed boxes use the game's Spanish name");
Check(SaveDocument.Hash(doc.BuildOutput()) == protectedHash, "No-op save is byte-for-byte identical");
var expectedForms = new Dictionary<ushort, ushort> { [152] = 154, [661] = 663, [659] = 660, [664] = 666, [179] = 181, [13] = 15, [172] = 26 };
foreach (var e in doc.Pokemon)
{
    var targets = Catalog.GetFinalForms(e);
    Check(targets.Any(x => x.Species == expectedForms[e.Original.Species]), "Final evolution: " + e.SpeciesName);
    Catalog.Optimize(e);
    Check(Catalog.Validate(e) == null && e.Edit.EVs.Sum() == 508 && e.Edit.IVs.All(x => x == 31), "Valid optimized build: " + e.SpeciesName);
}
Check(Catalog.NatureEffect(Nature.Adamant).Contains("↑ Ataque") && Catalog.NatureEffect(Nature.Adamant).Contains("↓ At. Especial"), "Nature effects use correct stat order");
var bunny = doc.Pokemon.Single(p => p.Original.Species == 659);
Check(Catalog.GetMoves(bunny).Any(x => x.Id == (int)Move.RockSmash), "Bunnelby learns Rock Smash");
Check(!Catalog.GetMoves(doc.Pokemon.Single(p => p.Original.Species == 179)).Any(x => x.Id == (int)Move.RockSmash), "Incompatible Rock Smash is excluded");
bunny.Edit.Moves = [(ushort)Move.Tackle, (ushort)Move.RockSmash, 0, 0];
var changed = doc.BuildOutput();
Check(SwishCrypto.GetIsHashValid(changed) && changed.Length == doc.OriginalBytes.Length, "Round-trip output passes save hash and size checks");
var first = doc.Pokemon[0]; var clean = Draft.From(first.Original);
first.Edit.IVs[0] = 32; Reject(() => doc.BuildOutput(), "Rejects IV above 31");
first.Edit = Draft.From(first.Original); first.Edit.EVs = [252, 252, 7, 0, 0, 0];
Reject(() => doc.BuildOutput(), "Rejects total EV above 510");
first.Edit.EVs = [253, 0, 0, 0, 0, 0]; Reject(() => doc.BuildOutput(), "Rejects per-stat EV above 252");
first.Edit = Draft.From(first.Original); first.Edit.Moves[1] = first.Edit.Moves[0];
Reject(() => doc.BuildOutput(), "Rejects duplicate moves");
first.Edit = Draft.From(first.Original); first.Edit.Moves[0] = 0;
Reject(() => doc.BuildOutput(), "Rejects gaps in moves");
first.Edit = Draft.From(first.Original); first.Edit.Moves[0] = (ushort)Move.RockSmash;
Reject(() => doc.BuildOutput(), "Rejects incompatible moves");
first.Edit = clean;
// Shiny edits must be stable across previews and serialization, and preserve trainer identity.
var shinyDoc = SaveDocument.Open(source);
var shinyEntry = shinyDoc.Pokemon.Single(p => p.Original.Species == 661);
shinyEntry.Edit.IsShiny = true;
Check(shinyDoc.DirtyCount == 1 && shinyEntry.Preview().IsShiny, "Shiny toggle changes the preview and marks the draft dirty");
var shinyBytes = shinyDoc.BuildOutput();
Check(shinyBytes.SequenceEqual(shinyDoc.BuildOutput()), "Shiny PID is deterministic across repeated serialization");
var shinyPath = Path.Combine(work, "main.shiny-test"); File.WriteAllBytes(shinyPath, shinyBytes);
var reopenedShiny = SaveDocument.Open(shinyPath);
var shinyPokemon = reopenedShiny.Pokemon.Single(p => p.Original.Species == 661);
Check(shinyPokemon.Original.IsShiny && shinyPokemon.Original.ChecksumValid, "Shiny flag persists after exporting and reopening");
Check(shinyPokemon.Original.ID32 == shinyEntry.Original.ID32 && shinyPokemon.Original.EncryptionConstant == shinyEntry.Original.EncryptionConstant,
    "Shiny edit preserves trainer IDs and encryption constant");
var beforeShiny = shinyEntry.Original.Data.ToArray(); var afterShiny = shinyPokemon.Original.Data.ToArray();
Check(Enumerable.Range(0, beforeShiny.Length).All(i => beforeShiny[i] == afterShiny[i] || i is 6 or 7 or >= 0x1C and <= 0x1F),
    "Shiny-only edit changes only PID and checksum bytes");
Catalog.Optimize(shinyEntry);
Check(shinyEntry.Edit.IsShiny && shinyEntry.Preview().IsShiny, "Optimization preserves the selected shiny state");
shinyPokemon.Edit.IsShiny = false;
var normalPath = Path.Combine(work, "main.normal-test"); File.WriteAllBytes(normalPath, reopenedShiny.BuildOutput());
Check(!SaveDocument.Open(normalPath).Pokemon.Single(p => p.Original.Species == 661).Original.IsShiny, "Shiny can be switched back to normal");
// Changing an ability touches its two stored values and the checksum, nothing else.
var abilityDoc = SaveDocument.Open(source);
var abilityEntry = abilityDoc.Pokemon.First(p => Catalog.GetAbilities(p).Count == 3);
var abilityChoices = Catalog.GetAbilities(abilityEntry);
Check(abilityChoices.Select(c => c.Slot).SequenceEqual([0, 1, 2]), "Offers primary, secondary and hidden abilities");
var hiddenAbility = abilityChoices[2];
Catalog.SetAbility(abilityEntry, hiddenAbility);
Check(abilityDoc.DirtyCount == 1 && abilityEntry.Preview().Ability == hiddenAbility.Ability && abilityEntry.Preview().AbilityNumber == 4, "Hidden ability changes the preview and marks the draft dirty");
var abilityPath = Path.Combine(work, "main.ability-test"); File.WriteAllBytes(abilityPath, abilityDoc.BuildOutput());
var reopenedAbility = SaveDocument.Open(abilityPath);
var savedAbility = reopenedAbility.Pokemon.Single(p => p.IsParty == abilityEntry.IsParty && p.Index == abilityEntry.Index);
Check(savedAbility.Original.Ability == hiddenAbility.Ability && savedAbility.Original.AbilityNumber == 4 && savedAbility.Original.ChecksumValid, "Hidden ability persists after exporting and reopening");
var beforeAbility = abilityEntry.Original.Data.ToArray(); var afterAbility = savedAbility.Original.Data.ToArray();
var abilityBytes = Enumerable.Range(0, beforeAbility.Length).Where(i => beforeAbility[i] != afterAbility[i]).ToList();
Console.WriteLine("Ability edit changed bytes: " + string.Join(", ", abilityBytes.Select(i => "0x" + i.ToString("X2"))));
Check(abilityBytes.All(i => i is 6 or 7 or >= 0x14 and <= 0x16), "Ability-only edit changes only ability and checksum bytes");
Catalog.SetAbility(savedAbility, Catalog.GetAbilities(savedAbility)[0]);
var secondAbilityPath = Path.Combine(work, "main.ability-back-test"); File.WriteAllBytes(secondAbilityPath, reopenedAbility.BuildOutput());
Check(SaveDocument.Open(secondAbilityPath).Pokemon.Single(p => p.IsParty == abilityEntry.IsParty && p.Index == abilityEntry.Index).Original.Ability == abilityChoices[0].Ability, "Hidden ability can be switched back to the primary one");
var cancelledAbility = SaveDocument.Open(source); var cancelEntry = cancelledAbility.Pokemon.First(p => Catalog.GetAbilities(p).Count == 3);
var cancelStart = Catalog.GetAbilities(cancelEntry).First(c => c.Ability == cancelEntry.Baseline.Ability);
Catalog.SetAbility(cancelEntry, Catalog.GetAbilities(cancelEntry).First(c => c.Ability != cancelEntry.Baseline.Ability)); Catalog.SetAbility(cancelEntry, cancelStart);
Check(cancelledAbility.DirtyCount == 0 && SaveDocument.Hash(cancelledAbility.BuildOutput()) == protectedHash, "Cancelling an ability change keeps the original save byte-for-byte");
var cancelled = SaveDocument.Open(source); cancelled.Pokemon[0].Edit.IsShiny = true; cancelled.Pokemon[0].Edit.IsShiny = false;
Check(cancelled.DirtyCount == 0 && SaveDocument.Hash(cancelled.BuildOutput()) == protectedHash, "Cancelling a shiny toggle keeps the original save byte-for-byte");
// Money and inventory edits use their own draft and touch only requested blocks/slots.
var bagDoc = SaveDocument.Open(source);
Check(bagDoc.Money == bagDoc.Save.Money && !bagDoc.HasChanges, "Money and inventory load without staging changes");
Check(new ushort[] { 50, 1124, 1125, 1126, 1127, 1128 }.All(id => bagDoc.Items.Any(i => i.Id == id && i.IsExperience)),
    "Inventory catalog includes Rare Candy and all five Exp. Candy sizes");
Check(bagDoc.Items.Where(i => i.Pouch == InventoryType.MegaStones).All(i => i.MaxCount == 1), "Mega Stones have a one-unit limit");
bagDoc.Money = 1_234_567;
Check(bagDoc.HasChanges && bagDoc.MoneyChanged && bagDoc.DirtyCount == 0, "Money-only edits mark the document dirty without changing Pokémon");
var moneyPath = Path.Combine(work, "main.money-test"); File.WriteAllBytes(moneyPath, bagDoc.BuildOutput());
Check(SaveDocument.Open(moneyPath).Save.Money == 1_234_567, "Edited money persists after export and reopening");
bagDoc.Money = -1; Reject(() => bagDoc.BuildOutput(), "Rejects negative money");
bagDoc.Money = (long)bagDoc.Save.MaxMoney + 1; Reject(() => bagDoc.BuildOutput(), "Rejects money above the game maximum");
bagDoc.ResetResources();
bagDoc.SetItemCount(1128, 0); Check(bagDoc.AddItem(1128, 75) == 75 && bagDoc.GetItemCount(1128) == 75, "Adds Exp. Candy XL to the current count");
Check(bagDoc.AddItem(1128, 1000) == 924 && bagDoc.GetItemCount(1128) == 999, "Adding items stops at the allowed maximum");
bagDoc.SetItemCount(1128, 120); Check(bagDoc.GetItemCount(1128) == 120, "Setting quantity replaces the total count");
Reject(() => bagDoc.SetItemCount(1128, -1), "Rejects negative item quantities");
Reject(() => bagDoc.SetItemCount(1128, 1000), "Rejects item quantities above their limit");
Reject(() => bagDoc.SetItemCount(2598, 1), "Quest-only items are not editable as ordinary inventory");
Reject(() => bagDoc.SetItemCount(2619, 999), "Collection progress items are not edited as ordinary inventory");
foreach (ushort id in new ushort[] { 50, 1124, 1125, 1126, 1127 }) bagDoc.SetItemCount(id, 30);
bagDoc.SetItemCount(658, 1); bagDoc.Money = bagDoc.Save.MaxMoney;
bagDoc.Pokemon[0].Edit.IVs[0] = 11;
var bagPath = Path.Combine(work, "main.inventory-test"); File.WriteAllBytes(bagPath, bagDoc.BuildOutput());
var reopenedBag = SaveDocument.Open(bagPath);
Check(reopenedBag.Money == bagDoc.Save.MaxMoney && reopenedBag.Pokemon[0].Original.IV_HP == 11,
    "Money and Pokémon edits save together");
Check(reopenedBag.GetItemCount(1128) == 120 && reopenedBag.GetItemCount(658) == 1
    && new ushort[] { 50, 1124, 1125, 1126, 1127 }.All(id => reopenedBag.GetItemCount(id) == 30),
    "Candies and Mega Stones persist after exporting and reopening");
Check(reopenedBag.ZaSave!.Items.GetItem(1128).Pouch == InventoryItem9a.PouchOther && !reopenedBag.ZaSave!.Items.GetItem(1128).IsNewNotify,
    "New candy records have the correct pocket and acquisition flags");
reopenedBag.SetItemCount(1128, 0);
var removedPath = Path.Combine(work, "main.item-removal-test"); File.WriteAllBytes(removedPath, reopenedBag.BuildOutput());
Check(SaveDocument.Open(removedPath).GetItemCount(1128) == 0, "Zero quantity removes an existing item without clearing other slots");
bagDoc.Pokemon[0].Edit = Draft.From(bagDoc.Pokemon[0].Original); bagDoc.ResetResources();
Check(!bagDoc.HasChanges && SaveDocument.Hash(bagDoc.BuildOutput()) == protectedHash, "Undoing money and item edits restores a byte-identical save");
// Equipment accounting and friendship must survive round-trip export.
var equipmentDoc = SaveDocument.Open(source);
var mareep = equipmentDoc.Pokemon.Single(p => p.Original.Species == 179);
Check(equipmentDoc.GetHeldChoices().Any(c => c.Id == 658) && !equipmentDoc.GetHeldChoices().Any(c => c.Id == 2598 || c.Id == 328),
    "Held-item choices include Mega Stones and exclude key items and TMs");
mareep.Edit.Friendship = 255;
var friendshipPath = Path.Combine(work, "main.friendship-test"); File.WriteAllBytes(friendshipPath, equipmentDoc.BuildOutput());
Check(SaveDocument.Open(friendshipPath).Pokemon.Single(p => p.Original.Species == 179).Original.CurrentFriendship == 255,
    "Maximum friendship persists after export and reopening");
mareep.Edit.Friendship = 256; Reject(() => equipmentDoc.BuildOutput(), "Rejects friendship above 255");
mareep.Edit.Friendship = -1; Reject(() => equipmentDoc.BuildOutput(), "Rejects negative friendship");
equipmentDoc.ResetPokemon(mareep);
equipmentDoc.SetItemCount(658, 1); equipmentDoc.EquipItem(mareep, 658);
Check(mareep.Edit.HeldItem == 658 && equipmentDoc.GetItemCount(658) == 1 && equipmentDoc.IsItemHeld(658),
    "Equipping a Mega Stone retains its single acquired unit");
var equippedPath = Path.Combine(work, "main.equipped-test"); File.WriteAllBytes(equippedPath, equipmentDoc.BuildOutput());
var equipped = SaveDocument.Open(equippedPath); var equippedMareep = equipped.Pokemon.Single(p => p.Original.Species == 179);
Check(equippedMareep.Original.HeldItem == 658 && equipped.ZaSave!.Items.GetItem(658).IsHeld && equipped.GetItemCount(658) == 1,
    "Mega Stone assignment and inventory held flag persist together");
var boxedPichu = equipped.Pokemon.Single(p => p.Original.Species == 172);
Reject(() => equipped.EquipItem(boxedPichu, 658), "Rejects assigning one unique stone to a second Pokémon, including boxed Pokémon");
Reject(() => equipped.SetItemCount(658, 0), "Cannot remove an assigned Mega Stone from the bag");
boxedPichu.Edit.HeldItem = 658; Reject(() => equipped.BuildOutput(), "Save validation rejects duplicate unique held items");
equipped.ResetPokemon(boxedPichu);
equipped.EquipItem(equippedMareep, 0);
Check(equipped.GetItemCount(658) == 1 && !equipped.IsItemHeld(658), "Unequipping a Mega Stone makes it available without duplicating it");
var unequippedPath = Path.Combine(work, "main.unequipped-test"); File.WriteAllBytes(unequippedPath, equipped.BuildOutput());
var unequipped = SaveDocument.Open(unequippedPath);
Check(!unequipped.ZaSave!.Items.GetItem(658).IsHeld && unequipped.Pokemon.Single(p => p.Original.Species == 179).Original.HeldItem == 0,
    "Unequipping clears both the Pokémon item and the inventory assignment flag");
equipped.EquipItem(boxedPichu, 658);
var transferredPath = Path.Combine(work, "main.stone-transfer-test"); File.WriteAllBytes(transferredPath, equipped.BuildOutput());
Check(SaveDocument.Open(transferredPath).Pokemon.Single(p => p.Original.Species == 172).Original.HeldItem == 658,
    "Unique stones can be transferred from the party to a boxed Pokémon");
var ordinary = SaveDocument.Open(source); var receiver = ordinary.Pokemon.Single(p => p.Original.Species == 179);
ordinary.SetItemCount(17, 3); ordinary.EquipItem(receiver, 17);
Check(ordinary.GetItemCount(17) == 2, "An ordinary held item consumes one unit from the bag");
ordinary.SetItemCount(149, 2); ordinary.EquipItem(receiver, 149);
Check(ordinary.GetItemCount(17) == 3 && ordinary.GetItemCount(149) == 1,
    "Changing held items returns the previous item and consumes the next");
var ordinaryPath = Path.Combine(work, "main.ordinary-equipment-test"); File.WriteAllBytes(ordinaryPath, ordinary.BuildOutput());
var ordinaryReloaded = SaveDocument.Open(ordinaryPath); var ordinaryReceiver = ordinaryReloaded.Pokemon.Single(p => p.Original.Species == 179);
ordinaryReloaded.EquipItem(ordinaryReceiver, 0);
Check(ordinaryReloaded.GetItemCount(149) == 2 && ordinaryReceiver.Edit.HeldItem == 0,
    "An ordinary item is returned correctly after reopening the save");
var emptyStock = SaveDocument.Open(source); var emptyReceiver = emptyStock.Pokemon[0]; emptyStock.SetItemCount(17, 0);
Reject(() => emptyStock.EquipItem(emptyReceiver, 17), "Cannot equip an ordinary item without stock");
Check(emptyReceiver.Edit.HeldItem == 0, "Failed equipment leaves the Pokémon unchanged");
Reject(() => emptyStock.EquipItem(emptyReceiver, 2598), "Cannot equip a key item");
var capacity = SaveDocument.Open(source); var capacityReceiver = capacity.Pokemon[0];
capacity.SetItemCount(17, 1); capacity.EquipItem(capacityReceiver, 17); capacity.SetItemCount(17, 999);
Reject(() => capacity.EquipItem(capacityReceiver, 0), "A full bag does not silently discard a returned item");
Check(capacityReceiver.Edit.HeldItem == 17 && capacity.GetItemCount(17) == 999, "Failed item return preserves equipment and bag quantities");
capacity.SetItemCount(17, 998); capacity.EquipItem(capacityReceiver, 0);
Check(capacity.GetItemCount(17) == 999, "Returned item fits after freeing space");
Reject(() => equipmentDoc.ResetResources(), "Cannot undo required ownership while its Mega Stone is assigned");
Check(equipmentDoc.GetItemCount(658) == 1 && mareep.Edit.HeldItem == 658, "Blocked resource undo preserves the ownership and assignment edits");
equipmentDoc.ResetPokemon(mareep); equipmentDoc.ResetResources();
Check(!equipmentDoc.HasChanges && SaveDocument.Hash(equipmentDoc.BuildOutput()) == protectedHash,
    "Cancelling friendship and equipment restores the exact original save");
var orbDoc = SaveDocument.Open(source); var orbReceiver = orbDoc.Pokemon[0]; orbDoc.SetItemCount(534, 1); orbDoc.EquipItem(orbReceiver, 534);
var orbPath = Path.Combine(work, "main.orb-test"); File.WriteAllBytes(orbPath, orbDoc.BuildOutput());
var orbReloaded = SaveDocument.Open(orbPath);
Check(orbReloaded.GetItemCount(534) == 0 && !orbReloaded.ZaSave!.Items.GetItem(534).IsNew && orbReloaded.Pokemon[0].Original.HeldItem == 534,
    "Unique primal orbs use consumed-item accounting and acquisition flags");
var handlerSave = (SAV9ZA)SaveUtil.GetSaveFile(File.ReadAllBytes(source))!;
var handled = (PA9)handlerSave.GetPartySlotAtIndex(0); handled.CurrentHandler = 1;
handled.OriginalTrainerFriendship = 33; handled.HandlingTrainerFriendship = 44;
handlerSave.SetPartySlotAtIndex(handled, 0, EntityImportSettings.None);
var handlerPath = Path.Combine(work, "main.handled-friendship-test"); File.WriteAllBytes(handlerPath, handlerSave.Write().ToArray());
var handlerDoc = SaveDocument.Open(handlerPath); handlerDoc.Pokemon[0].Edit.Friendship = 255;
var handlerOutput = Path.Combine(work, "main.handled-friendship-out"); File.WriteAllBytes(handlerOutput, handlerDoc.BuildOutput());
var handlerResult = SaveDocument.Open(handlerOutput).Pokemon[0].Original;
Check(handlerResult.HandlingTrainerFriendship == 255 && handlerResult.OriginalTrainerFriendship == 33 && handlerResult.CurrentHandler == 1,
    "Friendship editing changes the current handler's value and preserves the other trainer's value");
// The trainer and the Pokédex touch their own blocks and nothing else. This save is played on, so what it holds is read from it.
var toolDoc = SaveDocument.Open(source);
var card = toolDoc.Trainer.Original; var dex = toolDoc.Pokedex;
Check(card.Name.Length != 0 && card.Hours + card.Minutes != 0 && toolDoc.Trainer.IsSupported && !toolDoc.Trainer.HasBadges && toolDoc.Trainer.HasGender, "Reads the real trainer, whose game has no badges");
int seenBefore = dex.Entries.Count(toolDoc.Save.GetSeen), caughtBefore = dex.Entries.Count(toolDoc.Save.GetCaught);
Check(dex.CanEdit && dex.CanClear && dex.Entries.Count != 0 && dex.SeenCount == seenBefore && dex.CaughtCount == caughtBefore, "Reads the real Pokédex");
Check(SaveDocument.Hash(toolDoc.BuildOutput()) == protectedHash && !toolDoc.HasChanges, "Reading the trainer and the Pokédex keeps the save byte-for-byte");
var unseen = dex.Entries.Where(s => !dex.Get(s).Seen).ToList(); var owned = dex.Entries.Where(s => dex.Get(s).Caught).ToList();
Check(unseen.Count >= 2 && owned.Count >= 2, "The save has species to mark and to clear");
ushort seenOnly = unseen[0], newlyCaught = unseen[1], uncaught = owned[0], emptied = owned[^1];
dex.Set(seenOnly, true, false); dex.Set(newlyCaught, true, true); dex.Set(uncaught, true, false); dex.Set(emptied, false, false);
toolDoc.Trainer.Edit = card with { Name = "Ana", Gender = (byte)(1 - card.Gender), Tid = 123456, Sid = 789, Hours = card.Hours + 3 };
var toolPath = Path.Combine(work, "main.tools-test"); File.WriteAllBytes(toolPath, toolDoc.BuildOutput());
var toolSaved = SaveDocument.Open(toolPath);
var realBefore = (SAV9ZA)toolDoc.Save; var realAfter = (SAV9ZA)toolSaved.Save;
Check(toolSaved.Trainer.Original == toolDoc.Trainer.Edit && realAfter.OT == "Ana" && realAfter.ID32 == 789_123_456, "Trainer edits persist in the game's six-digit format");
Check(realAfter.Played.PlayedFractionalSeconds == realBefore.Played.PlayedFractionalSeconds && realAfter.Played.OverworldSeconds == realBefore.Played.OverworldSeconds, "Play time keeps its fraction and leaves the overworld clock alone");
Check(toolSaved.Pokedex.Get(seenOnly) == new DexState(true, false) && toolSaved.Pokedex.Get(newlyCaught) == new DexState(true, true)
    && toolSaved.Pokedex.Get(uncaught) == new DexState(true, false) && toolSaved.Pokedex.Get(emptied) == new DexState(false, false), "Pokédex edits persist after exporting and reopening");
int seenAfter = dex.Entries.Count(realAfter.GetSeen), caughtAfter = dex.Entries.Count(realAfter.GetCaught);
Check(seenAfter == seenBefore + 1 && caughtAfter == caughtBefore - 1, "No other species changes");
var sighted = realAfter.Zukan.GetEntry(seenOnly);
var sightedInfo = realAfter.Personal[seenOnly];
var sightedGender = sightedInfo.Genderless ? Gender.Genderless : sightedInfo.OnlyFemale ? Gender.Female : Gender.Male;
Check(sighted.IsSeen && sighted.GetIsGenderSeen((byte)sightedGender) && !sighted.GetDisplayIsNew() && !sighted.IsCaught
    && sighted.DisplayGender == sighted.GetDisplayGender(sightedGender, seenOnly, sighted.DisplayForm), "A seen species looks like a sighting in a played save");
var registered = realAfter.Zukan.GetEntry(newlyCaught);
Check(registered.IsCaught && registered.GetLanguageFlag(realAfter.Language) && registered.GetDisplayIsNew(), "A caught species looks like a catch in a played save");
var released = realAfter.Zukan.GetEntry(uncaught);
Check(released.IsSeen && !released.IsCaught && !released.GetLanguageFlag(realAfter.Language) && !released.GetDisplayIsNew(), "Clearing caught keeps what was seen");
var wiped = realAfter.Zukan.GetEntry(emptied);
Check(!wiped.IsSeen && !wiped.IsCaught && wiped.DisplayForm == 0, "Clearing seen empties the entry");
var changedBlocks = realBefore.AllBlocks.Zip(realAfter.AllBlocks).Where(b => !b.First.Data.SequenceEqual(b.Second.Data)).Select(b => b.First.Key).ToList();
Check(changedBlocks.Count == 3 && changedBlocks.Contains(SaveBlockAccessor9ZA.KPlayedSeconds)
    && realBefore.AllBlocks.Count(b => changedBlocks.Contains(b.Key) && b.Data.Length == realBefore.MyStatus.Data.Length) == 1
    && realBefore.AllBlocks.Count(b => changedBlocks.Contains(b.Key) && b.Data.Length == realBefore.Zukan.Data.Length) == 1, "Only the trainer, clock and Pokédex blocks change");
toolDoc.Trainer.Reset(); dex.Reset();
Check(!toolDoc.HasChanges && SaveDocument.Hash(toolDoc.BuildOutput()) == protectedHash, "Undoing the trainer and the Pokédex restores a byte-identical save");
var nothingNew = SaveDocument.Open(source); nothingNew.Trainer.Edit = card with { Name = "Abcdefghijklmn" };
Reject(() => nothingNew.BuildOutput(), "Rejects a trainer name the game can't store");
var fullDex = SaveDocument.Open(source); fullDex.Pokedex.MarkAll(caught: true);
var fullPath = Path.Combine(work, "main.full-dex-test"); File.WriteAllBytes(fullPath, fullDex.BuildOutput());
var fullRead = SaveDocument.Open(fullPath).Pokedex;
Check(fullRead.CaughtCount == fullRead.Entries.Count && fullRead.SeenCount == fullRead.Entries.Count, "The whole Pokédex can be marked as caught");
// Test the writer only on an isolated copy, never on the user's active save.
var copy = Path.Combine(work, "main.test"); File.WriteAllBytes(copy, File.ReadAllBytes(source));
var tempDoc = SaveDocument.Open(copy); tempDoc.Pokemon[0].Edit.IVs[0] = 12;
var backup = tempDoc.Write(copy, Path.Combine(work, "Backups"), _ => null);
Check(SaveDocument.Hash(File.ReadAllBytes(Path.Combine(backup, "main.test.original"))) == protectedHash, "Creates exact verified backup before overwriting");
var actual = SaveDocument.Open(copy);
Check(actual.Pokemon[0].Original.IV_HP == 12 && actual.DirtyCount == 0, "Atomic write and reopening retain requested edit");
ushort backedUp = actual.Pokedex.Entries.First(s => !actual.Pokedex.Get(s).Caught);
actual.Trainer.Edit = actual.Trainer.Original with { Name = "Ana" }; actual.Pokedex.Set(backedUp, true, true);
var toolBackup = actual.Write(copy, Path.Combine(work, "Backups"), _ => null);
var toolNotes = File.ReadAllText(Path.Combine(toolBackup, "cambios.json"));
Check(toolNotes.Contains("\"Ana\"") && toolNotes.Contains($"\"Species\": {backedUp}") && SaveDocument.Open(copy).Save.OT == "Ana", "Writer notes the trainer and the Pokédex in the backup");
actual = SaveDocument.Open(copy);
actual.Money = 500_000; actual.SetItemCount(1128, 250);
var resourceBackup = actual.Write(copy, Path.Combine(work, "Backups"), _ => null);
var savedResources = SaveDocument.Open(copy);
Check(savedResources.Save.Money == 500_000 && savedResources.GetItemCount(1128) == 250 && File.Exists(Path.Combine(resourceBackup, "main.test.original")),
    "Writer backs up, saves and reopens money and item changes");
var stale = SaveDocument.Open(copy); var external = File.ReadAllBytes(copy); external[40] ^= 1; File.WriteAllBytes(copy, external);
Reject(() => stale.Write(copy, Path.Combine(work, "Backups"), _ => null), "Rejects overwriting a save changed outside the app");
Check(File.ReadAllBytes(copy).SequenceEqual(external), "Conflict does not modify the destination");
Reject(() => SaveDocument.Open(copy), "Rejects corrupted input save");
Check(SaveDocument.Hash(File.ReadAllBytes(source)) == protectedHash, "User's real save remains untouched by tests");
File.WriteAllText(Path.Combine(work, "results.txt"), string.Join(Environment.NewLine, results));
Console.WriteLine($"{results.Count} checks passed.");
if (doc.ZaSave != null) ZaChecks.Run(source, Path.Combine(work, "za"));
else Console.WriteLine("Z-A checks skipped: this save is from another game. Run them on a Z-A save with --za-tools.");
AllGames.RealSave(source, Path.Combine(work, "boxes"));
AllGames.RealBoxName(source, Path.Combine(work, "names"));
