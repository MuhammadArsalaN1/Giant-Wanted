using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GiantWanted
{
    [Serializable]
    public class Wave
    {
        [Tooltip("How many giants this wave sends.")]
        public int count = 3;
        [Tooltip("Multiplies the giant prefab's authored max health.")]
        public float healthMultiplier = 1f;
        [Tooltip("Multiplies the giant prefab's authored move speed.")]
        public float speedMultiplier = 1f;
        [Tooltip("Seconds between spawns inside the wave.")]
        public float spawnInterval = 2f;
        [Tooltip("Random scale range for this wave's giants.")]
        public Vector2 scaleRange = new Vector2(1f, 1f);
        [Tooltip("Never let more than this many be alive at once. 0 = no cap.")]
        public int maxConcurrent = 0;
    }

    /// <summary>
    /// Spawns waves of giants on a ring around the city and keeps count of who is still alive.
    /// Pools everything, so a long endless run never instantiates after the first wave or two.
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
        [Header("References")]
        public Giant giantPrefab;
        [Tooltip("What the giants fly towards - normally the player rig.")]
        public Transform target;

        [Header("Spawn ring")]
        public float spawnRadius = 220f;
        public float spawnRadiusJitter = 40f;
        public float spawnHeight = 60f;
        public float spawnHeightJitter = 25f;
        [Tooltip("Half-angle of the arc giants spawn in, measured from the target's forward. 180 = all around.")]
        [Range(10f, 180f)] public float spawnArc = 110f;

        [Header("Pooling")]
        public int prewarm = 4;

        public int AliveCount { get; private set; }
        public bool IsSpawning { get; private set; }

        /// <summary>Raised whenever a giant is shot down. Argument is the giant.</summary>
        public event Action<Giant> GiantKilled;

        SimplePool<Giant> _pool;
        Transform _root;
        readonly List<Giant> _alive = new List<Giant>();

        public IReadOnlyList<Giant> AliveGiants => _alive;

        void Awake()
        {
            _root = new GameObject("GiantPool").transform;
            _root.SetParent(transform, false);

            if (giantPrefab != null)
                _pool = new SimplePool<Giant>(giantPrefab, _root, prewarm);
            else
                Debug.LogError("[WaveSpawner] No giant prefab assigned - nothing will spawn.", this);
        }

        public IEnumerator SpawnWave(Wave wave)
        {
            if (_pool == null || wave == null) yield break;

            IsSpawning = true;

            for (int i = 0; i < wave.count; i++)
            {
                if (wave.maxConcurrent > 0)
                {
                    while (AliveCount >= wave.maxConcurrent) yield return null;
                }

                SpawnOne(wave);

                if (i < wave.count - 1)
                    yield return new WaitForSeconds(wave.spawnInterval);
            }

            IsSpawning = false;
        }

        void SpawnOne(Wave wave)
        {
            Vector3 position = PickSpawnPoint();
            Quaternion rotation = target != null
                ? Quaternion.LookRotation(Vector3.ProjectOnPlane(target.position - position, Vector3.up).normalized, Vector3.up)
                : Quaternion.identity;

            Giant giant = _pool.Get(position, rotation);
            giant.Died += HandleGiantDied;
            giant.Finished += HandleGiantFinished;

            float scale = UnityEngine.Random.Range(wave.scaleRange.x, wave.scaleRange.y);
            giant.Spawn(target, wave.healthMultiplier, wave.speedMultiplier, scale);

            _alive.Add(giant);
            AliveCount = _alive.Count;
        }

        Vector3 PickSpawnPoint()
        {
            Vector3 center = target != null ? target.position : transform.position;
            Vector3 forward = target != null ? target.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            float angle = UnityEngine.Random.Range(-spawnArc, spawnArc);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;

            float radius = spawnRadius + UnityEngine.Random.Range(-spawnRadiusJitter, spawnRadiusJitter);
            float height = spawnHeight + UnityEngine.Random.Range(-spawnHeightJitter, spawnHeightJitter);

            return center + direction * radius + Vector3.up * height;
        }

        void HandleGiantDied(Giant giant)
        {
            if (_alive.Remove(giant))
                AliveCount = _alive.Count;

            if (GiantKilled != null) GiantKilled(giant);
        }

        void HandleGiantFinished(Giant giant)
        {
            giant.Died -= HandleGiantDied;
            giant.Finished -= HandleGiantFinished;

            if (_alive.Remove(giant)) AliveCount = _alive.Count;
            _pool.Release(giant);
        }

        /// <summary>Clear the field, e.g. on restart.</summary>
        public void DespawnAll()
        {
            StopAllCoroutines();
            IsSpawning = false;

            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                Giant giant = _alive[i];
                if (giant == null) continue;
                giant.Died -= HandleGiantDied;
                giant.Finished -= HandleGiantFinished;
            }

            _alive.Clear();
            AliveCount = 0;
            if (_pool != null) _pool.ReleaseAll();
        }

        void OnDrawGizmosSelected()
        {
            Vector3 center = target != null ? target.position : transform.position;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(center + Vector3.up * spawnHeight, spawnRadius);
        }
    }
}
