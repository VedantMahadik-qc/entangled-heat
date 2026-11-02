# Build plan (29 Oct → 9 Nov 2026)

Target: **submit on 8 Nov**, keeping 9 Nov as a buffer. Hard deadline 9 Nov 23:59 UK.

## Status (29 Oct, evening)
Everything that doesn't need the game is done: heat graph, search grid, superposed safehouses, HUD and key check, multi-engine pool, seed cache, release script and INSTALL.md. 35 tests pass and the mod compiles against SHVDN 3.6. The first real IBM run worked (`ibm_marrakesh`). **Next: the in-game test pass from 3 Nov** (see the schedule).

**2 Nov:** the game is bought on Epic. Enhanced (1.0.1158.16) isn't supported by ScriptHookV, so **GTA V Legacy** is downloading instead. **Each collapse now gets a fresh Atlas job** (`FreshJobPerCollapse=true`). Real emu jobs take ~4 s (graph) and ~7 s (grid). IBM failures now back off 5 min; they used to retry every tick. 45 tests pass and the release zip has been rebuilt.

**2 Nov, later:** IBM `qpu` is failing right now: Moth's IBM plan allocation is "not active" (see ATLAS_API.md). `fake` mode, a noisy snapshot of a real chip, works. Built the **Schrödinger's Stash** showcase mod in `stash/` (graph-v1 anti-correlated pairs, `fake` Marrakesh by default, 19 tests, `stash/release/SchrodingersStash-0.1.0.zip`). It's for the Discord post and doesn't reveal Entangled Heat. In-game testing happens alongside EH, but **EH comes first if time is short**.

## Tech stack
- **GTA V (PC, Legacy or Enhanced; confirm ScriptHookV support for your build)**, single-player only
- **ScriptHookV** (native hook) + **ScriptHookVDotNet 3** (C# scripting, .NET Framework 4.8)
- HUD uses SHVDN's built-in `TextElement` (LemonUI dropped, so judges install one less thing)
- `HttpClient` + `System.Text.Json`/Newtonsoft for the Moth API, on background tasks
- Visual Studio 2022 / Rider, OBS for recording
- Distribution: GitHub Releases and/or gta5-mods.com (free download)

## Architecture sketch
```
src/EntangledHeat.sln
src/EntangledHeat.Core/        netstandard2.0, no GTA dependency, unit-tested
  Config.cs                    EntangledHeat.ini (key, IBM toggle, gameplay toggles)
  Log.cs                       EntangledHeat.log (never logs the key)
  HudText.cs                   corner HUD line (status, stars, source, job, bits)
  Atlas/AtlasClient.cs         process → poll → result (async, TLS 1.2), /me key check
  Atlas/HeatRequests.cs        graph-v1 heat graph + labyrinth-v1 search grid bodies
  Atlas/MeasurementParser.cs   result JSON → (bitstring, count), checked on real emu + qpu jobs
  Quantum/ShotDeck.cs          each real shot handed out once
  Quantum/QuantumPool.cs       per-engine slots: background pre-fetch, IBM upgrade, offline cache
  Heat/HeatCollapse.cs         bits → Pursue/Intercept, partners, unbiased N-way choice
  Heat/SearchGrid.cs           16 bits → searched blocks → cordon streets
src/EntangledHeat/             net48 SHVDN3 script (needs the game to run)
  EntangledHeatScript.cs       per-frame HUD, 250 ms logic, wiring
  HeatDirector.cs              police cars ↔ qubits, orders, blips, decoherence
  SearchCordon.cs              search zones + roadblocks when the stars go grey
  Hideouts.cs                  superposed safehouses
src/EntangledHeat.Tests/       35 xunit tests for Core
src/tools/atlas_probe.py       real API calls → docs/samples/; `seed` → seed-cache/
seed-cache/                    real jobs shipped in the release
build-release.ps1              build + test + package release\EntangledHeat-<ver>.zip (checks for key leaks)
```

## Game access decision: **2 Nov**
Ask the friend first (team up / use their PC). If that doesn't work out by **Sun 2 Nov**, buy GTA V that day. Until then, build only the parts that don't need the game.

## Schedule

| Day | Date | Goal |
|---|---|---|
| ✅ | Wed 29 Oct | Atlas key, real API samples (emu + IBM), heat graph, search grid, hideouts, HUD, seed cache, release script, INSTALL.md |
| — | 30 Oct – 2 Nov | No game yet. **You:** post the Discord questions and do the originality check. **Me:** act on the Discord answers, the IBM 5-star seed, the write-up draft. **2 Nov:** game decision; start the download straight away |
| 6 | Mon 3 Nov | Install SHV + SHVDN3 + the release zip. Smoke test: notification, `ATLAS OK` HUD, collapse at 1★, blips, intercepts, decoherence |
| 7 | Tue 4 Nov | Search-grid roadblocks, tune hideout spots and timings, IBM notice at 4★+ |
| 8 | Wed 5 Nov | Clean-install test, 20-min session, 5★ stress test, no-key and no-network checks, fix bugs |
| 9 | Thu 6 Nov | Record footage and edit the video (royalty-free music, no Rockstar logos) |
| 10 | Fri 7 Nov | Write `docs/INSTALL.md`, the write-up and the release package. Upload the download |
| 11 | **Sat 8 Nov** | **Submit through the Airtable form.** Do a final check of the download link in a private window |
| — | Sun 9 Nov | Buffer only. The deadline is 23:59 UK |

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| API latency or IBM queue delays stall the game | Background pre-fetch pool, and the game only reads from the pool |
| Judges can't get the API working | Clear key setup, an in-game status check, a labelled cached fallback |
| A game patch breaks ScriptHookV | Note the tested game and SHV versions in INSTALL.md |
| Scope creep | Ship the Drive Engine heat system first. Labyrinth and hideouts are add-ons |
| Licensing problems in the video | Original or royalty-free audio, no Rockstar logos on the thumbnail |
