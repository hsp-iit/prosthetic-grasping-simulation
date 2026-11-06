# Grasp boxes: how they shape the training data

Notes from 2026-11-05, measurements added 2026-11-06. Source checked: `prosthetic-grasping-simulation`, branch `main`,
commit `a866327` (GitHub fork `anufriievsergii-netizen/prosthetic-grasping-simulation`).
Local unpushed work (the MuJoCo `save` / importer, the third scissors box) was **not** checked.

## Summary

- The **label** of a video comes only from the grasp box's **name**. Geometry is never read by the labeler.
- The box's **geometry** (position, depth, x/y/z size, orientation) decides **which images and motion** get that label.
- So a badly placed box produces correct-looking label strings on wrong views or the wrong part.
  The data is silently mislabelled, and only inspecting the generated frames catches it.
- Neither grasp type nor local surface shape fully sets the angles. Grasp type sets the approach side, through how the boxes
  were placed, and hand-chosen box size and depth set much of the rest (see section 6).
- The scissors prefab has no working object collider, so the view check can't reject any scissors view (see section 7).

## 1. Label: name only

`Assets/Scripts/Data_collection/Labelers/GraspTypeLabeler.cs`, lines ~241–260:

- reads the active box's `gameObject.name`
- strips the tags `[nops]`, `[usd]`, `[susd]`, `[left]`, `[right]`
- maps the grasp type to a preshape through `graspType2preshape`

Size, position and orientation are not used.

## 2. What the geometry controls

`Assets/Scripts/Data_collection/WristCameraMovement.cs`

| Box property | Effect | Code |
|---|---|---|
| Two z ends (centre ± half-height along local z) | One end, chosen at random, is the **aim point**: the camera flies straight at it and stops 0.2 m short. The other end is the **wrist-bend target**: the camera rotates toward it from a sampled frame onwards. | ~729–762 (`targetUpPoint` / `targetDownPoint`), ~810 (fe rotation), `REFERENCE_TO_CAMERA_OFFSET = 0.2f` |
| Centre depth + x/y size | Which approach directions pass the visibility check (see section 3) | ~617–677 |
| Orientation (box local x) | Pronation-supination angle, from the camera's right vector vs `graspToExecute.transform.right` | ~687–723 |
| z size | Separation of the aim end and the wrist-bend end, so how much the camera turns during the bend | ~731 (`mesh.bounds.extents.z`) |

The camera does **not** travel along box z. Its start is random on `planeWristPlacement`, and the object gets a random yaw
(`randomObjectRotation`, ~344–383; z = 180° for `[usd]`, randomly for `[susd]`).

## 3. Visibility check → approach cone

Lines ~617–677: `Physics.Linecast(camera, box centre)`. The sample is kept only if the **first** collider hit is this grasp box
on this object. Otherwise the camera position, object placement and rotation are resampled, up to 1,000 times. After that the run stops with
`INVALID iteration: no feasible camera-object pose`.

Geometry, for a locally flat surface with box z along the surface normal:

- a, b = box half-sizes in x and y; c = half-height in z
- d = depth of the box centre below the surface (d > 0 means inside the object)

A direction passes if the ray to the centre goes through the box's footprint at the surface, the a × b rectangle.
That makes the allowed directions a **rectangular pyramid** with its apex at the centre and the footprint as its base:

- x–z plane: tan θx < a / d
- y–z plane: tan θy < b / d
- in general: d·tanθ·|cosφ| < a and d·tanθ·|sinφ| < b (θ from normal, φ azimuth)

| Quantity | Effect |
|---|---|
| d larger (deeper) | Narrower cone. Halving d roughly doubles tan θ. |
| a (x half-size) | More tilt allowed in the x–z plane |
| b (y half-size) | More tilt allowed in the y–z plane, independent of x |
| c (z half-height) | Doesn't shape the cone. It only needs c > d so the box reaches the surface. |
| d ≤ 0 (centre at or above the surface) | Nearly the full hemisphere passes, and x/y hardly matter |

