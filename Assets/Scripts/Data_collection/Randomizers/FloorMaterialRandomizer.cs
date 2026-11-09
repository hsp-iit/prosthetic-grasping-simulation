using System;
using UnityEngine.Perception.Randomization.Parameters;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers.Tags;
using UnityEngine.Perception.Randomization.Samplers;

namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Randomizes the material of floor tagged with a FloorMaterialRandomizerTag
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Floor Material Randomizer")]
    public class FloorMaterialRandomizer : Randomizer
    {
        /// <summary>
        /// The list of materials to sample and apply to target floor
        /// </summary>
        [Tooltip("The list of materials to sample and apply to target floor.")]
        public CategoricalParameter<Material> materials;

        /// The sampler to get a sample float in range [0, 1], this will be used
        /// to decide whether to apply a random material to the object or not
        private UniformSampler samplerApplyMaterial;
        /// A float value between 0 and 1. The higher the value, the more likely it is
        /// that a material will be applied to the floor
        public float samplingRangeMaterial;
        // We need to store the original material, such that 
        // at the end of each iteration we can restore it (if changed)
        private Material originalObjMaterial = null;
        // Index, in the renderer's material slots, of the material replaced in this iteration
        private int replacedSlot = -1;

        // Since the gameobject we are using has several materials (instead
        // of one), and each one of these represent a different part, we have
        // to know which is the right one (i.e., the one representing the floor)
        private string FLOOR_MATERIAL_NAME = "MarbleFlooring_Mat";

        protected override void OnAwake()
        {
            samplerApplyMaterial = new UniformSampler(0, 1);
        }

        /// <summary>
        /// Randomizes the material of tagged floor at the start of each scenario iteration
        /// The randomization is performed only if the sampled float value
        /// falls within the user-defined sampling range.
        /// Works on sharedMaterials: reading Renderer.materials would create a new copy of
        /// every slot holding a material asset at each iteration, and those copies are never destroyed
        /// </summary>
        protected override void OnIterationStart()
        {
            var tags = tagManager.Query<FloorMaterialRandomizerTag>();
            foreach (var tag in tags)
            {
                float sampledFloat = samplerApplyMaterial.Sample();
                if (sampledFloat <= samplingRangeMaterial)
                {
                    Renderer renderer = tag.GetComponent<Renderer>();
                    Material[] newMaterials = renderer.sharedMaterials;
                    for (int i = 0; i < newMaterials.Length; i++)
                    {
                        Material mat = newMaterials[i];
                        if (mat != null && StripInstanceSuffix(mat.name) == FLOOR_MATERIAL_NAME)
                        {
                            originalObjMaterial = mat;
                            replacedSlot = i;
                            newMaterials[i] = materials.Sample();
                        }
                    }
                    if (originalObjMaterial == null)
                    {
                        Debug.LogError("Material " + FLOOR_MATERIAL_NAME + " not found in " +
                                       tag.name + " GameObject. Stopping execution . . .");
                        // Stop execution
                        SimulationStopper.Stop();
                    }

                    renderer.sharedMaterials = newMaterials;
                }
            }
        }

        protected override void OnIterationEnd()
        {
            if (originalObjMaterial != null)
            {
                var tags = tagManager.Query<FloorMaterialRandomizerTag>();
                foreach (var tag in tags)
                {
                    Renderer renderer = tag.GetComponent<Renderer>();
                    Material[] newMaterials = renderer.sharedMaterials;
                    if (replacedSlot >= 0 && replacedSlot < newMaterials.Length)
                    {
                        newMaterials[replacedSlot] = originalObjMaterial;
                        renderer.sharedMaterials = newMaterials;
                    }
                }
                originalObjMaterial = null;
                replacedSlot = -1;
            }
        }

        /// The material may be an instance created by an earlier access to Renderer.materials
        static string StripInstanceSuffix(string name)
        {
            const string suffix = " (Instance)";
            return name.EndsWith(suffix) ? name.Substring(0, name.Length - suffix.Length) : name;
        }

    }
}
