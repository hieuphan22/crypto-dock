using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed partial class CryptoDockBand : WrappedDockItem, IDisposable
{
    private readonly CryptoTickerService _tickerService;
    private readonly SettingsManager _settingsManager;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private Dictionary<string, CryptoTickerDockItem> _items = [];

    public CryptoDockBand(CryptoTickerService tickerService, SettingsManager settingsManager)
        : base([], "com.hieuphan.cmdpal.cryptodock.band", "Crypto Tickers")
    {
        _tickerService = tickerService;
        _settingsManager = settingsManager;
        Icon = new IconInfo("\uE8D7");

        RebuildItems();
        _settingsManager.SymbolsChanged += OnSymbolsChanged;
        _settingsManager.SettingsChanged += OnSettingsChanged;
        _ = RunRefreshLoopAsync(_cancellation.Token);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _settingsManager.SymbolsChanged -= OnSymbolsChanged;
        _settingsManager.SettingsChanged -= OnSettingsChanged;
        foreach (CryptoTickerDockItem item in _items.Values)
        {
            item.Dispose();
        }

        _cancellation.Dispose();
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshAsync(cancellationToken);
                await _refreshSignal.WaitAsync(_settingsManager.RefreshInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return;
        }

        try
        {
            IReadOnlyList<CryptoTicker> tickers = await _tickerService.GetTickersAsync(
                _settingsManager.Symbols,
                cancellationToken);

            foreach (CryptoTicker ticker in tickers)
            {
                string key = new WatchedSymbol(ticker.Market, ticker.Symbol).Key;
                if (_items.TryGetValue(key, out CryptoTickerDockItem? item))
                {
                    item.Update(ticker);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            foreach (CryptoTickerDockItem item in _items.Values)
            {
                item.MarkOffline();
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void OnSymbolsChanged(object? sender, EventArgs e)
    {
        RebuildItems();
        SignalRefresh();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        SignalRefresh();
    }

    private void RebuildItems()
    {
        foreach (CryptoTickerDockItem item in _items.Values)
        {
            item.Dispose();
        }

        _items = _settingsManager.Symbols
            .ToDictionary(
                symbol => symbol.Key,
                symbol => new CryptoTickerDockItem(symbol));

        Items = _items.Values.ToArray();
    }

    private void SignalRefresh()
    {
        if (_cancellation.IsCancellationRequested || _refreshSignal.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _refreshSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }
}
