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
    public string? PushFeedMessage { get; init; }
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

        string? pushMsg = null;
        if (!string.IsNullOrWhiteSpace(synced) && LooksLikeGitFeedClone(synced))
        {
            progress?.Report("Push feed su GitHub…");
            pushMsg = TryPushUpdateFeed(inst, synced, progress);
            // Ripeti reachability dopo push (con token se disponibile).
            if (!string.IsNullOrWhiteSpace(cfg.FeedBaseUrl))
                feedCheck = TryDescribeFeedReachability(cfg.FeedBaseUrl.Trim());
        }

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
            PushFeedMessage = pushMsg,
        };
    }

    public static bool LooksLikeGitFeedClone(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        try
        {
            var full = Path.GetFullPath(dir.Trim());
            return Directory.Exists(Path.Combine(full, ".git"));
        }
        catch { return false; }
    }

    /// <summary>
    /// Dopo sync su PublishTarget (clone MMX-NET): esegue toolkit\push_update_feed.ps1
    /// oppure git add/commit/push inline. Non force-push.
    /// </summary>
    public static string TryPushUpdateFeed(Install inst, string? publishTarget, IProgress<string>? progress = null)
    {
        var target = (publishTarget ?? "").Trim();
        if (string.IsNullOrWhiteSpace(target))
            return "Push saltato: PublishTarget vuoto.";
        if (!LooksLikeGitFeedClone(target))
            return "Push saltato: PublishTarget non è un clone git (.git assente).";

        var script = Path.Combine(inst.Root, "toolkit", "push_update_feed.ps1");
        if (File.Exists(script))
        {
            progress?.Report("Esecuzione push_update_feed.ps1…");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                    WorkingDirectory = Path.GetDirectoryName(script) ?? inst.Root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return "Push fallito: impossibile avviare PowerShell.";
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(120_000);
                var combined = ((stdout ?? "") + "\n" + (stderr ?? "")).Trim();
                if (p.ExitCode == 0)
                {
                    progress?.Report("Push feed OK.");
                    return string.IsNullOrWhiteSpace(combined)
                        ? "Push feed OK (push_update_feed.ps1)."
                        : "Push feed OK: " + TruncateOneLine(combined, 240);
                }
                return "Push feed fallito (exit " + p.ExitCode + "): " + TruncateOneLine(combined, 280);
            }
            catch (Exception ex)
            {
                return "Push feed fallito: " + ex.Message;
            }
        }

        // Fallback inline se lo script manca
        return TryGitPushInline(target, progress);
    }

    private static string TryGitPushInline(string target, IProgress<string>? progress)
    {
        try
        {
            var git = ResolveGitExe();
            progress?.Report("git add/commit/push…");
            RunGit(git, target, "add -A");
            var status = RunGit(git, target, "status --porcelain").Trim();
            if (string.IsNullOrWhiteSpace(status))
                return "Nessuna modifica da pushare nel clone feed.";

            var ver = "update";
            var verFile = Path.Combine(target, "ac_version.json");
            if (File.Exists(verFile))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(verFile));
                    if (doc.RootElement.TryGetProperty("Version", out var v) &&
                        v.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(v.GetString()))
                        ver = v.GetString()!.Trim();
                }
                catch { /* ignore */ }
            }

            RunGit(git, target, "commit -m \"feed " + ver.Replace("\"", "") + "\"");
            RunGit(git, target, "push");
            progress?.Report("Push feed OK.");
            return "Push feed OK (git inline) — versione " + ver + ".";
        }
        catch (Exception ex)
        {
            return "Push feed fallito: " + ex.Message;
        }
    }

    private static string ResolveGitExe()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe",
            "git",
        };
        foreach (var c in candidates)
        {
            if (c == "git") return c;
            if (File.Exists(c)) return c;
        }
        return "git";
    }

    private static string RunGit(string git, string workDir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = git,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git non avviabile.");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException(
                "git " + args + " → " + TruncateOneLine((stderr + " " + stdout).Trim(), 220));
        return stdout ?? "";
    }

    private static string TruncateOneLine(string s, int max)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
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
                    var tok = (
                        Environment.GetEnvironmentVariable("MMX_NET_UPDATE_FEED_TOKEN")
                        ?? Environment.GetEnvironmentVariable("AC_UPDATE_FEED_TOKEN")
                        ?? "").Trim();
#if DEVKIT
                    if (string.IsNullOrWhiteSpace(tok))
                        tok = UpdateService.TryReadGhAuthToken() ?? "";
#endif
                    using var req = UpdateService.CreateFeedRequest(HttpMethod.Get, manifestUrl, tok);
                    using var resp = http.SendAsync(req).GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                        return "Manifest remoto raggiungibile: " + manifestUrl;
                    var code = (int)resp.StatusCode;
                    var privHint = code is 401 or 403 or 404
                        ? (string.IsNullOrWhiteSpace(tok)
                            ? " Se il feed e' MMX-NET-feed (pubblico) e 404: push mancante. Se punta a MMX-NET privata: cambia FeedBaseUrl."
                            : " Token presente ma HTTP " + code + " — preferisci feed pubblico MMX-NET-feed (token vuoto), o push mancante.")
                        : "";
                    return "URL feed impostato (" + feed + "). Manifest non ancora online (HTTP " +
                           code + ") — dopo CARICA serve push del feed (auto se PublishTarget è clone)." + privHint;
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
                    "Repo PUBBLICA solo-feed (MMX-NET-feed): nessun PAT.\r\n" +
                    "  Codice/prodotto resta su MMX-NET privata (solo owner write).\r\n" +
                    "  Dopo CARICA: push automatico se PublishTarget e' il clone feed,\r\n" +
                    "  oppure toolkit\\push_update_feed.ps1\r\n\r\n" +
                    "Solo LOGANFOREWORD puo' pushare. Vedi FEED_PRIVATO_AMICI.md\r\n\r\n" +
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
