﻿
using System.Net.Http.Headers;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Totoro.Core.Helpers;
using static Totoro.Core.Services.AniList.AniListModelToAnimeModelConverter;

namespace Totoro.Core.Services.AniList;

public class AnilistService : IAnimeService, IAnilistService
{
    private readonly GraphQLHttpClient _anilistClient = new("https://graphql.anilist.co/", new NewtonsoftJsonSerializer());
    private readonly IAnimeIdService _animeIdService;
    private readonly ISettings _settings;

    public AnilistService(ILocalSettingsService localSettingsSerivce,
                          IAnimeIdService animeIdService,
                          ISettings settings)
    {
        _animeIdService = animeIdService;
        _settings = settings;
        var token = localSettingsSerivce.ReadSetting<AniListAuthToken>("AniListToken");
        if (!string.IsNullOrEmpty(token?.AccessToken))
        {
            _anilistClient.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }
    }

    public ListServiceType Type => ListServiceType.AniList;

    public async IAsyncEnumerable<AnimeModel> GetAiringAnime()
    {
        var response = await _anilistClient.SendQueryAsync<Query>(new GraphQL.GraphQLRequest
        {
            Query = new QueryQueryBuilder().WithPage(new PageQueryBuilder()
                .WithMedia(MediaQueryBuilder(), type: MediaType.Anime, status: MediaStatus.Releasing), page: 1, perPage: 20).Build()
        });

        if (response.Errors?.Length > 0)
        {
            yield break;
        }

        foreach (var item in response.Data.Page.Media.Where(FilterNsfw).Select(ConvertModel))
        {
            yield return item;
        }
    }

    public async IAsyncEnumerable<AnimeModel> GetAnime(string name)
    {
        var response = await _anilistClient.SendQueryAsync<Query>(new GraphQL.GraphQLRequest
        {
            Query = new QueryQueryBuilder().WithPage(new PageQueryBuilder()
                .WithMedia(MediaQueryBuilder(), search: name, type: MediaType.Anime), page: 1, perPage: 5).Build()
        });

        if (response.Errors?.Length > 0)
        {
            yield break;
        }

        foreach (var item in response.Data.Page.Media.Where(FilterNsfw).Select(ConvertModel))
        {
            yield return item;
        }
    }

    public async Task<AnimeModel> GetInformation(long id)
    {
        var query = new QueryQueryBuilder().WithMedia(MediaQueryBuilderFull(), id: (int)id,
                                                                               type: MediaType.Anime).Build();

        var response = await _anilistClient.SendQueryAsync<Query>(new GraphQL.GraphQLRequest
        {
            Query = query
        });

        return ConvertModel(response.Data.Media);
    }

    public async IAsyncEnumerable<AnimeModel> GetSeasonalAnime()
    {
        var current = AnimeHelpers.CurrentSeason();
        var prev = AnimeHelpers.PrevSeason();
        var next = AnimeHelpers.NextSeason();

        foreach (var season in new[] { current, prev, next })
        {
            var query = new QueryQueryBuilder().WithPage(new PageQueryBuilder()
                        .WithMedia(MediaQueryBuilder(), season: ConvertSeason(season.SeasonName),
                                                        seasonYear: season.Year,
                                                        type: MediaType.Anime), 1, 50).Build();

            var response = await _anilistClient.SendQueryAsync<Query>(new GraphQL.GraphQLRequest
            {
                Query = query
            });

            if (response.Errors?.Length > 0)
            {
                continue;
            }

            foreach (var item in response.Data.Page.Media.Where(FilterNsfw).Select(ConvertModel))
            {
                yield return item;
            }

        }
    }

    public async Task<string> GetBannerImage(long id)
    {
        var animeId = await _animeIdService.GetId(_settings.DefaultListService, ListServiceType.AniList, id);

        if (animeId is { AniList: null } or null)
        {
            return string.Empty;
        }

        var response = await _anilistClient.SendQueryAsync<Query>(new GraphQL.GraphQLRequest
        {
            Query = new QueryQueryBuilder().WithMedia(new MediaQueryBuilder()
                .WithBannerImage(), id: (int)animeId.AniList).Build()
        });

        if (response.Errors?.Length > 0)
        {
            return string.Empty;
        }

        return response.Data.Media.BannerImage;
    }

