"""Build and run the data collection player from a committed render spec, and record the run.

    python tools/render.py render_specs/gso_pilot.json            # build, render, verify, record
    python tools/render.py render_specs/gso_pilot.json --dry-run  # checks and commands only
    python tools/render.py --verify D:/renders/<run_id> --build-log D:/builds/<run_id>/build.log --per-grasp 50

A render is only made from a committed generator state, so the commit alone pins every
grasp_spec.json, prefab, scene and script. The script refuses to run when:
  - the working tree has changes or untracked files (--allow-dirty records the diff instead),
  - the Unity Editor has the project open (the build needs it closed),
  - the installed Unity version differs from ProjectSettings/ProjectVersion.txt,
  - the output folder exists, or the disk does not have room for the estimated output.

Steps: build with BatchDataCollection.Build into <build-root>/<run_id>/, restore the files the build
rewrites, run the player into <out-root>/<run_id>/, check the output (one label and the same frame
count per sequence, per_grasp sequences for every object-grasp pair of the build) and write
<out-root>/<run_id>/generation.json. The build folder is deleted after a successful run (it can be
rebuilt from the commit and the spec) unless --keep-build; its log is copied into the run folder.

Spec (JSON, committed under render_specs/):
    name          run name, used in the run id
    prefabs       prefab names (Assets/Prefabs/YCB or Assets/Prefabs/GSO); [] = the scene's list
    preshapes     preshape filter; [] = all grasps
    per_grasp     sequences per object-grasp pair
    seed          scenario random seed
    resolution    [width, height] of the player window = capture resolution
    expected_pairs   optional: fail before rendering if the build selects a different number of pairs

Standard library only, Python >= 3.9.
"""
import argparse
import datetime as dt
import hashlib
import json
import platform
import re
import shutil
import subprocess
import sys
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
UNITY_HUB_EDITORS = Path("C:/Program Files/Unity/Hub/Editor")
# Files the Windows build rewrites or deletes (README, "command-line" section); restored afterwards.
BUILD_TOUCHED = ["Assets/Resources", "ProjectSettings/BurstAotSettings_StandaloneWindows.json"]
# Measured on gso_pilot: 73 GB for 1800 sequences of 90 frames at 640x480.
MB_PER_SEQ_640x480 = 45
SPEC_KEYS = {"name", "prefabs", "preshapes", "per_grasp", "seed", "resolution", "expected_pairs", "description"}


def git(*args, check=True) -> str:
    r = subprocess.run(["git", *args], cwd=REPO, capture_output=True, text=True, encoding="utf-8")
    if check and r.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)}: {r.stderr.strip()}")
    return r.stdout.rstrip()  # keep leading spaces: porcelain status lines start with " M"


def fail(msg: str):
    sys.exit(f"render.py: {msg}")


def now() -> str:
    return dt.datetime.now().astimezone().isoformat(timespec="seconds")


# ---------------------------------------------------------------- checks

def load_spec(path: Path) -> dict:
    spec = json.loads(path.read_text(encoding="utf-8"))
    unknown = set(spec) - SPEC_KEYS
    missing = {"name", "per_grasp", "seed", "resolution"} - set(spec)
    if unknown or missing:
        fail(f"spec {path}: unknown keys {sorted(unknown)}, missing keys {sorted(missing)}")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", spec["name"]):
        fail("spec name may only contain letters, digits, '_' and '-'")
    spec.setdefault("prefabs", [])
    spec.setdefault("preshapes", [])
    return spec


def git_state() -> dict:
    sha = git("rev-parse", "HEAD")
    status = git("status", "--porcelain", "--untracked-files=all")
    return {
        "sha": sha,
        "branch": git("branch", "--show-current"),
        "remote": git("remote", "get-url", "origin", check=False),
        "pushed": bool(git("branch", "-r", "--contains", sha, check=False)),
        "dirty": bool(status),
        "status": status.splitlines(),
    }


def unity_versions() -> tuple[str, str]:
    text = (REPO / "ProjectSettings/ProjectVersion.txt").read_text()
    version = re.search(r"m_EditorVersion: (\S+)", text).group(1)
    revision = re.search(r"m_EditorVersionWithRevision: \S+ \((\w+)\)", text)
    return version, revision.group(1) if revision else ""


def perception_version() -> str:
    lock = json.loads((REPO / "Packages/packages-lock.json").read_text())
    entry = lock["dependencies"]["com.unity.perception"]
    if entry.get("source") == "embedded":
        pkg = json.loads((REPO / "Packages/com.unity.perception/package.json").read_text())
        return f"{pkg['version']} (embedded)"
    return entry["version"]


