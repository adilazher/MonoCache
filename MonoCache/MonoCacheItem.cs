namespace MonoCache
{
    internal class MonoCacheItem
    {
        public required string Key { get; set; }
        public required byte[] Value { get; set; }
        public DateTime InsertedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
