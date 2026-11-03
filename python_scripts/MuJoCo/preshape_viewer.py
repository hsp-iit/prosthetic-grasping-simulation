#!/usr/bin/env python3
"""Look at the Shadow Hand preshapes against the grasp parallelepipeds of the objects.

The Shadow Hand of MuJoCo Menagerie (https://github.com/google-deepmind/mujoco_menagerie,
shadow_hand/) is placed on a grasp parallelepiped of an object exported from Unity
(Tools -> Grasp Objects -> Export Objects for MuJoCo), in the preshape of that grasp
(GraspCatalog.cs) or in any other one, and can be closed on the object with its
position actuators. The hand is aligned with the parallelepiped as the wrist camera is
in WristCameraMovement: the palm faces its approach direction (z axis, signed towards
the object center, since the camera may reach either end of it), the palm width is
along its x axis, and the grasp point of the preshape (3.5 cm in front of the finger
bases for power grasps, as grasp_site of right_hand.xml; where the thumb meets the
fingertips for pinches) is at its center, moved along the approach direction by the
offset.

The preshapes are joint configurations in shadow_preshapes.json: "open" while
approaching, "closed" the target when closing.

Usage:
  python preshape_viewer.py --list
  python preshape_viewer.py 025_mug                     # interactive viewer
  python preshape_viewer.py 025_mug --render out_dir    # offscreen images, no window

Viewer keys (the viewer window must have the focus):
  Page Up / Page Down   next / previous grasp parallelepiped
  Up / Down             next / previous preshape (auto = the one of the grasp)
  Enter                 close the hand on the object / back to the preshape
  Home                  turn the hand by 180 degrees around the approach direction
  End                   approach from the other end of the parallelepiped
Terminal commands: type `help` in the terminal where the viewer was started.
"""

import argparse
import json
import queue
import sys
import threading
import time
from pathlib import Path
import os

import mujoco
import mujoco.viewer
import numpy as np

HERE = Path(__file__).resolve().parent
EXPORTED = HERE / "exported"
PRESHAPES_PATH = HERE / "shadow_preshapes.json"
DEFAULT_MENAGERIE = HERE.parents[2] / "mujoco_menagerie"

# The grasp point of the power preshapes in the palm frame: grasp_site of right_hand.xml
# (palm normal = -y, fingers = +z, palm width = x). The left hand is mirrored along x,
# hence the same point. The pinch preshapes use their fingertips instead (grasp_point
# in shadow_preshapes.json)
GRASP_POINT = np.array([0.0, -0.035, 0.09])
FINGERS = ["FF", "MF", "RF", "LF", "TH"]
# Distance of the fingertip from the origin of the distal body, along its z axis
TIP_LENGTH = {"FF": 0.026, "MF": 0.026, "RF": 0.026, "LF": 0.026, "TH": 0.032}
CLOSE_DURATION = 1.0      # seconds to ramp the actuators from open to closed
OFFSET_STEP = 0.005

BOX_COLOR = np.array([0.1, 0.8, 0.2])
OTHER_BOX_COLOR = np.array([0.9, 0.6, 0.1])

# GLFW key codes
KEY_ENTER, KEY_RIGHT, KEY_LEFT, KEY_DOWN, KEY_UP = 257, 262, 263, 264, 265
KEY_PAGE_UP, KEY_PAGE_DOWN, KEY_HOME, KEY_END = 266, 267, 268, 269

HELP = """Commands (Enter to send):
  n / p             next / previous grasp parallelepiped
  g <index>         go to a grasp parallelepiped
  s <preshape>      use a preshape (auto = the one of the grasp); s alone lists them
  c                 close the hand on the object / back to the preshape
  f                 turn the hand by 180 degrees around the approach direction
  a                 approach from the other end of the parallelepiped
  in / out          move the hand 5 mm towards / away from the object
  o <offset mm>     set the offset along the approach direction (+ = towards the object)
  obj <name|index>  open another object (the viewer window is reopened)
  list              list the objects
  r                 reload shadow_preshapes.json
  i                 print the grasp report again
  q                 quit"""


def list_objects():
    return sorted(p.parent.name for p in EXPORTED.glob("*/object.json"))


