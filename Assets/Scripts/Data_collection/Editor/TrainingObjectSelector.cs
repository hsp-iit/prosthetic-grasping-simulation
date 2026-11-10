using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Perception.Randomization.Randomizers.SampleRandomizers;
using UnityEngine.Perception.Randomization.Scenarios;

/// <summary>
/// Selects the objects (YCB, GSO, ...) and the grasps to generate data for, according
/// to the training needs (e.g. only the mugs, or only the pinch_3 grasps), and applies
/// the selection to the Simulation Scenario of the open scene: the prefabs list and the
/// preshape and grasp type filters of WristCameraMovement, and the Iteration Count
/// matching the resulting number of object-grasp pairs.
/// </summary>
public class TrainingObjectSelector : EditorWindow
{
    static readonly string[] PREFAB_DIRS = { "Assets/Prefabs/YCB", "Assets/Prefabs/GSO" };

    class Entry
    {
        public GameObject prefab;
        public string source;
        public string category;
        public string[] grasps;
        public bool included = true;
    }

    List<Entry> entries = new List<Entry>();
    // Filter values, true when selected
    Dictionary<string, bool> sources = new Dictionary<string, bool>();
    Dictionary<string, bool> categories = new Dictionary<string, bool>();
    Dictionary<string, bool> preshapes = new Dictionary<string, bool>();
    Dictionary<string, bool> graspTypes = new Dictionary<string, bool>();
    int numIterationsPerGrasp = 50;
    Vector2 scroll;

    [MenuItem("Tools/Grasp Objects/Training Object Selector")]
    static void Open()
    {
        GetWindow<TrainingObjectSelector>("Training Objects").Reload();
    }

    void OnEnable()
    {
        Reload();
    }

