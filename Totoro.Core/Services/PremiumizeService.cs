﻿using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AnimDL.Core.Helpers;

namespace Totoro.Core.Services;

public class DirectDownloadLink
{
    [JsonPropertyName("path")]
    public string FileName { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; }

    [JsonPropertyName("stream_link")]
    public string StreamLink { get; set; }
}

public class Transfer
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("progress")]
    public string Progress { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }
}

public class PremiumizeService : IPremiumizeService
{
    public const string Key = "PremiumizeKey";
    private readonly HttpClient _httpClient;
    private readonly string _api = @"https://www.premiumize.me/api";
    private string _apiKey;

    public PremiumizeService(HttpClient httpClient,
                             ISettings settings)
    {
        _httpClient = httpClient;
        _apiKey = settings.PremiumizeKey;
    }

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
    }

    public bool IsAuthenticated => !string.IsNullOrEmpty(_apiKey);

    public async Task<bool> Check(string magneticLink)
    {
        if(string.IsNullOrEmpty(_apiKey))
        {
            return false;
        }

        var json = await _httpClient.GetStringAsync(_api + "/cache/check", new Dictionary<string, string>
        {
            ["apikey"] = _apiKey,
            ["items[]"] = magneticLink
        });
        var jObject = JsonNode.Parse(json);
        return jObject?["response"]?.AsArray()?.Any(x => (bool)x.AsValue()) ?? false;
    }

    public async Task<IEnumerable<DirectDownloadLink>> GetDirectDownloadLinks(string magneticLink)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return Enumerable.Empty<DirectDownloadLink>();
        }

        var json = await _httpClient.PostFormUrlEncoded(_api + $"/transfer/directdl?apikey={_apiKey}", new Dictionary<string, string>
        {
            ["src"] = magneticLink,
        });

        var jObject = JsonNode.Parse(json);
        return jObject?["content"]?.Deserialize<List<DirectDownloadLink>>() ?? Enumerable.Empty<DirectDownloadLink>();
    }

    public async Task<string> CreateTransfer(string magneticLink)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return string.Empty;
        }

        var json = await _httpClient.PostFormUrlEncoded(_api + $"/transfer/create?apikey={_apiKey}", new Dictionary<string, string>
        {
            ["src"] = magneticLink,
        });

        var jObject = JsonNode.Parse(json);

        return jObject?["id"]?.ToString() ?? string.Empty;
    }

    public async Task<IEnumerable<Transfer>> GetTransfers()
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return Enumerable.Empty<Transfer>();
        }

        var json = await _httpClient.GetStringAsync(_api + "/transfer/list", new Dictionary<string, string>
        {
            ["apikey"] = _apiKey,
        });
        var jObject = JsonNode.Parse(json);

        return jObject?["transfers"]?.Deserialize<List<Transfer>>() ?? Enumerable.Empty<Transfer>();
    }
}
