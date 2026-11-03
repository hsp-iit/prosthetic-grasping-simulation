#!/usr/bin/env python3
"""Check every grasp box of the exported objects with the Shadow Hand, and calibrate an
automatic preshape choice against the preshapes of the grasps (GraspCatalog.cs).

For every grasp box, every preshape of shadow_preshapes.json is tried with a few
variations of the hand placement (thumb side, offset along the approach): the hand is
put in the open preshape (its penetration in the object is measured), then closed on the
object with the physics, and the resulting contacts are scored against what the preshape
needs (e.g. a pinch closes with fingertips only, a power grasp also touches with the
palm or the proximal phalanges).

Outputs, in --out (default python_scripts/MuJoCo/exported/_check, git-ignored):
- trials.csv: every trial (object, grasp, preshape, variation, metrics, score)
- grasps.csv: per grasp, the labeled preshape, its best trial and issues, the score of
  each preshape, the geometry of the object part (grasp_geometry.py) and the predicted
  preshapes
- report.md: the issues of the labeled preshapes (boxes to look at in Unity) and the
  calibration of the automatic preshape choice against the labels

Calibration: three predictors are compared with the labels, leave-one-object-out (each
object is predicted by a model fitted on the other objects only):
- physics: the preshape with the best score, no fitting
- geometry: a small decision tree on the geometry of the object part at the box
- geometry + physics: the same tree, also given the score of each preshape
and the baseline of always predicting the most frequent preshape.

Lift test (--lift): with the labeled preshape, in the best placement found above, and
in --lift-trials placements with a random error (up to --position-error mm along each
box axis and --rotation-error degrees around a random axis), the open hand moves forward
along the approach until it touches the object (at most 4 cm, as a user pushes the hand
onto the object; not into it, which would kick it away) and closes on it without
gravity (the object, free and damped as if resting on a table, can shift in the hand as
the fingers close), then gravity is turned on (object of --mass kg) and the
hand lifts the object by 10 cm and shakes it. The grasp holds when the object moved less than
2 cm in the hand and still touches it. A placement where the open hand already goes
into the object fails (a real hand would push the object away). Outputs lift.csv, the
lift_* columns of grasps.csv and a section of report.md.

Usage:
  python grasp_check.py                     # all exported objects
  python grasp_check.py --lift              # also the lift test
  python grasp_check.py 025_mug plum        # some objects (names or parts of them)
  python grasp_check.py --workers 4 --out /tmp/check
"""

import argparse
import csv
import itertools
import multiprocessing
import os
import sys
import time
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import grasp_geometry as gg  # noqa: E402
import preshape_viewer as pv  # noqa: E402

GEOMETRY = ["d1", "d2", "d1_local", "d2_local", "length", "depth"]
TREE_DEPTH = 3

FINGERS = ["FF", "MF", "RF", "LF"]

# Hand placement variations tried for every preshape
FLIPS = [False, True]
OFFSETS_MM = [-10, 0, 10]

# Thresholds
MAX_OPEN_PENETRATION_MM = 5.0     # the open preshape must not go deeper in the object
MAX_CONTACT_TO_BOX_MM = 30.0      # the closest contact must be this close to the box

# Lift test
LIFT_HEIGHT = 0.10            # m
LIFT_DURATION = 1.0           # s
SETTLE_DURATION = 0.3         # s after releasing the object, before lifting
SHAKE_AMPLITUDE = 0.02        # m, vertical
SHAKE_FREQUENCY = 2.0         # Hz
SHAKE_DURATION = 1.0          # s
MAX_ADVANCE = 0.040           # m: the hand moves forward until the open hand touches
ADVANCE_STEP = 0.002          # m
TOUCH_PENETRATION_MM = 1.0    # deeper than this counts as touching
# Damping of the free object while the hand closes (N s/m, N m s/rad): stands for the
# table it would rest on, so that a first light contact does not send it away
CLOSING_DAMPING = (2.0, 0.02)
MAX_SLIP_MM = 20.0            # largest move of the object in the hand for a hold

