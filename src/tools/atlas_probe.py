"""Atlas probe: call the Moth engines Entangled Heat will use, outside the game.

Usage (from the project root):
    python src/tools/atlas_probe.py me          # check the API key works
    python src/tools/atlas_probe.py graph       # police "heat" graph state (graph-v1)
    python src/tools/atlas_probe.py labyrinth   # police search grid (labyrinth-v1)
    python src/tools/atlas_probe.py all
    python src/tools/atlas_probe.py seed        # write seed-cache/ from real jobs for the release
    add --wanted N (1-5) to pick the star level for graph, default 3
    add --qpu to run on real IBM hardware (queue can take minutes to hours)

The key is read from the MOTH_API_KEY env var, or from config/EntangledHeat.ini
([Atlas] ApiKey=...). That file is gitignored. The key is never printed.
Raw results are saved to docs/samples/ so the in-game code can be written
against real output.
"""

import configparser
import json
import os
import sys
import time
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[2]
SAMPLES = ROOT / "docs" / "samples"


def load_key():
    key = os.environ.get("MOTH_API_KEY", "").strip()
    if key:
        return key
    ini = ROOT / "config" / "EntangledHeat.ini"
    if ini.exists():
        cp = configparser.ConfigParser()
        cp.read(ini, encoding="utf-8")
        key = cp.get("Atlas", "ApiKey", fallback="").strip()
    if not key:
        sys.exit("No API key. Set MOTH_API_KEY or copy config/EntangledHeat.example.ini "
                 "to config/EntangledHeat.ini and fill in ApiKey.")
    return key


API_BASE = os.environ.get("MOTH_API_BASE", "https://api.mothquantum.com").rstrip("/")
s = requests.Session()
s.headers.update({"User-Agent": "entangled-heat-probe", "Accept": "application/json"})


def api(method, path, **kw):
    r = s.request(method, f"{API_BASE}{path}", timeout=60, **kw)
    if r.status_code >= 400:
        raise RuntimeError(f"{method} {path} -> {r.status_code}: {r.text[:300]}")
    return r


def run_job(engine_id, params, poll=1.5):
    """submit -> poll -> result. Returns (result json, job_id, seconds)."""
    t0 = time.perf_counter()
    job_id = api("POST", f"/api/v1/engines/{engine_id}/process", json={"params": params}).json()["job_id"]
    print(f"  {engine_id}: job {job_id} submitted")
    while True:
        job = api("GET", f"/api/v1/jobs/{job_id}/status").json()
        if job["status"] == "completed":
            break
        if job["status"] in ("failed", "cancelled"):
            raise RuntimeError(f"job {job_id} {job['status']}: {job.get('error')}")
        time.sleep(poll)
    res = api("GET", f"/api/v1/jobs/{job_id}/result").json()
    return res, job_id, time.perf_counter() - t0


def save(name, payload):
    SAMPLES.mkdir(parents=True, exist_ok=True)
    path = SAMPLES / f"{name}-{time.strftime('%Y%m%d-%H%M%S')}.json"
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(f"  saved {path.relative_to(ROOT)}")


# Link strength between partner cars per wanted level (measured on emu, 29 Oct 2026:
# fraction 0.25 -> ~85% partner agreement, 0.5 -> ~92%, 1.0 -> 100%).
PAIR_FRACTION = {1: 0.1, 2: 0.25, 3: 0.5, 4: 1.0, 5: 1.0}
SQUAD_FRACTION = 0.25  # 5 stars: pairs chained, whole squad agrees ~50% of shots


def heat_graph_params(units=6, wanted=3, mode="emu"):
    """Police cars as three entangled pairs (0-1, 2-3, 4-5).

    Every car starts in superposition (X=1), then each pair gets a ZZ=+1 link whose
    strength grows with the wanted level: loose at 1 star, perfect Bell pairs at 4.
    At 5 stars the pairs are also linked to each other, so the squad moves as one.
    """
    level = min(max(wanted, 1), 5)
    ops = [{"type": "bloch", "qubit": q, "paulis": {"X": 1.0}} for q in range(units)]
    edges = []
    # Chain order matters: applying all pairs first and then the cross links cancels them out.
    for i in range(units - 1):
        is_pair = i % 2 == 0
        if not is_pair and level < 5:
            continue
        edges.append([i, i + 1])
        frac = PAIR_FRACTION[level] if is_pair else SQUAD_FRACTION
        ops.append({"type": "relationship", "qubits": [i, i + 1], "paulis": {"ZZ": 1.0}, "fraction": frac})
    return {"num_qubits": units, "coupling_map": edges, "operations": ops, "shots": 1024, "mode": mode}


