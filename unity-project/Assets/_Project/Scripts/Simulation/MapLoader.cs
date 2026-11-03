// ================================================================
// MapLoader.cs — 地图加载器
// 根据 MapDefinition 数据生成地图实体
// ================================================================
using UnityEngine;

public class MapLoader : MonoBehaviour
{
    public static MapLoader Instance { get; private set; }

    [Header("Available Maps")]
    public MapDefinition[] availableMaps;

    [Header("Prefabs")]
    public GameObject oreNodePrefab;
    public GameObject obstaclePrefab;
    public GameObject controlPointPrefab;

    [Header("Containers")]
    public Transform worldRoot;

    private MapDefinition currentMap;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 加载地图
    /// </summary>
    public void LoadMap(string mapId)
    {
        MapDefinition map = FindMap(mapId);
        if (map == null)
        {
            Debug.LogError($"Map '{mapId}' not found!");
            return;
        }

        currentMap = map;

        // 清空旧地图
        ClearWorld();

        // 生成矿点
        if (map.oreNodes != null)
        {
            foreach (var ore in map.oreNodes)
                SpawnOreNode(ore);
        }

        // 生成障碍物
        if (map.obstacles != null)
        {
            foreach (var obs in map.obstacles)
                SpawnObstacle(obs);
        }

        // 生成控制点
        if (map.controlPoints != null)
        {
            foreach (var cp in map.controlPoints)
                SpawnControlPoint(cp);
        }

        // 设置摄像机边界
        if (TouchInputSystem.Instance != null)
        {
            TouchInputSystem.Instance.worldBounds = map.worldSize;
        }

        // 重新生成流场网格
        if (FlowFieldPathfinding.Instance != null)
        {
            FlowFieldPathfinding.Instance.worldSize = map.worldSize;
        }

        Debug.Log($"[MapLoader] Map '{map.displayName}' loaded.");
    }

    /// <summary>
    /// 获取当前地图的出生点
    /// </summary>
    public Vector3 GetPlayerStartPosition(int playerIndex)
    {
        if (currentMap == null || currentMap.playerStartPositions == null) return Vector3.zero;
        if (playerIndex >= currentMap.playerStartPositions.Length) return Vector3.zero;
        return currentMap.playerStartPositions[playerIndex];
    }

    /// <summary>
    /// 获取当前地图
    /// </summary>
    public MapDefinition GetCurrentMap() => currentMap;

    MapDefinition FindMap(string mapId)
    {
        if (availableMaps == null) return null;
        foreach (var m in availableMaps)
        {
            if (m != null && m.mapId == mapId) return m;
        }
        return null;
    }

    void SpawnOreNode(OreNodeData data)
    {
        if (oreNodePrefab == null) return;
        var go = Instantiate(oreNodePrefab, data.position, Quaternion.identity, worldRoot);
        var node = go.GetComponent<OreNode>();
        if (node != null)
        {
            node.radius = data.radius;
            node.maxAmount = data.maxAmount;
            node.isInfinite = data.isInfinite;
            node.currentAmount = data.maxAmount;
        }
    }

    void SpawnObstacle(ObstacleData data)
    {
        if (obstaclePrefab == null) return;
        var go = Instantiate(obstaclePrefab, data.position, Quaternion.identity, worldRoot);
        var col = go.GetComponent<Collider2D>();
        if (col == null)
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.size = data.size;
        }
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.size = data.size;
    }

    void SpawnControlPoint(ControlPointData data)
    {
        if (controlPointPrefab == null) return;
        var go = Instantiate(controlPointPrefab, data.position, Quaternion.identity, worldRoot);
        var cp = go.GetComponent<ControlPoint>();
        if (cp != null)
        {
            cp.type = data.type;
            cp.radius = data.radius;
            cp.captureTime = data.captureTime;
        }
    }

    void ClearWorld()
    {
        if (worldRoot == null) return;
        for (int i = worldRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(worldRoot.GetChild(i).gameObject);
        }
    }
}
