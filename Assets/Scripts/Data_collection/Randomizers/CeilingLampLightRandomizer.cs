using System;
using UnityEngine.Perception.Randomization.Parameters;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers.Tags;
using UnityEngine.Perception.Randomization.Samplers;
using UnityEngine.Rendering.HighDefinition;

namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Randomizes the light of the ceiling lamp (it must be tagged 
    /// with CeilingLampLightRandomizerTag)
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Ceiling Lamp Light Randomizer")]
    public class CeilingLampLightRandomizer : Randomizer
    {
        public UniformSampler temperatureSampler;
        public UniformSampler intensitySampler;  

        // The ColorRGBparameter should be used instead of 
        // this, but it has a bug.
        public UniformSampler colorR;
        public UniformSampler colorG;
        public UniformSampler colorB;

        /// <summary>
        /// Randomizes the tagged of tagged ceiling lamps at the 
        /// start of each scenario iteration
        /// </summary>
        protected override void OnIterationStart()
        {
            float temperatureSampledValue = temperatureSampler.Sample();
            float intensitySampledValue = intensitySampler.Sample();
            Color lightColorSampledValue = new Color(
                colorR.Sample(), colorB.Sample(), colorG.Sample()
            );

            var tags = tagManager.Query<CeilingLampLightRandomizerTag>();
            foreach (var tag in tags)
            {
                Light ceilingLamp = tag.GetComponent<Light>();
                ceilingLamp.colorTemperature = temperatureSampledValue;

                ceilingLamp.color = lightColorSampledValue;

                HDAdditionalLightData lightData = ceilingLamp.GetComponent<HDAdditionalLightData>();
                lightData.intensity = intensitySampledValue;
            }
        }
    }
}
