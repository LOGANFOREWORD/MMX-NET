using System.IO;
using System.Text.Json;

namespace MmxNetLauncher;

public sealed class Install
{
    public const string DefaultRoot = @"F:\Anomaly Coop";
    public const string SteamAppId = "41700";

    public string Root { get; }
    public string Bin => Path.Combine(Root, "bin");
    public string Gamedata => Path.Combine(Root, "gamedata");
    public string Db => Path.Combine(Root, "db");
    public string AppData => Path.Combine(Root, "appdata");
    public string Logs => Path.Combine(AppData, "logs");
    public string Fsgame => Path.Combine(Root, "fsgame.ltx");
    public string ConfigJson => Path.Combine(Root, "ac_config.json");
    public string VersionJson => Path.Combine(Root, "ac_version.json");
    public string RazomRelease => Path.Combine(Root, "xrRazom-release.txt");
    public string LauncherCfg => Path.Combine(Root, "AnomalyLauncher.cfg");
    public string CommandLineTxt => Path.Combine(Root, "commandline.txt");
    public string SteamAppIdFile => Path.Combine(Bin, "steam_appid.txt");
    public string AxrOptions => Path.Combine(Gamedata, "configs", "axr_options.ltx");

    public Install(string root) => Root = root;

    public string ResolveClientExe()
    {
        var dx = "DX11";
        var cpu = "AVX";
        try
        {
            if (File.Exists(LauncherCfg))
            {
                var lines = File.ReadAllLines(LauncherCfg);
                if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0])) dx = lines[0].Trim();
                if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1])) cpu = lines[1].Trim();
            }
        }
        catch { /* default */ }

        var name = $"anomaly{dx.ToLowerInvariant()}{cpu.ToLowerInvariant()}.exe";
        var preferred = Path.Combine(Bin, name);
        if (File.Exists(preferred)) return preferred;

        foreach (var guess in new[]
                 {
                     "anomalydx11avx.exe", "anomalydx11.exe", "anomalydx10avx.exe",
                     "anomalydx9avx.exe", "AnomalyDX11AVX.exe"
                 })
        {
            var p = Path.Combine(Bin, guess);
            if (File.Exists(p)) return p;
        }
        return preferred;
    }

    public bool IsValid
    {
        get
        {
            try { return File.Exists(ResolveClientExe()) && File.Exists(Fsgame); }
            catch { return false; }
        }
    }

    public bool LooksLikeRoot
    {
        get
        {
            try
            {
                if (File.Exists(Fsgame)) return true;
                if (File.Exists(ConfigJson) || File.Exists(VersionJson)) return true;
                // MMX-Net: riconosce exe nuovo e legacy AnomalyCoop durante migrazione
                if (File.Exists(Path.Combine(Root, "MMX-Net-Launcher.exe"))) return true;
                if (File.Exists(Path.Combine(Root, "MMX-Net-Launcher-dev.exe"))) return true;
                if (File.Exists(Path.Combine(Root, "AnomalyCoop-Launcher.exe"))) return true;
                if (File.Exists(Path.Combine(Root, "AnomalyCoop-Launcher-dev.exe"))) return true;
                if (File.Exists(RazomRelease)) return true;
                if (Directory.Exists(Bin) && Directory.Exists(Gamedata)) return true;
                return false;
            }
            catch { return false; }
        }
    }

    public static Install Discover()
    {
        var exeDir = NormDir(Path.GetDirectoryName(Environment.ProcessPath));
        var baseDir = NormDir(AppContext.BaseDirectory);
        var parentExe = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(exeDir))
                parentExe = NormDir(Directory.GetParent(exeDir)?.FullName);
        }
        catch { /* ignore */ }

        foreach (var c in DistinctDirs(exeDir, baseDir, parentExe))
        {
            var inst = new Install(c);
            if (inst.LooksLikeRoot) return inst;
        }

        foreach (var c in DistinctDirs(exeDir, baseDir, parentExe, DefaultRoot))
        {
            var inst = new Install(c);
            if (inst.IsValid) return inst;
        }

        if (!string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir))
            return new Install(exeDir);

        if (!string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
            return new Install(baseDir);

        return new Install(DefaultRoot);
    }

    private static string NormDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            return Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch { return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }

    private static IEnumerable<string> DistinctDirs(params string[] dirs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dirs)
        {
            if (string.IsNullOrWhiteSpace(d)) continue;
            if (!seen.Add(d)) continue;
            yield return d;
        }
    }

    public bool GetConfigBool(string key, bool fallback = false)
    {
        try
        {
            if (!File.Exists(ConfigJson)) return fallback;
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigJson));
            if (doc.RootElement.TryGetProperty(key, out var v) &&
                v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return v.GetBoolean();
        }
        catch { /* ignore */ }
        return fallback;
    }

    public string GetConfigString(string key, string fallback = "")
    {
        try
        {
            if (!File.Exists(ConfigJson)) return fallback;
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigJson));
            if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? fallback;
        }
        catch { /* ignore */ }
        return fallback;
    }

    public void SetConfigBool(string key, bool value) =>
        SetConfigValue(key, (w, k) => w.WriteBoolean(k, value));

    public void SetConfigString(string key, string value) =>
        SetConfigValue(key, (w, k) => w.WriteString(k, value));

    private void SetConfigValue(string key, Action<Utf8JsonWriter, string> writeValue)
    {
        try
        {
            var json = File.Exists(ConfigJson) ? File.ReadAllText(ConfigJson) : "{}";
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                var wrote = false;
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Name == key)
                    {
                        writeValue(w, key);
                        wrote = true;
                    }
                    else p.WriteTo(w);
                }
                if (!wrote) writeValue(w, key);
                w.WriteEndObject();
            }
            File.WriteAllBytes(ConfigJson, stream.ToArray());
        }
        catch { /* non bloccare */ }
    }

    public PackVersion ReadVersion()
    {
        try
        {
            if (File.Exists(VersionJson))
                return JsonSerializer.Deserialize<PackVersion>(File.ReadAllText(VersionJson))
                       ?? PackVersion.Default;
        }
        catch { /* ignore */ }
        return PackVersion.Default;
    }

    public void WriteVersion(PackVersion v)
    {
        var json = JsonSerializer.Serialize(v, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(VersionJson, json);
    }

    public string PackStatus()
    {
        var v = ReadVersion();
        var xrr = File.Exists(Path.Combine(Gamedata, "scripts", "xrr_core.script"));
        return $"{v.Name} {v.Version} · xrRazom {(xrr ? "OK" : "MANCANTE")} · {v.Channel}";
    }
}

public sealed class PackVersion
{
    public string Name { get; set; } = "MMX-Net";
    public string Version { get; set; } = "0.1.0";
    public string Channel { get; set; } = "dev";
    public string Protocol { get; set; } = "89";
    public string Engine { get; set; } = "ST";
    public string Notes { get; set; } = "Anomaly 1.5.3 + xrRazom 1.3 Steam P2P (MMX-Net).";
    public static PackVersion Default => new();
}
