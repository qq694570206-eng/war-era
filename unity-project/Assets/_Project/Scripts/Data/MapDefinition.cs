// ================================================================
// MapDefinition.cs — 地图数据定义（ScriptableObject）
// ================================================================
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapDef_", menuName = "WarEra/Map Definition")]
public class MapDefinition : ScriptableObject
{
    [Header("Identity")]
    public string mapId = "map_id";
    public string displayName = "New Map";
    public string themeId = "ww2"; // ancient/ww1/ww2/postwar/modern
    public int playerCount = 2; // 2=1v1, 4=2v2/4人

    [Header("World")]
    public Vector2 worldSize = new Vector2(2400, 1600);
    public Sprite terrainPreview; // 小地图缩略图

    [Header("Spawn Points")]
    public Vector3[] playerStartPositions;

    [Header("Ore Nodes")]
    public OreNodeData[] oreNodes;

    [Header("Obstacles")]
    public ObstacleData[] obstacles;

    [Header("Control Points")]
    public ControlPointData[] controlPoints;

    [Header("Decorations")]
    public DecorationData[] decorations;

    [Header("Description")]
    [TextArea] public string description;
    public string mapType = "1v1"; // 1v1/2v2/4人
}

[System.Serializable]
public struct OreNodeData
{
    public Vector3 position;
    public float radius;
    public bool isInfinite;
    public int maxAmount;
}

[System.Serializable]
public struct ObstacleData
{
    public Vector3 position;
    public Vector2 size;
    public ObstacleType type;
}

[System.Serializable]
public struct ControlPointData
{
    public Vector3 position;
    public ControlPointType type;
    public float radius;
    public float captureTime;
}

[System.Serializable]
public struct DecorationData
{
    public Vector3 position;
    public Sprite sprite;
    public float rotation;
}

public enum ObstacleType
{
    BuildingRuin,   // 建筑废墟
    Rock,           // 岩石
    Forest,         // 树丛
    BarbedWire,     // 铁丝网
    Sandbag,        // 沙袋
    Wreckage,       // 残骸
    Roadblock       // 路障
}

public enum ControlPointType
{
    Economy,    // 经济点(+矿石/s)
    Tech,       // 科技点(+科技点/s)
    Vision,     // 视野点(全图可见)
    Highland    // 高地(视野+50%)
}
