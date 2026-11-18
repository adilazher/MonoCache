namespace MonoCache
{
    internal class MonoCacheCore
    {
        private readonly Dictionary<string, LinkedListNode<MonoCacheItem>> _internalCache = new();
        private readonly LinkedList<MonoCacheItem> _lruTracker = new();
        private readonly object _lock = new();

        /// <summary>
        /// Gets the number of items currently stored inside the cache.
        /// This can include items that are expired.
        /// </summary>
        public int ItemCount => _internalCache.Count;

        /// <summary>
        /// The sliding window time for the lifetime of each item in the cache.
        /// </summary>
        private readonly TimeSpan _slidingExpiryWindow = TimeSpan.FromMinutes(5);

        private const int _maxCacheCapacity = 100;

        private readonly string _walFilePath;

        /// <summary>
        /// Default Ctor
        /// </summary>
        public MonoCacheCore()
        {
            Directory.CreateDirectory("WAL");
            _walFilePath = Path.Combine("WAL", "monocacheWAL.txt");

            ReplayCache();
        }

        /// <summary>
        /// Ctor -- Initializes an instance of MonoCache with the passed item sliding expiry window.
        /// </summary>
        /// <param name="itemSlidingExpiryWindow">The timespan for which the item should be alive</param>
        public MonoCacheCore(TimeSpan itemSlidingExpiryWindow)
        {
            _slidingExpiryWindow = itemSlidingExpiryWindow;

            Directory.CreateDirectory("WAL");
            _walFilePath = Path.Combine("WAL", "monocacheWAL.txt");

            ReplayCache();
        }

        private void ReplayCache()
        {
            if (File.Exists(_walFilePath) == false)
            {
                return;
            }

            foreach (var line in File.ReadAllLines(_walFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split('|');

                if (parts[0] == "CLR")
                {
                    _internalCache.Clear();
                    _lruTracker.Clear();
                }
                else if (parts[0] == "PUT")
                {
                    string key = parts[1];
                    byte[] value = Convert.FromBase64String(parts[2]);
                    long expiresAtTicks = long.Parse(parts[3]);
                    DateTime expiresAt = new DateTime(expiresAtTicks, DateTimeKind.Utc);

                    MonoCacheItem cacheItem = new()
                    {
                        Key = key,
                        Value = value,
                        ExpiresAt = expiresAt,
                        InsertedAt = expiresAt - _slidingExpiryWindow
                    };

                    if (_internalCache.TryGetValue(key, out var existingNode))
                    {
                        _lruTracker.Remove(existingNode);
                    }

                    if (expiresAt > DateTime.UtcNow)
                    {
                        LinkedListNode<MonoCacheItem> linkedListNode = new(cacheItem);
                        _internalCache[key] = linkedListNode;
                        _lruTracker.AddLast(linkedListNode);
                    }
                }
            }

            CompactCache();

            // Yeet and start again for dis round
            File.Delete(_walFilePath);
        }

        /// <summary>
        /// Updates an exisitng item if it exists or adds a new item to the cache
        /// if it does not exist.
        /// </summary>
        /// <param name="key">The key against which we are adding or updating.</param>
        /// <param name="value">The new value</param>
        public void AddorUpdateItem(string key, byte[] value)
        {
            lock (_lock)
            {
                DateTime expiresAt = DateTime.UtcNow + _slidingExpiryWindow;
                string logLine = $"PUT|{key}|{Convert.ToBase64String(value)}|{expiresAt.Ticks}";
                FlushLineToLogfile(logLine);

                if (_internalCache.TryGetValue(key, out LinkedListNode<MonoCacheItem>? node))
                {
                    node.Value.Value = value;
                    node.Value.ExpiresAt = expiresAt;

                    _lruTracker.Remove(node);
                    _lruTracker.AddFirst(node);
                }
                else
                {
                    MonoCacheItem newItem = new MonoCacheItem
                    {
                        Key = key,
                        Value = value,
                        InsertedAt = expiresAt - _slidingExpiryWindow,
                        ExpiresAt = expiresAt
                    };

                    LinkedListNode<MonoCacheItem> newNode = new(newItem);
                    _internalCache.Add(key, newNode);
                    _lruTracker.AddFirst(newNode);

                    if (_internalCache.Count >= _maxCacheCapacity)
                    {
                        CompactCache();
                    }
                }
            }
        }

        /// <summary>
        /// Gets the cache value associated with the passed key.
        /// Yeets if it is expired.
        /// </summary>
        /// <param name="key">The key against which we want to get the value</param>
        /// <returns>The value</returns>
        public byte[]? GetValue(string key)
        {
            lock (_lock)
            {
                if (_internalCache.TryGetValue(key, out LinkedListNode<MonoCacheItem>? value))
                {
                    // Evict if expired
                    if (value.Value.ExpiresAt <= DateTime.UtcNow)
                    {
                        _internalCache.Remove(key);
                        _lruTracker.Remove(value);

                        return null;
                    }
                    else
                    {
                        value.Value.ExpiresAt = DateTime.UtcNow + _slidingExpiryWindow;
                        _lruTracker.Remove(value);
                        _lruTracker.AddFirst(value);

                        return value.Value.Value;
                    }
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Compacts the cache removing dead items from it.
        /// </summary>
        public void CompactCache()
        {
            lock (_lock)
            {
                LinkedListNode<MonoCacheItem>? listNode = _lruTracker.Last;

                while (listNode != null)
                {
                    LinkedListNode<MonoCacheItem>? previousNode = listNode.Previous;

                    if (listNode.Value.ExpiresAt <= DateTime.UtcNow)
                    {
                        _lruTracker.Remove(listNode);
                        _internalCache.Remove(listNode.Value.Key, out _);
                    }

                    listNode = previousNode;
                }

                while (_internalCache.Count > _maxCacheCapacity)
                {
                    var lruNode = _lruTracker.Last;

                    if (lruNode == null)
                    {
                        break;
                    }

                    _lruTracker.RemoveLast();
                    _internalCache.Remove(lruNode.Value.Key, out _);
                }
            }
        }

        /// <summary>
        /// Wipes the cache clean, emptying everything.
        /// </summary>
        public void ClearCache()
        {
            lock (_lock)
            {
                FlushLineToLogfile("CLR");

                _internalCache.Clear();
                _lruTracker.Clear();
            }
        }

        /// <summary>
        /// Flushes a log line into the WAL text file.
        /// </summary>
        /// <param name="logLine">The log line we want to flush into the file.</param>
        private void FlushLineToLogfile(string logLine)
        {
            File.AppendAllText(_walFilePath, logLine + Environment.NewLine);
        }
    }
}
