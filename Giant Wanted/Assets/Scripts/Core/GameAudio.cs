using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// Listens to the weapon and the game flow and plays one-shots. Every clip is optional,
    /// so the project ships silent - drop clips into the inspector and sound just works.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        [Header("Wiring")]
        public GameManager game;
        public WeaponController weapon;

        [Header("Clips (all optional)")]
        public AudioClip shot;
        public AudioClip reload;
        public AudioClip giantHit;
        public AudioClip giantDeath;
        public AudioClip waveStart;
        [Tooltip("Plays with the WAVE CLEARED banner.")]
        public AudioClip waveComplete;
        public AudioClip victory;
        public AudioClip defeat;

        [Header("Music")]
        [Tooltip("Looping background track. Left empty, no music source is created.")]
        public AudioClip music;
        [Tooltip("Own source so the loop is independent of the one-shots. Created on demand.")]
        public AudioSource musicSource;
        [Range(0f, 1f)] public float musicVolume = 0.35f;

        [Header("Mix")]
        [Range(0f, 1f)] public float shotVolume = 0.55f;
        [Range(0f, 1f)] public float uiVolume = 0.8f;
        [Tooltip("Random pitch spread on the shot so full auto does not sound robotic.")]
        public float shotPitchJitter = 0.06f;

        AudioSource _source;

        void Awake()
        {
            Instance = this;
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (weapon == null) weapon = FindFirstObjectByType<WeaponController>();

            SetUpMusic();
        }

        void SetUpMusic()
        {
            if (music == null && musicSource == null) return;

            if (musicSource == null)
            {
                GameObject go = new GameObject("Music");
                go.transform.SetParent(transform, false);
                musicSource = go.AddComponent<AudioSource>();
            }

            if (musicSource.clip == null) musicSource.clip = music;
            musicSource.loop = true;
            musicSource.playOnAwake = true;
            musicSource.spatialBlend = 0f;
            musicSource.volume = musicVolume;
        }

        void Start()
        {
            // playOnAwake covers a source authored in the scene; this covers one made just now.
            if (musicSource != null && musicSource.clip != null && !musicSource.isPlaying)
                musicSource.Play();
        }

        void OnEnable()
        {
            if (weapon != null)
            {
                weapon.Fired += OnFired;
                weapon.ReloadingChanged += OnReloading;
            }

            if (game != null)
            {
                game.StateChanged += OnStateChanged;
                game.WaveChanged += OnWaveChanged;
                if (game.spawner != null) game.spawner.GiantKilled += OnGiantKilled;
            }
        }

        void OnDisable()
        {
            if (weapon != null)
            {
                weapon.Fired -= OnFired;
                weapon.ReloadingChanged -= OnReloading;
            }

            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.WaveChanged -= OnWaveChanged;
                if (game.spawner != null) game.spawner.GiantKilled -= OnGiantKilled;
            }
        }

        void OnFired()
        {
            if (shot == null) return;
            _source.pitch = 1f + Random.Range(-shotPitchJitter, shotPitchJitter);
            _source.PlayOneShot(shot, shotVolume);
            _source.pitch = 1f;
        }

        void OnReloading(bool reloading)
        {
            if (reloading) Play(reload, uiVolume);
        }

        void OnGiantKilled(Giant giant)
        {
            Play(giantDeath, uiVolume);
        }

        void OnWaveChanged(int wave, int total)
        {
            Play(waveStart, uiVolume);
        }

        void OnStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.WaveCleared:
                    Play(waveComplete, uiVolume);
                    break;

                // The final wave ends in Victory rather than WaveCleared, so fall back to
                // the same sting if no dedicated victory clip is set.
                case GameState.Victory:
                    Play(victory != null ? victory : waveComplete, uiVolume);
                    break;

                case GameState.Defeat:
                    Play(defeat, uiVolume);
                    break;
            }
        }

        /// <summary>Positional hit sound, called from the giant when it takes damage.</summary>
        public void PlayHitAt(Vector3 position)
        {
            if (giantHit == null) return;
            AudioSource.PlayClipAtPoint(giantHit, position, uiVolume);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Play(AudioClip clip, float volume)
        {
            if (clip == null) return;
            _source.PlayOneShot(clip, volume);
        }
    }
}
