using UnityEngine;
using UnityEngine.EventSystems;

namespace GiantWanted
{
    /// <summary>
    /// Stationary turret-style aiming. Drag anywhere on screen (mouse or touch) to look
    /// around; the fire button is handled separately so you can hold it with the other thumb.
    ///
    /// Hierarchy this expects:
    ///   Player        (yaw pivot)  &lt;- this component
    ///     CameraPivot (pitch pivot)
    ///       Main Camera
    /// </summary>
    public class PlayerAim : MonoBehaviour
    {
        [Header("Pivots")]
        public Transform yawPivot;
        public Transform pitchPivot;
        public Camera cam;

        [Header("Sensitivity")]
        [Tooltip("Degrees of rotation per pixel of drag.")]
        public float dragSensitivity = 0.16f;
        [Tooltip("Extra multiplier applied while zoomed, so aiming stays precise.")]
        public float zoomSensitivityScale = 0.45f;
        public float smoothing = 18f;

        [Header("Limits")]
        public float minPitch = -35f;
        public float maxPitch = 70f;
        [Tooltip("Max degrees the player may turn left/right from the starting facing. 180 = free look.")]
        [Range(10f, 180f)] public float yawLimit = 180f;

        [Header("Zoom")]
        public float normalFov = 62f;
        public float zoomedFov = 32f;
        public float zoomSpeed = 10f;

        [Header("Recoil")]
        [Tooltip("How fast the view settles back after a shot.")]
        public float recoilRecovery = 9f;
        public float recoilSnappiness = 24f;

        [Header("Aim assist")]
        [Tooltip("Bullets bend towards a giant inside this cone (degrees). 0 disables assist.")]
        public float assistAngle = 3.5f;
        public float assistRange = 500f;

        public bool IsZoomed { get; private set; }
        /// <summary>Where the weapon should shoot, including aim assist.</summary>
        public Vector3 AimPoint { get; private set; }
        /// <summary>Normalised direction from the muzzle-ish camera to the aim point.</summary>
        public Vector3 AimDirection { get; private set; }
        /// <summary>True when a giant is inside the assist cone - used to tint the crosshair.</summary>
        public bool HasTarget { get; private set; }

        float _yaw, _pitch;
        float _yawHome;
        float _targetYaw, _targetPitch;
        Vector3 _recoil, _recoilVelocity;

        int _activeFinger = -1;
        Vector2 _lastPointer;
        bool _dragging;

        void Awake()
        {
            if (yawPivot == null) yawPivot = transform;
            if (cam == null) cam = GetComponentInChildren<Camera>();
            if (pitchPivot == null && cam != null) pitchPivot = cam.transform.parent;

            _yawHome = yawPivot.eulerAngles.y;
            _yaw = _targetYaw = 0f;
            _pitch = _targetPitch = pitchPivot != null ? NormalizePitch(pitchPivot.localEulerAngles.x) : 0f;

            if (cam != null) cam.fieldOfView = normalFov;
        }

        static float NormalizePitch(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        void Update()
        {
            ReadDrag();
            ApplyRotation();
            ApplyZoom();
            UpdateAimDirection();
        }

        // ----- input ------------------------------------------------------------

        void ReadDrag()
        {
            if (Input.touchCount > 0) ReadTouch();
            else ReadMouse();
        }

        void ReadTouch()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);

                if (_activeFinger == -1)
                {
                    // Claim the first finger that starts on empty screen (not on a button).
                    if (touch.phase == TouchPhase.Began && !IsPointerOverUI(touch.fingerId))
                    {
                        _activeFinger = touch.fingerId;
                        _lastPointer = touch.position;
                        _dragging = true;
                    }
                    continue;
                }

