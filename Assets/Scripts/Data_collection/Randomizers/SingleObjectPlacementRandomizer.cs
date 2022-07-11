using System;
using System.Linq;
using UnityEngine.Perception.Randomization.Parameters;
using UnityEngine.Perception.Randomization.Randomizers.Utilities;
using UnityEngine.Perception.Randomization.Samplers;

namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// It receives a plane and put on it randomly an object from a 
    /// given list of prefabs. This is done for every iteration.
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Single Object Placement Randomizer")]
    public class SingleObjectPlacementRandomizer : Randomizer
    {
        /// <summary>
        /// The list of prefabs from which to sample one object per iteration
        /// </summary>
        [Tooltip("The list of prefabs from which to sample one object per iteration.")]
        public GameObjectParameter prefabs;

        /// <summary>
        /// The plane onto which randomly placing the object 
        /// </summary>
        [Tooltip("The plane onto which randomly placing the object.")]
        public GameObject plane;
        private float rangeX;
        private float rangeZ;

        private UniformSampler samplerX;
        private UniformSampler samplerZ;

        private GameObject m_Container;
        private GameObjectOneWayCache m_GameObjectOneWayCache;

        protected override void OnAwake()
        {
            // This is a fake object, it's only purpose is to keep things
            // organized, i.e. we will pass this object to the constructor
            // of GameObjectOneWayCache so that all the objects that will 
            // be sampled will be child of this object.
            m_Container = new GameObject("Foreground Object");
            
            // scenario is an attribute of the class Randomizer, it's
            // a reference to the scenario containing this randomizer.
            // Actually, the created GameObject has the world as parent, 
            // we modify here the parent making it the scenario
            m_Container.transform.parent = scenario.transform;

            // It's the pool of prefabs where at each iteration we will
            // sample from.
            m_GameObjectOneWayCache = new GameObjectOneWayCache(
                m_Container.transform, prefabs.categories.Select(element => element.Item1).ToArray(), this);

            rangeX = plane.GetComponent<Renderer>().bounds.extents.x;
            rangeZ = plane.GetComponent<Renderer>().bounds.extents.z;
            samplerX = new UniformSampler(-rangeX, rangeX);
            samplerZ = new UniformSampler(-rangeZ, rangeZ);
        }

        /// <summary>
        /// Sample a GameObject and put it on a random position onto the plane
        /// </summary>
        protected override void OnIterationStart()
        {           
            // Sample and instantiate object
            GameObject instance = m_GameObjectOneWayCache.GetOrInstantiate(prefabs.Sample());

            // Sample random point onto the plane
            float sampleX = samplerX.Sample();
            float sampleZ = samplerZ.Sample();
            // Compute the y offset necessary to put the object onto the plane:
            //  get the distance from the object reference system to the bottom of the object
            float y_offset = instance.transform.Find("textured").gameObject.GetComponent<Renderer>().bounds.extents.y;

            // Place the object onto the plane
            Vector3 instancePlacement = plane.transform.position + new Vector3(sampleX, y_offset, sampleZ);
            instance.transform.position = instancePlacement;
        }

        /// <summary>
        /// Deletes generated foreground object after each scenario iteration is complete
        /// </summary>
        protected override void OnIterationEnd()
        {
            m_GameObjectOneWayCache.ResetAllObjects();
        }
    }
}
