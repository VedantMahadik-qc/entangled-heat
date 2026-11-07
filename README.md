# Entangled Heat

A GTA V single-player mod where **the police are a quantum state** and **you escape through quantum portals**, all measured live by [Moth Atlas](https://platform.mothquantum.com). Built for Moth's [GTA V Quantum Mod Bounty](https://mothquantum.com/bounties/gtavmod).

Three Atlas engines decide what happens in the game:

| Engine | What it decides |
|---|---|
| **Quantum Drive Engine** (`graph-v1`) | Each police car is a qubit, and entangled partners make the same call: chase you or race ahead to block the road. More stars mean stronger entanglement. Also decides the **entangled orb pairs** and which **twin exit** a portal uses |
| **Quantum Labyrinth** (`labyrinth-v1`) | When you break line of sight, which city blocks the police sweep and where the roadblocks go |
| **Quantum Blur** (`blur-v1`) | Where a portal can send you: you are one bright pixel, and the blurred image is your landing map, with interference echoes that a normal blur can't make |

## In the game

**The heat.** Get a wanted level and the 6 nearest police cars become qubits in three entangled pairs. Every collapse (every 20 s), one measured bitstring gives every car its order. Partners share a number on the map and a purple thread in the world. Wreck one and its partner **decoheres** and loses your trail. From 4 stars the results can come from real IBM hardware.

**The escape.** Drive through glowing **quantum orbs** for portal charges. Orbs come in entangled pairs: grab one and its partner across the map turns gold (+2) or plain (+1) at that instant. Press **L3** (controller) or **T** and a portal opens on the road ahead, with **four labelled exits** further on: two entangled twin pairs. Drive in: Quantum Blur picks the pair, then an entangled `graph-v1` pair picks the twin. You come out still moving, and the police search where you went in. Now and then the noisy chip breaks the entanglement and a **quantum copy of your car** comes out of the other twin.

Every result shows its Atlas job ID and measured bits on screen and in the log. Nothing is ever faked: offline, the mod replays real cached Atlas jobs and says so.

## Install

Grab the release zip and follow [docs/INSTALL.md](docs/INSTALL.md): ScriptHookV + ScriptHookVDotNet 3, copy `scripts\` into GTA V, paste a free Atlas API key into `scripts\EntangledHeat.ini`. GTA V Legacy (PC), Story Mode only.

## Repository

| Path | What's in it |
|---|---|
| `src/` | The police mod (C#, ScriptHookVDotNet 3): `EntangledHeat.Core` (Atlas client, shot pools, game-free logic), `EntangledHeat` (the script), tests, and `tools/atlas_probe.py` |
| `getaway/` | Portals and entangled orbs (also runs on its own as **Quantum Getaway**): see [getaway/README.md](getaway/README.md) |
| `stash/` | **Schrödinger's Stash**, a small showcase mod: entangled cash stashes on `graph-v1` |
| `docs/` | Install guide, design notes, the Atlas OpenAPI spec and real job samples |
| `seed-cache/` | Real Atlas jobs shipped for the first run and offline play |
| `build-release.ps1` | Builds, runs both test suites and packs `release\EntangledHeat-<version>.zip` (refuses to ship an API key) |
| [BOUNTY.md](BOUNTY.md) | Bounty rules, judging and planning notes |

Not affiliated with Rockstar Games or Take-Two. Uses Newtonsoft.Json (MIT).
