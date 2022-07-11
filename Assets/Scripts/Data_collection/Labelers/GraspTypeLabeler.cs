using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.DataModel;


/// <summary>
/// Produces grasp type annotation for the sequence.
/// </summary>
[Serializable]
public sealed class GraspTypeLabeler : CameraLabeler
{
    private AnnotationDefinition graspTypeAnnotationDefinition;
    public string annotationId = "94179c03-6258-4cfe-8449-f337fcd24311";    
    public override string description => "Produces grasp type annotation for the sequence";
    public override string labelerId => "GraspType";
    protected override bool supportsVisualization => false;

    class GraspTypeAnnotationDefinition : AnnotationDefinition
    {
        public GraspTypeAnnotationDefinition(string id) : base(id) { }

        public override string modelType => "GraspTypeAnnotationDefinition";
        public override string description => "Produces grasp type annotation for the sequence";
    }

    [Serializable]
    class GraspTypeAnnotation : Annotation
    {
        public GraspTypeMetaValues graspTypeValues;
        public override bool IsValid() => true;

        public GraspTypeAnnotation(AnnotationDefinition definition, string sensorId, GraspTypeMetaValues graspTypeValues)
            : base(definition, sensorId)
        {
            this.graspTypeValues = graspTypeValues;
        }
    }

    protected override void Setup()
    {
        graspTypeAnnotationDefinition = new GraspTypeAnnotationDefinition(annotationId);
        DatasetCapture.RegisterAnnotationDefinition(graspTypeAnnotationDefinition);
    }

    /// <summary>
    /// The scenario from which to take the object currently in the scene.
    /// </summary>
    public GameObject simulationScenario;

    /// <summary>
    /// The view type. It can be one of [Head_d435, Wrist_d435]. Default is Wrist_d435
    /// </summary>
    public string viewType = "Wrist_d435";      // TODO: remove _d435. Consequently, remove it also from post-processing script for label conversion     

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    private struct GraspTypeMetaValues
    {
        public string objectName;
        public string instanceName;
        public string textureName;
        public string graspTypeName;
        public string preshapeName;
        public string viewTypeName;

        public string objectSide;
        
        public bool noPronationSupination;
        public bool upsideDown;
        public bool samplingUpsideDown;

        public GraspTypeMetaValues(
            string objectName, 
            string instanceName, 
            string textureName, 
            string graspTypeName, 
            string preshapeName, 
            string viewTypeName, 
            string objectSide, 
            bool noPronationSupination, 
            bool upsideDown, 
            bool samplingUpsideDown
        )
        {
            this.objectName = objectName;
            this.instanceName = instanceName;
            this.textureName = textureName;
            this.graspTypeName = graspTypeName;
            this.preshapeName = preshapeName;
            this.viewTypeName = viewTypeName;
            this.objectSide = objectSide;
            this.noPronationSupination = noPronationSupination;
            this.upsideDown = upsideDown;
            this.samplingUpsideDown = samplingUpsideDown;
        }
    }

    private Dictionary<string, string> graspType2preshape = new Dictionary<string, string>()
    {
        {"adducted_thumb", "lateral_no3" },
        {"large_diameter", "power_no3" },
        {"small_diameter", "power_no3" },
        {"medium_wrap", "power_no3" },
        {"sphere_4fingers", "power_no3" },
        {"power_sphere", "power_no3" },
        {"prismatic_4fingers", "pinch_no3" },
        {"tripod", "pinch_3" },
        {"prismatic_2fingers", "pinch_3" },

        // TODO: are these below used?
        {"precision_sphere", "pinch_no3" },
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

    protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
    {
        if (! sensorHandle.ShouldCaptureThisFrame)
            return;

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
	    textureName = textureName.Replace(" (Instance)", "");        
        if (textureName == "material_0" || textureName == "material0")
        {
            textureName = "Original";
        }
        
        string graspTypeName = objInScene.transform.Find("textured").GetChild(childIdx).gameObject.name;
        bool noPronationSupination = graspTypeName.Contains("[nops]");
        bool upsideDown = graspTypeName.Contains("[usd]");
        bool samplingUpsideDown = graspTypeName.Contains("[susd]");
        graspTypeName = graspTypeName.Replace("[nops]", "").Replace("[usd]", "").Replace("[susd]", "");

        string objectSide = "none";
        if (graspTypeName.Contains("[left]"))
        {
            objectSide = "left";
            graspTypeName = graspTypeName.Replace("[left]", "");
        }
        else if (graspTypeName.Contains("[right]"))
        {
            objectSide = "right";
            graspTypeName = graspTypeName.Replace("[right]", "");
        }

        string preshapeName = graspType2preshape[graspTypeName];

        //Report using the PerceptionCamera's SensorHandle if scheduled 
        GraspTypeMetaValues curSequenceValues = new GraspTypeMetaValues(
            objectName, 
            instanceName, 
            textureName, 
            graspTypeName, 
            preshapeName, 
            viewType, 
            objectSide,
            noPronationSupination, 
            upsideDown, 
            samplingUpsideDown
        );
        var annotation = new GraspTypeAnnotation(graspTypeAnnotationDefinition, sensorHandle.Id, curSequenceValues);
        sensorHandle.ReportAnnotation(graspTypeAnnotationDefinition, annotation);
    }
}

