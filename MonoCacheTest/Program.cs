using System.Net.Sockets;
using MonoCache;
using MonoCache.Client;

namespace MonoCacheTest
{
    internal class Program
    {
        static int _failures = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("=== MonoCache Integration Tests ===\n");

            bool externalServer = IsServerRunning("localhost", 6969);

            if (externalServer)
                Console.WriteLine("Detected running server on port 6969 — skipping server start.\n");

            RunServerTests(externalServer);
            RunClientTests(externalServer);

            Console.WriteLine(_failures == 0
                ? "\nAll tests passed."
                : $"\n{_failures} test(s) FAILED.");

            Environment.Exit(_failures > 0 ? 1 : 0);
        }

        static bool IsServerRunning(string host, int port)
        {
            try
            {
                using var probe = new TcpClient(host, port);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        // ----------------------------------------------------------------
        // Server-side tests (MonoCacheServer + MonoCacheClient over TCP)
        // ----------------------------------------------------------------

        static void RunServerTests(bool externalServer)
        {
            Console.WriteLine("-- Server Tests --");

            MonoCacheServer? server = null;
            if (!externalServer)
            {
                server = new MonoCacheServer();
                server.Start();
                Thread.Sleep(100);
            }

            try
            {
                using var client = new MonoCacheClient("localhost", 6969);

                // Clear any state left by a previous run or the external server
                client.Clear();

                TestPutAndGet(client);
                TestGetMissingKey(client);
                TestPutUpdatesExistingKey(client);
                TestClearRemovesAllKeys(client);
                TestMultipleKeysDontInterfere(client);
                TestPutAndGetInt(client);
                TestPutAndGetComplexObject(client);
                TestGetAfterClearReturnsNull(client);
            }
            finally
            {
                server?.Stop();
            }
        }

        static void TestPutAndGet(MonoCacheClient client)
        {
            client.Put("greeting", "hello world");
            var result = client.Get<string>("greeting");
            Assert("PUT then GET returns correct value", result == "hello world");
        }

        static void TestGetMissingKey(MonoCacheClient client)
        {
            var result = client.Get<string>("no_such_key");
            Assert("GET missing key returns null", result == null);
        }

        static void TestPutUpdatesExistingKey(MonoCacheClient client)
        {
            client.Put("counter", 1);
            client.Put("counter", 2);
            var result = client.Get<int>("counter");
            Assert("PUT on existing key updates value", result == 2);
        }

        static void TestClearRemovesAllKeys(MonoCacheClient client)
        {
            client.Put("temp", "data");
            client.Clear();
            var result = client.Get<string>("temp");
            Assert("CLEAR removes all keys", result == null);
        }

        static void TestMultipleKeysDontInterfere(MonoCacheClient client)
        {
            client.Put("a", "alpha");
            client.Put("b", "beta");
            client.Put("c", "gamma");
            Assert("Multiple keys: 'a' correct", client.Get<string>("a") == "alpha");
            Assert("Multiple keys: 'b' correct", client.Get<string>("b") == "beta");
            Assert("Multiple keys: 'c' correct", client.Get<string>("c") == "gamma");
        }

        static void TestPutAndGetInt(MonoCacheClient client)
        {
            client.Put("num", 42);
            var result = client.Get<int>("num");
            Assert("PUT and GET integer value", result == 42);
        }

        static void TestPutAndGetComplexObject(MonoCacheClient client)
        {
            var payload = new CachePayload { Name = "test", Score = 99 };
            client.Put("obj", payload);
            var result = client.Get<CachePayload>("obj");
            Assert("PUT and GET complex object", result != null && result.Name == "test" && result.Score == 99);
        }

        static void TestGetAfterClearReturnsNull(MonoCacheClient client)
        {
            client.Put("x", "value");
            client.Put("y", "value");
            client.Clear();
            Assert("GET 'x' after CLEAR returns null", client.Get<string>("x") == null);
            Assert("GET 'y' after CLEAR returns null", client.Get<string>("y") == null);
        }

        // ----------------------------------------------------------------
        // Client tests (two concurrent clients against the same server)
        // ----------------------------------------------------------------

        static void RunClientTests(bool externalServer)
        {
            Console.WriteLine("\n-- Client Tests --");

            MonoCacheServer? server = null;
            if (!externalServer)
            {
                server = new MonoCacheServer();
                server.Start();
                Thread.Sleep(100);
            }

            try
            {
                using var clientA = new MonoCacheClient("localhost", 6969);
                using var clientB = new MonoCacheClient("localhost", 6969);

                clientA.Clear();

                TestTwoClientsShareState(clientA, clientB);
                TestClientPutOverwriteSeenByOtherClient(clientA, clientB);
                TestClientClearSeenByOtherClient(clientA, clientB);
            }
            finally
            {
                server?.Stop();
            }
        }

        static void TestTwoClientsShareState(MonoCacheClient clientA, MonoCacheClient clientB)
        {
            clientA.Put("shared", "from-A");
            var result = clientB.Get<string>("shared");
            Assert("Value written by client A is visible to client B", result == "from-A");
        }

        static void TestClientPutOverwriteSeenByOtherClient(MonoCacheClient clientA, MonoCacheClient clientB)
        {
            clientA.Put("key", "v1");
            clientB.Put("key", "v2");
            var result = clientA.Get<string>("key");
            Assert("PUT by client B overwrites value seen by client A", result == "v2");
        }

        static void TestClientClearSeenByOtherClient(MonoCacheClient clientA, MonoCacheClient clientB)
        {
            clientA.Put("persist", "yes");
            clientB.Clear();
            var result = clientA.Get<string>("persist");
            Assert("CLEAR by client B removes keys visible to client A", result == null);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        static void Assert(string testName, bool condition)
        {
            Console.WriteLine(condition ? $"  PASS  {testName}" : $"  FAIL  {testName}");
            if (!condition) _failures++;
        }

        record CachePayload
        {
            public string Name { get; init; } = "";
            public int Score { get; init; }
        }
    }
}
