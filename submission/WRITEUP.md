# Submission texts

Form: https://airtable.com/appgKPB3bLcYRkoxF/pag7N57cFSLvrxUJy/form · deadline **9 Nov 2026, 23:59 UK**.
Each entry needs a video, a public download link and "a few lines on how it uses Atlas". The short texts below are ready to paste.

---

## Entry 1: Entangled Heat (main entry)

**Download:** `release/EntangledHeat-0.2.0.zip` (GitHub release) · **Code:** https://github.com/VedantMahadik-qc/entangled-heat

### How it uses Atlas (paste this)

> Entangled Heat makes GTA V's police a quantum state and gives you quantum portals to escape them. Three Atlas engines decide the gameplay, live, from the player's own API key:
>
> **Quantum Drive Engine (graph-v1):** the 6 nearest police cars are qubits in three entangled pairs. Every collapse, one measured bitstring orders each car to chase you or block the road ahead; partners agree ~73% of the time at 1 star and 100% at 4 stars. Wreck a car and its partner decoheres and loses you. From 4 stars, results come from real IBM hardware. graph-v1 also decides the entangled power-up orbs (grab one and its partner across the map turns gold or plain) and which twin exit a portal uses, on IBM hardware when available.
>
> **Quantum Labyrinth (labyrinth-v1):** when you break line of sight, a 16-qubit grid of city blocks collapses into the area the police sweep, and roadblocks seal its edges.
>
> **Quantum Blur (blur-v1):** opening a portal blurs you, one bright pixel on a 32x32 grid over the road. The image Atlas returns is your landing map, with interference echoes no normal blur makes. Driving in, Quantum Blur picks the exit pair and graph-v1 picks the twin.
>
> Every result shows its job ID and measured bits on screen; nothing is faked, and offline play replays real cached jobs, labelled.

### Honest notes (if judges ask)
- Jobs are pre-fetched so the game never waits; each collapse, orb and portal uses a measurement nobody has used before, and the HUD says "new job" or "earlier job".
- Quantum Blur in its default `sim` mode is an exact simulation: Atlas returns the probabilities and the game draws the exit from them. `Mode=fake` estimates the image from 4,096 shots on a noisy IBM chip snapshot.
- The anti-correlated pairs could be imitated with a coin; what is quantum is where the results come from (measured entangled states, including real hardware noise, visible as the rare "both gold" orb or quantum copy of your car).
- IBM hardware availability is up to Moth's plan: on 2 Nov it was paused, on 7 Nov it worked (`ibm_pittsburgh`, 64 s, an entangled pair opposite 97.3% of the time). When it's unavailable the mod says so and carries on with the simulator/snapshot.
- An 8-minute test chase made 14 Atlas jobs (police + search grid); portals and orbs add about one job each.

---

## Entry 2 (optional): Quantum Getaway

The portal half on its own, as a lighter mod. Only enter it if there's time for its own video; it shares the portals with Entangled Heat, which may count against its originality.

**Download:** `getaway/release/QuantumGetaway-0.1.0.zip`

### How it uses Atlas (paste this)

> Quantum Getaway is a power-up mod about escaping through quantum portals. Two Atlas engines decide where you go:
>
> **Quantum Blur (blur-v1):** opening a portal (L3 / T) uploads a 32x32 image with one bright pixel, you, and Quantum Blur rotates its 10-qubit state. The returned image is your landing map: echoes 3, 7 and 15 cells away and a fractal grid of exits up to ~250 m, which a normal blur can't make. Four labelled exit portals are drawn from it, and the real image is shown on screen.
>
> **Quantum Drive Engine (graph-v1):** the exits are two entangled twin pairs. Driving in, Quantum Blur picks the pair and one measured entangled pair of qubits picks the twin; chip noise sometimes sends out a quantum copy of your car. The power-up orbs are entangled pairs too: grabbing one decides its partner across the map. Orbs and twins run on real IBM hardware whenever a job has finished.
>
> Every portal and orb shows its job IDs and measured bits; offline play replays real cached jobs, labelled.
