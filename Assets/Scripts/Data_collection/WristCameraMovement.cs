using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.Perception.Randomization.Scenarios;
using UnityEngine.Perception.Randomization.Samplers;
using UnityEngine.Perception.Randomization.Parameters;
using UnityEngine.Perception.Randomization.Randomizers.Utilities;

namespace UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers
{
    /// <summary>
    /// Move the camera towards the grasp parallelepiped of the object.
    /// Before starting the sequence, three sequence-dependant
    /// randomizations occur:
    /// (1) object placement
    /// (2) object rotation
    /// (3) wrist camera placement
    /// After that, check if the grasp sequence is feasible. If it is, 
    /// execute it, otherwise re-sample (1),(2),(3) and retry.
    /// 
    /// WARNING: this Randomizer must be putted as the LAST ONE in the 
    /// simulation scenario, since all the sequence-dependent randomizations
    /// must happen before this one.
    /// </summary>
    [Serializable]
    [AddRandomizerMenu("Perception/Wrist Camera Movement")]
    public class WristCameraMovement : Randomizer
    {
        /* This class inherits from Randomizer (even if it doesn't have 
         * the exact same purpose of other randomizers) just for sake of 
         * convenience: indeed in this way we are sure that by putting it 
         * as the last one in the Simulation Scenario, we are sure that 
         * all the sequence-dependant (related in terms of the feasibility
         * of the grasp) randomizers happen before this randomizer
         */

        // The camera to be moved towards the object part to grasp
        public GameObject wristCamera;

        private float framesCounter;

        private Vector3 straightLineDirection;
        
        // The wrist camera, starting from a certain time (randomly sampled) and
        // for a certain duration (randomly sampled), will start performing a 
        // pronation-supination of a percenatage (randomly sampled) of the 
        // whole angle
        private float psAngle;
        /// <summary>
        /// At which frame the wrist camera will start performing the pronation-supination
        /// </summary>
        public UniformSampler psRotationStartingSampler;
        private float psRotationStartingSampledValue;
        /// <summary>
        /// The frame duration required to complete the pronation-supination
        /// </summary>
        public UniformSampler psRotationDurationSampler;
        private float psRotationDurationSampledValue;
        /// <summary>
        /// The percentage of the total pronation-supination angle to rotate
        /// </summary>
        public UniformSampler samplerPsPercentageRotation;

        // The wrist camera, starting from a certain time (randomly sampled)
        // will start performing a flexion-extension until the end of the sequence
        private float feAngle;
        /// <summary>
        /// At which frame the wrist camera will start perfoming the flexion-extension.
        /// </summary>
        public UniformSampler feRotationStartingSampler;
        private float feRotationStartingSampledValue;

        // The target point (within the parallelepiped) towards which the 
        // wrist camera will perform the wrist flexion-extension.
        // E.g., it can be the center of the parallelepiped, the upper
        // or the lower part of the parallelepiped, or any other randomly
        // sampled point onto the parallelepiped
        private Vector3 targetFePoint;
        // To randomly sample which is the target point of the straight trajectory.
        // It can be the upper or the lower part (along z aixs) of the grasp parallelepiped.
        // The wrist flexion-extension will be perfomed towards (looking at) the other point
        private UniformSampler samplerTargetPoint;

        private bool isPositive;

        private float framesPerIteration;

        // The 2d points describing the minimum jerk straight line trajectory
        private List<Tuple<float, float>> minimumJerkPoints;

        // Since there is a little displacement between the reference system of the camera 
        // and where the camera is placed
        private float REFERENCE_TO_CAMERA_OFFSET = 0.2f;

        /// <summary>
        /// The plane onto which randomly place the wrist camera before starting the sequence
        /// </summary>
        public GameObject planeWristPlacement;
        private UniformSampler samplerYwristPlacement;
        private UniformSampler samplerZwristPlacement;

        /// <summary>
        /// The list of prefabs from which to sample one object per iteration
        /// </summary>
        [Tooltip("The list of prefabs from which to sample one object per iteration.")]
        public GameObjectParameter prefabs;

        private GameObject m_Container;
        private GameObjectOneWayCache m_GameObjectOneWayCache;

        private GameObject objInScene;

        /// <summary>
        /// The plane onto which randomly place the object
        /// </summary>
        public GameObject planeObjectPlacement;
        private UniformSampler samplerXobjectPlacement;
        private UniformSampler samplerZobjectPlacement;

        private UniformSampler samplerYobjectRotation;
	    private UniformSampler samplerUpsideDown;

