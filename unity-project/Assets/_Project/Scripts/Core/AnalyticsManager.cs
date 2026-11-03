// ================================================================
// AnalyticsManager.cs — 数据埋点
// 关键指标追踪：DAU/留存/付费率/对局时长/胜率
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class AnalyticsManager : MonoBehaviour
{
    public static AnalyticsManager Instance { get; private set; }

    [Header("Config")]
    public bool isEnabled = true;

    [Header("Session")]
    public float sessionStartTime;
    public int gamesPlayed = 0;
    public int gamesWon = 0;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        sessionStartTime = Time.time;
        TrackEvent("session_start");
    }

    void OnApplicationQuit()
    {
        TrackEvent("session_end", new Dictionary<string, object>
        {
            { "session_duration", Time.time - sessionStartTime }
        });
    }

    /// <summary>
    /// 追踪事件
    /// </summary>
    public void TrackEvent(string eventName, Dictionary<string, object> parameters = null)
    {
        if (!isEnabled) return;

        // TODO: 接入 Unity Analytics 或 Firebase Analytics
        // 当前为本地日志
        string paramStr = "";
        if (parameters != null)
        {
            paramStr = " {";
            foreach (var kv in parameters)
                paramStr += $"{kv.Key}={kv.Value}, ";
            paramStr = paramStr.TrimEnd(',', ' ') + "}";
        }

        Debug.Log($"[Analytics] {eventName}{paramStr}");
    }

    // === 游戏事件快捷方法 ===

    public void OnGameStart(string theme, string faction, string difficulty, string map)
    {
        TrackEvent("game_start", new Dictionary<string, object>
        {
            { "theme", theme },
            { "faction", faction },
            { "difficulty", difficulty },
            { "map", map }
        });
    }

    public void OnGameEnd(bool win, float duration, int unitsLost)
    {
        gamesPlayed++;
        if (win) gamesWon++;

        TrackEvent("game_end", new Dictionary<string, object>
        {
            { "result", win ? "win" : "lose" },
            { "duration", Mathf.Round(duration) },
            { "units_lost", unitsLost }
        });
    }

    public void OnBuildPlaced(string buildingType, int cost)
    {
        TrackEvent("build_place", new Dictionary<string, object>
        {
            { "building_type", buildingType },
            { "cost", cost }
        });
    }

    public void OnUnitProduced(string unitType, int cost)
    {
        TrackEvent("unit_produce", new Dictionary<string, object>
        {
            { "unit_type", unitType },
            { "cost", cost }
        });
    }

    public void OnTechUpgrade(string upgradeId)
    {
        TrackEvent("tech_upgrade", new Dictionary<string, object>
        {
            { "upgrade_id", upgradeId }
        });
    }

    public void OnSuperWeaponFired()
    {
        TrackEvent("superweapon_fire");
    }

    public void OnSquadAssigned(int slot)
    {
        TrackEvent("squad_assign", new Dictionary<string, object>
        {
            { "slot", slot }
        });
    }

    public void OnAdWatched(string adType, string reward)
    {
        TrackEvent("ad_watch", new Dictionary<string, object>
        {
            { "ad_type", adType },
            { "reward", reward }
        });
    }

    public void OnIAPPurchased(string productId, float price)
    {
        TrackEvent("iap_purchase", new Dictionary<string, object>
        {
            { "product_id", productId },
            { "price", price }
        });
    }

    public void OnLevelComplete(int level, int stars, float time)
    {
        TrackEvent("level_complete", new Dictionary<string, object>
        {
            { "level", level },
            { "stars", stars },
            { "time", Mathf.Round(time) }
        });
    }

    public void OnTutorialStep(int step)
    {
        TrackEvent("tutorial_step", new Dictionary<string, object>
        {
            { "step", step }
        });
    }

    /// <summary>
    /// 获取胜率
    /// </summary>
    public float GetWinRate()
    {
        if (gamesPlayed == 0) return 0f;
        return (float)gamesWon / gamesPlayed;
    }

    /// <summary>
    /// 获取会话时长
    /// </summary>
    public float GetSessionDuration()
    {
        return Time.time - sessionStartTime;
    }
}