Example: with the ends 5 mm outside and 15 mm inside, c = 10 mm and d = 5 mm. A half-width of 5 mm gives 45°, and 10 mm gives about 63°.

Caveats:
- A convex surface under the footprint widens the cone. A concave surface, or another part of the object in front, narrows it.
- Other grasp boxes do **not** block directions. All boxes are deactivated (~479) and only the target is activated (~612).
  (Corrected 2026-11-06: an earlier version of these notes said they did.)
- The **realised** distribution of views is this pyramid intersected with the directions reachable from the placement plane, given the object's yaw.
  A narrow pyramid shows up as many resamples, or `INVALID iteration`.
- A fully buried box (c < d) on a flat surface passes **nothing**. It only works where the surface curves away inside the footprint.

## 4. Discrepancies with the Visual Grasp Box Check doc

- The doc says "box size comes from the grasp type". On `main`, sizes are set **per box**. Scissors:
  `adducted_thumb` local scale ≈ (0.0069, 0.0084, 0.0138), `prismatic_4fingers` ≈ (0.0568, 0.0149, 0.0044).
  This may be true of the local MuJoCo `save` tool, which isn't on `main`.
- The doc lists 3 scissors boxes; the prefab on `main` (`Assets/Prefabs/YCB/037_scissors.prefab`) has 2.
- The doc describes only the aim role of the z ends. The other end is also the wrist-bend target.
- The doc says buried boxes give limited views. On a flat surface the check passes nothing at all (see section 3).

## 5. Measured angles per box

**Method.** Offline, not in Unity. Parsed the prefabs and `Data_collection.unity`, then reproduced the `Linecast` rule on the
OBJ meshes: closest collider wins, back faces are ignored (`m_QueriesHitBackfaces: 0`), and OBJ x is mirrored on import.
Each box got 4,000 sampled camera and object poses, drawn as in `randomWristCameraPlacement`, `randomObjectPlacement` and
`randomObjectRotation`.

**Checks.** Meshes and box centres match the MuJoCo exporter's output to 0.00 mm on 16 of 17 prefabs (see section 7 for the
block). The raycaster reproduces the section 3 formula on a synthetic flat surface. The numbers have not been compared against
PhysX in the Editor. The round table and lounge chair near the far end of the table are not modelled. The scripts are not in this repo.

**Columns.**
- a/b/c: box half-sizes.
- d: depth of the centre below the outermost surface along the approach side of box z. Negative means the centre is above
  that surface; for the mug's `sphere_4fingers[nops]` that is inside the cavity.
- Limits: largest tilt from the box axis towards ±x and ±y before the view is blocked. "—" means the axis itself is blocked.
- Formula: `atan(a/d)` / `atan(b/d)`, n/a when d ≤ 0.
- Realised: median angle between the view and the box axis, with the 5th–95th percentile.
- Pass: share of sampled poses that pass, including the table plane.

