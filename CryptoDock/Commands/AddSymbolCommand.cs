using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed partial class AddSymbolCommand : InvokableCommand
{
    private readonly SettingsManager _settingsManager;
    private readonly WatchedSymbol _symbol;

    public AddSymbolCommand(SettingsManager settingsManager, WatchedSymbol symbol)
    {
        _settingsManager = settingsManager;
        _symbol = symbol;
        Name = "Add to dock";
    }

    public override CommandResult Invoke()
    {
        bool added = _settingsManager.AddSymbol(_symbol);
        return CommandResult.ShowToast(added ? $"Added {_symbol.Symbol} ({_symbol.MarketLabel}) to Crypto Dock" : $"{_symbol.Symbol} ({_symbol.MarketLabel}) is already tracked");
    }
}
