#!/usr/bin/env python3
"""Measure approach angles of the YCB grasp boxes, offline, from the Unity YAML and the OBJ meshes.

Reproduces the check of WristCameraMovement.cs (~617-677): the closest collider on the line from
the camera to the box centre must be the box. Non-convex MeshColliders are one-sided
(m_QueriesHitBackfaces: 0). Unity's OBJ importer mirrors x and reverses the winding.

Usage: measure_grasp_boxes.py PROJECT {test|validate|run} [--out OUT] [--mc N]
"""
import argparse, csv, glob, json, multiprocessing as mp, os, re, sys
import numpy as np

CUBE_GUID = "0000000000000000e000000000000000"

# --- Scene values, Assets/Scenes/Data_collection.unity and Assets/Prefabs/Table/Table_New.prefab
DATASET = ["001_chips_can", "006_mustard_bottle", "010_potted_meat_can", "011_banana", "018_plum",
           "019_pitcher_base", "025_mug", "029_plate", "031_spoon", "033_spatula", "037_scissors",
           "040_large_marker", "048_hammer", "055_baseball", "red_wood_block"]
WRIST_POS = np.array([57.708, 3.472, -0.16])
WRIST_ROT = np.array([0.4968264, 0.5031535, -0.4968267, 0.50315344])
WRIST_SCALE = np.array([0.3182059, 1, 0.122653715])
TABLE_POS = np.array([62.45218, 2.0, -0.21820235])
TABLE_SCALE = 0.4
PLANE_LOCAL_POS = np.array([-0.10499954, 1.3300002, 0.0])
PLANE_LOCAL_SCALE = np.array([0.3367629, 1, 0.15242413])


def qmat(q):
    x, y, z, w = np.asarray(q, float) / np.linalg.norm(q)
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])


def trs(p, q, s):
    m = np.eye(4)
    m[:3, :3] = qmat(q) @ np.diag(s)
    m[:3, 3] = p
    return m


def vec(text, keys="xyz"):
    return [float(re.search(rf"\b{k}: *(-?[\d.eE+-]+)", text).group(1)) for k in keys]


def parse_prefab(path):
    """Return dict(name, mesh info, root matrix, textured matrix, boxes)."""
    docs = {}
    for d in re.split(r"^--- ", open(path).read(), flags=re.M)[1:]:
        m = re.match(r"!u!(\d+) &(-?\d+)", d)
        docs[m.group(2)] = (m.group(1), d)
    gos = {k: re.search(r"m_Name: (.*)", d).group(1).strip() for k, (c, d) in docs.items() if c == "1"}
    comp = {}
    for k, (c, d) in docs.items():
        g = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", d)
        if g:
            comp.setdefault(g.group(1), []).append((c, k, d))

    def tf(k):
        d = docs[k][1]
        return dict(go=re.search(r"m_GameObject: \{fileID: (-?\d+)", d).group(1),
                    father=re.search(r"m_Father: \{fileID: (-?\d+)", d).group(1),
                    m=trs(vec(re.search(r"m_LocalPosition: (.*)", d).group(1)),
                          vec(re.search(r"m_LocalRotation: (.*)", d).group(1), "xyzw"),
                          vec(re.search(r"m_LocalScale: (.*)", d).group(1))),
                    pos=vec(re.search(r"m_LocalPosition: (.*)", d).group(1)),
                    rot=vec(re.search(r"m_LocalRotation: (.*)", d).group(1), "xyzw"),
                    scale=vec(re.search(r"m_LocalScale: (.*)", d).group(1)))

    tfs = {k: tf(k) for k, (c, d) in docs.items() if c == "4"}
    root_k = next(k for k, t in tfs.items() if t["father"] == "0")
    tex_k = next(k for k, t in tfs.items() if t["father"] == root_k and gos[t["go"]] == "textured")
    tex = tfs[tex_k]
    # Instantiation overwrites root position and rotation; the prefab scale stays.
    root_s = np.diag(tfs[root_k]["scale"] + [1.0])
    tex_m = root_s @ tex["m"]
    children = re.findall(r"- \{fileID: (-?\d+)\}", re.search(r"m_Children:(.*?)m_Father", docs[tex_k][1], re.S).group(1))
    boxes = []
    for i, ck in enumerate(children):
        t = tfs[ck]
        cols = [c for c in comp[t["go"]] if c[0] == "65"]
        if not cols:
            continue
        size = vec(re.search(r"m_Size: (.*)", cols[0][2]).group(1))
        center = vec(re.search(r"m_Center: (.*)", cols[0][2]).group(1))
        m = tex_m @ t["m"]
        axes = m[:3, :3]
        norm = np.linalg.norm(axes, axis=0)
        unit = axes / norm
        boxes.append(dict(index=i, name=gos[t["go"]], center=(m @ np.array(center + [1.0]))[:3],
                          axes=unit, half=norm * np.array(size) / 2,
                          ortho=float(np.abs(unit.T @ unit - np.eye(3)).max()),
                          local_scale=t["scale"]))
    tex_comp = comp[tex["go"]]
    mf = next(c for c in tex_comp if c[0] == "33")
    gr = re.search(r"m_Mesh: \{fileID: (-?\d+), guid: (\w+)", mf[2])
    mc = next((c for c in tex_comp if c[0] == "64"), None)
    col = None
    if mc:
        g = re.search(r"m_Mesh: \{fileID: (-?\d+), guid: (\w+)", mc[2])
        col = (g.group(1), g.group(2))
    return dict(name=os.path.basename(path)[:-7], tex_m=tex_m, boxes=boxes,
                render=(gr.group(1), gr.group(2)), collider=col,
                box_collider_on_textured=any(c[0] == "65" for c in tex_comp))


