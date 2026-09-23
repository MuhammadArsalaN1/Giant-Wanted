using System;
using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// Tracer bullet. Moves along its own forward axis and sweeps a raycast between the
    /// previous and current position each frame, so it never tunnels through a giant
    /// even at very high speeds.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        [Header("Flight")]
        public float speed = 220f;
        public float maxLifetime = 3f;
        public float maxDistance = 800f;
        [Tooltip("Gravity applied to the tracer. 0 = perfectly straight shot.")]
        public float gravity = 0f;

        [Header("Damage")]
        public float damage = 25f;
        public LayerMask hitMask = ~0;

        [Header("Visuals")]
        [Tooltip("Optional child that gets stretched along the travel direction.")]
        public Transform model;

        /// <summary>Set by the weapon so the bullet can put itself back in the pool.</summary>
        public Action<Projectile> Despawn;

        /// <summary>Raised when the bullet hits something. Args: bullet, point, normal, what it hit (may be null).</summary>
        public event Action<Projectile, Vector3, Vector3, IDamageable> Impacted;
        /// <summary>Raised when the bullet stops for any reason - impact, timeout or range.</summary>
        public event Action<Projectile> Ended;

        Vector3 _velocity;
        Vector3 _lastPosition;
        float _life;
        float _travelled;
        bool _active;
        TrailRenderer _trail;

        void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
        }

        /// <summary>Fire the bullet. <paramref name="direction"/> does not need to be normalised.</summary>
        public void Launch(Vector3 origin, Vector3 direction, float damageAmount)
        {
            // Pooled bullets are reused, so drop last flight's listeners before this one.
            Impacted = null;
            Ended = null;

            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            damage = damageAmount;
            _velocity = direction * speed;
            _lastPosition = origin;
            _life = 0f;
            _travelled = 0f;
            _active = true;

            // Without this a recycled bullet streaks a trail from wherever it last died.
            if (_trail != null) _trail.Clear();
        }

        void OnDisable()
        {
            _active = false;
        }

        /// <summary>
        /// Retune a bullet that is already in the air, keeping its heading. The kill cam
        /// uses this to stretch the shot out to a readable length.
        /// </summary>
        public void SetSpeed(float newSpeed)
        {
            speed = Mathf.Max(0.01f, newSpeed);
            if (_velocity.sqrMagnitude > 0.0001f) _velocity = _velocity.normalized * speed;
        }

        void Update()
        {
            if (!_active) return;

            float dt = Time.deltaTime;
            _life += dt;

            if (gravity != 0f) _velocity += Vector3.down * (gravity * dt);

            Vector3 next = transform.position + _velocity * dt;
            Vector3 segment = next - _lastPosition;
            float distance = segment.magnitude;

            if (distance > 0.0001f &&
                Physics.Raycast(_lastPosition, segment / distance, out RaycastHit hit, distance,
                                hitMask, QueryTriggerInteraction.Collide))
            {
                Impact(hit);
                return;
            }

            transform.position = next;
            if (_velocity.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(_velocity);
            _lastPosition = next;
            _travelled += distance;

            if (_life >= maxLifetime || _travelled >= maxDistance) Finish();
        }

        void Impact(RaycastHit hit)
        {
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && target.IsAlive)
            {
                target.TakeDamage(damage, hit.point, hit.normal, false);
                if (FxPool.Instance != null) FxPool.Instance.SpawnBlood(hit.point, hit.normal);
            }
            else if (FxPool.Instance != null)
            {
                FxPool.Instance.SpawnImpact(hit.point, hit.normal);
            }

            transform.position = hit.point;

            if (Impacted != null) Impacted(this, hit.point, hit.normal, target);
            Finish();
        }

        void Finish()
        {
            _active = false;

            // Fire before despawning: listeners may still want the bullet's final position.
            if (Ended != null) Ended(this);

            if (Despawn != null) Despawn(this);
            else gameObject.SetActive(false);
        }
    }
}
