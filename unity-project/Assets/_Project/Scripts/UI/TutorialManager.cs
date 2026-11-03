// ================================================================
// TutorialManager.cs — 教程引导系统
// 首次玩家 14 步引导
// ================================================================
using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    [Header("State")]
    public int currentStep = 0;
    public bool isActive = false;
    public bool tutorialDone = false;

    [Header("Steps")]
    public TutorialStep[] steps;

    [Header("References")]
    public GameObject tutorialBubble; // 引导气泡 UI
    public UnityEngine.UI.Text tutorialText;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        tutorialDone = PlayerPrefs.GetInt("tutorial_done", 0) == 1;
    }

    void Start()
    {
        if (!tutorialDone && steps.Length > 0)
        {
            isActive = true;
            currentStep = 0;
            ShowStep();
        }
    }

    void Update()
    {
        if (!isActive) return;

        // 检查当前步骤的完成条件
        if (CheckStepComplete(currentStep))
        {
            NextStep();
        }
    }

    void ShowStep()
    {
        if (currentStep >= steps.Length)
        {
            CompleteTutorial();
            return;
        }

        TutorialStep step = steps[currentStep];

        if (tutorialBubble != null) tutorialBubble.SetActive(true);
        if (tutorialText != null) tutorialText.text = step.instruction;

        Debug.Log($"[Tutorial] Step {currentStep + 1}: {step.instruction}");
    }

    void NextStep()
    {
        currentStep++;
        ShowStep();
    }

    bool CheckStepComplete(int stepIndex)
    {
        if (stepIndex >= steps.Length) return true;

        TutorialStep step = steps[stepIndex];

        switch (step.completeCondition)
        {
            case TutorialCondition.SelectHarvester:
                return TouchInputSystem.Instance?.selectedUnits.Exists(u => u != null && u.isHarvester) ?? false;

            case TutorialCondition.OreCollected:
                return EconomySystem.Instance?.ore > 300;

            case TutorialCondition.BuildingPlaced:
                return UnitRegistry.Instance?.GetPlayerUnits().Exists(u => u != null && u.def.isBuilding && u.def.unitId == "powerplant") ?? false;

            case TutorialCondition.BarracksBuilt:
                return UnitRegistry.Instance?.GetPlayerUnits().Exists(u => u != null && u.def.unitId == "barracks") ?? false;

            case TutorialCondition.UnitProduced:
                return UnitRegistry.Instance?.GetPlayerUnits().Exists(u => u != null && !u.def.isBuilding && !u.isHarvester) ?? false;

            case TutorialCondition.UnitsSelected:
                return (TouchInputSystem.Instance?.selectedUnits.Count ?? 0) >= 2;

            case TutorialCondition.MoveCommand:
                // 检查是否有单位在移动
                return UnitRegistry.Instance?.GetPlayerUnits().Exists(u => u != null && u.hasMoveTarget) ?? false;

            case TutorialCondition.EnemyFound:
                return UnitRegistry.Instance?.GetEnemyUnits().Count > 0;

            case TutorialCondition.WarFactoryBuilt:
                return UnitRegistry.Instance?.GetPlayerUnits().Exists(u => u != null && u.def.unitId == "warfactory") ?? false;

            case TutorialCondition.SquadAssigned:
                return TouchInputSystem.Instance?.squads[0]?.Count > 0 ?? false;

            case TutorialCondition.RadarBuilt:
                return UnitRegistry.Instance?.GetPlayerUnits().Exists(u => u != null && u.def.unitId == "radar") ?? false;

            case TutorialCondition.ManualNext:
                return false; // 需要玩家点击"下一步"

            default:
                return false;
        }
    }

    void CompleteTutorial()
    {
        isActive = false;
        tutorialDone = true;
        PlayerPrefs.SetInt("tutorial_done", 1);
        PlayerPrefs.Save();

        if (tutorialBubble != null) tutorialBubble.SetActive(false);
        UIManager.Instance?.ShowToast("教程完成！开始自由对战");

        Debug.Log("[Tutorial] Completed.");
    }

    /// <summary>
    /// 跳过教程
    /// </summary>
    public void SkipTutorial()
    {
        CompleteTutorial();
    }
}

[System.Serializable]
public struct TutorialStep
{
    public string instruction;
    public TutorialCondition completeCondition;
}

public enum TutorialCondition
{
    SelectHarvester,     // 选中采矿车
    OreCollected,        // 采集到矿石
    BuildingPlaced,      // 放置了建筑
    BarracksBuilt,       // 兵营建成
    UnitProduced,        // 生产了单位
    UnitsSelected,       // 框选了多个单位
    MoveCommand,         // 下达了移动命令
    EnemyFound,          // 发现敌人
    WarFactoryBuilt,     // 车厂建成
    SquadAssigned,       // 编入编队
    RadarBuilt,          // 雷达站建成
    ManualNext           // 手动下一步
}
