﻿using Flurl;
using HtmlAgilityPack.CssSelectors.NetCore;
using Totoro.Plugins.Anime.Contracts;
using Totoro.Plugins.Contracts.Optional;
using Totoro.Plugins.Helpers;
using Totoro.Plugins.Options;

namespace Totoro.Plugins.Anime.Anime4Up;

internal class Catalog : IAnimeCatalog
{
    class SearchResult : ICatalogItem, IHaveImage
    {
        required public string Title { get; init; }
        required public string Url { get; init; }
        required public string Image { get; init; }
    }


    public async IAsyncEnumerable<ICatalogItem> Search(string query)
    {
        var doc = await ConfigManager<Config>.Current.Url
            .SetQueryParams(new
            {
                search_param = "animes",
                s = query
            })
            .GetHtmlDocumentAsync();

        foreach (var item in doc.QuerySelectorAll(".anime-card-container"))
        {
            var imageNode = item.QuerySelector("img");
            var image = imageNode.Attributes["src"].Value;
            var title = imageNode.Attributes["alt"].Value;
            var url = item.QuerySelector("a").Attributes["href"].Value;

            yield return new SearchResult
            {
                Title = title,
                Image = image,
                Url = url
            };
        }
    }
}