        /// <summary>
        /// It is needed to count the number of iterations for the 
        /// object-grasp pair.
        /// When iterationsCounter == numIterationsPerGrasp, move to the next
        /// object-grasp pair.
        /// </summary>
        private int iterationsCounter = -1;
        public int numIterationsPerGrasp;

        private List<Tuple<int, int>> objAndGraspPairs;

        private GameObject graspToExecute;

        /// <summary>
        /// The list of materials to sample and apply to target objects
        /// </summary>
        [Tooltip("The list of materials to sample and apply to target objects.")]
        public MaterialParameter materialsObject;
        /// The sampler to get a sample float in range [0, 1]. This will be used
        /// to decide whether to apply a random material to the object or not.
        private UniformSampler samplerApplyMaterialObject;
        /// <summary>
        /// A float value between 0 and 1. The higher the value, the more likely it is
        /// that a random material will be applied to the object
        /// </summary>
        public float samplingRangeObjectMaterial;
        // We need to store the original material, such that 
        // at the end of each sequence we can restore it (if changed)
        private Material originalObjMaterial = null;

        // Samplers for the objectmaterials (when applied to the object) to randomly 
        // change the metallic and smoothness properties (within a 
        // certain range of values) of the sampled material 
        private UniformSampler samplerObjectMetallicProperty;
        private UniformSampler samplerObjectSmoothnessProperty;
        private float objectMetallicBaseValue;
        private float objectSmoothnessBaseValue;

        /// <summary>
        /// Put here the hand obj. We will randomize the pose 
        /// and texture of this object 
        /// </summary>
        public GameObject hand;
        // The sampler to choose whether to put the hand in view or not. 
        // If the sampled value ([0, 1] value) is <= samplingRangeHand then 
        // the hand is putted into view
        private UniformSampler samplerApplyHandRandomization;
        /// <summary>
        /// A float value between 0 and 1. The higher the value, the more likely it is
        /// that the hand will be putted in view in a random position
        /// </summary>
        public float samplingRangeHandInView;
        // The starting pose for the hand to be in view
        private Vector3 initialHandPosition;
        private Quaternion initialHandRotation;
        // Starting from the initial pose of the hand in view, it will be randomized 
        // based on the following samplers
        private UniformSampler samplerHandPositionX;
        private UniformSampler samplerHandPositionY;
        private UniformSampler samplerHandPositionZ;
        private UniformSampler samplerHandRotationX;
        private UniformSampler samplerHandRotationY;
        private UniformSampler samplerHandRotationZ;

        /// <summary>
        /// The list of materials to sample and apply to the hand 
        /// </summary>
        [Tooltip("The list of materials to sample and apply to the hand")]
        public MaterialParameter materialsHand;
        private UniformSampler samplerApplyMaterialHand;
        /// <summary>
        /// A float value between 0 and 1. The higher the value, the more likely it is
        /// that a random material will be applied to the hand
        /// </summary>
        public float samplingRangeHandMaterial;
        // We need to store the original material, such that 
        // at the end of each sequence we can restore it (if changed)
        private Material originalHandMaterial = null;


        private void randomHandTransform()
        {
            float sampledFloat = samplerApplyHandRandomization.Sample();
            if (sampledFloat <= samplingRangeHandInView)
            {
                // Store the current pose of the hand (i.e., the one set in the editor), 
                // since at the end of the sequence we will need to set it back
                initialHandPosition = hand.transform.localPosition;
                initialHandRotation = hand.transform.localRotation;

                Vector3 position = Vector3.zero;
                Quaternion rotation = Quaternion.Euler(0, 0, 0);

                if (hand.name.Contains("Arm02"))
                {
                    // TODO: export local position and rotation values
                    //       in external variable, and add also the scale.
                    // Set the initial position of the hand in view
                    //hand.transform.localPosition = new Vector3(0.04f, -0.008f, 0.2f);                 
                    //hand.transform.localRotation = Quaternion.Euler(-50f, -79f, -27f);

                    // WARNING: The two lines above are commented out since we take the 
                    //          pose set in the editor (instead of hard-coding the
                    //          initial pose here) as the initial pose from which 
                    //          randomizing. However, notice that the ranges for 
                    //          randomization set here in the script DEPEND on
                    //          the initial pose value set in the editor.

                    // Now randomize position and rotation of the hand
                    position = hand.transform.localPosition;
                    position.x += samplerHandPositionX.Sample();
                    position.y -= samplerHandPositionY.Sample();
                    position.z += samplerHandPositionZ.Sample();
                    rotation = hand.transform.localRotation;
                    rotation *= Quaternion.Euler(samplerHandRotationX.Sample(), 0, 0);
                    rotation *= Quaternion.Euler(0, samplerHandRotationY.Sample(), 0);
                    rotation *= Quaternion.Euler(0, 0, samplerHandPositionZ.Sample());

                    // TODO: RANDOMIZE ALSO THE SCALE????
                }
                else if (hand.name.Contains("Hannes_ARM"))
                {
                    Debug.LogError("Hannes_ARM random hand transformation not implemented yet");
                    UnityEditor.EditorApplication.isPlaying = false;
                }
                else
                {
                    Debug.LogError("Unknown hand model");
                    UnityEditor.EditorApplication.isPlaying = false;
                }

                hand.transform.localPosition = position;
                hand.transform.localRotation = rotation;
            }
        }

