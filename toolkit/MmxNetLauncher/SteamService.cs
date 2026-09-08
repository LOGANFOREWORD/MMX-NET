using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MmxNetLauncher;

/// <summary>
/// Overlay Steam (Shift+Tab) si aggancia SOLO se Steam avvia DIRETTAMENTE l'exe Anomaly.
/// Avvio "diretto" dal launcher = niente overlay (ecco il bug inviti).
///
/// Metodo: shortcut Non-Steam in libreria Steam → Exe = AnomalyDX11AVX.exe
/// → steam://rungameid/... così Steam CreateProcess(Anomaly) + overlay + AppID 41700.
/// </summary>
public static class SteamService
{
    public const string AppId = Install.SteamAppId;
    public const string ShortcutAppName = "Call of Pripyat — MMX-Net";
    // MMX-Net: rinomina shortcut legacy Anomaly Coop allo stesso AppName
    public const string LegacyShortcutAppName = "Call of Pripyat — Anomaly Coop";

    public static bool IsSteamRunning()
    {
        try
        {
            if (Process.GetProcessesByName("steam").Length > 0) return true;
            if (Process.GetProcessesByName("Steam").Length > 0) return true;
            return Process.GetProcessesByName("steamwebhelper").Length > 0
                   || Process.GetProcessesByName("SteamWebHelper").Length > 0;
        }
        catch { return false; }
    }

