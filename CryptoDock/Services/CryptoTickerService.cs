using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CryptoDock;

internal sealed class CryptoTickerService : IDisposable
{
    private readonly SettingsManager _settingsManager;

    public CryptoTickerService(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

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

    private readonly HttpClient _bapiHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private readonly HttpClient _coingeckoHttpClient = CreateClientWithUserAgent();
    private readonly HttpClient _githubHttpClient = CreateClientWithUserAgent();

    private static HttpClient CreateClientWithUserAgent()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CryptoDock/2.0");
        return client;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BapiAssetInfo> _bapiCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> _coingeckoCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _githubLogoCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _bapiLoaded = false;
    private readonly SemaphoreSlim _bapiLock = new(1, 1);

    private record BapiAssetInfo(string AssetName, string LogoUrl);

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

        return await CreateTickerAsync(symbol.Market, ticker.Symbol ?? symbol.Symbol, ticker, cancellationToken);
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

            var tasks = tickers
                .Where(ticker => !string.IsNullOrWhiteSpace(ticker.Symbol) && !string.IsNullOrWhiteSpace(ticker.LastPrice))
                .Select(ticker => CreateTickerAsync(marketGroup.Key, ticker.Symbol!, ticker, cancellationToken));
            
            results.AddRange(await Task.WhenAll(tasks));
        }

        return results
            .OrderBy(ticker => Array.FindIndex(requestedSymbols, symbol => symbol.Market == ticker.Market && string.Equals(symbol.Symbol, ticker.Symbol, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private async Task EnsureBapiLoadedAsync(CancellationToken cancellationToken)
    {
        if (_bapiLoaded) return;

        await _bapiLock.WaitAsync(cancellationToken);
        try
        {
            if (_bapiLoaded) return;

            using var response = await _bapiHttpClient.GetAsync("https://www.binance.com/bapi/asset/v2/public/asset/asset/get-all-asset", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                
                if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement) && dataElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement asset in dataElement.EnumerateArray())
                    {
                        if (asset.TryGetProperty("assetCode", out JsonElement codeElement) && codeElement.ValueKind == JsonValueKind.String &&
                            asset.TryGetProperty("assetName", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String &&
                            asset.TryGetProperty("logoUrl", out JsonElement logoElement) && logoElement.ValueKind == JsonValueKind.String)
                        {
                            _bapiCache[codeElement.GetString()!] = new BapiAssetInfo(nameElement.GetString()!, logoElement.GetString()!);
                        }
                    }
                }
            }
            _bapiLoaded = true;
        }
        catch
        {
            // Ignore errors
        }
        finally
        {
            _bapiLock.Release();
        }
    }

    private async Task<CryptoTicker> CreateTickerAsync(MarketKind market, string symbol, BinanceTickerResponse ticker, CancellationToken cancellationToken)
    {
        string baseAsset = symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) 
            ? symbol[..^4].ToUpperInvariant() 
            : symbol.ToUpperInvariant();

        await EnsureBapiLoadedAsync(cancellationToken);

        bool hasLogo = false;
        string? logoUrl = null;
        string? fullName = null;

        if (_bapiCache.TryGetValue(baseAsset, out BapiAssetInfo? info))
        {
            fullName = info.AssetName;
            logoUrl = info.LogoUrl;
            hasLogo = !string.IsNullOrWhiteSpace(logoUrl);
        }
        else
        {
            if (_coingeckoCache.TryGetValue(baseAsset, out string? cachedCgUrl))
            {
                logoUrl = cachedCgUrl;
                hasLogo = !string.IsNullOrWhiteSpace(logoUrl);
            }
            else
            {
                try
                {
                    using var cgRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.coingecko.com/api/v3/search?query={baseAsset}");
                    using var cgResponse = await _coingeckoHttpClient.SendAsync(cgRequest, cancellationToken);
                    if (cgResponse.IsSuccessStatusCode)
                    {
                        await using Stream stream = await cgResponse.Content.ReadAsStreamAsync(cancellationToken);
                        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                        
                        if (doc.RootElement.TryGetProperty("coins", out JsonElement coinsElement) && coinsElement.ValueKind == JsonValueKind.Array && coinsElement.GetArrayLength() > 0)
                        {
                            JsonElement bestMatch = coinsElement[0];
                            foreach (JsonElement coin in coinsElement.EnumerateArray())
                            {
                                if (coin.TryGetProperty("symbol", out JsonElement symElement) && 
                                    symElement.ValueKind == JsonValueKind.String && 
                                    string.Equals(symElement.GetString(), baseAsset, StringComparison.OrdinalIgnoreCase))
                                {
                                    bestMatch = coin;
                                    break;
                                }
                            }

                            if (bestMatch.TryGetProperty("large", out JsonElement largeElement) && largeElement.ValueKind == JsonValueKind.String)
                            {
                                logoUrl = largeElement.GetString();
                                hasLogo = !string.IsNullOrWhiteSpace(logoUrl);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore CoinGecko errors
                }
                _coingeckoCache[baseAsset] = logoUrl;
            }

            if (!hasLogo)
            {
                logoUrl = $"https://raw.githubusercontent.com/spothq/cryptocurrency-icons/master/128/color/{baseAsset.ToLowerInvariant()}.png";
                if (_githubLogoCache.TryGetValue(baseAsset, out bool cachedValue))
                {
                    hasLogo = cachedValue;
                }
                else
                {
                    try
                    {
                        using var ghRequest = new HttpRequestMessage(HttpMethod.Head, logoUrl);
                        using var ghResponse = await _githubHttpClient.SendAsync(ghRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        hasLogo = ghResponse.IsSuccessStatusCode;
                    }
                    catch
                    {
                        hasLogo = false;
                    }
                    _githubLogoCache[baseAsset] = hasLogo;
                }
            }
        }

        return new CryptoTicker(
            market,
            symbol,
            ParseDecimal(ticker.LastPrice),
            ParseDecimal(ticker.PriceChangePercent),
            ParseDecimal(ticker.HighPrice),
            ParseDecimal(ticker.LowPrice),
            ParseDecimal(ticker.Volume),
            DateTimeOffset.Now,
            hasLogo,
            logoUrl,
            fullName);
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
