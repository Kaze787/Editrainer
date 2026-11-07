# Libraries

Binaries the app links against. They are kept here so that a checkout builds without restoring anything.

| File | What it is | Version | Licence | Source |
|---|---|---|---|---|
| `PKHeX.Core.dll` | Save reading and writing, legality rules | 26.10.18 (`bfe89c74`) | GPL-3.0-or-later | https://github.com/kwsch/PKHeX |
| `PKHeX.Core.AutoMod.dll` | Auto-Legality Mod engine: makes a legal Pokémon from a Showdown set | 26.08.27, commit `90410f2681a0a72680d12280a1e0f14715e67dff`, built against the `PKHeX.Core.dll` above | MIT, Copyright (c) 2018 Archit Date; notice in `CREDITOS.md` | https://github.com/santacrab2/PKHeX-Plugins |

`PKHeX.Core.AutoMod.dll` is 155,136 bytes, with this SHA-256:

```
d5ca0613ef985a55748d31f894448bd4cecdeddb7df71358075989d4dc8096b5
```

Its sources are unmodified. `build-tools/automod/PKHeX.Core.AutoMod.csproj` rebuilds it with the same bytes, and `build-tools/AutoLegality-notes.md` has the steps. It has to be rebuilt whenever `PKHeX.Core.dll` changes.