    void Reload()
    {
        entries.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PREFAB_DIRS.Where(AssetDatabase.IsValidFolder).ToArray()))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Transform textured = prefab.transform.Find("textured");
            if (textured == null)
                continue;
            GraspObjectInfo info = prefab.GetComponent<GraspObjectInfo>();
            entries.Add(new Entry
            {
                prefab = prefab,
                source = info != null && !string.IsNullOrEmpty(info.source)
                    ? info.source : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)),
                category = GraspCatalog.GetCategory(prefab) ?? "unknown",
                grasps = Enumerable.Range(0, textured.childCount).Select(i => textured.GetChild(i).name).ToArray()
            });
        }
        entries = entries.OrderBy(e => e.source).ThenBy(e => e.category).ThenBy(e => e.prefab.name).ToList();

        sources = ToFilter(entries.Select(e => e.source), sources);
        categories = ToFilter(entries.Select(e => e.category), categories);
        preshapes = ToFilter(entries.SelectMany(e => e.grasps).Select(g => GraspCatalog.GetPreshape(g) ?? "unknown"), preshapes);
        graspTypes = ToFilter(entries.SelectMany(e => e.grasps).Select(g => GraspCatalog.ParseGraspName(g).graspType), graspTypes);

        WristCameraMovement wrist = FindWristCameraMovement();
        if (wrist != null)
            numIterationsPerGrasp = wrist.numIterationsPerGrasp;
    }

    // Keep the previous values, the new keys are selected
    static Dictionary<string, bool> ToFilter(IEnumerable<string> keys, Dictionary<string, bool> previous)
    {
        return keys.Distinct().OrderBy(k => k).ToDictionary(k => k, k => !previous.ContainsKey(k) || previous[k]);
    }

    static FixedLengthScenario FindScenario()
    {
        return FindAnyObjectByType<FixedLengthScenario>();
    }

    static WristCameraMovement FindWristCameraMovement()
    {
        FixedLengthScenario scenario = FindScenario();
        return scenario == null ? null : scenario.randomizers.OfType<WristCameraMovement>().FirstOrDefault();
    }

    bool IsGraspSelected(string grasp)
    {
        return preshapes[GraspCatalog.GetPreshape(grasp) ?? "unknown"]
               && graspTypes[GraspCatalog.ParseGraspName(grasp).graspType];
    }

    IEnumerable<string> SelectedGrasps(Entry entry)
    {
        return entry.grasps.Where(IsGraspSelected);
    }

    bool PassesFilters(Entry entry)
    {
        return sources[entry.source] && categories[entry.category] && SelectedGrasps(entry).Any();
    }

    List<Entry> SelectedEntries()
    {
        return entries.Where(e => e.included && PassesFilters(e)).ToList();
    }

    void OnGUI()
    {
        if (GUILayout.Button("Reload prefabs from " + string.Join(", ", PREFAB_DIRS)))
            Reload();

        EditorGUILayout.BeginHorizontal();
        DrawFilter("Source", sources);
        DrawFilter("Category", categories);
        DrawFilter("Preshape", preshapes);
        DrawFilter("Grasp type", graspTypes);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        List<Entry> selected = SelectedEntries();
        List<string> selectedGrasps = selected.SelectMany(SelectedGrasps).ToList();
        EditorGUILayout.LabelField("Objects", EditorStyles.boldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        foreach (Entry entry in entries)
        {
            bool passes = PassesFilters(entry);
            using (new EditorGUI.DisabledScope(!passes))
            {
                EditorGUILayout.BeginHorizontal();
                entry.included = EditorGUILayout.ToggleLeft(entry.prefab.name, entry.included, GUILayout.Width(380));
                EditorGUILayout.LabelField(entry.source + " / " + entry.category, GUILayout.Width(140));
                EditorGUILayout.LabelField(string.Join(", ", entry.grasps.Select(g => IsGraspSelected(g) ? g : "(" + g + ")")));
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(selected.Count + " objects, " + selectedGrasps.Count + " object-grasp pairs");
        EditorGUILayout.LabelField("Pairs per preshape: " + string.Join(", ", selectedGrasps
            .GroupBy(g => GraspCatalog.GetPreshape(g) ?? "unknown").OrderBy(g => g.Key)
            .Select(g => g.Key + ": " + g.Count())));
        numIterationsPerGrasp = Mathf.Max(1, EditorGUILayout.IntField("Num Iterations Per Grasp", numIterationsPerGrasp));
        EditorGUILayout.LabelField("Iteration Count: " + selectedGrasps.Count * numIterationsPerGrasp);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Load selection from scenario"))
            LoadFromScenario();
        using (new EditorGUI.DisabledScope(selectedGrasps.Count == 0 || EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Apply to Simulation Scenario"))
                ApplyToScenario(selected, selectedGrasps.Count);
        }
        EditorGUILayout.EndHorizontal();
    }

    static void DrawFilter(string label, Dictionary<string, bool> filter)
    {
        EditorGUILayout.BeginVertical(GUILayout.MinWidth(150));
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("All", EditorStyles.miniButtonLeft))
            foreach (string key in filter.Keys.ToList()) filter[key] = true;
        if (GUILayout.Button("None", EditorStyles.miniButtonRight))
            foreach (string key in filter.Keys.ToList()) filter[key] = false;
        EditorGUILayout.EndHorizontal();
        foreach (string key in filter.Keys.ToList())
            filter[key] = EditorGUILayout.ToggleLeft(key, filter[key]);
        EditorGUILayout.EndVertical();
    }

    void LoadFromScenario()
    {
        WristCameraMovement wrist = FindWristCameraMovement();
        if (wrist == null)
        {
            ShowNotification(new GUIContent("No WristCameraMovement in the open scene"));
            return;
        }
        HashSet<GameObject> inScenario = new HashSet<GameObject>(wrist.prefabs.categories.Select(c => c.Item1));
        foreach (Entry entry in entries)
            entry.included = inScenario.Contains(entry.prefab);
        foreach (string key in preshapes.Keys.ToList())
            preshapes[key] = wrist.preshapeFilter.Count == 0 || wrist.preshapeFilter.Contains(key);
        foreach (string key in graspTypes.Keys.ToList())
            graspTypes[key] = wrist.graspTypeFilter.Count == 0 || wrist.graspTypeFilter.Contains(key);
        foreach (string key in sources.Keys.ToList())
            sources[key] = true;
        foreach (string key in categories.Keys.ToList())
            categories[key] = true;
        numIterationsPerGrasp = wrist.numIterationsPerGrasp;
    }

    void ApplyToScenario(List<Entry> selected, int numPairs)
    {
        FixedLengthScenario scenario = FindScenario();
        WristCameraMovement wrist = FindWristCameraMovement();
        if (wrist == null)
        {
            ShowNotification(new GUIContent("No WristCameraMovement in the open scene"));
            return;
        }

        Undo.RecordObject(scenario, "Apply training object selection");
        wrist.prefabs.SetOptions(selected.Select(e => e.prefab));
        // An empty filter means no filtering
        wrist.preshapeFilter = preshapes.All(p => p.Value) ? new List<string>()
            : preshapes.Where(p => p.Value).Select(p => p.Key).ToList();
        wrist.graspTypeFilter = graspTypes.All(g => g.Value) ? new List<string>()
            : graspTypes.Where(g => g.Value).Select(g => g.Key).ToList();
        wrist.numIterationsPerGrasp = numIterationsPerGrasp;
        // Same check as in WristCameraMovement.OnAwake
        scenario.constants.iterationCount = scenario.constants.startIteration + numPairs * numIterationsPerGrasp;
        EditorUtility.SetDirty(scenario);
        EditorSceneManager.MarkSceneDirty(scenario.gameObject.scene);

        Debug.Log("[Training objects] Simulation Scenario set to " + selected.Count + " objects, " + numPairs +
                  " object-grasp pairs, " + numIterationsPerGrasp + " iterations per grasp, Iteration Count " +
                  scenario.constants.iterationCount + ". Save the scene to keep it");
    }
}
