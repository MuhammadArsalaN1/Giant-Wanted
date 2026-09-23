using System.Collections;
using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// One place to ask for a short-lived effect. Everything is pooled so sustained fire
    /// never allocates. All prefab fields are optional - a missing prefab is simply skipped.
    /// </summary>
    public class FxPool : MonoBehaviour
    {
        public static FxPool Instance { get; private set; }

        [Header("Prefabs (optional)")]
        public ParticleSystem impactPrefab;
        public ParticleSystem bloodPrefab;
        public DamagePopup popupPrefab;

        [Header("Pool sizes")]
        public int impactPrewarm = 8;
        public int bloodPrewarm = 8;
        public int popupPrewarm = 12;

        SimplePool<ParticleSystem> _impacts;
        SimplePool<ParticleSystem> _blood;
        SimplePool<DamagePopup> _popups;
        Transform _root;

        void Awake()
        {
            Instance = this;
            _root = new GameObject("FxRoot").transform;
            _root.SetParent(transform, false);

            if (impactPrefab != null) _impacts = new SimplePool<ParticleSystem>(impactPrefab, _root, impactPrewarm);
            if (bloodPrefab != null) _blood = new SimplePool<ParticleSystem>(bloodPrefab, _root, bloodPrewarm);
            if (popupPrefab != null) _popups = new SimplePool<DamagePopup>(popupPrefab, _root, popupPrewarm);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SpawnImpact(Vector3 position, Vector3 normal)
        {
            Play(_impacts, position, normal);
        }

        public void SpawnBlood(Vector3 position, Vector3 normal)
        {
            Play(_blood, position, normal);
        }

        void Play(SimplePool<ParticleSystem> pool, Vector3 position, Vector3 normal)
        {
            if (pool == null) return;

            Quaternion rotation = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            ParticleSystem ps = pool.Get(position, rotation);
            ps.Clear(true);
            ps.Play(true);

            float life = ps.main.duration + ps.main.startLifetime.constantMax;
            StartCoroutine(ReleaseAfter(pool, ps, life));
        }

        IEnumerator ReleaseAfter(SimplePool<ParticleSystem> pool, ParticleSystem ps, float delay)
        {
            yield return new WaitForSeconds(delay);
            pool.Release(ps);
        }

        /// <summary>Floating damage number at a world position.</summary>
        public void SpawnPopup(Vector3 position, float amount, bool critical)
        {
            if (_popups == null) return;
            DamagePopup popup = _popups.Get(position, Quaternion.identity);
            popup.Show(Mathf.RoundToInt(amount), critical, ReleasePopup);
        }

        void ReleasePopup(DamagePopup popup)
        {
            _popups.Release(popup);
        }
    }
}
