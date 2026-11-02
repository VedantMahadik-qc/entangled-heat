# Entangled Heat: install guide

A single-player GTA V mod where the police are a quantum state measured by Moth Atlas. Setup takes about 2 minutes.

## You need
- **GTA V Legacy for PC, Story Mode.** Tested on: *(game build, filled in after testing)*
  - Use **Legacy**, not Enhanced. ScriptHookV only loads on game builds it supports, and the current Enhanced build (1.0.1158.16) isn't supported yet. Owning Enhanced gives you Legacy too: on Epic it's the library entry called just **"Grand Theft Auto V"**; on Steam it's **"Grand Theft Auto V Legacy"**.
- **ScriptHookV** by Alexander Blade: http://www.dev-c.com/gtav/scripthookv/
- **ScriptHookVDotNet 3** (v3.6.0 or newer): https://github.com/scripthookvdotnet/scripthookvdotnet/releases
- **A free Moth Atlas API key:** https://platform.mothquantum.com (API keys page)

## Install
1. **ScriptHookV:** copy `ScriptHookV.dll` and `dinput8.dll` into your GTA V folder, next to `GTA5.exe` (Legacy's exe; Enhanced's is `GTA5_Enhanced.exe`).
2. **ScriptHookVDotNet:** copy its files into the GTA V folder.
3. **Entangled Heat:** copy this zip's `scripts` folder into the GTA V folder. If a `scripts` folder already exists, merge them.
4. Open `scripts\EntangledHeat.ini` and paste your key after `ApiKey=`.
5. Launch GTA V **with BattlEye off** (Rockstar Launcher → Settings → GTA V → uncheck BattlEye) and load Story Mode.

Within a few seconds you'll get a notification. The top-left corner should read **`ATLAS OK`**.

## What to try
| Do this | What happens |
|---|---|
| Get 1–2 stars | The nearest 6 police cars become qubits. Each collapse (every 20 s) gives every car an order: **red** = chasing you, **blue** = racing ahead to block the road. Entangled partners share a **number** on the map and a **purple thread** in the world, and they tend to make the same call |
| Press **F10** | Switch quantum police off (normal GTA police) and back on, to feel the difference |
| Start a story mission | Quantum police pause automatically (missions script their own cops) and come back when it ends |
| Push it to 4–5 stars | The links tighten into perfect Bell pairs, and at 5 stars the whole squad often acts as one. From 4 stars, results come from **real IBM quantum hardware** once the job finishes (the HUD shows `IBM HARDWARE`) |
| Wreck a police car | Its entangled partner **decoheres**: it loses your trail for 25 s (white blip) |
| Break line of sight (stars flash grey) | The **search grid** collapses: red zones show which blocks are being swept, and blue cordon blips are roadblocks sealing them in |
| Reach a purple **superposed safehouse** (2+ stars) | It's in three places until you arrive. One measurement decides whether it's real (your heat is gone) or shows the real one |

The HUD shows every result's source, Atlas job ID and measured bitstring. Every job is also logged in `scripts\EntangledHeat\EntangledHeat.log` and shows up on your Atlas account.

## Offline and errors
- **No key, wrong key, or no internet:** the mod says so in a notification and the HUD, then replays **real cached Atlas results** (marked `CACHED (offline)`). It never invents a quantum result. It ships with real jobs, including IBM hardware runs.
- **No data at all:** the police use the normal game AI.
- **Script errors:** press `F4` for the ScriptHookVDotNet console, and send us `EntangledHeat.log`.

## Settings (`scripts\EntangledHeat.ini`)
| Key | Default | Meaning |
|---|---|---|
| `UseIbm` | `true` | Request real IBM hardware runs at high wanted levels |
| `IbmFromWantedLevel` | `4` | Star level where IBM runs start |
| `EnableHeatSystem` / `EnableSearchGrid` / `EnableSuperposedHideouts` | `true` | Turn each feature on or off |
| `CollapseIntervalSeconds` | `20` | Seconds between police collapses |
| `OfflineFallback` | `true` | Replay cached real results when Atlas can't be reached |
| `PhoneTexts` / `Tips` | `true` | Texts from "Uncle Shor" during chases, and one-time help tips |
| `ToggleKey` (`[Controls]`) | `F10` | Key that switches quantum police on and off |

## Uninstall
Delete `scripts\EntangledHeat.dll`, `EntangledHeat.Core.dll`, `EntangledHeat.ini` and the `scripts\EntangledHeat\` folder. Leave `Newtonsoft.Json.dll` if another mod uses it.

Single-player only. ScriptHookV switches itself off in GTA Online.
