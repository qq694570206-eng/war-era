// ================================================================
// SaveSystem.cs — 存档系统
// 遭遇战存档/战役进度/设置持久化
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    [Header("Config")]
    public int maxSaveSlots = 1; // 免费1个，内购可扩到5个
    public float autoSaveInterval = 30f;

    private float autoSaveTimer = 0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        // 自动存档
        autoSaveTimer += Time.deltaTime;
        if (autoSaveTimer >= autoSaveInterval)
        {
            autoSaveTimer = 0f;
            AutoSave();
        }
    }

    /// <summary>
    /// 保存遭遇战
    /// </summary>
    public void SaveSkirmish(int slot, string saveName)
    {
        if (slot < 0 || slot >= maxSaveSlots) return;

        var data = CollectSaveData();
        data["saveName"] = saveName;
        data["saveTime"] = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        string json = JsonUtility.ToJson(data, true);
        PlayerPrefs.SetString($"skirmish_save_{slot}", json);
        PlayerPrefs.Save();

        UIManager.Instance?.ShowToast($"已存档: {saveName}");
        Debug.Log($"[SaveSystem] Saved to slot {slot}");
    }

    /// <summary>
    /// 加载遭遇战
    /// </summary>
    public Dictionary<string, object> LoadSkirmish(int slot)
    {
        if (slot < 0 || slot >= maxSaveSlots) return null;

        string json = PlayerPrefs.GetString($"skirmish_save_{slot}", "");
        if (string.IsNullOrEmpty(json)) return null;

        // TODO: 反序列化并恢复游戏状态
        Debug.Log($"[SaveSystem] Loaded from slot {slot}");
        return null; // 暂时返回 null，后续实现恢复逻辑
    }

    /// <summary>
    /// 检查存档是否存在
    /// </summary>
    public bool HasSave(int slot)
    {
        return PlayerPrefs.HasKey($"skirmish_save_{slot}");
    }

    /// <summary>
    /// 删除存档
    /// </summary>
    public void DeleteSave(int slot)
    {
        PlayerPrefs.DeleteKey($"skirmish_save_{slot}");
        PlayerPrefs.Save();
    }

    void AutoSave()
    {
        if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;
        SaveSkirmish(0, "自动存档");
    }

    Dictionary<string, object> CollectSaveData()
    {
        var data = new Dictionary<string, object>();

        // 经济
        if (EconomySystem.Instance != null)
        {
            data["ore"] = EconomySystem.Instance.ore;
            data["powerSupply"] = EconomySystem.Instance.powerSupply;
            data["powerUsed"] = EconomySystem.Instance.powerUsed;
            data["techPoints"] = EconomySystem.Instance.techPoints;
        }

        // 科技
        if (TechSystem.Instance != null)
        {
            data["techLevel"] = TechSystem.Instance.techLevel;
        }

        // 游戏时间
        if (GameClock.Instance != null)
            data["gameTime"] = GameClock.Instance.GameTime;

        // 单位（简化版 — 只存关键信息）
        var units = new List<Dictionary<string, object>>();
        if (UnitRegistry.Instance != null)
        {
            foreach (var u in UnitRegistry.Instance.GetAllUnits())
            {
                if (u == null || u.isDead) continue;
                units.Add(new Dictionary<string, object>
                {
                    {"type", u.def.unitId},
                    {"x", u.transform.position.x},
                    {"y", u.transform.position.y},
                    {"hp", u.currentHp},
                    {"isEnemy", u.isEnemy}
                });
            }
        }
        data["units"] = units;

        return data;
    }

    /// <summary>
    /// 扩展存档槽位（内购调用）
    /// </summary>
    public void ExpandSaveSlots(int newMax)
    {
        maxSaveSlots = newMax;
        PlayerPrefs.SetInt("max_save_slots", newMax);
        PlayerPrefs.Save();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) AutoSave(); // 切后台自动存档
    }
}
