using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

/// <summary>
/// Browse the Google Scanned Objects (GSO) catalog, look at the objects (thumbnails from
/// Gazebo Fuel, and a 3D preview with the grasp parallelepipeds once added) and decide
/// which ones to add to the prefabs. An object is added with its category and either
/// with grasp parallelepipeds placed automatically (the grasps listed here) or without
/// them, to place them by hand in the prefab.
/// The catalog, the manifest (python_scripts/GSO/gso_objects.json) and the download are
/// handled by python_scripts/GSO/import_gso.py, the prefab by GsoPrefabBuilder.
/// </summary>
public class GsoBrowser : EditorWindow
{
    const string FUEL_MODELS_URL = "https://fuel.gazebosim.org/1.0/GoogleResearch/models/";
    const int NUM_THUMBNAILS = 5;
    const float ROW_HEIGHT = 20f;
    const float LIST_WIDTH = 360f;
    const string PYTHON_PREF = "GsoBrowser.python";

    static readonly string[] APPROACHES = { "top", "bottom", "left", "right", "front", "back" };
    static readonly string[] SIDES = { "none", "left", "right" };
    static readonly string[] FARTHEST_TO = { "none", "left", "right", "front", "back" };
    static readonly string[] ADDED_FILTER = { "All objects", "Added only", "Not added only" };
    static readonly Color[] GRASP_COLORS = { Color.green, Color.cyan, Color.magenta, Color.yellow, Color.red, Color.blue };

