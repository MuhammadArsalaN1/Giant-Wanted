using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// A collider on a giant that forwards damage to the owner, optionally scaling it.
    /// The head hit box is the weak point: big multiplier, "CRITICAL" popup.
    /// </summary>
    public class HitBox : MonoBehaviour, IDamageable
    {
        public Giant owner;
        [Tooltip("Damage multiplier applied before the hit reaches the giant.")]
        public float damageMultiplier = 1f;
        public bool isWeakPoint;

        public bool IsAlive => owner != null && owner.IsAlive;

        void Reset()
        {
            owner = GetComponentInParent<Giant>();
        }

        public void TakeDamage(float amount, Vector3 point, Vector3 normal, bool weakPoint)
        {
            if (owner == null) return;
            owner.TakeDamage(amount * damageMultiplier, point, normal, weakPoint || isWeakPoint);
        }
    }
}
