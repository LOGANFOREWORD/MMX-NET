using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MmxNetLauncher;

public sealed class DevPublishConfig
{
    public string FeedBaseUrl { get; set; } = "";

    public string PublishTarget { get; set; } = "";

    public string OutDir { get; set; } = @"dist\update";

    public bool AbsolutePackageUrl { get; set; } = true;

    public bool AutoBumpPatch { get; set; } = true;
}

public sealed class PublishResult
{
    public string OutDir { get; init; } = "";
    public string ZipPath { get; init; } = "";
    public string ManifestPath { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string SuggestedFeedUrl { get; init; } = "";
    public string PackageUrl { get; init; } = "";
    public PackVersion Version { get; init; } = PackVersion.Default;
    public string? SyncTarget { get; init; }
    public string? FeedCheckMessage { get; init; }
}

public static class PublishService
{
    public const string ConfigFileName = "ac_dev_publish.json";

    public static string ConfigPath(Install inst) => Path.Combine(inst.Root, ConfigFileName);

    public static DevPublishConfig Load(Install inst)
    {
        try
        {
            var path = ConfigPath(inst);
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<DevPublishConfig>(File.ReadAllText(path))
                       ?? new DevPublishConfig();
            }
        }
        catch { /* ignore */ }
        return new DevPublishConfig();
    }

