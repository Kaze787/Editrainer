# Auto-Legality Mod: study and decision

**Decision: use the engine.** It builds against the exact `PKHeX.Core.dll` this app ships, pulls in nothing else, and reaches almost every species of all 38 games in about two seconds per game at worst. It ships as one small binary, `lib/PKHeX.Core.AutoMod.dll`, built from unmodified sources. The app drives it from `src/LegalGenerator.cs`, which is the only file that touches it.

The fallback of generating with PKHeX.Core alone was not needed. See "The alternative" below.

## What it brings in

PKHeX-Avalonia vendors the engine as the project `PKHeX.AutoMod`. It comes from `santacrab2/PKHeX-Plugins`, a fork of `architdate/PKHeX-Plugins`.

| | |
|---|---|
| Projects | 1 (`PKHeX.Core.AutoMod`). The WinForms plugin, `PKHeX.Core.Enhancements` and `PKHeX.Core.Injection` are not needed |
| Dependencies | Only PKHeX.Core. No NuGet package |
| Sources | 27 `.cs` files, 7,067 lines, 358 KB |
| Binary | 155,136 bytes, against 19 MB for PKHeX.Core |
| Upstream commit | `90410f2681a0a72680d12280a1e0f14715e67dff`, 2026-08-31, engine version 26.08.27 |

The copy in PKHeX-Avalonia (clone at `632109d`) matches upstream: all 27 sources have the same blob hash as `PKHeX.Core.AutoMod` at that commit.

## Does it build against our PKHeX.Core

Yes, with 0 errors and 0 warnings and nothing silenced. The clone's Core is 26.10.18, the same as `lib/PKHeX.Core.dll` (file version 26.10.18.0). Upstream pins 26.8.26, so the sources are two months older than the Core they meet here, and no API drift shows.

## Licence

**The engine is MIT, not GPL.** PKHeX-Avalonia's `VENDORED.md` says GPL-3.0-or-later, but upstream's `LICENSE` at the vendored commit reads "MIT License, Copyright (c) 2018 Archit Date", GitHub reports the same for both repositories, and the file has said so since June 2018.

This is compatible with a GPL app, and it changes what is owed: the copyright and permission notice must travel with every copy. It is in `CREDITOS.md`, which `build.ps1` puts in the ZIP. The authors are credited there and in the README.

## What the app has to do about it

The engine is written for a plugin inside PKHeX, and several of its defaults are wrong for this app. Each row was found by reading the code or by measuring.

| What the engine does | Why that matters | What the app does |
|---|---|---|
| Every search first calls `ALMVersion.GetIsMismatch()`, which asks GitHub for the latest PKHeX | An offline tool would phone home, and a search would wait on the network | `EnableDevMode = true` skips the call. The tests watch HTTP and require zero requests, after proving that the watch sees one |
| `GetLegalFromSet` turns a failed set into a meme species | The wrong Pokémon would arrive as a success | Calls `TryAPIConvert` directly, which has no such path |
| Pokémon found in other games get a default trainer, "ALM" | They wouldn't be the player's | Passes the save's trainer in the set's own lines, with `AllowTrainerOverride` |
| `ForceLevel100for50` turns level 50 into 100 | The set isn't what was asked for | Off |
| Batch commands run `.Field=value` lines from the set | A pasted text could edit fields directly | Off |
| Adds every legal ribbon and some markings | Changes nobody asked for | Off |
| `GenerateEncounters` doesn't skip versions with no context, and throws `ArgumentOutOfRangeException` | A species that gets to the end of the list stops with an error (seen on Ursaluna and Celebi) | Removes those versions from `PriorityOrder` |
| It reads the save's `Version`, which can be a pair or `Any` | About half of a HeartGold sample failed | Passes `rules.Version`, the single game |
| Bare sets keep only the moves an encounter comes with | A level 100 Pokémon with one move | Asks for a legal moveset, and keeps the original if that isn't legal |
| Let's Go's CP goes stale after adaptation | "Calculated CP does not match" on Golem | Recalculates it |
| No cancellation token, only `Timeout` | A search can't be interrupted | Waits with the token and leaves the worker behind. It stops by itself at its next check |

These parts are never reached, and are listed so nobody turns them on by accident:

- `ModLogic.GenerateLivingDex`. PKHeX-Avalonia documents that it returns nothing because it skips `OptimizeCriteria`. This was not reproduced here: the app has its own loop, which also skips what the save owns and stops when the boxes are full.
- `SmogonSetGenerator`, which fetches web pages, and `Legalizer.Dump`, which writes `error_log.txt`.
- `TrainerSettings`, which reads a `trainers` folder beside the executable. Every search carries its trainer, so it isn't consulted.

One risk stays open. The engine calls `EntityConverter.ConvertToType`, which the box tools found can hang on impossible routes. The game list only holds versions that can reach the save, and no search hit the time limit in any game, but if one hung, that worker thread would keep spinning until the app closes. The window would still answer.

