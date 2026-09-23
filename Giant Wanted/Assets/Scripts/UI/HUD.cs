using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GiantWanted
{
    /// <summary>
    /// Wires the canvas to the game. Everything is optional - the HUD only touches the
    /// widgets that are actually assigned, so you can strip parts of the canvas freely.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        [Header("Game refs")]
        public GameManager game;
        public WeaponController weapon;
        public PlayerAim aim;

        [Header("Top bar")]
        public Text waveLabel;
        public Text scoreLabel;
        public Text coinLabel;
        public Text bestLabel;

        [Header("City health")]
        public Image cityFill;
        public Text cityLabel;
        public Gradient cityGradient;

        [Header("Weapon")]
        public Text ammoLabel;
        public Image reloadFill;
        public HoldButton fireButton;
        public Button reloadButton;
        public Button zoomButton;
        public Image zoomIcon;

        [Header("Crosshair")]
        public Image crosshair;
        public Color crosshairIdle = new Color(1f, 1f, 1f, 0.65f);
        public Color crosshairOnTarget = new Color(1f, 0.3f, 0.2f, 1f);
        public float crosshairSpreadPixels = 26f;

        [Header("Panels")]
        public GameObject startPanel;
        public GameObject gameplayPanel;
        public GameObject endPanel;
        public Text endTitle;
        public Text endBody;
        public Button startButton;
        public Button retryButton;

        [Header("Announcements")]
        public Text announcement;
        public float announcementTime = 1.6f;

        [Header("Damage flash")]
        public Image damageFlash;
        public float damageFlashTime = 0.35f;

        RectTransform _crosshairRect;
        Vector2 _crosshairBaseSize;
        Coroutine _announcementRoutine;
        Coroutine _flashRoutine;
        int _lastCityHealth;
        bool _cinematic;

        void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (weapon == null) weapon = FindFirstObjectByType<WeaponController>();
            if (aim == null) aim = FindFirstObjectByType<PlayerAim>();

            if (crosshair != null)
            {
                _crosshairRect = crosshair.rectTransform;
                _crosshairBaseSize = _crosshairRect.sizeDelta;
            }
        }

        void OnEnable()
        {
            if (game != null)
            {
                game.StateChanged += OnStateChanged;
                game.ScoreChanged += OnScoreChanged;
                game.CoinsChanged += OnCoinsChanged;
                game.CityHealthChanged += OnCityHealthChanged;
                game.WaveChanged += OnWaveChanged;
                game.Announcement += Announce;
            }

            if (weapon != null)
            {
                weapon.AmmoChanged += OnAmmoChanged;
                weapon.ReloadingChanged += OnReloadingChanged;
            }

            if (fireButton != null) fireButton.onHeldChanged.AddListener(OnFireHeld);
            if (reloadButton != null) reloadButton.onClick.AddListener(OnReloadPressed);
            if (zoomButton != null) zoomButton.onClick.AddListener(OnZoomPressed);
            if (startButton != null) startButton.onClick.AddListener(OnStartPressed);
            if (retryButton != null) retryButton.onClick.AddListener(OnStartPressed);
        }

        void OnDisable()
        {
            if (game != null)
            {
                game.StateChanged -= OnStateChanged;
                game.ScoreChanged -= OnScoreChanged;
                game.CoinsChanged -= OnCoinsChanged;
                game.CityHealthChanged -= OnCityHealthChanged;
                game.WaveChanged -= OnWaveChanged;
                game.Announcement -= Announce;
            }

            if (weapon != null)
            {
                weapon.AmmoChanged -= OnAmmoChanged;
                weapon.ReloadingChanged -= OnReloadingChanged;
            }

            if (fireButton != null) fireButton.onHeldChanged.RemoveListener(OnFireHeld);
            if (reloadButton != null) reloadButton.onClick.RemoveListener(OnReloadPressed);
            if (zoomButton != null) zoomButton.onClick.RemoveListener(OnZoomPressed);
            if (startButton != null) startButton.onClick.RemoveListener(OnStartPressed);
            if (retryButton != null) retryButton.onClick.RemoveListener(OnStartPressed);
        }

        void Start()
        {
            if (game != null)
            {
                _lastCityHealth = game.CityHealth;
                OnStateChanged(game.State);
                OnScoreChanged(game.Score);
                OnCoinsChanged(game.Coins);
                OnCityHealthChanged(game.CityHealth, game.cityMaxHealth);
            }

            if (weapon != null) OnAmmoChanged(weapon.Ammo, weapon.magazineSize);
            if (damageFlash != null) SetAlpha(damageFlash, 0f);
            if (announcement != null) SetAlpha(announcement, 0f);
        }

        void Update()
        {
            UpdateCrosshair();
            UpdateReloadDial();

            if (zoomIcon != null && aim != null)
                zoomIcon.color = aim.IsZoomed ? crosshairOnTarget : Color.white;
        }

        // ----- widgets ------------------------------------------------------------

        void UpdateCrosshair()
        {
            if (crosshair == null || _crosshairRect == null) return;

            bool onTarget = aim != null && aim.HasTarget;
            crosshair.color = Color.Lerp(crosshair.color, onTarget ? crosshairOnTarget : crosshairIdle,
                                         12f * Time.unscaledDeltaTime);

            // Bloom the reticle while firing, tighten it while zoomed.
            float bloom = 0f;
            if (weapon != null && weapon.IsFiring) bloom += crosshairSpreadPixels;
            if (aim != null && aim.IsZoomed) bloom -= crosshairSpreadPixels * 0.4f;

            Vector2 wanted = _crosshairBaseSize + Vector2.one * bloom;
            _crosshairRect.sizeDelta = Vector2.Lerp(_crosshairRect.sizeDelta, wanted, 10f * Time.unscaledDeltaTime);
        }

        void UpdateReloadDial()
        {
            if (reloadFill == null || weapon == null) return;
            reloadFill.enabled = weapon.IsReloading;
            if (weapon.IsReloading) reloadFill.fillAmount = weapon.ReloadProgress;
        }

        // ----- events -------------------------------------------------------------

        void OnStateChanged(GameState state)
        {
            bool playing = state == GameState.Playing || state == GameState.WaveCleared;

            if (startPanel != null) startPanel.SetActive(state == GameState.Ready);
            if (gameplayPanel != null) gameplayPanel.SetActive(playing && !_cinematic);
            if (endPanel != null) endPanel.SetActive(state == GameState.Victory || state == GameState.Defeat);

            if (state == GameState.Victory || state == GameState.Defeat)
            {
                if (endTitle != null) endTitle.text = state == GameState.Victory ? "CITY SAVED" : "CITY LOST";
                if (endBody != null && game != null)
                    endBody.text = "SCORE  " + game.Score + "\nBEST  " + game.BestScore +
                                   "\nWAVE  " + game.WaveNumber + "\nCOINS  " + game.Coins;
            }
        }

        void OnScoreChanged(int score)
        {
            if (scoreLabel != null) scoreLabel.text = score.ToString();
            if (bestLabel != null && game != null) bestLabel.text = "BEST " + game.BestScore;
        }

        void OnCoinsChanged(int coins)
        {
            if (coinLabel != null) coinLabel.text = coins.ToString();
        }

        void OnCityHealthChanged(int current, int max)
        {
            float normalized = max > 0 ? (float)current / max : 0f;

            if (cityFill != null)
            {
                cityFill.fillAmount = normalized;
                if (cityGradient != null) cityFill.color = cityGradient.Evaluate(normalized);
            }

            if (cityLabel != null) cityLabel.text = current + " / " + max;

            if (current < _lastCityHealth) FlashDamage();
            _lastCityHealth = current;
        }

        void OnWaveChanged(int wave, int total)
        {
            if (waveLabel == null) return;
            waveLabel.text = total > 0 ? "WAVE " + wave + " / " + total : "WAVE " + wave;
        }

        void OnAmmoChanged(int ammo, int magazine)
        {
            if (ammoLabel != null) ammoLabel.text = ammo + " / " + magazine;
        }

        void OnReloadingChanged(bool reloading)
        {
            if (ammoLabel != null && reloading) ammoLabel.text = "RELOADING";
        }

        void OnFireHeld(bool held)
        {
            if (weapon != null) weapon.SetFireHeld(held);
        }

        void OnReloadPressed()
        {
            if (weapon != null) weapon.Reload();
        }

        void OnZoomPressed()
        {
            if (aim != null) aim.ToggleZoom();
        }

        void OnStartPressed()
        {
            if (game != null) game.StartGame();
        }

        // ----- flourishes ---------------------------------------------------------

        /// <summary>Clears the screen for a cinematic (the bullet kill cam) and restores it after.</summary>
        public void SetCinematic(bool active)
        {
            _cinematic = active;

            if (crosshair != null) crosshair.gameObject.SetActive(!active);
            if (game != null) OnStateChanged(game.State);
            else if (gameplayPanel != null) gameplayPanel.SetActive(!active);
        }

        public void Announce(string message)
        {
            if (announcement == null) return;
            announcement.text = message;

            if (_announcementRoutine != null) StopCoroutine(_announcementRoutine);
            _announcementRoutine = StartCoroutine(AnnouncementRoutine());
        }

        IEnumerator AnnouncementRoutine()
        {
            float t = 0f;
            while (t < announcementTime)
            {
                t += Time.unscaledDeltaTime;
                float k = t / announcementTime;
                SetAlpha(announcement, k < 0.25f ? k / 0.25f : 1f - (k - 0.25f) / 0.75f);
                announcement.rectTransform.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }

            SetAlpha(announcement, 0f);
            _announcementRoutine = null;
        }

        void FlashDamage()
        {
            if (damageFlash == null) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(DamageFlashRoutine());
        }

        IEnumerator DamageFlashRoutine()
        {
            float t = 0f;
            while (t < damageFlashTime)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(damageFlash, Mathf.Lerp(0.45f, 0f, t / damageFlashTime));
                yield return null;
            }

            SetAlpha(damageFlash, 0f);
            _flashRoutine = null;
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = alpha;
            graphic.color = c;
        }
    }
}