# What each preshape needs once closed: the thumb, how many of the listed fingers, and
# whether the palm (palm, proximal phalanges, little finger metacarpal) must touch
# (power) or must not touch (pinches)
REQUIREMENTS = {
    "power_no3": {"fingers": FINGERS, "count": 3, "palm": "required"},
    "pinch_no3": {"fingers": FINGERS, "count": 2, "palm": "forbidden"},
    "pinch_3": {"fingers": ["FF", "MF"], "count": 1, "palm": "forbidden"},
    "lateral_no3": {"fingers": ["FF"], "count": 1, "palm": "any"},
}


def classify(body):
    """(finger, segment) of a hand body name without prefix, e.g. ffdistal -> (FF, distal).
    The palm, the little finger metacarpal, the wrist and the forearm are (palm, palm)"""
    for finger in ("ff", "mf", "rf", "lf", "th"):
        if body.startswith(finger) and body != "lfmetacarpal":
            return finger.upper(), body[len(finger):]
    return "palm", "palm"


def contacts_by_part(scene):
    """The contacts of the hand with the object: per (finger, segment), the distance in mm
    of the closest contact to the grasp box"""
    parts = {}
    for contact in scene.data.contact[:scene.data.ncon]:
        if scene.object_geom not in (contact.geom1, contact.geom2):
            continue
        other = contact.geom2 if contact.geom1 == scene.object_geom else contact.geom1
        body = scene.model.body(scene.model.geom_bodyid[other]).name.replace(scene.prefix, "")
        part = classify(body)
        distance = 1000 * scene.box_distance(contact.pos)
        parts[part] = min(distance, parts.get(part, np.inf))
    return parts


def deepest_penetration_mm(scene):
    deepest = 0.0
    for contact in scene.data.contact[:scene.data.ncon]:
        if scene.object_geom in (contact.geom1, contact.geom2):
            deepest = min(deepest, contact.dist)
    return -1000 * deepest


def score_trial(preshape, open_penetration, parts):
    """Score in [0, 1] (1 = all the needs of the preshape met, no penetration), whether
    the trial is feasible, and the unmet needs"""
    needs = REQUIREMENTS.get(preshape, {"fingers": FINGERS, "count": 1, "palm": "any"})
    touching = {finger for finger, _ in parts}
    palm_touch = any(segment in ("palm", "proximal", "knuckle") and finger != "TH"
                     for finger, segment in parts)
    closest = min(parts.values()) if parts else np.inf

    unmet = []
    thumb = 1.0 if "TH" in touching else 0.0
    if not thumb:
        unmet.append("thumb not touching")
    fingers = sum(f in touching for f in needs["fingers"])
    finger_part = min(1.0, fingers / needs["count"])
    if finger_part < 1:
        unmet.append(f"{fingers}/{needs['count']} of {'+'.join(needs['fingers'])} touching")
    palm_part = 1.0
    if needs["palm"] == "required" and not palm_touch:
        palm_part = 0.0
        unmet.append("palm not touching")
    elif needs["palm"] == "forbidden" and palm_touch:
        palm_part = 0.0
        unmet.append("palm touching")
    if closest > MAX_CONTACT_TO_BOX_MM:
        unmet.append("no contact near the box" if parts else "no contact")
    if open_penetration > MAX_OPEN_PENETRATION_MM:
        unmet.append(f"open hand {open_penetration:.0f} mm in the object")

    score = (thumb + finger_part + palm_part) / 3
    score -= 0.02 * max(0.0, open_penetration - 2.0)
    score -= 0.01 * max(0.0, min(closest, 100.0) - 15.0)
    return max(score, 0.0), not unmet, unmet


