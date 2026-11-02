# Schrödinger's Stash

A single-player GTA V mod where cash stashes are qubits, measured by **Moth Atlas**.

Press **F9** and six stashes appear on your map as purple ghost blips, in three **entangled pairs**. Partners share a number and sit on opposite sides of you. Every stash is *cash or empty in superposition*, and partners are anti-correlated: one is full, one is empty, and nobody knows which until you look.

Drive up to a stash and Atlas measures the pair:

- **Cash!** You collect $10,000, and its partner across town instantly collapses to *empty*. You never went near it.
- **Empty**, and the alarm trips (2 stars). But that means its partner is *guaranteed full*. The route is set. Go.
- On a noisy IBM chip, roughly **1 pair in 70 breaks the rule**: both full (**jackpot**) or both empty (**bust**). That's real hardware noise, and it turns into gameplay.

## How it uses Atlas

- Engine: **Quantum Drive Engine** (`graph-v1`). Six qubits, each in superposition (Bloch X=1), with a **ZZ = −1** relationship on each pair (0-1, 2-3, 4-5). Partners must disagree.
- **Every pair you open is measured by its own new Atlas job.** One job is pre-fetched in the background, so the game never waits. The notification and HUD show the job ID, the source and the pair's measured bits, e.g. `NOISY CHIP | new job 48b08755 | |10>`.
- Default mode `fake`: a noisy calibration snapshot of **IBM Marrakesh** (about 6 s per job). Measured on real jobs: partners came out opposite 100% of the time on the perfect simulator, and 98–99% on fake Marrakesh and Fez. Set `Mode=emu` for perfect entanglement, or `Mode=qpu` for real IBM hardware when it's available.
- Offline or no key: it replays a real cached Atlas job, clearly labelled `CACHED (offline)`. It never fakes a result. With no data at all it says so and resolves the pair classically.

## Install (about 2 minutes)

You need:
- **GTA V Legacy** for PC, Story Mode. ScriptHookV doesn't support the current Enhanced build yet. If you own Enhanced, you also own Legacy: on Epic it's the library entry called just "Grand Theft Auto V".
- [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) and [ScriptHookVDotNet 3 nightly](https://github.com/scripthookvdotnet/scripthookvdotnet-nightly/releases).
- A free Moth Atlas API key from https://platform.mothquantum.com.

Steps:
1. Copy ScriptHookV's `bin\` files and ScriptHookVDotNet's files into your GTA V folder (next to `GTA5.exe`).
2. Copy this zip's `scripts` folder into the GTA V folder. If one already exists, merge them.
3. Open `scripts\SchrodingersStash.ini` and paste your key after `ApiKey=`.
4. Launch Story Mode. The top-left corner reads **`STASH | ATLAS OK`**. Press **F9** to start a run, and F9 again to abandon it.

Settings (`scripts\SchrodingersStash.ini`): `Mode`, `Backend`, `StartKey`, `CashPerStash`, `AlarmStars`, `RunMinutes`, `PhoneTexts`. The log is in `scripts\SchrodingersStash\SchrodingersStash.log` (job IDs, never your key).

## Why it's quantum, not just random

A random-number version would pick each stash independently. Here the **correlation is the mechanic**. Opening one stash tells you about a place you've never been, and the only way partners disagree is through the ZZ = −1 link in the Atlas graph state. When the link fails, it's because a real chip's noise broke it, and you can see that happen in the game.