    public async Task<(int? Episode, DateTime? Time)> GetNextAiringEpisode(long id)
    {
        var animeId = await _animeIdService.GetId(_settings.DefaultListService, ListServiceType.AniList, id);

        if (animeId is { AniList: null } or null)
        {
            return (null, null);
        }

        var response = await _anilistClient.SendQueryAsync<Query>(new GraphQL.GraphQLRequest
        {
            Query = new QueryQueryBuilder().WithMedia(new MediaQueryBuilder()
                    .WithNextAiringEpisode(new AiringScheduleQueryBuilder()
                    .WithEpisode()
                    .WithTimeUntilAiring()), id: (int)animeId.AniList).Build()
        });

        if(response.Errors?.Length > 0)
        {
            return (null, null);
        }

        var ep = response.Data.Media.NextAiringEpisode?.Episode;
        var nextEp = response.Data.Media.NextAiringEpisode?.TimeUntilAiring;
        DateTime? dt = nextEp is null ? null : DateTime.Now + TimeSpan.FromSeconds(nextEp.Value);
        return (ep, dt);
    }

    private bool FilterNsfw(Media m)
    {
        if (_settings.IncludeNsfw)
        {
            return true;
        }

        return m.IsAdult is false or null;
    }

    private static MediaQueryBuilder MediaQueryBuilder()
    {
        return new MediaQueryBuilder()
            .WithId()
            .WithIdMal()
            .WithFormat()
            .WithTitle(new MediaTitleQueryBuilder()
                .WithEnglish()
                .WithNative()
                .WithRomaji())
            .WithCoverImage(new MediaCoverImageQueryBuilder()
                .WithLarge())
            .WithEpisodes()
            .WithStatus()
            .WithMeanScore()
            .WithPopularity()
            .WithDescription(asHtml: false)
            .WithTrailer(new MediaTrailerQueryBuilder()
                .WithSite()
                .WithThumbnail()
                .WithId())
            .WithGenres()
            .WithStartDate(new FuzzyDateQueryBuilder().WithAllFields())
            .WithEndDate(new FuzzyDateQueryBuilder().WithAllFields())
            .WithSeason()
            .WithSeasonYear()
            .WithBannerImage()
            .WithMediaListEntry(new MediaListQueryBuilder()
                .WithScore()
                .WithStatus()
                .WithStartedAt(new FuzzyDateQueryBuilder().WithAllFields())
                .WithCompletedAt(new FuzzyDateQueryBuilder().WithAllFields())
                .WithProgress())
            .WithIsAdult();
    }

    private static MediaQueryBuilder MediaQueryBuilderSimple()
    {
        return new MediaQueryBuilder()
            .WithId()
            .WithIdMal()
            .WithTitle(new MediaTitleQueryBuilder()
                .WithEnglish()
                .WithNative()
                .WithRomaji())
            .WithCoverImage(new MediaCoverImageQueryBuilder()
                .WithLarge())
            .WithType()
            .WithMediaListEntry(new MediaListQueryBuilder()
                .WithScore()
                .WithStatus()
                .WithStartedAt(new FuzzyDateQueryBuilder().WithAllFields())
                .WithCompletedAt(new FuzzyDateQueryBuilder().WithAllFields())
                .WithProgress())
            .WithStatus();
    }

    private static MediaQueryBuilder MediaQueryBuilderFull()
    {
        return new MediaQueryBuilder()
            .WithId()
            .WithIdMal()
            .WithTitle(new MediaTitleQueryBuilder()
                .WithEnglish()
                .WithNative()
                .WithRomaji())
            .WithCoverImage(new MediaCoverImageQueryBuilder()
                .WithLarge())
            .WithEpisodes()
            .WithStatus()
            .WithMeanScore()
            .WithPopularity()
            .WithDescription(asHtml: false)
            .WithTrailer(new MediaTrailerQueryBuilder()
                .WithSite()
                .WithThumbnail()
                .WithId())
            .WithGenres()
            .WithStartDate(new FuzzyDateQueryBuilder().WithAllFields())
            .WithEndDate(new FuzzyDateQueryBuilder().WithAllFields())
            .WithSeason()
            .WithSeasonYear()
            .WithBannerImage()
            .WithRelations(new MediaConnectionQueryBuilder()
                .WithNodes(MediaQueryBuilderSimple()))
            .WithRecommendations(new RecommendationConnectionQueryBuilder()
                .WithNodes(new RecommendationQueryBuilder()
                    .WithMediaRecommendation(MediaQueryBuilderSimple())))
            .WithMediaListEntry(new MediaListQueryBuilder()
                .WithScore()
                .WithStatus()
                .WithStartedAt(new FuzzyDateQueryBuilder().WithAllFields())
                .WithCompletedAt(new FuzzyDateQueryBuilder().WithAllFields())
                .WithProgress());
    }
}
