# Judging

Judges: the Moth team plus **one external judge**, who will be named before entries close. **They install and play every mod themselves.**

## Scoring: three criteria, weighted equally

| Criterion | What they say they test | What it means for us |
|---|---|---|
| **1. How well it runs** | "Whether it actually runs" | A clean install, no crashes, no frame freezes while waiting on the API, and clear error messages if the key or network is missing |
| **2. How much it uses Atlas** | "How much it uses Atlas" | Quantum output **drives core gameplay**, more than one engine, real API calls they can verify on the account |
| **3. Originality** | "Whether we've seen it before" | Not a shader/filter reskin and not a copy of a Moth Hack project. A concept that only makes sense because it's quantum |

**Tie-break: the higher Atlas score wins.** That makes criterion 2 effectively the most important one.

## Self-score checklist (fill in before submitting)

### Runs (aim for 5/5)
- [ ] Installs by dropping files into `scripts/` with ScriptHookV + ScriptHookVDotNet. The steps are in `docs/INSTALL.md`
- [ ] All API calls run off the game thread, so the game never hangs
- [ ] Results are pre-fetched and buffered, so gameplay never waits on a job
- [ ] Missing or invalid API key: an on-screen message, not a crash
- [ ] Offline or API down: a graceful fallback, clearly labelled in-game
- [ ] Tested on a clean GTA V install (latest patch), with no other mods
- [ ] The log file (`EntangledHeat.log`) records job IDs and errors

### Atlas usage (aim for 5/5, the tie-breaker)
- [ ] Quantum results change what happens in the game (spawns, AI, routes, outcomes), not just the visuals
- [ ] Uses **2–3 engines**, each with a clear gameplay role
- [ ] At least one engine run on **real IBM hardware** (the Drive Engine supports this) as a headline feature
- [ ] An in-game HUD shows when a quantum result was used ("collapsed by Atlas job #…")
- [ ] The API key comes from the user's own config, so judges' usage shows on their side and ours shows on our account

### Originality (aim for 5/5)
- [ ] Gameplay where the quantum part is the mechanic (superposition, entanglement, measurement) rather than a random number generator
- [ ] Checked the Moth Discord and GitHub for other entries and past Moth Hack projects so we don't overlap
- [ ] A memorable name and a 10-second hook in the video
