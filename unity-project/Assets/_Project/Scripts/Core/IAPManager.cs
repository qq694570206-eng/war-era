// ================================================================
// IAPManager.cs — 内购管理器
// 背景包解锁/便利性/外观/去广告
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class IAPManager : MonoBehaviour
{
    public static IAPManager Instance { get; private set; }

    [Header("Product IDs")]
    public const string THEME_ANCIENT = "theme_ancient";
    public const string THEME_WW1 = "theme_ww1";
    public const string THEME_POSTWAR = "theme_postwar";
    public const string THEME_MODERN = "theme_modern";
    public const string THEME_ALL = "theme_all_pack";
    public const string REMOVE_ADS = "remove_ads";
    public const string QUEUE_EXPAND = "queue_expand";
    public const string SQUAD_EXPAND = "squad_expand";
    public const string SAVE_EXPAND = "save_expand";

    [Header("State")]
    public HashSet<string> purchasedProducts = new HashSet<string>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        LoadPurchases();
    }

    /// <summary>
    /// 购买商品
    /// </summary>
    public void Purchase(string productId)
    {
        // TODO: 接入 Unity IAP / StoreKit / Google Play Billing
        // 当前为模拟购买
        purchasedProducts.Add(productId);
        SavePurchases();
        ApplyPurchase(productId);

        UIManager.Instance?.ShowToast($"购买成功！");
        Debug.Log($"[IAP] Purchased: {productId}");
    }

    /// <summary>
    /// 恢复购买
    /// </summary>
    public void RestorePurchases()
    {
        // TODO: 调用 Unity IAP 的 RestorePurchases
        UIManager.Instance?.ShowToast("已恢复购买");
    }

    /// <summary>
    /// 检查是否已购买
    /// </summary>
    public bool IsPurchased(string productId)
    {
        return purchasedProducts.Contains(productId);
    }

    /// <summary>
    /// 检查背景包是否解锁
    /// </summary>
    public bool IsThemeUnlocked(string themeId)
    {
        if (themeId == "ww2") return true; // 二战免费
        if (IsPurchased(THEME_ALL)) return true;
        return IsPurchased($"theme_{themeId}");
    }

    /// <summary>
    /// 是否已去广告
    /// </summary>
    public bool IsAdsRemoved()
    {
        return IsPurchased(REMOVE_ADS);
    }

    void ApplyPurchase(string productId)
    {
        switch (productId)
        {
            case QUEUE_EXPAND:
                // 生产队列从 5 扩到 8
                break;
            case SQUAD_EXPAND:
                // 编队从 5 扩到 8
                if (TouchInputSystem.Instance != null)
                {
                    // TODO: 扩展 squads 数组
                }
                break;
            case SAVE_EXPAND:
                // 存档槽位从 1 扩到 5
                if (SaveSystem.Instance != null)
                    SaveSystem.Instance.ExpandSaveSlots(5);
                break;
            case REMOVE_ADS:
                UIManager.Instance?.ShowToast("广告已永久移除");
                break;
        }
    }

    void LoadPurchases()
    {
        int count = PlayerPrefs.GetInt("iap_count", 0);
        for (int i = 0; i < count; i++)
        {
            string id = PlayerPrefs.GetString($"iap_{i}", "");
            if (!string.IsNullOrEmpty(id)) purchasedProducts.Add(id);
        }
    }

    void SavePurchases()
    {
        int i = 0;
        foreach (var id in purchasedProducts)
        {
            PlayerPrefs.SetString($"iap_{i}", id);
            i++;
        }
        PlayerPrefs.SetInt("iap_count", i);
        PlayerPrefs.Save();
    }
}
