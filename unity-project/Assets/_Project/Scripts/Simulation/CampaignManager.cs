// ================================================================
// CampaignManager.cs — 战役流程管理
// 关卡解锁/三星评价/触发事件/剧情播放
// ================================================================
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CampaignLevel_", menuName = "WarEra/Campaign Level")]
public class CampaignLevel : ScriptableObject
{
    [Header("Identity")]
    public string levelId;
    public string displayName;
    public int chapter;
    public string description;

    [Header("Map")]
    public string mapId;
    public string themeId = "ww2";
    public string factionId = "allies";

    [Header("Star Conditions")]
    public float timeLimit = 300f;      // 速通时间
    public int maxLosses = 5;            // 最多损失单位
    public int maxCost = 1500;           // 最大消耗

    [Header("Dialogues")]
    [TextArea] public string startDialogue;
    [TextArea] public string endDialogue;

    [Header("Triggers")]
    public TriggerData[] triggers;
}

[System.Serializable]
public struct TriggerData
{
    public string triggerType;   // "time", "unit_count", "building_destroyed"
    public float triggerValue;   // 时间/数量
    public string action;        // "spawn_enemies", "show_dialogue", "win"
    public string actionParam;   // 参数
}

public class CampaignManager : MonoBehaviour
{
    public static CampaignManager Instance { get; private set; }

    [Header("Levels")]
    public CampaignLevel[] levels;

    [Header("Progress")]
    public int currentLevel = 0;
    public Dictionary<string, int> levelStars = new Dictionary<string, int>(); // levelId → 星数

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LoadProgress();
    }

    /// <summary>
    /// 开始关卡
    /// </summary>
    public void StartLevel(int levelIndex)
    {
        if (levelIndex < 0 || levelIndex >= levels.Length) return;

        currentLevel = levelIndex;
        CampaignLevel level = levels[levelIndex];

        // 播放开场剧情
        if (!string.IsNullOrEmpty(level.startDialogue))
        {
            UIManager.Instance?.ShowAlert(level.startDialogue);
        }

        // 加载地图
        if (MapLoader.Instance != null)
            MapLoader.Instance.LoadMap(level.mapId);

        Debug.Log($"[Campaign] Level {levelIndex + 1} started: {level.displayName}");
    }

    /// <summary>
    /// 关卡完成
    /// </summary>
    public void CompleteLevel(bool victory)
    {
        if (!victory)
        {
            UIManager.Instance?.ShowGameOver(false);
            return;
        }

        CampaignLevel level = levels[currentLevel];

        // 计算星级
        int stars = CalculateStars(level);

        // 保存进度
        string levelId = level.levelId;
        if (!levelStars.ContainsKey(levelId) || levelStars[levelId] < stars)
        {
            levelStars[levelId] = stars;
            SaveProgress();
        }

        // 播放结束剧情
        if (!string.IsNullOrEmpty(level.endDialogue))
        {
            UIManager.Instance?.ShowAlert(level.endDialogue);
        }

        UIManager.Instance?.ShowGameOver(true);
        Debug.Log($"[Campaign] Level {currentLevel + 1} completed with {stars} stars.");
    }

    /// <summary>
    /// 计算星级
    /// </summary>
    int CalculateStars(CampaignLevel level)
    {
        int stars = 1; // 基础一星(完成)

        float gameTime = GameClock.Instance?.GameTime ?? 0f;
        if (gameTime <= level.timeLimit) stars++; // 速通

        // TODO: 统计损失和消耗
        // if (losses <= level.maxLosses) stars++;
        // if (totalCost <= level.maxCost) stars++;

        return Mathf.Clamp(stars, 1, 3);
    }

    /// <summary>
    /// 检查关卡是否解锁
    /// </summary>
    public bool IsLevelUnlocked(int levelIndex)
    {
        if (levelIndex == 0) return true;
        string prevLevelId = levels[levelIndex - 1].levelId;
        return levelStars.ContainsKey(prevLevelId);
    }

    /// <summary>
    /// 获取关卡星数
    /// </summary>
    public int GetLevelStars(string levelId)
    {
        return levelStars.ContainsKey(levelId) ? levelStars[levelId] : 0;
    }

    void LoadProgress()
    {
        for (int i = 0; i < levels.Length; i++)
        {
            string key = $"campaign_star_{levels[i].levelId}";
            if (PlayerPrefs.HasKey(key))
            {
                levelStars[levels[i].levelId] = PlayerPrefs.GetInt(key);
            }
        }
    }

    void SaveProgress()
    {
        foreach (var kv in levelStars)
        {
            PlayerPrefs.SetInt($"campaign_star_{kv.Key}", kv.Value);
        }
        PlayerPrefs.Save();
    }
}
