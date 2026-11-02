// ================================================================
// AudioManager.cs — 音频管理器（Web Audio 合成版在网页Demo用，Unity版用 AudioClip）
// ================================================================
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Weapon Sounds")]
    public AudioClip rifleSound;
    public AudioClip cannonSound;
    public AudioClip explosionSound;
    public AudioClip nukeSound;

    [Header("UI Sounds")]
    public AudioClip selectSound;
    public AudioClip moveSound;
    public AudioClip buildSound;
    public AudioClip errorSound;
    public AudioClip alertSound;

    [Header("Settings")]
    public int maxChannels = 24;
    public float weaponVolume = 0.7f;
    public float uiVolume = 0.5f;

    private AudioSource[] channels;
    private int channelIndex = 0;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        channels = new AudioSource[maxChannels];
        for (int i = 0; i < maxChannels; i++)
        {
            channels[i] = gameObject.AddComponent<AudioSource>();
            channels[i].playOnAwake = false;
            channels[i].spatialBlend = 0f; // 2D
        }
    }

    AudioSource GetChannel()
    {
        var ch = channels[channelIndex];
        channelIndex = (channelIndex + 1) % maxChannels;
        return ch;
    }

    void PlayClip(AudioClip clip, float volume)
    {
        if (clip == null) return;
        var ch = GetChannel();
        ch.clip = clip;
        ch.volume = volume;
        ch.Play();
    }

    public void PlayWeaponSound(DamageType type, bool isVehicle)
    {
        AudioClip clip = type == DamageType.Piercing ? cannonSound : rifleSound;
        PlayClip(clip, weaponVolume * (isVehicle ? 0.8f : 0.4f));
    }

    public void PlayExplosion(bool isVehicle = false)
    {
        PlayClip(explosionSound, weaponVolume * (isVehicle ? 0.8f : 0.4f));
    }

    public void PlayNuke()
    {
        PlayClip(nukeSound, weaponVolume);
    }

    public void PlaySelectSound() => PlayClip(selectSound, uiVolume * 0.5f);
    public void PlayMoveSound() => PlayClip(moveSound, uiVolume * 0.5f);
    public void PlayBuildSound() => PlayClip(buildSound, uiVolume);
    public void PlayErrorSound() => PlayClip(errorSound, uiVolume);
    public void PlayAlertSound() => PlayClip(alertSound, uiVolume);
}