def load_object(name):
    path = EXPORTED / name / "object.json"
    if not path.exists():
        sys.exit(f"{path} not found: export the objects from Unity "
                 "(Tools -> Grasp Objects -> Export Objects for MuJoCo) or check --list")
    obj = json.loads(path.read_text())
    obj["dir"] = path.parent
    for grasp in obj["grasps"]:
        for key in ("center", "size", "right", "up", "forward"):
            grasp[key] = np.array(grasp[key], dtype=float)
    return obj


def load_preshapes():
    data = json.loads(PRESHAPES_PATH.read_text())
    return {k: v for k, v in data.items() if not k.startswith("_")}


def box_rotation(grasp):
    """Right-handed rotation of the parallelepiped (x = right, y = up). The exported
    axes come from the left-handed Unity frame, so forward = -(right x up) here"""
    x = grasp["right"] / np.linalg.norm(grasp["right"])
    y = grasp["up"] - np.dot(grasp["up"], x) * x
    y /= np.linalg.norm(y)
    return np.column_stack([x, y, np.cross(x, y)])


def mat2quat(mat):
    quat = np.zeros(4)
    mujoco.mju_mat2Quat(quat, mat.flatten())
    return quat


def build_model(hand_xml, prefix, obj, mass=None):
    """The hand of the Menagerie mounted on a mocap body, without gravity, and the
    object with its grasp parallelepipeds (visual only, no collision). The object is
    static, or with a mass (kg) free to move (the lift test of grasp_check.py). With a
    mass, the hand is a free
    body welded to the mocap body hand_target (a mocap body has no velocity, hence its
    contacts carry no friction when it moves)"""
    text = hand_xml.read_text()
    text = text.replace('meshdir="assets"', f'meshdir="{hand_xml.parent / "assets"}"')
    text = text.replace("<option ", '<option gravity="0 0 0" ', 1)
    forearm = text.index(f'<body name="{prefix}forearm"')
    base = ('<body name="hand_base" mocap="true">\n' if mass is None
            else '<body name="hand_target" mocap="true"/>\n<body name="hand_base"><freejoint name="hand_free"/>\n')
    text = text[:forearm] + base + text[forearm:]
    end = text.index("</worldbody>")
    text = text[:end] + "</body>\n" + text[end:]

    folder = obj["dir"]
    if obj.get("texture") and (folder / "texture.png").exists():
        material = (f'<texture name="object" type="2d" file="{folder / "texture.png"}"/>\n'
                    '    <material name="object" texture="object" specular="0.2"/>')
    else:
        material = '<material name="object" rgba="0.7 0.7 0.7 1" specular="0.2"/>'
    boxes = []
    for i, grasp in enumerate(obj["grasps"]):
        quat = " ".join(f"{v:.6f}" for v in mat2quat(box_rotation(grasp)))
        pos = " ".join(f"{v:.6f}" for v in grasp["center"])
        size = " ".join(f"{max(v / 2, 1e-4):.6f}" for v in grasp["size"])
        boxes.append(f'<geom name="grasp_{i}" type="box" pos="{pos}" quat="{quat}" size="{size}" '
                     'contype="0" conaffinity="0" group="1" rgba="0.9 0.6 0.1 0.15"/>')
    boxes = "\n    ".join(boxes)
    object_geom = (f'<geom name="object" type="mesh" mesh="object" material="object" group="0"'
                   f' condim="6" friction="0.8 0.01 0.003"/>')
    equality = ""
    if mass is not None:
        object_geom = (f'<body name="object"><freejoint/>'
                       f'<geom name="object" type="mesh" mesh="object" material="object" group="0" '
                       f'mass="{mass}" condim="4" priority="1" friction="1 0.005 0.0001"/></body>')
        equality = '<equality><weld name="drive" body1="hand_base" body2="hand_target" solref="0.005 1"/></equality>'
    scene = f"""
  <visual>
    <global azimuth="150" elevation="-20" offwidth="1280" offheight="960"/>
    <headlight ambient="0.4 0.4 0.4" diffuse="0.6 0.6 0.6"/>
    <quality shadowsize="4096"/>
  </visual>
  <asset>
    <texture type="skybox" builtin="gradient" rgb1="0.3 0.5 0.7" rgb2="0 0 0" width="512" height="3072"/>
    <mesh name="object" file="{folder / 'mesh.obj'}"/>
    {material}
  </asset>
  <worldbody>
    <light pos="0 0 1.5" dir="0 0 -1" directional="true"/>
    {object_geom}
    {boxes}
  </worldbody>
  {equality}
"""
    end = text.rindex("</mujoco>")
    return mujoco.MjModel.from_xml_string(text[:end] + scene + "</mujoco>")