| Box | a/b/c mm | d mm | x± limit ° | y± limit ° | Formula x/y ° | Realised ° | Pass % |
|---|---|---:|---:|---:|---:|---:|---:|
| chips `sphere_4fingers[nops]` | 20.9/18.9/20.2 | 3.9 | 78/76 | 76/74 | 79/78 | 76 (73–78) | 20.1 |
| chips `large_diameter[right][susd]` | 40.0/15.9/18.0 | 17.4 | 66/67 | 50/51 | 66/42 | 28 (7–49) | 27.8 |
| chips `large_diameter[left][susd]` | 40.0/15.9/18.0 | 15.0 | 69/70 | 56/54 | 69/47 | 29 (10–53) | 30.2 |
| mustard `tripod[nops]` | 3.8/2.9/7.8 | 0.1 | 70/72 | 70/73 | 88/87 | 73 (72–73) | **0.9** |
| mustard `large_diameter[right]` | 29.2/4.6/10.4 | 3.4 | 72/68 | 54/57 | 83/54 | 30 (9–53) | 30.8 |
| mustard `large_diameter[left]` | 29.2/5.0/10.4 | 2.8 | 73/72 | 66/61 | 84/61 | 33 (11–61) | 35.1 |
| potted `medium_wrap[left][susd]` | 22.6/3.9/7.8 | 2.5 | 84/80 | 56/58 | 84/58 | 31 (9–55) | 33.2 |
| potted `sphere_4fingers` | 7.2/16.1/7.0 | 3.8 | 61/60 | 72/72 | 62/77 | 72 (71–73) | **1.6** |
| potted `medium_wrap[right][susd]` | 22.6/3.9/7.8 | 3.0 | 82/81 | 58/54 | 83/53 | 31 (9–55) | 30.0 |
| potted `sphere_4fingers[usd]` | 7.2/16.1/7.0 | 3.0 | 68/66 | 74/76 | 67/79 | 74 (71–77) | 9.1 |
| banana `prismatic_4fingers` | 27.9/19.5/13.6 | 13.5 | 20/60 | 2/130 | 64/55 | 79 (72–86) | 65.2 |
| banana `tripod` | 2.4/4.3/4.6 | 6.8 | — | — | 20/32 | 79 (72–86) | 40.8 |
| plum `power_sphere[nops]` | 28.1/27.4/10.9 | 9.1 | 112/112 | 115/112 | 72/72 | 80 (73–86) | 100 |
| pitcher `adducted_thumb` | 26.7/0.5/2.4 | 0.3 | 92/91 | 52/69 | 89/60 | 32 (9–63) | 32.7 |
| pitcher `prismatic_4fingers` | 32.2/3.3/2.6 | 2.4 | 72/92 | 54/52 | 86/53 | 81 (74–88) | 11.6 |
| mug `large_diameter[susd]` | 14.7/9.0/12.8 | 7.5 | 64/63 | 58/51 | 63/50 | 29 (10–54) | 30.9 |
| mug `sphere_4fingers[nops]` | 6.4/9.0/3.9 | −63.7 | 72/72 | 72/72 | n/a | 71 (70–72) | **2.2** |
| mug `prismatic_2fingers[susd]` | 3.7/2.4/3.7 | 2.7 | 54/57 | 42/42 | 53/42 | 25 (9–42) | 23.7 |
| mug `sphere_4fingers[nops][usd]` | 6.4/9.0/3.9 | 1.7 | 74/74 | 78/80 | 75/79 | 76 (72–79) | 33.5 |
| plate `adducted_thumb` | 5.7/23.0/6.4 | −21.9 | 160/38 | 122/126 | n/a | 42 (12–81) | 44.4 |
| plate `adducted_thumb[usd]` | 1.0/13.6/2.1 | 0.6 | 26/178 | 92/90 | 61/88 | 34 (12–69) | 35.9 |
| spoon `prismatic_4fingers` | 27.9/6.3/4.9 | 1.0 | 86/84 | 92/96 | 88/81 | 79 (71–87) | 98.4 |
| spatula `prismatic_4fingers` | 33.4/9.8/4.8 | 4.2 | 78/76 | 120/180 | 83/67 | 78 (67–90) | 91.3 |
| spatula `adducted_thumb[right]` | 1.2/1.4/5.8 | 5.2 | 14/13 | 15/15 | 13/15 | 13 (8–19) | 4.4 |
| spatula `adducted_thumb[left]` | 0.7/1.2/5.7 | 3.9 | 10/12 | 16/16 | 9/16 | 10 (4–17) | 10.1 |
| scissors `adducted_thumb`, collider working | 3.5/4.2/6.9 | 14.9 | — | — | 13/16 | 52 (29–81) | 15.1 |
| scissors `adducted_thumb`, **as on `main`** | same | same | 180/180 | 180/180 | — | **90 (18–164)** | **100** |
| scissors `prismatic_4fingers`, collider working | 28.4/7.4/2.2 | 1.7 | 63/87 | 97/84 | 87/77 | 79 (72–85) | 84.0 |
| scissors `prismatic_4fingers`, **as on `main`** | same | same | 180/180 | 180/180 | — | 79 (72–85) | **100** |
| marker `prismatic_4fingers` | 33.6/11.5/5.2 | 3.4 | 84/85 | 120/121 | 84/74 | 79 (72–86) | 99.1 |
| hammer `small_diameter` | 11.4/4.7/6.9 | 15.7 | — | — | 36/17 | 77 (69–85) | 23.9 |
| baseball `power_sphere[nops]` | 36.3/35.8/12.6 | 12.0 | 109/110 | 110/110 | 72/71 | 80 (73–86) | 100 |
| block `tripod[nops]` | 10.7/10.8/7.3 | 6.2 | 126/126 | 126/126 | 60/60 | 79 (72–86) | 100 |

