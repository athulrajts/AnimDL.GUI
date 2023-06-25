﻿using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Text.Json;
using CommunityToolkit.WinUI.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Serilog;
using Splat;
using Splat.Serilog;
using Totoro.Core;
using Totoro.Plugins;
using Totoro.Plugins.Anime.Contracts;
using Totoro.Plugins.Contracts;
using Totoro.Plugins.MediaDetection.Contracts;
using Totoro.Plugins.Torrents.Contracts;
using Totoro.WinUI.Helpers;
using Totoro.WinUI.Models;
using Totoro.WinUI.Services;
using Totoro.WinUI.ViewModels;
using Windows.ApplicationModel;
using WinUIEx;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Totoro.WinUI;

public partial class App : Application, IEnableLogger
{
    private static readonly IHost _host = Host
        .CreateDefaultBuilder()
        .ConfigureAppConfiguration(config =>
        {
            if (RuntimeHelper.IsMSIX)
            {
                config.SetBasePath(Package.Current.InstalledLocation.Path)
                      .AddJsonFile("appsettings.json");
            }
        })
        .ConfigureServices((context, services) =>
        {
            MessageBus.Current.RegisterMessageSource(Observable.Timer(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)).Select(_ => new MinuteTick()));

            services.AddPlatformServices()
                    .AddTotoro()
                    .AddPlugins()
                    .AddMyAnimeList()
                    .AddAniList()
                    .AddTorrenting()
                    .AddMediaDetection()
                    .AddTopLevelPages()
                    .AddDialogPages();

            services.AddSingleton(MessageBus.Current);
            services.AddTransient<DefaultExceptionHandler>();
            services.AddSingleton<IPluginManager>(x => new PluginManager(PluginFactory<AnimeProvider>.Instance,
                                                                         PluginFactory<ITorrentTracker>.Instance,
                                                                         PluginFactory<INativeMediaPlayer>.Instance));

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        })
        .Build();

    public static TotoroCommands Commands { get; private set; }

    public static T GetService<T>()
        where T : class
    {
        try
        {
            return _host.Services.GetService(typeof(T)) as T;
        }
        catch (Exception ex)
        {
            LogHost.Default.Error(ex, ex.Message);
            throw;
        }
    }

    public static object GetService(Type t) => _host.Services.GetService(t);

    public static WindowEx MainWindow { get; set; }

    public App()
    {
        InitializeComponent();
        ToastNotificationManagerCompat.OnActivated += ToastNotificationManagerCompat_OnActivated;
        AppDomain.CurrentDomain.ProcessExit += OnExit;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        UnhandledException += App_UnhandledException;
        DebugSettings.XamlResourceReferenceFailed += (_, e) => this.Log().Fatal(e.Message);
        DebugSettings.BindingFailed += (_, e) => this.Log().Warn(e.Message);
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        NativeMethods.AllowSleep();
        this.Log().Fatal(e.ExceptionObject);
    }

    private void OnExit(object sender, EventArgs e)
    {
        ToastNotificationManagerCompat.Uninstall();
    }

    private async void ToastNotificationManagerCompat_OnActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);

            if (!args.Contains("Type"))
            {
                return;
            }

            switch (args.GetEnum<ToastType>("Type"))
            {
                case ToastType.DownloadComplete:
                    Process.Start(new ProcessStartInfo { FileName = args.Get("File"), UseShellExecute = true });
                    break;
                case ToastType.FinishedEpisode:
                    var anime = JsonSerializer.Deserialize<AnimeModel>(args.Get("Payload"));
                    var ep = args.GetInt("Episode");
                    var trackingService = GetService<ITrackingServiceContext>();
                    await trackingService.Update(anime.Id, Tracking.WithEpisode(anime, ep));
                    break;
                case ToastType.SelectAnime:
                    var id = long.Parse((string)e.UserInput["animeId"]);
                    var nowPlayingViewModel = GetService<NowPlayingViewModel>();
                    RxApp.MainThreadScheduler.Schedule(async () => await nowPlayingViewModel.SetAnime(id));
                    break;
            }
        }
        catch (Exception ex)
        {
            this.Log().Error(ex);
        }
    }


    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        NativeMethods.AllowSleep();
        this.Log().Fatal(e.Exception, e.Message);
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow() { Title = "AppDisplayName".GetLocalized() };
        base.OnLaunched(args);
        RxApp.DefaultExceptionHandler = GetService<DefaultExceptionHandler>();
        Commands = GetService<TotoroCommands>();
        ConfigureLogging();
        var activationService = GetService<IActivationService>();
        await activationService.ActivateAsync(args);
    }

    private static void ConfigureLogging()
    {
        var knownFolders = GetService<IKnownFolders>();
        var log = System.IO.Path.Combine(knownFolders.Logs, "log.txt");
        var mimimumLogLevel = GetService<ISettings>().MinimumLogLevel;
        var configuration = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .WriteTo.File(log, rollingInterval: RollingInterval.Day);

        switch (mimimumLogLevel)
        {
            case LogLevel.Debug:
                configuration.MinimumLevel.Debug();
                break;
            case LogLevel.Information:
                configuration.MinimumLevel.Information();
                break;
            case LogLevel.Warning:
                configuration.MinimumLevel.Warning();
                break;
            case LogLevel.Error:
                configuration.MinimumLevel.Error();
                break;
            case LogLevel.Critical:
                configuration.MinimumLevel.Fatal();
                break;
        }

        Log.Logger = configuration.CreateLogger();
        Locator.CurrentMutable.UseSerilogFullLogger();
    }
}
