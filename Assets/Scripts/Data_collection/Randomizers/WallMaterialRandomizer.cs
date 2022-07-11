using System;
using UnityEngine.Perception.Randomization.Parameters;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers.Tags;
using UnityEngine.Perception.Randomization.Samplers;

namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Randomizes the material of wall tagged with a WallMaterialRandomizerTag
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Wall Material Randomizer")]
    public class WallMaterialRandomizer : Randomizer
    {
        /// <summary>
        /// The list of materials to sample and apply to target wall
        /// </summary>
        [Tooltip("The list of materials to sample and apply to target wall.")]
        public MaterialParameter materials;

        /// The sampler to get a sample float in range [0, 1], this will be used
        /// to decide whether to apply a random material to the object or not
        private UniformSampler samplerApplyMaterial;
        /// A float value between 0 and 1. The higher the value, the more likely it is
        /// that a material will be applied to the wall
        public float samplingRangeMaterial;
        // We need to store the original material, such that 
        // at the end of each iteration we can restore it (if changed)
        private Material originalObjMaterial = null;
        private string sampledObjMaterialName = null;

        // Since the gameobject we are using has several materials (instead
        // of one), and each one of these represent a different part, we have 
        // to know which is the right one (i.e. the one representing the wall)
        private string WALL_MATERIAL_NAME = "ConcreteHoles_Mat (Instance)";

        protected override void OnAwake()
        {
            samplerApplyMaterial = new UniformSampler(0, 1);
        }

        /// <summary>
        /// Randomizes the material of tagged wall at the start of each scenario iteration
        /// The randomization is performed only if the sampled float value
        /// falls within the user-defined sampling range.
        /// </summary>
        protected override void OnIterationStart()
        {
            var tags = tagManager.Query<WallMaterialRandomizerTag>();
            foreach (var tag in tags)
            {
                float sampledFloat = samplerApplyMaterial.Sample();
                if (sampledFloat <= samplingRangeMaterial)
                {
                    int materialsLength = tag.GetComponent<Renderer>().materials.Length;
                    Material[] newMaterials = new Material[materialsLength];
                    for (int i = 0; i < materialsLength; i++)
                    {
                        Material mat = tag.GetComponent<Renderer>().materials[i];
                        if (mat.name == WALL_MATERIAL_NAME)
                        {
                            originalObjMaterial = mat;

                            //tag.GetComponent<Renderer>().materials[i] = materials.Sample();
                            newMaterials[i] = materials.Sample();
                            sampledObjMaterialName = newMaterials[i].name + " (Instance)";
                        }
                        else
                        {
                            newMaterials[i] = mat;
                        }
                    }
                    if (originalObjMaterial == null)
                    {
                        Debug.LogError("Material " + WALL_MATERIAL_NAME + " not found in " +
                                       tag.name + " GameObject. Stopping execution . . .");
                        // Stop execution
                        UnityEditor.EditorApplication.isPlaying = false;
                    }

                    tag.GetComponent<Renderer>().materials = newMaterials;
                }
            }
        }

        protected override void OnIterationEnd()
        {
            if (originalObjMaterial != null)
            {
                var tags = tagManager.Query<WallMaterialRandomizerTag>();
                foreach (var tag in tags)
                {
                    int materialsLength = tag.GetComponent<Renderer>().materials.Length;
                    Material[] newMaterials = new Material[materialsLength];
                    for (int i = 0; i < materialsLength; i++)
                    {
                        Material mat = tag.GetComponent<Renderer>().materials[i];
                        if (mat.name == sampledObjMaterialName)
                        {
                            //tag.GetComponent<Renderer>().materials[i] = originalObjMaterial;
                            newMaterials[i] = originalObjMaterial;

                            originalObjMaterial = null;
                            sampledObjMaterialName = null;
                        }
                        else
                        {
                            newMaterials[i] = mat;
                        }
                    }
                    tag.GetComponent<Renderer>().materials = newMaterials;
                }
            }
        }

    }
}