def editor_open() -> bool:
    """Unity keeps Temp/UnityLockfile open (and locked) while the project is open in the Editor."""
    lock = REPO / "Temp/UnityLockfile"
    if not lock.exists():
        return False
    try:
        with open(lock, "a"):
            return False
    except PermissionError:
        return True


def free_bytes(path: Path) -> int:
    p = path
    while not p.exists():
        p = p.parent
    return shutil.disk_usage(p).free


def estimate_bytes(spec: dict, sequences: int) -> int:
    w, h = spec["resolution"]
    return int(sequences * MB_PER_SEQ_640x480 * 1e6 * (w * h) / (640 * 480))


def gpu() -> dict:
    try:
        out = subprocess.run(["nvidia-smi", "--query-gpu=name,driver_version", "--format=csv,noheader"],
                             capture_output=True, text=True, timeout=30).stdout.strip().splitlines()
        name, driver = [s.strip() for s in out[0].split(",")]
        return {"name": name, "driver": driver}
    except Exception:  # noqa: BLE001 - informational only
        return {}


# ---------------------------------------------------------------- build log and output checks

def parse_build_log(log: Path) -> dict:
    text = log.read_text(encoding="utf-8", errors="replace")
    pairs = re.findall(r"\[BatchDataCollection\] pair (\S+) / (\S+)", text)
    summary = re.search(r"\[BatchDataCollection\] prefabs=(\d+) .*?pairs=(\d+) perGrasp=(\d+) "
                        r"iterationCount=(\d+) seed=(\d+)", text)
    result = re.search(r"\[BatchDataCollection\] build result: (\w+)", text)
    return {
        "pairs": [f"{o} / {g}" for o, g in pairs],
        "num_pairs": int(summary.group(2)) if summary else None,
        "iteration_count": int(summary.group(4)) if summary else None,
        "seed": int(summary.group(5)) if summary else None,
        "result": result.group(1) if result else None,
    }


def grasp_type(child: str) -> str:
    """Grasp child names carry flags in brackets, e.g. large_diameter[susd] -> large_diameter."""
    return child.split("[", 1)[0]


def verify_output(out: Path, pairs: list[str], per_grasp: int) -> dict:
    """Group the captures by sequence and compare against per_grasp sequences per build pair."""
    datasets = sorted(out.glob("*/Dataset*")) or sorted(out.glob("Dataset*"))
    if len(datasets) != 1:
        return {"ok": False, "error": f"expected one Dataset* folder under {out}, found {len(datasets)}"}
    files = sorted(datasets[0].glob("captures_*.json"), key=lambda p: int(re.findall(r"\d+", p.stem)[-1]))
    seqs: dict[str, list[dict]] = {}
    for f in files:
        for cap in json.loads(f.read_text())["captures"]:
            seqs.setdefault(cap["sequence_id"], []).append(cap)

    lengths = Counter(len(c) for c in seqs.values())
    frames = lengths.most_common(1)[0][0] if lengths else 0
    valid, skipped = Counter(), []
    labels_seen = Counter()
    for seq_id, caps in seqs.items():
        anns = [a["grasp_type_values"] for c in caps for a in c["annotations"]
                if a.get("model_type") == "GraspTypeAnnotationDefinition"]
        labels = {(a["instance_name"], a["grasp_type_name"], a["preshape_name"]) for a in anns}
        steps = sorted(c["step"] for c in caps)
        if len(caps) != frames or len(labels) != 1 or steps != list(range(frames)):
            skipped.append({"sequence_id": seq_id, "frames": len(caps), "labels": sorted(labels)})
            continue
        (instance, gtype, preshape), = labels
        valid[f"{instance} / {gtype}"] += 1
        labels_seen[preshape] += 1

    expected = Counter()
    for p in pairs:
        obj, child = p.split(" / ")
        expected[f"{obj} / {grasp_type(child)}"] += per_grasp
    mismatched = {k: {"expected": expected.get(k, 0), "found": valid.get(k, 0)}
                  for k in sorted(set(expected) | set(valid)) if expected.get(k, 0) != valid.get(k, 0)}
    # Perception writes one unlabelled warm-up capture before the first sequence.
    unexpected_skips = [s for s in skipped if s["labels"]]
    return {
        "ok": not mismatched and not unexpected_skips and frames > 0,
        "dataset_folder": str(datasets[0].relative_to(out)).replace("\\", "/"),
        "sequences": sum(valid.values()),
        "expected_sequences": sum(expected.values()),
        "frames_per_sequence": frames,
        "per_preshape": dict(sorted(labels_seen.items())),
        "per_pair": dict(sorted(valid.items())),
        "mismatched_pairs": mismatched,
        "skipped": skipped,
    }


