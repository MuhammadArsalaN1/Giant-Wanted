using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// A flying giant. Closes on the city, hovers in range and pounds it until shot down.
    /// Drives the two states that exist on the monster animator (fly + die) directly by
    /// name, so no animator parameters or transitions have to be authored.
    /// </summary>
    public class Giant : MonoBehaviour, IDamageable
    {
        public enum Phase { Idle, Approach, Attack, Dying }

        [Header("References")]
        public Animator animator;
        public WorldHealthBar healthBar;
        [Tooltip("Colliders that are switched off the moment the giant dies.")]
        public Collider[] hitColliders;
        [Tooltip("Where damage numbers and the health bar are anchored. Defaults to this transform.")]
        public Transform centerPoint;

        [Header("Animator state names")]
        public string flyState = "Fly";
        public string dieState = "die";

        [Header("Stats")]
        public float maxHealth = 200f;
        public float moveSpeed = 8f;
        public float turnSpeed = 3f;
        [Tooltip("How close the giant gets to the city before it stops and attacks.")]
        public float attackRange = 25f;
        public float attackInterval = 2.5f;
        public int attackDamage = 8;

        [Header("Flight")]
        [Tooltip("Height above the target the giant settles at.")]
        public float hoverHeight = 18f;
        public float hoverAmplitude = 1.2f;
        public float hoverFrequency = 0.6f;
        public float verticalLerp = 1.5f;

        [Header("Attack motion")]
        [Tooltip("How far the giant lunges towards the city when it strikes.")]
        public float lungeDistance = 6f;
        public float lungeDuration = 0.45f;
        public float attackShake = 0.45f;

        [Header("Reward")]
        public int scoreValue = 100;
        public int coinValue = 10;

        [Header("Feedback")]
        public Color hitFlashColor = Color.white;
        public float hitFlashTime = 0.07f;
        public float deathSinkDelay = 2.2f;
        public float deathSinkSpeed = 4f;
        public float deathSinkTime = 2f;

        /// <summary>Raised once when the giant dies. Argument is this giant.</summary>
        public event Action<Giant> Died;
        /// <summary>Raised when the giant is fully finished (death animation + sink).</summary>
        public event Action<Giant> Finished;

        public bool IsAlive => _phase != Phase.Dying && _health > 0f;
        /// <summary>True when the killing blow landed on the weak point - worth a bonus.</summary>
        public bool KilledByWeakPoint { get; private set; }
        public float HealthNormalized => maxHealth > 0f ? _health / maxHealth : 0f;
        public Vector3 Center => centerPoint != null ? centerPoint.position : transform.position;

        Transform _target;
        Phase _phase = Phase.Idle;
        float _health;
        float _attackTimer;
        float _hoverSeed;
        float _baseSpeed;
        float _baseMaxHealth;
        Vector3 _baseScale;
        float _lungeOffset;
        Coroutine _flashRoutine;

        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<Color> _baseColors = new List<Color>();
        MaterialPropertyBlock _block;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (centerPoint == null) centerPoint = transform;
            if (hitColliders == null || hitColliders.Length == 0) hitColliders = GetComponentsInChildren<Collider>(true);

            _baseSpeed = moveSpeed;
            _baseMaxHealth = Mathf.Max(1f, maxHealth);
            _baseScale = transform.localScale;   // authored size - wave scaling multiplies this
            _block = new MaterialPropertyBlock();
            CacheRenderers();
        }

        void CacheRenderers()
        {
            _renderers.Clear();
            _baseColors.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                _renderers.Add(r);

                Material shared = r.sharedMaterial;
                Color c = Color.white;
                if (shared != null)
                {
                    if (shared.HasProperty(BaseColorId)) c = shared.GetColor(BaseColorId);
                    else if (shared.HasProperty(ColorId)) c = shared.GetColor(ColorId);
                }
                _baseColors.Add(c);
            }
        }

        /// <summary>Called by the spawner every time the giant comes out of the pool.</summary>
        public void Spawn(Transform target, float healthMultiplier, float speedMultiplier, float scale)
        {
            _target = target;

            // Always scale from the authored value so pooled reuse does not compound.
            maxHealth = _baseMaxHealth * Mathf.Max(0.01f, healthMultiplier);
            _health = maxHealth;

            moveSpeed = _baseSpeed * Mathf.Max(0.05f, speedMultiplier);
            transform.localScale = _baseScale * scale;

            _phase = Phase.Approach;
            KilledByWeakPoint = false;
            _attackTimer = attackInterval;
            _hoverSeed = UnityEngine.Random.value * 10f;
            _lungeOffset = 0f;

            SetCollidersEnabled(true);
            ApplyFlash(0f);

            if (healthBar != null)
            {
                healthBar.anchor = centerPoint;
                healthBar.ResetBar();
            }

            if (animator != null)
            {
                animator.enabled = true;
                animator.Rebind();
                animator.Play(flyState, 0, UnityEngine.Random.value);
            }
        }

        void Update()
        {
            if (_phase == Phase.Idle || _phase == Phase.Dying || _target == null) return;

            Vector3 targetPoint = _target.position;
            Vector3 flat = targetPoint - transform.position;
            flat.y = 0f;
            float distance = flat.magnitude;

            // Face the city.
            if (flat.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(flat.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
            }

            _phase = distance <= attackRange ? Phase.Attack : Phase.Approach;

            // Horizontal travel.
            if (_phase == Phase.Approach)
            {
                Vector3 step = flat.normalized * (moveSpeed * Time.deltaTime);
                if (step.magnitude > distance - attackRange) step = flat.normalized * Mathf.Max(0f, distance - attackRange);
                transform.position += step;
            }
            else
            {
                Tick_Attack();
            }

            // Vertical hover, independent of the horizontal chase.
            float bob = Mathf.Sin((Time.time + _hoverSeed) * hoverFrequency * Mathf.PI * 2f) * hoverAmplitude;
            float desiredY = targetPoint.y + hoverHeight + bob;
            Vector3 p = transform.position;
            p.y = Mathf.Lerp(p.y, desiredY, verticalLerp * Time.deltaTime);

            // Lunge offset is applied on top of the settled position.
            if (_lungeOffset != 0f)
            {
                Vector3 dir = flat.sqrMagnitude > 0.001f ? flat.normalized : transform.forward;
                p += dir * _lungeOffset;
            }

            transform.position = p;
        }

        void Tick_Attack()
        {
            _attackTimer -= Time.deltaTime;
            if (_attackTimer > 0f) return;

            _attackTimer = attackInterval;
            StartCoroutine(Strike());
        }

        IEnumerator Strike()
        {
            // Lunge in, land the hit, drift back out.
            float half = lungeDuration * 0.5f;
            float t = 0f;
            while (t < half && IsAlive)
            {
                t += Time.deltaTime;
                _lungeOffset = Mathf.Lerp(0f, lungeDistance, t / half);
                yield return null;
            }

            if (!IsAlive) { _lungeOffset = 0f; yield break; }

            if (GameManager.Instance != null) GameManager.Instance.DamageCity(attackDamage);
            if (CameraShaker.Instance != null) CameraShaker.Instance.Shake(attackShake);

            t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                _lungeOffset = Mathf.Lerp(lungeDistance, 0f, t / half);
                yield return null;
            }

            _lungeOffset = 0f;
        }

        public void TakeDamage(float amount, Vector3 point, Vector3 normal, bool weakPoint)
        {
            if (!IsAlive) return;

            _health -= amount;

            if (healthBar != null) healthBar.SetHealth(HealthNormalized);
            if (FxPool.Instance != null) FxPool.Instance.SpawnPopup(point, amount, weakPoint);
            if (GameAudio.Instance != null) GameAudio.Instance.PlayHitAt(point);

            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());

            if (_health <= 0f)
            {
                KilledByWeakPoint = weakPoint;
                Die();
            }
        }

        IEnumerator FlashRoutine()
        {
            ApplyFlash(1f);
            yield return new WaitForSeconds(hitFlashTime);
            ApplyFlash(0f);
            _flashRoutine = null;
        }

        void ApplyFlash(float amount)
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;

                Color c = Color.Lerp(_baseColors[i], hitFlashColor, amount);
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, c);
                _block.SetColor(ColorId, c);
                r.SetPropertyBlock(_block);
            }
        }

        void Die()
        {
            if (_phase == Phase.Dying) return;

            _phase = Phase.Dying;
            _health = 0f;
            _lungeOffset = 0f;

            StopAllCoroutines();
            ApplyFlash(0f);
            SetCollidersEnabled(false);
            if (healthBar != null) healthBar.Hide();

            if (animator != null) animator.Play(dieState, 0, 0f);

            if (Died != null) Died(this);
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            yield return new WaitForSeconds(deathSinkDelay);

            // Fall out of the sky rather than popping out of existence.
            float t = 0f;
            while (t < deathSinkTime)
            {
                t += Time.deltaTime;
                transform.position += Vector3.down * (deathSinkSpeed * Time.deltaTime);
                yield return null;
            }

            if (Finished != null) Finished(this);
            else gameObject.SetActive(false);
        }

        void SetCollidersEnabled(bool value)
        {
            if (hitColliders == null) return;
            foreach (Collider c in hitColliders)
                if (c != null) c.enabled = value;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
