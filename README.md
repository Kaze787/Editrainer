# Editrainer

Save editor for **every main Pokémon game**, from Game Boy to Switch. It runs on your own Windows PC: pick a Pokémon and change its IVs, EVs, nature, ability, moves, form, held item or shiny status, along with your money and items. A full backup is made every time you save.

## Get started

You don't need to use the command line. Just follow these five steps.

### 1. Install .NET Desktop Runtime 10

Open the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0). In the **.NET Desktop Runtime** section, find **Windows**, click **x64** under **Installers**, and run the file you get. Skip this step if you already have it.

### 2. Download Editrainer

**[Click here to download Editrainer-windows.zip](https://github.com/Kaze787/Editrainer/releases/latest/download/Editrainer-windows.zip)**

### 3. Extract the ZIP

Find the file in your **Downloads** folder, right-click it, choose **Extract All…** and click **Extract**. Use the new folder from now on: the app can't run from inside the ZIP.

### 4. Download the Pokémon images

In the new folder, double-click **`Download-Images.bat`**.

- A black window opens and counts the images as they arrive. It needs an internet connection and about 360 MB of free space, and it can take several minutes.
- When it says **Done**, press any key to close it.
- If it gets interrupted, double-click the file again: it continues where it stopped.

The images are not in the ZIP because they belong to their owners, so each person downloads them from [PokeAPI/sprites](https://github.com/PokeAPI/sprites). This step is optional: without the images the app still works, but it shows its icon instead of each Pokémon.

### 5. Open Editrainer

Double-click **`Editrainer.exe`**. It opens your most recent save by itself.

It opens in your Windows language if it's one of the nine it offers, and in English otherwise. To change it, click the **⚙** button at the top left.

> **If Windows shows "Windows protected your PC"** in step 4 or 5, click **More info** and then **Run anyway**. Windows shows this message for apps that aren't digitally signed.

> **If Editrainer doesn't open or stays frozen**, check your antivirus. Some antivirus programs block or hold back new apps that aren't digitally signed. Its history shows whether it stopped a file.

> **Linux and macOS are not supported.** Editrainer is built with Windows Forms, which only runs on Windows.

## Supported games

| Console | Games | Save file |
|---|---|---|
| Game Boy / Color | Red, Blue, Yellow, Gold, Silver, Crystal | `.sav`, `.srm` |
| Game Boy Advance | Ruby, Sapphire, Emerald, FireRed, LeafGreen | `.sav`, `.srm` |
| Nintendo DS | Diamond, Pearl, Platinum, HeartGold, SoulSilver, Black, White, Black 2, White 2 | `.sav`, `.dsv` |
| Nintendo 3DS | X, Y, Omega Ruby, Alpha Sapphire, Sun, Moon, Ultra Sun, Ultra Moon | `main` |
| Switch | Let's Go, Sword, Shield, Brilliant Diamond, Shining Pearl, Legends: Arceus, Scarlet, Violet, Legends: Z-A | `main`, `savedata.bin` |
| GameCube | Colosseum, XD | `.gci` (untested, see below) |

When you open a save, the app adopts that game's name, limits and categories.

Always open the game's **save file**, not an emulator save state (`.ss0`, `.ml1`…): those are not saves. **Find saves** checks the folders of Eden, Yuzu and its forks, Ryujinx, Citra, Azahar, Lime3DS, RetroArch, mGBA, melonDS, DeSmuME and Dolphin, plus the folder that holds the app. If it finds several, it lets you choose.

### What changes by generation

- **Gen 1 and 2**: DVs from 0 to 15 and EVs from 0 to 65535, with no natures. HP and Special are calculated from the other DVs, so those fields are locked where needed. Gen 1 has no held items, friendship or shiny Pokémon. In Gen 2, shininess depends on the DVs, so ticking it changes them.
- **Gen 3 and 4**: the nature depends on the Pokémon's PID. When you change it or tick shiny, the app looks for a PID that keeps the gender, ability and form. The game accepts it without trouble, but a legality checker might flag it.
- **Gen 3 to 5**: EVs go up to 255 per stat, with 510 in total. From Gen 6 onwards, 252 per stat.
- **Let's Go**: AVs from 0 to 200 replace EVs, with no total limit. CP is recalculated.
- **Money and bag limits**: each game uses its own, including the space in each pocket.
- Key items and PC items can't be edited. The rules for Mega Stones and orbs only apply in Z-A.

### Testing status

- **Z-A** has been tested with real saves.
- The other **37 games** have been tested from start to finish with generated saves that use each format's real structure (`tests --all-games`): open, edit, save with a backup, reopen and compare. **They have not been tested with real saves**: before trusting a game, make a copy and check it in the emulator.
- **Colosseum and XD** could not be tested, because their encrypted GameCube container can't be generated from scratch. PKHeX does the reading and writing, but use them with a copy.
- In **Diamond, Pearl and Platinum**, the move list leaves out special tutors because of a bug in PKHeX 26.10.18. Moves the Pokémon already knows are kept.

## How to use it

1. The app opens your most recent save automatically. You can also click **Open save**, click **Find saves**, or drag the file onto the window.
2. Pick a Pokémon in the left panel, which works like the game's box screen: your **Team** on top, and one box with its empty slots below. Change box with **◀ ▶** or with the list, which shows how many Pokémon each box holds. The save opens on the last box you used in the game. Hover over a Pokémon to see its name, level and place, or move with the arrow keys after clicking the grid. **✦** marks shiny Pokémon and a green dot marks those with unsaved changes. The search box finds Pokémon by name, nickname or box: it dims the team members that don't match and shows results from every box in place of the open one.
3. Change its IVs, EVs, nature, ability or moves. The **Result** numbers show its stats at its current level. The **Shiny** box, next to the name and picture, turns shininess on or off, and the artwork and sprite change right away.
4. **Optimize** suggests IVs of 31, an Adamant or Modest nature and 508 useful EVs. **Target** lets you choose an evolution or its current form. It doesn't change the species or level. For evolutions with several branches, you can pick the one you prefer.
5. **Optimize all** prepares every Pokémon in your team and boxes for its final evolution. When there are several branches, it uses the evolution with the highest Attack or Special Attack; check the individual choice if you want another branch.
6. In **Moves**, type a full name or part of one in any of the four fields. The list filters without regard to case or accents: `aerial` finds `Aerial Ace`. Pick a result with the mouse or press **Enter** to confirm. **Esc** cancels the search and keeps the equipped move.
7. Close that console's emulator and click **Save game**. The app verifies the file and keeps a full backup of the previous one. **Save as** exports a copy.

Changes stay in memory until you save. **Undo this Pokémon** discards that Pokémon's pending changes. It doesn't change what was saved in earlier sessions. `Ctrl+O` opens a save and `Ctrl+S` saves.

## Settings: theme and language

The **⚙** button at the top left opens the settings:

- **Theme**: light or dark.
- **Language**: Spanish, English, Simplified Chinese, French, German, Italian, Japanese, Korean or Brazilian Portuguese. It changes the app's text and the names of Pokémon, moves, abilities, natures, types and items.

When you click **Apply**, the change happens right away, without closing the window, and keeps the open save, unsaved changes, the selected Pokémon, the box and the tab. Your choice is stored in `settings.json`, next to the app. Until you make one, the app uses your Windows language, or English if it isn't one of these nine. Any Portuguese version of Windows opens in Brazilian Portuguese.

- The language only changes what you see. What gets written to the save is identical in every language, and nicknames and trainer names stay as they are in the save.
- Windows draws the buttons on message boxes and the open and save file dialogs, so they appear in your system's language.
- PKHeX has no game names in Portuguese, so in Brazilian Portuguese the app's own text is translated but the names of Pokémon, moves, abilities, natures, types and items stay in English, and so do legality reports. The language selector says so too.
- Russian, Hindi and Arabic aren't offered, because PKHeX has no game names in those languages either.

## Forms and colors

Pokémon with several forms show a **Form** selector next to **Shiny**: Vivillon patterns, Flabébé, Minior and Squawkabilly colors, Unown letters, Furfrou trims, regional forms, Rotom, Oricorio, Deoxys and many more. Alcremie's cream and sweet share a single list. Choosing a form updates the types, stats, ability and available moves. It is saved like any other change, and **Undo this Pokémon** reverts it.

- Only forms that each game keeps outside battle appear. Mega Evolutions, Primal and battle forms, fusions (Kyurem, Necrozma, Calyrex), Totem Pokémon and forms that depend on gender (Meowstic, Indeedee, Basculegion, Oinkologne) are not offered. A Pokémon that already has one of those forms keeps it and doesn't show the selector.
- If the form depends on an item (Arceus, Silvally, Genesect, Ogerpon, the Origin forms of Giratina, Dialga and Palkia), the item is equipped when you choose the form, as long as it's in your bag. If it isn't, the app tells you which one is missing. Going back to the normal form returns the item to the bag.
- Gen 1 has no forms. In Gen 2 and 3 only Unown stores one: in Gen 2 the letter comes from the DVs, so choosing it changes them, and a shiny Unown can only be I or V. In Gen 3 it comes from the PID. Deoxys in Gen 3 depends on the game, not on the Pokémon.
- Furfrou and Hoopa start with their day counter full. When it runs out, the game returns them to their normal form.
- A move that only the previous form could learn is removed when the form changes. Moves the Pokémon already knew in the save are kept.
- Some forms can't be obtained normally in certain games. The game accepts them, but a legality checker might flag them.

## Ability

Pick a Pokémon and open **Stats**. The **Ability** selector, below the nature, shows its current ability and lets you change it to the **primary**, **secondary** or **hidden** one, depending on what that Pokémon has in that game. If two slots share an ability, it appears once. The change is applied with **Save game**, and **Undo this Pokémon** reverts it.

- **Gen 1 and 2** have no abilities, so the selector doesn't appear. **Gen 3 and 4** only have primary and secondary abilities. Hidden abilities exist from Gen 5 onwards.
- **Gen 3 to 5**: primary and secondary abilities depend on one bit of the PID, so the app looks for a PID that keeps the gender, nature and shininess. The Gen 5 hidden ability doesn't touch the PID.
- **Let's Go, Legends: Arceus and Legends: Z-A** don't use abilities in battle. The change is stored in the Pokémon, but you won't notice it while playing those games.
- If a Pokémon already has an ability that isn't on its list, it appears as **(current)** and stays until you choose another.
- Changing form keeps the same ability slot. If the new form doesn't have that slot, it moves to the primary one.
- Some abilities can't be obtained normally in certain games. The game accepts them, but a legality checker might flag them.

## Held item and friendship

Pick a Pokémon from your team or boxes and open **Stats**. The held item and friendship are next to the nature and the IVs and EVs.

- Search for an item by typing in the selector. **Mega Stones only** limits the list to those stones.
- **Equip** gives the chosen item to the Pokémon and **Remove** takes it away. Normal items take one unit from the bag, and it goes back when removed.
- In Z-A, Mega Stones keep their unit in the inventory and are assigned to a single Pokémon. To move a stone to another Pokémon, remove it from the one holding it first.
- If you don't have an item, **Bag** opens its entry in **Money and items**, where you can add it. Then go back to **Stats** to equip it.
- **Friendship** takes values from 0 to 255. **Maximum (255)** prepares the highest value.

Item and friendship changes are applied with **Save game**. **Undo this Pokémon** restores the previous values and recalculates the bag quantities. If there's no room to return an item, or you're trying to undo getting a stone you just equipped, the app keeps the changes and tells you how to solve it.

## Details

Pick a Pokémon and open **Details**.

- **Nickname**: leave it empty to go back to the species name.
- **Original trainer**: the name stored in the Pokémon.
- **Level**: changing it also updates the experience and recalculates the stats.
- **Poké Ball**: from Gen 3 onwards.

Names must fit the game's alphabet and length, and the app tells you if they don't.

**Legality** shows PKHeX's verdict on the Pokémon as it would be saved, and updates as you edit. It's information only and never stops you from saving.

**Copy Showdown** copies the Pokémon as a Showdown set. **Paste Showdown** applies a copied set to the selected Pokémon, as long as it's the same species: level, nickname, nature, IVs, EVs, ability, shiny status, the moves it can learn in this game, and the held item if it's in your bag. Whatever it can't use is listed at the bottom of the window, and the form doesn't change. As on Showdown, a set with no `Level:` line means level 100.

## Money and items

The **Money and items** tab changes the save's money and prepares changes to the bag. The maximum depends on the game (9,999,999 in Z-A, 999,999 on Game Boy and GBA). **Maximum** fills in that amount without saving it yet.

- Search for items by name or filter by category. **Show candies** lists Rare Candy and the five sizes of Exp. Candy.
- Select an item, enter how many units you want and click **+ Add** to add them to the ones you have.
- **Set quantity** sets the exact total. A quantity of 0 removes the item from the bag.
- The table shows the prepared quantity and the item's maximum. Mega Stones and other unique items have a maximum of one.
- **Undo money and items** discards that tab's pending changes and keeps your Pokémon edits.

Click **Save game** to apply the money, item and Pokémon changes together, after an automatic backup. If you open another save or close the editor with pending bag changes, the app asks before discarding them.

Rows keep their room for fields and labels. If you shrink the window and the content doesn't fit, you can scroll inside each tab.

## Whole-save tools

The **Tools** button, next to **Find saves**, gathers everything that works on the whole save: the four tools below, the Legends: Z-A tools, the trainer and Pokédex editors, and the box sorting and undo described further down. These four open their own window beside the editor, so you can keep working while it's open. Only restoring a backup writes anything.

- **Legality audit** checks every Pokémon as it would be saved, pending changes included, and lists the ones that aren't legal with the reasons. Click one to see it in the editor, and use **Check again** after fixing it.
- **Box report** shows every Pokémon in one table: place, species, nickname, level, nature, ability, IVs, EVs, moves, held item, shiny and legality. Click a column to sort by it. **Export CSV** saves the table for a spreadsheet, using your system's list separator. IVs and EVs follow the order HP / Attack / Defense / Speed / Sp. Atk / Sp. Def.
- **Manage backups** lists the `Backups` folder with the date, the game and what changed. **Restore** asks first, keeps a copy of the current file, puts the backup back and checks the result byte by byte. It refuses while that console's emulator is running. It also works with no save open, in case yours no longer opens.
- **Compare saves** takes two saves of the same game and lists which Pokémon, money and items differ. It goes place by place, so a Pokémon that moved shows up in both places, and it reads the files on disk, so pending changes don't count.

## Legends: Z-A tools

With a Z-A save open, **Tools → Z-A tools…** gathers what only this game keeps. Everything stays pending until you press **Save game**, like any other edit, and counts towards the unsaved-changes prompt and the backup record.

- **Donuts:** add donuts with up to three level 3 powers, add a shiny assortment, or remove one. New donuts are PKHeX's five-star template and fill free slots, so the ones you made stay as they are. Donuts belong to the Mega Dimension DLC.
- **Clothing:** marks as bought what the save already has on record, kind by kind. It can't add anything else, because PKHeX has no list of Z-A's clothes, and it leaves hair and makeup alone.
- **Points:** Royale, Infinite Royale and hyperspace survey points.
- **Collection:** collects the Colorful Screws and the TMs on the map, marking each as taken and adding it to the bag. It only reaches places you've already visited, because a save has no record of the others. Screws stop at 100.

Story progress, event flags, Mable's research, your position and your trainer pictures can't be edited here. Most event flags have no public name, so changing one would be blind.

## Trainer and Pokédex

**Tools** opens two windows that work on the whole save. What you change there stays pending like any other change: it's applied with **Save game**, it counts as a pending change when you close the app, and it's listed in the automatic backup. **Close** keeps what you prepared, and **Undo** discards it.

**Trainer** shows the name, gender, trainer ID, secret ID, play time and badges. Each game only shows what it stores:

- **Gender** appears in Crystal and from Gen 3 onwards. From Gen 6 the character's clothes and model are saved separately and don't change with it, so keep the backup at hand if you change it.
- **IDs** follow each game: one 5-digit ID in Gen 1 and 2, a 5-digit ID and secret ID in Gen 3 to 6, and a 6-digit ID with a 4-digit secret ID from Gen 7.
- **Badges** are one box each in Gen 1 to 6 and in Brilliant Diamond and Shining Pearl, with a second row for Kanto in Gold, Silver, Crystal, HeartGold and SoulSilver. Sword and Shield only keep how many you have. The other games don't have badges to edit.
- Changing the name or the IDs makes the game treat the Pokémon you already have as received from another trainer.
- Colosseum, XD and box-only saves don't have this window.

**Pokédex** lists every species the open game can register. Search by name or number, tick **Seen** or **Caught**, or use **Mark all seen** and **Mark all caught**.

- A caught Pokémon counts as seen, and clearing **Seen** also clears **Caught**.
- Marking registers the species and keeps the forms, shinies and languages you already have. It isn't meant to complete them: how many forms it adds depends on the game.
- A game can still hide what you marked until you reach that point of the story, such as species outside the regional Pokédex before you get the National one.
- **Legends: Arceus** only adds marks, because its progress comes from research: what's already registered can't be cleared.
- **Scarlet and Violet** saves from before version 2.0.1 keep the Pokédex in an older format. You can look at it, but not change it.
- In **Legends: Z-A** without the DLC the list is the Lumiose Pokédex.
- Colosseum, XD and box-only saves don't have a Pokédex.

## Boxes

Move your Pokémon around the way you would in the game. Like every other edit, nothing changes in your file until you press **Save game**, and each Pokémon with a pending change shows its dot.

- **Move and swap:** drag a Pokémon to another place in the box or the team. If someone is already there, they swap. To reach another box, select the Pokémon, turn the page and right-click a place there: **Move here** or **Swap places**.
- **Clone:** hold **Ctrl** while you drag, or right-click an empty place to clone the selected Pokémon there. **Clone**, on a Pokémon's own menu, uses the first free place.
- **Delete:** right-click a Pokémon and choose **Delete**.
- **Export and import:** right-click a Pokémon to save it as a `.pk*` file, or an empty place to bring one in. A Pokémon from another game is converted where PKHeX has a route (older to newer, Game Boy to Gen 7 or later, and between the Switch games) and refused, with the reason, where it doesn't. Eggs hatch when they change format, and the Pokédex is not updated.
- **Sort:** in **Tools**, sort the open box by Pokédex number, level or name.
- **Undo:** right-click any place to undo the last change, or use **Tools** to undo it or all of them.

Some things stay in place on purpose, and the app tells you why when you try:

- Boxes the game hasn't unlocked yet can't receive anyone, because the game wouldn't show them.
- The team always keeps at least one Pokémon that isn't an egg, and never has gaps: whoever leaves, the rest move up, and whoever joins goes last.
- Pokémon in a saved battle team, and your partner in Let's Go, can't be moved. Take them out of the team in the game first. Team members in Let's Go can be moved, but not deleted.
- A Pokémon holding mail in Gen 2 to 5 stays where it is, because the letter is stored apart from it.
- Game Boy boxes and the Let's Go storage are lists with no gaps. Taking a Pokémon out closes the gap, and one dropped on an empty place goes to the end of the list.
- A held item goes back to the bag when its Pokémon is deleted, so the bag needs room for it. A clone takes its own unit from the bag, or arrives without the item if there is none.
- In Legends: Z-A, a Pokémon holding a Mega Stone or an orb can't be cloned, because only one Pokémon can hold each.
- Colosseum and XD saves can't be rearranged.

Every change is checked after writing: each place of the team and the boxes has to hold exactly what it should, and nothing else may differ. This was tested on all 38 games with generated saves and byte by byte on real Z-A saves, but not by loading the results in each game, so try it on a copy first.

## Backups

Backups go in the `Backups` folder, inside the app's folder. The **Open backups** link opens it. Each save includes:

- `main.original` (or `<name>.sav.original`): the full save from before the edit.
- `cambios.json`: which Pokémon and values changed, along with the trainer and Pokédex changes.
- `destino.anterior`: an extra copy, if a file other than the one you opened was overwritten.

To restore a backup, use **Tools > Manage backups**. To do it by hand, close the emulator, open the `.original` file and use **Save as** to store it under its original name in the emulator's save folder. To open `main.original`, choose the **All files** filter in the file selector.

The app refuses to save values outside the game's limits, repeated or incompatible moves, and saves that changed outside the app after being opened. It also reopens and verifies the generated save before replacing the old one.

## Images

`Download-Images.bat` fetches normal and shiny sprites and artwork for all 1,025 species and their forms: 5,368 PNG files, stored in `Assets`. After that, the app needs no internet connection. If a form has no picture of its own, the base species is shown, and the Pokémon keeps its form in the save.

Source: [PokeAPI/sprites](https://github.com/PokeAPI/sprites). The original credits are in `Assets/SPRITES-CREDITS.md`.

## Source code and building

You only need this section to change the code. The source is in `src`, and the editing library is [PKHeX.Core 26.10.18](https://github.com/kwsch/PKHeX/tree/26.10.18), by Kaphotics and contributors.

With the .NET 10 SDK installed, this builds the app and creates `Editrainer-windows.zip`:

```powershell
.\build.ps1
```

The save tests are in `tests`. They run on an isolated copy and never write to the save they receive. `--za-tools <save> <folder>` runs each Z-A tool through a real write and reopen on a copy of a real Z-A save. `--all-games <folder>` tests every game with generated saves, including every form of each one, in each language. `--strings src` checks that all the app's text is translated. Visual checks can be run with `Editrainer.exe --preview <save> <capture.png> <app-folder> <index> <tab>`.

## License and credits

Distributed under GPL-3.0-or-later: see `LICENSE`. Full credits are in `CREDITOS.md`.

Editrainer is an unofficial fan project, not affiliated with Nintendo, Creatures, GAME FREAK or The Pokémon Company. Pokémon and all related characters and images belong to their respective owners.