class PreshapeScene:
    """The model of one object with the hand, and the state chosen by the user"""

    def __init__(self, hand_xml, hand, obj, preshapes, mass=None):
        self.prefix = "rh_" if hand == "right" else "lh_"
        self.obj = obj
        self.preshapes = preshapes
        self.model = build_model(hand_xml, self.prefix, obj, mass)
        self.dynamic = mass is not None
        if self.dynamic:
            # The hand does not fall under its own weight
            hand = [b for b in range(self.model.nbody)
                    if self.model.body(b).name not in ("world", "object", "hand_target")]
            self.model.body_gravcomp[hand] = 1
            self.hand_free = self.model.joint("hand_free").qposadr[0]
        self.data = mujoco.MjData(self.model)
        self.object_geom = self.model.geom("object").id
        self.palm = self.model.body(self.prefix + "palm").id
        self.mocap = self.model.body("hand_target" if mass is not None else "hand_base").mocapid[0]
        self.distal = {f: self.model.body(self.prefix + f.lower() + "distal").id for f in FINGERS}
        # The thumb side of the palm (+x for the right hand, -x for the left one)
        self.thumb_x = np.sign(self.model.body(self.prefix + "thbase").pos[0])

        # The palm pose in the frame of the mocap body, with the wrist joints at 0
        mujoco.mj_forward(self.model, self.data)
        self.palm_rot = self.data.xmat[self.palm].reshape(3, 3).copy()
        self.palm_pos = self.data.xpos[self.palm].copy()

        self.grasp_index = 0
        self.preshape_override = None    # None = the preshape of the grasp
        self.flipped = False
        self.reversed = False            # approach from the other end of the box
        self.offset = 0.0
        self.closing_since = None        # simulation time when closing started
        # Error of the hand placement: translation (m, in the box frame) and rotation
        # vector (rad, in the world frame) around the grasp point
        self.position_error = np.zeros(3)
        self.rotation_error = np.zeros(3)

    # ------------------------------------------------------------------ state

    @property
    def grasp(self):
        return self.obj["grasps"][self.grasp_index] if self.obj["grasps"] else None

    @property
    def preshape(self):
        if self.preshape_override:
            return self.preshape_override
        name = self.grasp["preshape"] if self.grasp else None
        return name if name in self.preshapes else next(iter(self.preshapes))

    def preshape_names(self):
        return ["auto"] + list(self.preshapes)

    def cycle_preshape(self, step):
        names = self.preshape_names()
        current = self.preshape_override or "auto"
        self.set_preshape(names[(names.index(current) + step) % len(names)])

    def set_preshape(self, name):
        if name != "auto" and name not in self.preshapes:
            print(f"Unknown preshape {name}, one of: {', '.join(self.preshape_names())}")
            return
        self.preshape_override = None if name == "auto" else name
        self.reset()

    def set_grasp(self, index):
        if self.obj["grasps"]:
            self.grasp_index = index % len(self.obj["grasps"])
            self.reset()

    # ------------------------------------------------------------- kinematics

    def joint_qpos(self, config):
        """qpos of a joint configuration (joint names without the rh_/lh_ prefix)"""
        qpos = self.model.qpos0.copy()
        for name, value in config.items():
            joint = self.model.joint(self.prefix + name)
            low, high = self.model.jnt_range[joint.id]
            qpos[joint.qposadr[0]] = np.clip(value, low, high)
        return qpos

    def actuator_targets(self, qpos):
        """The position actuator targets holding qpos (joint angle or tendon length)"""
        scratch = mujoco.MjData(self.model)
        scratch.qpos[:] = qpos
        mujoco.mj_forward(self.model, scratch)
        return np.clip(scratch.actuator_length, *self.model.actuator_ctrlrange.T)

    def approach(self):
        """The approach direction: the z axis of the parallelepiped, signed towards the
        object center. Its sign is arbitrary in the YCB prefabs, and WristCameraMovement
        reaches either end of the parallelepiped"""
        grasp = self.grasp
        approach = grasp["forward"] / np.linalg.norm(grasp["forward"])
        if np.dot(approach, self.camera_lookat() - grasp["center"]) < 0:
            approach = -approach
        return -approach if self.reversed else approach

    def place_hand(self):
        """Move the mocap body so that the palm is aligned with the grasp"""
        grasp = self.grasp
        if grasp is None:
            return
        approach = self.approach()
        # The palm normal (-y of the palm) is the approach direction, the thumb side (+x
        # of the right palm) is the opposite of the parallelepiped x axis by default,
        # i.e. the thumb points up for a grasp from the side of a standing mug
        y = -approach
        x = -self.thumb_x * grasp["right"] * (-1 if self.flipped else 1)
        x = x - np.dot(x, y) * y
        x /= np.linalg.norm(x)
        rot = np.column_stack([x, y, np.cross(x, y)])
        target = grasp["center"] + approach * self.offset + box_rotation(grasp) @ self.position_error
        if np.any(self.rotation_error):
            error = np.zeros(9)
            quat = np.zeros(4)
            mujoco.mju_axisAngle2Quat(quat, self.rotation_error / np.linalg.norm(self.rotation_error),
                                      np.linalg.norm(self.rotation_error))
            mujoco.mju_quat2Mat(error, quat)
            rot = error.reshape(3, 3) @ rot
        palm_pos = target - rot @ self.grasp_point
        base_rot = rot @ self.palm_rot.T
        self.data.mocap_pos[self.mocap] = palm_pos - base_rot @ self.palm_pos
        self.data.mocap_quat[self.mocap] = mat2quat(base_rot)

    def reset(self):
        """The hand on the current grasp, in the open preshape"""
        mujoco.mj_resetData(self.model, self.data)
        self.closing_since = None
        config = self.preshapes[self.preshape]
        self.grasp_point = self.preshape_grasp_point(config)
        self.place_hand()
        self.data.qpos[:] = self.joint_qpos(config["open"])
        if self.dynamic:
            # The free hand starts on its mocap target
            self.data.qpos[self.hand_free:self.hand_free + 3] = self.data.mocap_pos[self.mocap]
            self.data.qpos[self.hand_free + 3:self.hand_free + 7] = self.data.mocap_quat[self.mocap]
        self.open_ctrl = self.actuator_targets(self.data.qpos)
        self.closed_ctrl = self.actuator_targets(self.joint_qpos(config["closed"]))
        self.data.ctrl[:] = self.open_ctrl
        mujoco.mj_forward(self.model, self.data)
        self.highlight()

    def highlight(self):
        for i in range(len(self.obj["grasps"])):
            geom = self.model.geom(f"grasp_{i}").id
            current = i == self.grasp_index
            color = BOX_COLOR if current else OTHER_BOX_COLOR
            self.model.geom_rgba[geom] = [*color, 0.6 if current else 0.4]

    def toggle_close(self):
        if self.closing_since is None:
            self.closing_since = self.data.time
        else:
            self.reset()

    def step(self, duration):
        """Advance the closing simulation by duration seconds"""
        if self.closing_since is None:
            return
        end = self.data.time + duration
        while self.data.time < end:
            alpha = min(1.0, (self.data.time - self.closing_since) / CLOSE_DURATION)
            self.data.ctrl[:] = (1 - alpha) * self.open_ctrl + alpha * self.closed_ctrl
            mujoco.mj_step(self.model, self.data)

    def close_fully(self):
        """Close the hand and let it settle (offscreen rendering)"""
        self.reset()
        self.closing_since = self.data.time
        self.step(CLOSE_DURATION + 0.5)

    # ----------------------------------------------------------------- report

    def box_distance(self, point):
        """Distance of a point from the current grasp parallelepiped (0 inside)"""
        grasp = self.grasp
        local = box_rotation(grasp).T @ (np.asarray(point) - grasp["center"])
        return float(np.linalg.norm(np.maximum(np.abs(local) - grasp["size"] / 2, 0)))

    def preshape_grasp_point(self, config):
        """The grasp point of a preshape in the palm frame: grasp_site, or the midpoint
        between the thumb tip and the mean of the other listed fingertips, closed"""
        fingers = config.get("grasp_point", "palm")
        if fingers == "palm":
            return GRASP_POINT
        scratch = mujoco.MjData(self.model)
        scratch.qpos[:] = self.joint_qpos(config["closed"])
        mujoco.mj_forward(self.model, scratch)
        tips = self.fingertips(scratch)
        others = [tips[f] for f in fingers if f != "TH"]
        if "TH" in fingers and others:
            point = (tips["TH"] + np.mean(others, axis=0)) / 2
        else:
            point = np.mean([tips[f] for f in fingers], axis=0)
        rot = scratch.xmat[self.palm].reshape(3, 3)
        return rot.T @ (point - scratch.xpos[self.palm])

    def fingertips(self, data=None):
        data = self.data if data is None else data
        tips = {}
        for finger, body in self.distal.items():
            rot = data.xmat[body].reshape(3, 3)
            tips[finger] = data.xpos[body] + rot @ np.array([0, 0, TIP_LENGTH[finger]])
        return tips

    def report(self):
        grasp = self.grasp
        if grasp is None:
            return f"{self.obj['name']}: no grasp parallelepiped"
        distances = ", ".join(f"{finger} {1000 * self.box_distance(tip):.0f}"
                              for finger, tip in self.fingertips().items())

        contacts = {}
        deepest = 0.0
        for contact in self.data.contact[:self.data.ncon]:
            if self.object_geom not in (contact.geom1, contact.geom2):
                continue
            other = contact.geom2 if contact.geom1 == self.object_geom else contact.geom1
            body = self.model.body(self.model.geom_bodyid[other]).name.replace(self.prefix, "")
            contacts.setdefault(body, []).append(self.box_distance(contact.pos))
            deepest = min(deepest, contact.dist)
        if self.closing_since is None:
            state = "open"
        elif self.data.time - self.closing_since < CLOSE_DURATION:
            state = "closing"
        else:
            state = "closed"
        size = " x ".join(f"{1000 * v:.0f}" for v in grasp["size"])
        lines = [
            f"[{self.grasp_index + 1}/{len(self.obj['grasps'])}] {grasp['name']}  "
            f"(grasp preshape {grasp['preshape']}, box {size} mm)",
            f"  hand: {self.preshape} {state}, offset {1000 * self.offset:+.0f} mm, "
            f"{'turned 180' if self.flipped else 'thumb opposite to box x'}"
            f"{', approach reversed' if self.reversed else ''}",
            f"  fingertip distance to the box (mm, 0 = inside): {distances}",
            "  contacts with the object (count, closest to the box in mm): "
            + (", ".join(f"{b} ({len(d)}, {1000 * min(d):.0f})" for b, d in sorted(contacts.items()))
                                              + f", deepest penetration {-1000 * deepest:.1f} mm"
                                              if contacts else "none"),
        ]
        return "\n".join(lines)

    # ---------------------------------------------------------------- drawing

    def camera_lookat(self):
        return np.mean([self.obj["bounds_min"], self.obj["bounds_max"]], axis=0)

    def extent(self):
        return float(np.linalg.norm(np.subtract(self.obj["bounds_max"], self.obj["bounds_min"])))

    def draw_approach(self, scn):
        """Add to scn an arrow along the approach direction, ending at the grasp center"""
        grasp = self.grasp
        if grasp is None or scn.ngeom >= scn.maxgeom:
            return
        approach = self.approach()
        start = grasp["center"] - approach * 0.12
        geom = scn.geoms[scn.ngeom]
        mujoco.mjv_initGeom(geom, mujoco.mjtGeom.mjGEOM_ARROW, np.zeros(3), np.zeros(3),
                            np.zeros(9), np.array([0.1, 0.8, 0.2, 0.8], dtype=np.float32))
        mujoco.mjv_connector(geom, mujoco.mjtGeom.mjGEOM_ARROW, 0.003, start, grasp["center"])
        scn.ngeom += 1