Generated Pokémon pass PKHeX's `LegalityAnalysis` twice, once in the engine and once in `Catalog.CheckLegality`, on the final bytes after `ForThisGame`. Placement goes through `Arrive` or `Change`, so the box rules, the undo stack and `VerifyStorage` all apply. `PlaceAll` makes one `Change`, so one undo removes a whole living Pokédex.

## Measured coverage

`CoreTests.exe --living-dex <folder>` asks for every species of each game, on the generated test saves. Every result below passed `Catalog.CheckLegality`. There were no web requests and no timeouts.

| Games | Reached | Seconds per game | Skipped |
|---|---|---|---|
| Red, Yellow | 149 of 151 | 0.4 to 0.6 | Mr. Mime, Jynx |
| Blue (Japanese test save) | 150 of 151 | 0.4 | Mew |
| Gold, Silver, Crystal | 251 of 251 | 0.3 to 0.6 | |
| Ruby, Sapphire, Emerald, FireRed, LeafGreen | 386 of 386 | 0.2 to 0.7 | |
| Diamond, Pearl, Platinum, HeartGold, SoulSilver | 493 of 493 | 0.2 to 0.8 | |
| Black, White, Black 2, White 2 | 649 of 649 | 0.4 to 0.5 | |
| X, Y, Omega Ruby, Alpha Sapphire | 721 of 721 | 0.3 to 0.4 | |
| Sun, Moon | 802 of 802 | 0.3 to 0.4 | |
| Ultra Sun, Ultra Moon | 807 of 807 | 0.3 | |
| Let's Go Pikachu, Let's Go Eevee | 153 of 153 | 0.1 | |
| Sword, Shield | 657 of 664 | 0.7 to 0.8 | Celebi, Jirachi, Diancie, Magearna, Zeraora, Meltan, Melmetal |
| Brilliant Diamond, Shining Pearl | 491 of 493 | 1.5 to 1.6 | Celebi, Deoxys |
| Legends: Arceus | 242 of 242 | 0.5 | |
| Scarlet, Violet | 699 of 733 | 1.3 to 1.9 | 34 species, among them Regirock, Uxie, Heatran, Giratina, Arceus and Keldeo |
| Legends: Z-A | 363 of 364 | 0.7 | Milotic |

Each skipped species is named with a reason, as the task asks. What is known about those reasons:

- On the Switch games the engine reports no legal encounter. Magearna was traced: its only encounters are in other games, which here would need a HOME tracker that the engine won't invent. The other species were not traced one by one, and Z-A's Milotic is unexplained.
- On Game Boy, some results come back in the other region's format, and the same rule that guards file imports refuses them: Japanese and international saves don't mix. That accounts for Mew on the Japanese Blue save and one of the two skipped on Red and Yellow.

`--box-save` also writes generated Pokémon into copies of two real Z-A saves. Only the places they went to differ, byte by byte, and they are still legal when the file is read back.

Legends: Arceus refuses sets of fewer than four moves. That is the game's rule, not a limit of the engine: a Pokémon there leaves no move slot empty while it knows more.

## The alternative

`EncounterMovesetGenerator` with `ConvertToPKM` and `LegalityAnalysis` would have added no binary. It would also have stopped at what the encounter gives: the engine's 7,000 lines are the rest of the job, such as evolving to the requested species, solving PID and IV correlations, requested abilities, Hidden Power, hyper training, memories and handler data. A set would have to be rebuilt on top of that by hand, for every generation. That is a large amount of subtle code to own, and it has no advantage while the engine works.

Fall back to it only if a future engine stops compiling and upstream doesn't follow.

## Updating

After replacing `lib/PKHeX.Core.dll`, rebuild the engine against it:

```
git clone https://github.com/santacrab2/PKHeX-Plugins
git -C PKHeX-Plugins checkout <commit>
dotnet build build-tools/automod/PKHeX.Core.AutoMod.csproj -c Release --no-incremental -p:Sources=<clone>/PKHeX.Core.AutoMod -o <output>
```

Copy `PKHeX.Core.AutoMod.dll` from `<output>` to `lib/`, then update the commit and version in the `.csproj`, in `lib/README.md` and in `CREDITOS.md`. Run `CoreTests.exe --generate <folder>` and `--living-dex <folder>`: a Core update can change results without breaking the build.

The build is deterministic. Repeated builds, and a build from another folder, gave the same SHA-256, and the binary holds no local paths.

## Not verified

- No generated Pokémon was loaded in a game. "Legal" here means that PKHeX's analysis accepts it.
- The binary in `lib/` was built from PKHeX-Avalonia's copy, which matches upstream by hash. The `git clone` step above was not run.
- Coverage was measured on generated test saves, which are nearly empty and in Spanish, or Japanese for Blue. Only Z-A was tried on real saves.
- The engine's time limit was never reached, so that path was not exercised. The slowest Pokémon took about a second.
- Saves in other languages may reach different encounters, especially on Game Boy, where Japanese and other saves don't mix.
