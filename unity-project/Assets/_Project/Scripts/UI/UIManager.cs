// ================================================================
// UIManager.cs — UI 管理器
// 资源条/建造面板/单位信息卡/编队栏/提示
// ================================================================
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Resource Bar")]
    public Text oreText;
    public Text powerText;
    public Text techText;

    [Header("Unit Info Panel")]
    public GameObject unitInfoPanel;
    public Text unitNameText;
    public Text unitCountText;
    public Image hpBarFill;
    public Text hpText;
    public Text dmgText;
    public Text rangeText;

    [Header("Toast")]
    public GameObject toastPanel;
    public Text toastText;
    public float toastDuration = 2f;

    [Header("Alert")]
    public GameObject alertPanel;
    public Text alertText;
    public float alertDuration = 5f;

    [Header("Game Over")]
    public GameObject gameOverPanel;
    public Text gameOverText;

    private float toastTimer = 0f;
    private float alertTimer = 0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (toastTimer > 0)
        {
            toastTimer -= Time.deltaTime;
            if (toastTimer <= 0) toastPanel.SetActive(false);
        }
        if (alertTimer > 0)
        {
            alertTimer -= Time.deltaTime;
            if (alertTimer <= 0) alertPanel.SetActive(false);
        }

        UpdateResourceBar();
    }

    void UpdateResourceBar()
    {
        if (EconomySystem.Instance == null) return;

        if (oreText != null)
        {
            oreText.text = Mathf.FloorToInt(EconomySystem.Instance.ore).ToString();
        }
        if (powerText != null)
        {
            int available = EconomySystem.Instance.GetPowerAvailable();
            powerText.text = $"{EconomySystem.Instance.powerUsed}/{EconomySystem.Instance.powerSupply}";
            powerText.color = available >= 0 ? new Color(0.25f, 0.65f, 0.84f) : new Color(0.91f, 0.30f, 0.24f);
        }
        if (techText != null)
        {
            techText.text = EconomySystem.Instance.techPoints.ToString();
        }
    }

    public void ShowUnitInfo(UnitEntity unit)
    {
        if (unitInfoPanel == null) return;
        unitInfoPanel.SetActive(true);

        if (unitNameText != null)
            unitNameText.text = unit.def.displayName + (unit.isEnemy ? " (敌)" : "");

        int count = TouchInputSystem.Instance?.selectedUnits.Count ?? 1;
        if (unitCountText != null)
            unitCountText.text = count > 1 ? $"×{count}" : "";

        if (hpBarFill != null)
        {
            float ratio = Mathf.Max(0, unit.currentHp / unit.maxHp);
            hpBarFill.fillAmount = ratio;
            hpBarFill.color = unit.isEnemy ? new Color(0.91f, 0.30f, 0.24f) :
                ratio > 0.5f ? new Color(0.25f, 0.65f, 0.84f) :
                ratio > 0.25f ? new Color(0.95f, 0.76f, 0.31f) : new Color(0.75f, 0.22f, 0.17f);
        }

        if (hpText != null)
            hpText.text = $"{Mathf.CeilToInt(Mathf.Max(0, unit.currentHp))}/{unit.maxHp}";
        if (dmgText != null)
            dmgText.text = (unit.def.damage > 0 ? unit.def.damage.ToString() : "—");
        if (rangeText != null)
            rangeText.text = (unit.def.range > 0 ? unit.def.range.ToString() : "—");
    }

    public void HideUnitInfo()
    {
        if (unitInfoPanel != null) unitInfoPanel.SetActive(false);
    }

    public void ShowToast(string message)
    {
        if (toastText != null) toastText.text = message;
        if (toastPanel != null) toastPanel.SetActive(true);
        toastTimer = toastDuration;
    }

    public void ShowAlert(string message)
    {
        if (alertText != null) alertText.text = message;
        if (alertPanel != null) alertPanel.SetActive(true);
        alertTimer = alertDuration;
    }

    public void ShowGameOver(bool victory)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverText != null)
        {
            gameOverText.text = victory ? "🏆 胜利！" : "💀 基地被摧毁";
            gameOverText.color = victory ? new Color(0.95f, 0.76f, 0.31f) : new Color(0.91f, 0.30f, 0.24f);
        }
    }
}