# -------------------------------------------------------------------- offscreen

def render(scene, out_dir):
    """For each grasp, an image of the open preshape and one of the closed hand, each
    with two views: from the side of the approach line, and three-quarter"""
    import PIL.Image
    out_dir.mkdir(parents=True, exist_ok=True)
    renderer = mujoco.Renderer(scene.model, height=480, width=640)
    camera = mujoco.MjvCamera()
    option = mujoco.MjvOption()
    option.flags[mujoco.mjtVisFlag.mjVIS_CONTACTPOINT] = True
    for i in range(max(1, len(scene.obj["grasps"]))):
        scene.set_grasp(i)
        grasp = scene.grasp
        approach = scene.approach() if grasp else np.array([1.0, 0.0, 0.0])
        side = np.cross(approach, [0, 0, 1])
        if np.linalg.norm(side) < 1e-3:
            side = np.array([0.0, 1.0, 0.0])
        side /= np.linalg.norm(side)
        camera.lookat[:] = grasp["center"] if grasp else scene.camera_lookat()
        camera.distance = max(0.3, 1.5 * scene.extent())
        for state in ("open", "closed"):
            if state == "closed":
                scene.close_fully()
            mujoco.mj_forward(scene.model, scene.data)
            views = []
            # The camera looks along view: azimuth and elevation of that direction
            for view, elevation in ((side, -30), (side + 0.7 * approach, -40)):
                camera.azimuth = np.degrees(np.arctan2(view[1], view[0]))
                camera.elevation = elevation
                renderer.update_scene(scene.data, camera, option)
                scene.draw_approach(renderer.scene)
                views.append(renderer.render())
            name = f"{i:02d}_{grasp['name'] if grasp else 'none'}_{scene.preshape}_{state}.png"
            path = out_dir / name.replace("[", "(").replace("]", ")")
            PIL.Image.fromarray(np.concatenate(views, axis=1)).save(path)
            print(scene.report())
            print(f"  -> {path}\n")
        scene.reset()
    renderer.close()


