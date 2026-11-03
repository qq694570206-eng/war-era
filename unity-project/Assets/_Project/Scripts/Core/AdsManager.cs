// ================================================================
// AdsManager.cs — 广告管理器
// 激励广告/插屏/横幅 + 频率控制 + 去广告
// ================================================================
using UnityEngine;

public class AdsManager : MonoBehaviour
{
    public static AdsManager Instance { get; private set; }

    [Header("Frequency Control")]
    public int interstitialEveryNGames = 3;
    private int gamesSinceLastInterstitial = 0;

    [Header("Rewarded Ad State")]
    public bool isRewardedAdReady = false;
    public bool isRewardedAdPlaying = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 检查是否已去广告
    /// </summary>
    public bool IsAdsRemoved()
    {
        return IAPManager.Instance?.IsAdsRemoved() ?? false;
    }

    // === 激励广告（玩家主动看，给奖励）===

    /// <summary>
    /// 看广告获得矿石
    /// </summary>
    public void ShowRewardedAd_Ore()
    {
        if (IsAdsRemoved())
        {
            // 已去广告，直接给奖励
            GrantOreReward();
            return;
        }

        // TODO: 调用 Unity Ads Show("rewarded_ore")
        // 当前模拟：直接给奖励
        GrantOreReward();
    }

    void GrantOreReward()
    {
        if (EconomySystem.Instance != null)
        {
            EconomySystem.Instance.ore += 500;
            UIManager.Instance?.ShowToast("获得 500 矿石！");
        }
    }

    /// <summary>
    /// 看广告立即完成建造
    /// </summary>
    public void ShowRewardedAd_FinishBuild()
    {
        if (IsAdsRemoved())
        {
            FinishBuildReward();
            return;
        }
        // TODO: Unity Ads
        FinishBuildReward();
    }

    void FinishBuildReward()
    {
        // 找到正在建造的建筑，立即完成
        if (UnitRegistry.Instance != null)
        {
            foreach (var u in UnitRegistry.Instance.GetPlayerUnits())
            {
                if (u != null && u.def.isBuilding && u.constructing)
                {
                    u.buildProgress = 1f;
                    u.constructing = false;
                    UIManager.Instance?.ShowToast($"{u.def.displayName} 立即完成！");
                    return;
                }
            }
        }
        UIManager.Instance?.ShowToast("没有正在建造的建筑");
    }

    /// <summary>
    /// 看广告复活 HQ
    /// </summary>
    public void ShowRewardedAd_ReviveHQ()
    {
        if (IsAdsRemoved())
        {
            ReviveHQReward();
            return;
        }
        // TODO: Unity Ads
        ReviveHQReward();
    }

    void ReviveHQReward()
    {
        if (UnitRegistry.Instance == null) return;
        // 找到已死亡的 HQ
        // TODO: 恢复 HQ 到 50% HP
        UIManager.Instance?.ShowToast("HQ 已恢复 50% HP！");
    }

    // === 插屏广告（被动，战斗结束后）===

    /// <summary>
    /// 尝试显示插屏广告
    /// </summary>
    public void TryShowInterstitial()
    {
        if (IsAdsRemoved()) return;

        gamesSinceLastInterstitial++;
        if (gamesSinceLastInterstitial >= interstitialEveryNGames)
        {
            gamesSinceLastInterstitial = 0;
            // TODO: 调用 Unity Ads Show("interstitial")
            Debug.Log("[Ads] Showing interstitial ad.");
        }
    }

    // === 横幅广告（主菜单常驻）===

    /// <summary>
    /// 显示横幅广告
    /// </summary>
    public void ShowBanner()
    {
        if (IsAdsRemoved()) return;
        // TODO: 调用 Unity Ads Banner.Show()
        Debug.Log("[Ads] Showing banner ad.");
    }

    /// <summary>
    /// 隐藏横幅广告
    /// </summary>
    public void HideBanner()
    {
        // TODO: Unity Ads Banner.Hide()
    }
}
