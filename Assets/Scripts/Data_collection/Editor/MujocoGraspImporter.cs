using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import the grasp boxes saved in the MuJoCo viewer of the grasping_physics_simulation
/// repository (save command of preshape_viewer.py): a hand pose placed and checked there
/// becomes a grasp parallelepiped of the prefab. Each object folder of its assets/objects
/// (the folder the exporter writes) may hold a mujoco_boxes.json, with the boxes in the
/// prefab root frame (Unity axes): name (grasp type and tags), position, size and axes.
/// The boxes are created as the other ones (GsoPrefabBuilder.CreateGraspBox: BoxCollider,
/// transparent material, inactive with the renderer disabled), the prefab is saved, the
/// object is exported again (so the viewer shows them as boxes) and the file is renamed
/// mujoco_boxes.imported-&lt;time&gt;.json so that they are not imported twice.
/// </summary>
public static class MujocoGraspImporter
{
    const string SAVED_FILE = "mujoco_boxes.json";
    // A saved box with the name of an existing one this close is the same box
    const float SAME_POSITION = 0.002f;

    [Serializable]
    class SavedBox
    {
        public string name;
        public float[] position;
        public float[] size;
        public float[] right;
        public float[] up;
        public float[] forward;
    }

    [Serializable]
    class SavedFile
    {
        public string @object;
        public string prefab;
        public SavedBox[] boxes;
    }

    static Vector3 V(float[] v)
    {
        return new Vector3(v[0], v[1], v[2]);
    }

    [MenuItem("Tools/Grasp Objects/Import Grasp Boxes from MuJoCo")]
    public static void ImportAll()
    {
        string objectsDir = MujocoGraspExporter.FindOutDir();
        if (objectsDir == null || !Directory.Exists(objectsDir))
        {
            Debug.LogWarning("Import Grasp Boxes from MuJoCo: no assets/objects folder of grasping_physics_simulation found");
            return;
        }
        string[] files = Directory.GetDirectories(objectsDir)
                         .Select(d => Path.Combine(d, SAVED_FILE)).Where(File.Exists).OrderBy(f => f).ToArray();
        if (files.Length == 0)
        {
            Debug.Log("Import Grasp Boxes from MuJoCo: no " + SAVED_FILE + " in " + objectsDir
                      + " (save boxes in the viewer with the save command)");
            return;
        }
        int imported = 0;
        foreach (string file in files)
            imported += Import(file);
        Debug.Log("Imported " + imported + " grasp boxes from MuJoCo (" + files.Length + " objects)");
    }

    /// <summary>
    /// Import the boxes of one mujoco_boxes.json into its prefab; returns how many were added
    /// </summary>
    public static int Import(string file)
    {
        SavedFile saved = JsonUtility.FromJson<SavedFile>(File.ReadAllText(file));
        if (saved == null || string.IsNullOrEmpty(saved.prefab) || saved.boxes == null)
        {
            Debug.LogWarning("Import Grasp Boxes from MuJoCo: " + file + " has no prefab or boxes, skipped");
            return 0;
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(saved.prefab) == null)
        {
            Debug.LogWarning("Import Grasp Boxes from MuJoCo: prefab " + saved.prefab + " not found, " + file + " skipped");
            return 0;
        }

        int added = 0;
        List<string> skipped = new List<string>();
        GameObject root = PrefabUtility.LoadPrefabContents(saved.prefab);
        try
        {
            Transform textured = root.transform.Find("textured");
            if (textured == null)
            {
                Debug.LogWarning("Import Grasp Boxes from MuJoCo: " + saved.prefab + " has no textured child, skipped");
                return 0;
            }
            Material material = GsoPrefabBuilder.GraspMaterial();
            foreach (SavedBox box in saved.boxes)
            {
                if (GraspCatalog.GetPreshape(box.name) == null)
                {
                    skipped.Add(box.name + " (grasp type not in GraspCatalog.graspType2preshape)");
                    continue;
                }
                Vector3 position = V(box.position);
                bool exists = textured.Cast<Transform>().Any(t => t.name == box.name
                    && (root.transform.InverseTransformPoint(t.position) - position).magnitude < SAME_POSITION);
                if (exists)
                {
                    skipped.Add(box.name + " (already in the prefab)");
                    continue;
                }
                // The saved axes are those of the box in the root frame: forward the approach
                // direction, up across the palm; right follows (Unity: right = up x forward)
                GameObject created = GsoPrefabBuilder.CreateGraspBox(textured, box.name, position,
                    Quaternion.LookRotation(V(box.forward), V(box.up)), V(box.size), material);
                created.GetComponent<Renderer>().enabled = false;
                created.SetActive(false);
                added++;
            }
            if (added > 0)
                PrefabUtility.SaveAsPrefabAsset(root, saved.prefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        foreach (string s in skipped)
            Debug.LogWarning("Import Grasp Boxes from MuJoCo: " + saved.prefab + ": " + s + " skipped");
        if (added > 0)
        {
            Debug.Log("Import Grasp Boxes from MuJoCo: " + added + " grasp boxes added to " + saved.prefab);
            WarnAutomaticGrasps(saved.prefab);
            MujocoGraspExporter.Export(saved.prefab);
        }
        // Renamed so that the boxes are not imported twice (a counter if imported twice in a second)
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string done = Path.Combine(Path.GetDirectoryName(file), "mujoco_boxes.imported-" + stamp + ".json");
        for (int i = 2; File.Exists(done); i++)
            done = Path.Combine(Path.GetDirectoryName(file), "mujoco_boxes.imported-" + stamp + "-" + i + ".json");
        File.Move(file, done);
        return added;
    }

    /// <summary>
    /// A GSO object with automatic grasps loses the boxes added by hand when its prefab is
    /// rebuilt (Build GSO Prefabs, or Update and rebuild prefab in the GSO Browser)
    /// </summary>
    static void WarnAutomaticGrasps(string prefabPath)
    {
        if (!prefabPath.StartsWith("Assets/Prefabs/GSO/"))
            return;
        string name = Path.GetFileNameWithoutExtension(prefabPath);
        string spec = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Assets", "3Dmodels", "GSO",
                                   name, "grasp_spec.json");
        if (File.Exists(spec) && File.ReadAllText(spec).Replace(" ", "").Contains("\"auto_grasps\":false"))
            return;
        Debug.LogWarning("Import Grasp Boxes from MuJoCo: " + name + " has automatic grasps: rebuilding its prefab "
                         + "would drop the imported boxes. Switch it to hand-placed grasps first (GSO Browser: untick "
                         + "'Add grasp boxes automatically' and 'Update and rebuild prefab').");
    }
}
