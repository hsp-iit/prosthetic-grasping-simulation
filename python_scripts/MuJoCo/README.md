# MuJoCo preshape viewer

Checks the grasp parallelepipeds ("grasp boxes") of the Unity objects against hand preshapes, using the
[Shadow Hand](https://github.com/google-deepmind/mujoco_menagerie/tree/main/shadow_hand) of MuJoCo Menagerie.
For each grasp box of an object, the hand is placed where the wrist camera approaches it, opened in the
preshape of the grasp, and can be closed on the object. The terminal reports how the fingers end up
relative to the box and which parts of the hand touch the object.

It helps to answer questions such as: is this grasp box on the right part of the object? Is its
orientation the one I meant? Does the preshape fit the object part, or does the hand go through the
object?

Files:

| File | What it is |
|---|---|
| `preshape_viewer.py` | the viewer (interactive window, or images with `--render`) |
| `shadow_preshapes.json` | the Shadow Hand joint angles of each preshape |
| `exported/` | the objects exported from Unity (git-ignored, about 280 MB) |
| `Assets/Scripts/Data_collection/Editor/MujocoGraspExporter.cs` | the Unity exporter |

## 1. Setup (once)

Clone MuJoCo Menagerie next to this repository (only `shadow_hand/` is used) and install MuJoCo in a
virtual environment:

```
cd ~/PycharmProjects
git clone https://github.com/google-deepmind/mujoco_menagerie.git
python3 -m venv ~/.venvs/mujoco
~/.venvs/mujoco/bin/pip install mujoco numpy pillow scipy scikit-learn
```

If the Menagerie is somewhere else, pass `--menagerie /path/to/mujoco_menagerie` to the viewer.

## 2. Export the objects from Unity

In the Unity Editor: `Tools -> Grasp Objects -> Export Objects for MuJoCo`. The Console shows
`Exported N objects for MuJoCo`. Every prefab in `Assets/Prefabs/YCB` and `Assets/Prefabs/GSO` with a
`textured` child is written to `exported/<prefab name>/`:

- `mesh.obj`: the mesh of `textured`, in the frame of the prefab root;
- `texture.png`: its base color texture (when it is a PNG);
- `object.json`: category, bounds, and every grasp box (the `BoxCollider` children of `textured`) with its
  name, preshape, center, size and axes.

Coordinates are converted from Unity (left-handed, y up) to MuJoCo (right-handed, z up).

**Export again whenever you change, add or remove grasp boxes**, then restart the viewer.

## 3. Run the viewer

From the repository root:

```
~/.venvs/mujoco/bin/python python_scripts/MuJoCo/preshape_viewer.py --list
~/.venvs/mujoco/bin/python python_scripts/MuJoCo/preshape_viewer.py 025_mug
```

`--list` prints every exported object with its grasps and their preshapes. The object can be given by its
full name, by part of it (`plum`, `Hilary`) or by its number in `--list`.

A window opens with the object, its grasp boxes and the hand on the first box:

- the **current box is green**, the other boxes are orange;
- the **green arrow** is the approach direction, ending at the box center;
- orange markers on the hand are the contact points with the object.

Mouse: left drag rotates, right drag moves, scroll zooms, double click selects a body.
Press `T` to make the object transparent and see the boxes inside it (e.g. in the mouth of a mug).

### Keys (in the viewer window; click it first)

| Key | Action |
|---|---|
| `Page Up` / `Page Down` | next / previous grasp box (`Fn` + `Up` / `Down` on most laptops) |
| `Up` / `Down` | next / previous preshape (`auto` = the preshape of the grasp) |
| `Enter` | close the hand on the object / back to the open preshape |
| `Home` | turn the hand by 180 degrees around the approach direction |
| `End` | approach from the other end of the box |

### Commands (type in the terminal, then Enter)

The terminal commands always work, also when a key does not reach the viewer.

| Command | Action |
|---|---|
| `n` / `p` | next / previous grasp box |
| `g 3` | go to grasp box 3 |
| `s` | list the preshapes |
| `s pinch_3` | use this preshape for every box (`s auto` = back to the preshape of each grasp) |
| `c` | close the hand / back to the open preshape |
| `f` | turn the hand by 180 degrees (thumb on the other side) |
| `a` | approach from the other end of the box |
| `in` / `out` | move the hand 5 mm towards / away from the object |
| `o 10` | set the offset along the approach to 10 mm (negative = away from the object) |
| `r` | reload `shadow_preshapes.json` (after editing it) |
| `i` | print the report again |
| `obj 018_plum` | open another object (the window is reopened) |
| `list` | list the objects |
| `help` | list the commands |
| `q` | quit |

### Typical workflow for one object

1. Start the viewer on the object; the hand is on box 1 in its preshape.
2. Look at the box and the arrow: is it on the intended part, approached from the intended side?
3. Press `Enter` (or `c`) and wait about one second: the fingers close until they touch the object, and the
   terminal prints the report of the closed hand.
4. `Enter` again to reopen, `Page Up` (or `n`) for the next box, and repeat.
5. If the hand is too far or goes through the object, adjust with `in` / `out`; if the thumb is on the wrong
   side, `f`; if the hand comes from inside the object, `a`.

## 4. Reading the report

After every change the terminal prints, for example:

```
[3/4] prismatic_2fingers[susd]  (grasp preshape pinch_3, box 7 x 5 x 7 mm)
  hand: pinch_3 closed, offset +0 mm, thumb opposite to box x
  fingertip distance to the box (mm, 0 = inside): FF 17, MF 11, RF 61, LF 64, TH 16
  contacts with the object (count, closest to the box in mm): mfdistal (3, 7), thdistal (4, 16), deepest penetration 0.4 mm
```

- **Line 1**: the box (index, name with its tags, preshape from `GraspCatalog.cs`, size x / y / z in mm).
- **Line 2**: the hand: preshape used, `open` / `closing` / `closed`, offset, thumb side, approach side.
- **Fingertip distance**: distance of each fingertip from the box surface, 0 when inside it. Fingers:
  `FF` index, `MF` middle, `RF` ring, `LF` little, `TH` thumb.
- **Contacts**: the hand parts touching the object (`distal`, `middle`, `proximal` phalanges, `palm`, ...),
  how many contact points, and how close the closest one is to the box.
- **Deepest penetration**: how far the hand goes into the object. With the hand open, a large value
  (more than a few mm) means the preshape collides with the object at that position; when closed it
  stays below 1 mm, since the physics stops the fingers.

A good grasp, once closed, has the fingers of the preshape (e.g. thumb, index and middle for `pinch_3`)
touching the object within a couple of centimeters of the box, and no large penetration while open.

## 5. Images without a window

```
~/.venvs/mujoco/bin/python python_scripts/MuJoCo/preshape_viewer.py 025_mug --render ~/renders/mug
```

Saves one PNG per grasp and state, e.g. `02_prismatic_2fingers(susd)_pinch_3_closed.png`, each with a
side view and a three-quarter view, and prints the same reports. Useful to check many objects at once or
to share them. On a machine without a display, prefix the command with `MUJOCO_GL=egl`.

Other options (also for the interactive viewer):

| Option | Meaning |
|---|---|
| `--preshape pinch_3` | use this preshape for every grasp |
| `--offset 10` | start with the hand 10 mm closer to the object |
| `--flip` | start with the hand turned by 180 degrees |
| `--hand left` | use the left Shadow Hand (not tested yet) |
| `--menagerie PATH` | location of the mujoco_menagerie clone |

## 6. How the hand is placed on a box

The grasp boxes follow the convention of `GsoPrefabBuilder` and `WristCameraMovement`: **z** is the
approach direction, **x** is along the palm width, **y** across it. The viewer places the hand so that:

- the **palm faces the approach direction** (z). The sign of z is not meaningful in the YCB prefabs
  (the camera reaches either end of the box), so the hand always approaches towards the object center;
  `a` / `End` reverses it;
- the **palm width is along x**, with the thumb on the side opposite to +x (e.g. thumb up for a standing mug
  grasped from the side); `f` / `Home` turns it to the other side. This choice is a convention of the
  viewer, not taken from the Unity hand;
- the **grasp point of the preshape is at the box center**, plus the offset along the approach:
  - `palm` (power grasps): 3.5 cm in front of the base of the fingers (`grasp_site` of the Menagerie model),
    where a held object sits;
  - a list of fingers (pinches): where the thumb meets these fingertips with the hand closed, so the
    fingertips close on the box.

The object is static (it does not move or fall) and there is no table and no gravity.

## 7. The preshapes and how to change them

The **preshape names** come from `graspType2preshape` in `Assets/Scripts/Data_collection/GraspCatalog.cs`,
where they are only labels (the hand in Unity is a rigid mesh):

| Preshape | Grasp types |
|---|---|
| `power_no3` | large_diameter, small_diameter, medium_wrap, sphere_4fingers, power_sphere, power_disk, precision_sphere |
| `pinch_no3` | prismatic_4fingers |
| `pinch_3` | tripod, prismatic_2fingers, tip_pinch |
| `lateral_no3` | adducted_thumb |

The **joint angles** in `shadow_preshapes.json` are not from the project nor from the Hannes hand: they are a
first approximation written for the Shadow Hand from the grasp type names (`pinch_3` is assumed to be a
tripod of thumb, index and middle). Tune them to your needs. Each preshape has:

```json
"pinch_3": {
  "description": "...",
  "grasp_point": ["TH", "FF", "MF"],
  "open":   { "FFJ3": 0.5, "FFJ2": 0.4, ... },
  "closed": { "FFJ3": 1.25, ... }
}
```

- `open`: the hand while approaching (what you see first);
- `closed`: the targets of the actuators when closing; the fingers stop earlier when they touch the object;
- `grasp_point`: `"palm"`, or a list of fingers (the midpoint between the thumb tip and the mean of the other
  listed fingertips, or the thumb tip alone for `["TH"]`).

Angles are in radians; missing joints are 0 and values are clipped to the joint limits. Joints (without the
`rh_` prefix):

| Joint | Meaning | Range (rad) |
|---|---|---|
| `WRJ2`, `WRJ1` | wrist (sideways, flexion) | about -0.5 to 0.2, -0.7 to 0.5 |
| `FFJ4`, `MFJ4`, `RFJ4`, `LFJ4` | finger spread (negative = away from the middle finger for index, ring, little) | -0.35 to 0.35 |
| `FFJ3` ... `LFJ3` | knuckle flexion | -0.26 to 1.57 |
| `FFJ2`, `FFJ1` ... | middle and distal flexion, coupled by a tendon (the actuator moves their sum) | 0 to 1.57 each |
| `LFJ5` | little finger metacarpal (cupping of the palm) | 0 to 0.79 |
| `THJ5` | thumb base rotation | -1.05 to 1.05 |
| `THJ4` | thumb abduction in front of the palm (0 = flat beside the palm) | 0 to 1.22 |
| `THJ3`, `THJ2` | thumb middle joints | -0.21 to 0.21, -0.70 to 0.70 |
| `THJ1` | thumb tip flexion | -0.26 to 1.57 |

To tune a preshape: keep the viewer open on an object using it, edit `shadow_preshapes.json`, save, and type
`r` in the terminal. The hand is redrawn with the new values (`c` to test the closing again).

To add a preshape, add an entry with a new name: it is available with `s <name>` and the `Up` / `Down`
keys. A grasp uses it automatically only if `GraspCatalog.cs` maps its grasp type to that name.

## 8. Batch check of all the grasp boxes, and calibration

`grasp_check.py` runs, without a window, every grasp box of every exported object (about 30 s for the 66
boxes with 4 processes). It needs two more packages:

```
~/.venvs/mujoco/bin/pip install scipy scikit-learn
~/.venvs/mujoco/bin/python python_scripts/MuJoCo/grasp_check.py                 # all objects
~/.venvs/mujoco/bin/python python_scripts/MuJoCo/grasp_check.py 025_mug plum    # some objects
```

For every box, every preshape is tried with 6 hand placements (both thumb sides, offsets of -10, 0 and
+10 mm). Each trial measures how deep the open hand goes into the object, closes the hand, and scores
the contacts against what the preshape needs:

| Preshape | Needs once closed |
|---|---|
| `power_no3` | thumb + 3 fingers, and the palm or proximal phalanges touching |
| `pinch_no3` | thumb + 2 fingers, palm not touching |
| `pinch_3` | thumb + index or middle, palm not touching |
| `lateral_no3` | thumb + index |

A trial is **feasible** when all its needs are met, the open hand goes at most 5 mm into the object and
a contact is within 30 mm of the box. The thresholds and needs are at the top of `grasp_check.py`.

Outputs in `python_scripts/MuJoCo/exported/_check/`:

- `report.md`: start here.
  - **Check of the labeled preshapes**: the boxes not feasible with their own preshape, worst first,
    with the reason and the preshapes that do work. Open them in the viewer (`g <index>`) and in Unity.
  - **Calibration**: how well the preshape can be predicted automatically, compared with your labels.
  - **Per grasp**: the geometry at each box and the predictions.
- `grasps.csv`: one row per box (scores of all preshapes, geometry, predictions).
- `trials.csv`: one row per trial (useful to see which placement worked).

### Geometry of the part at a box

`grasp_geometry.py` measures the object part at each box on the mesh only. It does not use the box
size, which for the GSO automatic grasps is set by the grasp type and would give the label away. The
mesh is sliced through the box center, across the palm width, and the piece closest to the box is
kept, e.g. the handle of a mug and not its body:

- `d1` x `d2`: the size of that piece (thick or thin, round or flat), also within 40 mm of the box
  (`d1_local` x `d2_local`), so that a cap on a bottle still reads as small;
- `L`: how far it continues along the palm width (a marker is long, a cap short, a plate rim very short).

### Calibration

Four ways of choosing the preshape are compared with the labels:

- `baseline`: always the most frequent preshape (power);
- `physics`: the preshape with the best score, nothing fitted;
- `geometry`: a decision tree of depth 3 on the geometry;
- `geometry + physics`: the same tree, also given the best score of each preshape.

The trees are evaluated without seeing the object being predicted: **leave one object out**, and the
stricter **leave one category out** (all the mugs held out together), which is closer to a truly new kind
of object. The fitted trees are printed in the report, so the rules can be read.

Results on 2026-11-03 (66 boxes of 35 objects):

| Predictor | Leave one object out | Leave one category out |
|---|---|---|
| baseline | 50% | 50% |
| physics | 59% | 59% |
| geometry | 65% | 56% |
| geometry + physics | 83% | 73% |

Physics alone hardly beats the baseline: it says whether a preshape *can* hold the part, and often
several can. Physics and geometry together do clearly better, but not well enough to label new objects
without checking. Use the predictions as suggestions to review. With 66 boxes the trees are small and can
change when objects are added: rerun the check after adding objects.

## 9. Limitations

- **Convex hulls**: MuJoCo collides with the convex hull of the object mesh, so hollow parts (the inside of a
  mug, of a shoe, the hole of a handle) count as solid. The visual mesh is still the real one.
- **Not the Hannes hand**: the Shadow Hand is larger and has more degrees of freedom; the preshapes only
  approximate the prosthesis ones.
- **No table, no gravity, static object**: the viewer checks geometry and contacts, not whether the object
  would be lifted.
- **One object per window**: `obj <name>` reopens the window with the new object.

## 10. Troubleshooting

| Problem | Fix |
|---|---|
| `object.json not found` | export from Unity (step 2), or check the name with `--list` |
| `right_hand.xml not found` | clone the Menagerie next to the repository or pass `--menagerie` |
| `No module named mujoco` | run with `~/.venvs/mujoco/bin/python`, not `python3` |
| keys do nothing | click the viewer window first; Page Up / Down may need `Fn`; or use the terminal commands |
| changes in Unity not shown | export again and restart the viewer |
| changes in `shadow_preshapes.json` not shown | type `r` in the terminal |
| only one box visible | the others may be inside or under the object: press `T`, rotate, or go through them with `n` |
| the hand comes from inside the object | `a` (approach from the other end of the box) |
