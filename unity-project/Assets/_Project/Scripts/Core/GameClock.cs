// ================================================================
// GameClock.cs — 统一时间源，支持暂停/倍速
// ================================================================
using UnityEngine;

public class GameClock : MonoBehaviour
{
    public static GameClock Instance { get; private set; }

    [Header("Time Control")]
    public float timeScale = 1f;
    public bool isPaused = false;

    /// <summary>
    /// 游戏逻辑 deltaTime（受暂停和倍速影响）
    /// </summary>
    public float DeltaTime => isPaused ? 0f : Time.deltaTime * timeScale;

    /// <summary>
    /// 游戏运行总时间
    /// </summary>
    public float GameTime { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (!isPaused)
            GameTime += DeltaTime;
    }

    public void Pause() => isPaused = true;
    public void Resume() => isPaused = false;
    public void SetSpeed(float speed) => timeScale = speed;
}
