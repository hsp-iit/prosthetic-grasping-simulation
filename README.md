
# Grasp Pre-shape Selection by Synthetic Training: <br>Eye-in-hand Shared Control on the Hannes Prosthesis 

<img src="https://img.shields.io/badge/unity-2020.3.11f1-green.svg?style=flat-square" alt="unity 2020.3.20f1">
&nbsp;
<img src="https://img.shields.io/badge/Production-0.11.2--preview.2-blue.svg?style=flat-square" alt="perception 0.11.2-preview.2">



<p align="center">
  <a href="https://arxiv.org/abs/2203.09812" style="font-size: 25px; text-decoration: none">Paper</a>
  &nbsp; &nbsp;
  <a href="" style="font-size: 25px; text-decoration: none">Demonstration videoTODO</a>
  &nbsp; &nbsp;
  <a href="" style="font-size: 25px; text-decoration: none">Presentation videoTODO</a>
  <br>
  <a href="" style="font-size: 25px; text-decoration: none">Synthetic dataset generatedTODO</a>
  &nbsp; &nbsp;
  <a href="" style="font-size: 25px; text-decoration: none">Real dataset collectedTODO</a>
  &nbsp; &nbsp;
  <a href="https://github.com/hsp-iit/prosthetic-grasping-experiments" style="font-size: 25px; text-decoration: none">Experiments repository</a>
  &nbsp; &nbsp;
  <br>
  <img src="https://user-images.githubusercontent.com/50639319/191701556-514403b3-4579-4cea-bafb-67aa4d0a20c8.gif">
</p>

We introduce a synthetic dataset generation pipeline designed for vision-based prosthetic grasping. The method supports multiple grasps per object by overlaying a transparent parallelepiped onto each object part to grasp. The camera follows a straight line towards the object part while recording the video. The scene, initial camera pose and object pose are randomized in each video. We used 15 objects from the YCB dataset. <br>_Our work is accepted to IROS 2022_

## Getting started
- The project uses Unity 2020.3.11.f1. Find the version [here](https://unity3d.com/get-unity/download/archive) and click on the `Unity Hub` button to download.
- It has been tested on Windows 10 and Ubuntu 20.
- All the necessary packages (e.g. [perception](https://github.com/Unity-Technologies/com.unity.perception)) come pre-installed in the repository, therefore no installation step is required.

## Installation
- Ensure that you have [Git LFS](https://docs.github.com/en/repositories/working-with-files/managing-large-files/installing-git-large-file-storage) installed.
- Clone the repository: `git clone https://github.com/hsp-iit/prosthetic-grasping-simulation`
- Go on the Unity Hub, click on Open and locate the downloaded repository.

## Synthetic dataset generation in Unity
- Once the project is open, ensure that the correct scene is selected: in the `Project` window open `Assets\Scenes` and double-click on `Data_collection.unity` to open the scene.
- Before running the simulation, remove motion blur and lit shader mode to both (TODO add further instructions).
- Click on the play button on top to start collecting the synthetic dataset.
- WARNING: if you want to change settings, e.g. enable bounding box or semantic segmentation labeling, import your own objects or change the number of videos collected, few settings need to be adjusted. These are not explained here for the sake of brevity, feel free to contact me (federico.vasile@iit.it) or open an issue and I will provide you all the instructions.

## Converting the generated videos into our own format
- When the simulation is over, you can find the labels (along with other metadata) as json files (`captures_***.json`) into the `Dataset023982da-0257-4541-9886-d22172b6c94c` folder (notice that this is an example folder, you will have a different hash code following the `Dataset` name). <br>All the video frames (`rgb_***.png`) are located under the `RGB_another_hash_code_` folder.
- We provide you a script to convert the frames and labels into the structure used by our [experiments pipeline](https://github.com/hsp-iit/prosthetic-grasping-experiments). Each video will be organized according to the following path: `DATASET_BASE_FOLDER/CATEGORY_NAME/OBJECT_NAME/PRESHAPE_NAME/Wrist_d435/rgb*/*.png`. <br>For example: `ycb_synthetic_dataset/dispenser/006_mustard_bottle/power_no3/Wrist_d435/rgb*/*.png`
- To run the script, go into `Assets/Scripts/Data_collection/PostProcessing_dataset` and copy `script_convert_dataset.py` into the folder of your synthetic dataset generated (i.e. the folder containing the `Dataset_hash_code_` and `RGB_another_hash_code_` folders). Go into the synthetic dataset folder and run the script: `python3 script_convert_dataset.py`

## Citation
```
@INPROCEEDINGS{vasile2022,
	author={F. Vasile and E. Maiettini and G. Pasquale and A. Florio and N. Boccardo and L. Natale},
	booktitle={2022 IEEE/RSJ International Conference on Intelligent Robots and Systems (IROS)},
	title={Grasp Pre-shape Selection by Synthetic Training: Eye-in-hand Shared Control on the Hannes Prosthesis},
	year={2022},
	month={Oct},
}
```
## Manteiner
This repository is manteined by:
| | |
|:---:|:---:|
| [<img src="https://github.com/FedericoVasile1.png" width="40">](https://github.com/FedericoVasile1) | [@FedericoVasile1](https://github.com/FedericoVasile1) |

## Related links:
- For further details about our synthetic data generation pipeline, please refer to our [paper](https://arxiv.org/abs/2203.09812) (specifically SEC. IV) and feel free to contact me: federico.vasile@iit.it
- A demonstration video of our model trained on the synthetic data and tested on the Hannes prosthesis is available [hereTODO]()
- A presentation video summarizing our work is available [hereTODO]()
- The synthetic dataset used in our experiments is available for download [hereTODO]()
- Along with the synthetic data generation pipeline, we collected a real dataset, available for download [hereTODO]()
- To reproduce our experiments  you need both the real and the synthetic dataset. To use our [experiments pipeline](https://github.com/hsp-iit/prosthetic-grasping-experiments), ensure that both datasets are in the correct format. If you download our datasets, they already are in the correct format. Otherwise, to convert your synthetic dataset generated into our own format, use the script discussed above.