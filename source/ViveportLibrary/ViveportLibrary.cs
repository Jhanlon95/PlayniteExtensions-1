﻿using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteExtensions.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using ViveportLibrary.Api;

namespace ViveportLibrary
{
    public class ViveportLibrary : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public static readonly string IconPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "icon.png");
        public override string LibraryIcon { get; } = IconPath;

        private ViveportLibrarySettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("97d85dbd-ad52-4834-bf4b-f6681f1445cc");

        public override string Name { get; } = "Viveport";

        public override LibraryClient Client { get; } = new ViveportLibraryClient();

        private IAppDataReader AppDataReader { get; }

        public ViveportLibrary(IPlayniteAPI api) : base(api)
        {
            settings = new ViveportLibrarySettingsViewModel(this);
            Properties = new LibraryPluginProperties
            {
                HasSettings = true
            };
            AppDataReader = new AppDataReader();
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var installedApps = AppDataReader.GetInstalledApps();
            if (installedApps == null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage("viveport-installed-app-reader-error", "Couldn't read the locally installed Viveport apps. Check your Viveport desktop client installation.", NotificationType.Error));
                yield break;
            }

            var allAppMetadata = AppDataReader.GetAppMetadata();
            if (allAppMetadata == null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage("viveport-installed-app-reader-error", "Couldn't read the Viveport licensed apps. Check your Viveport desktop client installation.", NotificationType.Error));
                yield break;
            }

            var allLicenseData = AppDataReader.GetLicenseData();

            var installedDict = installedApps.ToDictionary(x => x.AppId);
            var metadataDict = allAppMetadata.ToDictionarySafe(x => x.Id, favorBiggerObject: true);
            var licenseDict = allLicenseData?.ToDictionarySafe(x => x.AppId) ?? new Dictionary<string, LicenseData>();

            var keys = metadataDict.Keys.ToHashSet();
            foreach (var key in installedDict.Keys)
                keys.Add(key);

            foreach (var key in keys)
            {
                installedDict.TryGetValue(key, out var installedAppData);

                if (!metadataDict.TryGetValue(key, out var appMetadata))
                    logger.Warn($"Couldn't find metadata for app {key} ({installedAppData?.Title})");

                var game = new GameMetadata
                {
                    GameId = key,
                    Name = appMetadata.Title ?? installedAppData.Title,
                    InstallDirectory = installedAppData?.Path,
                    IsInstalled = installedAppData != null,
                    Source = new MetadataNameProperty("Viveport"),
                };

                if (settings.Settings.TagSubscriptionGames
                    && !string.IsNullOrWhiteSpace(settings.Settings.SubscriptionTagName)
                    && licenseDict.TryGetValue(key, out var licenseData)
                    && licenseData.Licensing == "rsu")
                {
                    game.Tags = new HashSet<MetadataProperty> { new MetadataNameProperty(settings.Settings.SubscriptionTagName) };
                }

                yield return game;
            }
        }

        public static string SplitPascalCase(string pascalCaseStr)
        {
            StringBuilder output = new StringBuilder();
            for (int i = 0; i < pascalCaseStr.Length; i++)
            {
                char a = pascalCaseStr[i];

                if (i + 1 < pascalCaseStr.Length)
                {
                    char b = pascalCaseStr[i + 1];
                    if (char.IsLower(a) && char.IsUpper(b))
                    {
                        output.Append(a);
                        output.Append(' ');
                        output.Append(b);
                        i++;
                        continue;
                    }
                    if (i + 3 < pascalCaseStr.Length)
                    {
                        char c = pascalCaseStr[i + 2];
                        char d = pascalCaseStr[i + 3];
                        if (a == 'H' && b == 't' && c == 'c' && char.IsUpper(d))
                        {
                            output.Append("HTC " + d);
                            i += 3;
                            continue;
                        }
                    }
                }
                output.Append(a);
            }
            return output.ToString();
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new ViveportLibrarySettingsView();
        }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id)
                yield break;

            yield return new AutomaticPlayController(args.Game)
            {
                Name = "Start via Viveport",
                Path = $"vive://runapp/{args.Game.GameId}",
                Type = AutomaticPlayActionType.Url,
                WorkingDir = args.Game.InstallDirectory,
                TrackingMode = TrackingMode.Directory,
                TrackingPath = args.Game.InstallDirectory,
            };
        }
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem { MenuSection = "@Viveport", Description = $"Tag all Viveport Infinity games as {settings?.Settings?.SubscriptionTagName}", Action = a => SetSubscriptionTags() };
        }

        public override LibraryMetadataProvider GetMetadataDownloader()
        {
            return new ViveportMetadataProvider(new ViveportApiClient(), settings.Settings);
        }

        public void SetSubscriptionTags()
        {
            if (string.IsNullOrWhiteSpace(settings.Settings.SubscriptionTagName))
            {
                PlayniteApi.Dialogs.ShowErrorMessage("Tag name setting is empty", "Error setting tag");
                return;
            }

            var games = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();
            PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
            {
                a.ProgressMaxValue = games.Count + 1;
                a.CurrentProgressValue++;
                var licenses = AppDataReader.GetLicenseData().ToDictionary(x => x.AppId);

                using (PlayniteApi.Database.BufferedUpdate())
                {
                    var subscriptionTag = PlayniteApi.Database.Tags.Add(settings.Settings.SubscriptionTagName);

                    foreach (var game in games)
                    {
                        a.CurrentProgressValue++;
                        if (a.CancelToken.IsCancellationRequested)
                            break;

                        if (!licenses.TryGetValue(game.GameId, out var license))
                            continue;

                        if (license.Licensing == "rsu" && game.TagIds?.Contains(subscriptionTag.Id) == false)
                        {
                            var tagIds = game.TagIds ?? new List<Guid>();
                            tagIds.Add(subscriptionTag.Id);
                            game.TagIds = tagIds;
                        }
                    }
                }
            }, new GlobalProgressOptions("Settings Viveport subscription tag", true) { IsIndeterminate = false });
        }
    }
}