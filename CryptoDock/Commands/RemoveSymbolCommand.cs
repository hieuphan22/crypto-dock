using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed partial class RemoveSymbolCommand : InvokableCommand
{
    private readonly SettingsManager _settingsManager;
    private readonly WatchedSymbol _symbol;

    public RemoveSymbolCommand(SettingsManager settingsManager, WatchedSymbol symbol)
    {
        _settingsManager = settingsManager;
        _symbol = symbol;
        Name = "Remove from dock";
    }

    public override CommandResult Invoke()
    {
        bool removed = _settingsManager.RemoveSymbol(_symbol);
        return CommandResult.ShowToast(removed ? $"Removed {_symbol.Symbol} ({_symbol.MarketLabel}) from Crypto Dock" : $"{_symbol.Symbol} ({_symbol.MarketLabel}) is not tracked");
    }
}
