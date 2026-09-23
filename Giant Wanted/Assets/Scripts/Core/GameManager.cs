using System;
using System.Collections;
using UnityEngine;

namespace GiantWanted
{
    public enum GameState
    {
        Ready,        // title card, waiting for the player to tap
        Playing,      // wave in progress
        WaveCleared,  // short breather between waves
        Victory,
        Defeat
    }

    /// <summary>
    /// Owns the run: wave order, city health, score and coins. Everything else listens to
    /// its events rather than polling, so the HUD and the weapon stay decoupled from the flow.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("References")]
        public WaveSpawner spawner;
        public WeaponController weapon;

        [Header("City")]
        public int cityMaxHealth = 100;

        [Header("Waves")]
        public Wave[] waves;
        [Tooltip("Seconds of calm between waves.")]
        public float waveBreak = 3.5f;
        [Tooltip("After the last authored wave, keep generating harder ones instead of winning.")]
        public bool endless = false;
        [Tooltip("Per-wave difficulty growth used for generated endless waves.")]
        public float endlessHealthGrowth = 0.18f;
        public float endlessSpeedGrowth = 0.06f;

        [Header("Kill cam")]
        [Tooltip("Slow motion when the final giant of a wave dies.")]
        public bool slowMoOnLastKill = true;
        public float slowMoScale = 0.35f;
        public float slowMoDuration = 0.9f;

        [Header("Scoring")]
        public int weakPointBonus = 50;

        public GameState State { get; private set; } = GameState.Ready;
        public int Score { get; private set; }
        public int Coins { get; private set; }
        public int CityHealth { get; private set; }
        public int WaveNumber { get; private set; }
        public int TotalWaves { get { return waves != null ? waves.Length : 0; } }
        public int BestScore { get { return PlayerPrefs.GetInt(BestScoreKey, 0); } }

        /// <summary>
        /// Raised high by a cinematic (the bullet kill cam) to pause wave progression and
        /// suppress the built-in slow-motion beat, so the two do not fight over the moment.
        /// </summary>
        public bool CinematicHold { get; set; }

        public event Action<GameState> StateChanged;
        public event Action<int> ScoreChanged;
        public event Action<int> CoinsChanged;
        public event Action<int, int> CityHealthChanged;   // current, max
        public event Action<int, int> WaveChanged;         // wave number, total (0 = endless)
        public event Action<string> Announcement;

        const string BestScoreKey = "GW_BestScore";
        const string CoinsKey = "GW_Coins";

        Coroutine _flow;
        Coroutine _slowMo;