# -------------------------------------------------------------------- interactive

def read_terminal(commands):
    while True:
        try:
            line = input()
        except EOFError:
            return
        commands.put(line.strip())


def run_viewer(scene, commands):
    """Run the viewer until the window is closed or another object is asked for.
    Returns the name or index of the object to open next, or None to quit"""
    keys = {KEY_PAGE_UP: "n", KEY_PAGE_DOWN: "p", KEY_UP: "s+", KEY_DOWN: "s-",
            KEY_ENTER: "c", KEY_HOME: "f", KEY_END: "a"}

    def on_key(keycode):
        if keycode in keys:
            commands.put(keys[keycode])

    scene.reset()
    print(scene.report())
    next_object = None
    quit_requested = False
    viewer = mujoco.viewer.launch_passive(scene.model, scene.data, key_callback=on_key,
                                          show_left_ui=False, show_right_ui=False)
    try:
        viewer.cam.lookat[:] = scene.camera_lookat()
        viewer.cam.distance = max(0.4, 2.0 * scene.extent())
        viewer.opt.flags[mujoco.mjtVisFlag.mjVIS_CONTACTPOINT] = True
        last = time.time()
        reported_closed = False
        while viewer.is_running():
            changed = False
            while not commands.empty():
                command = commands.get()
                result = handle_command(scene, command)
                if result == "quit":
                    quit_requested = True
                    break
                if isinstance(result, tuple):
                    next_object = result[1]
                    break
                changed = changed or result
            if next_object is not None or quit_requested:
                break
            now = time.time()
            with viewer.lock():
                scene.step(min(now - last, 0.05))
                viewer.user_scn.ngeom = 0
                scene.draw_approach(viewer.user_scn)
            last = now
            if changed:
                reported_closed = False
                print(scene.report())
            elif (scene.closing_since is not None and not reported_closed
                  and scene.data.time - scene.closing_since > CLOSE_DURATION + 0.3):
                reported_closed = True
                print(scene.report())
            viewer.sync()
            time.sleep(0.01)
    finally:
        # Wait for the viewer thread to close its window: exiting while it is still
        # tearing down GLFW crashes the interpreter
        viewer.close()
        while viewer.is_running():
            time.sleep(0.01)
        time.sleep(0.2)
    return None if quit_requested else next_object


