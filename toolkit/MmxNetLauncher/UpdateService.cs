using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MmxNetLauncher;

public static class UpdateService
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MmxNetLauncher/0.1");
        return c;
    }

    public sealed class CheckResult
    {
        public bool Ok { get; init; }
        public bool UpdateAvailable { get; init; }
        public string Message { get; init; } = "";
        public PackVersion Local { get; init; } = PackVersion.Default;
        public UpdateManifest? Remote { get; init; }
    }

    public static string ResolveFeedToken(Install? inst)
    {
        var fromCfg = inst?.GetConfigString("updateFeedToken", "")?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(fromCfg)) return fromCfg;
        var env =
            (Environment.GetEnvironmentVariable("MMX_NET_UPDATE_FEED_TOKEN") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return (Environment.GetEnvironmentVariable("AC_UPDATE_FEED_TOKEN") ?? "").Trim();
    }

    public static async Task<CheckResult> CheckAsync(Install inst, string? feedUrl = null, CancellationToken ct = default)
    {
        var local = inst.ReadVersion();
        var url = (feedUrl ?? inst.GetConfigString("updateFeedUrl", "")).Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return new CheckResult
            {
                Ok = false,
                Message = "Nessun URL feed configurato (ac_config.json → updateFeedUrl). " +
                          "Logan: imposta FeedBaseUrl in ac_dev_publish.json, pubblica e carica dist\\update\\ online.",
                Local = local,
            };
        }

        try
        {
            var token = ResolveFeedToken(inst);
            var remote = await FetchManifestAsync(url, token, ct).ConfigureAwait(false);
            var channel = inst.GetConfigString("updateChannel", local.Channel);
            if (!string.IsNullOrWhiteSpace(channel) &&
                !string.IsNullOrWhiteSpace(remote.Channel) &&
                !remote.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase) &&
                !channel.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                return new CheckResult
                {
                    Ok = true,
                    UpdateAvailable = false,
                    Message = $"Feed channel '{remote.Channel}' ≠ locale '{channel}'.",
                    Local = local,
                    Remote = remote,
                };
            }

            var newer = CompareVersions(remote.Version, local.Version) > 0;
            return new CheckResult
            {
                Ok = true,
                UpdateAvailable = newer,
                Message = newer
                    ? $"Nuova versione {remote.Version} (locale {local.Version})."
                    : $"Sei aggiornato ({local.Version}).",
                Local = local,
                Remote = remote,
            };
        }
        catch (Exception ex)
        {
            var hint = "";
            var msg = ex.Message ?? "";
            if (msg.Contains("401", StringComparison.Ordinal) ||
                msg.Contains("403", StringComparison.Ordinal) ||
                msg.Contains("404", StringComparison.Ordinal))
            {
                hint = " Se il feed è su repo GitHub privata: invita l'amico come collaboratore " +
                       "e metti un PAT read-only in ac_config.json → updateFeedToken " +
                       "(oppure env MMX_NET_UPDATE_FEED_TOKEN).";
            }
            return new CheckResult
            {
                Ok = false,
                Message = "Check aggiornamenti fallito: " + msg + hint,
                Local = local,
            };
        }
    }

    public static Task<UpdateManifest> FetchManifestAsync(string feedUrl, CancellationToken ct = default) =>
        FetchManifestAsync(feedUrl, token: null, ct);

    public static async Task<UpdateManifest> FetchManifestAsync(
        string feedUrl,
        string? token,
        CancellationToken ct = default)
    {
        feedUrl = feedUrl.Trim();

        if (TryGetLocalFeedDir(feedUrl, out var localDir))
            return await Task.Run(() => ReadLocalManifest(localDir), ct).ConfigureAwait(false);

        string json;
        string resolvedUrl = feedUrl;

        if (LooksLikeJsonFile(feedUrl))
        {
            json = await HttpGetStringAsync(feedUrl, token, ct).ConfigureAwait(false);
        }
        else
        {
            var baseUrl = feedUrl.TrimEnd('/') + "/";
            try
            {
                resolvedUrl = baseUrl + PackService.ManifestName;
                json = await HttpGetStringAsync(resolvedUrl, token, ct).ConfigureAwait(false);
            }
            catch
            {
                resolvedUrl = baseUrl + "ac_version.json";
                json = await HttpGetStringAsync(resolvedUrl, token, ct).ConfigureAwait(false);
                var ver = JsonSerializer.Deserialize<PackVersion>(json) ?? PackVersion.Default;
                return new UpdateManifest
                {
                    Name = ver.Name,
                    Version = ver.Version,
                    Channel = ver.Channel,
                    Protocol = ver.Protocol,
                    Engine = ver.Engine,
                    Notes = ver.Notes,
                    PackageUrl = baseUrl + PackService.ZipName,
                };
            }
        }

        var m = JsonSerializer.Deserialize<UpdateManifest>(json)
                ?? throw new InvalidOperationException("Manifest vuoto.");

        var baseForPackage = BaseDirectoryUrl(LooksLikeJsonFile(feedUrl) ? feedUrl : resolvedUrl);
        if (string.IsNullOrWhiteSpace(m.PackageUrl))
            m.PackageUrl = baseForPackage + PackService.ZipName;
        else if (!IsAbsoluteHttpUrl(m.PackageUrl) && !IsLocalPath(m.PackageUrl))
            m.PackageUrl = baseForPackage + m.PackageUrl.TrimStart('/');

        return m;
    }

    private static async Task<string> HttpGetStringAsync(string url, string? token, CancellationToken ct)
    {
        using var req = CreateFeedRequest(HttpMethod.Get, url, token);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var shortBody = body.Length > 180 ? body[..180] + "…" : body;
            throw new HttpRequestException(
                $"HTTP {(int)resp.StatusCode} su {url}" +
                (string.IsNullOrWhiteSpace(shortBody) ? "" : ": " + shortBody.Trim()));
        }
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> HttpGetBytesAsync(string url, string? token, CancellationToken ct)
    {
        using var req = CreateFeedRequest(HttpMethod.Get, url, token);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} download {url}");
        }
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    internal static HttpRequestMessage CreateFeedRequest(HttpMethod method, string url, string? token)
    {
        var effectiveUrl = url;
        var effectiveToken = (token ?? "").Trim();

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            var userInfo = uri.UserInfo;
            var colon = userInfo.IndexOf(':');
            var embedded = colon >= 0 ? userInfo[(colon + 1)..] : userInfo;
            if (!string.IsNullOrWhiteSpace(embedded) && string.IsNullOrWhiteSpace(effectiveToken))
                effectiveToken = embedded.Trim();
            var b = new UriBuilder(uri) { UserName = "", Password = "" };
            effectiveUrl = b.Uri.ToString();
        }

        var req = new HttpRequestMessage(method, effectiveUrl);
        if (!string.IsNullOrWhiteSpace(effectiveToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effectiveToken);
            req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        }
        return req;
    }

    private static UpdateManifest ReadLocalManifest(string dir)
    {
        var manifestPath = Path.Combine(dir, PackService.ManifestName);
        var versionPath = Path.Combine(dir, "ac_version.json");
        var zipPath = Path.Combine(dir, PackService.ZipName);

        if (File.Exists(manifestPath))
        {
            var m = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(manifestPath))
                    ?? throw new InvalidOperationException("Manifest locale vuoto.");
            if (string.IsNullOrWhiteSpace(m.PackageUrl) || !IsAbsoluteHttpUrl(m.PackageUrl))
            {
                if (!File.Exists(zipPath))
                    throw new FileNotFoundException("Manca ac-update.zip nel feed locale.", zipPath);
                m.PackageUrl = zipPath;
            }
            else if (!IsAbsoluteHttpUrl(m.PackageUrl) && IsLocalPath(m.PackageUrl) && !Path.IsPathRooted(m.PackageUrl))
                m.PackageUrl = Path.GetFullPath(Path.Combine(dir, m.PackageUrl));
            return m;
        }

        if (!File.Exists(versionPath))
            throw new FileNotFoundException(
                "Feed locale senza ac_update_manifest.json né ac_version.json: " + dir);

        var ver = JsonSerializer.Deserialize<PackVersion>(File.ReadAllText(versionPath))
                  ?? PackVersion.Default;
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Manca ac-update.zip nel feed locale.", zipPath);

        return new UpdateManifest
        {
            Name = ver.Name,
            Version = ver.Version,
            Channel = ver.Channel,
            Protocol = ver.Protocol,
            Engine = ver.Engine,
            Notes = ver.Notes,
            PackageUrl = zipPath,
        };
    }

    private static bool TryGetLocalFeedDir(string feedUrl, out string dir)
    {
        dir = "";
        var raw = feedUrl.Trim().Trim('"');
        if (raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(raw);
                raw = uri.LocalPath;
                if (OperatingSystem.IsWindows() && raw.StartsWith('/') && raw.Length > 2 && raw[2] == ':')
                    raw = raw[1..];
            }
            catch { return false; }
        }

        if (!IsLocalPath(raw)) return false;

        try
        {
            var full = Path.GetFullPath(raw);
            if (File.Exists(full) && LooksLikeJsonFile(full))
            {
                dir = Path.GetDirectoryName(full) ?? "";
                return !string.IsNullOrWhiteSpace(dir);
            }
            if (Directory.Exists(full))
            {
                dir = full;
                return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }

    private static bool IsLocalPath(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (s.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) return true;
        if (Path.IsPathRooted(s)) return true;
        return s.Contains('\\') || (s.Contains('/') && !s.Contains("://"));
    }

    private static string BaseDirectoryUrl(string url)
    {
        url = url.Trim();
        if (LooksLikeJsonFile(url) || url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var slash = url.LastIndexOf('/');
            return slash > 0 ? url[..(slash + 1)] : url;
        }
        return url.TrimEnd('/') + "/";
    }

    private static bool IsAbsoluteHttpUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static async Task<PackVersion> DownloadAndApplyAsync(
        Install inst,
        UpdateManifest remote,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(remote.PackageUrl))
            throw new InvalidOperationException("Manifest senza PackageUrl.");

        progress?.Report("Download pacchetto…");
        var token = ResolveFeedToken(inst);
        byte[] bytes;
        if (IsAbsoluteHttpUrl(remote.PackageUrl))
        {
            bytes = await HttpGetBytesAsync(remote.PackageUrl, token, ct).ConfigureAwait(false);
        }
        else if (File.Exists(remote.PackageUrl))
        {
            bytes = await File.ReadAllBytesAsync(remote.PackageUrl, ct).ConfigureAwait(false);
        }
        else
            throw new FileNotFoundException("Pacchetto aggiornamento non trovato.", remote.PackageUrl);

        if (!string.IsNullOrWhiteSpace(remote.PackageSha256))
        {
            progress?.Report("Verifica SHA-256…");
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var expect = remote.PackageSha256.Trim().ToLowerInvariant();
            if (!hash.Equals(expect, StringComparison.Ordinal))
                throw new InvalidOperationException("SHA-256 del pacchetto non corrisponde.");
        }

        var tmp = Path.Combine(Path.GetTempPath(), "ac-update-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
        try
        {
            progress?.Report("Applicazione overlay…");
            return PackService.ApplyUpdate(inst, tmp);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public static int CompareVersions(string a, string b)
    {
        static int[] Parts(string s)
        {
            var clean = (s ?? "0").Split(new[] { '-', '+' }, 2)[0];
            return clean.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        }

        var pa = Parts(a);
        var pb = Parts(b);
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < pa.Length ? pa[i] : 0;
            var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static bool LooksLikeJsonFile(string url) =>
        url.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
}