        void Awake()
        {
            Instance = this;
            CityHealth = cityMaxHealth;
            Coins = PlayerPrefs.GetInt(CoinsKey, 0);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        void Start()
        {
            if (spawner != null) spawner.GiantKilled += HandleGiantKilled;
            SetState(GameState.Ready);
            CityHealthChanged?.Invoke(CityHealth, cityMaxHealth);
            ScoreChanged?.Invoke(Score);
            CoinsChanged?.Invoke(Coins);
        }

        // ----- flow -------------------------------------------------------------

        public void StartGame()
        {
            if (State == GameState.Playing || State == GameState.WaveCleared) return;

            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            CinematicHold = false;
            Score = 0;
            CityHealth = cityMaxHealth;
            WaveNumber = 0;

            if (spawner != null) spawner.DespawnAll();
            if (weapon != null) weapon.ResetWeapon();

            ScoreChanged?.Invoke(Score);
            CityHealthChanged?.Invoke(CityHealth, cityMaxHealth);

            SetState(GameState.Playing);

            if (_flow != null) StopCoroutine(_flow);
            _flow = StartCoroutine(RunWaves());
        }

        public void Restart()
        {
            StartGame();
        }

        IEnumerator RunWaves()
        {
            int index = 0;

            while (true)
            {
                Wave wave = GetWave(index);
                if (wave == null) break;

                WaveNumber = index + 1;
                WaveChanged?.Invoke(WaveNumber, endless ? 0 : TotalWaves);
                Announcement?.Invoke("WAVE " + WaveNumber);

                yield return spawner.SpawnWave(wave);

                // Wait for the field to clear (or for the run to end).
                while (State == GameState.Playing && (spawner.IsSpawning || spawner.AliveCount > 0))
                    yield return null;

                if (State != GameState.Playing) yield break;

                // Let a kill cam play out before the wave banner and the next spawn.
                while (CinematicHold && State == GameState.Playing) yield return null;
                if (State != GameState.Playing) yield break;

                index++;
                if (GetWave(index) == null) break;

                SetState(GameState.WaveCleared);
                Announcement?.Invoke("WAVE CLEARED");
                yield return new WaitForSeconds(waveBreak);

                if (State != GameState.WaveCleared) yield break;
                SetState(GameState.Playing);
            }

            Win();
        }

        /// <summary>Authored wave, or a generated one when running endless.</summary>
        Wave GetWave(int index)
        {
            if (waves != null && index < waves.Length) return waves[index];
            if (!endless || waves == null || waves.Length == 0) return null;

            Wave last = waves[waves.Length - 1];
            int extra = index - waves.Length + 1;

            return new Wave
            {
                count = last.count + Mathf.FloorToInt(extra * 0.5f),
                healthMultiplier = last.healthMultiplier * (1f + endlessHealthGrowth * extra),
                speedMultiplier = last.speedMultiplier * (1f + endlessSpeedGrowth * extra),
                spawnInterval = Mathf.Max(0.5f, last.spawnInterval - 0.08f * extra),
                scaleRange = last.scaleRange,
                maxConcurrent = last.maxConcurrent
            };
        }

        void Win()
        {
            SaveProgress();
            SetState(GameState.Victory);
            Announcement?.Invoke("CITY SAVED");
        }

        void Lose()
        {
            SaveProgress();
            if (_flow != null) { StopCoroutine(_flow); _flow = null; }
            SetState(GameState.Defeat);
            Announcement?.Invoke("CITY LOST");
        }

        void SaveProgress()
        {
            if (Score > BestScore) PlayerPrefs.SetInt(BestScoreKey, Score);
            PlayerPrefs.SetInt(CoinsKey, Coins);
            PlayerPrefs.Save();
        }

        void SetState(GameState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(State);
        }

        // ----- gameplay hooks ---------------------------------------------------

        public void DamageCity(int amount)
        {
            if (State != GameState.Playing && State != GameState.WaveCleared) return;

            CityHealth = Mathf.Max(0, CityHealth - amount);
            CityHealthChanged?.Invoke(CityHealth, cityMaxHealth);

            if (CityHealth <= 0) Lose();
        }

        public void AddScore(int amount)
        {
            Score += amount;
            ScoreChanged?.Invoke(Score);
        }

        public void AddCoins(int amount)
        {
            Coins += amount;
            CoinsChanged?.Invoke(Coins);
        }

        void HandleGiantKilled(Giant giant)
        {
            AddScore(giant.scoreValue + (giant.KilledByWeakPoint ? weakPointBonus : 0));
            AddCoins(giant.coinValue);
            if (giant.KilledByWeakPoint) Announcement?.Invoke("HEADSHOT +" + weakPointBonus);

            bool lastOfWave = !spawner.IsSpawning && spawner.AliveCount == 0;
            if (slowMoOnLastKill && lastOfWave && State == GameState.Playing && !CinematicHold)
            {
                if (_slowMo != null) StopCoroutine(_slowMo);
                _slowMo = StartCoroutine(SlowMoRoutine());
            }
        }

        IEnumerator SlowMoRoutine()
        {
            Time.timeScale = slowMoScale;
            Time.fixedDeltaTime = 0.02f * slowMoScale;

            yield return new WaitForSecondsRealtime(slowMoDuration);

            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            _slowMo = null;
        }
    }
}
