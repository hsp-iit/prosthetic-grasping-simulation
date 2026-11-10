#!/bin/bash
# Runs the data collection with a standalone Linux build of the project
# (see README, "Synthetic dataset generation with a Linux build").
#
# Usage: ./run_data_collection.sh PATH_TO_BUILD/DataCollection.x86_64 OUTPUT_FOLDER
#
# The player needs a window: Linux builds do not render cameras in -batchmode.
# The environment variables make the NVIDIA GPU render and present the frames
# on laptops with an integrated + NVIDIA GPU (PRIME render offload).
set -e

if [ $# -ne 2 ]; then
    echo "Usage: $0 PATH_TO_BUILD/DataCollection.x86_64 OUTPUT_FOLDER"
    exit 1
fi

PLAYER=$1
OUTPUT=$2
mkdir -p "$OUTPUT"

__NV_PRIME_RENDER_OFFLOAD=1 __VK_LAYER_NV_optimus=NVIDIA_only \
    "$PLAYER" -screen-fullscreen 0 -screen-width 1280 -screen-height 960 \
    -logFile "$OUTPUT/player.log" --output-path "$OUTPUT"
