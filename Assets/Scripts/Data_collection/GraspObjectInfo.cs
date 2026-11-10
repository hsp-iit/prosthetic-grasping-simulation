using UnityEngine;

/// <summary>
/// Describes an object to grasp whose category is not in GraspCatalog.instance2object,
/// e.g. the objects imported from Google Scanned Objects. It is put on the prefab root
/// </summary>
public class GraspObjectInfo : MonoBehaviour
{
    /// <summary>
    /// The object category used in the labels, e.g. mug, can, dispenser
    /// </summary>
    public string category;

    /// <summary>
    /// The dataset the object comes from, e.g. YCB, GSO
    /// </summary>
    public string source;

    /// <summary>
    /// The name of the object in its source dataset
    /// </summary>
    public string sourceName;
}
