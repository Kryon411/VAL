using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace VAL.Host.Portal
{
    internal sealed class PortalStaging
    {
        // Bounded in-memory staging buffer (no persistence)
        // Thread-safe because clipboard polling + send loop can overlap.
        private const int MAX = 10;
        private readonly object _lock = new();
        private readonly List<BitmapSource> _items = new();

        public int Count
        {
            get { lock (_lock) return _items.Count; }
        }

        public void Clear()
        {
            lock (_lock) _items.Clear();
        }

        /// <summary>
        /// Try to add an image to the staging buffer.
        /// Returns false if the buffer is full.
        /// </summary>
        public bool TryAdd(BitmapSource img)
        {
            if (img == null) return false;

            lock (_lock)
            {
                if (_items.Count >= MAX) return false;
                _items.Add(img);
                return true;
            }
        }

        /// <summary>
        /// Drain up to max items (FIFO) and remove them from the staging buffer.
        /// </summary>
        public BitmapSource[] Drain(int max)
        {
            if (max <= 0) return Array.Empty<BitmapSource>();

            lock (_lock)
            {
                if (_items.Count == 0) return Array.Empty<BitmapSource>();

                int take = Math.Min(Math.Min(max, MAX), _items.Count);
                var drained = _items.Take(take).ToArray();
                _items.RemoveRange(0, take);
                return drained;
            }
        }
    }
}
