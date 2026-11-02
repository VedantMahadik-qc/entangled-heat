# Entangled Heat: design

GTA V single-player mod. While you're wanted, the police are run by a quantum graph state measured through Moth Atlas.

## Core loop
1. You get a wanted level. The mod pre-fetches a **heat graph** job from Atlas (`graph-v1`) for that star level.
2. Every collapse (when the wanted level changes, then every ~20 s), the 6 nearest police cars become qubits 0–5. One real measured shot (a bitstring) decides each car's order:
   - `1` → **Pursue**: chase you directly (red blip)
   - `0` → **Intercept**: race ahead of you and set a roadblock (blue blip)
3. **Entangled partners:** cars are three entangled pairs (0-1, 2-3, 4-5). **More stars = stronger entanglement**: at 1 star partners agree ~73% of the time (a sloppy, disorganised response), at 4 stars they are perfect Bell pairs (100%), and at 5 stars the pairs are also chained, so **the whole squad acts as one** in about half the collapses. Across pairs, cars split roughly 50/50 between chasing and blocking.
4. **Decoherence:** destroy or disable a car and its entangled partner **loses your trail** (grey blip) for a while. Picking the right car to take out is the skill.
5. **Superposed safehouses (2+ stars):** three ghost safehouse blips appear. The first one you reach triggers a measurement. If the safehouse collapses *there*, your heat is gone. If not, the real one is revealed with a route.
6. **Search grid:** when you break line of sight (stars flash grey), one `labyrinth-v1` shot covers the 4×4 grid of ~90 m blocks around your last known position (`GET_PLAYER_WANTED_CENTRE_POSITION`). Bit 1 means **the police sweep that block** (red zone blip). Every street is a ZZ=+1 corridor, so searched blocks come in clusters. Off-grid counts as unsearched, so a **roadblock goes on every street around a searched cluster's edge**, capped to the 4 nearest you (blue blips, police car + 2 cops). All 1s means a wide perimeter, all 0s means they lost the area. We use roadblocks on the edge of the searched area, not on every street where neighbouring blocks disagree, because the top outcome (13.5%) is "all blocks agree", which would give no roadblocks at all. Requested with `top_n: -1`: one job returns all ~1,950 layouts across 4,096 shots, so the deck is the full distribution (about 10 of 16 blocks searched on average).
7. **Visible entanglement:** partner cars share a blip number (pair 1/2/3), and a purple thread links them in the world. A cone over each car shows its order (red chase, blue intercept, white decohered). Each collapse plays a short sound and screen pulse. When a partner is wrecked, the thread vanishes: that's decoherence you can see.
8. **Personality and teaching:** phone texts from "Uncle Shor" (from Moth's own bounty page) at key moments: first heat, 5 stars, IBM queued and arrived, decoherence, search grid, safehouse, escape. They're rate-limited, and several go out once per session. One-time help tips explain each mechanic the first time it happens. Both are flavour only and never affect gameplay.
9. **Toggle (F10):** switches quantum police off (vanilla GTA) and back on, so judges and the video can compare.
10. **HUD:** the top-left line `ATLAS OK | 4 STARS | IBM HARDWARE | job 1a2b3c4d | |010110>` shows the key check (`GET /api/v1/me`) and the source, job and bits of the result in use.

## Measured on the Atlas simulator (29 Oct 2026)
| Wanted | Links (chain order, ZZ=+1) | Partners agree | Pairs agree | Whole squad same |
|---|---|---|---|---|
| 1 | pairs at 0.1 | ~73% | ~50% | rare |
| 3 | pairs at 0.5 | ~92% | ~50% | rare |
| 4 | pairs at 1.0 | 100% | ~50% | rare |
| 5 | pairs 1.0 + cross 0.25 | 100% | ~70% | ~50% |

**On IBM hardware** (`ibm_marrakesh`, 4 stars, job `159d6d50`, 20 s including the queue): partners agreed 95%, 96% and 99% of the time, against 100% on the simulator. That's real hardware noise, and it makes a good point for the video. At 5 stars (job `e0cd1db8`, also `ibm_marrakesh`), partners agreed 96%, 94% and 99%, after about 17 minutes in the queue. Queue times vary a lot, so the simulator always plays first and the IBM deck takes over when it arrives.

**API limitation:** `graph-v1` returns only the top 20 bitstrings, with no `top_n` option. At low stars the outcomes are spread widely (1 star: the top 20 cover ~470 of 1,024 shots), so rare orders are missing from the deck. Every shot handed out is still a real measurement. Raised on Discord.

What didn't work: `ZZ=-1` "rival" links and `Z` bias ops cancelled the pair links when combined (all partners ~50–70%, bias had no effect). Op order matters; the chain order above is the one that holds.

## Quantum → game mapping

| Quantum concept | In game |
|---|---|
| Qubit | One police car |
| Superposition (`X=1` prep) | A car's order is undecided until the collapse |
| Measurement (one shot) | The collapse: every car gets its order at once |
| `ZZ=+1` correlation | Partner cars make the same call |
| Wanted level → link strength | More stars = a tighter, more coordinated squad |
| Decoherence | Losing a car breaks its partner's link to you |
| Superposed location | Safehouse exists in 3 places until observed |
| Labyrinth corridor correlations | Searched blocks cluster together, and roadblocks seal each cluster's edge |
| IBM hardware (`mode: qpu`) | From 4 stars, results measured on a real IBM chip, flagged in the HUD |

## Honest quantum
- **Every collapse gets its own new Atlas job.** One finished job waits per star level; the collapse measures one shot from it and the next job starts straight away (real emu jobs take ~4 s for the heat graph and ~7 s for the search grid, against a 20 s collapse interval). The notification and HUD say **new job …**. If a collapse comes before the next job lands (e.g. stars change twice in a row), it takes another unused shot from the previous job and says **earlier job …**.
- Every order comes from a **real measured shot** returned by Atlas. Each shot is handed out once, never reused or invented.
- Real results are cached on disk. Offline, the mod replays cached real shots and shows **OFFLINE (cached Atlas job …)** in the HUD.
- The release ships a **seed cache** of real jobs (`seed-cache/`, built by `atlas_probe.py seed`, with IBM runs preferred), so a judge's first run has quantum data before the API answers.
- With no data at all, the mod leaves vanilla police AI alone and says so. It never fakes a quantum result.

## Stability rules
- No network or JSON work on the game thread. All Atlas calls run as background tasks; the tick only reads ready shots.
- Pre-fetch the current and next star level, one ready job each. `FreshJobPerCollapse=false` in the ini goes back to one job serving many collapses (refill at 75% used) if the rate limit ever bites.
- IBM jobs: at most one in flight per slot; a hardware job that is ready wins the next collapse. A failed IBM job (queue timeout, QPU access gated) backs off 5 minutes while the simulator carries on.
- Failures back off for 30 s, get logged to `scripts/EntangledHeat/EntangledHeat.log` and never crash the game.