    public static string? FindSteamExe()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var p = k?.GetValue("SteamExe") as string;
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
        }
        catch { /* ignore */ }

        var guesses = new[]
        {
            @"C:\Program Files (x86)\Steam\steam.exe",
            @"C:\Program Files\Steam\steam.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
        };
        return guesses.FirstOrDefault(File.Exists);
    }

    public static string? FindSteamPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var p = k?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                return p.Replace('/', '\\');
        }
        catch { /* ignore */ }

        var exe = FindSteamExe();
        return exe == null ? null : Path.GetDirectoryName(exe);
    }

    public static void EnsureSteamReady(bool required)
    {
        if (!required) return;
        if (IsSteamRunning()) return;

        var steam = FindSteamExe();
        if (steam == null)
            throw new InvalidOperationException("Steam non installato.");

        Process.Start(new ProcessStartInfo { FileName = steam, UseShellExecute = true });
        for (var i = 0; i < 50; i++)
        {
            Thread.Sleep(500);
            if (IsSteamRunning()) return;
        }
        throw new InvalidOperationException("Steam non risponde. Aprilo e riprova.");
    }

    public static string StatusText()
    {
        if (!IsSteamRunning())
            return FindSteamExe() == null ? "Steam: non installato" : "Steam: non in esecuzione";
        return "Steam: pronto · overlay richiede avvio da libreria Steam";
    }

    public static void WriteAppId(string dir, bool enabled)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "steam_appid.txt"), enabled ? AppId : "480");
    }

    public static string? FindCallOfPripyatManifest()
    {
        var steam = FindSteamPath();
        if (steam == null) return null;
        foreach (var lib in EnumerateSteamLibraryPaths(steam))
        {
            var acf = Path.Combine(lib, "steamapps", $"appmanifest_{AppId}.acf");
            if (File.Exists(acf)) return acf;
        }
        return null;
    }

    public static IEnumerable<string> EnumerateSteamLibraryPaths(string steamPath)
    {
        yield return steamPath;
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        string text;
        try { text = File.ReadAllText(vdf); }
        catch { yield break; }
        foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
        {
            var p = m.Groups[1].Value.Replace("\\\\", "\\");
            if (Directory.Exists(p)) yield return p;
        }
    }

    /// <summary>
    /// Unico metodo affidabile per Shift+Tab: Steam deve avviare Anomaly.exe.
    /// </summary>
    public static string LaunchAnomalyAsCallOfPripyat(
        string exePath,
        IReadOnlyList<string> args,
        string startDir,
        Install inst)
    {
        EnsureSteamReady(true);
        WriteAppId(inst.Bin, true);
        WriteAppId(inst.Root, true);

        var steamExe = FindSteamExe()
            ?? throw new InvalidOperationException("steam.exe non trovato.");

        var launchOpts = string.Join(' ', args);
        // 1) CoP Launch Options → Anomaly DIRETTA (niente .cmd + start: quello spezza l'overlay)
        try
        {
            EnsureCopLaunchOptionsPointToAnomaly(exePath, launchOpts);
        }
        catch { /* CoP assente ok */ }

        // 2) Shortcut libreria: Steam CreateProcess(Anomaly) = overlay
        var id = RegisterSteamLibraryShortcut(exePath, startDir, launchOpts, inst);
        var marker = Path.Combine(inst.Root, "ac_steam_library_ok.txt");
        if (!File.Exists(marker))
        {
            RestartSteamAndWait(steamExe);
            id = RegisterSteamLibraryShortcut(exePath, startDir, launchOpts, inst);
            File.WriteAllText(marker, id.ToString());
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{id}",
            UseShellExecute = true,
        });

        for (var i = 0; i < 30; i++)
        {
            Thread.Sleep(500);
            if (GameLooksRunning(exePath))
            {
                OpenFriends();
                return $"Steam library rungameid/{id} → Anomaly (overlay ON) · AppID {AppId}";
            }
        }

        // Ultimo fallback: diretto (overlay probabilmente assente — avvisa)
        StartDirectLikeXrMpe(exePath, args, startDir);
        OpenFriends();
        return "fallback diretto -steam (overlay può mancare: in Steam cerca «Call of Pripyat — MMX-Net» e avvialo da lì)";
    }

    /// <summary>
    /// Launch Options CoP = Anomaly.exe DIRETTA (Steam inietta overlay su Anomaly).
    /// </summary>
    public static void EnsureCopLaunchOptionsPointToAnomaly(string anomalyExe, string launchOpts)
    {
        if (FindCallOfPripyatManifest() == null) return;
        var opt = $"\"{anomalyExe}\" {launchOpts}";
        EnsureCopLaunchOptions(opt);
    }

    public static void EnsureCopLaunchOptions(string launchOptions)
    {
        var steam = FindSteamPath() ?? throw new InvalidOperationException("Steam path non trovato.");
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata))
            throw new InvalidOperationException("Steam userdata non trovato.");

        var any = false;
        foreach (var userDir in Directory.GetDirectories(userdata))
        {
            var name = Path.GetFileName(userDir);
            if (name is null || !name.All(char.IsDigit) || name == "0") continue;
            var local = Path.Combine(userDir, "config", "localconfig.vdf");
            if (!File.Exists(local)) continue;
            if (SetLaunchOptionsInLocalConfig(local, AppId, launchOptions))
                any = true;
        }
        if (!any)
            throw new InvalidOperationException("localconfig.vdf: impossibile scrivere Launch Options.");
    }

    public static bool SetLaunchOptionsInLocalConfig(string path, string appId, string launchOptions)
    {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch { return false; }

        try { File.Copy(path, path + ".ac_bak", overwrite: true); } catch { /* ignore */ }

        var escaped = launchOptions.Replace("\\", "\\\\");

        var rxOpt = new Regex(
            $"(\"{appId}\"\\s*\\{{[\\s\\S]*?\"LaunchOptions\"\\s*\")([^\\\"]*)(\")",
            RegexOptions.IgnoreCase);
        if (rxOpt.IsMatch(text))
        {
            text = rxOpt.Replace(text, m => m.Groups[1].Value + escaped + m.Groups[3].Value, 1);
            File.WriteAllText(path, text, Encoding.UTF8);
            return true;
        }

        var rxApp = new Regex($"(\"{appId}\"\\s*\\{{)", RegexOptions.IgnoreCase);
        if (rxApp.IsMatch(text))
        {
            text = rxApp.Replace(text,
                m => m.Groups[1].Value + "\n\t\t\t\t\t\"LaunchOptions\"\t\t\"" + escaped + "\"",
                1);
            File.WriteAllText(path, text, Encoding.UTF8);
            return true;
        }

        var rxApps = new Regex("(\"apps\"\\s*\\{)", RegexOptions.IgnoreCase);
        if (rxApps.IsMatch(text))
        {
            var block =
                "\n\t\t\t\t\"" + appId + "\"\n\t\t\t\t{\n" +
                "\t\t\t\t\t\"LaunchOptions\"\t\t\"" + escaped + "\"\n" +
                "\t\t\t\t}\n";
            text = rxApps.Replace(text, m => m.Groups[1].Value + block, 1);
            File.WriteAllText(path, text, Encoding.UTF8);
            return true;
        }
        return false;
    }

    public static uint RegisterSteamLibraryShortcut(
        string exePath,
        string startDir,
        string launchOptions,
        Install inst)
    {
        var steamPath = FindSteamPath()
            ?? throw new InvalidOperationException("Steam path non trovato.");
        var userdata = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userdata))
            throw new InvalidOperationException("Steam userdata non trovato.");

        var exeQuoted = "\"" + exePath + "\"";
        var dirQuoted = "\"" + startDir.TrimEnd('\\') + "\\\"";
        uint lastId = 0;

        foreach (var userDir in Directory.GetDirectories(userdata))
        {
            var name = Path.GetFileName(userDir);
            if (name is null || !name.All(char.IsDigit) || name == "0") continue;
            var cfg = Path.Combine(userDir, "config");
            Directory.CreateDirectory(cfg);
            var shortcutsPath = Path.Combine(cfg, "shortcuts.vdf");
            lastId = UpsertShortcut(shortcutsPath, ShortcutAppName, exeQuoted, dirQuoted, launchOptions);
            File.WriteAllText(Path.Combine(inst.Root, "ac_steam_shortcut_id.txt"), lastId.ToString());
        }

        if (lastId == 0)
            throw new InvalidOperationException("Nessun account Steam userdata per lo shortcut.");
        return lastId;
    }

    private static uint UpsertShortcut(
        string shortcutsPath,
        string appName,
        string exeQuoted,
        string startDirQuoted,
        string launchOptions)
    {
        var entries = File.Exists(shortcutsPath)
            ? ShortcutVdf.Read(shortcutsPath)
            : new List<ShortcutEntry>();

        var existing = entries.FirstOrDefault(e =>
            e.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase) ||
            e.AppName.Equals(LegacyShortcutAppName, StringComparison.OrdinalIgnoreCase) ||
            e.Exe.Equals(exeQuoted, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.AppName = appName;
            existing.Exe = exeQuoted;
            existing.StartDir = startDirQuoted;
            existing.LaunchOptions = launchOptions;
            existing.AllowOverlay = true;
            existing.AllowDesktopConfig = true;
        }
        else
        {
            entries.Add(new ShortcutEntry
            {
                AppName = appName,
                Exe = exeQuoted,
                StartDir = startDirQuoted,
                LaunchOptions = launchOptions,
                AllowOverlay = true,
                AllowDesktopConfig = true,
            });
        }

        ShortcutVdf.Write(shortcutsPath, entries);
        return ShortcutVdf.ComputeAppId(exeQuoted, appName);
    }

    public static void StartDirectLikeXrMpe(string exePath, IReadOnlyList<string> args, string startDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = startDir,
            UseShellExecute = false,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        psi.Environment["SteamAppId"] = AppId;
        psi.Environment["SteamGameId"] = AppId;
        psi.Environment["SteamOverlayGameId"] = AppId;
        var p = Process.Start(psi);
        if (p == null)
            throw new InvalidOperationException("Process.Start fallito: " + exePath);
    }

    private static void OpenFriends()
    {
        try
        {
            Process.Start(new ProcessStartInfo("steam://open/friends") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private static void RestartSteamAndWait(string steamExe)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("steam"))
            {
                try { p.CloseMainWindow(); } catch { /* ignore */ }
            }
            Thread.Sleep(2500);
            foreach (var p in Process.GetProcessesByName("steam"))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
            Thread.Sleep(2500);
        }
        catch { /* ignore */ }

        Process.Start(new ProcessStartInfo { FileName = steamExe, UseShellExecute = true });
        for (var i = 0; i < 80; i++)
        {
            Thread.Sleep(500);
            if (IsSteamRunning())
            {
                Thread.Sleep(4000);
                return;
            }
        }
    }

    private static bool GameLooksRunning(string exePath)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(exePath);
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path != null &&
                        path.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        return false;
    }
}

