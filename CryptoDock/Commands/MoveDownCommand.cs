using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed partial class MoveDownCommand : InvokableCommand
{
    private readonly SettingsManager _settingsManager;
    private readonly WatchedSymbol _symbol;

    public MoveDownCommand(SettingsManager settingsManager, WatchedSymbol symbol)
    {
        _settingsManager = settingsManager;
        _symbol = symbol;
        Name = "Move Down";
        Icon = new IconInfo("\uE74B"); // Down arrow icon
    }

    public override CommandResult Invoke()
    {
        _settingsManager.MoveSymbolDown(_symbol);
        return CommandResult.KeepOpen();
    }
}
