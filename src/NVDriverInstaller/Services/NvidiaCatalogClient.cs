using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using NVDriverInstaller.Core.Models;

namespace NVDriverInstaller.Services;

public static class NvidiaCatalogClient
{
    private const string IndexUrl = "https://gpu.me/v1/index2.json";
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public static bool LastFetchFailed { get; private set; }
    public static string? LastFetchError { get; private set; }

    static NvidiaCatalogClient()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NVDriverInstaller/1.0");
    }

    public static async Task<bool> CheckConnectivityAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, IndexUrl);
            using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.IsSuccessStatusCode)
            {
                LastFetchFailed = false;
                LastFetchError = null;
                return true;
            }
        }
        catch (Exception ex)
        {
            LastFetchFailed = true;
            LastFetchError = ex.Message;
            return false;
        }

        LastFetchFailed = true;
        LastFetchError = "Server returned non-success status.";
        return false;
    }

    public static async Task<List<DriverPackage>> FetchAvailablePackagesAsync(string driverType = "desktop", bool dchOnly = true, CancellationToken ct = default)
    {
        var packages = new List<DriverPackage>();

        try
        {
            var response = await HttpClient.GetStringAsync(IndexUrl, ct);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("versions", out var versionsElem) && versionsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in versionsElem.EnumerateArray())
                {
                    var type = item.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";
                    var isDch = item.TryGetProperty("dch", out var dProp) && dProp.GetInt32() == 1;

                    if (!type.Equals(driverType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (dchOnly && !isDch)
                        continue;

                    var version = item.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "" : "";
                    var key = item.TryGetProperty("key", out var kProp) ? kProp.GetString() ?? "" : "";
                    var url = item.TryGetProperty("url", out var uProp) ? uProp.GetString() : null;
                    var date = item.TryGetProperty("date", out var dtProp) ? dtProp.GetString() ?? "" : "";

                    var isStudio = key.Contains("-nsd-", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(version))
                    {
                        var platform = driverType.Equals("notebook", StringComparison.OrdinalIgnoreCase) ? "notebook" : "desktop";
                        var subType = isStudio ? "nsd-dch" : "dch";
                        url = $"https://us.download.nvidia.com/Windows/{version}/{version}-{platform}-win10-win11-64bit-international-{subType}-whql.exe";
                    }

                    packages.Add(new DriverPackage
                    {
                        Version = version,
                        DriverType = driverType.ToLowerInvariant(),
                        Dch = isDch,
                        IsStudio = isStudio,
                        DownloadUrl = url,
                        ReleaseDate = date
                    });
                }
            }

            LastFetchFailed = false;
            LastFetchError = null;
        }
        catch (Exception ex)
        {
            LastFetchFailed = true;
            LastFetchError = ex.Message;

            // Catalog fetch failure, fallback to current known baseline
            packages.Add(new DriverPackage
            {
                Version = "566.14",
                DriverType = driverType,
                Dch = true,
                IsStudio = false,
                DownloadUrl = $"https://us.download.nvidia.com/Windows/566.14/566.14-{driverType}-win10-win11-64bit-international-dch-whql.exe",
                ReleaseDate = "Latest"
            });
        }

        // Sort descending by version
        return packages.OrderByDescending(p => ParseVersion(p.Version)).ToList();
    }

    public static Version ParseVersion(string versionStr)
    {
        var clean = Regex.Replace(versionStr, @"[^\d.]", "").Trim();
        if (Version.TryParse(clean, out var v))
            return v;

        var parts = clean.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out var major))
            return new Version(major, 0);

        return new Version(0, 0);
    }
}
