using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MonoCache.Client
{
    public class MonoCacheClient : IDisposable
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly object _lock = new();

        public MonoCacheClient(string host, int port)
        {
            _client = new TcpClient(host, port);
            _stream = _client.GetStream();
        }

        public void Put(string key, object value)
        {
            lock (_lock)
            {
                string json = JsonSerializer.Serialize(value);
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);
                byte[] valueBytes = Encoding.UTF8.GetBytes(json);

                _stream.WriteByte(1); // PUT
                _stream.Write(BitConverter.GetBytes(keyBytes.Length), 0, 4);
                _stream.Write(keyBytes, 0, keyBytes.Length);
                _stream.Write(BitConverter.GetBytes(valueBytes.Length), 0, 4);
                _stream.Write(valueBytes, 0, valueBytes.Length);
                _stream.Flush();

                _stream.ReadByte(); // ACK
            }
        }

        public T? Get<T>(string key)
        {
            lock (_lock)
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(key);

                _stream.WriteByte(2); // GET
                _stream.Write(BitConverter.GetBytes(keyBytes.Length), 0, 4);
                _stream.Write(keyBytes, 0, keyBytes.Length);
                _stream.Flush();

                int found = _stream.ReadByte();
                if (found == 0) return default;

                byte[] lengthBuffer = new byte[4];
                int rb = 0;
                while (rb < 4) rb += _stream.Read(lengthBuffer, rb, 4 - rb);
                int valueLength = BitConverter.ToInt32(lengthBuffer, 0);

                byte[] value = new byte[valueLength];
                int totalRead = 0;
                while (totalRead < valueLength)
                {
                    totalRead += _stream.Read(value, totalRead, valueLength - totalRead);
                }

                string json = Encoding.UTF8.GetString(value);
                return JsonSerializer.Deserialize<T>(json);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _stream.WriteByte(3);
                _stream.Flush();
                _stream.ReadByte();
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}