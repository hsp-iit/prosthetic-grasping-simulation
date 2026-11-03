
<div align="center">    

# Grasp Pre-shape Selection by Synthetic Training: <br>Eye-in-hand Shared Control on the Hannes Prosthesis 
</div>

<img src="https://img.shields.io/badge/Unity-6000.6.5f1-green.svg?style=flat-square" alt="Unity 6000.6.5f1"> <img src="https://img.shields.io/badge/HDRP-17.6.0-orange.svg?style=flat-square" alt="HDRP 17.6.0"> <img src="https://img.shields.io/badge/Perception-1.0.0--preview.1%20(patched)-blue.svg?style=flat-square" alt="Perception 1.0.0-preview.1">



<p align="center">
  <a href="https://arxiv.org/abs/2203.09812" style="font-size: 25px; text-decoration: none">Paper</a>
  &nbsp; &nbsp;
  <a href="https://drive.google.com/file/d/16QcD1yprsNhxPc93EbLV_Mby2FlcJcJ7/view?usp=sharing" style="font-size: 25px; text-decoration: none">Demonstration video</a>
  &nbsp; &nbsp;
  <a href="https://drive.google.com/file/d/1qy1HoTzGodUyE1Ao1ezXsuYVNoqWn7Gg/view?usp=sharing" style="font-size: 25px; text-decoration: none">Presentation video</a>
  <br>
  <a href="https://zenodo.org/record/7327516#.Y4Mu23bMKF5" style="font-size: 25px; text-decoration: none">Synthetic dataset generated</a>
  &nbsp; &nbsp;
  <a href="https://zenodo.org/record/7327150#.Y4Mu4HbMKF5" style="font-size: 25px; text-decoration: none">Real dataset collected</a>
  &nbsp; &nbsp;
  <a href="https://github.com/hsp-iit/prosthetic-grasping-experiments" style="font-size: 25px; text-decoration: none">Experiments repository</a>
  &nbsp; &nbsp;
  <br>
  <img src="synthetic_samples.gif">
</p>

We introduce a synthetic dataset generation pipeline designed for vision-based prosthetic grasping. The method supports multiple grasps per object by overlaying a transparent parallelepiped onto each object part to grasp. The camera follows a straight line towards the object part while recording the video. The scene, initial camera position and object pose are randomized in each video. <br>We used 15 objects from the YCB dataset, where 7 of them have one grasp and 8 of them have multiple grasps, resulting in _31 grasp type - object part_ pairs.<br>_Our work is accepted to IROS 2022_.

