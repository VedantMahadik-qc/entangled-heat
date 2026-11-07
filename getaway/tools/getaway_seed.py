"""Build seed-cache/hop.json and leap.json from real blur-v1 jobs saved by blur_probe.py.

Usage: python getaway_seed.py <hop-sample.json> <leap-sample.json>
  e.g. python getaway_seed.py ../docs/samples/blur-sim-s0.4-32-190859.json ../docs/samples/blur-sim-s0.8-32-190931.json
Each sample needs its -raw.png next to it (the exact image Atlas returned). The release ships these
so a player's first tunnels work before the API answers; the mod labels them CACHED (offline).
Same shape as TunnelPool.CacheFile.
"""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

SEED = Path(__file__).resolve().parents[1] / "seed-cache"


def seed(sample, kind):
    sample = Path(sample)
    meta = json.loads(sample.read_text())
    rgb = np.array(Image.open(sample.with_name(sample.stem + "-raw.png")).convert("RGB"), dtype=int)
    luma = (rgb.sum(axis=2) + 1) // 3  # same rounding as Png.DecodeLuma
    machine = meta["params"].get("machine", "")
    source = "NoisyChip" if machine.startswith("fake_") else "IbmHardware" if machine.startswith("ibm_") else "Simulator"
    out = {"JobId": meta["job_id"], "Source": source, "Kind": kind, "Size": int(luma.shape[0]),
           "Pixels": [int(v) for v in luma.flatten()]}
    SEED.mkdir(exist_ok=True)
    (SEED / f"{kind.lower()}.json").write_text(json.dumps(out))
    p = luma / luma.sum()
    print(f"{kind}: job {meta['job_id']} {meta['params']} stay {p[0, 15]:.3f}")


if __name__ == "__main__":
    seed(sys.argv[1], "Hop")
    seed(sys.argv[2], "Leap")
