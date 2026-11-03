// ================================================================
// LocalizationManager.cs — 本地化系统
// CSV 文本表 → 多语言切换
// ================================================================
using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class LocalizationManager : MonoBehaviour
{
    public static LocalizationManager Instance { get; private set; }

    [Header("Config")]
    public string defaultLanguage = "zh-CN";
    public TextAsset csvFile; // strings.csv

    [Header("Runtime")]
    public string currentLanguage = "zh-CN";

    private Dictionary<string, Dictionary<string, string>> localizedTexts;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        currentLanguage = PlayerPrefs.GetString("language", defaultLanguage);
        LoadCSV();
    }

    /// <summary>
    /// 获取本地化文本
    /// </summary>
    public static string Get(string key)
    {
        if (Instance == null || Instance.localizedTexts == null) return key;
        if (!Instance.localizedTexts.ContainsKey(Instance.currentLanguage)) return key;
        if (!Instance.localizedTexts[Instance.currentLanguage].ContainsKey(key)) return key;
        return Instance.localizedTexts[Instance.currentLanguage][key];
    }

    /// <summary>
    /// 切换语言
    /// </summary>
    public void SetLanguage(string lang)
    {
        if (localizedTexts != null && localizedTexts.ContainsKey(lang))
        {
            currentLanguage = lang;
            PlayerPrefs.SetString("language", lang);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 获取所有支持的语言
    /// </summary>
    public List<string> GetAvailableLanguages()
    {
        if (localizedTexts == null) return new List<string>();
        return new List<string>(localizedTexts.Keys);
    }

    void LoadCSV()
    {
        localizedTexts = new Dictionary<string, Dictionary<string, string>>();

        if (csvFile == null)
        {
            Debug.LogWarning("[Localization] No CSV file assigned.");
            return;
        }

        string[] lines = csvFile.text.Split('\n');
        if (lines.Length < 2) return;

        // 第一行是表头: key,zh-CN,en,zh-TW,ja
        string[] headers = lines[0].Trim().Split(',');

        // 初始化语言字典
        for (int c = 1; c < headers.Length; c++)
        {
            if (!localizedTexts.ContainsKey(headers[c]))
                localizedTexts[headers[c]] = new Dictionary<string, string>();
        }

        // 逐行读取
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 2) continue;

            string key = parts[0];
            for (int c = 1; c < headers.Length && c < parts.Length; c++)
            {
                string lang = headers[c];
                if (localizedTexts.ContainsKey(lang))
                    localizedTexts[lang][key] = parts[c];
            }
        }

        Debug.Log($"[Localization] Loaded {lines.Length - 1} entries for {localizedTexts.Count} languages.");
    }
}
