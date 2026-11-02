"""Build seed-cache/stash.json for the release from a real Atlas job.

Uses the newest saved docs/samples/stash-pairs-<mode>-*.json, or runs a new job if there is none
(or with --new). The key comes from MOTH_API_KEY or config/SchrodingersStash.ini (never commit it).

    python tools/stash_seed.py [--new] [--mode fake|emu|qpu] [--backend marrakesh]
"""
import configparser
import json
import os
import sys
import time
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
SAMPLES = ROOT / "docs" / "samples"
SEED = ROOT / "seed-cache"
API = os.environ.get("MOTH_API_BASE", "https://api.mothquantum.com").rstrip("/")
SOURCES = {"emu": "Simulator", "fake": "NoisyChip", "qpu": "IbmHardware"}


def load_key():
    key = os.environ.get("MOTH_API_KEY", "").strip()
    ini = ROOT / "config" / "SchrodingersStash.ini"
    if not key and ini.exists():
        cp = configparser.ConfigParser()
        cp.read(ini, encoding="utf-8")
        key = cp.get("Atlas", "ApiKey", fallback="").strip()
    if not key:
        sys.exit("No API key. Set MOTH_API_KEY or fill in ApiKey in config/SchrodingersStash.ini.")
    return key


def pair_graph(mode, backend):
    """Same request as Stash.PairGraph in SchrodingersStash.Core."""
    ops = [{"type": "bloch", "qubit": q, "paulis": {"X": 1.0}} for q in range(6)]
    pairs = [[0, 1], [2, 3], [4, 5]]
    ops += [{"type": "relationship", "qubits": p, "paulis": {"ZZ": -1.0}, "fraction": 1.0} for p in pairs]
    params = {"num_qubits": 6, "coupling_map": pairs, "operations": ops, "shots": 1024, "mode": mode}
    if mode != "emu" and backend:
        params["backend_name"] = backend
    return params


def run_job(params):
    s = requests.Session()
    s.headers.update({"Authorization": f"Bearer {load_key()}", "Accept": "application/json"})
    r = s.post(f"{API}/api/v1/engines/graph-v1/process", json={"params": params}, timeout=60)
    r.raise_for_status()
    job_id = r.json()["job_id"]
    print(f"job {job_id} submitted ({params['mode']})")
    while True:
        status = s.get(f"{API}/api/v1/jobs/{job_id}/status", timeout=60).json()
        if status["status"] == "completed":
            break
        if status["status"] in ("failed", "cancelled"):
            sys.exit(f"job {job_id} {status['status']}: {status.get('error')}")
        time.sleep(15 if params["mode"] == "qpu" else 1.5)
    result = s.get(f"{API}/api/v1/jobs/{job_id}/result", timeout=60).json()
    SAMPLES.mkdir(parents=True, exist_ok=True)
    path = SAMPLES / f"stash-pairs-{params['mode']}-{params.get('backend_name', 'sim')}-{time.strftime('%Y%m%d-%H%M%S')}.json"
    path.write_text(json.dumps({"request": params, "job_id": job_id, "result": result}, indent=2), encoding="utf-8")
    print(f"saved {path.relative_to(ROOT)}")
    return path


def main():
    args = sys.argv[1:]
    mode = args[args.index("--mode") + 1] if "--mode" in args else "fake"
    backend = args[args.index("--backend") + 1] if "--backend" in args else "marrakesh"
    existing = sorted(SAMPLES.glob(f"stash-pairs-{mode}-*.json"))
    path = run_job(pair_graph(mode, backend)) if "--new" in args or not existing else existing[-1]

    sample = json.loads(path.read_text(encoding="utf-8"))
    measurements = sample["result"]["result"]["output"]["measurements"]
    counts = {m["bitstring"]: m["count"] for m in measurements if m.get("count")}
    SEED.mkdir(exist_ok=True)
    seed = {"JobId": sample["job_id"], "Source": SOURCES[sample["request"]["mode"]], "Counts": counts}
    (SEED / "stash.json").write_text(json.dumps(seed, indent=2), encoding="utf-8")
    total = sum(counts.values())
    print(f"seed-cache/stash.json: job {sample['job_id'][:8]} ({seed['Source']}), {total} shots, {len(counts)} outcomes")
    for p in range(3):
        opposite = sum(c for b, c in counts.items() if b[2 * p] != b[2 * p + 1])
        print(f"  pair {p + 1}: {opposite / total:.1%} opposite")


if __name__ == "__main__":
    main()
