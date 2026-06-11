using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CryptoDock;

public sealed class CryptoTickerService : IDisposable
{
    public static readonly string[] DefaultSymbols = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"];
    private IReadOnlyList<CryptoSymbol>? _spotExchangeSymbols;
    private IReadOnlyList<CryptoSymbol>? _futuresExchangeSymbols;

    private readonly HttpClient _spotHttpClient = new()
    {
        BaseAddress = new Uri("https://api.binance.com"),
        Timeout = TimeSpan.FromSeconds(8),
    };

    private readonly HttpClient _futuresHttpClient = new()
    {
        BaseAddress = new Uri("https://fapi.binance.com"),
        Timeout = TimeSpan.FromSeconds(8),
    };

    public async Task<CryptoTicker> GetTickerAsync(WatchedSymbol symbol, CancellationToken cancellationToken = default)
    {
        HttpClient client = ClientFor(symbol.Market);
        using HttpResponseMessage response = await client.GetAsync($"{TickerPathFor(symbol.Market)}?symbol={symbol.Symbol}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        BinanceTickerResponse? ticker = await JsonSerializer.DeserializeAsync(
            stream,
            CryptoDockJsonContext.Default.BinanceTickerResponse,
            cancellationToken);

        if (ticker is null || string.IsNullOrWhiteSpace(ticker.LastPrice))
        {
            throw new InvalidOperationException($"Binance returned an empty ticker for {symbol.Symbol}.");
        }

        return CreateTicker(symbol.Market, ticker.Symbol ?? symbol.Symbol, ticker);
    }

