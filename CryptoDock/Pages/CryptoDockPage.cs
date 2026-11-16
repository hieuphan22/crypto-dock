using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed partial class CryptoDockPage : DynamicListPage, IDisposable
{
    private readonly CryptoTickerService _tickerService;
    private readonly SettingsManager _settingsManager;

    public CryptoDockPage(CryptoTickerService tickerService, SettingsManager settingsManager)
    {
        _tickerService = tickerService;
        _settingsManager = settingsManager;
        _settingsManager.SymbolsChanged += OnSymbolsChanged;
        Icon = new IconInfo("\uE8D7");
        Title = "Manage Crypto Dock";
        Name = "Open";
        PlaceholderText = "Search Binance spot/futures pairs, e.g. ADA, XAU, XAG";
        EmptyContent = new CommandItem(new NoOpCommand())
        {
            Title = "Type a coin symbol to add it to the dock",
            Subtitle = "Examples: ADA, DOGE, XAU, XAG",
        };
    }

    public void Dispose()
    {
        _settingsManager.SymbolsChanged -= OnSymbolsChanged;
    }

    private void OnSymbolsChanged(object? sender, EventArgs e)
    {
        RaiseItemsChanged();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        return string.IsNullOrWhiteSpace(SearchText) ? GetTrackedItems() : GetSearchItems();
    }

    private IListItem[] GetTrackedItems()
    {
        try
        {
            IReadOnlyList<CryptoTicker> tickers = _tickerService
                .GetTickersAsync(_settingsManager.Symbols)
                .GetAwaiter()
                .GetResult();

            return tickers
                .Select(ticker =>
                {
                    IconInfo iconInfo = CryptoTickerDockItem.CreateLogoOrFallbackIcon(ticker, ticker.DirectionIcon);
                    var watchedSymbol = new WatchedSymbol(ticker.Market, ticker.Symbol);
                    return new ListItem(new CopyTextCommand(ticker.Summary) { Name = "Copy price" })
                    {
                        Title = ticker.Summary,
                        Subtitle = $"24h high {CryptoTicker.FormatPrice(ticker.HighPrice)} | low {CryptoTicker.FormatPrice(ticker.LowPrice)}",
                        Icon = iconInfo,
                        MoreCommands =
                        [
                            new CommandContextItem(new CopyTextCommand(ticker.DetailsText) { Name = "Copy details" }),
                            new CommandContextItem(new OpenUrlCommand(watchedSymbol.BinanceUrl) { Name = "Open Binance" }),
                            new CommandContextItem(new MoveUpCommand(_settingsManager, watchedSymbol)),
                            new CommandContextItem(new MoveDownCommand(_settingsManager, watchedSymbol)),
                            new CommandContextItem(new RemoveSymbolCommand(_settingsManager, watchedSymbol)),
                        ],
                    };
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Unable to load Binance prices",
                    Subtitle = ex.Message,
                    Icon = new IconInfo("\uE783"),
                },
            ];
        }
    }

    private IListItem[] GetSearchItems()
    {
        try
        {
            HashSet<string> trackedSymbols = _settingsManager.Symbols.Select(symbol => symbol.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<CryptoSymbol> symbols = _tickerService
                .SearchSymbolsAsync(SearchText, _settingsManager.MarketSource)
                .GetAwaiter()
                .GetResult();

            return symbols
                .Select(symbol =>
                {
                    WatchedSymbol watchedSymbol = symbol.ToWatchedSymbol();
                    bool tracked = trackedSymbols.Contains(watchedSymbol.Key);
                    return new ListItem(tracked ? new NoOpCommand() : new AddSymbolCommand(_settingsManager, watchedSymbol))
                    {
                        Title = $"{symbol.Pair} [{symbol.ShortMarketLabel}]",
                        Subtitle = tracked ? $"Already tracked ({symbol.MarketLabel})" : $"Add {symbol.Symbol} from {symbol.MarketLabel} to Crypto Dock",
                        Icon = new IconInfo(tracked ? "\uE73E" : "\uE710"),
                        MoreCommands = tracked
                            ? [new CommandContextItem(new RemoveSymbolCommand(_settingsManager, watchedSymbol))]
                            : [new CommandContextItem(new AddSymbolCommand(_settingsManager, watchedSymbol))],
                    };
                })
                .ToArray();
        }
        catch (Exception ex)
        {
            return
            [
                new ListItem(new NoOpCommand())
                {
                    Title = "Unable to search Binance symbols",
                    Subtitle = ex.Message,
                    Icon = new IconInfo("\uE783"),
                },
            ];
        }
    }

}