def check_object(args):
    """All the trials of one object (run in a worker process)"""
    name, menagerie, hand = args
    hand_xml = Path(menagerie) / "shadow_hand" / f"{hand}_hand.xml"
    scene = pv.PreshapeScene(hand_xml, hand, pv.load_object(name), pv.load_preshapes())
    vertices, faces = gg.load_obj(scene.obj["dir"] / "mesh.obj")
    trials = []
    for index, grasp in enumerate(scene.obj["grasps"]):
        geometry = gg.part_features(vertices, faces, grasp["center"], pv.box_rotation(grasp))
        for preshape, flip, offset in itertools.product(scene.preshapes, FLIPS, OFFSETS_MM):
            scene.grasp_index = index
            scene.preshape_override = preshape
            scene.flipped = flip
            scene.offset = offset / 1000
            scene.reset()
            open_penetration = deepest_penetration_mm(scene)
            scene.close_fully()
            parts = contacts_by_part(scene)
            score, feasible, unmet = score_trial(preshape, open_penetration, parts)
            trials.append({
                "object": name,
                "category": scene.obj["category"],
                "grasp_index": index + 1,
                "grasp": grasp["name"],
                "label": grasp["preshape"],
                "preshape": preshape,
                "flip": flip,
                "offset_mm": offset,
                "open_penetration_mm": round(open_penetration, 1),
                "closest_contact_mm": round(min(parts.values()), 1) if parts else "",
                "touching": " ".join(sorted({f for f, _ in parts})),
                "contacts": " ".join(f"{f}:{s}" for f, s in sorted(parts)),
                "score": round(score, 3),
                "feasible": feasible,
                "unmet": "; ".join(unmet),
                **{f"geom_{k}": round(v, 1) for k, v in geometry.items()},
            })
    return trials


def lift_trial(scene, gravity):
    """Move the open hand forward until it touches the object (as a user would), close
    it on the free object without gravity, then turn gravity on, lift and shake.
    Returns (holds, slip in mm, open penetration in mm, advance in mm, reason)"""
    model, data = scene.model, scene.data
    model.opt.gravity[:] = 0
    start_offset = scene.offset
    scene.reset()
    open_penetration = deepest_penetration_mm(scene)
    if open_penetration > MAX_OPEN_PENETRATION_MM:
        return False, float("nan"), open_penetration, 0.0, "open hand in the object"
    while open_penetration <= TOUCH_PENETRATION_MM and scene.offset - start_offset < MAX_ADVANCE:
        scene.offset += ADVANCE_STEP
        scene.reset()
        open_penetration = deepest_penetration_mm(scene)
    # Back to the last placement not touching the object: an initial overlap would kick it
    while scene.offset > start_offset and deepest_penetration_mm(scene) > 0:
        scene.offset -= ADVANCE_STEP
        scene.reset()
    advance = 1000 * (scene.offset - start_offset)
    scene.offset = start_offset
    dofs = slice(scene.object_dof, scene.object_dof + 6)
    model.dof_damping[dofs] = [CLOSING_DAMPING[0]] * 3 + [CLOSING_DAMPING[1]] * 3
    scene.closing_since = data.time
    scene.step(pv.CLOSE_DURATION + 0.5)

    def object_in_palm():
        rot = data.xmat[scene.palm].reshape(3, 3)
        return rot.T @ (data.xpos[scene.object_body] - data.xpos[scene.palm])

    start = object_in_palm()
    model.dof_damping[dofs] = 0
    model.opt.gravity[:] = gravity
    up = -gravity / np.linalg.norm(gravity)
    base = data.mocap_pos[scene.mocap].copy()
    scene.step(SETTLE_DURATION)
    t0 = data.time
    total = LIFT_DURATION + SHAKE_DURATION
    while data.time - t0 < total:
        t = data.time - t0
        height = LIFT_HEIGHT * min(1.0, t / LIFT_DURATION)
        if t > LIFT_DURATION:
            height += SHAKE_AMPLITUDE * np.sin(2 * np.pi * SHAKE_FREQUENCY * (t - LIFT_DURATION))
        data.mocap_pos[scene.mocap] = base + up * height
        scene.step(0.005)
    slip = 1000 * float(np.linalg.norm(object_in_palm() - start))
    touching = any(scene.object_geom in (c.geom1, c.geom2) for c in data.contact[:data.ncon])
    if not touching:
        return False, slip, open_penetration, advance, "dropped"
    if slip > MAX_SLIP_MM:
        return False, slip, open_penetration, advance, "slipped"
    return True, slip, open_penetration, advance, ""


