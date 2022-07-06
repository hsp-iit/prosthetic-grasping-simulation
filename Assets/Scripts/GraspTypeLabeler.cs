using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine.Rendering;

namespace UnityEngine.Perception.GroundTruth
{
    /// <summary>
    /// Produces grasp type annotation for the sequence.
    /// </summary>
    [Serializable]
    public sealed class GraspTypeLabeler : CameraLabeler
    {
        private AnnotationDefinition graspTypeAnnotationDefinition;

        private Dictionary<string, string> graspType2preshape = new Dictionary<string, string>()
        {
            {"adducted_thumb[left]", "lateral_no3[left]" },
            {"adducted_thumb[right]", "lateral_no3[right]" },
            {"adducted_thumb", "lateral_no3" },
            {"large_diameter[left]", "power_no3[left]" },
            {"large_diameter[right]", "power_no3[right]" },
            {"large_diameter", "power_no3" },
            {"small_diameter", "power_no3" },
            {"medium_wrap[left]", "power_no3[left]" },
            {"medium_wrap[right]", "power_no3[right]" },
            {"medium_wrap", "power_no3" },
            {"sphere_4fingers", "power_no3" },
            {"power_sphere", "power_no3" },
            {"prismatic_4fingers", "pinch_no3" },
            {"precision_sphere", "pinch_no3" },
            {"tripod", "pinch_no3" },
            {"prismatic_2fingers", "pinch_3" },
            {"tip_pinch", "pinch_3"},
        };

        private Dictionary<string, string> instance2object = new Dictionary<string, string>()
        {
            {"pitcher", "dispenser" },
            {"mustard", "dispenser" },
            {"red_plate", "plate" },
            {"abrasive_sponge", "sponge" },
            {"pringles", "tube" },
            {"meat_can", "can" },
            {"red_mug", "mug" },
            {"hammer", "big_tool" },
            {"plum", "small_ball" },
            {"baseball_ball", "big_ball" },
            {"ball", "big_ball"},
            {"spoon", "small_tool" },
            {"marker", "small_tool" },
            {"red_cube", "small_cube" },
            {"book", "book"},
            {"book_opened", "book"},
            {"bottle", "dispenser"},
            {"bottle_prescription", "dispenser"},
            {"cap", "small_tool"},
            {"cap_2", "small_tool"},
            {"cap_3", "small_tool"},
            {"eraser", "small_tool"},
            {"eraser_2", "small_tool"},
            {"flashdrive", "small_tool"},
            {"highlighter", "small_tool"},
            {"notebook", "book"},
            {"oriental_gadget", "small_tool"},
            {"paintbrush", "big_tool"},
            {"paintbrush_thin", "small_tool"},
            {"paper_airplane", "small_tool"},
            {"paper_punch", "small_tool"},
            {"pen", "small_tool"},
            {"pen_thin", "small_tool"},
            {"pencil", "smalll_tool"},
            {"pendrive", "small_tool"},
            {"scotch", "small_tool"},
            {"sharpner", "small_tool"},
            {"tape_duct", "small_tool"},
            {"toothbrush", "small_tool"},
            {"wallet_closed", "wallet"},
            {"wallet_opened", "wallet"},
        };

        /// <summary>
        /// The scenario from which to take the object currently in the scene.
        /// </summary>
        public GameObject simulationScenario;

        public string viewType;

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "NotAccessedField.Local")]
        private struct GraspTypeValue
        {
            public string objectName;
            public string instanceName;
            public string texture;
            public string graspType;
            public string preshape;
            public string viewType;
        }

        protected override bool supportsVisualization => false;

        ///<inheritdoc/>
        public override string description
        {
            get => "Produces grasp type annotation for the sequence";
            protected set { }
        }

        public GraspTypeLabeler()
        { 
        }

        ///<inheritdoc/>
        protected override void Setup()
        {
            graspTypeAnnotationDefinition = DatasetCapture.RegisterAnnotationDefinition(
                "Sequence grasp type",
                "The grasp type expected to perform at the end of a given sequence",
                id: Guid.Parse("C0B4A22C-0420-4D9F-BAFC-954B8F7B35A7"));
        }

        protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
        {
            var annotationValues = new List<string>();

            // Retrieve the object in the scene
            string OBJECTS_PARENT_NAME = "Foreground Object";
            Transform childTransf = simulationScenario.transform.Find(OBJECTS_PARENT_NAME);
            GameObject allObjsInCache = null;
            if (childTransf != null)
            {
                allObjsInCache = childTransf.gameObject;
            }
            else
            {
                throw new Exception("Child " + OBJECTS_PARENT_NAME + " not found in Simulation Scenario object");
            }
            int count = 0;
            GameObject objInScene = null;
            foreach (Transform tran in allObjsInCache.transform)
            {
                // Is there a cleaner way to check for which object is in view? (without affecting performance)
                if (tran.position.x != 10000 & tran.position.y != 0 & tran.position.z != 0)
                {
                    count++;
                    objInScene = tran.gameObject;
                }
            }
            if (count != 1)
            {
                throw new Exception("There must be one object in scene, but " + count + " were found");
            }

            // Once we have the object, there will be only activate grasp child, this will be our grasp 
            int countActiveGrasp = 0;
            int childIdx = -1;
            int i;
            for (i = 0; i < objInScene.transform.Find("textured").childCount; i++) 
            {
                if (objInScene.transform.Find("textured").GetChild(i).gameObject.activeSelf)
                {
                    countActiveGrasp += 1;
                    childIdx = i;
                }
            }
            if (countActiveGrasp != 1)
            {
                Debug.LogError("SOMETHING WENT WRONG, THERE MUST BE EXACTLY ONE GRASP BUT " + countActiveGrasp + " FOUND. STOPPING EXECUTION . . .");
                UnityEditor.EditorApplication.isPlaying = false;
            }

            string instanceName = objInScene.name.Replace("(Clone)", "");
            string objectName = instance2object[instanceName];
            string textureName = objInScene.transform.Find("textured").GetComponent<Renderer>().material.name;
            string graspTypeName = objInScene.transform.Find("textured").GetChild(childIdx).gameObject.name;
	    graspTypeName = graspTypeName.Replace("[nops]", "").Replace("[usd]", "").Replace("[susd]", "");
            string preshapeName = graspType2preshape[graspTypeName];

            if (textureName == "material_0 (Instance)" || textureName == "material0 (Instance)")
            {
                textureName = "Original";
            }
	    textureName = textureName.Replace(" (Instance)", "");

            //Report using the PerceptionCamera's SensorHandle if scheduled 
            if (sensorHandle.ShouldCaptureThisFrame)
            {
                sensorHandle.ReportAnnotationValues(
                    graspTypeAnnotationDefinition,
                    new[] { new GraspTypeValue { 
                                                objectName = objectName,
                                                instanceName = instanceName,
                                                texture = textureName,
                                                graspType = graspTypeName,
                                                preshape = preshapeName,
                                                viewType = viewType
                                               } 
                          }
                    );
            }

        }
    }
}