def guid_index(project):
    idx = {}
    for f in glob.glob(os.path.join(project, "Assets/3Dmodels/YCB/**/*.meta"), recursive=True):
        m = re.search(r"^guid: (\w+)", open(f, errors="ignore").read(4000), re.M)
        if m:
            idx[m.group(1)] = f[:-5]
    return idx


def load_obj_unity(path):
    """Vertices and faces as Unity sees them: x mirrored, winding reversed."""
    vs, fs = [], []
    for line in open(path, errors="ignore"):
        if line.startswith("v "):
            vs.append(line.split()[1:4])
        elif line.startswith("f "):
            ids = [int(p.split("/")[0]) for p in line.split()[1:]]
            ids = [i - 1 if i > 0 else len(vs) + i for i in ids]
            for a, b in zip(ids[1:-1], ids[2:]):
                fs.append((ids[0], b, a))
    v = np.array(vs, float)
    v[:, 0] *= -1
    return v, np.array(fs)


def cube_mesh():
    v = np.array([[x, y, z] for x in (-.5, .5) for y in (-.5, .5) for z in (-.5, .5)], float)
    f = np.array([[0, 1, 3], [0, 3, 2], [4, 6, 7], [4, 7, 5], [0, 4, 5], [0, 5, 1],
                  [2, 3, 7], [2, 7, 6], [0, 2, 6], [0, 6, 4], [1, 5, 7], [1, 7, 3]])
    return v, f


def object_mesh(project, pf, idx):
    guid = pf["render"][1]
    if guid == CUBE_GUID:
        v, f = cube_mesh()
    else:
        v, f = load_obj_unity(idx[guid])
    bounds_local = np.array([[x, y, z] for x in (v[:, 0].min(), v[:, 0].max())
                             for y in (v[:, 1].min(), v[:, 1].max())
                             for z in (v[:, 2].min(), v[:, 2].max())])
    m = pf["tex_m"]
    v = v @ m[:3, :3].T + m[:3, 3]
    corners = bounds_local @ m[:3, :3].T + m[:3, 3]
    return v, f, corners


class Tris:
    def __init__(self, v, f):
        self.a = v[f[:, 0]]
        self.e1 = v[f[:, 1]] - self.a
        self.e2 = v[f[:, 2]] - self.a
        self.n = np.cross(self.e1, self.e2)
        self.areas = np.linalg.norm(self.n, axis=1) / 2


def first_hit(tr, origins, dirs, front_only=True, chunk=48):
    """Closest t per ray (inf when none) and the index of the triangle hit. dirs are unit vectors."""
    origins, dirs = np.atleast_2d(origins), np.atleast_2d(dirs)
    best = np.full(len(origins), np.inf)
    who = np.full(len(origins), -1)
    if tr is None:
        return best, who
    for s in range(0, len(origins), chunk):
        o = origins[s:s + chunk, None, :]
        d = dirs[s:s + chunk, None, :]
        h = np.cross(d, tr.e2[None])
        det = np.sum(tr.e1[None] * h, axis=2)
        ok = det > 1e-16 if front_only else np.abs(det) > 1e-16
        with np.errstate(divide="ignore", invalid="ignore"):
            inv = 1.0 / det
            ss = o - tr.a[None]
            u = np.sum(ss * h, axis=2) * inv
            q = np.cross(ss, tr.e1[None])
            v = np.sum(d * q, axis=2) * inv
            t = np.sum(tr.e2[None] * q, axis=2) * inv
            hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-9)
        t = np.where(hit, t, np.inf)
        best[s:s + chunk] = t.min(axis=1)
        who[s:s + chunk] = t.argmin(axis=1)
    return best, who