        private void randomHandMaterial()
        {
            float sampledFloat = samplerApplyMaterialHand.Sample();
            if (sampledFloat <= samplingRangeHandMaterial)
            {
                if (hand.name.Contains("Arm02"))
                {
                    originalHandMaterial = hand.GetComponent<Renderer>().material;
                    hand.GetComponent<Renderer>().material = materialsHand.Sample();
                }
                else if (hand.name.Contains("Hannes_ARM"))
                {
                    Debug.LogError("Hannes_ARM random hand material not implemented yet");
                    UnityEditor.EditorApplication.isPlaying = false;
                }
                else
                {
                    Debug.LogError("Unknown hand model");
                    UnityEditor.EditorApplication.isPlaying = false;
                }
            }
        }


        private void randomObjectMaterial(GameObject instance)
        {
            float sampledFloat = samplerApplyMaterialObject.Sample();
            if (sampledFloat <= samplingRangeObjectMaterial)
            {
                originalObjMaterial = instance.transform.Find("textured")
                    .GetComponent<Renderer>().material;
                Material sampledMaterial = materialsObject.Sample();
                instance.transform.Find("textured")
                    .GetComponent<Renderer>().material = sampledMaterial;

                // Modify the metallic and smoothness of the material
                float sampledMetallicValue = samplerObjectMetallicProperty.Sample();
                float sampledSmoothnessValue = samplerObjectSmoothnessProperty.Sample();
                sampledMaterial.SetFloat("_Metallic", 
                                         objectMetallicBaseValue + sampledMetallicValue);
                sampledMaterial.SetFloat("_Smoothness", 
                                         objectSmoothnessBaseValue + sampledSmoothnessValue);
            }
        }

        private void randomObjectPlacement(GameObject instanceToPlace, GameObject graspToExecute)
        {
            // Sample random point onto the plane..
            float sampleX = samplerXobjectPlacement.Sample();
            float sampleZ = samplerZobjectPlacement.Sample();

            // ..but, regarding some objects, set a fixed x value
            // in order to put the object on the table border
	        if (instanceToPlace.name.Contains("048_hammer")
	            || instanceToPlace.name.Contains("029_plate")
                || (instanceToPlace.name.Contains("026_sponge")
                    && graspToExecute.name.Contains("adducted_thumb"))
                || (instanceToPlace.name.Contains("037_scissors")
                    && graspToExecute.name.Contains("adducted_thumb"))
                || (instanceToPlace.name.Contains("033_spatula")
                    && graspToExecute.name.Contains("adducted_thumb"))
                || instanceToPlace.name.Contains("book") 
                || instanceToPlace.name.Contains("book_opened")
                || instanceToPlace.name.Contains("notebook") 
                || instanceToPlace.name.Contains("wallet_opened")
                || instanceToPlace.name.Contains("wallet_closed")
                || instanceToPlace.name.Contains("paintbrush(Clone)"))
	        {
                // Put the object on the border of the table
                sampleX = - planeObjectPlacement.GetComponent<Renderer>().bounds.extents.x - 0.08f;
            }

            // Compute the y offset necessary to have the object standing 
            // on the table (i.e., neither on air nor inside the table).
            // Get the distance from the origin of the object reference frame to
            // the lowest part of the object.
            float y_offset = instanceToPlace.transform.Find("textured").gameObject.
                GetComponent<Renderer>().bounds.extents.y;

            // Place the object onto the plane
            Vector3 instancePlacement = planeObjectPlacement.transform.position 
                                        + new Vector3(sampleX, y_offset, sampleZ);
            instanceToPlace.transform.position = instancePlacement;
        }