def lift_object(args):
    """The lift trials of one object (run in a worker process)"""
    name, menagerie, hand, placements, config = args
    hand_xml = Path(menagerie) / "shadow_hand" / f"{hand}_hand.xml"
    scene = pv.PreshapeScene(hand_xml, hand, pv.load_object(name), pv.load_preshapes(), mass=config["mass"])
    scene.object_body = scene.model.body("object").id
    scene.object_dof = scene.model.body_dofadr[scene.object_body]
    rng = np.random.default_rng(config["seed"])
    results = []
    for index, grasp in enumerate(scene.obj["grasps"]):
        if grasp["preshape"] not in scene.preshapes or (index + 1) not in placements:
            continue
        flip, offset = placements[index + 1]
        scene.grasp_index = index
        scene.preshape_override = grasp["preshape"]
        scene.flipped = flip
        scene.offset = offset / 1000
        # Objects put upside down in Unity ([usd]) are grasped upside down
        gravity = np.array([0, 0, 9.81 if "[usd]" in grasp["name"] else -9.81])
        for trial in range(config["trials"] + 1):
            if trial == 0:            # the nominal placement
                position, rotation = np.zeros(3), np.zeros(3)
            else:
                position = rng.uniform(-1, 1, 3) * config["position_error"] / 1000
                axis = rng.normal(size=3)
                rotation = axis / np.linalg.norm(axis) * np.radians(rng.uniform(-1, 1) * config["rotation_error"])
            scene.position_error, scene.rotation_error = position, rotation
            holds, slip, penetration, advance, reason = lift_trial(scene, gravity)
            results.append({
                "object": name,
                "grasp_index": index + 1,
                "grasp": grasp["name"],
                "preshape": grasp["preshape"],
                "trial": trial,
                "position_error_mm": " ".join(f"{1000 * v:.1f}" for v in position),
                "rotation_error_deg": round(float(np.degrees(np.linalg.norm(rotation))), 1),
                "open_penetration_mm": round(penetration, 1),
                "advance_mm": round(advance),
                "slip_mm": round(slip, 1) if slip == slip and reason != "dropped" else "",
                "holds": holds,
                "reason": reason,
            })
    return results


def add_lift(rows, lift):
    """Per grasp: whether the nominal placement holds, and the rate of the perturbed ones"""
    by_grasp = defaultdict(list)
    for result in lift:
        by_grasp[(result["object"], result["grasp_index"])].append(result)
    for r in rows:
        results = by_grasp.get((r["object"], r["grasp_index"]))
        if not results:
            continue
        nominal = [x for x in results if x["trial"] == 0][0]
        perturbed = [x for x in results if x["trial"] > 0]
        r["lift_nominal"] = nominal["holds"]
        r["lift_rate"] = round(sum(x["holds"] for x in perturbed) / max(1, len(perturbed)), 2)
        r["lift_failures"] = ", ".join(f"{reason} {count}" for reason, count in
                                       Counter(x["reason"] for x in perturbed if not x["holds"]).most_common())


def summarize(trials):
    """Per grasp: the best trial of each preshape, the prediction and the issues"""
    grasps = defaultdict(list)
    for trial in trials:
        grasps[(trial["object"], trial["grasp_index"])].append(trial)
    rows = []
    for (name, index), group in sorted(grasps.items()):
        best = {}
        for trial in group:
            if trial["preshape"] not in best or trial["score"] > best[trial["preshape"]]["score"]:
                best[trial["preshape"]] = trial
        label = group[0]["label"]
        predicted = max(best, key=lambda p: best[p]["score"])
        labeled = best.get(label)
        rows.append({
            "object": name,
            "category": group[0]["category"],
            "grasp_index": index,
            "grasp": group[0]["grasp"],
            "label": label,
            "label_score": labeled["score"] if labeled else "",
            "label_feasible": labeled["feasible"] if labeled else "",
            "label_best_variation": (f"flip={labeled['flip']} offset={labeled['offset_mm']}mm"
                                     if labeled else ""),
            "label_flip": labeled["flip"] if labeled else "",
            "label_offset_mm": labeled["offset_mm"] if labeled else "",
            "label_issues": labeled["unmet"] if labeled else "preshape not in shadow_preshapes.json",
            "predicted": predicted,
            "predicted_score": best[predicted]["score"],
            "agree": predicted == label,
            **{f"score_{p}": best[p]["score"] for p in sorted(best)},
            "feasible_preshapes": " ".join(sorted(p for p in best if best[p]["feasible"])),
            **{k: group[0][f"geom_{k}"] for k in GEOMETRY},
        })
    return rows


