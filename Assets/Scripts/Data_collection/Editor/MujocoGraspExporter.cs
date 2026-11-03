using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Export the grasp objects (YCB and GSO prefabs) with their grasp parallelepipeds for
/// the MuJoCo tools of the grasping_physics_simulation repository (preshape_viewer.py,
/// grasp_check.py). For each prefab with a textured child, assets/objects/&lt;name&gt;/ of that
/// repository gets:
/// - mesh.obj: the mesh of textured in the frame of the prefab root, with normals and uv
/// - texture.png: the base color texture, if it is a PNG
/// - object.json: category, bounds and the grasp parallelepipeds (name, preshape,
///   center, size and axes)
/// Everything is converted from the Unity frame (left-handed, y up) to the MuJoCo one
/// (right-handed, z up) by swapping y and z. The parallelepiped axes keep their meaning:
/// right (x, along the palm width), up (y, across it), forward (z, the approach
/// direction, towards the object).
/// </summary>
public static class MujocoGraspExporter
{
    static readonly string[] PREFAB_DIRS = { "Assets/Prefabs/YCB", "Assets/Prefabs/GSO" };

    const string OUT_DIR_PREF = "MujocoGraspExporter.outDir";
    const string PHYSICS_REPO = "grasping_physics_simulation";

    static string OutDir;

    /// <summary>
    /// assets/objects of the grasping_physics_simulation repository: the folder chosen
    /// before, or the repository next to this Unity project, or asked to the user.
    /// Returns null if none is chosen
    /// </summary>
    internal static string FindOutDir()
    {
        string saved = EditorPrefs.GetString(OUT_DIR_PREF, "");
        if (!string.IsNullOrEmpty(saved) && Directory.Exists(saved))
            return saved;
        string projectParent = Path.GetDirectoryName(Path.GetDirectoryName(Application.dataPath));
        string sibling = Path.Combine(projectParent, PHYSICS_REPO);
        if (Directory.Exists(sibling))
            return Path.Combine(sibling, "assets", "objects");
        string repo = EditorUtility.OpenFolderPanel("Select the " + PHYSICS_REPO + " repository", projectParent, "");
        if (string.IsNullOrEmpty(repo))
            return null;
        string chosen = Path.Combine(repo, "assets", "objects");
        EditorPrefs.SetString(OUT_DIR_PREF, chosen);
        return chosen;
    }

    [MenuItem("Tools/Grasp Objects/Export Objects for MuJoCo")]
    public static void ExportAll()
    {
        OutDir = FindOutDir();
        if (OutDir == null)
        {
            Debug.LogWarning("Export Objects for MuJoCo: no " + PHYSICS_REPO + " folder chosen, nothing exported");
            return;
        }
        Directory.CreateDirectory(OutDir);
        string[] paths = AssetDatabase.FindAssets("t:Prefab", PREFAB_DIRS)
                         .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
        int exported = 0;
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Export Objects for MuJoCo", paths[i], (float)i / paths.Length);
                if (Export(paths[i]))
                    exported++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        Debug.Log("Exported " + exported + " objects for MuJoCo to " + OutDir);
    }

    // Unity (left-handed, y up) to MuJoCo (right-handed, z up)
    static Vector3 ToMujoco(Vector3 v)
    {
        return new Vector3(v.x, v.z, v.y);
    }

    static string F(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    static string Json(Vector3 v)
    {
        return "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + "]";
    }

