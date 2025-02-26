﻿using Newtonsoft.Json;
using Playnite.SDK;
using PlayniteExtensions.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace IgnMetadata
{
    public class IgnClient
    {
        private readonly IWebDownloader downloader;
        private ILogger logger = LogManager.GetLogger();

        public IgnClient(IWebDownloader downloader)
        {
            this.downloader = downloader;
        }

        public ICollection<IgnGame> Search(string searchString)
        {
            if (string.IsNullOrWhiteSpace(searchString))
                return new List<IgnGame>();

            var variables = new { term = searchString, count = 20, objectType = "Game" };
            var data = Call<IgnSearchResultData>("SearchObjectsByName", variables, "60d952351fb009854f3049a800a7bb36f244b098effb3aa14f8e0df3819fbfd1");

            return data?.SearchObjectsByName?.Objects;
        }

        public IgnGame Get(string slug, string region)
        {
            var variables = new { slug = slug, objectType = "Game", region = region, state = "Published" };
            var data = Call<IgnGetGameResultData>("ObjectSelectByTypeAndSlug", variables, "a3400aea0c03af4f7105ed7d30e931f048563f4031dd41abdf98449d271232d6");

            return data?.ObjectSelectByTypeAndSlug;
        }

        private T Call<T>(string operationName, object variables, string hash)
        {
            var extensions = new { persistedQuery = new { version = 1, sha256Hash = hash } };
            var variablesParameter = ToQueryStringParameter(variables);
            var extensionsParameter = ToQueryStringParameter(extensions);
            string url = $"https://mollusk.apis.ign.com/graphql?operationName={operationName}&variables={variablesParameter}&extensions={extensionsParameter}";

            var headers = new Dictionary<string, string>
            {
                { "apollographql-client-name", "kraken" },
                { "apollographql-client-version", "v0.13.13" }
            };

            var response = downloader.DownloadString(url, referer: "https://www.ign.com/upcoming/games", customHeaders: headers);
            if (string.IsNullOrWhiteSpace(response?.ResponseContent))
            {
                logger.Error($"Failed to get content from {url}");
                return default;
            }

            var root = JsonConvert.DeserializeObject<IgnResponseRoot<T>>(response.ResponseContent);
            if (root != null && root.Errors.Any())
            {
                foreach (var error in root.Errors)
                {
                    logger.Error(error.Message);
                }
                return default;
            }

            return root.Data;
        }

        private static string ToQueryStringParameter(object obj)
        {
            return Uri.EscapeDataString(JsonConvert.SerializeObject(obj, Formatting.None));
        }
    }

    public class IgnResponseRoot<T>
    {
        public T Data;
        public IgnError[] Errors = new IgnError[0];
    }

    public class IgnError
    {
        public string Message;
    }

    public class IgnSearchResultData
    {
        public IgnSearchResultObjects SearchObjectsByName;
    }

    public class IgnGetGameResultData
    {
        public IgnGame ObjectSelectByTypeAndSlug;
    }

    public class IgnSearchResultObjects
    {
        public IgnGame[] Objects = new IgnGame[0];
        public IgnPageInfo PageInfo;
    }

    public class IgnPageInfo
    {
        public bool HasNext;
        public int? NextCursor;
        public int Total;
    }

    public class IgnGame
    {
        public string Id;
        public string Slug;
        public string Url;
        public IgnGameMetadata Metadata;
        public IgnUrlHolder PrimaryImage;
        public IgnAttribute[] Features = new IgnAttribute[0];
        public IgnAttribute[] Franchises = new IgnAttribute[0];
        public IgnAttribute[] Genres = new IgnAttribute[0];
        public IgnAttribute[] Producers = new IgnAttribute[0];
        public IgnAttribute[] Publishers = new IgnAttribute[0];
        public IgnObjectRegion[] ObjectRegions = new IgnObjectRegion[0];
    }

    public class IgnObjectRegion
    {
        /// <summary>
        /// The game's name for this particular release - often empty
        /// </summary>
        public string Name;
        public string Region;
        public IgnRelease[] Releases = new IgnRelease[0];

        /// <summary>
        /// Not in search results
        /// </summary>
        public IgnAgeRating AgeRating;

        /// <summary>
        /// Not in search results
        /// </summary>
        public IgnAttribute[] AgeRatingDescriptors = new IgnAttribute[0];
    }

    public class IgnAgeRating
    {
        public string Name;
        public string AgeRatingType;
    }

    public class IgnRelease
    {
        public string Date;
        public bool EstimatedDate;
        public IgnAttribute[] PlatformAttributes = new IgnAttribute[0];
    }

    public class IgnGameMetadata
    {
        public IgnNameData Names;

        /// <summary>
        /// Not in search results
        /// </summary>
        public IgnDescriptions Descriptions;

        /// <summary>
        /// Not in search results
        /// </summary>
        public string State;
    }

    public class IgnDescriptions
    {
        public string Long;
        public string Short;
    }

    public class IgnNameData
    {
        public string Name;
        public string Short;
        public string[] Alt = new string[0];
    }

    public class IgnUrlHolder
    {
        public string Url;
    }

    public class IgnAttribute
    {
        public string Name;
        public string Slug;
    }
}
