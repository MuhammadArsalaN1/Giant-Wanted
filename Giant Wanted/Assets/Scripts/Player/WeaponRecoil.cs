using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// Procedural gun kick and idle sway, applied on top of whatever pose the gun was
    /// authored with. Put this on the gun transform itself.
    /// </summary>
    public class WeaponRecoil : MonoBehaviour
    {
        [Header("Kick")]
        public float kickBack = 0.07f;
        public float kickUp = 0.02f;
        public float kickRotation = 7f;
        public float returnSpeed = 14f;
        public float snappiness = 20f;

        [Header("Idle sway")]
        public float swayAmount = 0.006f;
        public float swaySpeed = 1.4f;

        Vector3 _homePosition;
        Quaternion _homeRotation;

        Vector3 _positionOffset, _positionTarget;
        Vector3 _rotationOffset, _rotationTarget;

        void Awake()
        {
            _homePosition = transform.localPosition;
            _homeRotation = transform.localRotation;
        }

        public void Kick()
        {
            _positionTarget += new Vector3(0f, kickUp, -kickBack);
            _rotationTarget += new Vector3(
                -kickRotation,
                Random.Range(-kickRotation, kickRotation) * 0.3f,
                Random.Range(-kickRotation, kickRotation) * 0.25f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            _positionTarget = Vector3.Lerp(_positionTarget, Vector3.zero, returnSpeed * dt);
            _positionOffset = Vector3.Lerp(_positionOffset, _positionTarget, snappiness * dt);

            _rotationTarget = Vector3.Lerp(_rotationTarget, Vector3.zero, returnSpeed * dt);
            _rotationOffset = Vector3.Lerp(_rotationOffset, _rotationTarget, snappiness * dt);

            float t = Time.time * swaySpeed;
            Vector3 sway = new Vector3(Mathf.Sin(t) * swayAmount, Mathf.Sin(t * 1.7f) * swayAmount, 0f);

            transform.localPosition = _homePosition + _positionOffset + sway;
            transform.localRotation = _homeRotation * Quaternion.Euler(_rotationOffset);
        }

        /// <summary>Call after moving the gun in the inspector at runtime.</summary>
        public void CaptureHome()
        {
            _homePosition = transform.localPosition;
            _homeRotation = transform.localRotation;
        }
    }
}
