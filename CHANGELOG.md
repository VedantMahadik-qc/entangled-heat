# Changelog

## Entangled Heat 0.2.0 (8 Nov 2026)

The escape half: quantum portals and entangled power-ups (developed as `getaway/`, also released on its own as Quantum Getaway). One zip, three Atlas engines.

### Added
- **Quantum portals** (`blur-v1` + `graph-v1`). Press L3 (controller) or T: a portal opens on the road ahead with four labelled exits, two entangled twin pairs drawn from the Quantum Blur image of you as one bright pixel. Driving in, Quantum Blur picks the pair and a measured entangled pair of qubits picks the twin. Chip noise can send a quantum copy of your car out of the other twin, or drop you between them.
- **Entangled orbs** (`graph-v1`). Power-ups spawn in pairs on opposite sides of you; grabbing one decides its partner across the map (gold +2 charges, plain +1).
- **Real IBM hardware for orbs and twin exits.** A hardware job runs alongside the noisy-chip snapshot; its real measurements take over when it finishes (checked on `ibm_pittsburgh`, 24–64 s per job). The HUD shows `ORBS: IBM HARDWARE (chip)` while they're in use.
- **Quantum Blur panel**: the real `blur-v1` image is drawn on screen while a portal is open, with the four exits marked and the measured exit blinking after.
- Controller support (L3), shared API key (`QuantumGetaway.ini` falls back to `EntangledHeat.ini`), offline seed cache for both new engines.

### Changed
- Notifications name the exact measurement used, e.g. `job 12ae84f9, shot 17/1016`: every shot is handed out once.
- `build-release.ps1` builds and tests both projects and packs one zip; it still refuses to ship an API key.
- The repository README describes the mod; the bounty notes moved to `BOUNTY.md`. INSTALL.md covers portals and lists the tested game build.

## Entangled Heat 0.1.0 (2 Nov 2026)

- Quantum police (`graph-v1`): the 6 nearest police cars are qubits in entangled pairs; every collapse orders each car to chase or intercept. Stronger entanglement at higher wanted levels, decoherence when a partner is wrecked, IBM hardware from 4 stars.
- Quantum search grid (`labyrinth-v1`): when you break line of sight, a 16-qubit grid of city blocks decides the police sweep and the roadblocks.
- Superposed safehouses, Uncle Shor texts, F10 to compare with vanilla police, offline replay of real cached jobs.
- Schrödinger's Stash showcase mod (`stash/`).
