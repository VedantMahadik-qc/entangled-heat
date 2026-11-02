# How to win

## What the scoring rewards

1. **Atlas usage is the tie-breaker**, so it counts most. The quantum output has to *be* the gameplay. The brief explicitly rules out "just a filter or a hidden feature", which kills most shader-only ideas.
2. **They install and play it themselves.** A buggy entry loses a full third of the score. A small, polished mod beats a big broken one.
3. **"Whether we've seen it before."** Avoid generic "quantum random" spawners. The mechanic should rely on quantum ideas (superposition, entanglement, measurement collapse), not just "random number, but quantum".
4. Moth's whole page is themed around GTA flavour (wanted levels, texts from "Moth", a missed call from "Uncle Shor"). They want **fun and personality**, not a science demo.

## Ideas, ranked

### ⭐ 1. "Entangled Heat": a quantum wanted system (Recommended)
Police response is driven by quantum graph states.
- **Drive Engine (core):** each wanted star adds police units as nodes in an entangled graph state. When you're spotted, the graph is **measured**: the bitstring decides which units converge, which set roadblocks and which lose you. Units are *entangled*, so their behaviour is correlated: lose one and its partner also goes dark. Higher stars run on **real IBM hardware**, which makes a headline: "your wanted level was decided on a quantum computer".
- **Labyrinth (second engine):** each time you break line of sight, a quantum-walk maze picks the police **search grid** and roadblock pattern across nearby streets.
- **Superposition hideouts:** when you're wanted, three possible safehouses appear as faded "ghost" blips. Only on arrival does the location **collapse** (one quantum measurement), so the escape plan has to cover all three.
- **HUD:** "Heat collapsed by Atlas · job #…" so judges can see the quantum part working.
- **Why it wins:** core gameplay, 2–3 engines, verifiable API use, a fresh mechanic, and it fits Moth's wanted-level theme exactly.

### 2. "Schrödinger's Getaway": quantum heist escape routes
Labyrinth generates a checkpoint route through Los Santos. The getaway car exists in several superposed spawn points until you "observe" it by getting close. Teleblur morphs the minimap overlay between candidate routes. Strong Atlas use and fun, but closer to standard race mods.

### 3. "Uncertain Garage": quantum vehicle physics
Quantum Blur warps a car's handling data (mass, grip, torque curves) each time you enter it. Teleblur morphs the engine sound between two cars. Fun to watch, but it's mostly data tweaks, so less of a core-gameplay hook.

### 4. "Quantum Labyrinth Arena"
A Labyrinth maze built from props on the airport or the Maze Bank arena, regenerated every round. It's easy to show on video, but maze generation is a known Moth demo, so it scores lower on originality.

### ❌ Avoid
- Entanglement Shader as the only feature: it reads as a filter
- Anything touching GTA Online, or using leaked GTA VI material
- Recognisable licensed music in the video

## The edge: polish for the judges
- **A one-minute install** with a clear `docs/INSTALL.md`, an example config and an on-screen status like "Atlas connected ✓"
- **Pre-fetch buffer + offline fallback**, so the mod works smoothly even if the API is slow on judging day
- **Video:** a 10-second hook, then gameplay, then a split screen of the game with the Atlas job log, then "ran on IBM hardware" proof. Aim for 1–2 minutes with royalty-free music
- **Write-up:** state exactly which engines, how many calls per minute of play, and which game decisions each result controls
- **Submit a day early** (8 Nov) and use Discord to ask the open API questions now
