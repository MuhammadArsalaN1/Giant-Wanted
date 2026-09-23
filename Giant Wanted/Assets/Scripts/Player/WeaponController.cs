using System;
using System.Collections;
using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// The gun. Pools tracers, handles fire rate, spread, magazine + reload, muzzle flash,
    /// recoil and camera shake. Firing is driven by <see cref="SetFireHeld"/> (the on-screen
    /// FIRE button) plus a couple of desktop keys for testing in the editor.
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Header("References")]
        public PlayerAim aim;
        [Tooltip("Tip of the barrel - tracers spawn here.")]
        public Transform muzzle;
        public Projectile bulletPrefab;
        public ParticleSystem muzzleFlash;
        public WeaponRecoil recoil;

        [Header("Ballistics")]
        public float damage = 28f;
        [Tooltip("Shots per second while the trigger is held.")]
        public float fireRate = 7f;
        public float bulletSpeed = 260f;
        public float range = 700f;
        [Tooltip("Cone half-angle in degrees added to every shot.")]
        public float spread = 0.8f;
        [Tooltip("Extra spread multiplier while not zoomed.")]
        public float hipFireSpreadScale = 2.2f;
        public LayerMask hitMask = ~0;

        [Header("Magazine")]
        public int magazineSize = 30;
        public float reloadTime = 1.5f;
        public bool autoReloadWhenEmpty = true;

        [Header("Feel")]
        public float recoilPitch = 1.6f;
        public float recoilYaw = 0.5f;
        public float recoilRoll = 0.6f;
        public float shakePerShot = 0.07f;

        [Header("Pooling")]
        public int bulletPrewarm = 24;

        public int Ammo { get; private set; }
        public bool IsReloading { get; private set; }
        public bool IsFiring { get; private set; }
        /// <summary>0..1 while reloading, 1 when idle.</summary>
        public float ReloadProgress { get; private set; } = 1f;
        /// <summary>Held by cinematics (the kill cam) so a follow-up shot cannot beat the hero bullet.</summary>
        public bool FiringBlocked { get; set; }

        public event Action<int, int> AmmoChanged;   // ammo, magazine size
        public event Action<bool> ReloadingChanged;
        public event Action Fired;
        /// <summary>Raised right after a tracer is launched, so cinematics can latch onto it.</summary>
        public event Action<Projectile> BulletSpawned;

        SimplePool<Projectile> _bullets;
        Transform _bulletRoot;
        float _nextShotTime;
        bool _fireHeld;

        void Awake()
        {
            if (aim == null) aim = GetComponentInParent<PlayerAim>();
            if (muzzle == null) muzzle = transform;

            _bulletRoot = new GameObject("BulletPool").transform;

            if (bulletPrefab != null)
                _bullets = new SimplePool<Projectile>(bulletPrefab, _bulletRoot, bulletPrewarm);
            else
                Debug.LogError("[WeaponController] No bullet prefab assigned.", this);

            Ammo = magazineSize;
        }

        void Start()
        {
            AmmoChanged?.Invoke(Ammo, magazineSize);
        }

        void Update()
        {
            // Desktop conveniences so the game is testable without touching the HUD.
            bool keyFire = Input.GetKey(KeyCode.Space) || Input.GetMouseButton(1);
            if (Input.GetKeyDown(KeyCode.R)) Reload();

            IsFiring = _fireHeld || keyFire;

            if (!CanShoot()) return;
            if (!IsFiring) return;

            if (Time.time >= _nextShotTime) Shoot();
        }

        bool CanShoot()
        {
            if (IsReloading || FiringBlocked) return false;
            if (_bullets == null) return false;

            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing &&
                GameManager.Instance.State != GameState.WaveCleared)
                return false;

            if (Ammo <= 0)
            {
                if (autoReloadWhenEmpty) Reload();
                return false;
            }

            return true;
        }

        void Shoot()
        {
            _nextShotTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);

            Ammo--;
            AmmoChanged?.Invoke(Ammo, magazineSize);

            Vector3 origin = muzzle.position;
            Vector3 direction = aim != null ? (aim.AimPoint - origin).normalized : muzzle.forward;
            direction = ApplySpread(direction);

            Projectile bullet = _bullets.Get(origin, Quaternion.LookRotation(direction));
            bullet.speed = bulletSpeed;
            bullet.maxDistance = range;
            bullet.hitMask = hitMask;
            bullet.Despawn = ReleaseBullet;
            bullet.Launch(origin, direction, damage);
            BulletSpawned?.Invoke(bullet);

            if (muzzleFlash != null)
            {
                muzzleFlash.Clear(true);
                muzzleFlash.Play(true);
            }

            if (aim != null)
            {
                float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                aim.AddRecoil(recoilPitch, recoilYaw * sign, recoilRoll * sign);
            }

            if (recoil != null) recoil.Kick();
            if (CameraShaker.Instance != null) CameraShaker.Instance.Shake(shakePerShot);

            Fired?.Invoke();

            if (Ammo <= 0 && autoReloadWhenEmpty) Reload();
        }

        Vector3 ApplySpread(Vector3 direction)
        {
            float cone = spread * (aim != null && aim.IsZoomed ? 1f : hipFireSpreadScale);
            if (cone <= 0f) return direction;

            // Random point inside a cone of half-angle `cone` around `direction`.
            Vector2 disc = UnityEngine.Random.insideUnitCircle * Mathf.Tan(cone * Mathf.Deg2Rad);
            Quaternion rotation = Quaternion.LookRotation(direction);
            Vector3 offset = rotation * new Vector3(disc.x, disc.y, 0f);
            return (direction + offset).normalized;
        }

        void ReleaseBullet(Projectile bullet)
        {
            _bullets.Release(bullet);
        }

        // ----- public controls ---------------------------------------------------

        /// <summary>Hooked to the HUD fire button (pointer down/up).</summary>
        public void SetFireHeld(bool held)
        {
            _fireHeld = held;
        }

        public void Reload()
        {
            if (IsReloading || Ammo >= magazineSize) return;
            StartCoroutine(ReloadRoutine());
        }

        IEnumerator ReloadRoutine()
        {
            IsReloading = true;
            ReloadProgress = 0f;
            ReloadingChanged?.Invoke(true);

            float t = 0f;
            while (t < reloadTime)
            {
                t += Time.deltaTime;
                ReloadProgress = Mathf.Clamp01(t / reloadTime);
                yield return null;
            }

            Ammo = magazineSize;
            ReloadProgress = 1f;
            IsReloading = false;
            ReloadingChanged?.Invoke(false);
            AmmoChanged?.Invoke(Ammo, magazineSize);
        }

        /// <summary>Full reset used when a run starts.</summary>
        public void ResetWeapon()
        {
            StopAllCoroutines();
            IsReloading = false;
            ReloadProgress = 1f;
            FiringBlocked = false;
            _fireHeld = false;
            Ammo = magazineSize;
            _nextShotTime = 0f;

            if (_bullets != null) _bullets.ReleaseAll();

            ReloadingChanged?.Invoke(false);
            AmmoChanged?.Invoke(Ammo, magazineSize);
            if (aim != null) aim.ResetAim();
        }
    }
}
