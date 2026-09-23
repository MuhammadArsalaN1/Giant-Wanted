using System.Collections.Generic;
using UnityEngine;

namespace GiantWanted
{
    /// <summary>
    /// Minimal component pool. Instances are parented to <paramref name="parent"/> and
    /// toggled with SetActive so Awake/OnEnable still run on the pooled behaviours.
    /// </summary>
    public class SimplePool<T> where T : Component
    {
        readonly T _prefab;
        readonly Transform _parent;
        readonly Stack<T> _idle = new Stack<T>();
        readonly List<T> _live = new List<T>();

        public IReadOnlyList<T> Live => _live;

        public SimplePool(T prefab, Transform parent, int prewarm = 0)
        {
            _prefab = prefab;
            _parent = parent;
            for (int i = 0; i < prewarm; i++)
            {
                T item = Object.Instantiate(_prefab, _parent);
                item.gameObject.SetActive(false);
                _idle.Push(item);
            }
        }

        public T Get(Vector3 position, Quaternion rotation)
        {
            T item = _idle.Count > 0 ? _idle.Pop() : Object.Instantiate(_prefab, _parent);
            Transform t = item.transform;
            t.SetPositionAndRotation(position, rotation);
            item.gameObject.SetActive(true);
            _live.Add(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;
            if (!_live.Remove(item)) return;   // already released
            item.gameObject.SetActive(false);
            item.transform.SetParent(_parent, false);
            _idle.Push(item);
        }

        public void ReleaseAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--) Release(_live[i]);
        }
    }
}
