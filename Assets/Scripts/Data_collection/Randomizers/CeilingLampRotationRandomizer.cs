using System;
using UnityEngine.Perception.Randomization.Parameters;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers.Tags;
using UnityEngine.Perception.Randomization.Samplers;

namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Randomizes the rotation of the ceiling lamp (it must be tagged 
    /// with CeilingLampRotationRandomizerTag)
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Ceiling Lamp Rotation Randomizer")]
    public class CeilingLampRotationRandomizer : Randomizer
    {
        private Vector3Parameter rotation = new Vector3Parameter
        {
            //x = new UniformSampler(-18.22f, 24.902f),
            x = new UniformSampler(-21, 30),
            y = new ConstantSampler(0),
            //z = new UniformSampler(-19.398f, 0)
            z = new UniformSampler(-22, 0)
        };

        /// <summary>
        /// Randomizes the rotation of tagged ceiling lamp at the 
        /// start of each scenario iteration
        /// </summary>
        protected override void OnIterationStart()
        {
            var tags = tagManager.Query<CeilingLampRotationRandomizerTag>();
            foreach (var tag in tags)
            {
                tag.transform.localRotation = Quaternion.Euler(rotation.Sample());
            }
        }
    }
}