def calibrate(rows):
    """Leave-one-object-out predictions of the geometry and geometry + physics trees,
    added to rows; returns the accuracies and the trees fitted on all the grasps"""
    from sklearn.tree import DecisionTreeClassifier, export_text

    preshapes = sorted({k[len("score_"):] for r in rows for k in r if k.startswith("score_")})
    labels = np.array([r["label"] for r in rows])
    groupings = {"object": np.array([r["object"] for r in rows]),
                 "category": np.array([str(r["category"]) for r in rows])}
    feature_sets = {
        "geometry": GEOMETRY,
        "geometry + physics": GEOMETRY + [f"score_{p}" for p in preshapes],
    }
    # accuracy[predictor] = (leave-one-object-out, leave-one-category-out)
    physics = float(np.mean([r["predicted"] == r["label"] for r in rows]))
    accuracy = {"physics": (physics, physics)}
    baseline = []
    for groups in groupings.values():
        hits = []
        for held_out in np.unique(groups):
            train = groups != held_out
            hits += list(labels[~train] == Counter(labels[train]).most_common(1)[0][0])
        baseline.append(float(np.mean(hits)))
    accuracy["baseline"] = tuple(baseline)

    trees = {}
    for name, features in feature_sets.items():
        x = np.array([[float(r.get(f) or 0.0) for f in features] for r in rows])
        scores = []
        for grouping, groups in groupings.items():
            predictions = np.empty(len(rows), dtype=object)
            for held_out in np.unique(groups):
                train = groups != held_out
                tree = DecisionTreeClassifier(max_depth=TREE_DEPTH, min_samples_leaf=2, random_state=0)
                tree.fit(x[train], labels[train])
                predictions[~train] = tree.predict(x[~train])
            if grouping == "object":
                for r, prediction in zip(rows, predictions):
                    r[f"predicted_{name.replace(' + ', '_')}"] = prediction
            scores.append(float(np.mean(predictions == labels)))
        accuracy[name] = tuple(scores)
        tree = DecisionTreeClassifier(max_depth=TREE_DEPTH, min_samples_leaf=2, random_state=0)
        tree.fit(x, labels)
        trees[name] = export_text(tree, feature_names=features, decimals=1)
    return accuracy, trees


def write_csv(path, rows):
    if not rows:
        return
    fields = list(dict.fromkeys(k for row in rows for k in row))
    with open(path, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)


def lift_report(rows, config):
    tested = [r for r in rows if "lift_rate" in r]
    if not tested:
        return []
    nominal = sum(r["lift_nominal"] for r in tested)
    mean_rate = np.mean([r["lift_rate"] for r in tested])
    lines = [
        "## Lift test",
        "",
        f"Labeled preshape, best placement of the check, object of {config['mass']} kg released under "
        f"gravity, lifted by {100 * LIFT_HEIGHT:.0f} cm and shaken (+-{100 * SHAKE_AMPLITUDE:.0f} cm, "
        f"{SHAKE_FREQUENCY:.0f} Hz). Perturbed: {config['trials']} placements per box with up to "
        f"+-{config['position_error']:.0f} mm along each box axis and +-{config['rotation_error']:.0f} degrees. "
        f"Holds = moved less than {MAX_SLIP_MM:.0f} mm in the hand and still touching it.",
        "",
        f"- Nominal placement holds: **{nominal}/{len(tested)}** boxes",
        f"- Perturbed placements holding, mean over the boxes: **{100 * mean_rate:.0f}%**",
        "",
        "| Preshape | Boxes | Nominal holds | Perturbed holding (mean) |",
        "|---|---|---|---|",
    ]
    for preshape in sorted({r["label"] for r in tested}):
        group = [r for r in tested if r["label"] == preshape]
        lines.append(f"| {preshape} | {len(group)} | {sum(r['lift_nominal'] for r in group)} | "
                     f"{100 * np.mean([r['lift_rate'] for r in group]):.0f}% |")
    lines += [
        "",
        "Per box, least robust first:",
        "",
        "| Object | Grasp | Label | Nominal | Perturbed holding | Failures (perturbed) |",
        "|---|---|---|---|---|---|",
    ]
    for r in sorted(tested, key=lambda r: (r["lift_rate"], r["lift_nominal"])):
        lines.append(f"| {r['object']} | {r['grasp_index']}. `{r['grasp']}` | {r['label']} | "
                     f"{'holds' if r['lift_nominal'] else 'fails'} | {100 * r['lift_rate']:.0f}% | "
                     f"{r['lift_failures'] or '-'} |")
    return lines + [""]


