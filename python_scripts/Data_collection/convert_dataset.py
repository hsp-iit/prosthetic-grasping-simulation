# In the json file captures_xxx.json, the captures field is a list,
# where item i contains all of the info about i-th frame captured.
# Currently, the fields of interest in each one of these
# items are the following:
#   - step: it is an int representing the frame number within 
#           the sequence, i.e., from 0 to 89 in our case
#   - filename: the path to the rgb frame
#   - annotations: a list where each item is a labeler 
#                  (e.g. bounding box, grasp type, etc..)
#                  - for each item our fields of interest are
#                    the following ones:
#                    - annotation definition: contains the id of 
#                      the labeler
#                    - values: a list containing the labels
#
# We currently want to store only the grasp type label, in the same
# format as our real dataset
# folder structure of real dataset: 
#   iHannesDataset/$object_name$/$instance_name$/$preshape_name$/$view_name$/rgb_*****/********.jpg
#
# This file has to be placed under the base folder of the generated 
# synthetic dataset. 
# For instance: 
#      2eb88f2d-53e9-4eb1-8f22-67be14bc47aa   (sample name of the base folder, it is an hash)
#      |- Dataset****
#      |   |- captures_000.json
#      |   |- captures_001.json
#      |   |- captures_***.json
#      |- RGB****
#      |   |- rgb_2.png
#      |   |- rgb_3.png
#      |   |- rgb_*.png
#      |- script_convert_dataset.py

import json
import os
import glob
import argparse


def main(args):
    launching_dir = os.path.basename(os.getcwd())
    file_dir = os.path.basename(os.path.dirname(__file__))
    if launching_dir != file_dir:
        raise Exception(
            'The script must be run from the folder where it is.'
            ' E.g., given folder_name, you must '
            'run cd folder_name; python script_convert_dataset.py.\n'
            'You can not do python dataset_folder_name/script_convert_dataset.py'
        )
    folder_w_captures_files = os.path.basename(os.getcwd())
    folder_w_captures_files = glob.glob('Dataset*')
    if len(folder_w_captures_files) != 1:
        raise Exception('No Dataset* folder found. Did you put the script in '
                        'the right folder?')
    folder_w_rgbs = glob.glob('RGB*')
    if len(folder_w_rgbs) != 1:
        raise Exception('No RGB* folder found. Did you put the script in '
                        'the right folder?')

    # Retrieve the id of grasp type labeler
    GRASP_TYPE_ANNOTATION_DEFINITION = 'GraspTypeAnnotationDefinition'

    json_files = glob.glob(os.path.join('Dataset*', 'captures*.json'))
    for i in range(len(json_files)):    
        json_filename = 'captures_' + str(i).zfill(3) + '.json'
        appo = os.path.basename(json_files[i])
        json_filename = json_files[i].replace(appo, json_filename)
        with open(json_filename, 'r') as f:
            captures = json.load(f)['captures']

            # Iterate over each frame
            for _, cap in enumerate(captures): 
                step = cap['step']
                filename = cap['filename']
            
                if not os.path.exists(os.path.join(os.getcwd(), filename)):
                    print('The file {} does not exist at the path {}, '
                          'therefore skipping it.'.format(filename, os.getcwd()))
                    continue 

                annotations = cap['annotations']
                # Search for the grasp type labeler
                flag = False
                for ann in annotations:
                    if ann['model_type'] != GRASP_TYPE_ANNOTATION_DEFINITION:
                        continue
                    flag = True
                    metadatas = ann['grasp_type_values']
                    # Parameters sampled for the sequence (not available
                    # in datasets generated before they were reported)
                    sequence_parameters = ann.get('sequence_parameters')

                    object_name = metadatas['object_name']
                    instance_name = metadatas['instance_name']
                    preshape = metadatas['preshape_name']
                    grasp_type = metadatas['grasp_type_name']
                    view = metadatas['view_type_name'].strip()

                    if view == 'Wrist':
                        # Replace with Wrist_d435 in order to have the 
                        # same base folder as in the real dataset 
                        view = 'Wrist_d435'
                    else:
                        raise RuntimeError(
                            'Unexpected view_type field encountered. '
                            'Expected Wrist but {} found.'.format(view)    
                        )
                if not flag:
                    # Perception 1.0 captures one frame before the first
                    # iteration starts, when no object is in the scene yet
                    print('No grasp type annotation ({}) for {}, therefore '
                          'skipping it.'.format(GRASP_TYPE_ANNOTATION_DEFINITION,
                                                filename))
                    continue
                
                # Construct path as the real dataset one
                partial_new_path = os.path.join(args.output_base_folder, 
                                                object_name,
                                                instance_name,
                                                preshape,
                                                view)
    
                if step == 0:
                    if os.path.isdir(partial_new_path):
                        num_dirs = len(os.listdir(partial_new_path))
                        new_path = os.path.join(partial_new_path, 
                                                'rgb_'+(str(num_dirs).zfill(5))) 
                        os.mkdir(new_path)
                    else: 
                        new_path = os.path.join(partial_new_path, 'rgb') 
                        os.makedirs(new_path)

                num_dirs = len(os.listdir(partial_new_path))
                new_path = os.path.join(
                    partial_new_path,
                    'rgb' if num_dirs==1 else 'rgb_'+(str(num_dirs-1).zfill(5))
                )

                img_extension = filename.split('.')[-1]
                complete_path = os.path.join(
                    new_path, str(step).zfill(8) + '.' + img_extension
                )

                os.rename(os.path.join(os.getcwd(), filename), 
                          os.path.join(os.getcwd(), complete_path))
                #copyfile(os.path.join(os.getcwd(), filename), 
                #         os.path.join(os.getcwd(), complete_path))

                # Save also metadata/data.log file, in this way we can completely
                # reuse the pytorch dataset class previously designed 
                # for the real dataset

                path_to_metadata = complete_path.replace(view, 'metadata').replace('rgb', 'seq')
                img_name = os.path.basename(path_to_metadata)
                path_to_metadata = path_to_metadata.replace(img_name, '')

                if not os.path.isdir(path_to_metadata):
                    os.makedirs(path_to_metadata)
                    # Create and write data.log file
                    with open(os.path.join(path_to_metadata, 'data.log'), 'w') as data_log:
                        # sample row from the real dataset:
                        #  frame_id timestamp_in timestamp_out object instance grasp_type preshape elevation approach ojbAzimuth objElevation
                        out_data_log = 'junk junk junk {} {} {} {} elevation_0 approach_0 objAzimuth_0 objElevation_+90'.format(
                            object_name, instance_name, grasp_type, preshape
                        )
                        data_log.write(out_data_log)
                    # Keep also the parameters sampled for the sequence, 
                    # e.g. to analyze the distribution of the generated data
                    if sequence_parameters is not None:
                        with open(os.path.join(path_to_metadata, 'sequence_parameters.json'), 'w') as f_params:
                            json.dump(sequence_parameters, f_params, indent=4)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument(
        '--output_base_folder', type=str, default='iHannesDataset_synthetic'
    )
    args = parser.parse_args()

    print('Creating {} folder and moving rgb frames there. Each video is '
          'arranged according to the following structure:'
          '{}/$object_name$/$instance_name$/$preshape_name$/Wrist_d435/rgb_*****/********.png'
          .format(args.output_base_folder, args.output_base_folder))

    main(args)