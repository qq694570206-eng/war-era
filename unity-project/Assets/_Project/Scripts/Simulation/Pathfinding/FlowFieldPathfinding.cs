// ================================================================
// FlowFieldPathfinding.cs — 流场寻路系统
// 大部队同目标移动的高性能寻路方案
// 目标点生成流场 → 单位按流场方向走，不需要各自寻路
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class FlowFieldPathfinding : MonoBehaviour
{
    public static FlowFieldPathfinding Instance { get; private set; }

    [Header("Grid Settings")]
    public float cellSize = 2f;
    public Vector2 worldSize = new Vector2(2400, 1600);
    public LayerMask obstacleLayer = ~0;

    [Header("Debug")]
    public bool showDebug = false;

    private int gridWidth, gridHeight;
    private Cell[,] grid;
    private Vector2Int currentTargetCell;
    private bool hasFlowField = false;

    struct Cell
    {
        public float cost;        // 到目标的最佳代价（越低越优）
        public Vector2 direction; // 最佳移动方向（归一化）
        public bool isBlocked;    // 是否被障碍阻挡
        public bool hasDirection; // 是否已计算方向
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        gridWidth = Mathf.CeilToInt(worldSize.x / cellSize);
        gridHeight = Mathf.CeilToInt(worldSize.y / cellSize);
        grid = new Cell[gridWidth, gridHeight];
    }

    /// <summary>
    /// 为目标点生成流场（全图单位共用一个流场）
    /// </summary>
    public void GenerateFlowField(Vector3 targetWorldPos)
    {
        Vector2Int targetCell = WorldToGrid(targetWorldPos);
        if (!IsValidCell(targetCell.x, targetCell.y)) return;

        currentTargetCell = targetCell;

        // 重置网格
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                grid[x, y].cost = float.MaxValue;
                grid[x, y].hasDirection = false;
                grid[x, y].isBlocked = IsCellBlocked(x, y);
            }
        }

        // 目标点代价为 0
        grid[targetCell.x, targetCell.y].cost = 0;

        // BFS/Dijkstra 扩散代价
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(targetCell);

        int maxIterations = gridWidth * gridHeight;
        int iterations = 0;

        while (queue.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            Vector2Int cell = queue.Dequeue();
            float currentCost = grid[cell.x, cell.y].cost;

            // 检查 8 方向邻居
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = cell.x + dx;
                    int ny = cell.y + dy;
                    if (!IsValidCell(nx, ny)) continue;
                    if (grid[nx, ny].isBlocked) continue;

                    // 对角线移动时检查两侧是否可通行（防止穿墙角）
                    if (dx != 0 && dy != 0)
                    {
                        if (grid[cell.x + dx, cell.y].isBlocked || grid[cell.x, cell.y + dy].isBlocked)
                            continue;
                    }

                    float moveCost = (dx != 0 && dy != 0) ? 1.414f : 1f;
                    float newCost = currentCost + moveCost;

                    if (newCost < grid[nx, ny].cost)
                    {
                        grid[nx, ny].cost = newCost;
                        queue.Enqueue(new Vector2Int(nx, ny));
                    }
                }
            }
        }

        // 计算每个格子的最佳方向（指向代价最低的邻居）
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (grid[x, y].isBlocked || grid[x, y].cost == 0) continue;

                float bestCost = grid[x, y].cost;
                Vector2 bestDir = Vector2.zero;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (!IsValidCell(nx, ny) || grid[nx, ny].isBlocked) continue;

                        if (grid[nx, ny].cost < bestCost)
                        {
                            bestCost = grid[nx, ny].cost;
                            bestDir = new Vector2(nx - x, ny - y).normalized;
                        }
                    }
                }

                if (bestDir != Vector2.zero)
                {
                    grid[x, y].direction = bestDir;
                    grid[x, y].hasDirection = true;
                }
            }
        }

        hasFlowField = true;
    }

    /// <summary>
    /// 获取某位置应走的方向（单位调用此方法，不需要自己寻路）
    /// </summary>
    public Vector2 GetDirection(Vector3 worldPos)
    {
        if (!hasFlowField) return Vector2.zero;

        Vector2Int cell = WorldToGrid(worldPos);
        if (!IsValidCell(cell.x, cell.y)) return Vector2.zero;
        if (!grid[cell.x, cell.y].hasDirection) return Vector2.zero;

        return grid[cell.x, cell.y].direction;
    }

    /// <summary>
    /// 检查位置是否可达（非阻挡）
    /// </summary>
    public bool IsReachable(Vector3 worldPos)
    {
        Vector2Int cell = WorldToGrid(worldPos);
        if (!IsValidCell(cell.x, cell.y)) return false;
        return !grid[cell.x, cell.y].isBlocked;
    }

    Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(worldPos.x / cellSize), 0, gridWidth - 1),
            Mathf.Clamp(Mathf.FloorToInt(worldPos.y / cellSize), 0, gridHeight - 1)
        );
    }

    Vector3 GridToWorld(int x, int y)
    {
        return new Vector3(
            (x + 0.5f) * cellSize,
            (y + 0.5f) * cellSize,
            0
        );
    }

    bool IsValidCell(int x, int y)
    {
        return x >= 0 && x < gridWidth && y >= 0 && y < gridHeight;
    }

    bool IsCellBlocked(int x, int y)
    {
        Vector3 worldPos = GridToWorld(x, y);
        Collider2D hit = Physics2D.OverlapBox(worldPos, Vector2.one * cellSize * 0.9f, 0, obstacleLayer);
        return hit != null;
    }

    // 调试可视化
    void OnDrawGizmos()
    {
        if (!showDebug || grid == null) return;

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector3 pos = GridToWorld(x, y);

                if (grid[x, y].isBlocked)
                {
                    Gizmos.color = new Color(1, 0, 0, 0.3f);
                    Gizmos.DrawCube(pos, Vector3.one * cellSize * 0.9f);
                }
                else if (grid[x, y].hasDirection)
                {
                    Gizmos.color = new Color(0, 1, 0, 0.2f);
                    Gizmos.DrawLine(pos, pos + (Vector3)grid[x, y].direction * cellSize * 0.5f);
                }
            }
        }
    }
}
