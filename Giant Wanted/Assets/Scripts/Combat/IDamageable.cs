using UnityEngine;

namespace GiantWanted
{
    /// <summary>Anything a <see cref="Projectile"/> can hurt.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(float amount, Vector3 point, Vector3 normal, bool weakPoint);
    }
}
