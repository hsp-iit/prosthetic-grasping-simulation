using System;
using UnityEngine.Perception.Randomization.Parameters;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers.Tags;
using UnityEngine.Perception.Randomization.Samplers;

namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Randomizes the material of table tagged with a TableMaterialRandomizerTag
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Table Material Randomizer")]
    public class TableMaterialRandomizer : Randomizer
    {
        /// <summary>
        /// The list of materials to sample and apply to target table
        /// </summary>
        [Tooltip("The list of materials to sample and apply to target table.")]
        public MaterialParameter materials;

        /// The sampler to get a sample float in range [0, 1], this will be used
        /// to decide whether to apply a random material to the object or not
        private UniformSampler samplerApplyMaterial;
        /// A float value between 0 and 1. The higher the value, the more likely it is
        /// that a material will be applied to the table
        public float samplingRangeMaterial;
        // We need to store the original material, such that 
        // at the end of each iteration we can restore it (if changed)
        private Material originalObjMaterial = null;

        protected override void OnAwake()
        {
            samplerApplyMaterial = new UniformSampler(0, 1);
        }

        /// <summary>
        /// Randomizes the material of tagged table at the start of each scenario iteration
        /// The randomization is performed only if the sampled float value
        /// falls within the user-defined sampling range.
        /// </summary>
        protected override void OnIterationStart()
        {
            var tags = tagManager.Query<TableMaterialRandomizerTag>();
            foreach (var tag in tags)
            {
                float sampledFloat = samplerApplyMaterial.Sample();
                if (sampledFloat <= samplingRangeMaterial)
                {
                    originalObjMaterial = tag.GetComponent<Renderer>().material;
                    tag.GetComponent<Renderer>().material = materials.Sample();
                }
            }
        }

        protected override void OnIterationEnd()
        {
            if (originalObjMaterial != null)
            {
                var tags = tagManager.Query<TableMaterialRandomizerTag>();
                foreach (var tag in tags)
                {
                    float sampledFloat = samplerApplyMaterial.Sample();
                    if (sampledFloat <= samplingRangeMaterial)
                    {
                        tag.GetComponent<Renderer>().material = originalObjMaterial;
                        originalObjMaterial = null;
                    }
                }
            }
        }
    }
}
