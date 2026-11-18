using MonoCache;

internal class Program
{
    private static void Main(string[] args)
    {
        var server = new MonoCacheServer();
        server.Start();

        Console.WriteLine("Press Enter to stop the server.");
        Console.ReadLine();

        server.Stop();
        Console.WriteLine("Server stopped.");
    }
}