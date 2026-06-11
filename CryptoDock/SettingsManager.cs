using System.IO;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace CryptoDock;

internal sealed class SettingsManager : JsonSettingsManager
{
    private const string Namespace = "cryptoDock";
    private static readonly WatchedSymbol[] DefaultSymbols =
    [
        WatchedSymbol.Spot("BTCUSDT"),
        WatchedSymbol.Spot("ETHUSDT"),
        WatchedSymbol.Spot("BNBUSDT"),
        WatchedSymbol.Spot("SOLUSDT"),
        WatchedSymbol.Spot("XRPUSDT"),
    ];

    private readonly object _symbolsLock = new();
    private List<WatchedSymbol> _symbols = [];

    public event EventHandler? SymbolsChanged;
    public event EventHandler? SettingsChanged;

    private static string Namespaced(string propertyName) => $"{Namespace}.{propertyName}";

    private readonly ChoiceSetSetting _refreshInterval = new(
        Namespaced(nameof(RefreshIntervalSeconds)),
        "Refresh interval",
        "How often Crypto Dock updates Binance prices.",
        [
            new ChoiceSetSetting.Choice("10 seconds", "10"),
            new ChoiceSetSetting.Choice("30 seconds", "30"),
            new ChoiceSetSetting.Choice("1 minute", "60"),
            new ChoiceSetSetting.Choice("5 minutes", "300"),
        ]);

    private readonly ChoiceSetSetting _marketSource = new(
        Namespaced(nameof(MarketSource)),
        "Search market",
        "Choose which Binance market Crypto Dock searches when adding symbols.",
        [
            new ChoiceSetSetting.Choice("Spot + Futures", "Both"),
            new ChoiceSetSetting.Choice("Spot only", "Spot"),
            new ChoiceSetSetting.Choice("Futures only", "Futures"),
        ]);

    public int RefreshIntervalSeconds =>
        int.TryParse(_refreshInterval.Value, out int seconds) ? seconds : 10;

    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(RefreshIntervalSeconds);

    public MarketSource MarketSource =>
        Enum.TryParse(_marketSource.Value, out MarketSource source) ? source : MarketSource.Both;

    public IReadOnlyList<WatchedSymbol> Symbols
    {
        get
        {
            lock (_symbolsLock)
            {
                return _symbols.ToArray();
            }
        }
    }

    public SettingsManager()
    {
        FilePath = SettingsJsonPath();

        Settings.Add(_refreshInterval);
        Settings.Add(_marketSource);
        LoadSettings();
        LoadSymbols();

        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public bool AddSymbol(WatchedSymbol symbol)
    {
        symbol = new WatchedSymbol(symbol.Market, WatchedSymbol.NormalizeSymbol(symbol.Symbol));
        if (string.IsNullOrWhiteSpace(symbol.Symbol))
        {
            return false;
        }

        lock (_symbolsLock)
        {
            if (_symbols.Any(existing => existing.Market == symbol.Market && string.Equals(existing.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _symbols.Add(symbol);
            SaveSymbols();
        }

        SymbolsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RemoveSymbol(WatchedSymbol symbol)
    {
        symbol = new WatchedSymbol(symbol.Market, WatchedSymbol.NormalizeSymbol(symbol.Symbol));
        bool removed;

        lock (_symbolsLock)
        {
            removed = _symbols.RemoveAll(existing => existing.Market == symbol.Market && string.Equals(existing.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                SaveSymbols();
            }
        }

        if (removed)
        {
            SymbolsChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    private static string SettingsJsonPath()
    {
        string directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Namespace}.settings.json");
    }

    private static string SymbolsPath()
    {
        string directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Namespace}.symbols.txt");
    }

    private void LoadSymbols()
    {
        string path = SymbolsPath();
        if (!File.Exists(path))
        {
            _symbols = DefaultSymbols.ToList();
            SaveSymbols();
            return;
        }

        _symbols = File.ReadAllLines(path)
            .Select(WatchedSymbol.Parse)
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol.Symbol))
            .DistinctBy(symbol => symbol.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (_symbols.Count == 0)
        {
            _symbols = DefaultSymbols.ToList();
            SaveSymbols();
        }
    }

    private void SaveSymbols()
    {
        File.WriteAllLines(SymbolsPath(), _symbols.Select(symbol => symbol.Key));
    }
}
