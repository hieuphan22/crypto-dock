using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

public sealed partial class CryptoDockCommandsProvider : CommandProvider
{
    private readonly CryptoTickerService _tickerService = new();
    private readonly SettingsManager _settingsManager = new();
    private readonly CryptoDockPage _page;
    private readonly CryptoDockBand _dockBand;

    public CryptoDockCommandsProvider()
    {
        DisplayName = "Crypto Dock";
        Id = "com.hieuphan.cmdpal.cryptodock";
        Icon = new IconInfo("\uE8D7");
        Settings = _settingsManager.Settings;

        _page = new CryptoDockPage(_tickerService, _settingsManager);
        _dockBand = new CryptoDockBand(_tickerService, _settingsManager);
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return
        [
            new CommandItem(_page)
            {
                Title = "Manage Crypto Dock",
                Subtitle = "Search symbols, edit settings, and manage tracked tickers",
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
        ];
    }

    public override ICommandItem[] GetDockBands()
    {
        return [_dockBand];
    }

    public override void Dispose()
    {
        _dockBand.Dispose();
        _tickerService.Dispose();
        base.Dispose();
    }
}