    static string RepoDir => Path.GetDirectoryName(Application.dataPath);
    static string ToolPath => Path.Combine(RepoDir, "python_scripts", "GSO", "import_gso.py");
    static string ManifestPath => Path.Combine(RepoDir, "python_scripts", "GSO", "gso_objects.json");
    // Written by import_gso.py
    static string CatalogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                              ".cache", "gso_fuel_catalog.json");

    [Serializable]
    class FuelModel
    {
        public string name;
        public string description;
        public string[] categories;
        public long filesize;
        public string license_name;
    }

    [Serializable]
    class FuelCatalog
    {
        public FuelModel[] models;
    }

    [Serializable]
    class ManifestGrasp
    {
        public string name;
        public string approach = "top";
    }

    [Serializable]
    class ManifestObject
    {
        public string name;
        public string category;
        public float yaw;
        public string farthest_to;
        public bool auto_grasps = true;
        public ManifestGrasp[] grasps;
    }

    [Serializable]
    class Manifest
    {
        public ManifestObject[] objects;
    }

    // A grasp of the object being added, edited with popups and toggles
    class GraspRow
    {
        public int type;
        public int side;
        public bool nops, usd, susd, border;
        public int approach;
    }

    FuelModel[] catalog = new FuelModel[0];
    Dictionary<string, ManifestObject> manifest = new Dictionary<string, ManifestObject>();
    List<int> filtered = new List<int>();
    int addedCount;
    string[] fuelCategories = { "All categories" };
    string[] graspTypes;

    // Filters
    string search = "";
    int fuelCategory;
    int addedFilter;

    Vector2 listScroll;
    Vector2 detailsScroll;
    int selected = -1;
    string selectedName;
    int thumbnailIndex;

    // The add form of the selected object
    string category = "";
    bool autoGrasps = true;
    float yaw;
    int farthestTo;
    List<GraspRow> graspRows = new List<GraspRow>();
    int copyFrom;

    Dictionary<string, Texture2D> thumbnails = new Dictionary<string, Texture2D>();
    Dictionary<string, UnityWebRequest> thumbnailRequests = new Dictionary<string, UnityWebRequest>();

    // The built-in preview of the prefab (GameObjectInspector), which renders it
    // correctly in HDRP and can be rotated by dragging
    Editor previewEditor;
    string previewPrefabPath;
    bool showGraspBoxes = true;

    string log = "";

    [MenuItem("Tools/Grasp Objects/GSO Browser")]
    static void Open()
    {
        GetWindow<GsoBrowser>("GSO Browser");
    }

    void OnEnable()
    {
        graspTypes = GraspCatalog.graspType2preshape.Keys.OrderBy(k => k).ToArray();
        LoadManifest();
        LoadCatalog(false);
        EditorApplication.update += PollThumbnails;
    }

    void OnDisable()
    {
        EditorApplication.update -= PollThumbnails;
        foreach (UnityWebRequest request in thumbnailRequests.Values)
            request.Dispose();
        thumbnailRequests.Clear();
        foreach (Texture2D texture in thumbnails.Values)
            if (texture != null)
                DestroyImmediate(texture);
        thumbnails.Clear();
        ClearPreview();
    }

    // ------------------------------------------------------------------ data

    void LoadCatalog(bool refresh)
    {
        if (refresh || !File.Exists(CatalogPath))
        {
            log = "";
            // import_gso.py search fetches the catalog from Gazebo Fuel and caches it
            EditorUtility.DisplayProgressBar("GSO Browser", "Fetching the GSO catalog from Gazebo Fuel", 0.5f);
            try
            {
                RunTool(refresh ? new[] { "search", "^$", "--refresh" } : new[] { "search", "^$" });
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
        if (!File.Exists(CatalogPath))
            return;
        catalog = JsonUtility.FromJson<FuelCatalog>("{\"models\":" + File.ReadAllText(CatalogPath) + "}").models
                  .OrderBy(m => m.name).ToArray();
        fuelCategories = new[] { "All categories" }
            .Concat(catalog.SelectMany(m => m.categories ?? new string[0]).Distinct().OrderBy(c => c))
            .Concat(new[] { "(no category)" }).ToArray();
        ApplyFilters();
    }

    void LoadManifest()
    {
        manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath)).objects
                   .ToDictionary(o => o.name);
    }

    static bool IsAdded(string name)
    {
        return File.Exists(GsoPrefabBuilder.PrefabPath(name));
    }

    void ApplyFilters()
    {
        string[] words = search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string categoryFilter = fuelCategories[Mathf.Clamp(fuelCategory, 0, fuelCategories.Length - 1)];
        filtered.Clear();
        addedCount = catalog.Count(m => IsAdded(m.name));
        for (int i = 0; i < catalog.Length; i++)
        {
            FuelModel model = catalog[i];
            string text = (model.name + " " + model.description).ToLowerInvariant();
            if (!words.All(text.Contains))
                continue;
            string[] categories = model.categories ?? new string[0];
            if (fuelCategory == fuelCategories.Length - 1 ? categories.Length > 0
                : fuelCategory > 0 && !categories.Contains(categoryFilter))
                continue;
            bool added = IsAdded(model.name);
            if ((addedFilter == 1 && !added) || (addedFilter == 2 && added))
                continue;
            filtered.Add(i);
        }
    }

    void Select(int catalogIndex)
    {
        selected = catalogIndex;
        selectedName = catalog[catalogIndex].name;
        thumbnailIndex = 0;
        log = "";
        LoadForm();
        UpdatePreviewInstance();
        GUI.FocusControl(null);
    }

    // Fill the add form from the manifest, if the object is in it
    void LoadForm()
    {
        graspRows.Clear();
        ManifestObject obj;
        if (manifest.TryGetValue(selectedName, out obj))
        {
            category = obj.category;
            autoGrasps = obj.auto_grasps;
            yaw = obj.yaw;
            farthestTo = Mathf.Max(0, Array.IndexOf(FARTHEST_TO, obj.farthest_to));
            SetGraspRows(obj.grasps);
        }
        else
        {
            // Nothing carried over from the previously selected object
            category = "";
            autoGrasps = true;
            yaw = 0f;
            farthestTo = 0;
        }
    }

    void SetGraspRows(ManifestGrasp[] grasps)
    {
        graspRows.Clear();
        foreach (ManifestGrasp grasp in grasps ?? new ManifestGrasp[0])
        {
            GraspCatalog.GraspName parsed = GraspCatalog.ParseGraspName(grasp.name);
            graspRows.Add(new GraspRow
            {
                type = Mathf.Max(0, Array.IndexOf(graspTypes, parsed.graspType)),
                side = Mathf.Max(0, Array.IndexOf(SIDES, parsed.objectSide)),
                nops = parsed.noPronationSupination,
                usd = parsed.upsideDown,
                susd = parsed.samplingUpsideDown,
                border = parsed.onTableBorder,
                approach = Mathf.Max(0, Array.IndexOf(APPROACHES, grasp.approach))
            });
        }
    }

    // Same naming as the grasp parallelepipeds of the YCB prefabs, e.g. large_diameter[left][susd]
    string GraspName(GraspRow row)
    {
        return graspTypes[row.type] + (row.side > 0 ? "[" + SIDES[row.side] + "]" : "") + (row.nops ? "[nops]" : "")
               + (row.usd ? "[usd]" : "") + (row.susd ? "[susd]" : "") + (row.border ? "[border]" : "");
    }

    // ------------------------------------------------------------ thumbnails

    Texture2D Thumbnail(string name, int index)
    {
        string key = name + "/" + index;
        Texture2D texture;
        if (thumbnails.TryGetValue(key, out texture))
            return texture;
        if (!thumbnailRequests.ContainsKey(key))
        {
            UnityWebRequest request = UnityWebRequestTexture.GetTexture(
                FUEL_MODELS_URL + Uri.EscapeDataString(name) + "/tip/files/thumbnails/" + index + ".jpg");
            request.SendWebRequest();
            thumbnailRequests[key] = request;
        }
        return null;
    }

    void PollThumbnails()
    {
        if (thumbnailRequests.Count == 0)
            return;
        foreach (string key in thumbnailRequests.Keys.ToList())
        {
            UnityWebRequest request = thumbnailRequests[key];
            if (!request.isDone)
                continue;
            thumbnails[key] = request.result == UnityWebRequest.Result.Success
                ? DownloadHandlerTexture.GetContent(request) : null;
            request.Dispose();
            thumbnailRequests.Remove(key);
            Repaint();
        }
    }

    // ------------------------------------------------------------ 3D preview

    void ClearPreview()
    {
        if (previewEditor != null)
            DestroyImmediate(previewEditor);
        previewEditor = null;
        previewPrefabPath = null;
    }

    void UpdatePreviewInstance()
    {
        ClearPreview();
        if (selectedName == null || !IsAdded(selectedName))
            return;
        previewPrefabPath = GsoPrefabBuilder.PrefabPath(selectedName);
        previewEditor = Editor.CreateEditor(AssetDatabase.LoadAssetAtPath<GameObject>(previewPrefabPath));
    }

    void DrawPreview(Rect rect)
    {
        if (previewEditor == null)
            return;
        // The preview uses the event, hence its type is read before
        bool repaint = Event.current.type == EventType.Repaint;
        previewEditor.OnInteractivePreviewGUI(rect, GUIStyle.none);
        if (showGraspBoxes && repaint)
            DrawGraspBoxes(rect);
    }

    // The grasp parallelepipeds are inactive, hence not rendered by the preview: they are
    // drawn as wireframes, projecting their corners with the camera of the preview. The
    // camera and the instance of the preview are internal to GameObjectInspector: if
    // they cannot be found (e.g. in another Unity version), only the object is shown
    void DrawGraspBoxes(Rect rect)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        System.Collections.IDictionary previews = previewEditor.GetType().GetField("m_PreviewInstances", flags)
            ?.GetValue(previewEditor) as System.Collections.IDictionary;
        object preview = previews?.Values.Cast<object>().FirstOrDefault();
        if (preview == null)
            return;
        PreviewRenderUtility utility = preview.GetType().GetField("renderUtility", flags)?.GetValue(preview) as PreviewRenderUtility;
        GameObject instance = preview.GetType().GetProperty("gameObject", flags)?.GetValue(preview) as GameObject;
        Transform textured = instance == null ? null : instance.transform.Find("textured");
        // The preview instance has no collider: the parallelepiped sizes are taken from the prefab
        Transform prefabTextured = AssetDatabase.LoadAssetAtPath<GameObject>(previewPrefabPath).transform.Find("textured");
        if (utility == null || textured == null || textured.childCount != prefabTextured.childCount)
            return;

        Camera camera = utility.camera;
        Func<Vector3, Vector3> toGui = world =>
        {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            return new Vector3(rect.x + viewport.x * rect.width, rect.y + (1f - viewport.y) * rect.height, 0f);
        };
        for (int i = 0; i < textured.childCount; i++)
        {
            Transform grasp = textured.GetChild(i);
            BoxCollider box = prefabTextured.GetChild(i).GetComponent<BoxCollider>();
            if (box == null)
                continue;
            Vector3[] corners = new Vector3[8];
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f,
                                             (c & 4) == 0 ? -0.5f : 0.5f);
                corners[c] = toGui(grasp.TransformPoint(box.center + Vector3.Scale(corner, box.size)));
            }
            Handles.color = GRASP_COLORS[i % GRASP_COLORS.Length];
            for (int c = 0; c < 8; c++)
            {
                // The 12 edges join the corners differing in one coordinate
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((c & bit) == 0)
                        Handles.DrawAAPolyLine(2f, corners[c], corners[c | bit]);
            }
            // The approach direction (z axis), towards the parallelepiped
            Handles.DrawAAPolyLine(2f, toGui(grasp.TransformPoint(box.center - new Vector3(0f, 0f, box.size.z * 2f))),
                                   toGui(grasp.TransformPoint(box.center)));
        }
        Handles.color = Color.white;
    }

    // ------------------------------------------------------------- python tool

    static string Quote(string arg)
    {
        return "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // The full path of the Python executable. A bare name (e.g. python3) is looked up in
    // the PATH currently set in Windows, not in the one Unity inherited when it started,
    // such that a Python added to PATH afterwards is found without restarting Unity.
    // The WindowsApps folder is skipped: its python.exe and python3.exe are Microsoft
    // Store stubs, which only print "Python was not found"
    static string ResolvePython(string python)
    {
        if (Application.platform != RuntimePlatform.WindowsEditor || Path.IsPathRooted(python))
            return python;
        string path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) + ";"
                      + Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) + ";"
                      + Environment.GetEnvironmentVariable("Path");
        string[] names = Path.HasExtension(python) ? new[] { python } : new[] { python + ".exe", python };
        foreach (string dir in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string expanded = Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"'));
            if (expanded.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            foreach (string name in names)
            {
                try
                {
                    string candidate = Path.Combine(expanded, name);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Invalid characters in a PATH entry
                }
            }
        }
        return python;
    }

    bool RunTool(params string[] args)
    {
        string python = ResolvePython(EditorPrefs.GetString(PYTHON_PREF, "python3"));
        ProcessStartInfo info = new ProcessStartInfo(python, Quote(ToolPath) + " " + string.Join(" ", args.Select(Quote)))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = RepoDir
        };
        try
        {
            using (Process process = Process.Start(info))
            {
                // Read stderr asynchronously, such that neither pipe can fill up and block
                var stderr = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                // The output of search is the whole list of models, only its last line is kept
                if (args[0] == "search")
                    output = output.TrimEnd().Split('\n').Last();
                string result = "$ import_gso.py " + string.Join(" ", args) + "\n" + output + stderr.Result;
                log += result + "\n";
                if (process.ExitCode != 0)
                {
                    // 9009: the Microsoft Store stub ran instead of Python
                    if (process.ExitCode == 9009)
                    {
                        result += "\n" + python + " is not a Python installation: set the full path of python.exe " +
                                  "in the Python executable field below";
                        log += result.Substring(result.LastIndexOf('\n') + 1) + "\n";
                    }
                    Debug.LogError("[GSO Browser] " + result);
                    return false;
                }
                return true;
            }
        }
        catch (Exception e)
        {
            string result = "Cannot run " + python + " (set the Python executable below): " + e.Message;
            log += result + "\n";
            Debug.LogError("[GSO Browser] " + result);
            return false;
        }
    }

    void AddToPrefabs()
    {
        List<string> args = new List<string> { "add", selectedName, "--category", category.Trim(), "--replace" };
        // The add command replaces the whole manifest entry, hence the yaw is passed again
        if (yaw != 0f)
            args.AddRange(new[] { "--yaw", yaw.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        if (farthestTo > 0)
            args.AddRange(new[] { "--farthest-to", FARTHEST_TO[farthestTo] });
        if (autoGrasps)
        {
            foreach (GraspRow row in graspRows)
                args.AddRange(new[] { "--grasp", GraspName(row) + ":" + APPROACHES[row.approach] });
        }
        else
        {
            args.Add("--manual-grasps");
        }

        log = "";
        EditorUtility.DisplayProgressBar("GSO Browser", "Downloading " + selectedName, 0.3f);
        try
        {
            if (!RunTool(args.ToArray()) || !RunTool("download", "--names", selectedName))
                return;
            EditorUtility.DisplayProgressBar("GSO Browser", "Building the prefab of " + selectedName, 0.8f);
            AssetDatabase.Refresh();
            string prefabPath = GsoPrefabBuilder.Build("Assets/3Dmodels/GSO/" + selectedName + "/grasp_spec.json");
            log += "\nBuilt " + prefabPath;
            Debug.Log("[GSO Browser] Added " + selectedName + ": " + prefabPath);
        }
        catch (Exception e)
        {
            log += "\n" + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            LoadManifest();
            ApplyFilters();
            UpdatePreviewInstance();
        }
    }

    void RemoveFromProject()
    {
        if (!EditorUtility.DisplayDialog("GSO Browser", "Remove " + selectedName + " from the manifest, " +
                                         "and delete its model and prefab?", "Remove", "Cancel"))
            return;
        ClearPreview();
        log = "";
        RunTool("remove", selectedName);
        AssetDatabase.Refresh();
        LoadManifest();
        ApplyFilters();
    }

    // -------------------------------------------------------------------- GUI

    void OnGUI()
    {
        HandleKeyboard();

        Rect listRect = new Rect(0f, 0f, LIST_WIDTH, position.height);
        Rect detailsRect = new Rect(LIST_WIDTH + 4f, 0f, position.width - LIST_WIDTH - 8f, position.height);
        GUILayout.BeginArea(listRect);
        DrawList();
        GUILayout.EndArea();
        GUILayout.BeginArea(detailsRect);
        detailsScroll = EditorGUILayout.BeginScrollView(detailsScroll);
        if (selected >= 0)
            DrawDetails();
        else
            EditorGUILayout.HelpBox("Select an object on the left (Up/Down arrows to scroll through them, " +
                                    "Left/Right arrows to change thumbnail).", MessageType.Info);
        DrawSettings();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void HandleKeyboard()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField || filtered.Count == 0)
            return;
        int row = filtered.IndexOf(selected);
        if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.UpArrow)
        {
            row = Mathf.Clamp(row + (e.keyCode == KeyCode.DownArrow ? 1 : -1), 0, filtered.Count - 1);
            Select(filtered[row]);
            // Keep the selected row visible
            float listHeight = position.height - 80f;
            listScroll.y = Mathf.Clamp(listScroll.y, (row + 1) * ROW_HEIGHT - listHeight, row * ROW_HEIGHT);
            e.Use();
        }
        else if ((e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.LeftArrow) && selected >= 0)
        {
            thumbnailIndex = (thumbnailIndex + (e.keyCode == KeyCode.RightArrow ? 1 : NUM_THUMBNAILS - 1)) % NUM_THUMBNAILS;
            e.Use();
        }
    }

    void DrawList()
    {
        EditorGUI.BeginChangeCheck();
        search = EditorGUILayout.TextField("Search", search);
        fuelCategory = EditorGUILayout.Popup("GSO category", fuelCategory, fuelCategories);
        addedFilter = EditorGUILayout.Popup("Show", addedFilter, ADDED_FILTER);
        if (EditorGUI.EndChangeCheck())
            ApplyFilters();
        EditorGUILayout.LabelField(filtered.Count + " of " + catalog.Length + " objects, " +
                                   addedCount + " added", EditorStyles.miniLabel);

        // Only the visible rows are drawn
        Rect viewRect = GUILayoutUtility.GetRect(LIST_WIDTH, LIST_WIDTH, 100f, 10000f, GUILayout.ExpandHeight(true));
        Rect contentRect = new Rect(0f, 0f, viewRect.width - 16f, filtered.Count * ROW_HEIGHT);
        listScroll = GUI.BeginScrollView(viewRect, listScroll, contentRect);
        int first = Mathf.Max(0, (int)(listScroll.y / ROW_HEIGHT));
        int last = Mathf.Min(filtered.Count, first + (int)(viewRect.height / ROW_HEIGHT) + 2);
        for (int i = first; i < last; i++)
        {
            FuelModel model = catalog[filtered[i]];
            Rect row = new Rect(0f, i * ROW_HEIGHT, contentRect.width, ROW_HEIGHT);
            bool added = IsAdded(model.name);
            ManifestObject obj;
            string label = (added ? "✓ " : "    ") + model.name
                           + (manifest.TryGetValue(model.name, out obj) ? "  [" + obj.category + "]" : "");
            if (filtered[i] == selected)
                EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.9f, 0.5f));
            if (GUI.Button(row, label, added ? EditorStyles.boldLabel : EditorStyles.label))
                Select(filtered[i]);
        }
        GUI.EndScrollView();
    }

    void DrawDetails()
    {
        FuelModel model = catalog[selected];
        bool added = IsAdded(model.name);
        EditorGUILayout.LabelField(model.name, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(model.description, EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("GSO category: " + string.Join(", ", model.categories ?? new string[0]) +
                                   "    Size: " + (model.filesize / 1e6f).ToString("F1") + " MB    License: " +
                                   model.license_name, EditorStyles.miniLabel);

        // Thumbnails from Gazebo Fuel
        EditorGUILayout.BeginHorizontal();
        Rect big = GUILayoutUtility.GetRect(320f, 240f, GUILayout.Width(320f), GUILayout.Height(240f));
        Texture2D thumbnail = Thumbnail(model.name, thumbnailIndex);
        if (thumbnail != null)
            GUI.DrawTexture(big, thumbnail, ScaleMode.ScaleToFit);
        else
            EditorGUI.LabelField(big, "Loading...", EditorStyles.centeredGreyMiniLabel);

        // 3D preview of the prefab, once added
        if (added)
        {
            if (previewPrefabPath != GsoPrefabBuilder.PrefabPath(model.name) || previewEditor == null)
                UpdatePreviewInstance();
            Rect preview = GUILayoutUtility.GetRect(320f, 240f, GUILayout.Width(320f), GUILayout.Height(240f));
            DrawPreview(preview);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < NUM_THUMBNAILS; i++)
        {
            Rect small = GUILayoutUtility.GetRect(60f, 45f, GUILayout.Width(60f), GUILayout.Height(45f));
            Texture2D texture = Thumbnail(model.name, i);
            if (texture != null)
                GUI.DrawTexture(small, texture, ScaleMode.ScaleToFit);
            if (i == thumbnailIndex)
                Handles.DrawSolidRectangleWithOutline(small, Color.clear, Color.white);
            if (GUI.Button(small, GUIContent.none, GUIStyle.none))
                thumbnailIndex = i;
        }
        if (added)
        {
            GUILayout.Space(20f);
            showGraspBoxes = EditorGUILayout.ToggleLeft("Show grasp boxes (drag the 3D preview to rotate)", showGraspBoxes);
        }
        EditorGUILayout.EndHorizontal();

        if (added)
            DrawAddedObject();
        EditorGUILayout.Space();
        DrawAddForm(added);

        if (!string.IsNullOrEmpty(log))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(log.Trim(), MessageType.None);
        }
    }

    void DrawAddedObject()
    {
        string prefabPath = GsoPrefabBuilder.PrefabPath(selectedName);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Transform textured = prefab.transform.Find("textured");
        EditorGUILayout.LabelField("Added: " + prefabPath + ", category " + GraspCatalog.GetCategory(prefab),
                                   EditorStyles.boldLabel);
        if (textured.childCount == 0)
            EditorGUILayout.HelpBox("No grasp box yet: open the prefab and add them with Tools -> Grasp Objects -> " +
                                    "Add Grasp Box to Selected Object.", MessageType.Warning);
        for (int i = 0; i < textured.childCount; i++)
        {
            Color color = GRASP_COLORS[i % GRASP_COLORS.Length];
            string name = textured.GetChild(i).name;
            EditorGUILayout.LabelField("<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">■</color> " + name +
                                       " (" + (GraspCatalog.GetPreshape(name) ?? "unknown grasp type") + ")",
                                       new GUIStyle(EditorStyles.label) { richText = true });
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open prefab"))
            AssetDatabase.OpenAsset(prefab);
        if (GUILayout.Button("Ping in Project"))
            EditorGUIUtility.PingObject(prefab);
        if (GUILayout.Button("Remove from project"))
            RemoveFromProject();
        EditorGUILayout.EndHorizontal();
    }

    void DrawAddForm(bool added)
    {
        EditorGUILayout.LabelField(added ? "Change and rebuild" : "Add to prefabs", EditorStyles.boldLabel);

        string[] knownCategories = GraspCatalog.instance2object.Values.Concat(manifest.Values.Select(o => o.category))
                                   .Distinct().OrderBy(c => c).ToArray();
        EditorGUILayout.BeginHorizontal();
        category = EditorGUILayout.TextField("Category", category);
        int known = EditorGUILayout.Popup(-1, knownCategories, GUILayout.Width(120f));
        if (known >= 0)
            category = knownCategories[known];
        EditorGUILayout.EndHorizontal();
        yaw = EditorGUILayout.FloatField(new GUIContent("Yaw (degrees)",
            "Rotation around the vertical axis, applied before \"Farthest part to\""), yaw);
        farthestTo = EditorGUILayout.Popup(new GUIContent("Farthest part to",
            "Rotate the object such that its most protruding part (e.g. the handle of a mug) faces this side"),
            farthestTo, FARTHEST_TO);

        autoGrasps = EditorGUILayout.ToggleLeft("Add grasp boxes automatically", autoGrasps);
        if (autoGrasps)
        {
            // Copy the grasps of an object of the same category already in the manifest
            ManifestObject[] sameCategory = manifest.Values
                .Where(o => o.category == category.Trim() && o.name != selectedName && o.auto_grasps).ToArray();
            if (sameCategory.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                copyFrom = EditorGUILayout.Popup("Copy grasps from", Mathf.Min(copyFrom, sameCategory.Length - 1),
                                                 sameCategory.Select(o => o.name).ToArray());
                if (GUILayout.Button("Copy", GUILayout.Width(60f)))
                {
                    SetGraspRows(sameCategory[copyFrom].grasps);
                    farthestTo = Mathf.Max(0, Array.IndexOf(FARTHEST_TO, sameCategory[copyFrom].farthest_to));
                }
                EditorGUILayout.EndHorizontal();
            }

            for (int i = 0; i < graspRows.Count; i++)
            {
                GraspRow row = graspRows[i];
                EditorGUILayout.BeginHorizontal();
                row.type = EditorGUILayout.Popup(row.type, graspTypes, GUILayout.Width(140f));
                EditorGUILayout.LabelField("side", GUILayout.Width(28f));
                row.side = EditorGUILayout.Popup(row.side, SIDES, GUILayout.Width(55f));
                row.nops = EditorGUILayout.ToggleLeft(new GUIContent("nops", "No pronation-supination"), row.nops, GUILayout.Width(50f));
                row.usd = EditorGUILayout.ToggleLeft(new GUIContent("usd", "Object upside down"), row.usd, GUILayout.Width(45f));
                row.susd = EditorGUILayout.ToggleLeft(new GUIContent("susd", "Object randomly standing or upside down"), row.susd, GUILayout.Width(50f));
                row.border = EditorGUILayout.ToggleLeft(new GUIContent("border", "Object on the table border"), row.border, GUILayout.Width(58f));
                EditorGUILayout.LabelField("approach", GUILayout.Width(55f));
                row.approach = EditorGUILayout.Popup(row.approach, APPROACHES, GUILayout.Width(65f));
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    graspRows.RemoveAt(i);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("    " + GraspName(row) + " (" + GraspCatalog.GetPreshape(GraspName(row)) + ")",
                                           EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Add grasp", GUILayout.Width(100f)))
                graspRows.Add(new GraspRow { type = Mathf.Max(0, Array.IndexOf(graspTypes, "tripod")) });
        }
        else
        {
            EditorGUILayout.HelpBox("The prefab is created without grasp boxes: add them by hand in the prefab " +
                                    "(Tools -> Grasp Objects -> Add Grasp Box to Selected Object). " +
                                    "Rebuilding the prefab keeps them.", MessageType.Info);
        }

        string problem = string.IsNullOrWhiteSpace(category) ? "Set the category"
                         : autoGrasps && graspRows.Count == 0 ? "Add at least one grasp, or untick the automatic grasp boxes"
                         : null;
        if (problem != null)
            EditorGUILayout.HelpBox(problem, MessageType.Warning);
        using (new EditorGUI.DisabledScope(problem != null))
        {
            if (GUILayout.Button(added ? "Update and rebuild prefab" : "Add to prefabs", GUILayout.Height(28f)))
            {
                AddToPrefabs();
                GUIUtility.ExitGUI();
            }
        }
        if (added && autoGrasps)
            EditorGUILayout.LabelField("Rebuilding overwrites the changes made by hand to the grasp boxes of the prefab.",
                                       EditorStyles.miniLabel);
    }

    void DrawSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        string python = EditorGUILayout.TextField("Python executable", EditorPrefs.GetString(PYTHON_PREF, "python3"));
        EditorPrefs.SetString(PYTHON_PREF, python);
        if (GUILayout.Button("Refresh catalog", GUILayout.Width(120f)))
        {
            LoadCatalog(true);
            GUIUtility.ExitGUI();
        }
        EditorGUILayout.EndHorizontal();
    }
}
