using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed partial class MoveUpCommand : InvokableCommand
{
    private readonly SettingsManager _settingsManager;
    private readonly WatchedSymbol _symbol;

    public MoveUpCommand(SettingsManager settingsManager, WatchedSymbol symbol)
    {
        _settingsManager = settingsManager;
        _symbol = symbol;
        Name = "Move Up";
        Icon = new IconInfo("\uE74A"); // Up arrow icon
    }

    public override CommandResult Invoke()
    {
        _settingsManager.MoveSymbolUp(_symbol);
        return CommandResult.KeepOpen();
    }
}
