# Quantum Getaway

A GTA V single-player mod about escaping through quantum portals.

- **Entangled power-ups:** glowing **quantum orbs** sit on the streets around you in **pairs** (purple dots on the map; partners share a number). One of each pair is **gold** (+2 portal charges), the other plain (+1), and nothing decides which until you grab one. Grab it and its partner across the map turns gold or plain at that instant. You can hold 3 charges.
- **Open a portal:** press **L3** (left stick click) on a controller or **T** on the keyboard. A swirling portal opens on the road ahead.
- **The exit is in superposition:** four **exit portals** open up the road (and on the map), labelled with their odds: two **entangled twin pairs**, **1A/1B** and **2A/2B**, mirrored left and right of your path, up to ~250 m away.
- **Drive through:** the exit is measured, and you come out of one of the four with your speed intact. If you're wanted, the police keep searching where you went in.
- **See the quantum state:** while a portal is open, the actual image Quantum Blur returned is drawn in the corner (you are the green cell, ahead is up). After you go through, the measured exit blinks in it.

## How it uses Atlas

Two engines, and **every portal uses both**: **Quantum Blur** measures which pair of exits you go to, and the **Quantum Drive Engine (`graph-v1`)** measures which twin of the pair you come out of. `graph-v1` also decides every orb pair.

### Every portal, step by step

1. **Open:** the exit map comes from `blur-v1` (below). Two twin pairs are drawn from it, weighted by its probabilities, so every portal shows a different set. Each pair is a spot left of your path (A) and its mirror on the right (B). The odds shown are the pairs' blur probabilities relative to each other, split between the twins.
2. **Drive in, measurement 1 (Quantum Blur):** pair 1 or pair 2 is drawn with those odds.
3. **Measurement 2 (graph-v1):** one measured shot from a new `graph-v1` job settles an entangled pair of qubits linked so they always disagree: `01` = A, `10` = B. The other twin vanishes.
4. **Noise:** on the noisy IBM chip snapshot the link breaks 1–2% of the time. `11` = **both**: you come out of one twin and a **quantum copy of your car** comes out of the other and drives off (the police search around it). `00` = **neither**: you come out in the middle, between the twins.

### Portals: Quantum Blur (`blur-v1`)

Quantum Blur builds the landing map. The mod uploads a 32×32 image that is dark everywhere except one bright pixel: you, at the back-middle of a grid laid over the street ahead (8 m cells). Quantum Blur encodes the image as a 10-qubit state (each qubit is one bit of a pixel's x or y position) and rotates every qubit. QuantumBlur writes each pixel's measurement probability back as its brightness, so the returned image *is* your probability of landing in each cell.

A classical blur would only smear you into a soft blob. The qubit rotations instead put weight on cells that differ from yours in single bits of their position, so you get **interference echoes 1, 3, 7 and 15 cells away**, and at the portal's strength (0.8, reach 0.7) a fractal grid of exits. That grid is what the ghost rings show.

With 8 m cells the exit is never the entrance, 69% of exits are 16+ cells (~130 m+) away, and the mean is 20 cells (~160 m). *Measured from real `blur-v1` jobs on 7 Nov 2026 (simulator); raw results are in `docs/samples/`.* (The Core also has a short-range "Hop" setting, 0.4/0.5, that bounces 15% of the time like real tunnelling; it's tested but not used in the game.)

**The measurement:** with `Mode=sim` (the default) Atlas returns the exact probabilities and the mod draws one cell from them as you drive into the portal. With `Mode=fake` the image is estimated by Atlas from **4,096 measured shots** on a noisy snapshot of IBM's `ibm_marrakesh` chip, so the draw picks among real measurement outcomes. `Mode=qpu` asks for the real chip.

### Orb pairs: Quantum Drive Engine (`graph-v1`)

Orbs spawn in pairs on opposite sides of you. Each `graph-v1` job prepares 6 qubits as three pairs: every qubit in superposition (X=1, gold or plain 50/50) with a ZZ=-1 link inside each pair, so partners always disagree. Grabbing an orb takes one measured shot from a new job: its bit decides your orb, and its partner's bit, measured in the same shot, decides the orb across the map. By default it runs on `fake` mode, a noisy snapshot of IBM's `ibm_marrakesh` chip, where partners disagree 98–99% of the time. That noise is visible in the game: now and then a pair comes out **both gold** (or both plain), and the notification says it was the chip's noise. `OrbMode=emu` gives the perfect simulator.

**Real IBM hardware:** alongside that, the mod sends the same circuit to a real IBM quantum computer (`mode: qpu`, least busy chip). When the job finishes, its ~1,000 real hardware measurements take over: every orb pair and every twin exit is then settled by its own real hardware shot, and the notification says "IBM quantum hardware". The next hardware job starts when they run low. Measured on 7 Nov 2026: `ibm_pittsburgh`, 24–64 s per job, an entangled pair opposite 97.3% of the time. If IBM is unavailable or queued, the game carries on with `OrbMode` and says so (`OrbHardware=false` turns this off).

### Honest quantum
- **Every portal and every orb pair is measured from its own new Atlas job.** One finished job per engine waits ready; using it starts the next at once (jobs take ~4–5 s). The notification and HUD show the job ID and say **new job** or, if you use two portals before the next job lands, **earlier job**.
- The ghost exits you see are the job you're measuring: the preview and the exit use the same result.
- Offline, portals use cached real Atlas results and labels them **CACHED (offline)**. The release ships real jobs for both engines so the first portals and orbs work before the API answers. With no data at all, portals are switched off and say so; it never fakes a result.
- Every job, error, orb measurement (bits, job ID) and landing (cell, probability, distance, job ID) is written to `scripts\QuantumGetaway\QuantumGetaway.log`, and every job shows on your own Atlas account.

## Install

1. GTA V **Legacy** (PC, story mode) with **ScriptHookV** and **ScriptHookVDotNet 3** installed.
2. Copy everything in `scripts\` from the zip into `<GTA V>\scripts\`.
3. Open `scripts\QuantumGetaway.ini` and paste your free Atlas API key from https://platform.mothquantum.com after `ApiKey=`.
4. Start story mode. A notification says **Atlas connected**. You start with one charge: press **L3** or **T**.

Settings (buttons, charges, orb count, cell size, `Mode=sim|fake|qpu` for portals, `OrbMode=emu|fake|qpu` for orbs) are in the same ini.

## Build

`powershell -ExecutionPolicy Bypass -File build-getaway.ps1` builds, runs the tests and writes `release\QuantumGetaway-<version>.zip`. The tests include the PNG decoder and wavefunction sampling against real `blur-v1` outputs. `QG_LIVE=1` also runs live tests that make real `blur-v1` and `graph-v1` jobs. `tools/blur_probe.py` runs single probes; `tools/getaway_seed.py` rebuilds the seed cache.

Uses Newtonsoft.Json (MIT). Not affiliated with Rockstar Games.
