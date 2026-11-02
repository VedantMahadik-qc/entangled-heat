# Atlas engines and API

Source: the public spec at https://api.mothquantum.com/openapi.json (saved to `docs/moth-openapi.json`) and https://api.mothquantum.com/docs.

## The 5 bounty engines and their API IDs

| Bounty name | Engine ID | Input → output | Hardware |
|---|---|---|---|
| Quantum Blur | `blur-v1` | image (or 3D/audio via other blur variants) → warped image | simulator |
| Quantum Teleblur | `telablur-v1` (sic) | `image1` + `image2` (+ `mask`) → morphed image | sim / noisy emu / qpu |
| Entanglement Shader | `entanglement-shader-v1` | JSON params → ZIP of shaders (HLSL/GLSL/OSL…) + LUTs | none at render time |
| Quantum Labyrinth | `labyrinth-v1` | grid + candidate corridors → quantum-sampled maze "level data" | `emu` or IBM `qpu` |
| Quantum Drive Engine | `graph-v1` ("Quantum Graph Engine") **or** `qdrive-api-v1` (both count, confirmed on Discord) | graph + Bloch/ZZ targets → tomography + sampled bitstrings | `emu`, `fake`, IBM `qpu` |

✅ **Answered on Discord (30 Oct 2026, Moth team):** "Both qdrive-api-v1 and graph-v1 are the same class of engines based on Quantum graph. QDrive is an improvement on top of quantum graph from our research team." So `graph-v1` counts as the Drive Engine. `qdrive-api-v1` (QASM3 ansatz circuits + ordered `targets`, `machine` default `aer`, optional `sample`/`tomography`) is the newer research version of the same idea.

Many more engines exist (`coin-toss-v1`, `comet-qrng-v1`, `retrocausal-echo-v1`, `otoc-echo-v1`, `qrc-*`, `tamagotchi-v1`, …). Only the five above are listed for the bounty.

## Wire contract
- Base `https://api.mothquantum.com`, header `Authorization: Bearer <key>`
- `POST /api/v1/engines/<id>/process` with body `{"params": {...}, "input_files": {slot: asset_id}}` → `job_id`
- `GET /api/v1/jobs/<id>/status` until the status is `completed` (or `failed`/`cancelled`)
- `GET /api/v1/jobs/<id>/result` returns `outputs[]` (files, fetched via presigned URL with no bearer header) or an inline `result`
- File inputs: `POST /api/v1/assets` {filename, content_type, size_bytes} → PUT bytes to `upload.url` → `POST /assets/<id>/complete`
- `GET /api/v1/me` checks the key. `GET /api/v1/jobs` lists your jobs (this is what Moth can check)

## graph-v1: the core of Entangled Heat
```json
{"params": {"num_qubits": 6, "coupling_map": [[0,1],[1,2]],
  "operations": [{"type":"bloch","qubit":0,"paulis":{"X":1.0}},
                 {"type":"relationship","qubits":[0,1],"paulis":{"ZZ":1.0}}],
  "shots": 1024, "mode": "emu"}}
```
- 2–20 qubits. `ZZ:+1` means the pair agrees and `ZZ:-1` means it disagrees, which is exactly the "entangled partners" mechanic
- Output: `tomography` (exact Bloch vectors and edge correlations), `measurements` (top 20 bitstrings, `dominant_bitstring`, `edge_agreement_score`)
- Bitstring index i = qubit i (qubit 0 is leftmost)
- `mode: "qpu"` uses Moth's shared IBM credentials (no IBM account needed). The queue can take minutes to hours

## labyrinth-v1: the police search grid
```json
{"params": {"level_data": {"grid_size":{"rows":4,"cols":4}, "num_qubits":16,
  "coupling_map": [[0,1],[0,4]]}, "shots": 4096, "mode": "emu"}}
```
- One qubit per room, numbered row-major. `coupling_map` lists the candidate open corridors, and only horizontal/vertical neighbours are allowed
- Params: `shots` ≤10000, `steps` 1–5, `fraction`, `k` 1–4, `top_n` (default 16, **-1 = every layout**)
- `coupling_map` = open corridors (ZZ=+1). Neighbouring pairs left out become walls (ZZ=-1)
- Output (checked on a real job): `output.results.measurements` = [{bitstring, probability}] (no counts, so counts = probability × `output.metrics.num_shots`), plus `z_expectations`, `zz_couplings`, `target.edge_signs`

## Checked on real jobs (29 Oct 2026)
- `graph-v1` `mode: qpu` works with Moth's shared IBM credentials: `ibm_marrakesh`, 20 s end to end, with the same output shape as emu plus `backend` and `ibm_job_id`
- `graph-v1` returns only the **top 20** bitstrings and has no `top_n` param. Moth confirmed this on Discord (30 Oct) and said they'll make it configurable "in the next update" (no date). Until then the design must work with top 20. When it lands: add `top_n: -1` to `HeatRequests.cs`, re-run `atlas_probe.py seed`, and update the top-20 test comment in `CoreTests.cs`. There's also a `fake` mode (a noisy snapshot of a real chip, needs `backend_name`)

## Probe script
`src/tools/atlas_probe.py` calls `me`, `graph-v1` and `labyrinth-v1` with Entangled Heat-style inputs and saves the raw results to `docs/samples/`. The key comes from `MOTH_API_KEY` or `config/EntangledHeat.ini` (gitignored).

## Discord questions
- [x] Does "Quantum Drive Engine" mean `graph-v1` or `qdrive-api-v1`? **Both count** (same engine class; QDrive is the improved version)
- [x] Free-tier credits/rate limits: **answered (2 Nov)**. There's one limit of **300 requests/min per key** that covers every request (job submits, status polls, results). No credit cap was mentioned. QPU jobs are "early alpha", and a separate QPU tier/access-control system will be "announced soon". Our worst case is about 3 emu jobs polling at 1.5 s plus QPU polls at 15 s, roughly 150 req/min, so we stay under the limit. If QPU access gets gated, the 4★+ IBM upgrade fails and the mod falls back to emu with a 30 s retry
- [—] Judges' API key / cached fallback: **dropped (30 Oct)**. Assume judges bring their own key; keep the labelled seed-cache fallback for no-key/no-network
- [x] Can `graph-v1` return more than the top 20 bitstrings? **Not yet**; configurable "in the next update" (no date given)

## Checked 2 Nov 2026 (17:00)
- **IBM hardware (`mode: qpu`) is failing right now.** The job submits and then fails on IBM's side with code 1370: *"The allocation for this instance's plan on the account is not active"* (`ibm_collection_failed`, not retryable). Moth's shared IBM plan seems paused, which fits "QPU early alpha / own tier soon". Entangled Heat's 4★+ IBM request now fails cleanly: it backs off 5 min and the simulator carries on. **Ask on Discord** whether QPU comes back before 9 Nov.
- **`mode: fake` works** (`graph-v1`, needs `backend_name`, e.g. `marrakesh`, `fez`; full list in the spec: `fake_cusco` … `fake_quebec`). This is a noisy calibration snapshot of a real IBM chip, ~6–7 s, with no queue. With ZZ=−1 pairs: 98–99% opposite on fake Marrakesh/Fez vs 100% on emu. Samples are in `docs/samples/stash-pairs-fake-*.json`.
