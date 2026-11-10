using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Perception.GroundTruth;
using UnityEngine.Perception.GroundTruth.DataModel;
using UnityEngine.Perception.Settings;
using UnityEngine.Perception.Randomization.Scenarios;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers;


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

    // Set when the simulation is being stopped. SimulationStopper.Stop() does not take
    // effect immediately (e.g., in the Editor play mode ends at the end of the frame), 
    // hence this flag is used to not report any (wrong) annotation in the meantime
    private bool isStopped = false;

    private void StopSimulation(string message)
    {
        Debug.LogError(message);
        SimulationStopper.Stop();
        isStopped = true;
    }

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

        // Perception 1.0 serializes custom annotations only through ToMessage,
        // so the grasp values must be written explicitly. Keys match the ones
        // read by python_scripts/Data_collection/convert_dataset.py
        public override void ToMessage(IMessageBuilder builder)
        {
            base.ToMessage(builder);
            builder.AddString("model_type", modelType);
            var values = builder.AddNestedMessage("grasp_type_values");
            values.AddString("object_name", graspTypeValues.objectName);
            values.AddString("instance_name", graspTypeValues.instanceName);
            values.AddString("texture_name", graspTypeValues.textureName);
            values.AddString("grasp_type_name", graspTypeValues.graspTypeName);
            values.AddString("preshape_name", graspTypeValues.preshapeName);
            values.AddString("view_type_name", graspTypeValues.viewTypeName);
            values.AddString("object_side", graspTypeValues.objectSide);
            values.AddBool("no_pronation_supination", graspTypeValues.noPronationSupination);
            values.AddBool("upside_down", graspTypeValues.upsideDown);
            values.AddBool("sampling_upside_down", graspTypeValues.samplingUpsideDown);

            WristCameraMovement.SequenceParameters seq = graspTypeValues.sequenceParameters;
            var seqValues = builder.AddNestedMessage("sequence_parameters");
            seqValues.AddInt("placement_attempts", seq.placementAttempts);
            seqValues.AddFloatArray("camera_start_position", ToArray(seq.cameraStartPosition));
            seqValues.AddFloatArray("target_point", ToArray(seq.targetPoint));
            seqValues.AddBool("reach_up_point", seq.reachUpPoint);
            seqValues.AddFloatArray("object_position", ToArray(seq.objectPosition));
            seqValues.AddFloat("object_rotation_y", seq.objectRotationY);
            seqValues.AddFloat("object_rotation_z", seq.objectRotationZ);
            seqValues.AddFloat("ps_angle", seq.psAngle);
            seqValues.AddInt("ps_start_frame", seq.psStartFrame);
            seqValues.AddInt("ps_duration_frames", seq.psDurationFrames);
            seqValues.AddInt("fe_start_frame", seq.feStartFrame);
        }

        static float[] ToArray(Vector3 v)
        {
            return new float[] { v.x, v.y, v.z };
        }
    }

    protected override void Setup()
    {
        graspTypeAnnotationDefinition = new GraspTypeAnnotationDefinition(annotationId);
        DatasetCapture.RegisterAnnotationDefinition(graspTypeAnnotationDefinition);

        if (viewType != "Wrist")
        {
            StopSimulation("Wrong value in View Type field: " + viewType + ". " +
                           "Currently, the only admitted value is [Wrist]");
        }
    }

    /// <summary>
    /// The scenario from which to take the object currently in the scene.
    /// </summary>
    public GameObject simulationScenario;

    /// <summary>
    /// The view type. It could be one of [Head, Wrist]. But, we temporarily 
    /// removed the Head camera, therefore only Wrist option is allowed
    /// </summary>
    public string viewType = "Wrist";      

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

        public WristCameraMovement.SequenceParameters sequenceParameters;

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
            bool samplingUpsideDown,
            WristCameraMovement.SequenceParameters sequenceParameters
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
            this.sequenceParameters = sequenceParameters;
        }
    }

    protected override void OnBeginRendering(ScriptableRenderContext scriptableRenderContext)
    {
        if (isStopped || ! sensorHandle.ShouldCaptureThisFrame)
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
        // Perception 1.0 starts capturing one frame before the scenario starts
        // its first iteration, so no object is placed yet: nothing to label
        if (count == 0 && simulationScenario.GetComponent<ScenarioBase>().currentIteration == 0)
            return;
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
            StopSimulation("SOMETHING WENT WRONG, THERE MUST BE EXACTLY ONE GRASP BUT " + countActiveGrasp + " FOUND. STOPPING EXECUTION . . .");
            return;
        }

        string instanceName = objInScene.name.Replace("(Clone)", "");
        string objectName = GraspCatalog.GetCategory(objInScene);
        if (objectName == null)
        {
            StopSimulation("Unknown category for object [" + instanceName + "]: add it to " +
                           "GraspCatalog.instance2object or add a GraspObjectInfo component to its prefab");
            return;
        }

        // Use sharedMaterial, since the material getter would instantiate a new copy 
        // of the material (which is never destroyed)
        string textureName = objInScene.transform.Find("textured").GetComponent<Renderer>().sharedMaterial.name;
	    textureName = textureName.Replace(" (Instance)", "");        
        if (textureName == "material_0" || textureName == "material0")
        {
            textureName = "Original";
        }
        
        GraspCatalog.GraspName graspName = GraspCatalog.ParseGraspName(
            objInScene.transform.Find("textured").GetChild(childIdx).gameObject.name);
        string graspTypeName = graspName.graspType;
        string objectSide = graspName.objectSide;
        bool noPronationSupination = graspName.noPronationSupination;
        bool upsideDown = graspName.upsideDown;
        bool samplingUpsideDown = graspName.samplingUpsideDown;

        string preshapeName;
        if (!GraspCatalog.graspType2preshape.TryGetValue(graspTypeName, out preshapeName))
        {
            StopSimulation("Unknown grasp type [" + graspTypeName + "] for object [" + instanceName + "]: " +
                           "add it to GraspCatalog.graspType2preshape");
            return;
        }
        
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
            samplingUpsideDown,
            simulationScenario.GetComponent<ScenarioBase>()
                .GetRandomizer<WristCameraMovement>().currentSequenceParameters
        );
        var annotation = new GraspTypeAnnotation(graspTypeAnnotationDefinition, sensorHandle.Id, curSequenceValues);
        sensorHandle.ReportAnnotation(graspTypeAnnotationDefinition, annotation);

    }
}

