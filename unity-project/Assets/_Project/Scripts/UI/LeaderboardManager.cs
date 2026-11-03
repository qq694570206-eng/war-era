// ================================================================
// LeaderboardManager.cs — 本地排行榜
// 速通/连胜/积分，PlayerPrefs 存储
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class LeaderboardManager : MonoBehaviour
{
    public static LeaderboardManager Instance { get; private set; }

    [Header("Config")]
    public int maxEntries = 10;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 提交速通成绩
    /// </summary>
    public void SubmitSpeedrun(string mapId, float time)
    {
        string key = $"leaderboard_speed_{mapId}";
        var entries = LoadEntries(key);
        entries.Add(new LeaderboardEntry { score = time, displayText = $"{FormatTime(time)}", date = System.DateTime.Now.ToString("MM-dd") });
        entries.Sort((a, b) => a.score.CompareTo(b.score)); // 时间升序（越快越好）
        if (entries.Count > maxEntries) entries.RemoveRange(maxEntries, entries.Count - maxEntries);
        SaveEntries(key, entries);
    }

    /// <summary>
    /// 提交连胜
    /// </summary>
    public void SubmitStreak(int streak)
    {
        string key = "leaderboard_streak";
        var entries = LoadEntries(key);
        entries.Add(new LeaderboardEntry { score = streak, displayText = $"{streak} 连胜", date = System.DateTime.Now.ToString("MM-dd") });
        entries.Sort((a, b) => b.score.CompareTo(a.score)); // 数量降序
        if (entries.Count > maxEntries) entries.RemoveRange(maxEntries, entries.Count - maxEntries);
        SaveEntries(key, entries);
    }

    /// <summary>
    /// 获取排行榜
    /// </summary>
    public List<LeaderboardEntry> GetSpeedrunBoard(string mapId)
    {
        return LoadEntries($"leaderboard_speed_{mapId}");
    }

    public List<LeaderboardEntry> GetStreakBoard()
    {
        return LoadEntries("leaderboard_streak");
    }

    public List<LeaderboardEntry> GetScoreBoard()
    {
        int score = AchievementManager.Instance?.GetTotalScore() ?? 0;
        return new List<LeaderboardEntry> { new LeaderboardEntry { score = score, displayText = $"{score} 分", date = System.DateTime.Now.ToString("MM-dd") } };
    }

    List<LeaderboardEntry> LoadEntries(string key)
    {
        var entries = new List<LeaderboardEntry>();
        int count = PlayerPrefs.GetInt($"{key}_count", 0);
        for (int i = 0; i < count; i++)
        {
            entries.Add(new LeaderboardEntry
            {
                score = PlayerPrefs.GetFloat($"{key}_{i}_score", 0),
                displayText = PlayerPrefs.GetString($"{key}_{i}_text", ""),
                date = PlayerPrefs.GetString($"{key}_{i}_date", "")
            });
        }
        return entries;
    }

    void SaveEntries(string key, List<LeaderboardEntry> entries)
    {
        PlayerPrefs.SetInt($"{key}_count", entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            PlayerPrefs.SetFloat($"{key}_{i}_score", entries[i].score);
            PlayerPrefs.SetString($"{key}_{i}_text", entries[i].displayText);
            PlayerPrefs.SetString($"{key}_{i}_date", entries[i].date);
        }
        PlayerPrefs.Save();
    }

    string FormatTime(float seconds)
    {
        int m = Mathf.FloorToInt(seconds / 60);
        int s = Mathf.FloorToInt(seconds % 60);
        return $"{m}:{s:00}";
    }
}

[System.Serializable]
public class LeaderboardEntry
{
    public float score;
    public string displayText;
    public string date;
}
