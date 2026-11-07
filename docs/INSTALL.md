# Entangled Heat: install guide

A single-player GTA V mod where the police are a quantum state measured by Moth Atlas, and you escape them through quantum portals. Three Atlas engines: `graph-v1`, `labyrinth-v1` and `blur-v1`. Setup takes about 2 minutes.

## You need
- **GTA V Legacy for PC, Story Mode.** Tested on: game build **1.0.3889.0** (Legacy, Epic), ScriptHookV **v3889.0**, ScriptHookVDotNet **3.7.0**.
  - Use **Legacy**, not Enhanced. ScriptHookV only loads on game builds it supports, and the current Enhanced build (1.0.1158.16) isn't supported yet. Owning Enhanced gives you Legacy too: on Epic it's the library entry called just **"Grand Theft Auto V"**; on Steam it's **"Grand Theft Auto V Legacy"**.
- **ScriptHookV** by Alexander Blade: http://www.dev-c.com/gtav/scripthookv/
- **ScriptHookVDotNet 3** (v3.6.0 or newer): https://github.com/scripthookvdotnet/scripthookvdotnet/releases
- **A free Moth Atlas API key:** https://platform.mothquantum.com (API keys page)

## Install
1. **ScriptHookV:** copy `ScriptHookV.dll` and `dinput8.dll` into your GTA V folder, next to `GTA5.exe` (Legacy's exe; Enhanced's is `GTA5_Enhanced.exe`).
2. **ScriptHookVDotNet:** copy its files into the GTA V folder.
3. **Entangled Heat:** copy this zip's `scripts` folder into the GTA V folder. If a `scripts` folder already exists, merge them.
4. Open `scripts\EntangledHeat.ini` and paste your key after `ApiKey=`. That one key is used by the whole mod (leave `QuantumGetaway.ini`'s key empty).
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

### Escape: quantum portals
Works with a **controller** (L3 = left stick click) or the keyboard (**T**).

| Do this | What happens |
|---|---|
| Drive through a glowing purple **quantum orb** (purple numbered dots on the map) | Orbs come in **entangled pairs** (same number). Grabbing one measures the pair with a `graph-v1` job: one is **gold** (+2 portal charges), the other plain (+1), and its partner's dot across the map turns gold or white at that moment. You start with 1 charge and hold up to 3 |
| Press **L3** / **T** | A portal opens on the road ahead with **four exit portals** further up the road, labelled with their odds: two entangled twin pairs, **1A/1B** and **2A/2B**. The real Quantum Blur (`blur-v1`) image they come from is shown on the right |
| Drive into the portal | **Quantum Blur** picks pair 1 or 2, then an entangled `graph-v1` pair picks **A or B**. You come out there, still moving. If you're wanted, the police keep searching where you went in, which is where the search grid seals the streets |
| Keep using portals | Now and then the noisy chip breaks the entanglement: a **quantum copy of your car** comes out of the other twin, or you land between them. The notification says so |

The HUD shows every result's source, Atlas job ID and measured bitstring. Every job is also logged in `scripts\EntangledHeat\EntangledHeat.log` and shows up on your Atlas account.

## Offline and errors
- **No key, wrong key, or no internet:** the mod says so in a notification and the HUD, then replays **real cached Atlas results** (marked `CACHED (offline)`). It never invents a quantum result. It ships with real jobs, including IBM hardware runs.
- **No data at all:** the police use the normal game AI.
- **Script errors:** open the ScriptHookVDotNet console (its key is `ConsoleKeyBinding` in `ScriptHookVDotNet.ini`) and send us `EntangledHeat.log` and `QuantumGetaway\QuantumGetaway.log`.

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

## Settings (`scripts\QuantumGetaway.ini`, the portals)
| Key | Default | Meaning |
|---|---|---|
| `ApiKey` | empty | Leave empty to use `EntangledHeat.ini`'s key |
| `Mode` | `sim` | Quantum Blur: `sim` exact simulator, `fake` noisy IBM chip snapshot (estimated from 4,096 shots), `qpu` real IBM hardware |
| `OrbMode` | `fake` | `graph-v1` for orbs and twin exits: `emu` perfect simulator, `fake` noisy IBM chip snapshot, `qpu` real hardware |
| `TunnelKey` / `ControllerL3` | `T` / `true` | Keyboard key and controller button that open a portal |
| `StartCharges` / `MaxCharges` | `1` / `3` | Portal charges |
| `Orbs` / `OrbsWanted` | `4` / `8` | Orbs kept around you, and while wanted |
| `PortalSeconds` | `6` | An unused portal closes and refunds its charge |
| `CellSize` | `8` | Metres per Quantum Blur grid cell (exits reach ~250 m) |

## Uninstall
Delete `scripts\EntangledHeat.dll`, `EntangledHeat.Core.dll`, `EntangledHeat.ini`, `QuantumGetaway.dll`, `QuantumGetaway.Core.dll`, `QuantumGetaway.ini` and the `scripts\EntangledHeat\` and `scripts\QuantumGetaway\` folders. Leave `Newtonsoft.Json.dll` if another mod uses it.

Single-player only. ScriptHookV switches itself off in GTA Online.