This covers the 31 boxes in the scene's object list (31 × `numIterationsPerGrasp` 50 = `iterationCount` 1550).
`021_bleach_cleanser` and `026_sponge` are not in that list and are left out.

**What the table shows.**
- The camera limits the views more than the cones do. It starts 4–5 m away and always above the object, looking down by a
  median of 9–18°.
- Vertical-z boxes (17) are only seen from the side, at a median of about 79° off the box axis. The 5th percentile is at least 67°
  on all of them, so the inner part of the cone is never used.
- Horizontal-z boxes (14) get their spread from the random object yaw, with a median of about 30°. For most of them, the y limit sets the widest
  view and the pass rate (y limit / 180° ≈ pass rate, e.g. 50° → 28%).
- Mustard `tripod`, potted `sphere_4fingers` and mug `sphere_4fingers[nops]` reject almost every sample, and only the steepest camera
  positions pass. Mustard `tripod` gives about a 0.6% chance that `INVALID iteration` stops a full run (plausible range 0.02–7%,
  since only 36 of 4,000 samples passed).
- The section 3 formula is a good first estimate. For boxes with the centre under the surface and a clear axis, the median error
  is 4°, and 70 of 104 limits are within 10°. It fails where the box overhangs a curved part or edge (plum, baseball, block,
  marker, banana).
- Buried boxes confirm section 3. Banana `tripod`, scissors `adducted_thumb` and hammer have c < d and are blocked along their own
  axis. They pass only because the surface curves away.
- Pronation-supination, before the 0.7–1 scaling: nearly constant at about −90° on horizontal-z boxes, because their x axis is close to
  vertical. Most vertical-z boxes span up to about ±80°; mustard, potted meat and mug-type boxes vary less or skip it. Hammer reaches
  −130°, because the forced-supination branch (~694–708) has no 90° cap.

## 6. Do grasp type and local geometry set the angles?

**Mostly no.** Grasp type sets the approach side, through how the boxes were placed. Surface shape explains part of the cone width,
and the rest comes from how each box was sized and placed by hand.

**Grasp type.** `WristCameraMovement.cs` never derives a cone from it. Grasp names only affect object placement, the upside-down
tags and the pronation-supination rule.
- It does decide the z direction: every box of a grasp type has its z either vertical or horizontal, with no exceptions in the 31 boxes.
  That direction accounts for 92% of the variation in the median view angle.
- It doesn't decide the cone width: it accounts for only 29% of the variation in the y limit.

| Grasp type | Boxes | Box z | Tighter x limit ° | Tighter y limit ° | Pass % |
|---|---:|---|---:|---:|---:|
| `adducted_thumb` | 6 | horizontal | 10–91 | 15–122 | 4–44 |
| `prismatic_4fingers` | 6 | vertical | 20–84 | 2–120 | 12–99 |
| `tripod` | 3 | vertical | 70–126 | 70–126 | 1–100 |
| `sphere_4fingers` | 5 | vertical | 60–76 | 72–78 | 2–33 |
| `large_diameter` | 5 | horizontal | 63–72 | 50–61 | 28–35 |

