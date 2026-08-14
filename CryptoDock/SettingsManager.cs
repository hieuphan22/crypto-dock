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

    private readonly ChoiceSetSetting _volatilityAlertsEnabled = new(
        Namespaced(nameof(VolatilityAlertsEnabled)),
        "Volatility alerts",
        "Show dock alerts for large price moves. Price history is collected only while enabled.",
        [
            new ChoiceSetSetting.Choice("Off", "false"),
            new ChoiceSetSetting.Choice("On", "true"),
        ]);

    private readonly ChoiceSetSetting _volatilityWindow = new(
        Namespaced(nameof(VolatilityWindowMinutes)),
        "Alert analysis window",
        "Compare price action across this rolling window.",
        [
            new ChoiceSetSetting.Choice("3 minutes", "3"),
            new ChoiceSetSetting.Choice("5 minutes", "5"),
            new ChoiceSetSetting.Choice("15 minutes", "15"),
            new ChoiceSetSetting.Choice("1 hour", "60"),
        ]);

    private readonly ChoiceSetSetting _dumpThreshold = new(
        Namespaced(nameof(DumpThresholdPercent)),
        "Dump warning threshold",
        "Alert when the current price falls this far from the window high.",
        PercentageChoices("1", "3", "5", "10"));

    private readonly ChoiceSetSetting _reboundThreshold = new(
        Namespaced(nameof(ReboundThresholdPercent)),
        "Rebound warning threshold",
        "Alert when the current price rises this far from the window low.",
        PercentageChoices("1", "3", "5", "10"));

    private readonly ChoiceSetSetting _rangeThreshold = new(
        Namespaced(nameof(RangeThresholdPercent)),
        "Volatility range threshold",
        "Alert when the high-to-low range reaches this size within the window.",
        PercentageChoices("1", "2", "5", "10", "15"));

    public int RefreshIntervalSeconds =>
        int.TryParse(_refreshInterval.Value, out int seconds) ? seconds : 10;

    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(RefreshIntervalSeconds);

    public MarketSource MarketSource =>
        Enum.TryParse(_marketSource.Value, out MarketSource source) ? source : MarketSource.Both;

    public bool VolatilityAlertsEnabled => bool.TryParse(_volatilityAlertsEnabled.Value, out bool enabled) && enabled;

    public int VolatilityWindowMinutes => ParseChoice(_volatilityWindow.Value, 15);

    public decimal DumpThresholdPercent => ParseDecimalChoice(_dumpThreshold.Value, 3m);

    public decimal ReboundThresholdPercent => ParseDecimalChoice(_reboundThreshold.Value, 3m);

    public decimal RangeThresholdPercent => ParseDecimalChoice(_rangeThreshold.Value, 5m);

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

        _volatilityWindow.Value = "15";
        _dumpThreshold.Value = "3";
        _reboundThreshold.Value = "3";
        _rangeThreshold.Value = "5";

        Settings.Add(_refreshInterval);
        Settings.Add(_marketSource);
        Settings.Add(_volatilityAlertsEnabled);
        Settings.Add(_volatilityWindow);
        Settings.Add(_dumpThreshold);
        Settings.Add(_reboundThreshold);
        Settings.Add(_rangeThreshold);
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

    public bool MoveSymbolUp(WatchedSymbol symbol)
    {
        symbol = new WatchedSymbol(symbol.Market, WatchedSymbol.NormalizeSymbol(symbol.Symbol));
        bool moved = false;
        lock (_symbolsLock)
        {
            int index = _symbols.FindIndex(existing => existing.Market == symbol.Market && string.Equals(existing.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase));
            if (index > 0)
            {
                var item = _symbols[index];
                _symbols.RemoveAt(index);
                _symbols.Insert(index - 1, item);
                SaveSymbols();
                moved = true;
            }
        }
        if (moved) SymbolsChanged?.Invoke(this, EventArgs.Empty);
        return moved;
    }

    public bool MoveSymbolDown(WatchedSymbol symbol)
    {
        symbol = new WatchedSymbol(symbol.Market, WatchedSymbol.NormalizeSymbol(symbol.Symbol));
        bool moved = false;
        lock (_symbolsLock)
        {
            int index = _symbols.FindIndex(existing => existing.Market == symbol.Market && string.Equals(existing.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index < _symbols.Count - 1)
            {
                var item = _symbols[index];
                _symbols.RemoveAt(index);
                _symbols.Insert(index + 1, item);
                SaveSymbols();
                moved = true;
            }
        }
        if (moved) SymbolsChanged?.Invoke(this, EventArgs.Empty);
        return moved;
    }

    private static string SettingsJsonPath()
    {
#if DEBUG
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cryptoDock");
#else
        string directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
#endif
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{Namespace}.settings.json");
    }

    private static string SymbolsPath()
    {
#if DEBUG
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cryptoDock");
#else
        string directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
#endif
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

    private static List<ChoiceSetSetting.Choice> PercentageChoices(params string[] values) =>
        values.Select(value => new ChoiceSetSetting.Choice($"{value}%", value)).ToList();

    private static int ParseChoice(string? value, int fallback) =>
        int.TryParse(value, out int parsed) ? parsed : fallback;

    private static decimal ParseDecimalChoice(string? value, decimal fallback) =>
        decimal.TryParse(value, out decimal parsed) ? parsed : fallback;
}
