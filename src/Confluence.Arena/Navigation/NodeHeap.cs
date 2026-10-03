using System;

namespace Confluence.Arena.Navigation
{
    /// <summary>
    /// Binary min-heap of (key, node) pairs for Dijkstra searches. Lazy deletion: callers skip entries whose key is
    /// stale. Ties break on a per-node rank (the node index when none is given), so expansion order never depends on
    /// insertion history. <see cref="ArenaNav"/> passes its mirror-equivariant rank, so on a half-turn symmetric arena
    /// mirrored nodes expand in mirrored order and every search gives mirrored results.
    /// </summary>
    internal sealed class NodeHeap
    {
        private readonly int[] _rank;
        private float[] _keys;
        private int[] _nodes;
        private int _count;

        public NodeHeap(int capacity, int[] rank = null)
        {
            capacity = Math.Max(16, capacity);
            _keys = new float[capacity];
            _nodes = new int[capacity];
            _rank = rank;
        }

        public int Count => _count;

        /// <summary>Key of the smallest entry (the heap must not be empty).</summary>
        public float PeekKey => _keys[0];

        /// <summary>Node of the smallest entry (the heap must not be empty).</summary>
        public int PeekNode => _nodes[0];

        public void Clear() => _count = 0;

        public void Push(float key, int node)
        {
            if (_count == _keys.Length)
            {
                Array.Resize(ref _keys, _count * 2);
                Array.Resize(ref _nodes, _count * 2);
            }

            int i = _count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (!Less(key, node, _keys[parent], _nodes[parent]))
                {
                    break;
                }

                _keys[i] = _keys[parent];
                _nodes[i] = _nodes[parent];
                i = parent;
            }

            _keys[i] = key;
            _nodes[i] = node;
        }

        public int Pop(out float key)
        {
            key = _keys[0];
            int result = _nodes[0];
            _count--;
            if (_count > 0)
            {
                float lastKey = _keys[_count];
                int lastNode = _nodes[_count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= _count)
                    {
                        break;
                    }

                    if (child + 1 < _count && Less(_keys[child + 1], _nodes[child + 1], _keys[child], _nodes[child]))
                    {
                        child++;
                    }

                    if (!Less(_keys[child], _nodes[child], lastKey, lastNode))
                    {
                        break;
                    }

                    _keys[i] = _keys[child];
                    _nodes[i] = _nodes[child];
                    i = child;
                }

                _keys[i] = lastKey;
                _nodes[i] = lastNode;
            }

            return result;
        }

        private bool Less(float keyA, int nodeA, float keyB, int nodeB)
        {
            if (keyA != keyB)
            {
                return keyA < keyB;
            }

            return _rank == null ? nodeA < nodeB : _rank[nodeA] < _rank[nodeB];
        }
    }
}