def box_entry(origins, dirs, box):
    """Slab test. Entry t along dirs (inf on a miss); an origin inside the box does not hit, as in PhysX."""
    r = (origins - box["center"]) @ box["axes"]
    d = dirs @ box["axes"]
    with np.errstate(divide="ignore", invalid="ignore"):
        t1 = (-box["half"] - r) / d
        t2 = (box["half"] - r) / d
    lo = np.minimum(t1, t2)
    hi = np.maximum(t1, t2)
    lo = np.where(np.isnan(lo), -np.inf, lo)
    hi = np.where(np.isnan(hi), np.inf, hi)
    tn, tf = lo.max(axis=1), hi.min(axis=1)
    return np.where((tn <= tf) & (tn > 0), tn, np.inf)


def visible(tr, box, cams, plane=None):
    """Would the closest collider on camera -> box centre be the box?"""
    v = box["center"] - cams
    d = v / np.linalg.norm(v, axis=1, keepdims=True)
    tb = box_entry(cams, d, box)
    tm, _ = first_hit(tr, cams, d)
    res = np.isfinite(tb) & (tb < tm)
    if plane is not None:
        y0, xmin, xmax, zmin, zmax = plane
        with np.errstate(divide="ignore", invalid="ignore"):
            tp = (y0 - cams[:, 1]) / d[:, 1]
        px = cams[:, 0] + tp * d[:, 0]
        pz = cams[:, 2] + tp * d[:, 2]
        blocked = (d[:, 1] < 0) & (tp > 0) & (tp < tb) & (px >= xmin) & (px <= xmax) & (pz >= zmin) & (pz <= zmax)
        res &= ~blocked
    return res


def fibonacci(n):
    i = np.arange(n) + .5
    z = 1 - 2 * i / n
    p = np.pi * (1 + 5 ** .5) * i
    r = np.sqrt(1 - z * z)
    return np.c_[r * np.cos(p), r * np.sin(p), z]


def sweep_limit(tr, box, s, phi_deg, far=5.0):
    """Largest continuous tilt from the outward z axis towards azimuth phi. NaN when the axis is blocked."""
    th = np.radians(np.arange(0, 180.5, 0.5))
    x, y, z = box["axes"].T
    phi = np.radians(phi_deg)
    u = np.cos(th)[:, None] * (s * z) + np.sin(th)[:, None] * (np.cos(phi) * x + np.sin(phi) * y)
    ok = visible(tr, box, box["center"] + far * u)
    if not ok[0]:
        return float("nan")
    fail = np.flatnonzero(~ok)
    return 180.0 if len(fail) == 0 else float(np.degrees(th[fail[0] - 1]))


def inside(tr, p):
    """Majority of three parity tests (the meshes may have small holes)."""
    votes = 0
    for d in np.array([[.5774, .5774, .5774], [-.2673, .5345, .8018], [.7071, -.7071, .01]]):
        n = 0
        o = p.copy()
        for _ in range(60):
            t, _w = first_hit(tr, o[None], d[None], front_only=False)
            if not np.isfinite(t[0]):
                break
            n += 1
            o = o + (t[0] + 1e-7) * d
        votes += n % 2
    return votes >= 2


