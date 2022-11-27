
<div align="center">    

# Grasp Pre-shape Selection by Synthetic Training: <br>Eye-in-hand Shared Control on the Hannes Prosthesis 
</div>

<img src="https://img.shields.io/badge/Unity-2020.3.11f1-green.svg?style=flat-square" alt="Unity 2020.3.20f1"> <img src="https://img.shields.io/badge/Perception-0.11.2--preview.2-blue.svg?style=flat-square" alt="Perception 0.11.2-preview.2">



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
- The project uses Unity 2020.3.11.f1. Find the version [here](https://unity3d.com/get-unity/download/archive) and click on the `Unity Hub` button to download.
- It has been tested on Windows 10/11.
- All the necessary packages (e.g. [Perception](https://github.com/Unity-Technologies/com.unity.perception)) come pre-installed in the repository, therefore no installation step is required.

## Installation
- Install [Git for Windows](https://git-scm.com/download/win) (notice that this is a project called Git for Windows, which is not Git itself)
- Install [Git LFS](https://docs.github.com/en/repositories/working-with-files/managing-large-files/installing-git-large-file-storage). 
- Open a Command Prompt and run `git lfs install` to initialize it.<br>Then, clone the repository: `git clone https://github.com/hsp-iit/prosthetic-grasping-simulation`
- Git LFS has some problems with the file `Assets/Scene/SampleScene/LightingData.asset`. Therefore, download the original `LightingData.asset` file from [here](https://drive.google.com/file/d/1b3FNFQTLm2TPxQbnImntQfC2IqRy9eas/view?usp=sharing) and replace it.
- Go on the Unity Hub, click on Open and locate the downloaded repository.

## Synthetic dataset generation in Unity
- Once the project is open, ensure that the correct scene is selected: in the `Project` tab open `Assets\Scenes` and double-click on `Data_collection.unity` to open the scene.
- From the top bar menu, open `Edit -> Project Settings`
  - In `Project Settings`, search for `Lit Shader Mode` and set it to `Both`.
  ![lit_shader_mode](https://user-images.githubusercontent.com/50639319/192339142-3e17b12c-f81f-4828-ac54-b609185cb2d3.png)

  - In `Project Settings`, search for `Motion Blur` and disable it.
  ![motion_blur](https://user-images.githubusercontent.com/50639319/192340209-f4924a9e-977d-44c3-aee9-19729006eb70.png)

- [OPTIONAL] The pipeline generates the same number of videos for each _grasp type - object part_ pair (recall, there are currently 31 pairs). 50 videos are generated for each pair, resulting in 1550 videos. From the `Hierarchy` tab (left-hand side) click on `Simulation Scenario` and its properties will appear in the `Inspector` tab (right-hand side). Make sure that the value of `Fixed Length Scenario -> Scenario Properties -> Constants -> Iteration Count` is set to 1550 <u>and</u> the value of `Fixed Length Scenario -> Randomizers -> WristCameraMovement -> Num Iterations Per Grasp` is set to 50. If you want to generate a different number of videos, change these values accordingly. For instance, to generate 10 videos for each pair, set `Iteration Count` to 31*10=310 <u>and</u> `Num Iterations Per Grasp` to 10. If the numbers are not consistent, the execution stops.
- [OPTIONAL] To set the dataset output folder, go on `File -> Project Settings -> Perception` and click on the `Change Folder` button to set a net `Base Path`
- :rocket: Click on the play button on top to start collecting the synthetic dataset.
- [WARNING]: if you want to change settings, e.g., enable bounding box/semantic segmentation labeling or import your own objects, few settings need to be adjusted. These are not explained here for the sake of brevity, feel free to contact me (federico.vasile@iit.it) or open an issue and I will provide you all the instructions.
- When the simulation is over, go on the `Hierarchy` tab and select `WristCamera`. In the `Inspector` tab search for `Latest Generated Dataset` and click on `Show folder` to locate the dataset folder.

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