    public async Task<IReadOnlyList<CryptoTicker>> GetTickersAsync(IEnumerable<WatchedSymbol> symbols, CancellationToken cancellationToken = default)
    {
        WatchedSymbol[] requestedSymbols = symbols.ToArray();
        if (requestedSymbols.Length == 0)
        {
            return [];
        }

        List<CryptoTicker> results = [];

        foreach (IGrouping<MarketKind, WatchedSymbol> marketGroup in requestedSymbols.GroupBy(symbol => symbol.Market))
        {
            if (marketGroup.Key == MarketKind.Futures)
            {
                foreach (WatchedSymbol symbol in marketGroup)
                {
                    results.Add(await GetTickerAsync(symbol, cancellationToken));
                }

                continue;
            }

            string[] marketSymbols = marketGroup.Select(symbol => symbol.Symbol).ToArray();
            string symbolsJson = JsonSerializer.Serialize(marketSymbols, CryptoDockJsonContext.Default.StringArray);
            string escapedSymbols = Uri.EscapeDataString(symbolsJson);

            HttpClient client = ClientFor(marketGroup.Key);
            using HttpResponseMessage response = await client.GetAsync($"{TickerPathFor(marketGroup.Key)}?symbols={escapedSymbols}", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            BinanceTickerResponse[]? tickers = await JsonSerializer.DeserializeAsync(
                stream,
                CryptoDockJsonContext.Default.BinanceTickerResponseArray,
                cancellationToken);

            if (tickers is null || tickers.Length == 0)
            {
                continue;
            }

            results.AddRange(tickers
                .Where(ticker => !string.IsNullOrWhiteSpace(ticker.Symbol) && !string.IsNullOrWhiteSpace(ticker.LastPrice))
                .Select(ticker => CreateTicker(marketGroup.Key, ticker.Symbol!, ticker)));
        }

        return results
            .OrderBy(ticker => Array.FindIndex(requestedSymbols, symbol => symbol.Market == ticker.Market && string.Equals(symbol.Symbol, ticker.Symbol, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static CryptoTicker CreateTicker(MarketKind market, string symbol, BinanceTickerResponse ticker)
    {
        return new CryptoTicker(
            market,
            symbol,
            ParseDecimal(ticker.LastPrice),
            ParseDecimal(ticker.PriceChangePercent),
            ParseDecimal(ticker.HighPrice),
            ParseDecimal(ticker.LowPrice),
            ParseDecimal(ticker.Volume),
            DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<CryptoSymbol>> SearchSymbolsAsync(string query, MarketSource source, CancellationToken cancellationToken = default)
    {
        query = query.Trim().Replace("/", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (query.Length == 0)
        {
            return [];
        }

        List<CryptoSymbol> symbols = [];

        if (source is MarketSource.Spot or MarketSource.Both)
        {
            symbols.AddRange(await GetExchangeSymbolsAsync(MarketKind.Spot, cancellationToken));
        }

        if (source is MarketSource.Futures or MarketSource.Both)
        {
            symbols.AddRange(await GetExchangeSymbolsAsync(MarketKind.Futures, cancellationToken));
        }

        return symbols
            .Where(symbol =>
                symbol.Symbol.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                symbol.BaseAsset.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(symbol => symbol.Symbol.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(symbol => symbol.Symbol.Length)
            .Take(25)
            .ToArray();
    }

    private async Task<IReadOnlyList<CryptoSymbol>> GetExchangeSymbolsAsync(MarketKind market, CancellationToken cancellationToken)
    {
        if (market == MarketKind.Spot && _spotExchangeSymbols is not null)
        {
            return _spotExchangeSymbols;
        }

        if (market == MarketKind.Futures && _futuresExchangeSymbols is not null)
        {
            return _futuresExchangeSymbols;
        }

        HttpClient client = ClientFor(market);
        using HttpResponseMessage response = await client.GetAsync(ExchangeInfoPathFor(market), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        BinanceExchangeInfoResponse? exchangeInfo = await JsonSerializer.DeserializeAsync(
            stream,
            CryptoDockJsonContext.Default.BinanceExchangeInfoResponse,
            cancellationToken);

        CryptoSymbol[] exchangeSymbols = exchangeInfo?.Symbols?
            .Where(symbol =>
                string.Equals(symbol.Status, "TRADING", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(symbol.QuoteAsset, "USDT", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(symbol.Symbol) &&
                !string.IsNullOrWhiteSpace(symbol.BaseAsset))
            .Select(symbol => new CryptoSymbol(market, symbol.Symbol!, symbol.BaseAsset!, symbol.QuoteAsset!))
            .OrderBy(symbol => symbol.Symbol)
            .ToArray() ?? [];

        if (market == MarketKind.Spot)
        {
            _spotExchangeSymbols = exchangeSymbols;
        }
        else
        {
            _futuresExchangeSymbols = exchangeSymbols;
        }

        return exchangeSymbols;
    }

    public void Dispose()
    {
        _spotHttpClient.Dispose();
        _futuresHttpClient.Dispose();
    }

    private HttpClient ClientFor(MarketKind market) => market == MarketKind.Futures ? _futuresHttpClient : _spotHttpClient;

    private static string TickerPathFor(MarketKind market) => market == MarketKind.Futures ? "/fapi/v1/ticker/24hr" : "/api/v3/ticker/24hr";

    private static string ExchangeInfoPathFor(MarketKind market) => market == MarketKind.Futures ? "/fapi/v1/exchangeInfo" : "/api/v3/exchangeInfo";

    private static decimal ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : 0m;
    }

    internal sealed record BinanceTickerResponse(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("lastPrice")] string? LastPrice,
        [property: JsonPropertyName("priceChangePercent")] string? PriceChangePercent,
        [property: JsonPropertyName("highPrice")] string? HighPrice,
        [property: JsonPropertyName("lowPrice")] string? LowPrice,
        [property: JsonPropertyName("volume")] string? Volume);

    internal sealed record BinanceExchangeInfoResponse(
        [property: JsonPropertyName("symbols")] BinanceSymbolResponse[]? Symbols);

    internal sealed record BinanceSymbolResponse(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("baseAsset")] string? BaseAsset,
        [property: JsonPropertyName("quoteAsset")] string? QuoteAsset);
}

public sealed record CryptoSymbol(MarketKind Market, string Symbol, string BaseAsset, string QuoteAsset)
{
    public string MarketLabel => Market == MarketKind.Futures ? "Futures" : "Spot";

    public string ShortMarketLabel => Market == MarketKind.Futures ? "F" : "S";

    public string Pair => $"{BaseAsset}/{QuoteAsset}";

    public WatchedSymbol ToWatchedSymbol() => new(Market, Symbol);
}

[JsonSerializable(typeof(CryptoTickerService.BinanceTickerResponse))]
[JsonSerializable(typeof(CryptoTickerService.BinanceTickerResponse[]))]
[JsonSerializable(typeof(CryptoTickerService.BinanceExchangeInfoResponse))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class CryptoDockJsonContext : JsonSerializerContext
{
}
