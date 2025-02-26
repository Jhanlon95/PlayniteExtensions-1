﻿using GiantBombMetadata.Api;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using PlayniteExtensions.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GiantBombMetadata
{
    public class GiantBombMetadataProvider : OnDemandMetadataProvider
    {
        private readonly MetadataRequestOptions options;
        private readonly GiantBombMetadata plugin;
        private readonly GiantBombApiClient apiClient;
        private GiantBombObjectDetails foundGame = null;
        private IPlatformUtility platformUtility;
        private ILogger logger = LogManager.GetLogger();

        public override List<MetadataField> AvailableFields { get => plugin.SupportedFields; }

        public GiantBombMetadataProvider(MetadataRequestOptions options, GiantBombMetadata plugin, GiantBombApiClient apiClient, IPlatformUtility platformUtility)
        {
            this.options = options;
            this.plugin = plugin;
            this.apiClient = apiClient;
            this.platformUtility = platformUtility;
        }

        private GiantBombGameDetails GetGameDetails()
        {
            if (foundGame is GiantBombGameDetails details)
                return details;

            if (foundGame == null)
            {
                GetSearchResultGame();
            }

            if (foundGame is GiantBombSearchResultItem searchResult)
            {
                return (GiantBombGameDetails)(foundGame = apiClient.GetGameDetails(searchResult.Guid));
            }
            return (GiantBombGameDetails)(foundGame = new GiantBombGameDetails());
        }

        private GiantBombObjectDetails GetSearchResultGame()
        {
            if (foundGame != null)
                return foundGame;

            if (options.IsBackgroundDownload)
            {
                string guid = GiantBombHelper.GetGiantBombGuidFromGameLinks(options.GameData);
                if (guid != null)
                    return (GiantBombGameDetails)(foundGame = apiClient.GetGameDetails(guid));

                if (string.IsNullOrWhiteSpace(options.GameData.Name))
                    return foundGame = new GiantBombGameDetails();

                var searchResult = apiClient.SearchGames(options.GameData.Name);

                if (searchResult == null)
                    return foundGame = new GiantBombGameDetails();

                var snc = new SortableNameConverter(new string[0], batchOperation: false, numberLength: 1, removeEditions: true);

                var nameToMatch = snc.Convert(options.GameData.Name).Deflate();

                var matchedGames = searchResult.Where(g => HasMatchingName(g, nameToMatch, snc) && HasPlatformOverlap(g)).ToList();

                switch (matchedGames.Count)
                {
                    case 0:
                        return foundGame = new GiantBombGameDetails();
                    case 1:
                        return foundGame = matchedGames.First();
                    default:
                        var searchReleaseDate = options.GameData.ReleaseDate?.Date;
                        var sortedByReleaseDateProximity = matchedGames.OrderBy(g => GetDaysApart(searchReleaseDate, g.ReleaseDate)).ToList();
                        return foundGame = sortedByReleaseDateProximity.First();
                }
            }
            else
            {
                var selectedGame = plugin.PlayniteApi.Dialogs.ChooseItemWithSearch(null, (a) =>
                {
                    var searchOutput = new List<GenericItemOption>();

                    if (string.IsNullOrWhiteSpace(a))
                        return searchOutput;

                    if (Regex.IsMatch(a, @"^3030-[0-9]+$"))
                    {
                        try
                        {
                            var gameById = apiClient.GetGameDetails(a);
                            searchOutput.Add(new GenericSearchResultGame(gameById));
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, $"Failed to get Giant Bomb data by ID for <{a}>");
                        }
                    }

                    try
                    {
                        var searchResult = apiClient.SearchGames(a);
                        searchOutput.AddRange(searchResult.Select(r => new GenericSearchResultGame(r)));

                    }
                    catch (Exception e)
                    {
                        logger.Error(e, $"Failed to get Giant Bomb search data for <{a}>");
                    }

                    return searchOutput;
                }, options.GameData.Name, string.Empty);

                return foundGame = ((GenericSearchResultGame)selectedGame)?.Game ?? new GiantBombGameDetails();
            }
        }

        private class GenericSearchResultGame : GenericItemOption
        {
            public GenericSearchResultGame(GiantBombObjectDetails g) : base(g.Name, g.ReleaseDate)
            {
                Game = g;
                if (g.AliasesSplit.Any())
                    Name += $" (AKA {string.Join("/", g.AliasesSplit)})";

                if (g.Platforms?.Length > 0)
                {
                    if (Description?.Length > 0)
                        Description += " | ";
                    else
                        Description = "";

                    Description += string.Join(", ", g.Platforms.Select(p => p.Abbreviation));
                }
            }

            public GiantBombObjectDetails Game { get; set; }
        }

        private int GetDaysApart(DateTime? searchDate, string resultDate)
        {
            if (searchDate == null)
                return 0;

            var resultReleaseDate = resultDate.ParseReleaseDate(logger);

            if (resultReleaseDate == null)
                return 365 * 2; //allow anything within a year to take precedence over this

            var daysApart = (searchDate.Value.Date - resultReleaseDate.Value.Date).TotalDays;

            return Math.Abs((int)daysApart);
        }

        private static bool HasMatchingName(GiantBombObjectDetails g, string deflatedSearchName, SortableNameConverter snc)
        {
            var gameNames = new List<string> { g.Name.Deflate() };
            if (g.AliasesSplit?.Any() == true)
                gameNames.AddRange(g.AliasesSplit.Select(a => a.Deflate()));

            foreach (var gameName in gameNames)
            {
                var deflatedGameName = snc.Convert(gameName).Deflate();
                if (deflatedSearchName.Equals(deflatedGameName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasPlatformOverlap(GiantBombObjectDetails g)
        {
            var searchGamePlatforms = options.GameData.Platforms;
            if (searchGamePlatforms == null || searchGamePlatforms.Count == 0 || g.Platforms == null || g.Platforms.Length == 0)
                return true; //no search platforms acts as a wildcard - everything could be a match

            var gbPlatforms = g.Platforms.SelectMany(p => platformUtility.GetPlatforms(p.Name)).ToList();
            foreach (var gbp in gbPlatforms)
            {
                if (gbp is MetadataSpecProperty specProperty)
                {
                    if (searchGamePlatforms.Any(p => specProperty.Id == p.SpecificationId))
                        return true;
                }
                else if (gbp is MetadataNameProperty nameProperty)
                {
                    if (searchGamePlatforms.Any(p => nameProperty.Name == p.Name))
                        return true;
                }
            }
            return false;
        }

        private List<MetadataNameProperty> GetValues(PropertyImportTarget target)
        {
            var data = GetGameDetails();
            var output = new List<MetadataNameProperty>();
            output.AddRange(GetValues(plugin.Settings.Settings.Characters, target, data.Characters));
            output.AddRange(GetValues(plugin.Settings.Settings.Concepts, target, data.Concepts));
            output.AddRange(GetValues(plugin.Settings.Settings.Locations, target, data.Locations));
            output.AddRange(GetValues(plugin.Settings.Settings.Objects, target, data.Objects));
            output.AddRange(GetValues(plugin.Settings.Settings.People, target, data.People));
            output.AddRange(GetValues(plugin.Settings.Settings.Themes, target, data.Themes));
            output = output.OrderBy(x => x.Name).ToList();
            return output;
        }

        private IEnumerable<MetadataNameProperty> GetValues(GiantBombPropertyImportSetting importSetting, PropertyImportTarget target, GiantBombObject[] data)
        {
            if (importSetting.ImportTarget != target || data == null || data.Length == 0)
                return new MetadataNameProperty[0];

            return data.Select(d => new MetadataNameProperty($"{importSetting.Prefix}{d.Name}"));
        }

        public override string GetDescription(GetMetadataFieldArgs args)
        {
            string description = GetSearchResultGame().Description;
            if (string.IsNullOrWhiteSpace(description))
                return null;

            description = GiantBombHelper.MakeHtmlUrlsAbsolute(description, "https://www.giantbomb.com/");
            return description;
        }

        public override IEnumerable<MetadataProperty> GetTags(GetMetadataFieldArgs args)
        {
            return GetValues(PropertyImportTarget.Tags);
        }

        public override IEnumerable<MetadataProperty> GetGenres(GetMetadataFieldArgs args)
        {
            return GetValues(PropertyImportTarget.Genres);
        }

        public override IEnumerable<MetadataProperty> GetPlatforms(GetMetadataFieldArgs args)
        {
            return GetSearchResultGame().Platforms?.SelectMany(p => platformUtility.GetPlatforms(p.Name));
        }

        public override ReleaseDate? GetReleaseDate(GetMetadataFieldArgs args)
        {
            return GetGameDetails().ReleaseDate?.ParseReleaseDate(logger);
        }

        public override string GetName(GetMetadataFieldArgs args)
        {
            return GetSearchResultGame().Name;
        }

        public override IEnumerable<MetadataProperty> GetDevelopers(GetMetadataFieldArgs args)
        {
            return GetGameDetails().Developers?.Select(d => new MetadataNameProperty(d.Name.TrimCompanyForms()));
        }

        public override IEnumerable<MetadataProperty> GetPublishers(GetMetadataFieldArgs args)
        {
            return GetGameDetails().Publishers?.Select(d => new MetadataNameProperty(d.Name.TrimCompanyForms()));
        }

        public override IEnumerable<MetadataProperty> GetSeries(GetMetadataFieldArgs args)
        {
            if (!plugin.Settings.Settings.GetSingleSeries || GetGameDetails() == null || GetGameDetails().Franchises.Length < 1)
            {
                return GetGameDetails().Franchises?.Select(f => new MetadataNameProperty(f.Name));
            }

            List<MetadataProperty> list = new List<MetadataProperty>();

            MetadataNameProperty first = new MetadataNameProperty(GetGameDetails().Franchises.First().Name);
            list.Add(first);

            return list;
        }

        public override IEnumerable<MetadataProperty> GetAgeRatings(GetMetadataFieldArgs args)
        {
            return GetGameDetails().Ratings?.Select(r => new MetadataNameProperty(r.Name));
        }

        public override IEnumerable<Link> GetLinks(GetMetadataFieldArgs args)
        {
            string url = GetSearchResultGame().SiteDetailUrl;
            if (string.IsNullOrWhiteSpace(url))
                yield break;

            yield return new Link("Giant Bomb", url);
        }

        public override MetadataFile GetCoverImage(GetMetadataFieldArgs args)
        {
            var coverUrl = GetSearchResultGame().Image?.Original;
            if (coverUrl == null)
                return null;

            return new MetadataFile(coverUrl);
        }

        private static Regex pressEventOrCoverRegex = new Regex(@"\b(e3|pax|box art)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool ImageCanBeUsedAsBackground(GiantBombImage img)
        {
            return img.Tags.Contains("screenshot", StringComparison.InvariantCultureIgnoreCase)
                || img.Tags.Contains("concept", StringComparison.InvariantCultureIgnoreCase)
                || !pressEventOrCoverRegex.IsMatch(img.Tags);
        }

        public override MetadataFile GetBackgroundImage(GetMetadataFieldArgs args)
        {
            var images = GetGameDetails().Images?.Where(ImageCanBeUsedAsBackground).ToList();
            if (images == null || images.Count == 0)
                return null;

            if (options.IsBackgroundDownload)
            {
                return new MetadataFile(images.First().Original);
            }
            else
            {
                var imageOptions = images?.Select(i => new GiantBombImageFileOption(i)).ToList<ImageFileOption>();
                var selected = plugin.PlayniteApi.Dialogs.ChooseImageFile(imageOptions, "Select Giant Bomb background");
                var originalUrl = (selected as GiantBombImageFileOption)?.Image.Original;

                if (originalUrl == null)
                    return null;

                return new MetadataFile(originalUrl);
            }
        }

        private class GiantBombImageFileOption : ImageFileOption
        {
            public GiantBombImageFileOption(GiantBombImage image)
            {
                Image = image;
                Path = image.ThumbUrl;
            }

            public GiantBombImage Image { get; }
        }
    }
}