def outer_depth(tr, box, s, far=1.0):
    """Signed distance from the centre to the outermost front face along s*z, seen from outside.

    Positive: the surface is beyond the centre, so the centre is under it. Negative: the surface is
    behind the centre. Also returns the median over the footprint, the share of the footprint over
    the object, and the tilt of the mean surface normal from s*z.
    """
    z, c = box["axes"][:, 2], box["center"]
    axis_t, _ = first_hit(tr, (c + s * far * z)[None], (-s * z)[None])
    d_axis = far - float(axis_t[0]) if np.isfinite(axis_t[0]) else None
    gx, gy = np.meshgrid(np.linspace(-.9, .9, 9), np.linspace(-.9, .9, 9))
    off = gx.ravel()[:, None] * box["half"][0] * box["axes"][:, 0] + gy.ravel()[:, None] * box["half"][1] * box["axes"][:, 1]
    t, w = first_hit(tr, c + off + s * far * z, np.tile(-s * z, (len(off), 1)))
    good = np.isfinite(t)
    if not good.any():
        return dict(d=d_axis, d_foot=None, cover=0.0, tilt=None)
    n = tr.n[w[good]]
    n = n / np.linalg.norm(n, axis=1, keepdims=True)
    mean = n.mean(axis=0)
    tilt = None
    if np.linalg.norm(mean) > 1e-6:
        tilt = float(np.degrees(np.arccos(np.clip(mean / np.linalg.norm(mean) @ (s * z), -1, 1))))
    return dict(d=d_axis, d_foot=float(np.median(far - t[good])), cover=float(good.mean()), tilt=tilt)


def euler_y_z(yaw_deg, z_deg):
    """Quaternion.Euler(0, y, z) as a matrix: Unity applies z, x, then y."""
    a, b = np.radians(yaw_deg), np.radians(z_deg)
    ry = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
    rz = np.array([[np.cos(b), -np.sin(b), 0], [np.sin(b), np.cos(b), 0], [0, 0, 1]])
    return ry @ rz


def scene():
    plane_pos = TABLE_POS + TABLE_SCALE * PLANE_LOCAL_POS
    ext = TABLE_SCALE * PLANE_LOCAL_SCALE * 5
    wc = np.array([[x, 0, z] for x in (-5, 5) for z in (-5, 5)], float) * WRIST_SCALE
    we = np.abs(wc @ qmat(WRIST_ROT).T).max(axis=0)
    return dict(plane_pos=plane_pos, ext_x=ext[0], ext_z=ext[2], wrist_ext_y=we[1], wrist_ext_z=we[2])


FIXED_X_ALWAYS = ("048_hammer", "029_plate")
FIXED_X_ADDUCTED = ("026_sponge", "037_scissors", "033_spatula")
SUPINATION_ONLY = (("small_diameter", "048_hammer"), ("medium_wrap", "010_potted_meat_can"),
                   ("large_diameter", "006_mustard_bottle"), ("large_diameter", "001_chips_can"),
                   ("prismatic_2fingers", "025_mug"), ("large_diameter", "025_mug"))


def signed_angle(frm, to, axis):
    frm = frm / np.linalg.norm(frm, axis=-1, keepdims=True)
    to = to / np.linalg.norm(to, axis=-1, keepdims=True)
    ang = np.degrees(np.arccos(np.clip(np.sum(frm * to, axis=-1), -1, 1)))
    return ang * np.sign(np.sum(axis * np.cross(frm, to), axis=-1))