        private void randomObjectRotation(GameObject instanceToRotate, GameObject graspToExecute)
        {
	        float z;
	        if (graspToExecute.name.Contains("[usd]"))
	        {
		        // In this case we put the object upside down since the considered 
		        // grasp makes sense only in this way
	            z = 180.0f;
	        }
	        else
            {
	            if (graspToExecute.name.Contains("[susd]"))
	            {
                    // This grasp can be executed in both ways (i.e., standing or upside down),
                    // hence sample in which of the two ways to put the object
	                float sampleUpsideDown = samplerUpsideDown.Sample();
                    float THRESHOLD = 0.5f;
	                if (sampleUpsideDown > THRESHOLD) 
	                {
		                z = 180.0f;
	                } 
		            else 
	                {
		                z = 0.0f;
		            } 
		        }
		        else
		        {
	                // This grasp can not be executed with the object upside down
		            z = 0.0f;
		        }
	        }

	        // Sample the Y rotation of the object
	        float sampledYrotation = samplerYobjectRotation.Sample();
            
	        // Apply the overall rotation
	        Vector3 rotation = new Vector3(0, sampledYrotation, z);
	        instanceToRotate.transform.rotation = Quaternion.Euler(rotation); 
        }

        private void randomWristCameraPlacement()
        {
            // Put the camera in an initial random position onto the plane
            float sampleY = samplerYwristPlacement.Sample();
            float sampleZ = samplerZwristPlacement.Sample();
            Vector3 wristCameraSampledPosition = new Vector3(0, sampleY, sampleZ)
                                                 + planeWristPlacement.transform.position;

            // Currently, we do not care about the rotation since we will set it 
            // later by looking at the target point of the grasp parallelepiped.
            // Hence only the position is set here 
            wristCamera.transform.position = wristCameraSampledPosition;
        }

        private List<Tuple<float, float>> minimumJerkStraightLineTrajectory(float x0, float y0, float xf, float yf)
        {
            /* x0 and y0 are the initial hand coordinates
             * xf and yf are the final hand coordinates
             * The method returns the list of points, each points is the offset w.r.t. the previous point
             */

            List<Tuple<float, float>> points = new List<Tuple<float, float>>();

            float sumXcomponent = 0;
            float sumYcomponent = 0;
            for (int i = 1; i < (framesPerIteration + 1); i++)
            {
                float tau = ((float)i) / framesPerIteration;

                float x_component = x0 + (x0 - xf) * (15f*(float)Math.Pow(tau, 4d) - 6f*(float)Math.Pow(tau, 5d) - 10f*(float)Math.Pow(tau, 3d));
                float y_component = y0 + (y0 - yf) * (15f*(float)Math.Pow(tau, 4d) - 6f*(float)Math.Pow(tau, 5d) - 10f*(float)Math.Pow(tau, 3d));

                // Represent the point as subsequent offset w.r.t. the previous point
                x_component -= sumXcomponent;
                y_component -= sumYcomponent;

                points.Add(new Tuple<float, float>(x_component, y_component));

                sumXcomponent += x_component;
                sumYcomponent += y_component;
            }

            return points;
        }

