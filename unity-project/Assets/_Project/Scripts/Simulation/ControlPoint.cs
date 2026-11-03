// ================================================================
// ControlPoint.cs — 控制点（可占领的战略点）
// ================================================================
using UnityEngine;

public class ControlPoint : MonoBehaviour
{
    [Header("Config")]
    public ControlPointType type = ControlPointType.Economy;
    public float radius = 40f;
    public float captureTime = 10f;
    public float captureProgress = 0f;
    public int controllingPlayer = -1; // -1=中立, 0=玩家, 1=敌方

    [Header("Visual")]
    public SpriteRenderer flagRenderer;
    public Color neutralColor = new Color(0.5f, 0.5f, 0.5f);
    public Color playerColor = new Color(0.25f, 0.65f, 0.84f);
    public Color enemyColor = new Color(0.91f, 0.30f, 0.24f);

    private float checkTimer = 0f;

    void Update()
    {
        float dt = GameClock.Instance?.DeltaTime ?? Time.deltaTime;
        checkTimer += dt;

        // 每 0.5 秒检查一次区域内单位
        if (checkTimer >= 0.5f)
        {
            checkTimer = 0f;
            CheckCapture();
        }

        // 已占领的给加成
        if (controllingPlayer >= 0)
        {
            ApplyBonus(dt);
        }

        UpdateVisual();
    }

    void CheckCapture()
    {
        int playerUnits = 0;
        int enemyUnits = 0;

        foreach (var unit in UnitRegistry.Instance.GetAllUnits())
        {
            if (unit == null || unit.isDead || unit.def.isBuilding) continue;
            float dist = Vector3.Distance(unit.transform.position, transform.position);
            if (dist <= radius)
            {
                if (unit.isEnemy) enemyUnits++;
                else playerUnits++;
            }
        }

        // 只有单方单位在区域内才推进占领
        if (playerUnits > 0 && enemyUnits == 0)
        {
            if (controllingPlayer != 0)
            {
                captureProgress += 0.5f / captureTime;
                if (captureProgress >= 1f)
                {
                    controllingPlayer = 0;
                    captureProgress = 1f;
                    UIManager.Instance?.ShowToast("控制点已占领！");
                }
            }
        }
        else if (enemyUnits > 0 && playerUnits == 0)
        {
            if (controllingPlayer != 1)
            {
                captureProgress -= 0.5f / captureTime;
                if (captureProgress <= -1f)
                {
                    controllingPlayer = 1;
                    captureProgress = -1f;
                    UIManager.Instance?.ShowAlert("敌方占领了控制点！");
                }
            }
        }
        // 双方都在区域内 = 僵持，不推进
    }

    void ApplyBonus(float dt)
    {
        if (controllingPlayer == 0) // 玩家
        {
            switch (type)
            {
                case ControlPointType.Economy:
                    EconomySystem.Instance.ore += Mathf.RoundToInt(20f * dt);
                    break;
                case ControlPointType.Tech:
                    EconomySystem.Instance.techPoints += Mathf.RoundToInt(1f * dt);
                    break;
                case ControlPointType.Vision:
                    // 周围全图可见
                    FogOfWarSystem.Instance?.MarkVisible(transform.position, 300f);
                    break;
                case ControlPointType.Highland:
                    FogOfWarSystem.Instance?.MarkVisible(transform.position, 200f);
                    break;
            }
        }
    }

    void UpdateVisual()
    {
        if (flagRenderer == null) return;

        Color targetColor = neutralColor;
        if (controllingPlayer == 0) targetColor = playerColor;
        else if (controllingPlayer == 1) targetColor = enemyColor;

        flagRenderer.color = Color.Lerp(flagRenderer.color, targetColor, 0.1f);
    }
}