def monte_carlo(tr, corners, obj, box, s, n, seed):
    """Sample as randomWristCameraPlacement / randomObjectPlacement / randomObjectRotation do."""
    sc = scene()
    rng = np.random.default_rng(seed)
    name = box["name"]
    z_flip = np.zeros(n)
    if "[usd]" in name:
        z_flip[:] = 180
    elif "[susd]" in name:
        z_flip = np.where(rng.random(n) > .5, 180.0, 0.0)
    fixed = obj in FIXED_X_ALWAYS or (obj in FIXED_X_ADDUCTED and "adducted_thumb" in name)
    xs = np.full(n, -sc["ext_x"] - .08) if fixed else rng.uniform(-sc["ext_x"], sc["ext_x"], n)
    zs = rng.uniform(-sc["ext_z"], sc["ext_z"], n)
    y_off = (corners[:, 1].max() - corners[:, 1].min()) / 2
    roots = sc["plane_pos"] + np.c_[xs, np.full(n, y_off), zs]
    yaws = rng.uniform(0, 360, n)
    cams = WRIST_POS + np.c_[np.zeros(n), rng.uniform(-sc["wrist_ext_y"], sc["wrist_ext_y"], n),
                             rng.uniform(-sc["wrist_ext_z"], sc["wrist_ext_z"], n)]
    # Work in the object frame: inverse-transform each camera and the table plane per sample.
    cl = np.empty((n, 3))
    Rs = np.empty((n, 3, 3))
    for i in range(n):
        Rs[i] = euler_y_z(yaws[i], z_flip[i])
        cl[i] = Rs[i].T @ (cams[i] - roots[i])
    ok = visible(tr, box, cl)
    # Table plane test, in world space, against the box entry in the object frame.
    centre_w = roots + np.einsum("ijk,k->ij", Rs, box["center"])
    d_w = centre_w - cams
    L = np.linalg.norm(d_w, axis=1)
    d_w /= L[:, None]
    d_l = (box["center"] - cl) / L[:, None]
    tb = box_entry(cl, d_l, box)
    with np.errstate(divide="ignore", invalid="ignore"):
        tp = (sc["plane_pos"][1] - cams[:, 1]) / d_w[:, 1]
    px = cams[:, 0] + tp * d_w[:, 0]
    pz = cams[:, 2] + tp * d_w[:, 2]
    blocked = (d_w[:, 1] < 0) & (tp > 0) & (tp < tb) \
        & (np.abs(px - sc["plane_pos"][0]) <= sc["ext_x"]) & (np.abs(pz - sc["plane_pos"][2]) <= sc["ext_z"])
    ok &= ~blocked
    out = dict(pass_rate=float(ok.mean()), n_pass=int(ok.sum()))
    if not ok.any():
        return out
    u = (cl[ok] - box["center"])
    u /= np.linalg.norm(u, axis=1, keepdims=True)
    th = np.degrees(np.arccos(np.clip(u @ (s * box["axes"][:, 2]), -1, 1)))
    out["frac_plus"] = float((u @ box["axes"][:, 2] > 0).mean())
    out.update(th_p5=float(np.percentile(th, 5)), th_med=float(np.median(th)), th_p95=float(np.percentile(th, 95)),
               th_max=float(th.max()),
               elev_med=float(np.median(np.degrees(np.arcsin(d_w[ok, 1])))))
    # pronation-supination angle, lines ~687-723, before the 0.7-1 scaling
    if "[nops]" not in name:
        f = d_w[ok]
        up = np.array([0.0, 1.0, 0.0])
        right = np.cross(up, f)
        bx = np.einsum("ijk,k->ij", Rs[ok], box["axes"][:, 0])
        ps = signed_angle(right, bx, up)
        forced = "adducted_thumb" in name or any(g in name and o in obj for g, o in SUPINATION_ONLY)
        if forced:
            ps = np.where(ps > 0, -ps, ps)
        else:
            ps = np.where(np.abs(ps) > 90, ps - np.sign(ps) * 180, ps)
            ps = -ps
        out.update(ps_p5=float(np.percentile(ps, 5)), ps_med=float(np.median(ps)), ps_p95=float(np.percentile(ps, 95)))
    return out


