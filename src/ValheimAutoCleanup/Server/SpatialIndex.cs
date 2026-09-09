using System.Collections.Generic;
using UnityEngine;

namespace ValheimAutoCleanup.Server
{
    /// <summary>
    /// A flat uniform grid over the XZ plane used to answer "is there a protected thing
    /// within R metres of this point?" without scanning every protector for every item.
    ///
    /// A world can hold tens of thousands of build pieces. Testing each candidate item
    /// against each piece would be quadratic; bucketing by cell makes each query touch only
    /// the handful of cells the query radius overlaps.
    ///
    /// The index is rebuilt from scratch at the start of each cleanup pass and thrown away
    /// afterwards, so it never holds stale world state. Buckets are pooled between passes to
    /// keep allocation pressure low.
    /// </summary>
    internal sealed class SpatialIndex
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, List<Vector3>> _cells = new Dictionary<long, List<Vector3>>();
        private readonly Stack<List<Vector3>> _bucketPool = new Stack<List<Vector3>>();

        internal SpatialIndex(float cellSize)
        {
            _cellSize = cellSize < 1f ? 1f : cellSize;
        }

        internal int Count { get; private set; }

        internal void Clear()
        {
            foreach (var bucket in _cells.Values)
            {
                bucket.Clear();
                _bucketPool.Push(bucket);
            }

            _cells.Clear();
            Count = 0;
        }

        internal void Add(Vector3 position)
        {
            var key = KeyFor(CellOf(position.x), CellOf(position.z));

            if (!_cells.TryGetValue(key, out var bucket))
            {
                bucket = _bucketPool.Count > 0 ? _bucketPool.Pop() : new List<Vector3>(8);
                _cells[key] = bucket;
            }

            bucket.Add(position);
            Count++;
        }

        /// <summary>
        /// True when any indexed position lies within <paramref name="radius"/> metres of
        /// <paramref name="point"/>, measured on the XZ plane. Height is ignored deliberately:
        /// a dropped item on the floor below a building is still "inside the base".
        /// </summary>
        internal bool AnyWithin(Vector3 point, float radius)
        {
            if (Count == 0 || radius <= 0f)
            {
                return false;
            }

            var radiusSq = radius * radius;
            var span = Mathf.CeilToInt(radius / _cellSize);
            var centerX = CellOf(point.x);
            var centerZ = CellOf(point.z);

            for (var x = centerX - span; x <= centerX + span; x++)
            {
                for (var z = centerZ - span; z <= centerZ + span; z++)
                {
                    if (!_cells.TryGetValue(KeyFor(x, z), out var bucket))
                    {
                        continue;
                    }

                    for (var i = 0; i < bucket.Count; i++)
                    {
                        var candidate = bucket[i];
                        var dx = candidate.x - point.x;
                        var dz = candidate.z - point.z;
                        if ((dx * dx) + (dz * dz) <= radiusSq)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private int CellOf(float coordinate)
        {
            return Mathf.FloorToInt(coordinate / _cellSize);
        }

        private static long KeyFor(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }
    }
}
