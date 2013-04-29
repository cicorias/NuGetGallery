﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NuGetGallery
{
    public class JsonStatisticsService : IStatisticsService
    {
        private enum Reports
        {
            RecentPopularity,           //  most frequently downloaded package registration in last 6 weeks
            RecentPopularityDetail,     //  most frequently downloaded package, specific to actual version
            RecentPopularityDetail_     //  breakout by version for a package (drill down from RecentPopularity) 
        };

        private IReportService _reportService;
        private List<StatisticsPackagesItemViewModel> _downloadPackagesSummary;
        private List<StatisticsPackagesItemViewModel> _downloadPackageVersionsSummary;
        private List<StatisticsPackagesItemViewModel> _downloadPackagesAll;
        private List<StatisticsPackagesItemViewModel> _downloadPackageVersionsAll;

        public JsonStatisticsService(IReportService reportService)
        {
            _reportService = reportService;
        }

        public IEnumerable<StatisticsPackagesItemViewModel> DownloadPackagesSummary 
        {
            get
            {
                if (_downloadPackagesSummary == null)
                {
                    _downloadPackagesSummary = new List<StatisticsPackagesItemViewModel>();
                }
                return _downloadPackagesSummary;
            }
        }

        public IEnumerable<StatisticsPackagesItemViewModel> DownloadPackageVersionsSummary
        {
            get
            {
                if (_downloadPackageVersionsSummary == null)
                {
                    _downloadPackageVersionsSummary = new List<StatisticsPackagesItemViewModel>();
                }
                return _downloadPackageVersionsSummary;
            }
        }

        public IEnumerable<StatisticsPackagesItemViewModel> DownloadPackagesAll
        {
            get
            {
                if (_downloadPackagesAll == null)
                {
                    _downloadPackagesAll = new List<StatisticsPackagesItemViewModel>();
                }
                return _downloadPackagesAll;
            }
        }

        public IEnumerable<StatisticsPackagesItemViewModel> DownloadPackageVersionsAll
        {
            get
            {
                if (_downloadPackageVersionsAll == null)
                {
                    _downloadPackageVersionsAll = new List<StatisticsPackagesItemViewModel>();
                }
                return _downloadPackageVersionsAll;
            }
        }

        public async Task<bool> LoadDownloadPackages()
        {
            try
            {
                string json = await _reportService.Load(Reports.RecentPopularity.ToString() + ".json");

                if (json == null)
                {
                    return false;
                }

                JArray array = JArray.Parse(json);

                ((List<StatisticsPackagesItemViewModel>)DownloadPackagesAll).Clear();

                foreach (JObject item in array)
                {
                    ((List<StatisticsPackagesItemViewModel>)DownloadPackagesAll).Add(new StatisticsPackagesItemViewModel
                    {
                        PackageId = item["PackageId"].ToString(),
                        Downloads = item["Downloads"].Value<int>()
                    });
                }

                int count = ((List<StatisticsPackagesItemViewModel>)DownloadPackagesAll).Count;

                ((List<StatisticsPackagesItemViewModel>)DownloadPackagesSummary).Clear();

                for (int i = 0; i < Math.Min(10, count); i++)
                {
                    ((List<StatisticsPackagesItemViewModel>)DownloadPackagesSummary).Add(((List<StatisticsPackagesItemViewModel>)DownloadPackagesAll)[i]);
                }

                return true;
            }
            catch (JsonReaderException e)
            {
                QuietLog.LogHandledException(e);
                return false;
            }
            catch (StorageException e)
            {
                QuietLog.LogHandledException(e);
                return false;
            }
            catch (ArgumentException e)
            {
                QuietLog.LogHandledException(e);
                return false;
            }
        }

        public async Task<bool> LoadDownloadPackageVersions()
        {
            try
            {
                string json = await _reportService.Load(Reports.RecentPopularityDetail.ToString() + ".json");

                if (json == null)
                {
                    return false;
                }

                JArray array = JArray.Parse(json);

                ((List<StatisticsPackagesItemViewModel>)DownloadPackageVersionsAll).Clear();

                foreach (JObject item in array)
                {
                    ((List<StatisticsPackagesItemViewModel>)DownloadPackageVersionsAll).Add(new StatisticsPackagesItemViewModel
                    {
                        PackageId = item["PackageId"].ToString(),
                        PackageVersion = item["PackageVersion"].ToString(),
                        Downloads = item["Downloads"].Value<int>(),
                        PackageTitle = GetOptionalProperty("PackageTitle", item),
                        PackageDescription = GetOptionalProperty("PackageDescription", item),
                        PackageIconUrl = GetOptionalProperty("PackageIconUrl", item)
                    });
                }

                int count = ((List<StatisticsPackagesItemViewModel>)DownloadPackageVersionsAll).Count;

                ((List<StatisticsPackagesItemViewModel>)DownloadPackageVersionsSummary).Clear();

                for (int i = 0; i < Math.Min(10, count); i++)
                {
                    ((List<StatisticsPackagesItemViewModel>)DownloadPackageVersionsSummary).Add(((List<StatisticsPackagesItemViewModel>)DownloadPackageVersionsAll)[i]);
                }

                return true;
            }
            catch (JsonReaderException e)
            {
                QuietLog.LogHandledException(e);
                return false;
            }
            catch (StorageException e)
            {
                QuietLog.LogHandledException(e);
                return false;
            }
            catch (ArgumentException e)
            {
                QuietLog.LogHandledException(e);
                return false;
            }
        }

        private static string GetOptionalProperty(string propertyName, JObject obj)
        {
            JToken token;
            if (obj.TryGetValue(propertyName, out token))
            {
                return token.ToString();
            }
            return null;
        }

        public async Task<StatisticsPackagesReport> GetPackageDownloadsByVersion(string packageId)
        {
            try
            {
                if (string.IsNullOrEmpty(packageId))
                {
                    return null;
                }

                string reportName = string.Format(CultureInfo.CurrentCulture, "{0}{1}.json", Reports.RecentPopularityDetail_, packageId);

                reportName = reportName.ToLowerInvariant();

                string json = await _reportService.Load(reportName);

                if (json == null)
                {
                    return null;
                }

                JObject content = JObject.Parse(json);

                StatisticsPackagesReport report = new StatisticsPackagesReport();

                report.Facts = CreateFacts(content);

                return report;
            }
            catch (JsonReaderException e)
            {
                QuietLog.LogHandledException(e);
                return null;
            }
            catch (StorageException e)
            {
                QuietLog.LogHandledException(e);
                return null;
            }
            catch (ArgumentException e)
            {
                QuietLog.LogHandledException(e);
                return null;
            }
        }

        public async Task<StatisticsPackagesReport> GetPackageVersionDownloadsByClient(string packageId, string packageVersion)
        {
            try
            {
                if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(packageVersion))
                {
                    return null;
                }

                string reportName = string.Format(CultureInfo.CurrentCulture, "{0}{1}.json", Reports.RecentPopularityDetail_, packageId);

                reportName = reportName.ToLowerInvariant();

                string json = await _reportService.Load(reportName);

                if (json == null)
                {
                    return null;
                }

                JObject content = JObject.Parse(json);

                StatisticsPackagesReport report = new StatisticsPackagesReport();

                IList<StatisticsFact> facts = new List<StatisticsFact>();

                foreach (StatisticsFact fact in CreateFacts(content))
                {
                    if (fact.Dimensions["Version"] == packageVersion)
                    {
                        facts.Add(fact);
                    }
                }

                report.Facts = facts;

                return report;
            }
            catch (JsonReaderException e)
            {
                QuietLog.LogHandledException(e);
                return null;
            }
            catch (StorageException e)
            {
                QuietLog.LogHandledException(e);
                return null;
            }
            catch (ArgumentException e)
            {
                QuietLog.LogHandledException(e);
                return null;
            }
        }

        private static IList<StatisticsFact> CreateFacts(JObject data)
        {
            IList<StatisticsFact> facts = new List<StatisticsFact>();

            foreach (JObject perVersion in data["Items"])
            {
                string version = (string)perVersion["Version"];

                foreach (JObject perClient in perVersion["Items"])
                {
                    string clientName = (string)perClient["ClientName"];
                    string clientVersion = (string)perClient["ClientVersion"];

                    string operation = "unknown";

                    JToken opt;
                    if (perClient.TryGetValue("Operation", out opt))
                    {
                        operation = (string)opt;
                    }

                    int downloads = (int)perClient["Downloads"];

                    facts.Add(new StatisticsFact(CreateDimensions(version, clientName, clientVersion, operation), downloads));
                }
            }

            return facts;
        }

        private static IDictionary<string, string> CreateDimensions(string version, string clientName, string clientVersion, string operation)
        {
            return new Dictionary<string, string> 
            { 
                { "Version", version },
                { "ClientName", clientName },
                { "ClientVersion", clientVersion },
                { "Operation", operation }
            };
        }
    }
}