def measure_box(args):
    project, obj, bidx, mesh_on, mc_n = args
    pf = parse_prefab(os.path.join(project, "Assets/Prefabs/YCB", obj + ".prefab"))
    v, f, corners = object_mesh(project, pf, guid_index(project))
    tr = Tris(v, f) if mesh_on else None
    box = pf["boxes"][bidx]
    far = 5.0
    # coverage: one Fibonacci sphere, split by the sign of box z
    dirs = fibonacci(4000)
    ok = visible(tr, box, box["center"] + far * dirs)
    zc = dirs @ box["axes"][:, 2]
    fp, fm = float(ok[zc > 0].mean()), float(ok[zc < 0].mean())
    # approach side = the side of z the accepted views come from; geometry only breaks a tie
    tr_real = Tris(v, f)
    sides = {sg: outer_depth(tr_real, box, sg) for sg in (1.0, -1.0)}
    seed = 12345 + 97 * bidx + sum(map(ord, obj))
    probe = monte_carlo(tr, corners, obj, box, 1.0, mc_n, seed=seed)
    frac_plus = probe.get("frac_plus", 0.5)
    if abs(frac_plus - .5) > .1:
        s = 1.0 if frac_plus > .5 else -1.0
    else:
        key = lambda sg: sides[sg]["d"] if sides[sg]["d"] is not None and sides[sg]["cover"] >= .5 else np.inf
        s = 1.0 if key(1.0) <= key(-1.0) else -1.0
    info = sides[s]
    d = info["d"] if info["d"] is not None else info["d_foot"]
    centre_inside = inside(tr_real, box["center"])
    a, b, c = box["half"]
    pred_x = pred_y = None
    if d is not None:
        pred_x = float(np.degrees(np.arctan2(a, d))) if d > 0 else 90.0
        pred_y = float(np.degrees(np.arctan2(b, d))) if d > 0 else 90.0
    lims = {k: sweep_limit(tr, box, s, p) for k, p in dict(xp=0, yp=90, xm=180, ym=270).items()}
    omega = float(ok[(zc * s) > 0].mean()) * 2 * np.pi   # on the outward hemisphere only
    alpha = float(np.degrees(np.arccos(max(-1.0, 1 - omega / (2 * np.pi)))))
    mc = monte_carlo(tr, corners, obj, box, s, mc_n, seed=seed)
    z = box["axes"][:, 2]
    return dict(object=obj, in_dataset=obj in DATASET, index=bidx, box=box["name"], mesh_blocks=mesh_on,
                a_mm=a * 1e3, b_mm=b * 1e3, c_mm=c * 1e3, d_mm=None if d is None else d * 1e3,
                d_axis_mm=None if info["d"] is None else info["d"] * 1e3,
                d_foot_mm=None if info["d_foot"] is None else info["d_foot"] * 1e3, footprint_cover=info["cover"],
                centre_inside=centre_inside, outward=("+z" if s > 0 else "-z"),
                pred_x=pred_x, pred_y=pred_y, lim_xp=lims["xp"], lim_xm=lims["xm"], lim_yp=lims["yp"], lim_ym=lims["ym"],
                visible_outer_hemi=fp if s > 0 else fm, visible_inner_hemi=fm if s > 0 else fp,
                alpha_eq=alpha, normal_tilt=info["tilt"],
                z_vs_object_up=float(np.degrees(np.arccos(np.clip(abs(z[1]), 0, 1)))),
                ortho=box["ortho"], **mc)


def test():
    """Flat surface z=0, normal +z, large: the pyramid formula of section 3 must come out."""
    v = np.array([[-1, -1, 0], [1, -1, 0], [1, 1, 0], [-1, 1, 0]], float)
    tr = Tris(v, np.array([[0, 1, 2], [0, 2, 3]]))
    cases = [(5e-3, 10e-3, 10e-3, 5e-3), (10e-3, 5e-3, 10e-3, 5e-3), (7e-3, 3e-3, 20e-3, 10e-3)]
    bad = 0
    for a, b, c, d in cases:
        box = dict(center=np.array([0, 0, -d]), axes=np.eye(3), half=np.array([a, b, c]))
        lx = sweep_limit(tr, box, 1.0, 0, far=1.0)
        ly = sweep_limit(tr, box, 1.0, 90, far=1.0)
        px, py = np.degrees(np.arctan2(a, d)), np.degrees(np.arctan2(b, d))
        print(f"a={a*1e3:g} b={b*1e3:g} c={c*1e3:g} d={d*1e3:g}: x {lx:.1f} vs {px:.1f}, y {ly:.1f} vs {py:.1f}")
        bad += abs(lx - px) > 1 or abs(ly - py) > 1
    # c < d, fully buried on a flat surface: nothing may pass
    box = dict(center=np.array([0, 0, -20e-3]), axes=np.eye(3), half=np.array([10e-3, 10e-3, 5e-3]))
    dirs = fibonacci(2000)
    cams = box["center"] + dirs
    r = visible(tr, box, cams)
    above = cams[:, 2] > 0   # a camera below the one-sided surface sees its back face, which is not hit
    print("buried box, pass fraction with the camera above the surface:", r[above].mean())
    bad += r[above].mean() != 0
    # a closed cube: front hit from outside, parity inside
    v, f = cube_mesh()
    c = Tris(v, f)
    t, _ = first_hit(c, np.array([[0, 0, 5.0]]), np.array([[0, 0, -1.0]]))
    print("cube from +z: t =", t[0], "| signed volume", float(np.sum(np.einsum('ij,ij->i', c.a, c.n)) / 6))
    bad += abs(t[0] - 4.5) > 1e-9
    bad += not inside(c, np.zeros(3))
    bad += inside(c, np.array([2.0, 0, 0]))
    # euler convention: Unity Euler(0,90,0) sends +z to +x
    print("Euler(0,90,0)*z =", np.round(euler_y_z(90, 0) @ [0, 0, 1], 6))
    bad += not np.allclose(euler_y_z(90, 0) @ [0, 0, 1], [1, 0, 0])
    print("FAILED" if bad else "ALL OK")
    return bad


