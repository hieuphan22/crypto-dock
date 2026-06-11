using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace CryptoDock;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            return;
        }

        ManualResetEvent extensionDisposedEvent = new(false);
        ComServer server = new();

        CryptoDockExtension extensionInstance = new(extensionDisposedEvent);
        server.RegisterClass<CryptoDockExtension, IExtension>(() => extensionInstance);
        server.Start();

        extensionDisposedEvent.WaitOne();
        server.Stop();
        server.UnsafeDispose();
        extensionDisposedEvent.Dispose();
    }
}
