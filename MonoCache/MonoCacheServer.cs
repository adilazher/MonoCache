using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MonoCache
{
    public class MonoCacheServer
    {
        private readonly TcpListener _tcpListener;
        private readonly MonoCacheCore _monoCache;
        private volatile bool _isRunning;

        public MonoCacheServer()
        {
            _tcpListener = new TcpListener(IPAddress.Any, 6969);
            _monoCache = new();
        }

        public void Start()
        {
            _tcpListener.Start();
            _isRunning = true;

            Console.WriteLine("Cache server running on port 6969");

            Task.Run(() =>
            {
                while (_isRunning)
                {
                    try
                    {
                        TcpClient tcpClient = _tcpListener.AcceptTcpClient();
                        Task.Run(() => HandleClient(tcpClient));
                    }
                    catch (SocketException) when (!_isRunning)
                    {
                        // Listener was stopped via Stop(), exit cleanly
                        break;
                    }
                }
            });
        }

        public void Stop()
        {
            _isRunning = false;
            _tcpListener.Stop();
        }

        private void HandleClient(TcpClient client)
        {
            Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
            var stream = client.GetStream();

            try
            {
                while (client.Connected)
                {
                    int command = stream.ReadByte();
                    if (command == -1)
                    {
                        Console.WriteLine("Client disconnected (EOF)");
                        break;
                    }

                    if (command == 1)
                    {
                        Console.WriteLine("Received PUT command");

                        byte[] keyLengthBuffer = new byte[4];
                        int bytesRead = 0;
                        while (bytesRead < 4)
                        {
                            int read = stream.Read(keyLengthBuffer, bytesRead, 4 - bytesRead);
                            if (read == 0) throw new Exception("Connection closed while reading key length");
                            bytesRead += read;
                        }
                        int keyLength = BitConverter.ToInt32(keyLengthBuffer, 0);

                        byte[] keyBytes = new byte[keyLength];
                        bytesRead = 0;
                        while (bytesRead < keyLength)
                        {
                            int read = stream.Read(keyBytes, bytesRead, keyLength - bytesRead);
                            if (read == 0) throw new Exception("Connection closed while reading key");
                            bytesRead += read;
                        }
                        string key = Encoding.UTF8.GetString(keyBytes);

                        byte[] valueLengthBuffer = new byte[4];
                        bytesRead = 0;
                        while (bytesRead < 4)
                        {
                            int read = stream.Read(valueLengthBuffer, bytesRead, 4 - bytesRead);
                            if (read == 0) throw new Exception("Connection closed while reading value length");
                            bytesRead += read;
                        }
                        int valueLength = BitConverter.ToInt32(valueLengthBuffer, 0);

                        byte[] value = new byte[valueLength];
                        int totalRead = 0;
                        while (totalRead < valueLength)
                        {
                            int read = stream.Read(value, totalRead, valueLength - totalRead);
                            if (read == 0) throw new Exception("Connection closed while reading value");
                            totalRead += read;
                        }

                        Console.WriteLine($"PUT: key='{key}', value length={valueLength}");
                        _monoCache.AddorUpdateItem(key, value);
                        stream.WriteByte(1);
                        stream.Flush();
                        Console.WriteLine("Sent ACK for PUT");
                    }
                    else if (command == 2)
                    {
                        Console.WriteLine("Received GET command");

                        byte[] keyLengthBuffer = new byte[4];
                        int bytesRead = 0;
                        while (bytesRead < 4)
                        {
                            int read = stream.Read(keyLengthBuffer, bytesRead, 4 - bytesRead);
                            if (read == 0) throw new Exception("Connection closed while reading key length");
                            bytesRead += read;
                        }
                        int keyLength = BitConverter.ToInt32(keyLengthBuffer, 0);

                        byte[] keyBytes = new byte[keyLength];
                        bytesRead = 0;
                        while (bytesRead < keyLength)                        {
                            int read = stream.Read(keyBytes, bytesRead, keyLength - bytesRead);
                            if (read == 0) throw new Exception("Connection closed while reading key");
                            bytesRead += read;
                        }
                        string key = Encoding.UTF8.GetString(keyBytes);

                        Console.WriteLine($"GET: key='{key}'");
                        byte[]? value = _monoCache.GetValue(key);

                        if (value == null)
                        {
                            Console.WriteLine("Value not found, sending 0");
                            stream.WriteByte(0);
                            stream.Flush();
                        }
                        else
                        {
                            Console.WriteLine($"Value found, sending value length={value.Length}");
                            stream.WriteByte(1);
                            stream.Write(BitConverter.GetBytes(value.Length), 0, 4);
                            stream.Write(value, 0, value.Length);
                            stream.Flush();
                        }
                    }
                    else if (command == 3)
                    {
                        Console.WriteLine("Received CLEAR command");
                        _monoCache.ClearCache();
                        stream.WriteByte(1);
                        stream.Flush();
                        Console.WriteLine("Sent ACK for CLEAR");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine("Client connection closed");
            }
        }
    }
}
