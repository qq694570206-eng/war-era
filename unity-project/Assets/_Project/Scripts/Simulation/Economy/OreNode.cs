// ================================================================
// OreNode.cs — 矿石节点（可采集的资源点）
// ================================================================
using UnityEngine;

public class OreNode : MonoBehaviour
{
    [Header("Settings")]
    public float radius = 25f;
    public int maxAmount = 9999;
    public int currentAmount;
    public bool isInfinite = true;

    [Header("Visual")]
    public Sprite sprite;
    public Color oreColor = new Color(0.77f, 0.66f, 0.42f); // #C4A96A

    void Awake()
    {
        currentAmount = maxAmount;
    }

    /// <summary>
    /// 采矿车请求采集
    /// </summary>
    public int Harvest(int requestedAmount)
    {
        if (requestedAmount <= 0) return 0;
        if (!isInfinite && currentAmount <= 0) return 0;

        int harvested = isInfinite ? requestedAmount : Mathf.Min(requestedAmount, currentAmount);
        if (!isInfinite) currentAmount -= harvested;
        return harvested;
    }

    /// <summary>
    /// 是否已枯竭
    /// </summary>
    public bool IsDepleted() => !isInfinite && currentAmount <= 0;
}