def image_size(out: Path) -> list[int] | None:
    png = next(out.glob("*/RGB*/*.png"), None)
    if png is None:
        return None
    head = png.read_bytes()[:24]  # PNG IHDR: width and height at bytes 16-24
    return [int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")]


def folder_bytes(path: Path) -> int:
    return sum(f.stat().st_size for f in path.rglob("*") if f.is_file())


# ---------------------------------------------------------------- main

def restore_build_touched(before: list[str]):
    """Put back what the build rewrites, then check the build left nothing else behind."""
    git("checkout", "HEAD", "--", *BUILD_TOUCHED)
    after = git("status", "--porcelain", "--untracked-files=all").splitlines()
    if sorted(after) != sorted(before):
        new = sorted(set(after) - set(before))
        raise RuntimeError(f"the build left changes in the project: {new}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("spec", type=Path, nargs="?")
    p.add_argument("--out-root", type=Path, default=Path("D:/renders"))
    p.add_argument("--build-root", type=Path, default=Path("D:/builds"))
    p.add_argument("--unity", type=Path, help="Unity.exe (default: the Hub install of ProjectVersion.txt)")
    p.add_argument("--allow-dirty", action="store_true", help="render from uncommitted changes, saving the diff")
    p.add_argument("--keep-build", action="store_true")
    p.add_argument("--dry-run", action="store_true", help="run the checks and print the commands")
    p.add_argument("--verify", type=Path, help="only check an existing output folder (needs --build-log)")
    p.add_argument("--build-log", type=Path)
    p.add_argument("--per-grasp", type=int, default=50)
    args = p.parse_args()

    if args.verify:
        if not args.build_log:
            fail("--verify needs --build-log")
        pairs = parse_build_log(args.build_log)["pairs"]
        report = verify_output(args.verify, pairs, args.per_grasp)
        print(json.dumps({k: v for k, v in report.items() if k != "per_pair"}, indent=1))
        sys.exit(0 if report["ok"] else 1)
    if not args.spec:
        p.error("spec is required")

    # ---- checks
    spec_path = args.spec.resolve()
    spec = load_spec(spec_path)
    state = git_state()
    if state["dirty"] and not args.allow_dirty:
        fail("the working tree is not clean; commit (or stash) first, or pass --allow-dirty:\n  "
             + "\n  ".join(state["status"]))
    touched = [s for s in state["status"] if any(s[3:].startswith(t) for t in BUILD_TOUCHED)]
    if touched:
        fail(f"uncommitted changes in files the build rewrites (they are restored from HEAD after "
             f"the build and would be lost): {touched}")
    spec_rel = spec_path.relative_to(REPO).as_posix()
    if not args.allow_dirty and not git("ls-files", spec_rel):
        fail(f"{spec_rel} is not committed")
    if not state["pushed"]:
        print(f"warning: {state['sha'][:7]} is not on any remote branch yet", file=sys.stderr)
    if editor_open():
        fail("the project is open in the Unity Editor; close it first")
    version, revision = unity_versions()
    unity = args.unity or UNITY_HUB_EDITORS / version / "Editor/Unity.exe"
    if not unity.exists():
        fail(f"Unity {version} not found at {unity} (pass --unity)")
    if version not in str(unity):
        fail(f"{unity} does not look like Unity {version} (ProjectVersion.txt)")

    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M")
    run_id = f"{spec['name']}_{stamp}_{state['sha'][:7]}" + ("_dirty" if state["dirty"] else "")
    out = args.out_root / run_id
    build_dir = args.build_root / run_id
    exe = build_dir / "DataCollection.exe"
    build_log = build_dir / "build.log"
    if out.exists() or build_dir.exists():
        fail(f"{out} or {build_dir} already exists")
    if free_bytes(args.build_root) < 4e9:
        fail(f"less than 4 GB free for the build on {args.build_root}")

    build_cmd = [str(unity), "-batchmode", "-quit", "-projectPath", str(REPO),
                 "-executeMethod", "BatchDataCollection.Build",
                 "-perGrasp", str(spec["per_grasp"]), "-seed", str(spec["seed"]),
                 "-buildPath", str(exe), "-logFile", str(build_log)]
    if spec["prefabs"]:
        build_cmd += ["-prefabs", ",".join(spec["prefabs"])]
    if spec["preshapes"]:
        build_cmd += ["-preshapes", ",".join(spec["preshapes"])]
    w, h = spec["resolution"]
    player_cmd = [str(exe), "-screen-fullscreen", "0", "-screen-width", str(w), "-screen-height", str(h),
                  "-logFile", str(out / "player.log"), "--output-path", str(out)]

    print(f"run {run_id}\n  generator {state['sha']} ({state['branch']}), Unity {version}\n"
          f"  build: {subprocess.list2cmdline(build_cmd)}\n  player: {subprocess.list2cmdline(player_cmd)}")
    if args.dry_run:
        n = len(spec["prefabs"]) * 2 * spec["per_grasp"]  # rough: ~2 grasps per object
        print(f"  disk: ~{estimate_bytes(spec, n) / 1e9:.0f} GB needed for ~{n} sequences (~2 grasps per "
              f"object; checked exactly after the build), {free_bytes(args.out_root) / 1e9:.0f} GB free "
              f"on {args.out_root.anchor}")
        return

    record = {
        "run_id": run_id,
        "status": "building",
        "spec_path": spec_rel,
        "spec": spec,
        "spec_sha256": hashlib.sha256(spec_path.read_bytes()).hexdigest(),
        "generator": {k: state[k] for k in ("remote", "branch", "sha", "pushed", "dirty")},
        "unity": {"version": version, "revision": revision, "exe": str(unity)},
        "perception": perception_version(),
        "machine": {"os": platform.platform(), "gpu": gpu()},
        "build": {"command": build_cmd},
        "player": {"command": player_cmd},
        "started": now(),
    }
    out.mkdir(parents=True)

    def save():
        (out / "generation.json").write_text(json.dumps(record, indent=1) + "\n", encoding="utf-8")

    if state["dirty"]:
        (out / "dirty.patch").write_text(git("diff", "HEAD", "--binary") + "\n", encoding="utf-8")
        record["generator"]["dirty_status"] = state["status"]
        record["generator"]["dirty_patch"] = "dirty.patch"
    save()

    try:
        # ---- build
        t0 = dt.datetime.now()
        r = subprocess.run(build_cmd)
        restore_build_touched(state["status"])
        info = parse_build_log(build_log)
        shutil.copy2(build_log, out / "build.log")
        record["build"].update(info, seconds=round((dt.datetime.now() - t0).total_seconds()),
                               exit_code=r.returncode)
        if r.returncode != 0 or info["result"] != "Succeeded":
            raise RuntimeError(f"build failed (exit {r.returncode}, result {info['result']}), see build.log")
        if spec.get("expected_pairs") is not None and info["num_pairs"] != spec["expected_pairs"]:
            raise RuntimeError(f"build selected {info['num_pairs']} pairs, spec expects {spec['expected_pairs']}")
        need = estimate_bytes(spec, info["num_pairs"] * spec["per_grasp"])
        if free_bytes(out) < 1.1 * need:
            raise RuntimeError(f"~{need / 1e9:.0f} GB needed for {info['num_pairs']} pairs, "
                               f"{free_bytes(out) / 1e9:.0f} GB free")

        # ---- render
        record["status"] = "rendering"
        save()
        t0 = dt.datetime.now()
        r = subprocess.run(player_cmd)
        record["player"].update(seconds=round((dt.datetime.now() - t0).total_seconds()), exit_code=r.returncode)
        if r.returncode != 0:
            raise RuntimeError(f"player exited with {r.returncode}, see player.log")

        # ---- verify and record
        check = verify_output(out, info["pairs"], spec["per_grasp"])
        check["image_size"] = image_size(out)
        if check["image_size"] and check["image_size"] != [w, h]:
            check["ok"] = False
            check["error"] = f"images are {check['image_size']}, spec resolution {[w, h]}"
        record["output"] = check
        record["output"]["bytes"] = folder_bytes(out)
        record["status"] = "ok" if check["ok"] else "failed_verification"
    except Exception as e:  # noqa: BLE001 - recorded, then re-raised as exit code
        record["status"] = "failed"
        record["error"] = str(e)
    record["finished"] = now()
    save()

    if record["status"] == "ok" and not args.keep_build:
        shutil.rmtree(build_dir, ignore_errors=True)
    print(f"{record['status']}: {out}")
    if record["status"] != "ok":
        print(record.get("error") or json.dumps(record["output"]["mismatched_pairs"], indent=1), file=sys.stderr)
        sys.exit(1)
    o = record["output"]
    print(f"  {o['sequences']} sequences x {o['frames_per_sequence']} frames, {o['bytes'] / 1e9:.1f} GB, "
          f"per preshape {o['per_preshape']}")


if __name__ == "__main__":
    main()