                if (touch.fingerId != _activeFinger) continue;

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    _activeFinger = -1;
                    _dragging = false;
                }
                else if (_dragging)
                {
                    AddLook(touch.position - _lastPointer);
                    _lastPointer = touch.position;
                }
            }
        }

        void ReadMouse()
        {
            _activeFinger = -1;

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUI(-1))
            {
                _dragging = true;
                _lastPointer = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                _dragging = false;
            }

            if (_dragging && Input.GetMouseButton(0))
            {
                Vector2 now = Input.mousePosition;
                AddLook(now - _lastPointer);
                _lastPointer = now;
            }
        }

        static bool IsPointerOverUI(int fingerId)
        {
            if (EventSystem.current == null) return false;
            return fingerId >= 0
                ? EventSystem.current.IsPointerOverGameObject(fingerId)
                : EventSystem.current.IsPointerOverGameObject();
        }

        void AddLook(Vector2 delta)
        {
            float sensitivity = dragSensitivity * (IsZoomed ? zoomSensitivityScale : 1f);

            _targetYaw += delta.x * sensitivity;
            _targetPitch -= delta.y * sensitivity;

            if (yawLimit < 180f) _targetYaw = Mathf.Clamp(_targetYaw, -yawLimit, yawLimit);
            _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
        }

        // ----- output -----------------------------------------------------------

        void ApplyRotation()
        {
            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            _yaw = Mathf.Lerp(_yaw, _targetYaw, k);
            _pitch = Mathf.Lerp(_pitch, _targetPitch, k);

            // Recoil is an additive offset that decays back to zero.
            _recoil = Vector3.SmoothDamp(_recoil, Vector3.zero, ref _recoilVelocity,
                                         1f / Mathf.Max(0.01f, recoilRecovery), Mathf.Infinity,
                                         Time.unscaledDeltaTime);

            yawPivot.rotation = Quaternion.Euler(0f, _yawHome + _yaw + _recoil.y, 0f);
            if (pitchPivot != null) pitchPivot.localRotation = Quaternion.Euler(_pitch + _recoil.x, 0f, _recoil.z);
        }

        void ApplyZoom()
        {
            if (cam == null) return;
            float wanted = IsZoomed ? zoomedFov : normalFov;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, wanted, zoomSpeed * Time.unscaledDeltaTime);
        }

        void UpdateAimDirection()
        {
            if (cam == null) return;

            Transform camT = cam.transform;
            AimDirection = camT.forward;
            AimPoint = camT.position + camT.forward * assistRange;
            HasTarget = false;

            if (assistAngle <= 0f) return;

            Giant best = FindAssistTarget(camT);
            if (best == null) return;

            HasTarget = true;
            AimPoint = best.Center;
            AimDirection = (AimPoint - camT.position).normalized;
        }

        Giant FindAssistTarget(Transform camT)
        {
            WaveSpawner spawner = GameManager.Instance != null ? GameManager.Instance.spawner : null;
            if (spawner == null) return null;

            Giant best = null;
            float bestAngle = assistAngle;

            var alive = spawner.AliveGiants;
            for (int i = 0; i < alive.Count; i++)
            {
                Giant giant = alive[i];
                if (giant == null || !giant.IsAlive) continue;

                Vector3 to = giant.Center - camT.position;
                if (to.sqrMagnitude > assistRange * assistRange) continue;

                float angle = Vector3.Angle(camT.forward, to);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = giant;
                }
            }

            return best;
        }

        /// <summary>Punch the view. x = up kick, y = horizontal kick, z = roll.</summary>
        public void AddRecoil(float pitchKick, float yawKick, float roll)
        {
            _recoil += new Vector3(-pitchKick, yawKick, roll);
            _recoilVelocity += new Vector3(-pitchKick, yawKick, roll) * recoilSnappiness * 0.01f;
        }

        public void SetZoom(bool zoomed)
        {
            IsZoomed = zoomed;
        }

        public void ToggleZoom()
        {
            IsZoomed = !IsZoomed;
        }

        /// <summary>Snap the view back to the starting facing (used on restart).</summary>
        public void ResetAim()
        {
            _yaw = _targetYaw = 0f;
            _pitch = _targetPitch = 0f;
            _recoil = Vector3.zero;
            _recoilVelocity = Vector3.zero;
            IsZoomed = false;
        }
    }
}