## Getting started
- The project uses Unity 6000.6.5f1 (Unity 6) with HDRP 17.6.0. Install it from the Unity Hub (`Installs -> Install Editor -> Archive`).
- It has been tested on Linux (Ubuntu). The original Unity 2020.3.11f1 version of the project (tested on Windows 10/11) is available on the `main` branch.
- All the necessary packages come pre-installed in the repository, therefore no installation step is required.
  - [Perception](https://github.com/Unity-Technologies/com.unity.perception) is archived and its last release (1.0.0-preview.1) does not compile on Unity 6000.6. A patched copy is embedded in `Packages/com.unity.perception` (see `PATCHES.md` there).

## Installation
- Install Git and [Git LFS](https://docs.github.com/en/repositories/working-with-files/managing-large-files/installing-git-large-file-storage). On Ubuntu: `sudo apt install git git-lfs` (on Windows use [Git for Windows](https://git-scm.com/download/win)).
- Open a terminal and run `git lfs install` to initialize it.<br>Then, clone the repository and switch to this branch: `git clone -b upgrade_unity https://github.com/hsp-iit/prosthetic-grasping-simulation`
- Go on the Unity Hub, click on `Add -> Add project from disk`, locate the downloaded repository and open it with Unity 6000.6.5f1. The first import takes a while.
- [Linux laptops with NVIDIA + integrated GPU] The project renders with Vulkan, and Unity picks the discrete NVIDIA GPU automatically. You can check it in the Editor log (`~/.config/unity3d/Editor.log`), which should contain `Vulkan renderer=[NVIDIA ...]`, or with `nvidia-smi` while Unity is open. If the integrated GPU is picked instead, add `-force-device-index 1` (the NVIDIA index listed in the `[Vulkan init] Physical Device` lines of the log) to the project's command line arguments in the Unity Hub.

## Synthetic dataset generation in Unity
- Once the project is open, ensure that the correct scene is selected: in the `Project` tab open `Assets/Scenes` and double-click on `Data_collection.unity` to open the scene.
- From the top bar menu, open `Edit -> Project Settings`
  - In `Project Settings`, search for `Lit Shader Mode` and set it to `Both` (in Unity 6 it is under `Quality -> HDRP -> Rendering`).
  ![lit_shader_mode](https://user-images.githubusercontent.com/50639319/192339142-3e17b12c-f81f-4828-ac54-b609185cb2d3.png)

  - In `Project Settings`, search for `Motion Blur` and disable it.
  ![motion_blur](https://user-images.githubusercontent.com/50639319/192340209-f4924a9e-977d-44c3-aee9-19729006eb70.png)

- [OPTIONAL] The pipeline generates the same number of videos for each _grasp type - object part_ pair (recall, there are currently 31 pairs). 50 videos are generated for each pair, resulting in 1550 videos. From the `Hierarchy` tab (left-hand side) click on `Simulation Scenario` and its properties will appear in the `Inspector` tab (right-hand side). Make sure that the value of `Fixed Length Scenario -> Scenario Properties -> Constants -> Iteration Count` is set to 1550 (and `Frames Per Iteration`, which in Perception 1.0 is a field of the scenario itself, to 90) <u>and</u> the value of `Fixed Length Scenario -> Randomizers -> WristCameraMovement -> Num Iterations Per Grasp` is set to 50. If you want to generate a different number of videos, change these values accordingly. For instance, to generate 10 videos for each pair, set `Iteration Count` to 31*10=310 <u>and</u> `Num Iterations Per Grasp` to 10. If the numbers are not consistent, the execution stops.
- [OPTIONAL] To set the dataset output folder, go on `Edit -> Project Settings -> Perception` and click on the `Change Folder` button to set a new `Base Path`. Keep `Perception` (not `Solo`) as the active endpoint: it produces the `captures_***.json` format expected by the conversion script.
- :rocket: Click on the play button on top to start collecting the synthetic dataset.
  - [WARNING] On GPUs with 4 GB of memory (e.g. RTX 3050 Laptop) the Editor together with the simulation can run out of GPU memory, and the Editor may freeze while waiting for a frame to be presented. In that case generate the dataset with a Linux build instead (see below).
- [WARNING]: if you want to change settings, e.g., enable bounding box/semantic segmentation labeling or import your own objects, few settings need to be adjusted. These are not explained here for the sake of brevity, feel free to contact me (federico.vasile@iit.it) or open an issue and I will provide you all the instructions.
- When the simulation is over, go on the `Hierarchy` tab and select `WristCamera`. In the `Inspector` tab search for `Latest Generated Dataset` and click on `Show folder` to locate the dataset folder.

## Adding objects from Google Scanned Objects
More training data can be generated with objects from [Google Scanned Objects](https://app.gazebosim.org/GoogleResearch/fuel/collections/Scanned%20Objects%20by%20Google%20Research) (GSO, 1033 household objects, CC BY 4.0, downloaded from Gazebo Fuel). They are imported in the same format as the YCB objects:
- the model in `Assets/3Dmodels/GSO/<name>/google_16k/` (`textured.obj`, `textured.mtl`, `texture_map.png`), together with `grasp_spec.json` (category and grasps) and `metadata.pbtxt` (GSO metadata and license);
- the prefab in `Assets/Prefabs/GSO/<name>.prefab`, with the same structure as the YCB prefabs: the root (`Labeling` and `GraspObjectInfo` components), the `textured` child (mesh and `MeshCollider`) and the grasp parallelepipeds as its inactive children (transparent material, renderer disabled, `BoxCollider`).

The starter set below is included in the repository (models and prefabs). Other objects are added with the steps below.

### Quick start
```
python3 python_scripts/GSO/import_gso.py download        # the objects of the list not imported yet
```
Then, in Unity:
1. `Tools -> Grasp Objects -> Build GSO Prefabs` (or browse and add objects one by one with `Tools -> Grasp Objects -> GSO Browser`, see below)
2. `Tools -> Grasp Objects -> Training Object Selector`: select the objects and grasps, click `Apply to Simulation Scenario` and save the scene.
3. Click the play button (or make a Linux build, see below).

### Starter set
`python_scripts/GSO/gso_objects.json` comes with 14 objects of the existing categories, i.e. 28 object-grasp pairs (power_no3: 15, pinch_3: 8, pinch_no3: 3, lateral_no3: 2):

| Category | Objects | Grasps |
|:---|:---|:---|
| mug | `ACE_Coffee_Mug_Kristen_16_oz_cup`, `Room_Essentials_Mug_White_Yellow`, `Cole_Hardware_Mug_Classic_Blue`, `Threshold_Porcelain_Coffee_Mug_All_Over_Bead_White` | `large_diameter[susd]`, `sphere_4fingers[nops]`, `prismatic_2fingers[susd]` (handle) |
| dispenser | `Marc_Anthony_Skip_Professional_Oil_of_Morocco_Conditioner_with_Argan_Oil`, `Lutein`, `5_HTP`, `CoQ10` | `large_diameter`, `tripod[nops]` (cap) |
| tube | `Nestle_Nesquik_Chocolate_Powder_Flavored_Milk_Additive_109_Oz_Canister` | `sphere_4fingers[nops]`, `large_diameter[left][susd]`, `large_diameter[right][susd]` |
| plate | `Threshold_Dinner_Plate_Square_Rim_White_Porcelain`, `Room_Essentials_Salad_Plate_Turquoise` | `adducted_thumb[border]` (rim) |
| small_tool | `Craftsman_Grip_Screwdriver_Phillips_Cushion`, `Diamond_Visions_Scissors_Red` | `prismatic_4fingers` |
| big_tool | `OXO_Cookie_Spatula` | `prismatic_4fingers` |

### Browsing the objects in Unity (GSO Browser)
`Tools -> Grasp Objects -> GSO Browser` shows the whole GSO catalog, to look at the objects and decide which ones to add:
- On the left, the list of the 1033 objects, filtered by text (`Search`, all the words must be in the name or description), GSO category and `Show` (all, added only, not added only). Added objects have a check mark and their category. Scroll through them with the Up/Down arrows.
- On the right, the selected object: description, size, license and its 5 thumbnails from Gazebo Fuel (Left/Right arrows or click on them). Once added, a 3D preview of the prefab (drag to rotate) shows the grasp parallelepipeds as colored wireframes, with a line along their approach direction, and lists them with their preshape.
- To add an object, set its `Category` (type it or pick a known one), optionally `Farthest part to` (e.g. `right` for the handle of a mug), and choose:
  - `Add grasp boxes automatically` ticked: list the grasps (grasp type, side, `nops`/`usd`/`susd`/`border` tags, approach), or copy them from an object of the same category with `Copy grasps from`. The grasp parallelepipeds are placed as described in [Building the prefabs](#building-the-prefabs).
  - unticked: the prefab is created without grasp parallelepipeds, to place them by hand (see [Placing the grasp boxes by hand](#placing-the-grasp-boxes-by-hand)).

  Then click `Add to prefabs`: the object is added to `gso_objects.json`, downloaded, converted and its prefab built, and the 3D preview appears.
- For an added object: `Update and rebuild prefab` applies the changes made in the window, `Open prefab` opens it in prefab mode, `Remove from project` removes it from `gso_objects.json` and deletes its model and prefab.
- The window runs `import_gso.py` (set `Python executable` at the bottom if `python3` is not in the `PATH`). `Refresh catalog` downloads the catalog again from Gazebo Fuel. The output of the commands is shown below the form.

### Selecting and importing objects from the command line
- Search the GSO models (the catalog is cached in `~/.cache/gso_fuel_catalog.json`, use `--refresh` to update it):
  ```
  python3 python_scripts/GSO/import_gso.py search "mug|cup"
  python3 python_scripts/GSO/import_gso.py search . --fuel-category "Bottles and Cans and Cups"
  ```
- Add a model to the list, with its category (used as `object_name` in the labels) and its grasps (`NAME:APPROACH`):
  ```
  python3 python_scripts/GSO/import_gso.py add Cole_Hardware_Mug_Classic_Blue --category mug --farthest-to right \
      --grasp "large_diameter[susd]:left" --grasp "sphere_4fingers[nops]:top" --grasp "prismatic_2fingers[susd]:right"
  ```
- List or download the objects needed for the training, selected by name (`--names`), category (`--categories`), preshape (`--preshapes`, objects having at least one grasp with that preshape), grasp type (`--grasp-types`) and `--max-per-category`. `list` shows the selected objects (`*` = already imported) and the resulting object-grasp pairs per preshape; `download --dry-run` does the same without downloading:
  ```
  python3 python_scripts/GSO/import_gso.py list --preshapes pinch_3
  python3 python_scripts/GSO/import_gso.py download --preshapes pinch_3 --max-per-category 2
  python3 python_scripts/GSO/import_gso.py download --categories mug dispenser
  ```
  The preshape and grasp type filters select whole objects, and the pairs shown count all their grasps (e.g. `--preshapes pinch_3` selects 8 objects with 20 pairs, 8 of them `pinch_3`). To execute only the grasps of a preshape, use the preshape filter of the Training Object Selector (see below).

  Downloading an object already imported only updates its `grasp_spec.json` (e.g. after changing its grasps in the list); use `--force` to download it again.
- Other commands: `add --manual-grasps` adds an object without grasps, to place them by hand in the prefab; `add --replace` replaces an object already in the list; `remove NAME` removes an object from the list and deletes its model and prefab (`--keep-files` to only remove it from the list).

The fields of an object in `gso_objects.json`:

| Field | Description |
|:---|:---|
| `name` | Model name on Gazebo Fuel |
| `category` | Object category used in the labels, e.g. `mug`, `dispenser` (see `GraspCatalog.instance2object` for the YCB ones) |
| `yaw` | Optional rotation (degrees) around the vertical axis |
| `auto_grasps` | Optional, default `true`. `false`: the grasp parallelepipeds are placed by hand in the prefab, and `grasps` is empty |
| `farthest_to` | Optional, `left`/`right`/`front`/`back`: rotates the object such that its most protruding part (e.g. the handle of a mug) faces that side |
| `grasps[].name` | Grasp type and tags, as the grasp parallelepipeds of the YCB prefabs, e.g. `large_diameter[left][susd]`. Tags: `[left]`/`[right]` (object side), `[nops]` (no pronation-supination), `[usd]` (upside down), `[susd]` (randomly standing or upside down), `[border]` (object placed on the table border, e.g. to grasp a plate by its rim) |
| `grasps[].approach` | Where the camera comes from, in the frame of the upright object: `top`, `bottom`, `left` (-z), `right` (+z), `front` (-x), `back` (+x) |
| `grasps[].height` | Optional, side grasps: where along the object height (0..1, default 0.5) |
| `grasps[].offset` | Optional `[x, y, z]` (meters) added to the grasp position |
| `grasps[].size` | Optional `[x, y, z]` (meters) parallelepiped size: x along the palm width, z along the approach direction. By default, a size per grasp type, similar to the YCB prefabs |

### Building the prefabs
`Tools -> Grasp Objects -> Build GSO Prefabs` builds (or rebuilds) the prefab of every object in `Assets/3Dmodels/GSO`. The model is put upright and its bounding box centered on the prefab origin. Each grasp parallelepiped is placed where its approach line reaches the mesh surface, with its center 5 mm outside the surface, such that the visibility check of `WristCameraMovement` hits it before the object; for top and bottom grasps of hollow objects (e.g. a mug) it is placed on the rim. The prefabs are built in a preview scene (the open scene is not modified) and rebuilding a prefab keeps its GUID, hence the references in the scene.

The placement is automatic: check the grasp parallelepipeds (in the 3D preview of the GSO Browser, or in the prefab) and adjust them if needed. Rebuilding a prefab with automatic grasps overwrites the manual changes.

### Placing the grasp boxes by hand
A grasp parallelepiped can be added by hand to any object prefab, YCB or GSO (any prefab with a `textured` child):
1. Open the prefab in prefab mode: double-click it in `Assets/Prefabs/YCB` or `Assets/Prefabs/GSO`, or `Open prefab` in the GSO Browser.
2. Select the object (or its `textured` child) and `Tools -> Grasp Objects -> Show Grasp Boxes of Selected Object` to see the grasp parallelepipeds it already has.
3. `Tools -> Grasp Objects -> Add Grasp Box to Selected Object` adds a grasp parallelepiped named `tripod[nops]` under `textured`, on top of the object and approached from the top, with the components and transparent material of the YCB ones.
4. Move, rotate and scale it (with the gizmo in `Local` mode):
   - its z axis (blue) is the approach direction, pointing towards the object;
   - its x axis (red) is aligned with the palm width;
   - its center should be just outside the object surface (about 5 mm), otherwise the camera-object poses are rejected by the visibility check.
5. Rename it with the grasp type and tags, `type[left|right][nops][usd][susd][border]` (e.g. `large_diameter[left]`, `prismatic_2fingers[nops][usd]`). The grasp type must be in `graspType2preshape` of `GraspCatalog.cs`, otherwise the simulation stops with an error.
6. `Tools -> Grasp Objects -> Hide Grasp Boxes of Selected Object` before saving the prefab: as in the YCB prefabs, they are stored inactive with the renderer disabled, and `WristCameraMovement` activates the one to execute.
7. Save the prefab.

The YCB prefabs are never rebuilt, so the grasp parallelepipeds added to them are kept. For the GSO objects:
- Objects with hand-placed grasps (`auto_grasps: false`): rebuilding the prefab (`Build GSO Prefabs` or `Update and rebuild prefab`) keeps all the grasp parallelepipeds in it.
- Objects with automatic grasps (`auto_grasps: true`, the default): rebuilding the prefab places the grasp parallelepipeds again from `gso_objects.json` and **drops the ones added by hand**. Either add the grasp to the object's list in the GSO Browser (it is then placed automatically), or switch the object to hand-placed grasps, keeping the automatic ones already in the prefab: untick `Add grasp boxes automatically` in the GSO Browser and click `Update and rebuild prefab`, or

  ```bash
  python python_scripts/GSO/import_gso.py add NAME --category CATEGORY --manual-grasps --replace
  ```

### Selecting the training objects
`Tools -> Grasp Objects -> Training Object Selector` lists the prefabs in `Assets/Prefabs/YCB` and `Assets/Prefabs/GSO` with their source, category and grasps:
- filter them by source (YCB, GSO), category, preshape and grasp type, and include or exclude single objects;
- the number of selected objects, the object-grasp pairs per preshape (to balance the data) and the resulting `Iteration Count` are shown;
- `Apply to Simulation Scenario` sets, in the open scene, the `WristCameraMovement` prefabs list, its `Preshape Filter` and `Grasp Type Filter` (only the grasps passing them are executed, empty = all), `Num Iterations Per Grasp` and the matching `Iteration Count`. It can be undone (`Ctrl+Z`); save the scene to keep it. `Load selection from scenario` shows the current selection of the scenario.

For instance, selecting only the `pinch_3` preshape gives 13 object-grasp pairs (8 GSO and 5 YCB), i.e. an `Iteration Count` of 650 with 50 iterations per grasp.

### Validation
The pose sampling and trajectory check of `WristCameraMovement` were simulated in edit mode on the `Data_collection` scene, with 300 random camera-object poses per object-grasp pair (the simulation stops when no valid pose is found in 1000 attempts):

| Object-grasp pairs | Valid poses |
|:---|:---|
| GSO starter set (28 pairs) | 51-100% |
| YCB prefabs (36 pairs, for reference) | 1-100% |

The GSO prefabs are upright and textured with their original texture (labeled as `Original`, as the YCB ones). A full Play mode data collection with the GSO objects has not been run yet.

### Limitations
- The automatic grasp placement is a starting point: check it for every new object, especially for handles and objects with irregular shapes.
- The pronation-supination direction is forced (supination) for some YCB object-grasp pairs, hard-coded by object name in `WristCameraMovement` (e.g. `large_diameter` of `025_mug`); this is not applied to the GSO objects.
- The GSO object names are not in the label configs (`IdLabelConfig`, `SemanticSegmentationLabelConfig`): add them if you enable the bounding box or segmentation labelers.
- The category of a GSO object is stored in the `GraspObjectInfo` component of its prefab; objects without it must be listed in `GraspCatalog.instance2object`, otherwise the simulation stops with an error.
- Attribution: Google Scanned Objects, Google LLC, [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/) (the license of each object is in its `metadata.pbtxt`).

## Checking the preshapes against the grasp boxes in MuJoCo
The physics checks of the grasp boxes and preshapes are in the separate repository [grasping_physics_simulation](../grasping_physics_simulation) (next to this one): the [Shadow Hand](https://github.com/google-deepmind/mujoco_menagerie/tree/main/shadow_hand) of MuJoCo Menagerie is placed on each grasp parallelepiped of an object, in the preshape of that grasp, moved freely, closed on the object and lift-tested, and a batch check lists the suspicious boxes. See its README.

This project only exports the objects for it: `Tools -> Grasp Objects -> Export Objects for MuJoCo` writes every YCB and GSO prefab (the mesh of `textured`, its texture and the grasp parallelepipeds, converted to the MuJoCo frame) to `assets/objects/` of `grasping_physics_simulation`, looked for next to this project (otherwise a folder is asked and remembered). Export again after changing the grasp boxes.

## Synthetic dataset generation with a Linux build
A standalone build uses less GPU memory than the Editor and is the most reliable way to generate the full dataset.
- Set the scenario values (`Iteration Count`, `Frames Per Iteration`, `Num Iterations Per Grasp`) in the `Data_collection` scene as described above and save the scene.
- Open `File -> Build Profiles`, select `Linux`, make sure `Scenes/Data_collection` is the only scene in the list, click `Build` and choose an empty folder outside the repository (e.g. `~/builds/data_collection`), naming the executable `DataCollection`. The first build takes about 30 minutes (shader compilation).
- Run it with `./run_data_collection.sh ~/builds/data_collection/DataCollection.x86_64 ~/synthetic_dataset`. A 1280x960 window shows the simulation (it must stay open: Linux builds do not render cameras in `-batchmode`), the dataset is written to `~/synthetic_dataset/<hash>/` and the window closes when all the videos are generated.

## Converting the generated videos into our own format
- Once you are in the dataset folder mentioned above, you can find the labels (along with other metadata) as json files (`captures_***.json`) into the `Dataset023982da-0257-4541-9886-d22172b6c94c` folder (this is an example folder, you will have a different hash code following the `Dataset` name). <br>All the video frames (`rgb_***.png`) are located under the `RGB_another_hash_code_` folder.
- We provide a script to convert the frames and labels into the structure used by our [experiments pipeline](https://github.com/hsp-iit/prosthetic-grasping-experiments). Each video will be organized according to the following path: `DATASET_BASE_FOLDER/CATEGORY_NAME/OBJECT_NAME/PRESHAPE_NAME/Wrist_d435/rgb*/*.png`. <br>For example: `ycb_synthetic_dataset/dispenser/006_mustard_bottle/power_no3/Wrist_d435/rgb*/*.png`
- To run the script, go into `python_scripts/Data_collection` and copy `convert_dataset.py` into the folder of your synthetic dataset generated (i.e. the folder containing the `Dataset_hash_code_` and `RGB_another_hash_code_` folders). Go into the synthetic dataset folder and run the script: `python3 convert_dataset.py`

## Citation
```
@inproceedings{vasile2022,
    author    = {F. Vasile and E. Maiettini and G. Pasquale and A. Florio and N. Boccardo and L. Natale},
    title     = {Grasp Pre-shape Selection by Synthetic Training: Eye-in-hand Shared Control on the Hannes Prosthesis},
    booktitle = {2022 IEEE/RSJ International Conference on Intelligent Robots and Systems (IROS)},
    year      = {2022},
    month     = {Oct},
}
```
## Mantainer
This repository is mantained by:
| | |
|:---:|:---:|
| [<img src="https://github.com/FedericoVasile1.png" width="40">](https://github.com/FedericoVasile1) | [@FedericoVasile1](https://github.com/FedericoVasile1) |

## Related links:
- For further details about our synthetic data generation pipeline, please refer to our [paper](https://arxiv.org/abs/2203.09812) (specifically SEC. IV) and feel free to contact me: federico.vasile@iit.it
- A demonstration video of our model trained on the synthetic data and tested on the Hannes prosthesis is available [here](https://drive.google.com/file/d/16QcD1yprsNhxPc93EbLV_Mby2FlcJcJ7/view?usp=sharing)
- A presentation video summarizing our work is available [here](https://drive.google.com/file/d/1qy1HoTzGodUyE1Ao1ezXsuYVNoqWn7Gg/view?usp=sharing)
- The synthetic dataset used in our experiments is available for download [here](https://zenodo.org/record/7327516#.Y4Mu23bMKF5)
- Along with the synthetic data generation pipeline, we collected a real dataset, available for download [here](https://zenodo.org/record/7327150#.Y4Mu4HbMKF5)
- To reproduce our experiments, you need both the real and the synthetic dataset. To use our [experiments pipeline](https://github.com/hsp-iit/prosthetic-grasping-experiments), ensure that both datasets are in the correct format (we provide a script in the <i>experiments pipeline</i> to automatically download and correctly arrange both datasets).
