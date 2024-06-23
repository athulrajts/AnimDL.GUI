﻿using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Flurl;
using Totoro.Plugins.Options;
using Xunit.Abstractions;

namespace Totoro.Plugins.Anime.Anime4Up.Tests;

[ExcludeFromCodeCoverage]
public class StreamProviderTests
{
    public const string MushokuTensei = "mushoku-tensei";

    private readonly ITestOutputHelper _output;
    private readonly JsonSerializerOptions _searializerOption = new() { WriteIndented = true };
    private readonly Dictionary<string, string> _urlMap = new()
    {
        { MushokuTensei, Url.Combine(ConfigManager<Config>.Current.Url, "/anime/kimetsu-no-yaiba-hashira-geiko-hen/") },
    };
    private readonly bool _allEpisodes = false;

    public StreamProviderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(MushokuTensei, 12)]
    public async Task GetNumberOfEpisodes(string key, int expected)
    {
        // arrange
        var url = _urlMap[key];
        var sut = new StreamProvider();

        // act
        var actual = await sut.GetNumberOfStreams(url);

        // assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(MushokuTensei)]
    public async Task GetStreams(string key)
    {
        // arrange
        var url = _urlMap[key];
        var sut = new StreamProvider();

        // act
        var result = await sut.GetStreams(url, _allEpisodes ? Range.All : 1..1).ToListAsync();

        Assert.NotEmpty(result);
        foreach (var item in result)
        {
            _output.WriteLine(JsonSerializer.Serialize(item, item.GetType(), _searializerOption));
        }
    }
}