    static string JsonString(string s)
    {
        return s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// Export one prefab, returning false if it has no textured child with a mesh
    /// </summary>
    public static bool Export(string prefabPath)
    {
        if (OutDir == null)
            OutDir = FindOutDir();
        if (OutDir == null)
            return false;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform textured = root.transform.Find("textured");
            MeshFilter meshFilter = textured == null ? null : textured.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogWarning("MuJoCo export: " + prefabPath + " has no textured mesh, skipped");
                return false;
            }

            string dir = Path.Combine(OutDir, root.name);
            Directory.CreateDirectory(dir);
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix * textured.localToWorldMatrix;
            Bounds bounds = WriteMesh(meshFilter.sharedMesh, toRoot, Path.Combine(dir, "mesh.obj"));
            bool hasTexture = CopyTexture(textured.GetComponent<Renderer>(), Path.Combine(dir, "texture.png"));

            StringBuilder json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"name\": ").Append(JsonString(root.name)).Append(",\n");
            json.Append("  \"prefab\": ").Append(JsonString(prefabPath)).Append(",\n");
            json.Append("  \"category\": ").Append(JsonString(GraspCatalog.GetCategory(root))).Append(",\n");
            json.Append("  \"texture\": ").Append(hasTexture ? "true" : "false").Append(",\n");
            json.Append("  \"bounds_min\": ").Append(Json(bounds.min)).Append(",\n");
            json.Append("  \"bounds_max\": ").Append(Json(bounds.max)).Append(",\n");
            json.Append("  \"grasps\": [");
            bool first = true;
            foreach (Transform grasp in textured)
            {
                BoxCollider box = grasp.GetComponent<BoxCollider>();
                if (box == null)
                    continue;
                // The box axes and sizes in the root frame, robust to any scale of the
                // parallelepiped, of textured and of the collider itself
                Vector3 right = root.transform.InverseTransformVector(grasp.TransformVector(Vector3.right * box.size.x));
                Vector3 up = root.transform.InverseTransformVector(grasp.TransformVector(Vector3.up * box.size.y));
                Vector3 forward = root.transform.InverseTransformVector(grasp.TransformVector(Vector3.forward * box.size.z));
                Vector3 center = root.transform.InverseTransformPoint(grasp.TransformPoint(box.center));

                json.Append(first ? "\n" : ",\n");
                first = false;
                json.Append("    {\n");
                json.Append("      \"name\": ").Append(JsonString(grasp.name)).Append(",\n");
                json.Append("      \"preshape\": ").Append(JsonString(GraspCatalog.GetPreshape(grasp.name))).Append(",\n");
                json.Append("      \"center\": ").Append(Json(ToMujoco(center))).Append(",\n");
                json.Append("      \"size\": ").Append(Json(new Vector3(right.magnitude, up.magnitude, forward.magnitude))).Append(",\n");
                json.Append("      \"right\": ").Append(Json(ToMujoco(right.normalized))).Append(",\n");
                json.Append("      \"up\": ").Append(Json(ToMujoco(up.normalized))).Append(",\n");
                json.Append("      \"forward\": ").Append(Json(ToMujoco(forward.normalized))).Append("\n");
                json.Append("    }");
            }
            json.Append(first ? "]\n" : "\n  ]\n");
            json.Append("}\n");
            File.WriteAllText(Path.Combine(dir, "object.json"), json.ToString());
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Write the mesh as OBJ in the MuJoCo frame and return its bounds in that frame.
    /// The frame change is a reflection, hence the triangle winding is reversed to keep
    /// the faces pointing outwards
    /// </summary>
    static Bounds WriteMesh(Mesh mesh, Matrix4x4 toRoot, string path)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uv = mesh.uv;
        bool hasNormals = normals.Length == vertices.Length;
        bool hasUv = uv.Length == vertices.Length;

        StringBuilder obj = new StringBuilder();
        obj.Append("# Exported by MujocoGraspExporter from Unity\n");
        Bounds bounds = new Bounds();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = ToMujoco(toRoot.MultiplyPoint3x4(vertices[i]));
            if (i == 0)
                bounds = new Bounds(v, Vector3.zero);
            else
                bounds.Encapsulate(v);
            obj.Append("v ").Append(F(v.x)).Append(' ').Append(F(v.y)).Append(' ').Append(F(v.z)).Append('\n');
        }
        if (hasNormals)
            foreach (Vector3 normal in normals)
            {
                Vector3 n = ToMujoco(toRoot.MultiplyVector(normal).normalized);
                obj.Append("vn ").Append(F(n.x)).Append(' ').Append(F(n.y)).Append(' ').Append(F(n.z)).Append('\n');
            }
        if (hasUv)
            foreach (Vector2 t in uv)
                obj.Append("vt ").Append(F(t.x)).Append(' ').Append(F(t.y)).Append('\n');

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] triangles = mesh.GetTriangles(s);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                obj.Append('f');
                // Reversed winding
                foreach (int index in new[] { triangles[t], triangles[t + 2], triangles[t + 1] })
                {
                    int k = index + 1;
                    obj.Append(' ').Append(k);
                    if (hasUv || hasNormals)
                        obj.Append('/').Append(hasUv ? k.ToString() : "");
                    if (hasNormals)
                        obj.Append('/').Append(k);
                }
                obj.Append('\n');
            }
        }
        File.WriteAllText(path, obj.ToString());
        return bounds;
    }

    /// <summary>
    /// Copy the base color texture of the renderer if it is a PNG file (the format
    /// MuJoCo reads), returning whether it was copied
    /// </summary>
    static bool CopyTexture(Renderer renderer, string path)
    {
        Material material = renderer == null ? null : renderer.sharedMaterial;
        if (material == null)
            return false;
        Texture texture = null;
        foreach (string property in new[] { "_BaseColorMap", "_MainTex", "_BaseMap" })
            if (texture == null && material.HasProperty(property))
                texture = material.GetTexture(property);
        string texturePath = texture == null ? null : AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(texturePath) || !texturePath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
                File.Delete(path);
            return false;
        }
        File.Copy(Path.Combine(Path.GetDirectoryName(Application.dataPath), texturePath), path, true);
        return true;
    }
}