        protected override void OnAwake()
        {
            // Get the sequence length
            framesPerIteration = (float)((FixedLengthScenario)scenario).constants.framesPerIteration;

            // Get the plane ranges onto which randomly put the camera
            float rangeYwrist = planeWristPlacement.GetComponent<Renderer>().bounds.extents.y;
            float rangeZwrist = planeWristPlacement.GetComponent<Renderer>().bounds.extents.z;
            samplerYwristPlacement = new UniformSampler(-rangeYwrist, rangeYwrist);
            samplerZwristPlacement = new UniformSampler(-rangeZwrist, rangeZwrist);

            // Get the plane ranges onto which randomly put the object
            float rangeXobject = planeObjectPlacement.GetComponent<Renderer>().bounds.extents.x;
            float rangeZobject = planeObjectPlacement.GetComponent<Renderer>().bounds.extents.z;
            samplerXobjectPlacement = new UniformSampler(-rangeXobject, rangeXobject);
            samplerZobjectPlacement = new UniformSampler(-rangeZobject, rangeZobject);

            // Pool of prefabs
            m_Container = new GameObject("Foreground Object");
            m_Container.transform.parent = scenario.transform;
            m_GameObjectOneWayCache = new GameObjectOneWayCache(
                m_Container.transform, 
                prefabs.categories.Select(element => element.Item1).ToArray(),
                this
            );

            // Sample object structure (in terms of GameObject names) when you import 
            // a YCB model in the Unity game view
            //  default
            //  |- textured
            //  |- |- large_diameter[right]
            //  |- |- large_diameter[left]
            //  |- |- tripod

            // Given the list of prefabs, construct a list of (idxObj,idxGraspInObj) tuple
            // where:
            // - idxObj is the index of the object in the prefabs list
            // - idxGraspInObj is the index of the grasp in the children hierarchy
            // Furthermore, due to how the GraspTypeLabeler works, during the sequence
            // all the grasps of all the objects must be deactivated, except the one
            // corresponding to the label of that sequence. Hence deactivate all grasps
            // here, they will be activated one at a time later
            objAndGraspPairs = new List<Tuple<int, int>>();
            GameObject[] objects = prefabs.categories.Select(element => element.Item1).ToArray();
            for (int idxObj = 0; idxObj < objects.Length; idxObj++)
            {
                Transform children = objects[idxObj].transform.Find("textured");
                for (int idxGraspInObj = 0; idxGraspInObj < children.childCount; idxGraspInObj++)
                {
                    children.GetChild(idxGraspInObj).gameObject.SetActive(false);
                    objAndGraspPairs.Add(Tuple.Create(idxObj, idxGraspInObj));
                }
            }

            FixedLengthScenario castedScenario = (FixedLengthScenario)scenario;
            int totalIterations = castedScenario.constants.iterationCount - castedScenario.constants.startIteration;
            if (objAndGraspPairs.Count * numIterationsPerGrasp != totalIterations)   
            {
                Debug.LogError("Wrong value assigned in FixedLengthScenario for " +
                               "startIteration and iterationCount fields: " +
                               "there are " + objAndGraspPairs.Count + 
                               " object-grasp pairs, each object-grasp pair is executed for " +
                               + numIterationsPerGrasp + " times, resulting in "
                               + objAndGraspPairs.Count * numIterationsPerGrasp +
                               ". However, the number of iterations declared " +
                               "in FixedLengthScenario is " + totalIterations);
                // Stop execution
                UnityEditor.EditorApplication.isPlaying = false;
            }

            // The sampler for object rotation around y axis 
            // The object can have any rotation, i.e. [0, 360]
            samplerYobjectRotation = new UniformSampler(0, 360);

            // For objects having the grasp parallelepiped with the [susd]
            // tag in the name, this sampler is used to decide wheter the object will 
            // be standing or upside down (i.e., if the sampled value is below
            // a threshold, the object will be standing, otherwise upside down)
            samplerUpsideDown = new UniformSampler(0, 1);

            // The sampler to decide whether or not to sample a material.
            // If the sampled value is below this.samplingRangeMaterial then
            // a material will be sampled.
            samplerApplyMaterialObject = new UniformSampler(0, 1);
            // The samplers for the properties of the randomly sampled material
            samplerObjectMetallicProperty = new UniformSampler(0, 0.5f);
            samplerObjectSmoothnessProperty = new UniformSampler(0, 0.2f);
            objectMetallicBaseValue = 0.5f;
            objectSmoothnessBaseValue = 0.4f;

            // Within iteration, the frame number at which we currently are
            framesCounter = 0.0f;

            // To randomly sample the upper or the lower part of the grasp parallelepiped
            samplerTargetPoint = new UniformSampler(0, 1);

            // To decide whether or not to apply the hand position and rotation randomization
            samplerApplyHandRandomization = new UniformSampler(0, 1);

            // The value inserted here into the samplers are HARD-CODED by several trails
            // into the unity editor to manually check what is the reasonable range of values
            // to randomize to have the hand in random positions in the upper part of the camera
            if (hand.name.Contains("Arm02"))
            {
                samplerHandPositionX = new UniformSampler(0, 0.02f);
                samplerHandPositionY = new UniformSampler(0, 0.001f);
                samplerHandPositionZ = new UniformSampler(0, 0.1f);
                samplerHandRotationX = new UniformSampler(0, 1);
                samplerHandRotationY = new UniformSampler(0, 3);
                samplerHandRotationZ = new UniformSampler(0, 5);
            }
            else if (hand.name.Contains("Hannes_ARM"))
            {
                Debug.LogError("Hannes_ARM range of values not implemented yet");
                UnityEditor.EditorApplication.isPlaying = false;
            }
            else
            {
                Debug.LogError("Unknown hand model");
                UnityEditor.EditorApplication.isPlaying = false;
            }

            samplerApplyMaterialHand = new UniformSampler(0, 1);
        }

        /// <summary>
        /// Used to sample a random point from the grasp parallelepiped
        /// </summary>
        private Vector3 GetRandomPointInsideCollider(BoxCollider boxCollider)
        {
            Vector3 extents = boxCollider.size / 2f;
            Vector3 point = new Vector3(
                new UniformSampler(-extents.x, extents.x).Sample(),
                new UniformSampler(-extents.y, extents.y).Sample(),
                new UniformSampler(-extents.z, extents.z).Sample()
            );

            return boxCollider.transform.TransformPoint(point);
        }

