﻿using System.Diagnostics;
using MonoTorrent;
using MonoTorrent.Client;
using Splat;
using Totoro.Core.Torrents.Rss;

namespace Totoro.Core.Services;

internal class RssDownloader : IRssDownloader, IEnableLogger
{
    public const string HashKey = "InfoHashes";
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ITorrentEngine _torrentEngine;
    private readonly ISettings _settings;
    private readonly IToastService _toastService;
    private readonly ITrackingServiceContext _trackingServiceContext;
    private readonly List<InfoHash> _infoHashes;
    private readonly List<AnimeModel> _watchingAnime = [];

    public IList<RssFeed> Feeds { get; }

    public RssDownloader(ILocalSettingsService localSettingsService,
                         ITorrentEngine torrentEngine,
                         ISettings settings,
                         IToastService toastService,
                         ITrackingServiceContext trackingServiceContext)
    {
        _localSettingsService = localSettingsService;
        _torrentEngine = torrentEngine;
        _settings = settings;
        _toastService = toastService;
        _trackingServiceContext = trackingServiceContext;
        _infoHashes = localSettingsService.ReadSetting<List<string>>(HashKey, []).Select(InfoHash.FromHex).ToList();

        Feeds =
        [
            new RssFeed(new RssFeedOptions()
            {
                Url = "https://subsplease.org/rss/?r=1080",
                IsEnabled = settings.AutoDownloadTorrents
            })
        ];
    }

    public async Task Initialize()
    {
        foreach (var item in _torrentEngine.TorrentManagers.Where(x => !x.Complete && _infoHashes.Contains(x.InfoHashes.V1OrV2)))
        {
            item.TorrentStateChanged += TorrentManager_TorrentStateChanged;
            await item.StartAsync();
        }

        _trackingServiceContext
            .GetCurrentlyAiringTrackedAnime()
            .ToObservable()
            .Finally(() =>
            {
                foreach (var feed in Feeds)
                {
                    feed.OnNew.Subscribe(async x => await OnNewTorrentAvailable(x));
                    feed.Start();
                }
            })
            .Subscribe(_watchingAnime.Add);
    }

    public void SaveState()
    {
        _localSettingsService.SaveSetting(HashKey, _infoHashes.Select(x => x.ToHex()));
    }

    private async Task OnNewTorrentAvailable(RssFeedItem item)
    {
        var parseResult = Anitomy.Anitomy.Parse(item.Title);
        var title = parseResult.FirstOrDefault(x => x.Category == Anitomy.ElementCategory.AnimeTitle)?.Value;
        var episode = parseResult.FirstOrDefault(x => x.Category == Anitomy.ElementCategory.EpisodeNumber)?.Value;

        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        if (_watchingAnime.FirstOrDefault(x => x.Title.Contains(title, StringComparison.CurrentCultureIgnoreCase) || (x.AlternativeTitles?.Any(x => x.Contains(title, StringComparison.CurrentCultureIgnoreCase)) ?? true)) is not { } anime)
        {
            return;
        }

        if (!int.TryParse(episode, out int ep))
        {
            return;
        }

        if (ep <= anime.Tracking.WatchedEpisodes)
        {
            return;
        }

        this.Log().Info("New torrent available {0}, Episode {1}", title, episode);

        var saveDirectory = Path.Combine(_settings.UserTorrentsDownloadDirectory, title);

        var torrentManager = item switch
        {
            TorrentRssFeedItem trfi => await _torrentEngine.DownloadFromUrl(trfi.Torrent, saveDirectory),
            MagnetRssFeedItem mrfi => await _torrentEngine.DownloadFromMagnet(mrfi.Magnet, saveDirectory),
            _ => throw new UnreachableException()
        };

        torrentManager.TorrentStateChanged += TorrentManager_TorrentStateChanged;
        _infoHashes.Add(torrentManager.InfoHashes.V1OrV2);
    }

    private void TorrentManager_TorrentStateChanged(object sender, TorrentStateChangedEventArgs e)
    {
        if (e.NewState != TorrentState.Seeding)
        {
            return;
        }

        var torrentManager = (TorrentManager)sender;

        _infoHashes.Remove(torrentManager.InfoHashes.V1OrV2);
        _toastService.DownloadCompleted(torrentManager.SavePath, torrentManager.Torrent.Name);
    }
}
