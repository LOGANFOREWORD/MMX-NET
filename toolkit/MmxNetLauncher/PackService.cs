using System.IO;
using System.IO.Compression;
using System.Text.Json;
using MmxNetShared;

namespace MmxNetLauncher;

public static class PackService
{
    public const string ZipName = "ac-update.zip";
    public const string ManifestName = "ac_update_manifest.json";

    public static readonly string[] RootFiles =
    {
        "ac_version.json",
        "ac_steam_cop_bridge.cmd",
        "ac_steam_launch.args",
        "steam_appid.txt",
    };

    private static readonly HashSet<string> PreserveExisting =
        new(StringComparer.OrdinalIgnoreCase) { "ac_config.json" };

    public static readonly string[] DefaultGamedataRelPaths =
    {
        @"configs\axr_options.ltx",
        @"scripts\xrr_core.script",
        @"scripts\xrr_net_client.script",
        @"scripts\xrr_net_server.script",
        @"scripts\xrr_callbacks.script",
    };

    public static readonly string[] OptionalRepairRelPaths =
    {
        "xrRazom-release.txt",
        @"bin\steam_api64.dll",
        @"bin\GameNetworkingSockets.dll",
    };

    public static void PackUpdate(Install inst, string zipPath, PackVersion version, IEnumerable<string>? extraRelPaths = null)
    {
        inst.WriteVersion(version);
        var tmp = Path.Combine(Path.GetTempPath(), "ac_update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.Copy(inst.VersionJson, Path.Combine(tmp, "ac_version.json"), true);

            foreach (var name in RootFiles)
            {
                if (name.Equals("ac_version.json", StringComparison.OrdinalIgnoreCase)) continue;
                var src = Path.Combine(inst.Root, name);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(tmp, name), true);
            }

            // MMX-Net: packa l'exe utente; fallback legacy AnomalyCoop se non ancora rebuild
            var launcherCandidates = new[]
            {
                Path.Combine(inst.Root, "MMX-Net-Launcher.exe"),
                Path.Combine(inst.Root, "dist", "user", "MMX-Net-Launcher.exe"),
                Path.Combine(inst.Root, "AnomalyCoop-Launcher.exe"),
                Path.Combine(inst.Root, "dist", "user", "AnomalyCoop-Launcher.exe"),
            };
            var launcher = launcherCandidates.FirstOrDefault(File.Exists);
            if (launcher != null)
                File.Copy(launcher, Path.Combine(tmp, "MMX-Net-Launcher.exe"), true);

            // Uninstaller: non nello zip update (launcher+uninstaller self-contained >100MB GitHub).
            // Viene distribuito con installer / MmxNet-Download.zip e copiato in root all'install.

            var includeList = Path.Combine(inst.Root, "toolkit", "pack", "ac_pack_include.txt");
            var rels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in DefaultGamedataRelPaths)
                rels.Add(NormalizeRel(Path.Combine("gamedata", r)));
            foreach (var r in OptionalRepairRelPaths)
                rels.Add(NormalizeRel(r));
            if (extraRelPaths != null)
            {
                foreach (var r in extraRelPaths)
                    if (!string.IsNullOrWhiteSpace(r)) rels.Add(NormalizeRel(r));
            }
            if (File.Exists(includeList))
            {
                foreach (var line in File.ReadAllLines(includeList))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#') || t.StartsWith(';')) continue;
                    rels.Add(NormalizeRel(t));
                }
            }

            foreach (var rel in rels)
            {
                var src = Path.Combine(inst.Root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmp, zipPath, CompressionLevel.SmallestSize, false);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    public static PackVersion ApplyUpdate(Install inst, string zipPath, Action<string>? log = null)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Pacchetto aggiornamento non trovato.", zipPath);

        var tmp = Path.Combine(Path.GetTempPath(), "ac_apply_" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(zipPath, tmp);
        try
        {
            var pending = CopyTreeOverlay(tmp, inst.Root);
            foreach (var rel in pending)
                log?.Invoke("File in uso — scritto " + rel + FileOverlay.PendingSuffix + " (si applica al riavvio).");

            var verFile = Path.Combine(tmp, "ac_version.json");
            if (File.Exists(verFile))
            {
                if (FileOverlay.CopyOverwriteOrPending(verFile, inst.VersionJson, out var verPending) && verPending != null)
                    log?.Invoke("File in uso — scritto " + Path.GetFileName(verPending) + " (si applica al riavvio).");
            }

            // Dopo overlay: togli exe/artifact AnomalyCoop non più usati (MMX-Net)
            foreach (var name in LegacyCleanup.RemoveFromInstallRoot(inst.Root))
                log?.Invoke("Rimosso legacy: " + name);

            return inst.ReadVersion();
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Copia overlay con overwrite. File già presenti in PreserveExisting (es. ac_config.json) restano.
    /// Se destinazione locked → scrive .new e restituisce i path relativi pending.
    /// </summary>
    public static IReadOnlyList<string> CopyTreeOverlay(string srcRoot, string dstRoot)
    {
        srcRoot = Path.GetFullPath(srcRoot);
        dstRoot = Path.GetFullPath(dstRoot);
        Directory.CreateDirectory(dstRoot);
        var pending = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(srcRoot, file);
            var name = Path.GetFileName(rel);
            var dst = Path.Combine(dstRoot, rel);
            if (PreserveExisting.Contains(name) && File.Exists(dst))
                continue;
            if (FileOverlay.CopyOverwriteOrPending(file, dst, out _))
                pending.Add(rel.Replace('/', '\\'));
        }
        return pending;
    }

    public static UpdateManifest BuildManifest(PackVersion version, string packageUrl, string? sha256 = null)
    {
        return new UpdateManifest
        {
            Name = version.Name,
            Version = version.Version,
            Channel = version.Channel,
            Protocol = version.Protocol,
            Engine = version.Engine,
            Notes = version.Notes,
            PackageUrl = packageUrl,
            PackageSha256 = sha256 ?? "",
            PublishedUtc = DateTime.UtcNow.ToString("o"),
        };
    }

    public static void WriteManifest(string path, UpdateManifest m)
    {
        var json = JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, json);
    }

    private static string NormalizeRel(string rel) =>
        rel.Replace('\\', '/').Trim().TrimStart('/');
}

public sealed class UpdateManifest
{
    public string Name { get; set; } = "MMX-Net";
    public string Version { get; set; } = "0.1.0";
    public string Channel { get; set; } = "dev";
    public string Protocol { get; set; } = "89";
    public string Engine { get; set; } = "ST";
    public string Notes { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public string PublishedUtc { get; set; } = "";
    public string MinLauncherVersion { get; set; } = "0.1.0";

    public PackVersion ToPackVersion() => new()
    {
        Name = Name,
        Version = Version,
        Channel = Channel,
        Protocol = Protocol,
        Engine = Engine,
        Notes = Notes,
    };
}
