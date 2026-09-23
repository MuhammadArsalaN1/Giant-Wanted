using System.Collections;
using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// Cinematic kill cam for the last giant of a wave.
    ///
    /// Once the final giant is one shot from death the cam arms itself. The next bullet
    /// fired gets its own camera, which chases it in slow motion all the way to the hit.
    /// When the giant dies the cam holds on the death animation for a beat, then hands
    /// control back to the gun camera and releases the wave flow so the next wave starts.
    ///
    /// Add this anywhere in the scene (GameSystems is a good home). Every reference is
    /// optional - anything left empty is resolved at Awake, and the camera itself is
    /// created at runtime if one is not assigned.
    /// </summary>
    /// <summary>How the kill cam sits relative to the bullet it is following.</summary>
    public enum KillCamFraming
    {
        /// <summary>Abeam the bullet, watching it cross the frame in profile.</summary>
        SideProfile,
        /// <summary>Behind and slightly off the bullet, riding along with it.</summary>
        ChaseBehind
    }

    public class BulletCam : MonoBehaviour
    {
        [Header("Wiring (auto-resolved when empty)")]
        public Camera bulletCamera;
        public Camera gunCamera;
        public WeaponController weapon;
        public GameManager game;
        public WaveSpawner spawner;
        public HUD hud;

        [Header("When to arm")]
        [Tooltip("Only fire the cam for the final giant of a wave, once nothing else is spawning.")]
        public bool onlyOnLastGiantOfWave = true;
        [Tooltip("Arm once the giant's remaining health is at or below one shot of weapon damage.")]
        public bool useWeaponDamageAsThreshold = true;
        [Tooltip("Used instead when the option above is off: fraction of max health.")]
        [Range(0.01f, 1f)] public float healthFractionThreshold = 0.12f;

        [Header("Framing (all distances are multiples of the giant's height)")]
        [Tooltip("SideProfile watches the bullet cross the frame from the side. " +
                 "ChaseBehind rides along behind it.")]
        public KillCamFraming framing = KillCamFraming.SideProfile;

        [Header("Side profile")]
        [Tooltip("How far out to the side of the bullet's path the camera sits. " +
                 "The tracer and the bullet scale with this, so it stays readable however wide you go.")]
        public float sideDistance = 3.5f;
        [Tooltip("How far above the bullet.")]
        public float sideHeight = 0.5f;
        [Tooltip("How far behind the bullet along its travel. 0 = perfectly abeam.")]
        public float sideLag = 0.3f;
        [Tooltip("Put the camera on the left of the bullet's path instead of the right.")]
        public bool cameraOnLeft;
        [Tooltip("0 looks straight at the bullet, 1 looks straight at the giant.")]
        [Range(0f, 1f)] public float sideLookBias = 0.15f;

        [Header("Chase behind")]
        public float followDistance = 0.28f;
        public float sideOffset = 0.11f;
        public float heightOffset = 0.06f;
        [Tooltip("0 looks straight at the bullet, 1 looks straight at the giant.")]
        [Range(0f, 1f)] public float lookAheadBias = 0.38f;

        [Header("Camera feel")]
        public float positionSmoothing = 16f;
        public float rotationSmoothing = 12f;
        public float fieldOfView = 45f;
        [Tooltip("Distance to pull back to while holding on the dying giant, so it fits in frame. 0 = stay put.")]
        public float deathDistance = 1.6f;
        [Tooltip("Degrees per second the camera orbits the dying giant, so the held shot is not frozen.")]
        public float deathOrbitSpeed = 10f;

        [Header("Timing")]
        [Tooltip("Retune the followed bullet so the chase always lasts 'flightSeconds', " +
                 "however fast the weapon normally shoots.")]
        public bool slowTheBullet = true;
        [Tooltip("Real seconds the hero bullet takes to reach the giant. Raise it to make the shot even easier to read.")]
        public float flightSeconds = 2.5f;
        [Tooltip("Time scale while the bullet is in flight. This slows the world, not the bullet - " +
                 "the bullet is governed by 'flightSeconds'.")]
        [Range(0.05f, 1f)] public float flightTimeScale = 0.25f;
        [Tooltip("Time scale while the death animation plays. 1 = as authored.")]
        [Range(0.05f, 1f)] public float deathTimeScale = 1f;
        [Tooltip("Seconds of death animation to show before cutting back.")]
        public float deathHoldSeconds = 1f;
        [Tooltip("Hard limit so a missed shot can never strand the cam.")]
        public float maxFollowSeconds = 6f;
        [Tooltip("Quiet period after a shot that did not land, so misses do not strobe the cam.")]
        public float retryCooldown = 0.7f;

        [Header("Tracer visibility")]
        [Tooltip("Fatten and lengthen the hero bullet's trail so it still reads from the kill cam's stand-off.")]
        public bool boostTrail = true;
        [Tooltip("Trail length as a fraction of the whole flight. 0.35 = the streak spans a third of the way to the giant.")]
        [Range(0.02f, 1f)] public float trailLengthFraction = 0.35f;
        [Tooltip("Trail width as a fraction of how far the camera is standing off. " +
                 "Sizing it this way keeps the streak the same thickness on screen at any distance.")]
        public float trailWidthFraction = 0.035f;
        [Tooltip("Bullet length as a fraction of the camera stand-off. The bullet is only ever " +
                 "scaled up, never below its authored size. 0 disables it.")]
        public float bulletSizeFraction = 0.03f;

        [Header("Presentation")]
        public bool hideHudDuringCam = true;
        [Tooltip("Layers the kill cam must not render - normally the first-person gun.")]
        public LayerMask hideLayers;

        public bool IsRunning { get; private set; }

        Transform _camTransform;
        Giant _armed;
        bool _bulletEnded;
        bool _createdCamera;
        float _nextArmTime;

        Vector3 _deathBaseDirection;
        float _deathOrbitAngle;
        bool _deathFramed;

        Projectile _overriddenBullet;
        TrailRenderer _overriddenTrail;
        float _savedLifetime;
        float _savedTrailTime;
        float _savedTrailWidth;
        Vector3 _savedBulletScale;
        bool _scaledBullet;

        // ----------------------------------------------------------------- setup

        void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (weapon == null) weapon = FindFirstObjectByType<WeaponController>();
            if (hud == null) hud = FindFirstObjectByType<HUD>();
            if (spawner == null && game != null) spawner = game.spawner;
            if (spawner == null) spawner = FindFirstObjectByType<WaveSpawner>();

            if (gunCamera == null && weapon != null && weapon.aim != null) gunCamera = weapon.aim.cam;
            if (gunCamera == null) gunCamera = Camera.main;

            EnsureCamera();
        }

        void EnsureCamera()
        {
            if (bulletCamera == null)
            {
                GameObject go = new GameObject("BulletCam (runtime)");
                bulletCamera = go.AddComponent<Camera>();
                if (gunCamera != null) bulletCamera.CopyFrom(gunCamera);
                _createdCamera = true;
            }

            bulletCamera.enabled = false;
            bulletCamera.fieldOfView = fieldOfView;
            bulletCamera.cullingMask &= ~hideLayers.value;

            // Tagging it lets Camera.main - and therefore damage popups - follow the cut.
            if (bulletCamera.CompareTag("Untagged")) bulletCamera.tag = "MainCamera";

            _camTransform = bulletCamera.transform;
        }

        void OnEnable()
        {
            if (weapon != null) weapon.BulletSpawned += OnBulletSpawned;
        }

        void OnDisable()
        {
            if (weapon != null) weapon.BulletSpawned -= OnBulletSpawned;

            if (IsRunning)
            {
                StopAllCoroutines();
                Finish(false);
            }
        }

        void OnDestroy()
        {
            if (_createdCamera && bulletCamera != null) Destroy(bulletCamera.gameObject);
        }

        // ----------------------------------------------------------------- arming

        void Update()
        {
            if (IsRunning) return;
            if (Time.unscaledTime < _nextArmTime) { _armed = null; return; }

            _armed = FindArmedTarget();
        }

        Giant FindArmedTarget()
        {
            if (game == null || spawner == null || weapon == null) return null;
            if (game.State != GameState.Playing) return null;

            if (onlyOnLastGiantOfWave && (spawner.IsSpawning || spawner.AliveCount != 1)) return null;

            var alive = spawner.AliveGiants;
            for (int i = 0; i < alive.Count; i++)
            {
                Giant giant = alive[i];
                if (giant == null || !giant.IsAlive) continue;

                float remaining = giant.HealthNormalized * giant.maxHealth;
                float threshold = useWeaponDamageAsThreshold
                    ? weapon.damage
                    : giant.maxHealth * healthFractionThreshold;

                if (remaining <= threshold) return giant;
            }

            return null;
        }

        void OnBulletSpawned(Projectile bullet)
        {
            if (IsRunning || bullet == null) return;
            if (_armed == null || !_armed.IsAlive) return;

            StartCoroutine(Run(bullet, _armed));
        }

        // -------------------------------------------------------------- sequence

        IEnumerator Run(Projectile bullet, Giant target)
        {
            IsRunning = true;
            _armed = null;
            _bulletEnded = false;
            _deathFramed = false;

            // Hold the wave flow so the next wave cannot start behind the cinematic.
            if (game != null) game.CinematicHold = true;
            if (hud != null && hideHudDuringCam) hud.SetCinematic(true);

            // Stop the player from landing a faster follow-up shot that would kill the
            // giant before the hero bullet ever arrives.
            if (weapon != null) weapon.FiringBlocked = true;

            bullet.Ended += OnBulletEnded;

            float scale = MeasureHeight(target);
            Activate();
            FrameBullet(bullet.transform, target, scale, true);

            SetTimeScale(flightTimeScale);

            OverrideBullet(bullet, target, scale);

            bool targetDied = false;
            float elapsed = 0f;
            float limit = Mathf.Max(maxFollowSeconds, flightSeconds + 2f);

            while (elapsed < limit)
            {
                elapsed += Time.unscaledDeltaTime;

                bool bulletLive = !_bulletEnded && bullet != null && bullet.isActiveAndEnabled;
                if (bulletLive) FrameBullet(bullet.transform, target, scale, false);
                else FrameTarget(target, scale);

                if (target == null || !target.IsAlive) { targetDied = true; break; }
                if (game != null && game.State != GameState.Playing) break;
                if (_bulletEnded) break;   // shot missed or did not finish the job

                yield return null;
            }

            bullet.Ended -= OnBulletEnded;
            RestoreBullet();

            if (targetDied)
            {
                // Let the death animation read at full speed before cutting away.
                SetTimeScale(deathTimeScale);

                float hold = 0f;
                while (hold < deathHoldSeconds)
                {
                    hold += Time.unscaledDeltaTime;
                    FrameTarget(target, scale);
                    yield return null;
                }
            }

            Finish(targetDied);
        }

        /// <summary>
        /// Retime the hero bullet so the trip to the giant takes <see cref="flightSeconds"/> of
        /// real time, and fatten its tracer so it still reads from the cam's stand-off. The
        /// bullet and the trail both advance on scaled time, so the world slow-down has to be
        /// divided back out - otherwise the two settings fight each other.
        /// </summary>
        void OverrideBullet(Projectile bullet, Giant target, float scale)
        {
            _overriddenBullet = bullet;
            _savedLifetime = bullet.maxLifetime;

            _overriddenTrail = bullet.GetComponent<TrailRenderer>();
            float authoredTrailWidth = 0f;
            if (_overriddenTrail != null)
            {
                _savedTrailTime = _overriddenTrail.time;
                _savedTrailWidth = _overriddenTrail.widthMultiplier;
                authoredTrailWidth = _overriddenTrail.startWidth;
            }

            Vector3 aimPoint = target != null ? target.Center : bullet.transform.position + bullet.transform.forward;
            float distance = Vector3.Distance(bullet.transform.position, aimPoint);
            if (distance < 0.01f) return;

            // How far the camera sits from the bullet. Everything the viewer has to see is
            // sized against this, so pulling the shot wider never loses the tracer.
            float standOff = Mathf.Max(0.01f, scale * (framing == KillCamFraming.SideProfile
                ? sideDistance
                : followDistance));

            // How long the flight lasts in the scaled clock that the bullet and trail both use.
            float scaledFlight = slowTheBullet
                ? Mathf.Max(0.05f, flightSeconds * Mathf.Max(0.01f, flightTimeScale))
                : distance / Mathf.Max(0.01f, bullet.speed);

            if (slowTheBullet)
            {
                bullet.SetSpeed(distance / scaledFlight);
                // The prefab's lifetime is tuned for a fast bullet; give this one room to arrive.
                bullet.maxLifetime = Mathf.Max(_savedLifetime, scaledFlight * 2f);
            }

            if (boostTrail && _overriddenTrail != null && authoredTrailWidth > 0.0001f)
            {
                // Length off the flight time, width off the stand-off: the streak keeps the
                // same share of the screen whatever the world scale or the camera distance.
                _overriddenTrail.time = Mathf.Max(_savedTrailTime, scaledFlight * trailLengthFraction);

                float wantedWidth = standOff * trailWidthFraction;
                if (wantedWidth > authoredTrailWidth)
                    _overriddenTrail.widthMultiplier = _savedTrailWidth * (wantedWidth / authoredTrailWidth);

                _overriddenTrail.Clear();
            }

            if (bulletSizeFraction > 0f)
            {
                float length = MeasureLength(bullet);
                float wantedLength = standOff * bulletSizeFraction;

                if (length > 0.0001f && wantedLength > length)
                {
                    _savedBulletScale = bullet.transform.localScale;
                    _scaledBullet = true;
                    bullet.transform.localScale = _savedBulletScale * (wantedLength / length);
                }
            }
        }

        /// <summary>Longest world-space dimension of the bullet mesh, ignoring its trail.</summary>
        static float MeasureLength(Projectile bullet)
        {
            Bounds bounds = default;
            bool any = false;

            foreach (Renderer renderer in bullet.GetComponentsInChildren<Renderer>())
            {
                if (renderer is TrailRenderer || renderer is ParticleSystemRenderer) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!any) return 0f;

            Vector3 size = bounds.size;
            return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        }

        /// <summary>Put the pooled bullet back how it was found, so normal shots are unaffected.</summary>
        void RestoreBullet()
        {
            if (_overriddenBullet != null)
            {
                _overriddenBullet.maxLifetime = _savedLifetime;
                if (_scaledBullet) _overriddenBullet.transform.localScale = _savedBulletScale;
            }

            if (_overriddenTrail != null)
            {
                _overriddenTrail.time = _savedTrailTime;
                _overriddenTrail.widthMultiplier = _savedTrailWidth;
            }

            _overriddenBullet = null;
            _overriddenTrail = null;
            _scaledBullet = false;
        }

        void OnBulletEnded(Projectile bullet)
        {
            _bulletEnded = true;
        }

        void Activate()
        {
            if (gunCamera != null) gunCamera.enabled = false;
            if (bulletCamera != null)
            {
                bulletCamera.fieldOfView = fieldOfView;
                bulletCamera.enabled = true;
            }
        }

        void Finish(bool killed)
        {
            // No-op if the flight loop already restored it; matters on an aborted cam.
            RestoreBullet();

            if (bulletCamera != null) bulletCamera.enabled = false;
            if (gunCamera != null) gunCamera.enabled = true;

            SetTimeScale(1f);

            if (weapon != null) weapon.FiringBlocked = false;

            if (hud != null) hud.SetCinematic(false);
            if (game != null) game.CinematicHold = false;

            IsRunning = false;
            _armed = null;
            _bulletEnded = false;
            _deathFramed = false;

            // A shot that missed should not immediately re-trigger the cut.
            if (!killed) _nextArmTime = Time.unscaledTime + retryCooldown;
        }

        static void SetTimeScale(float value)
        {
            Time.timeScale = value;
            Time.fixedDeltaTime = 0.02f * value;
        }

        // -------------------------------------------------------------- framing

        void FrameBullet(Transform bullet, Giant target, float scale, bool snap)
        {
            Vector3 forward = bullet.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            right = right.sqrMagnitude > 0.001f ? right.normalized : bullet.right;

            Vector3 desired;
            float bias;

            if (framing == KillCamFraming.SideProfile)
            {
                // Stand off to one side of the flight path: the bullet holds frame while
                // the world streaks past behind it.
                float side = cameraOnLeft ? -1f : 1f;
                desired = bullet.position
                          + right * (side * sideDistance * scale)
                          - forward * (sideLag * scale)
                          + Vector3.up * (sideHeight * scale);
                bias = sideLookBias;
            }
            else
            {
                desired = bullet.position
                          - forward * (followDistance * scale)
                          + right * (sideOffset * scale)
                          + Vector3.up * (heightOffset * scale);
                bias = lookAheadBias;
            }

            Vector3 lookAt = target != null
                ? Vector3.Lerp(bullet.position, target.Center, bias)
                : bullet.position + forward * scale;

            Apply(desired, lookAt, snap);
        }

        void FrameTarget(Giant target, float scale)
        {
            if (target == null) return;

            Vector3 center = target.Center;

            // Lock the viewing angle the moment the hold starts, then orbit from there.
            if (!_deathFramed)
            {
                _deathBaseDirection = _camTransform.position - center;
                if (_deathBaseDirection.sqrMagnitude < 0.0001f) _deathBaseDirection = -_camTransform.forward;
                _deathBaseDirection.Normalize();
                _deathOrbitAngle = 0f;
                _deathFramed = true;
            }

            _deathOrbitAngle += deathOrbitSpeed * Time.unscaledDeltaTime;
            Vector3 direction = Quaternion.AngleAxis(_deathOrbitAngle, Vector3.up) * _deathBaseDirection;

            // Pull back far enough that the whole giant reads, instead of sitting inside it.
            float distance = deathDistance > 0f
                ? deathDistance * scale
                : Vector3.Distance(_camTransform.position, center);

            Apply(center + direction * distance, center, false);
        }

        void Apply(Vector3 position, Vector3 lookAt, bool snap)
        {
            Vector3 toTarget = lookAt - position;
            Quaternion rotation = toTarget.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(toTarget.normalized, Vector3.up)
                : _camTransform.rotation;

            if (snap)
            {
                _camTransform.SetPositionAndRotation(position, rotation);
                return;
            }

            float dt = Time.unscaledDeltaTime;
            _camTransform.position = Vector3.Lerp(_camTransform.position, position,
                                                  1f - Mathf.Exp(-positionSmoothing * dt));
            _camTransform.rotation = Quaternion.Slerp(_camTransform.rotation, rotation,
                                                     1f - Mathf.Exp(-rotationSmoothing * dt));
        }

        /// <summary>World height of a giant, used to keep the framing scale-independent.</summary>
        static float MeasureHeight(Giant giant)
        {
            if (giant == null) return 10f;

            Bounds bounds = default;
            bool any = false;

            foreach (Renderer renderer in giant.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return any ? Mathf.Max(0.1f, bounds.size.y) : 10f;
        }
    }
}