    public static void Save(Install inst, DevPublishConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath(inst), json);
    }

    public static string ResolveOutDir(Install inst, DevPublishConfig cfg)
    {
        var rel = string.IsNullOrWhiteSpace(cfg.OutDir) ? @"dist\update" : cfg.OutDir.Trim();
        return Path.IsPathRooted(rel)
            ? Path.GetFullPath(rel)
            : Path.GetFullPath(Path.Combine(inst.Root, rel));
    }

    public static PublishResult Publish(
        Install inst,
        PackVersion version,
        DevPublishConfig cfg,
        IProgress<string>? progress = null)
    {
        version.Version = (version.Version ?? "").Trim();
        if (string.IsNullOrWhiteSpace(version.Version))
            throw new InvalidOperationException("Indica una versione (es. 0.1.1).");

        var outDir = ResolveOutDir(inst, cfg);
        Directory.CreateDirectory(outDir);

        progress?.Report("Aggiornamento ac_version.json…");
        inst.WriteVersion(version);

        var zipPath = Path.Combine(outDir, PackService.ZipName);
        progress?.Report("Generazione " + PackService.ZipName + "…");
        PackService.PackUpdate(inst, zipPath, version);

        progress?.Report("Calcolo SHA-256…");
        var sha = Sha256File(zipPath);

        var feedNorm = NormalizeFeedUrl(cfg.FeedBaseUrl);
        var packageUrl = "ac-update.zip";
        if (cfg.AbsolutePackageUrl &&
            (feedNorm.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             feedNorm.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            packageUrl = feedNorm + PackService.ZipName;

        var manifest = PackService.BuildManifest(version, packageUrl, sha);
        var manifestPath = Path.Combine(outDir, PackService.ManifestName);
        progress?.Report("Scrittura manifest…");
        PackService.WriteManifest(manifestPath, manifest);
        File.Copy(inst.VersionJson, Path.Combine(outDir, "ac_version.json"), true);

        var installerDir = Path.Combine(inst.Root, "dist", "installer");
        if (Directory.Exists(installerDir))
        {
            try
            {
                File.Copy(zipPath, Path.Combine(installerDir, "ac-overlay.zip"), true);
            }
            catch { /* non bloccante */ }
        }

        string? synced = null;
        var target = (cfg.PublishTarget ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(target))
        {
            progress?.Report("Copia su publishTarget…");
            Directory.CreateDirectory(target);
            foreach (var name in new[] { PackService.ZipName, PackService.ManifestName, "ac_version.json" })
            {
                var src = Path.Combine(outDir, name);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(target, name), true);
            }
            synced = Path.GetFullPath(target);
        }

        Save(inst, cfg);
        TryBakeUserConfigTemplate(inst, cfg);
        progress?.Report("Snapshot ultimo publish…");
        DevChangeDetectService.CaptureAfterPublish(inst, version, outDir);

        var suggested = NormalizeFeedUrl(cfg.FeedBaseUrl);
        if (string.IsNullOrWhiteSpace(suggested))
        {
            suggested = synced ?? outDir;
            if (!suggested.EndsWith(Path.DirectorySeparatorChar) && !suggested.EndsWith('/'))
                suggested += Path.DirectorySeparatorChar;
        }

        string? feedCheck = null;
        if (!string.IsNullOrWhiteSpace(cfg.FeedBaseUrl))
            feedCheck = TryDescribeFeedReachability(cfg.FeedBaseUrl.Trim());

        return new PublishResult
        {
            OutDir = outDir,
            ZipPath = zipPath,
            ManifestPath = manifestPath,
            Sha256 = sha,
            SuggestedFeedUrl = suggested,
            PackageUrl = packageUrl,
            Version = version,
            SyncTarget = synced,
            FeedCheckMessage = feedCheck,
        };
    }

    public static bool HasPublishDestination(DevPublishConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.FeedBaseUrl) || !string.IsNullOrWhiteSpace(cfg.PublishTarget);

    public static string NormalizeFeedUrl(string? url)
    {
        var t = (url ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return "";
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (!t.EndsWith('/')) t += "/";
            return t;
        }
        try
        {
            var full = Path.GetFullPath(t.TrimEnd('\\', '/'));
            return full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch
        {
            return t;
        }
    }

    public static string TryDescribeFeedReachability(string feedBaseUrl)
    {
        var feed = NormalizeFeedUrl(feedBaseUrl);
        if (string.IsNullOrWhiteSpace(feed)) return "FeedBaseUrl vuoto.";

        try
        {
            if (Directory.Exists(feed.TrimEnd('\\', '/')))
            {
                var m = Path.Combine(feed.TrimEnd('\\', '/'), PackService.ManifestName);
                return File.Exists(m)
                    ? "Feed locale OK — manifest presente."
                    : "Feed locale: cartella ok (manifest apparirà dopo sync/copia).";
            }

            if (feed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var manifestUrl = feed + PackService.ManifestName;
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("MmxNetLauncher/0.1");
                    // MMX-Net: token env per check reachability (devkit non legge ac_config amici)
                    var tok = (
                        Environment.GetEnvironmentVariable("MMX_NET_UPDATE_FEED_TOKEN")
                        ?? Environment.GetEnvironmentVariable("AC_UPDATE_FEED_TOKEN")
                        ?? "").Trim();
                    using var req = UpdateService.CreateFeedRequest(HttpMethod.Get, manifestUrl, tok);
                    using var resp = http.SendAsync(req).GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                        return "Manifest remoto raggiungibile: " + manifestUrl;
                    var code = (int)resp.StatusCode;
                    var privHint = code is 401 or 403 or 404
                        ? " Repo privata? Serve collaboratore + PAT in updateFeedToken / MMX_NET_UPDATE_FEED_TOKEN."
                        : "";
                    return "URL feed impostato (" + feed + "). Manifest non ancora online (HTTP " +
                           code + ") — carica dist\\update\\ e push del feed." + privHint;
                }
                catch (Exception ex)
                {
                    return "URL feed impostato (" + feed + "). Check HTTP non riuscito ora: " +
                           ex.Message + " — normale se non hai ancora caricato i file.";
                }
            }
        }
        catch (Exception ex)
        {
            return "Check feed: " + ex.Message;
        }

        return "URL/path feed amici: " + feed;
    }

    public static void TryBakeUserConfigTemplate(Install inst, DevPublishConfig cfg)
    {
        try
        {
            var feed = NormalizeFeedUrl(cfg.FeedBaseUrl);
            if (string.IsNullOrWhiteSpace(feed)) return;

            var template = Path.Combine(inst.Root, "toolkit", "pack", "ac_config.user.json");
            if (!File.Exists(template)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(template));
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                var wroteFeed = false;
                var wroteCheck = false;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.NameEquals("updateFeedUrl"))
                    {
                        w.WriteString("updateFeedUrl", feed);
                        wroteFeed = true;
                    }
                    else if (p.NameEquals("checkUpdatesOnStart"))
                    {
                        w.WriteBoolean("checkUpdatesOnStart", true);
                        wroteCheck = true;
                    }
                    else p.WriteTo(w);
                }
                if (!wroteFeed) w.WriteString("updateFeedUrl", feed);
                if (!wroteCheck) w.WriteBoolean("checkUpdatesOnStart", true);
                w.WriteEndObject();
            }
            File.WriteAllBytes(template, stream.ToArray());

            try
            {
                var notePath = Path.Combine(inst.Root, "dist", "update", "LEGGIMI_FEED.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
                File.WriteAllText(notePath,
                    "MMX-Net — feed aggiornamenti\r\n" +
                    "================================\r\n\r\n" +
                    "URL da mettere in ac_config.json → updateFeedUrl (amici):\r\n" +
                    feed + "\r\n\r\n" +
                    "Repo GitHub PRIVATA:\r\n" +
                    "  1) Logan invita l'amico come Collaborator (Read) sulla repo del feed\r\n" +
                    "  2) L'amico crea un PAT fine-grained (Contents: Read) e lo mette in\r\n" +
                    "     ac_config.json → \"updateFeedToken\": \"github_pat_…\"\r\n" +
                    "     (oppure variabile d'ambiente AC_UPDATE_FEED_TOKEN — mai nel repo git)\r\n\r\n" +
                    "Il launcher utente all'avvio (checkUpdatesOnStart: true) mostra il popup\r\n" +
                    "se ac_update_manifest.json ha Version maggiore della locale.\r\n");
            }
            catch { /* ignore */ }
        }
        catch { /* non bloccante */ }
    }

    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true,
        });
    }

    public static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}
