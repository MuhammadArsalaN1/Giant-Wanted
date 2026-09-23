using UnityEngine;
using UnityEngine.UI;

namespace GiantWanted
{
    /// <summary>
    /// Billboarded health bar floating above a giant. Stays hidden until the giant is hit,
    /// so a skyline full of untouched enemies is not covered in bars.
    /// </summary>
    public class WorldHealthBar : MonoBehaviour
    {
        public Image fill;
        public CanvasGroup group;
        public Transform anchor;

        [Header("Placement")]
        public Vector3 worldOffset = new Vector3(0f, 3f, 0f);
        [Tooltip("Distance at which the bar renders at its authored size. Further away it scales up so it stays readable.")]
        public float referenceDistance = 60f;
        public float minScale = 0.4f;
        public float maxScale = 8f;

        [Header("Behaviour")]
        public bool hideUntilDamaged = true;
        public float fadeSpeed = 6f;

        Camera _camera;
        float _target = 1f;
        float _displayed = 1f;
        bool _visible;
        Vector3 _baseScale;
        bool _captured;

        void Awake()
        {
            if (!_captured)
            {
                _baseScale = transform.localScale;
                _captured = true;
            }
        }

        void OnEnable()
        {
            _camera = Camera.main;
            ResetBar();
        }

        public void ResetBar()
        {
            _target = 1f;
            _displayed = 1f;
            _visible = !hideUntilDamaged;
            if (fill != null) fill.fillAmount = 1f;
            if (group != null) group.alpha = _visible ? 1f : 0f;
        }

        public void SetHealth(float normalized)
        {
            _target = Mathf.Clamp01(normalized);
            _visible = true;
        }

        public void Hide()
        {
            _visible = false;
        }

        void LateUpdate()
        {
            // Re-read every frame so bars turn to face the kill cam when it cuts in.
            Camera active = Camera.main;
            if (active != null) _camera = active;

            if (anchor != null) transform.position = anchor.position + worldOffset;

            if (_camera != null)
            {
                transform.rotation = _camera.transform.rotation;

                // Constant apparent size: bars on distant giants stay legible.
                float distance = Vector3.Distance(transform.position, _camera.transform.position);
                float scale = Mathf.Clamp(distance / Mathf.Max(0.01f, referenceDistance), minScale, maxScale);
                transform.localScale = _baseScale * scale;
            }

            _displayed = Mathf.MoveTowards(_displayed, _target, Time.deltaTime * 1.5f);
            if (fill != null) fill.fillAmount = _displayed;

            if (group != null)
                group.alpha = Mathf.MoveTowards(group.alpha, _visible ? 1f : 0f, fadeSpeed * Time.deltaTime);
        }
    }
}