        /// <summary>
        /// (1) generate a random position for the camera
        /// (2) generate a random position for the object 
        /// (3) generate a random rotation for the object
        /// (4) generate a random texture for the object
        /// (5) check whether the trajectory for a given grasp is valid,
        ///      if true executes the grasp otherwise 
        ///      re-sample (1),(2),(3) and retry (5)
        /// </summary>
        protected override void OnIterationStart()
        {
            randomWristCameraPlacement();

            randomHandTransform();
            randomHandMaterial();

            iterationsCounter++;
            // It indicates when to get a new object-grasp pair from the list. That is, when 
            // the number of iterations for the current object-grasp pair is reached 
            bool newBatch = false;
            if (iterationsCounter % numIterationsPerGrasp == 0 && iterationsCounter != 0)
            {
                objAndGraspPairs.RemoveAt(0);
                newBatch = true;
            }

            // Get current object and grasp to execute
            int idxObj = objAndGraspPairs[0].Item1;
            int idxGraspInObj = objAndGraspPairs[0].Item2;
            objInScene = m_GameObjectOneWayCache.GetOrInstantiate(idxObj);
            graspToExecute = objInScene.transform.Find("textured").GetChild(idxGraspInObj).gameObject;

            randomObjectPlacement(objInScene, graspToExecute);
            randomObjectRotation(objInScene, graspToExecute);

            randomObjectMaterial(objInScene);

            if (newBatch || iterationsCounter == 0)
            {
                // Activate the grasp for the novel object-grasp pair. This is needed for 
                // the GraspTypeLabeler, since the current iteration will be labeled with 
                // the activate grasp child of the object.
                graspToExecute.SetActive(true);
                Debug.Log("[INFO] Starting sequences for [" 
                          + objInScene.name + "][" + graspToExecute.name + "]");
            }

            // Check whether the camera-to-grasp straight line trajectory is collision-free
            RaycastHit hitInfo;
            Vector3 targetPoint = Vector3.zero;     // junk, it will be correctly initilized below
            int MAX_ATTEMPTS = 1000;
            int i;
            for (i = 0; i < MAX_ATTEMPTS; i++)
            {
                // Here we necessarily need to synchronize the physics with the rendering, 
                // otherwise the Linecast would detect the past object 
                Physics.SyncTransforms();

                targetPoint = graspToExecute.transform.position;
                // A little change to targetPoint: instead of moving towards
                //  the central point of the grasp parallelepiped (as happens with
                //  the line above), we will towards a random point onto
                //  the grasp parallelepiped (as happens in the line below)
                //targetPoint = GetRandomPointInsideCollider(graspToExecute.GetComponent<BoxCollider>());

                if (! Physics.Linecast(wristCamera.transform.position, targetPoint, out hitInfo))
                {
                    Debug.LogError("Something went wrong: no one collision " +
                                   "found. Stopping execution . . .");
                    //Debug.DrawLine(wristCamera.transform.position, target.position, Color.green);
                    // Stop execution
                    UnityEditor.EditorApplication.isPlaying = false;
                }
                else
                {
                    GameObject collidedObj = hitInfo.collider.gameObject;
                    // Take the parent of the parent (instead of the parent), since there
                    // is an intermediate child called textured (refer to the GameObject structure
                    // at line 331 for more information on this)
                    GameObject parentOfCollidedObj = collidedObj.transform.parent.gameObject
                        .transform.parent.gameObject;

                    // Check whether the collision happened with the desired grasp.
                    // If the collision happened with
                    // something different (e.g. another grasp, the object, etc.. the
                    // trajectory is not a valid trajectory
                    if (collidedObj.name == graspToExecute.name & objInScene.name == parentOfCollidedObj.name)
                    {
                        //Debug.Log("VALID iteration for [" + collidedObj.name + "][" + parentOfCollidedObj.name + "]");
                        break;
                    }
                    else
                    { 
                        // Invalid trajectory: sample another camera
                        // position, object placement and rotation
                        randomWristCameraPlacement();
                        randomObjectPlacement(objInScene, graspToExecute);
                        randomObjectRotation(objInScene, graspToExecute);
                    }
                }
            }
            if (i == MAX_ATTEMPTS)
            {
                Debug.LogError("INVALID iteration: no feasible camera-object pose " +
                               "found for [" + objInScene.name + "][" + graspToExecute.name + "]");
                // Stop execution
                UnityEditor.EditorApplication.isPlaying = false;
            }

            // Compute pronation-supination angle
            if (graspToExecute.name.Contains("[nops]"))
            {
                // [nops] tag means no prontation supination to perform.
                psAngle = float.NaN;
            }
            else
            {
                // Make the camera pointing at the center of the parallelepiped,
                // in order to correctly compute the ps angle 
                wristCamera.transform.LookAt(graspToExecute.transform);
                psAngle = Vector3.SignedAngle(wristCamera.transform.right, 
                                              graspToExecute.transform.right, 
                                              Vector3.up);

                if (graspToExecute.name.Contains("adducted_thumb")
                    || (graspToExecute.name.Contains("small_diameter") && objInScene.name.Contains("048_hammer"))
                    || (graspToExecute.name.Contains("medium_wrap") && objInScene.name.Contains("010_potted_meat_can"))
                    || (graspToExecute.name.Contains("large_diameter") && objInScene.name.Contains("006_mustard_bottle"))
                    || (graspToExecute.name.Contains("large_diameter") && objInScene.name.Contains("001_chips_can"))
                    || (graspToExecute.name.Contains("prismatic_2fingers") && objInScene.name.Contains("025_mug"))
                    || (graspToExecute.name.Contains("large_diameter") && objInScene.name.Contains("025_mug")))
                {
                    if (psAngle > 0)
                    {
                        // The angle has to be negative, since we are in a case where
                        // we necessarily have to do a supination (i.e. rotate thumb upwards)
                        // e.g. in the adducted thumb case
                        psAngle *= -1f;
                    }
                }
                else
                {
                    // Here we are in the case where a pronation-supination has to be
                    // performed, but not necessarily rotating the thumb upwards. That means
                    // rotating the wrist (pronation or supination) and the only constraint is that
                    // the rotation can't be higher than 90 degrees (since it is not humanly feasible)
                    if (Math.Abs(psAngle) > 90)
                    {
                        psAngle = psAngle + (Mathf.Sign(psAngle) * -1 * 180);
                    }
                    // Revert the sign since we will need to perform a rotation of -psAngle
                    // around z axis to aling the two vectors generating the psAngle
                    psAngle *= -1;
                }
            }
            psAngle *= samplerPsPercentageRotation.Sample();
            psRotationStartingSampledValue = (float)Math.Round(psRotationStartingSampler.Sample(), 0);      // round to int
            psRotationDurationSampledValue = (float)Math.Round(psRotationDurationSampler.Sample(), 0);

            // Choose the point of the grasp to reach. It will randomly be the upper or the lower
            // extremity (along z axis) of the grasp parallelepiped
            float halfHeight = graspToExecute.GetComponent<MeshFilter>().mesh.bounds.extents.z;
            Vector3 targetUpPoint = graspToExecute.transform.TransformPoint(
                //graspToExecute.transform.localPosition + new Vector3(0f, 0f, -halfHeight));
                graspToExecute.transform.InverseTransformPoint(targetPoint) + new Vector3(0f, 0f, -halfHeight));
            Vector3 targetDownPoint = graspToExecute.transform.TransformPoint(
                //graspToExecute.transform.localPosition + new Vector3(0f, 0f, halfHeight));
                graspToExecute.transform.InverseTransformPoint(targetPoint) + new Vector3(0f, 0f, halfHeight));

            // targetPoint is used to determine the straight line
            //  trajectory from the camera to targetPoint, which is one 
            //  of the two extremity of the grasp parallelepiped
            // targetFePoint is the point towards which the camera will
            //  start performing, at a certain time, the flexion-extension
            //  (which is the other extremity of the grasp parallelepiped)
            
            //Vector3 targetPoint;    
            //Vector3 targetFePoint;  
            
            float sampledPoint = samplerTargetPoint.Sample();
            if (sampledPoint > 0.5)        
            {
                targetPoint = targetUpPoint;
                targetFePoint = targetDownPoint;
            }
            else
            {
                targetPoint = targetDownPoint;
                targetFePoint = targetUpPoint;
            }

            // Make wrist camera look at the target point to reach.
            wristCamera.transform.LookAt(targetPoint);

            // Sample the frame at which the camera will start performing the flexion-extension
            feRotationStartingSampledValue = (float)Math.Round(feRotationStartingSampler.Sample(), 0);

            // Compute the initial distance from the camera to the target object grasp,
            // since at each frame we will take a step forward such that 
            // after framesPerIteration frames the camera will collide with that point
            float dist = Vector3.Distance(wristCamera.transform.position, targetPoint);
            straightLineDirection = (targetPoint - wristCamera.transform.position).normalized;

            // Starts a little bit backwards since there is an offset between the reference
            // system of the camera and the camera itself (i.e. the near clipping plane)
            wristCamera.transform.Translate(new Vector3(0.0f, 0.0f, -REFERENCE_TO_CAMERA_OFFSET));

            // Get all the points for the minimum jerk straight line trajectory
            float x0 = 0;
            float z0 = 0;
            float xf = 0;
            float zf = dist;
            minimumJerkPoints = minimumJerkStraightLineTrajectory(x0, z0, xf, zf);
        }