def validate(project):
    idx = guid_index(project)
    exp_dir = os.path.abspath(os.path.join(project, "../grasping_physics_simulation/assets/objects"))
    print("scene:", {k: np.round(v, 4).tolist() for k, v in scene().items()})
    for f in sorted(glob.glob(os.path.join(project, "Assets/Prefabs/YCB/*.prefab"))):
        pf = parse_prefab(f)
        v, tris, corners = object_mesh(project, pf, idx)
        tr = Tris(v, tris)
        vol = float(np.sum(np.einsum("ij,ij->i", tr.a, tr.n)) / 6)
        col = pf["collider"]
        status = "none(BoxCollider)" if pf["box_collider_on_textured"] and col is None else (
            "same as render" if col == pf["render"] else
            ("MISSING GUID" if col and col[1] not in idx else f"other asset {os.path.basename(idx[col[1]])}"))
        msg = f"{pf['name']:22s} boxes={len(pf['boxes'])} tris={len(tris):6d} vol={vol*1e6:9.1f}cm3 collider={status}"
        ej = os.path.join(exp_dir, pf["name"], "object.json")
        if os.path.exists(ej):
            e = json.load(open(ej))
            mine = np.c_[v[:, 0], v[:, 2], v[:, 1]]
            dmin = np.abs(mine.min(0) - e["bounds_min"]).max()
            dmax = np.abs(mine.max(0) - e["bounds_max"]).max()
            by = {}
            for g in e["grasps"]:
                by.setdefault(g["name"], []).append(np.array(g["center"]))
            dc = []
            for b in pf["boxes"]:
                c = np.array([b["center"][0], b["center"][2], b["center"][1]])
                if b["name"] in by:
                    dc.append(min(np.abs(c - x).max() for x in by[b["name"]]))
                else:
                    dc.append(float("nan"))
            msg += f" | exporter: bounds diff {max(dmin, dmax)*1e3:.2f} mm, box centre diffs mm {np.round(np.array(dc)*1e3, 2).tolist()}, n_exp={len(e['grasps'])}"
        print(msg, "| shear", max(b["ortho"] for b in pf["boxes"]))


def fmt(x, n=0):
    return "—" if x is None or (isinstance(x, float) and np.isnan(x)) else f"{x:.{n}f}"


def run(project, out, mc_n):
    jobs = []
    for f in sorted(glob.glob(os.path.join(project, "Assets/Prefabs/YCB/*.prefab"))):
        pf = parse_prefab(f)
        missing = pf["collider"] is not None and pf["collider"][1] not in guid_index(project)
        for i in range(len(pf["boxes"])):
            jobs.append((project, pf["name"], i, True, mc_n))
            if missing:   # as shipped, nothing blocks the view
                jobs.append((project, pf["name"], i, False, mc_n))
    with mp.Pool(max(1, (os.cpu_count() or 2) - 1)) as p:
        rows = []
        for r in p.imap(measure_box, jobs):
            rows.append(r)
            print(f"{r['object']:22s}{r['box']:32s} mesh={r['mesh_blocks']!s:5} "
                  f"d={fmt(r['d_mm'],1):>6} pred=({fmt(r['pred_x'])},{fmt(r['pred_y'])}) "
                  f"x=({fmt(r['lim_xp'])},{fmt(r['lim_xm'])}) y=({fmt(r['lim_yp'])},{fmt(r['lim_ym'])}) "
                  f"pass={r.get('pass_rate',0):.3f} th={fmt(r.get('th_med'))}", flush=True)
    os.makedirs(out, exist_ok=True)
    keys = []
    for r in rows:
        keys += [k for k in r if k not in keys]
    with open(os.path.join(out, "grasp_box_angles.csv"), "w", newline="") as h:
        w = csv.DictWriter(h, keys)
        w.writeheader()
        w.writerows(rows)
    json.dump(rows, open(os.path.join(out, "grasp_box_angles.json"), "w"), indent=1)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("project")
    ap.add_argument("cmd", choices=["test", "validate", "run"])
    ap.add_argument("--out", default=".")
    ap.add_argument("--mc", type=int, default=4000)
    a = ap.parse_args()
    if a.cmd == "test":
        sys.exit(1 if test() else 0)
    if a.cmd == "validate":
        validate(a.project)
    else:
        run(a.project, a.out, a.mc)