Blocked-axis boxes are left out of the limits. Two types with a single box are not shown.

**Local geometry.** The formula using a, b and d only partly predicts the cone (r² = 0.39 on the tighter limits, for boxes with
the centre under the surface). Curvature, edges and nearby parts cause much of the rest. But a, b and d are themselves chosen by hand. Boxes that
look like left/right pairs on the same part differ: chips y limit 50° vs 54° (depths 17.4 and 15.0 mm), mustard 54° vs 61°.

**Effect on labels.** A cone never changes the label text. Each box runs in its own batch, so the schedule picks the label and the cone
only filters which views can carry it. That filter still shapes which views go with which label.
- A median of about 10% of passing poses would pass for two or more grasp types on the same object, so in most poses the view implies
  one grasp type.
- Some objects overlap much more: banana 39%, chips 22%, flipped mug 16%, scissors 12% (with a working collider), pitcher 11%.
- With the scissors as on `main`, every pose passes for both grasp types.

So a network can learn "this yaw range means this grasp", but the ranges partly reflect box sizes rather than what the Hannes hand can
tolerate. `adducted_thumb` allows about ±15° on the spatula and about ±120° on the plate.

**How firm this is.** The numbers are descriptive, not statistically firm. There are only 31 boxes, grasp type is tangled up with object,
and two types have a single box. The overlap test uses normal table placement for every box, ignoring the table-edge placement used for
some boxes (all plate boxes, plus `adducted_thumb` on spatula and scissors).

## 7. Other problems found

- **Scissors collider.** The `MeshCollider` in `037_scissors.prefab` points to GUID `1cf4d5d1ad0282a49a679a5f89d389b4`, which no asset
  has. The object can't block the view, so every sample passes. `adducted_thumb` passes 100% instead of about 15%, with views from
  both sides, including through the object. About 85% of its starting views would be rejected with a working collider, so existing
  scissors data is affected. Inferred from the dangling GUID, not yet seen at runtime in the Editor.
- **Box renderers.** The box `MeshRenderer` is enabled on banana, spatula and scissors (in the dataset), and on bleach and sponge (not in it).
  It is off on the others checked. The material has alpha 0, so the box shouldn't be plainly visible, but `m_CastShadows` is on. Whether
  the box shows up as a shadow or highlight needs checking in rendered frames.
- **Red block scale.** `red_wood_block` is a 2 cm cube in the Unity scene but 10 cm in the MuJoCo export, whose values match leaving out the
  prefab root's 0.2 scale. The exporter source isn't on `main`, so the cause is inferred.

## 8. Open questions to look at properly

1. If `save` gives every box of a grasp type the same x/y, then the angular tolerance is fixed per grasp type and
   only centre depth varies it. Is that intended? Should x/y encode a real, per-grasp tolerance of the Hannes preshape?
2. Should the approach cone be **explicit** (sample the camera start inside a chosen cone around box z) instead of a side effect of the ray check?
   Section 6 supports this: a tolerance per grasp type, with the `Linecast` kept only for occlusion. The tolerances still have to come from somewhere,
   ideally the hand's real tolerance.
3. Measure the realised approach-angle distribution per box from generated sequences (log camera direction vs box z at the start).
   Estimated offline in section 5; not yet confirmed from generated sequences.
4. The ray targets the box **centre**. `GetRandomPointInsideCollider` exists but is commented out (~633). Targeting random points
   would change the cone and the views.
5. Check box orientation (local x) for each box, since it directly sets the pronation-supination motion.
   Summarised in section 5; the per-box values have not been checked against the intended wrist motion.
6. Re-check the scissors boxes, and the other objects, against section 3 once the local importer work is pushed.
