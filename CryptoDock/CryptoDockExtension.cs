using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace CryptoDock;

[ComVisible(true)]
[Guid("106E39F7-BFDD-4C88-A92C-99FE3F765150")]
[ComDefaultInterface(typeof(IExtension))]
public sealed partial class CryptoDockExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly CryptoDockCommandsProvider _provider = new();

    public CryptoDockExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType)
    {
        return providerType == ProviderType.Commands ? _provider : null;
    }

    public void Dispose()
    {
        _provider.Dispose();
        _extensionDisposedEvent.Set();
    }
}
