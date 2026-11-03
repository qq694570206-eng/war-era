// ================================================================
// ThemeManager.cs — 多背景包管理
// 加载/切换/卸载背景资源与数据表
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class ThemeManager : MonoBehaviour
{
    public static ThemeManager Instance { get; private set; }

    [Header("Available Themes")]
    public ThemeDefinition[] themes;

    [Header("Current")]
    public string currentThemeId = "ww2";
    public ThemeDefinition currentTheme;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 加载背景包
    /// </summary>
    public void LoadTheme(string themeId)
    {
        ThemeDefinition theme = FindTheme(themeId);
        if (theme == null)
        {
            Debug.LogError($"Theme '{themeId}' not found!");
            return;
        }

        // 检查是否已解锁
        if (IAPManager.Instance != null && !IAPManager.Instance.IsThemeUnlocked(themeId))
        {
            UIManager.Instance?.ShowToast("此背景包未解锁，请前往商店购买");
            return;
        }

        currentThemeId = themeId;
        currentTheme = theme;

        Debug.Log($"[ThemeManager] Theme '{theme.displayName}' loaded.");
    }

    /// <summary>
    /// 获取当前背景
    /// </summary>
    public ThemeDefinition GetCurrentTheme() => currentTheme;

    /// <summary>
    /// 检查背景是否解锁
    /// </summary>
    public bool IsThemeUnlocked(string themeId)
    {
        if (themeId == "ww2") return true;
        return IAPManager.Instance?.IsThemeUnlocked(themeId) ?? false;
    }

    /// <summary>
    /// 获取所有背景
    /// </summary>
    public ThemeDefinition[] GetAllThemes() => themes;

    ThemeDefinition FindTheme(string id)
    {
        if (themes == null) return null;
        foreach (var t in themes)
        {
            if (t != null && t.themeId == id) return t;
        }
        return null;
    }
}

[CreateAssetMenu(fileName = "ThemeDef_", menuName = "WarEra/Theme Definition")]
public class ThemeDefinition : ScriptableObject
{
    [Header("Identity")]
    public string themeId = "ww2";
    public string displayName = "二战";
    public string description;

    [Header("Factions")]
    public FactionDefinition faction1; // 玩家方
    public FactionDefinition faction2; // 敌方

    [Header("Units")]
    public UnitDefinition[] units;
    public UnitDefinition[] buildings;

    [Header("Visual")]
    public Sprite terrainTile;
    public Color ambientColor = Color.white;

    [Header("IAP")]
    public bool isFree = false;
    public string iapProductId = "";
}
