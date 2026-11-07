"""Probe blur-v1 as a tunnelling kernel: one bright pixel in, landing distribution out.

Usage: python blur_probe.py <strength> [mode] [size] [x] [y]
  mode: sim (default) | emu   (emu = fake_marrakesh, estimated from shots)
Saves the input/output PNGs and the result JSON to getaway/docs/samples/.
The key comes from MOTH_API_KEY or config/EntangledHeat.ini (same as atlas_probe.py).
"""
import io
import json
import sys
import time
from pathlib import Path

import numpy as np
import requests
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src" / "tools"))
import atlas_probe as ap  # noqa: E402

ap.s.headers["Authorization"] = f"Bearer {ap.load_key()}"
OUT = Path(__file__).resolve().parents[1] / "docs" / "samples"


def upload_png(arr, name):
    buf = io.BytesIO()
    Image.fromarray(arr.astype(np.uint8), "L").save(buf, "PNG")
    data = buf.getvalue()
    meta = ap.api("POST", "/api/v1/assets", json={"filename": name, "content_type": "image/png",
                                                  "size_bytes": len(data)}).json()
    up = meta["upload"]
    r = requests.request(up.get("method", "PUT"), up["url"], data=data,
                         headers=up.get("headers") or {"Content-Type": "image/png"}, timeout=60)
    r.raise_for_status()
    asset_id = meta.get("asset_id") or meta.get("id")
    ap.api("POST", f"/api/v1/assets/{asset_id}/complete")
    return asset_id


def main():
    strength = float(sys.argv[1])
    mode = sys.argv[2] if len(sys.argv) > 2 else "sim"
    n = int(sys.argv[3]) if len(sys.argv) > 3 else 32
    x = int(sys.argv[4]) if len(sys.argv) > 4 else n // 2 - 1
    y = int(sys.argv[5]) if len(sys.argv) > 5 else 0
    img = np.zeros((n, n))
    img[y, x] = 255
    asset = upload_png(img, f"tunnel-{n}.png")
    params = {"strength": strength, "style": "rx", "size": n, "reach": float(sys.argv[6]) if len(sys.argv) > 6 else 0.0}
    if mode == "emu":
        params.update({"machine": "fake_marrakesh", "shots": 4096})
    t0 = time.perf_counter()
    job = ap.api("POST", "/api/v1/engines/blur-v1/process",
                 json={"params": params, "input_files": {"image": asset}}).json()["job_id"]
    while True:
        st = ap.api("GET", f"/api/v1/jobs/{job}/status").json()
        if st["status"] == "completed":
            break
        if st["status"] in ("failed", "cancelled"):
            sys.exit(f"job {job} {st['status']}: {st.get('error')}")
        time.sleep(1.5)
    res = ap.api("GET", f"/api/v1/jobs/{job}/result").json()
    secs = time.perf_counter() - t0
    url = next(o["url"] for o in res["outputs"] if o.get("url"))
    out = np.array(Image.open(io.BytesIO(requests.get(url, timeout=60).content)).convert("L"), dtype=float)
    tag = f"blur-{mode}-s{strength}-{n}-{time.strftime('%H%M%S')}"
    np.save(OUT / f"{tag}.npy", out)
    Image.fromarray(img.astype(np.uint8)).resize((n * 8, n * 8), Image.NEAREST).save(OUT / f"{tag}-in.png")
    Image.fromarray(out.astype(np.uint8)).resize((n * 8, n * 8), Image.NEAREST).save(OUT / f"{tag}-out.png")
    (OUT / f"{tag}.json").write_text(json.dumps({"job_id": job, "params": params, "seconds": secs,
                                                  "pixel": [x, y], "result": res}, indent=1))
    p = out ** 2 / (out ** 2).sum()  # brightness ~ amplitude, so weight by brightness^2
    print(f"job {job} {secs:.1f}s  nonzero={int((out > 0).sum())}  origin p={p[y, x]:.3f}")
    for (yy, xx) in sorted(zip(*np.nonzero(out)), key=lambda c: -out[c])[:14]:
        print(f"  ({xx - x:+3d},{yy - y:+3d}) val={out[yy, xx]:5.0f} p={p[yy, xx]:.3f}")


if __name__ == "__main__":
    main()
