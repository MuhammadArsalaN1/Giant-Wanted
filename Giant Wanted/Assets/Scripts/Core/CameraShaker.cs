using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// Additive positional/rotational noise on the camera itself. The camera is a child of
    /// the pitch pivot, so shaking its local transform never fights the aim controller.
    /// </summary>
    public class CameraShaker : MonoBehaviour
    {
        public static CameraShaker Instance { get; private set; }

        [Tooltip("How quickly a shake dies down. Higher = snappier.")]
        public float decay = 4f;
        public float positionStrength = 0.06f;
        public float rotationStrength = 1.4f;
        public float frequency = 22f;

        float _trauma;
        float _seed;

        void Awake()
        {
            Instance = this;
            _seed = Random.value * 100f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Add shake. 0..1, accumulates and clamps.</summary>
        public void Shake(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        void LateUpdate()
        {
            if (_trauma <= 0f)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                return;
            }

            // Squaring makes small shakes subtle and big ones punchy.
            float power = _trauma * _trauma;
            float t = Time.unscaledTime * frequency;

            Vector3 offset = new Vector3(
                Mathf.PerlinNoise(_seed, t) - 0.5f,
                Mathf.PerlinNoise(_seed + 11f, t) - 0.5f,
                Mathf.PerlinNoise(_seed + 23f, t) - 0.5f) * (2f * power * positionStrength);

            Vector3 euler = new Vector3(
                Mathf.PerlinNoise(_seed + 31f, t) - 0.5f,
                Mathf.PerlinNoise(_seed + 43f, t) - 0.5f,
                Mathf.PerlinNoise(_seed + 57f, t) - 0.5f) * (2f * power * rotationStrength);

            transform.localPosition = offset;
            transform.localRotation = Quaternion.Euler(euler);

            _trauma = Mathf.Max(0f, _trauma - decay * Time.unscaledDeltaTime);
        }
    }
}