        protected override void OnUpdate()
        {
            // Get the step value, i.e., how much to move the camera forward
            // Since we use a straight line trajectory and the direction of this 
            // line is the same as the z-axis direction of the camera frame,
            // we have only one component (i.e., z component, which is Item2 in the list)
            // instead of two (the other component will be zero)
            float zStep = minimumJerkPoints[0].Item2;
            minimumJerkPoints.RemoveAt(0);
            Vector3 step = straightLineDirection * zStep;
            // Move the camera towards the grasp area
            wristCamera.transform.position += step;

            framesCounter += 1.0f;

            // Pronation-supination rotation
            if (framesCounter >= psRotationStartingSampledValue 
                && framesCounter < (psRotationStartingSampledValue + psRotationDurationSampledValue)
                && (! float.IsNaN(psAngle)))
            {
                wristCamera.transform.localRotation *= Quaternion.Euler(0f, 0f, psAngle / psRotationDurationSampledValue);        
            }

            // Flexion-extension rotation
            if (framesCounter >= feRotationStartingSampledValue)
            {
                Vector3 targetAngleDir = targetFePoint - wristCamera.transform.position;
                
                // Use the first line if you want to have flexion-extension
                //  rotation randomly up (extension) or
                //  down (flexion) for each sequence.
                //  Otherwise use the second line it you want to rotate only down
                feAngle = Vector3.SignedAngle(targetAngleDir, 
                                              wristCamera.transform.forward, 
                                              Vector3.up);
		        //feAngle = Vector3.Angle(targetAngleDir, wristCamera.transform.forward);
                
                if (framesCounter == feRotationStartingSampledValue)
                {
                    if (feAngle > 0f)
                    {
                        isPositive = true;
                    }
                    else
                    {
                        isPositive = false;
                    }
                }
                else
                {
                    if ((isPositive && feAngle < 0f)
                         || ((!isPositive) && feAngle > 0f))
                    {
                        // In some edge cases it happens that the camera flips up and down,
                        // therefore the angle sometimes has positive values and sometimes 
                        // negative values. To overcome this flipping, we keep track of the 
                        // first sign of the angle and force the camera to rotate along that 
                        // direction in the subsequent frames
                        // A sample case in which we found this problem is (sometimes) in 
                        // the adducted thumb case, so the code here is to overcome this problem
                        feAngle *= -1f;
                    }
                }

                wristCamera.transform.localRotation *= Quaternion.Euler(feAngle * 0.1f, 0f, 0f);        
            }
        }

