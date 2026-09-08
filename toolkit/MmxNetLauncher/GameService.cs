using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MmxNetLauncher;

/// <summary>
/// Avvio Anomaly + xrRazom. Niente dedicated: host = giocatore in-game (Steam listen).
/// Steam vede il processo come Call of Pripyat (App ID 41700) → Shift+Tab / inviti amici.
/// </summary>
public sealed class GameService
{
    private readonly Install _inst;
    public GameService(Install inst) => _inst = inst;

    /// <param name="asHost">
    /// true = HOST: xrr_host_session + xrr_host_steam (amici entrano da Shift+Tab / Join Game).
    /// false = PLAY/join: solo steam on, niente host auto (niente dialog IP).
    /// </param>
    public string PlayClient(bool steam, bool stripDebug, bool asHost)
    {
        var exe = _inst.ResolveClientExe();
        if (!File.Exists(exe))
            throw new FileNotFoundException("Client Anomaly non trovato in bin\\.", exe);

        if (IsOurGameRunning())
        {
            FocusOurGame();
            return "(già aperto — focus)";
        }

        // Come MMX: Steam App ID 41700 (Call of Pripyat) + steam_api.
        // Come guida xrRazom: host_session + host_steam → overlay invite, NON dialog IP.
        PrepareSteamAsCallOfPripyat(steam);
        if (stripDebug)
            ApplyFriendsProfile();
        ApplyCoopSafeOptions();
        ApplyXrRazomNetMode(steam, asHost);

        if (!steam)
            throw new InvalidOperationException(
                "Per inviti Shift+Tab serve Steam (come xrMPE «Use Steam to connect»).");

        // Come xrmpe-launcher: -steam primo + App ID 41700.
        // Preferisce avvio da libreria Steam (overlay); fallback = diretto come xrMPE.
        var argList = BuildArgList(steam, stripDebug);
        return SteamService.LaunchAnomalyAsCallOfPripyat(exe, argList, _inst.Root, _inst);
    }

    /// <summary>
    /// Forza user.ltx: Steam P2P come CoP. HOST = sessione all'avvio partita;
    /// PLAY = non host (l'amico entra da overlay, non da "Connect" IP).
    /// </summary>
    public void ApplyXrRazomNetMode(bool steam, bool asHost)
    {
        var path = Path.Combine(_inst.AppData, "user.ltx");
        if (!File.Exists(path)) return;
        try
        {
            var lines = File.ReadAllLines(path).ToList();
            SetOrAddUserVar(lines, "xrr_host_steam", steam ? "on" : "off");
            SetOrAddUserVar(lines, "xrr_host_session", asHost && steam ? "on" : "off");
            File.WriteAllLines(path, lines);
        }
        catch { /* non bloccare */ }
    }

    private static void SetOrAddUserVar(List<string> lines, string key, string value)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase) ||
                t.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = key + " " + value;
                return;
            }
        }
        lines.Add(key + " " + value);
    }

    /// <summary>
    /// steam_appid 41700 in bin\ (accanto all'exe) e in root (cwd),
    /// così Steam Overlay / Friends vedono Call of Pripyat.
    /// </summary>
    public void PrepareSteamAsCallOfPripyat(bool enabled)
    {
        SteamService.WriteAppId(_inst.Bin, enabled);
        SteamService.WriteAppId(_inst.Root, enabled);
    }

    public static List<string> BuildArgList(bool steam, bool stripDebug)
    {
        var parts = new List<string>();
        if (steam) parts.Add("-steam"); // ordine come log xrMPE
        parts.Add("-smap2048");
        if (!stripDebug) parts.Add("-dbg");
        return parts;
    }

    public static string BuildArgs(bool steam, bool stripDebug) =>
        string.Join(' ', BuildArgList(steam, stripDebug));

    public void ApplyFriendsProfile()
    {
        try
        {
            if (File.Exists(_inst.CommandLineTxt))
            {
                var lines = File.ReadAllLines(_inst.CommandLineTxt)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l)
                                && !l.Equals("-dbg", StringComparison.OrdinalIgnoreCase)
                                && !l.Equals("/dbg", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (lines.Count == 0) lines.Add("-smap2048");
                File.WriteAllLines(_inst.CommandLineTxt, lines, Encoding.ASCII);
            }

            if (File.Exists(_inst.LauncherCfg))
            {
                var cfg = File.ReadAllLines(_inst.LauncherCfg).ToList();
                for (var i = 0; i < cfg.Count; i++)
                {
                    if (cfg[i].Trim().Equals("DBG", StringComparison.OrdinalIgnoreCase))
                        cfg[i] = "NODBG";
                }
                File.WriteAllLines(_inst.LauncherCfg, cfg, Encoding.ASCII);
            }
        }
        catch { /* non bloccare avvio */ }
    }

    /// <summary>Spegne EA backpack (DESYNC/HUD) in axr_options senza toccare il resto.</summary>
    public void ApplyCoopSafeOptions()
    {
        try
        {
            var path = _inst.AxrOptions;
            if (!File.Exists(path)) return;
            var text = File.ReadAllText(path);
            var next = Regex.Replace(text,
                @"(EA_settings/enable_backpack_addon\s*=\s*)true",
                "$1false",
                RegexOptions.IgnoreCase);
            next = Regex.Replace(next,
                @"(EA_settings/enable_crouch_toggle_backpack_addon\s*=\s*)true",
                "$1false",
                RegexOptions.IgnoreCase);
            if (!ReferenceEquals(next, text) && next != text)
                File.WriteAllText(path, next);
        }
        catch { /* non bloccare */ }
    }

    public bool IsOurGameRunning()
    {
        try
        {
            var exeName = Path.GetFileNameWithoutExtension(_inst.ResolveClientExe());
            foreach (var p in Process.GetProcessesByName(exeName))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path != null &&
                        path.StartsWith(_inst.Bin, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* accesso negato */ }
            }
        }
        catch { /* ignore */ }
        return false;
    }

    public void FocusOurGame()
    {
        try
        {
            var exeName = Path.GetFileNameWithoutExtension(_inst.ResolveClientExe());
            foreach (var p in Process.GetProcessesByName(exeName))
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (path == null ||
                        !path.StartsWith(_inst.Bin, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (p.MainWindowHandle != IntPtr.Zero)
                        Native.SetForegroundWindow(p.MainWindowHandle);
                }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
    }

    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
