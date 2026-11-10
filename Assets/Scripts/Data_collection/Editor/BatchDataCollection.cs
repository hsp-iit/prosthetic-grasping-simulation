using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers;
using UnityEngine.Perception.Randomization.Scenarios;

/// <summary>
/// Builds a Windows player of Data_collection with the scenario configured from the command line,
/// without leaving changes in the scene (the scene file is restored byte for byte after the build).
///
///   Unity.exe -batchmode -quit -projectPath PROJECT -executeMethod BatchDataCollection.Build
///       -preshapes lateral_no3,pinch_no3 -perGrasp 75 -seed 12345 -buildPath D:/builds/A/DataCollection.exe
///
/// -preshapes: comma-separated preshape filter (empty or omitted = all grasps).
/// -prefabs: optional comma-separated prefab names (looked up in Assets/Prefabs/YCB and Assets/Prefabs/GSO)
///           replacing the scenario's prefabs list; omitted = keep the list saved in the scene.
/// Iteration Count is set to (number of selected object-grasp pairs) * perGrasp.
///
/// Build only some GSO prefabs (the others, e.g. with hand-placed grasps, are not touched):
///   Unity.exe -batchmode -quit -projectPath PROJECT -executeMethod BatchDataCollection.BuildGsoPrefabs
///       -objects Cole_Hardware_Hammer_Black,Threshold_Porcelain_Pitcher_White
/// </summary>
public static class BatchDataCollection
{
    const string ScenePath = "Assets/Scenes/Data_collection.unity";

    static string Arg(string name, string fallback = null)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static List<string> ListArg(string name) =>
        (Arg(name, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

    public static void BuildGsoPrefabs()
    {
        List<string> objects = ListArg("-objects");
        if (objects.Count == 0)
            throw new ArgumentException("-objects is required");
        AssetDatabase.Refresh();
        foreach (string name in objects)
            Debug.Log("[BatchDataCollection] built " + GsoPrefabBuilder.Build($"Assets/3Dmodels/GSO/{name}/grasp_spec.json"));
    }

    static GameObject LoadPrefab(string name)
    {
        foreach (string dir in new[] { "Assets/Prefabs/YCB", "Assets/Prefabs/GSO" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}.prefab");
            if (prefab != null)
                return prefab;
        }
        throw new ArgumentException("Prefab not found: " + name);
    }

    public static void Build()
    {
        List<string> preshapes = ListArg("-preshapes");
        List<string> prefabNames = ListArg("-prefabs");
        int perGrasp = int.Parse(Arg("-perGrasp", "50"));
        uint seed = uint.Parse(Arg("-seed", "539662088"));
        string buildPath = Arg("-buildPath") ?? throw new ArgumentException("-buildPath is required");

        byte[] backup = File.ReadAllBytes(ScenePath);
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var scenario = UnityEngine.Object.FindAnyObjectByType<FixedLengthScenario>();
            var wrist = scenario.randomizers.OfType<WristCameraMovement>().First();
            if (prefabNames.Count > 0)
                wrist.prefabs.SetOptions(prefabNames.Select(LoadPrefab));
            wrist.preshapeFilter = preshapes;
            wrist.graspTypeFilter = new List<string>();
            wrist.numIterationsPerGrasp = perGrasp;

            int pairs = 0;
            foreach (GameObject obj in wrist.prefabs.categories.Select(c => c.Item1))
            {
                Transform textured = obj.transform.Find("textured");
                for (int i = 0; i < textured.childCount; i++)
                    if (wrist.IsGraspSelected(textured.GetChild(i).name))
                    {
                        pairs++;
                        Debug.Log($"[BatchDataCollection] pair {obj.name} / {textured.GetChild(i).name}");
                    }
            }
            scenario.constants.randomSeed = seed;
            scenario.constants.iterationCount = scenario.constants.startIteration + pairs * perGrasp;
            EditorUtility.SetDirty(scenario);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BatchDataCollection] prefabs={wrist.prefabs.categories.Count} " +
                      $"preshapes=[{string.Join(",", preshapes)}] pairs={pairs} " +
                      $"perGrasp={perGrasp} iterationCount={scenario.constants.iterationCount} seed={seed}");

            Directory.CreateDirectory(Path.GetDirectoryName(buildPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = buildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log($"[BatchDataCollection] build result: {report.summary.result}, {report.summary.totalTime}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
        finally
        {
            File.WriteAllBytes(ScenePath, backup);
            AssetDatabase.Refresh();
        }
    }
}
