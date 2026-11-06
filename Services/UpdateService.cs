using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlueDock.Services
{
    /// <summary>A release on GitHub that is newer than the running build.</summary>
    public sealed record UpdateInfo(Version Version, string AssetName, string DownloadUrl, long Size, string PageUrl);

    /// <summary>
    /// Checks the project's GitHub releases for a newer version and, on request, downloads and
    /// starts its installer. Installing over a running copy is fine: the installer closes the
    /// old one through the Restart Manager.
    ///
    /// Everything here is best effort. No network, a rate limit, a release without an installer:
    /// all of it just means "no update today", never an error in the user's face.
    /// </summary>
    public static class UpdateService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/laurensguijt/BlueDock/releases/latest";

        /// <summary>Where a download is allowed to come from. GitHub redirects assets to its CDN.</summary>
        private static readonly string[] AllowedHosts =
        {
            "api.github.com",
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
        };

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            // GitHub rejects requests without a user agent.
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlueDock", CurrentVersion.ToString(3)));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        /// <summary>The newer release, or null when there is none or the check could not run.</summary>
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
        {
            try
            {
                using var response = await Http.GetAsync(LatestReleaseApi, cancel);
                if (!response.IsSuccessStatusCode) return null;

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
                var root = document.RootElement;

                if (!root.TryGetProperty("tag_name", out var tag)) return null;
                var version = ParseVersion(tag.GetString());
                if (version == null || version <= CurrentVersion) return null;

                if (!root.TryGetProperty("assets", out var assets)) return null;
                foreach (var asset in assets.EnumerateArray())
                {
                    string? name = asset.GetProperty("name").GetString();
                    string? url = asset.GetProperty("browser_download_url").GetString();
                    if (name == null || url == null) continue;
                    if (!name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsAllowed(url)) continue;

                    long size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                    return new UpdateInfo(version, name, url, size, page);
                }
            }
            catch
            {
                // Offline, rate limited, malformed: no update today.
            }
            return null;
        }

        /// <summary>
        /// Downloads the installer and starts it. Returns false when that did not work out;
        /// the caller can then send the user to the release page instead.
        /// </summary>
        public static async Task<bool> DownloadAndRunAsync(UpdateInfo update, IProgress<int>? progress = null,
                                                           CancellationToken cancel = default)
        {
            if (!IsAllowed(update.DownloadUrl)) return false;

            string folder = Path.Combine(Path.GetTempPath(), "BlueDock-update");
            string file = Path.Combine(folder, update.AssetName);

            try
            {
                Directory.CreateDirectory(folder);

                using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
                {
                    if (!response.IsSuccessStatusCode) return false;
                    // Redirects land on GitHub's CDN; make sure that is where we ended up.
                    var final = response.RequestMessage?.RequestUri?.ToString() ?? update.DownloadUrl;
                    if (!IsAllowed(final)) return false;

                    long total = response.Content.Headers.ContentLength ?? update.Size;
                    using var source = await response.Content.ReadAsStreamAsync(cancel);
                    using var target = File.Create(file);

                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                        done += read;
                        if (total > 0) progress?.Report((int)(done * 100 / total));
                    }
                }

                // A truncated download would fail in a far more confusing way later on.
                if (update.Size > 0 && new FileInfo(file).Length != update.Size) return false;

                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAllowed(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

        /// <summary>"v0.1.4" and "0.1.4" both mean the same release.</summary>
        private static Version? ParseVersion(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            tag = tag.TrimStart('v', 'V');
            return Version.TryParse(tag, out var version) ? Normalize(version) : null;
        }

        /// <summary>A tag has three parts, an assembly version four; compare them on equal footing.</summary>
        private static Version Normalize(Version v) =>
            new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build, 0);
    }
}