def handle_command(scene, command):
    """Apply a command: True if the state changed, ("object", name) to open another
    object, "quit" to quit"""
    parts = command.split()
    if not parts:
        return False
    verb, args = parts[0].lower(), parts[1:]
    if verb == "n":
        scene.set_grasp(scene.grasp_index + 1)
    elif verb == "p":
        scene.set_grasp(scene.grasp_index - 1)
    elif verb == "g" and args and args[0].lstrip("-").isdigit():
        scene.set_grasp(int(args[0]) - 1)
    elif verb == "s+":
        scene.cycle_preshape(1)
    elif verb == "s-":
        scene.cycle_preshape(-1)
    elif verb == "s":
        if not args:
            print("Preshapes: " + ", ".join(scene.preshape_names()))
            return False
        scene.set_preshape(args[0])
    elif verb == "c":
        scene.toggle_close()
    elif verb == "f":
        scene.flipped = not scene.flipped
        scene.reset()
    elif verb == "a":
        scene.reversed = not scene.reversed
        scene.reset()
    elif verb in ("in", "out"):
        scene.offset += OFFSET_STEP if verb == "in" else -OFFSET_STEP
        scene.reset()
    elif verb == "o" and args:
        scene.offset = float(args[0]) / 1000
        scene.reset()
    elif verb == "r":
        scene.preshapes = load_preshapes()
        scene.reset()
    elif verb == "i":
        return True
    elif verb == "list":
        for i, name in enumerate(list_objects(), 1):
            print(f"  {i:3d} {name}")
        return False
    elif verb == "obj" and args:
        return ("object", args[0])
    elif verb in ("q", "quit", "exit"):
        return "quit"
    else:
        print(HELP)
        return False
    return True