def search_grid_params(rows=4, cols=4, mode="emu"):
    """City blocks around the player as a grid of rooms. Every adjacent pair is
    offered as a corridor; the engine's quantum sampling decides the final layout,
    which the game reads as open streets vs. police roadblocks."""
    edges = []
    for r in range(rows):
        for c in range(cols):
            i = r * cols + c
            if c + 1 < cols:
                edges.append([i, i + 1])
            if r + 1 < rows:
                edges.append([i, i + cols])
    return {"level_data": {"name": "search-grid", "grid_size": {"rows": rows, "cols": cols},
                           "num_qubits": rows * cols, "coupling_map": edges},
            "shots": 4096, "top_n": -1, "mode": mode}


def partner_agreement(measurements):
    """Fraction of shots where each entangled pair (0-1, 2-3, 4-5) made the same call."""
    total = sum(m["count"] for m in measurements)
    return [round(sum(m["count"] for m in measurements if m["bitstring"][a] == m["bitstring"][a + 1]) / total, 2)
            for a in (0, 2, 4)]


def cmd_me():
    me = api("GET", "/api/v1/me").json()
    print("  authenticated as:", me.get("email") or me.get("id") or "(ok)")


def cmd_engine(name, engine_id, params):
    print(f"> {name} ({params.get('mode')})")
    # IBM queues can take hours, so poll slowly (same as AtlasClient in the mod).
    res, job_id, secs = run_job(engine_id, params, poll=15 if params.get("mode") == "qpu" else 1.5)
    print(f"  done in {secs:.1f}s")
    save(name, {"engine": engine_id, "job_id": job_id, "seconds": round(secs, 2),
                "request": params, "result": res})
    return res


SEED = ROOT / "seed-cache"


def counts_of(result):
    """(bitstring -> shots) from a saved result, in the same way as MeasurementParser.cs."""
    out = result["result"]["output"]
    if "measurements" in out:  # graph-v1: real counts
        return {m["bitstring"]: m["count"] for m in out["measurements"] if m.get("count")}
    shots = out.get("metrics", {}).get("num_shots") or 1000  # labyrinth-v1: probabilities only
    counts = {m["bitstring"]: round(m["probability"] * shots) for m in out["results"]["measurements"]}
    return {b: n for b, n in counts.items() if n > 0}


def latest_sample(pattern, params):
    """Newest saved job whose request matches the current params. IBM runs beat simulator runs."""
    matches = []
    for path in sorted(SAMPLES.glob(pattern)):
        sample = json.loads(path.read_text(encoding="utf-8"))
        req = dict(sample["request"])
        if {**req, "mode": "emu"} == {**params, "mode": "emu"}:
            matches.append(sample)
    hardware = [m for m in matches if m["request"].get("mode") == "qpu"]
    return (hardware or matches or [None])[-1]


def cmd_seed():
    """Write seed-cache/ (heat-L1..5.json, grid.json) in the QuantumPool cache format from real
    jobs, so a judge's very first run already has real quantum data. Runs any missing jobs on emu."""
    SEED.mkdir(exist_ok=True)
    slots = [(f"heat-L{n}", f"heat-graph-L{n}-*.json", "graph-v1", heat_graph_params(wanted=n)) for n in range(1, 6)]
    slots.append(("grid", "search-grid-*.json", "labyrinth-v1", search_grid_params()))
    for name, pattern, engine, params in slots:
        sample = latest_sample(pattern, params)
        if sample is None:
            print(f"> {name}: no matching sample, running one")
            cmd_engine(pattern.split("-*")[0], engine, params)
            sample = latest_sample(pattern, params)
        source = "IbmHardware" if sample["request"].get("mode") == "qpu" else "Simulator"
        counts = counts_of(sample["result"])
        (SEED / f"{name}.json").write_text(json.dumps(
            {"JobId": sample["job_id"], "Source": source, "Counts": counts}, indent=2), encoding="utf-8")
        print(f"  {name}: job {sample['job_id'][:8]} ({source}), {sum(counts.values())} shots, {len(counts)} outcomes")


def main():
    args = sys.argv[1:] or ["all"]
    mode = "qpu" if "--qpu" in args else "emu"
    wanted = int(args[args.index("--wanted") + 1]) if "--wanted" in args else 3
    what = args[0]
    s.headers["Authorization"] = f"Bearer {load_key()}"
    if what == "seed":
        return cmd_seed()
    if what in ("me", "all"):
        cmd_me()
    if what in ("graph", "all"):
        res = cmd_engine(f"heat-graph-L{wanted}", "graph-v1", heat_graph_params(wanted=wanted, mode=mode))
        out = res["result"]["output"]
        print("  dominant bitstring:", out.get("dominant_bitstring"),
              "| partner agreement:", partner_agreement(out["measurements"]))
    if what in ("labyrinth", "all"):
        cmd_engine("search-grid", "labyrinth-v1", search_grid_params(mode=mode))


if __name__ == "__main__":
    main()
