// ================================================================
// FogOfWarSystem.cs — 战争迷雾
// 未探索=全黑 / 已探索不在视野=半暗 / 当前视野=透明
// 雷达站建造后全图探索
// ================================================================
using UnityEngine;

public class FogOfWarSystem : MonoBehaviour
{
    public static FogOfWarSystem Instance { get; private set; }

    [Header("Grid")]
    public float fogCellSize = 48f;
    public Vector2 worldSize = new Vector2(2400, 1600);

    [Header("Render")]
    public GameObject fogPlanePrefab; // 一个半透明黑色 plane，铺满全图
    public Material fogMaterial;

    private int gridW, gridH;
    private byte[] exploredGrid; // 0=未探索, 1=已探索, 2=当前可见

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        gridW = Mathf.CeilToInt(worldSize.x / fogCellSize);
        gridH = Mathf.CeilToInt(worldSize.y / fogCellSize);
        exploredGrid = new byte[gridW * gridH];
    }

    void Update()
    {
        // 降级：所有 2 → 1（上一帧可见的本帧降级为已探索）
        for (int i = 0; i < exploredGrid.Length; i++)
        {
            if (exploredGrid[i] == 2) exploredGrid[i] = 1;
        }

        // 标记玩家单位视野范围
        foreach (var unit in UnitRegistry.Instance.GetPlayerUnits())
        {
            if (unit == null || unit.isDead) continue;
            MarkVisible(unit.transform.position, unit.def.sightRange);
        }

        // 雷达站全图探索
        bool hasRadar = false;
        foreach (var u in UnitRegistry.Instance.GetPlayerUnits())
        {
            if (u == null || u.isDead) continue;
            if (u.def.unitId == "radar") { hasRadar = true; break; }
        }
        if (hasRadar)
        {
            for (int i = 0; i < exploredGrid.Length; i++)
                exploredGrid[i] = 2;
        }
    }

    public void MarkVisible(Vector3 worldPos, float sightRange)
    {
        int cx = Mathf.FloorToInt(worldPos.x / fogCellSize);
        int cy = Mathf.FloorToInt(worldPos.y / fogCellSize);
        int r = Mathf.CeilToInt(sightRange / fogCellSize);

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int tx = cx + dx, ty = cy + dy;
                if (tx < 0 || tx >= gridW || ty < 0 || ty >= gridH) continue;

                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist <= r)
                {
                    exploredGrid[ty * gridW + tx] = 2;
                }
            }
        }
    }

    /// <summary>
    /// 某位置是否当前可见
    /// </summary>
    public bool IsVisible(Vector3 worldPos)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(worldPos.x / fogCellSize), 0, gridW - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(worldPos.y / fogCellSize), 0, gridH - 1);
        return exploredGrid[y * gridW + x] == 2;
    }

    /// <summary>
    /// 某位置是否已探索过（但不一定当前可见）
    /// </summary>
    public bool IsExplored(Vector3 worldPos)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(worldPos.x / fogCellSize), 0, gridW - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(worldPos.y / fogCellSize), 0, gridH - 1);
        return exploredGrid[y * gridW + x] > 0;
    }

    /// <summary>
    /// 获取迷雾状态（0=未探索, 1=已探索, 2=可见）
    /// </summary>
    public byte GetFogState(int gridX, int gridY)
    {
        if (gridX < 0 || gridX >= gridW || gridY < 0 || gridY >= gridH) return 0;
        return exploredGrid[gridY * gridW + gridX];
    }

    public int GridWidth => gridW;
    public int GridHeight => gridH;
    public float CellSize => fogCellSize;
}