def resolve_object(name_or_index):
    names = list_objects()
    if name_or_index.isdigit() and 1 <= int(name_or_index) <= len(names):
        return names[int(name_or_index) - 1]
    if name_or_index in names:
        return name_or_index
    matches = [n for n in names if name_or_index.lower() in n.lower()]
    if len(matches) == 1:
        return matches[0]
    print(f"No single object matching {name_or_index}: {', '.join(matches) or 'none'}")
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("object", nargs="?", help="exported object: name, part of it or index of --list")
    parser.add_argument("--list", action="store_true", help="list the exported objects")
    parser.add_argument("--menagerie", type=Path, default=DEFAULT_MENAGERIE,
                        help=f"the mujoco_menagerie clone (default {DEFAULT_MENAGERIE})")
    parser.add_argument("--hand", choices=["right", "left"], default="right")
    parser.add_argument("--preshape", help="preshape to use instead of the one of each grasp")
    parser.add_argument("--offset", type=float, default=0.0,
                        help="mm along the approach direction (+ = towards the object)")
    parser.add_argument("--flip", action="store_true", help="turn the hand by 180 degrees")
    parser.add_argument("--render", type=Path, metavar="DIR",
                        help="save an image per grasp (open and closed) instead of opening the viewer")
    args = parser.parse_args()

    if args.list or not args.object:
        for i, name in enumerate(list_objects(), 1):
            obj = json.loads((EXPORTED / name / "object.json").read_text())
            grasps = ", ".join(f"{g['name']} ({g['preshape']})" for g in obj["grasps"]) or "no grasps"
            print(f"{i:3d} {name}: {grasps}")
        if not list_objects():
            print("No exported object: Tools -> Grasp Objects -> Export Objects for MuJoCo in Unity")
        return

    hand_xml = args.menagerie / "shadow_hand" / f"{args.hand}_hand.xml"
    if not hand_xml.exists():
        sys.exit(f"{hand_xml} not found: clone https://github.com/google-deepmind/mujoco_menagerie "
                 "or pass --menagerie")
    preshapes = load_preshapes()

    def make_scene(name):
        scene = PreshapeScene(hand_xml, args.hand, load_object(name), preshapes)
        scene.preshape_override = args.preshape
        scene.offset = args.offset / 1000
        scene.flipped = args.flip
        return scene

    name = resolve_object(args.object)
    if name is None:
        sys.exit(1)
    if args.render:
        render(make_scene(name), args.render)
        return

    commands = queue.Queue()
    threading.Thread(target=read_terminal, args=(commands,), daemon=True).start()
    print(HELP)
    while name is not None:
        scene = make_scene(name)
        print(f"\n== {name} ({scene.obj['category']}) ==")
        requested = run_viewer(scene, commands)
        if requested is None:
            break
        name = resolve_object(requested) or name
        preshapes = scene.preshapes


if __name__ == "__main__":
    main()
    # The thread reading the terminal is blocked in input(): a normal interpreter
    # shutdown with it aborts (Python 3.12), hence exit directly
    sys.stdout.flush()
    os._exit(0)