def write_report(path, rows, elapsed, accuracy, trees, lift_config=None):
    preshapes = sorted({r["label"] for r in rows} | {r["predicted"] for r in rows})
    agree = sum(r["agree"] for r in rows)
    feasible = sum(r["label_feasible"] is True for r in rows)
    lines = [
        "# Grasp check report",
        "",
        f"{len(rows)} grasp boxes of {len({r['object'] for r in rows})} objects, "
        f"{len(FLIPS) * len(OFFSETS_MM)} hand placements per preshape, {elapsed / 60:.1f} min.",
        "",
        "Thresholds: open hand at most "
        f"{MAX_OPEN_PENETRATION_MM:.0f} mm in the object, closest contact at most "
        f"{MAX_CONTACT_TO_BOX_MM:.0f} mm from the box. Needs of each preshape once closed: "
        + "; ".join(f"`{p}` thumb + {n['count']} of {'+'.join(n['fingers'])}, palm {n['palm']}"
                    for p, n in REQUIREMENTS.items()) + ".",
        "",
        "## Check of the labeled preshapes",
        "",
        f"{feasible}/{len(rows)} grasp boxes are feasible with their labeled preshape "
        "(all its needs met in at least one placement). The others, worst first:",
        "",
        "| Object | Grasp | Label | Score | Issues of the best placement | Feasible preshapes |",
        "|---|---|---|---|---|---|",
    ]
    issues = [r for r in rows if r["label_feasible"] is not True]
    for r in sorted(issues, key=lambda r: r["label_score"] if r["label_score"] != "" else -1):
        lines.append(f"| {r['object']} | {r['grasp_index']}. `{r['grasp']}` | {r['label']} | "
                     f"{r['label_score']} | {r['label_issues']} | {r['feasible_preshapes'] or '-'} |")

    lines += [
        "",
        *(lift_report(rows, lift_config) if lift_config else []),
        "## Calibration: automatic preshape choice vs labeled preshape",
        "",
        "Accuracy when each object (or each whole category) is predicted by a model fitted on the "
        "other objects (categories) only:",
        "",
        "| Predictor | Leave one object out | Leave one category out |",
        "|---|---|---|",
        *[f"| {name} | {100 * a:.0f}% | {100 * b:.0f}% |" for name, (a, b) in accuracy.items()],
        "",
        "`baseline` always predicts the most frequent preshape of the training objects; `physics` the "
        "preshape with the best score (no fitting). Leaving one object out is optimistic, since similar "
        "objects (e.g. the GSO mugs, whose grasps were copied from each other) stay in the training set; "
        "leaving one category out is closer to a really new kind of object.",
        "",
        "Decision trees fitted on all the grasps (features in mm; `score_*` the best score of a preshape):",
        "",
        *[line for name, text in trees.items() for line in (f"**{name}**", "", "```", text.rstrip(), "```", "")],
        f"### Physics only: {agree}/{len(rows)} ({100 * agree / max(1, len(rows)):.0f}%)",
        "",
        "Confusion matrix (rows: label, columns: prediction):",
        "",
        "| label \\ predicted | " + " | ".join(preshapes) + " |",
        "|---|" + "---|" * len(preshapes),
    ]
    confusion = Counter((r["label"], r["predicted"]) for r in rows)
    for label in preshapes:
        lines.append(f"| {label} | " + " | ".join(str(confusion[(label, p)]) for p in preshapes) + " |")
    lines += [
        "",
        "## Per grasp",
        "",
        "Geometry of the part at the box (mm): `d1` x `d2` its cross-section, `L` its length along the "
        "palm width; predictions leaving one object out (`*` = differs from the label).",
        "",
        "| Object | Grasp | Label | d1 x d2 (local) | L | Physics | Geometry | Geometry + physics |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for r in rows:
        def mark(p):
            return p if p == r["label"] else f"{p} *"
        lines.append(
            f"| {r['object']} | {r['grasp_index']}. `{r['grasp']}` | {r['label']} | "
            f"{r['d1']:.0f} x {r['d2']:.0f} ({r['d1_local']:.0f} x {r['d2_local']:.0f}) | {r['length']:.0f} | "
            f"{mark(r['predicted'])} | {mark(r['predicted_geometry'])} | {mark(r['predicted_geometry_physics'])} |")
    Path(path).write_text("\n".join(lines) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("objects", nargs="*", help="objects to check (default: all the exported ones)")
    parser.add_argument("--menagerie", type=Path, default=pv.DEFAULT_MENAGERIE)
    parser.add_argument("--hand", choices=["right", "left"], default="right")
    parser.add_argument("--workers", type=int, default=max(1, min(4, (os.cpu_count() or 2) // 2)),
                        help="parallel processes (each one loads an object; default %(default)s)")
    parser.add_argument("--out", type=Path, default=pv.EXPORTED / "_check")
    parser.add_argument("--lift", action="store_true", help="also run the lift and perturbation test")
    parser.add_argument("--lift-trials", type=int, default=10, help="perturbed placements per box")
    parser.add_argument("--position-error", type=float, default=10.0, help="mm along each box axis")
    parser.add_argument("--rotation-error", type=float, default=15.0, help="degrees")
    parser.add_argument("--mass", type=float, default=0.2, help="object mass (kg) in the lift test")
    parser.add_argument("--seed", type=int, default=0)
    args = parser.parse_args()

    names = [pv.resolve_object(o) for o in args.objects] if args.objects else pv.list_objects()
    if None in names:
        sys.exit(1)
    if not (args.menagerie / "shadow_hand").exists():
        sys.exit(f"{args.menagerie / 'shadow_hand'} not found: pass --menagerie")
    args.out.mkdir(parents=True, exist_ok=True)

    start = time.time()
    trials = []
    jobs = [(name, str(args.menagerie), args.hand) for name in names]
    with multiprocessing.Pool(args.workers) as pool:
        for i, result in enumerate(pool.imap_unordered(check_object, jobs), 1):
            trials += result
            if result:
                print(f"[{i}/{len(names)}] {result[0]['object']}: {len({t['grasp_index'] for t in result})} grasps")
            else:
                print(f"[{i}/{len(names)}] (an object without grasps)")
    elapsed = time.time() - start

    rows = summarize(trials)
    accuracy, trees = calibrate(rows)
    lift_config = None
    if args.lift:
        lift_config = {"trials": args.lift_trials, "position_error": args.position_error,
                       "rotation_error": args.rotation_error, "mass": args.mass, "seed": args.seed}
        placements = defaultdict(dict)
        for r in rows:
            if r["label_flip"] != "":
                placements[r["object"]][r["grasp_index"]] = (r["label_flip"], r["label_offset_mm"])
        jobs = [(name, str(args.menagerie), args.hand, placements[name], lift_config)
                for name in names if placements.get(name)]
        lift = []
        lift_start = time.time()
        with multiprocessing.Pool(args.workers) as pool:
            for i, result in enumerate(pool.imap_unordered(lift_object, jobs), 1):
                lift += result
                print(f"[lift {i}/{len(jobs)}] {result[0]['object'] if result else ''}")
        print(f"Lift test: {len(lift)} trials in {(time.time() - lift_start) / 60:.1f} min")
        add_lift(rows, lift)
        write_csv(args.out / "lift.csv", lift)
        elapsed = time.time() - start
    write_csv(args.out / "trials.csv", trials)
    write_csv(args.out / "grasps.csv", rows)
    write_report(args.out / "report.md", rows, elapsed, accuracy, trees, lift_config)
    feasible = sum(r["label_feasible"] is True for r in rows)
    print(f"\n{len(rows)} grasps in {elapsed / 60:.1f} min: {feasible} feasible with their labeled preshape")
    print("Accuracy (leave one object out / one category out): "
          + ", ".join(f"{k} {100 * a:.0f}/{100 * b:.0f}%" for k, (a, b) in accuracy.items()))
    if args.lift:
        tested = [r for r in rows if "lift_rate" in r]
        print(f"Lift: nominal placement holds for {sum(r['lift_nominal'] for r in tested)}/{len(tested)} boxes, "
              f"perturbed placements hold {100 * np.mean([r['lift_rate'] for r in tested]):.0f}% on average")
    print(f"Report: {args.out / 'report.md'}")


if __name__ == "__main__":
    main()
