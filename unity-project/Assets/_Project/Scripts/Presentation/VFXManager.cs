// ================================================================
// VFXManager.cs — 特效管理器（对象池）
// 子弹/爆炸/枪口闪光/移动指示/攻击指示
// ================================================================
using UnityEngine;
using System.Collections.Generic;

public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance { get; private set; }

    [Header("Prefabs")]
    public GameObject bulletPrefab;
    public GameObject explosionPrefab;
    public GameObject muzzleFlashPrefab;
    public GameObject moveIndicatorPrefab;
    public GameObject attackIndicatorPrefab;

    [Header("Pool Settings")]
    public int bulletPoolSize = 200;
    public int explosionPoolSize = 20;
    public int indicatorPoolSize = 10;

    private List<GameObject> bulletPool = new List<GameObject>();
    private List<GameObject> explosionPool = new List<GameObject>();
    private List<GameObject> indicatorPool = new List<GameObject>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        InitPools();
    }

    void InitPools()
    {
        for (int i = 0; i < bulletPoolSize; i++)
        {
            var go = CreatePooledObject(bulletPrefab, "Bullet_" + i);
            if (go != null) bulletPool.Add(go);
        }
        for (int i = 0; i < explosionPoolSize; i++)
        {
            var go = CreatePooledObject(explosionPrefab, "Explosion_" + i);
            if (go != null) explosionPool.Add(go);
        }
        for (int i = 0; i < indicatorPoolSize; i++)
        {
            var go = CreatePooledObject(moveIndicatorPrefab, "Indicator_" + i);
            if (go != null) indicatorPool.Add(go);
        }
    }

    GameObject CreatePooledObject(GameObject prefab, string name)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, transform);
        go.name = name;
        go.SetActive(false);
        return go;
    }

    GameObject GetFromPool(List<GameObject> pool, GameObject fallback)
    {
        foreach (var go in pool)
        {
            if (go != null && !go.activeSelf) return go;
        }
        // 池耗尽，复用第一个
        if (pool.Count > 0) return pool[0];
        return null;
    }

    public void SpawnBullet(Vector3 from, Vector3 to, bool isEnemy)
    {
        var go = GetFromPool(bulletPool, bulletPrefab);
        if (go == null) return;
        go.transform.position = from;
        go.SetActive(true);

        var bullet = go.GetComponent<BulletVisual>();
        if (bullet != null) bullet.Initialize(from, to, isEnemy);
    }

    /// <summary>
    /// 播放爆炸。<paramref name="scale"/> 语义 = 【直径倍数】，
    /// 即目标爆炸直径 = EXPLOSION_BASE_DIAMETER × scale。
    ///
    /// 【为什么改语义】旧语义是「乘到 prefab 的 localScale 上」，而占位图
    /// placehold_circle.png 是 64px @ PPU 1.0 = 64 世界单位。调用点传 25 时，
    /// 实际直径 = 64 × 25 = 1600 世界单位，而屏幕全高只有 600（orthographicSize=300）
    /// —— 任意单位一死，爆炸会盖住整个屏幕（2.7 倍屏高）。
    ///
    /// 现在把基准直径显式写在这里，调用点只表达"多大倍数的爆炸"，与贴图尺寸解耦。
    /// 这样以后换真图（explosion 单帧 24 世界单位）也不用改调用点。
    /// </summary>
    public void SpawnExplosion(Vector3 position, float scale = 1f)
    {
        var go = GetFromPool(explosionPool, explosionPrefab);
        if (go == null) return;

        if (scale <= 0f) scale = 1f;   // 防御：传 0 会缩成不可见，排查起来很费时

        go.transform.position = position;
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(true);

        var ps = go.GetComponent<ParticleSystem>();
        if (ps != null) { ps.Clear(); ps.Play(); }

        // 回收时长 0.6 → 0.70：16 帧 @24fps = 0.6667s，0.6 会截掉最后一帧
        // （美术规格 §4.3-2。本轮还是占位图，但先把时长对齐，换序列帧时不再动这里。）
        StartCoroutine(DeactivateAfter(go, 0.70f));
    }

    public void SpawnMuzzleFlash(Vector3 position)
    {
        if (muzzleFlashPrefab == null) return;
        var go = Instantiate(muzzleFlashPrefab, position, Quaternion.identity, transform);
        Destroy(go, 0.1f);
    }

    public void SpawnMoveIndicator(Vector3 position)
    {
        if (moveIndicatorPrefab == null) return;
        var go = GetFromPool(indicatorPool, moveIndicatorPrefab);
        if (go == null) return;
        go.transform.position = position;
        go.SetActive(true);
        StartCoroutine(DeactivateAfter(go, 0.4f));
    }

    public void SpawnAttackIndicator(Vector3 position)
    {
        if (attackIndicatorPrefab == null) return;
        var go = Instantiate(attackIndicatorPrefab, position, Quaternion.identity, transform);
        Destroy(go, 0.4f);
    }

    System.Collections.IEnumerator DeactivateAfter(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go != null) go.SetActive(false);
    }
}

/// <summary>
/// 子弹视觉（挂在 bulletPrefab 上）
/// </summary>
public class BulletVisual : MonoBehaviour
{
    private Vector3 startPos;
    private Vector3 endPos;
    private float progress = 0f;
    private float speed = 600f;
    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    public void Initialize(Vector3 from, Vector3 to, bool isEnemy)
    {
        startPos = from;
        endPos = to;
        progress = 0f;
        if (sr != null) sr.color = isEnemy ? new Color(0.91f, 0.30f, 0.24f) : new Color(0.95f, 0.76f, 0.31f);
    }

    void Update()
    {
        float dist = Vector3.Distance(startPos, endPos);
        if (dist < 0.1f) { gameObject.SetActive(false); return; }

        progress += speed * Time.deltaTime / dist;
        if (progress >= 1f) { gameObject.SetActive(false); return; }

        transform.position = Vector3.Lerp(startPos, endPos, progress);
    }
}
