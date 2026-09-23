using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GiantWanted
{
    /// <summary>
    /// World-space damage number that drifts up, fades out and returns itself to the pool.
    /// Uses a world-space Canvas plus legacy Text, so it needs no font asset import.
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        public Text label;
        public CanvasGroup group;

        [Header("Motion")]
        public float lifetime = 0.8f;
        public float riseSpeed = 2.5f;
        public float randomSpread = 0.6f;

        [Header("Colours")]
        public Color normalColor = new Color(1f, 0.95f, 0.75f);
        public Color criticalColor = new Color(1f, 0.35f, 0.2f);
        public float criticalScale = 1.5f;

        [Header("Distance scaling")]
        [Tooltip("Distance at which the popup renders at its authored size.")]
        public float referenceDistance = 40f;
        public float minScale = 0.5f;
        public float maxScale = 10f;

        Camera _camera;
        Vector3 _drift;
        Action<DamagePopup> _release;
        Vector3 _authoredScale;
        bool _captured;

        void Awake()
        {
            if (!_captured)
            {
                _authoredScale = transform.localScale;
                _captured = true;
            }
        }

        public void Show(int amount, bool critical, Action<DamagePopup> release)
        {
            _release = release;
            _camera = Camera.main;

            if (label != null)
            {
                label.text = critical ? amount + "!" : amount.ToString();
                label.color = critical ? criticalColor : normalColor;
            }

            // Keep a constant apparent size no matter how far away the giant is.
            float distanceFactor = 1f;
            if (_camera != null)
            {
                float distance = Vector3.Distance(transform.position, _camera.transform.position);
                distanceFactor = Mathf.Clamp(distance / Mathf.Max(0.01f, referenceDistance), minScale, maxScale);
            }

            transform.localScale = _authoredScale * ((critical ? criticalScale : 1f) * distanceFactor);
            _drift = new Vector3(
                UnityEngine.Random.Range(-randomSpread, randomSpread),
                riseSpeed,
                UnityEngine.Random.Range(-randomSpread, randomSpread));

            StopAllCoroutines();
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            float t = 0f;
            Vector3 scale = transform.localScale;

            while (t < lifetime)
            {
                t += Time.deltaTime;
                float k = t / lifetime;

                transform.position += _drift * Time.deltaTime;
                if (_camera != null) transform.rotation = _camera.transform.rotation;
                transform.localScale = scale * (1f + 0.25f * Mathf.Sin(k * Mathf.PI));
                if (group != null) group.alpha = 1f - k * k;

                yield return null;
            }

            if (_release != null) _release(this);
            else gameObject.SetActive(false);
        }
    }
}