        protected override void OnIterationEnd()
        {
            if ((iterationsCounter + 1) % numIterationsPerGrasp == 0)
            {
                // If the sequences for the current object-grasp pair 
                // are terminated, deactivate the grasp 
                graspToExecute.SetActive(false);
            }

            // Deletes generated foreground object after each scenario iteration is complete
            m_GameObjectOneWayCache.ResetAllObjects();

            // Re-assign the original material to the object, if changed
            if (originalObjMaterial != null)
            { 
                objInScene.transform.Find("textured").GetComponent<Renderer>().material = originalObjMaterial;
                originalObjMaterial = null;
            }
                
            // Re-assign the original material to the hand, if changed
            if (originalHandMaterial != null)
            {
                if (hand.name.Contains("Arm02"))
                {
                    hand.GetComponent<Renderer>().material = originalHandMaterial;
                    originalHandMaterial = null;
                }
                else if (hand.name.Contains("Hannes_ARM"))
                {
                    Debug.LogError("Hannes_ARM restoring back initial material not implemented yet");
                    UnityEditor.EditorApplication.isPlaying = false;
                }
                else
                {
                    Debug.LogError("Unknown hand model");
                    UnityEditor.EditorApplication.isPlaying = false;
                }
            }

            // Set back the hand to the initial pose
            hand.transform.localPosition = initialHandPosition;
            hand.transform.localRotation = initialHandRotation;

            framesCounter = 0.0f;
        }

    }

}
