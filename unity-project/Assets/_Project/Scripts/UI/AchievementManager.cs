// ================================================================
// AchievementManager.cs — 成就系统
// 16 个成就 + 积分
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class AchievementManager : MonoBehaviour
{
    public static AchievementManager Instance { get; private set; }

    [Header("Achievements")]
    public AchievementData[] achievements;

    [Header("State")]
    public HashSet<string> unlockedAchievements = new HashSet<string>();
    public int totalScore = 0;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LoadProgress();
    }

    /// <summary>
    /// 解锁成就
    /// </summary>
    public void Unlock(string achievementId)
    {
        if (unlockedAchievements.Contains(achievementId)) return;

        AchievementData data = FindAchievement(achievementId);
        if (data == null) return;

        unlockedAchievements.Add(achievementId);
        totalScore += data.score;
        SaveProgress();

        UIManager.Instance?.ShowToast($"🏆 成就解锁: {data.displayName} (+{data.score})");
        Debug.Log($"[Achievement] Unlocked: {achievementId}");
    }

    /// <summary>
    /// 检查是否已解锁
    /// </summary>
    public bool IsUnlocked(string achievementId)
    {
        return unlockedAchievements.Contains(achievementId);
    }

    /// <summary>
    /// 获取总积分
    /// </summary>
    public int GetTotalScore() => totalScore;

    /// <summary>
    /// 获取解锁进度
    /// </summary>
    public float GetProgress()
    {
        if (achievements.Length == 0) return 0f;
        return (float)unlockedAchievements.Count / achievements.Length;
    }

    // === 游戏事件触发 ===

    public void OnGameWin()
    {
        Unlock("first_win");
        CheckStreak();
    }

    public void OnGameComplete()
    {
        Unlock("first_battle");
    }

    int winStreak = 0;
    void CheckStreak()
    {
        winStreak++;
        if (winStreak >= 3) Unlock("streak_3");
        if (winStreak >= 10) Unlock("streak_10");
        PlayerPrefs.SetInt("win_streak", winStreak);
    }

    public void OnGameLose()
    {
        winStreak = 0;
        PlayerPrefs.SetInt("win_streak", 0);
    }

    public void OnSuperWeaponFired()
    {
        Unlock("nuke_used");
    }

    public void OnCampaignComplete()
    {
        Unlock("campaign_complete");
    }

    AchievementData FindAchievement(string id)
    {
        if (achievements == null) return null;
        foreach (var a in achievements)
        {
            if (a != null && a.achievementId == id) return a;
        }
        return null;
    }

    void LoadProgress()
    {
        int count = PlayerPrefs.GetInt("achievement_count", 0);
        for (int i = 0; i < count; i++)
        {
            string id = PlayerPrefs.GetString($"achievement_{i}", "");
            if (!string.IsNullOrEmpty(id)) unlockedAchievements.Add(id);
        }
        totalScore = PlayerPrefs.GetInt("achievement_score", 0);
        winStreak = PlayerPrefs.GetInt("win_streak", 0);
    }

    void SaveProgress()
    {
        int i = 0;
        foreach (var id in unlockedAchievements)
        {
            PlayerPrefs.SetString($"achievement_{i}", id);
            i++;
        }
        PlayerPrefs.SetInt("achievement_count", i);
        PlayerPrefs.SetInt("achievement_score", totalScore);
        PlayerPrefs.Save();
    }
}

[System.Serializable]
public class AchievementData
{
    public string achievementId;
    public string displayName;
    [TextArea] public string description;
    public int score;
}
