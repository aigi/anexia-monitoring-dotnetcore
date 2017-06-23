﻿using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.PlatformAbstractions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VersionMonitorNetCore.Models;


namespace VersionMonitorNetCore.Services
{
    /// <summary>
    /// Provides monitoring methods
    /// </summary>
    internal class MonitoringService : IDisposable
    {
        private const string EMPTY_VERSION_PLACEHOLDER = "<unknown>";
        private const string NUGET_URL_PREFIX = "https://api-v2v3search-0.nuget.org/query?q=packageid:";
        private HttpClient _client = new HttpClient();

        /// <summary>
        /// Checks if the services are running (database and optional custom services)
        /// </summary>
        /// <returns></returns>
        internal string GetServiceStates()
        {
            StringBuilder builder = new StringBuilder();

            if (VersionMonitor.CheckDatabaseFunction == null)
                builder.AppendLine("Database check is not configured!");
            else
                builder.AppendLine(String.Format("Database connection: {0}", VersionMonitor.CheckDatabaseFunction() ? "OK" : "NOK"));

            if (VersionMonitor.CheckCustomServicesFunction != null)
            {
                foreach (var result in VersionMonitor.CheckCustomServicesFunction())
                    builder.AppendLine(String.Format("{0}: {1}", result.ServiceName, result.IsRunning ? "OK" : "NOK"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Gets the runtime and modules info
        /// </summary>
        /// <returns></returns>
        internal async Task<dynamic> GetModulesInfo()
        {
            return new
            {
                runtime = GetRuntime(),
                modules = await GetModules()
            };
        }

        #region Runtime helper

        /// <summary>
        /// get runtime info
        /// </summary>
        /// <returns></returns>
        private RuntimeInfo GetRuntime()
        {
            var framework = PlatformServices.Default.Application.RuntimeFramework;

            return new RuntimeInfo
            {
                // fix value, this application is only used for .NET Core
                Platform = "dotnetcore",
                PlatformVersion = framework.Version.ToString(),
                Framework = framework.Identifier,
                FrameworkInstalledVersion = framework.Version.ToString(),
                FrameworkNewestVersion = null //todo: newest framework version
            };
        }

        /// <summary>
        /// get info about modules
        /// </summary>
        /// <returns></returns>
        private async Task<List<ModuleInfo>> GetModules()
        {
            List<ModuleInfo> modules = new List<ModuleInfo>();
            var entryAssembly = Assembly.GetEntryAssembly();
            string entryAssemblyName = entryAssembly.GetName().Name.ToLower();
            var libraries = DependencyContext.Load(entryAssembly).RuntimeLibraries.OrderBy(x => x.Name);

            foreach (var library in libraries)
            {
                // no need to display executing assembly
                if (library.Name == entryAssemblyName)
                    continue;

                modules.Add(new ModuleInfo()
                {
                    Name = library.Name,
                    InstalledVersion = library.Version,
                    NewestVersion = await GetNewestModuleVersion(library.Name)
                });
            }

            return modules;
        }

        private async Task<string> GetNewestModuleVersion(string library)
        {
            var response = await _client.GetAsync(NUGET_URL_PREFIX + library);
            // status code verification
            response.EnsureSuccessStatusCode();
            // read response content
            string stringResponse = await response.Content.ReadAsStringAsync();

            var nugetJson = JsonConvert.DeserializeObject<NugetJson>(stringResponse);
            if (nugetJson != null && nugetJson.data.Count > 0)
            {
                var nugetVersions = nugetJson.data.First().Versions;
                Version maxVersion = null;
                foreach (var nugetVersion in nugetVersions)
                {
                    Version currentVersion;
                    if (Version.TryParse(nugetVersion.version, out currentVersion))
                        maxVersion = (maxVersion == null || currentVersion > maxVersion) ? currentVersion : maxVersion;
                }
                if (maxVersion != null)
                    return maxVersion.ToString();
            }

            return EMPTY_VERSION_PLACEHOLDER;
        }

        #endregion

        #region Token validation

        /// <summary>
        /// Check if access token has been provided in Initialize-method
        /// </summary>
        /// <returns></returns>
        public bool CheckTokenConfiguration()
        {
            return !String.IsNullOrWhiteSpace(VersionMonitor.AccessToken);
        }

        /// <summary>
        /// Check if the access token from the query matches the one from the configuration
        /// </summary>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public bool ValidateToken(string accessToken)
        {
            return VersionMonitor.AccessToken == accessToken;
        }

        #endregion

        public void Dispose()
        {
            _client.Dispose();
            _client = null;
        }
    }
}