internal sealed class ShortcutEntry
{
    public string AppName { get; set; } = "";
    public string Exe { get; set; } = "";
    public string StartDir { get; set; } = "";
    public string LaunchOptions { get; set; } = "";
    public string Icon { get; set; } = "";
    public string ShortcutPath { get; set; } = "";
    public bool AllowDesktopConfig { get; set; } = true;
    public bool AllowOverlay { get; set; } = true;
    public bool IsHidden { get; set; }
    public int LastPlayTime { get; set; }
}

internal static class ShortcutVdf
{
    public static uint ComputeAppId(string exeQuoted, string appName)
    {
        var crc = Crc32(Encoding.UTF8.GetBytes(exeQuoted + appName));
        return crc | 0x80000000u;
    }

    public static List<ShortcutEntry> Read(string path)
    {
        var list = new List<ShortcutEntry>();
        try
        {
            var data = File.ReadAllBytes(path);
            var i = 0;
            if (i >= data.Length || data[i++] != 0x00) return list;
            if (ReadString(data, ref i) != "shortcuts") return list;
            while (i < data.Length)
            {
                if (data[i] == 0x08) { i++; break; }
                if (data[i] != 0x00) break;
                i++;
                var idx = ReadString(data, ref i);
                if (!int.TryParse(idx, out _))
                {
                    SkipObject(data, ref i);
                    continue;
                }
                list.Add(ReadEntry(data, ref i));
            }
        }
        catch { list.Clear(); }
        return list;
    }

