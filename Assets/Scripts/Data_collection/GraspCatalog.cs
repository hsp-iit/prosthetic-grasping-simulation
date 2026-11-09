using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The tables shared by the data collection scripts: which preshape each grasp type
/// is labeled with, and which category each object belongs to. It also parses the
/// names of the grasp parallelepipeds, e.g. large_diameter[left][susd], where:
/// - [left], [right]: the side of the object where the grasp is
/// - [nops]: no pronation-supination is performed
/// - [usd]: the object is put upside down
/// - [susd]: the object is randomly put standing or upside down
/// - [border]: the object is placed on the border of the table
/// </summary>
public static class GraspCatalog
{
    public static readonly Dictionary<string, string> graspType2preshape = new Dictionary<string, string>()
    {
        {"adducted_thumb", "lateral_no3" },
        {"large_diameter", "power_no3" },
        {"small_diameter", "power_no3" },
        {"medium_wrap", "power_no3" },
        {"sphere_4fingers", "power_no3" },
        {"power_sphere", "power_no3" },
        {"power_disk", "power_no3"},
        {"prismatic_4fingers", "pinch_no3" },
        {"tripod", "pinch_3" },
        {"prismatic_2fingers", "pinch_3" },

        // TODO: are these below used?
        //{"precision_sphere", "pinch_no3" },
        {"precision_sphere", "power_no3" },
        {"tip_pinch", "pinch_3"}
    };

    // The category of the objects whose prefab has no GraspObjectInfo component
    // (e.g., the YCB ones). Objects imported afterwards (e.g., from Google Scanned
    // Objects) carry their category in the GraspObjectInfo component instead
    public static readonly Dictionary<string, string> instance2object = new Dictionary<string, string>()
    {
        {"019_pitcher_base", "dispenser" },
        {"006_mustard_bottle", "dispenser" },
        {"029_plate", "plate" },
        {"026_sponge", "sponge" },
        {"010_potted_meat_can", "can" },
        {"025_mug", "mug" },
        {"018_plum", "small_ball" },
        {"055_baseball", "big_ball"},
        {"031_spoon", "small_tool" },
        {"011_banana", "long_fruit"},
        {"021_bleach_cleanser", "dispenser"},
        {"033_spatula", "big_tool"},
        {"037_scissors",  "small_tool"},
        {"040_large_marker", "small_tool"},
        {"048_hammer", "big_tool"},
        {"001_chips_can", "tube" },
        {"red_wood_block", "small_cube" },


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
        {"pencil", "small_tool"},
        {"pendrive", "small_tool"},
        {"scotch", "small_tool"},
        {"sharpner", "small_tool"},
        {"tape_duct", "small_tool"},
        {"toothbrush", "small_tool"},
        {"wallet_closed", "wallet"},
        {"wallet_opened", "wallet"},
    };

    /// <summary>
    /// The category of an object (prefab or instance in scene), or null if unknown
    /// </summary>
    public static string GetCategory(GameObject obj)
    {
        GraspObjectInfo info = obj.GetComponent<GraspObjectInfo>();
        if (info != null && !string.IsNullOrEmpty(info.category))
            return info.category;

        string instanceName = obj.name.Replace("(Clone)", "");
        string category;
        return instance2object.TryGetValue(instanceName, out category) ? category : null;
    }

    public struct GraspName
    {
        public string graspType;
        // One of [left, right, none]
        public string objectSide;
        public bool noPronationSupination;
        public bool upsideDown;
        public bool samplingUpsideDown;
        public bool onTableBorder;
    }

    /// <summary>
    /// Parse the name of a grasp parallelepiped, e.g. large_diameter[left][susd]
    /// </summary>
    public static GraspName ParseGraspName(string name)
    {
        GraspName parsed = new GraspName
        {
            noPronationSupination = name.Contains("[nops]"),
            upsideDown = name.Contains("[usd]"),
            samplingUpsideDown = name.Contains("[susd]"),
            onTableBorder = name.Contains("[border]"),
            objectSide = "none"
        };
        name = name.Replace("[nops]", "").Replace("[usd]", "").Replace("[susd]", "").Replace("[border]", "");

        if (name.Contains("[left]"))
        {
            parsed.objectSide = "left";
            name = name.Replace("[left]", "");
        }
        else if (name.Contains("[right]"))
        {
            parsed.objectSide = "right";
            name = name.Replace("[right]", "");
        }
        parsed.graspType = name;

        return parsed;
    }

    /// <summary>
    /// The preshape of a grasp parallelepiped, or null if its grasp type is unknown
    /// </summary>
    public static string GetPreshape(string graspName)
    {
        string preshape;
        return graspType2preshape.TryGetValue(ParseGraspName(graspName).graspType, out preshape) ? preshape : null;
    }
}