    public static void Write(string path, List<ShortcutEntry> entries)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            WriteObjectStart(w, "shortcuts");
            for (var n = 0; n < entries.Count; n++)
            {
                WriteObjectStart(w, n.ToString());
                var e = entries[n];
                WriteString(w, "appname", e.AppName);
                WriteString(w, "Exe", e.Exe);
                WriteString(w, "StartDir", e.StartDir);
                WriteString(w, "icon", e.Icon);
                WriteString(w, "ShortcutPath", e.ShortcutPath);
                WriteString(w, "LaunchOptions", e.LaunchOptions);
                WriteInt(w, "IsHidden", e.IsHidden ? 1 : 0);
                WriteInt(w, "AllowDesktopConfig", e.AllowDesktopConfig ? 1 : 0);
                WriteInt(w, "AllowOverlay", e.AllowOverlay ? 1 : 0);
                WriteInt(w, "OpenVR", 0);
                WriteInt(w, "Devkit", 0);
                WriteString(w, "DevkitGameID", "");
                WriteInt(w, "DevkitOverrideAppID", 0);
                WriteInt(w, "LastPlayTime", e.LastPlayTime);
                WriteString(w, "FlatpakAppID", "");
                WriteObjectStart(w, "tags");
                w.Write((byte)0x08);
                w.Write((byte)0x08);
            }
            w.Write((byte)0x08);
        }
        if (File.Exists(path))
        {
            try { File.Copy(path, path + ".ac_bak", overwrite: true); } catch { /* ignore */ }
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static ShortcutEntry ReadEntry(byte[] data, ref int i)
    {
        var e = new ShortcutEntry();
        while (i < data.Length)
        {
            var t = data[i++];
            if (t == 0x08) break;
            var key = ReadString(data, ref i);
            switch (t)
            {
                case 0x01:
                    Assign(e, key, ReadString(data, ref i));
                    break;
                case 0x02:
                    var n = BitConverter.ToInt32(data, i); i += 4;
                    AssignInt(e, key, n);
                    break;
                case 0x00:
                    SkipObject(data, ref i);
                    break;
                default:
                    return e;
            }
        }
        return e;
    }

    private static void Assign(ShortcutEntry e, string key, string v)
    {
        switch (key.ToLowerInvariant())
        {
            case "appname": e.AppName = v; break;
            case "exe": e.Exe = v; break;
            case "startdir": e.StartDir = v; break;
            case "launchoptions": e.LaunchOptions = v; break;
            case "icon": e.Icon = v; break;
            case "shortcutpath": e.ShortcutPath = v; break;
        }
    }

    private static void AssignInt(ShortcutEntry e, string key, int v)
    {
        switch (key.ToLowerInvariant())
        {
            case "allowoverlay": e.AllowOverlay = v != 0; break;
            case "allowdesktopconfig": e.AllowDesktopConfig = v != 0; break;
            case "ishidden": e.IsHidden = v != 0; break;
            case "lastplaytime": e.LastPlayTime = v; break;
        }
    }

    private static void SkipObject(byte[] data, ref int i)
    {
        while (i < data.Length)
        {
            var t = data[i++];
            if (t == 0x08) return;
            ReadString(data, ref i);
            if (t == 0x00) SkipObject(data, ref i);
            else if (t == 0x01) ReadString(data, ref i);
            else if (t == 0x02) i += 4;
            else return;
        }
    }

    private static string ReadString(byte[] data, ref int i)
    {
        var start = i;
        while (i < data.Length && data[i] != 0) i++;
        var s = Encoding.UTF8.GetString(data, start, i - start);
        if (i < data.Length) i++;
        return s;
    }

    private static void WriteObjectStart(BinaryWriter w, string name)
    {
        w.Write((byte)0x00);
        WriteCString(w, name);
    }

    private static void WriteString(BinaryWriter w, string key, string value)
    {
        w.Write((byte)0x01);
        WriteCString(w, key);
        WriteCString(w, value ?? "");
    }

    private static void WriteInt(BinaryWriter w, string key, int value)
    {
        w.Write((byte)0x02);
        WriteCString(w, key);
        w.Write(value);
    }

    private static void WriteCString(BinaryWriter w, string s)
    {
        w.Write(Encoding.UTF8.GetBytes(s));
        w.Write((byte)0);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
