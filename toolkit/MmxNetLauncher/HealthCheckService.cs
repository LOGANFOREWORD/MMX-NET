using System.IO;
using System.Text.RegularExpressions;

namespace MmxNetLauncher;

public enum CheckSev { Fatal, Problem, Desync, Info }

public sealed record CheckItem(int N, CheckSev Sev, bool Pass, string Msg);

public sealed class HealthReport
{
    public List<CheckItem> Items { get; } = new();
    public int FatalFails => Items.Count(i => !i.Pass && i.Sev == CheckSev.Fatal);
    public int ProblemFails => Items.Count(i => !i.Pass && i.Sev == CheckSev.Problem);
    public int DesyncFails => Items.Count(i => !i.Pass && i.Sev == CheckSev.Desync);
    public bool OkToLaunch => FatalFails == 0;
    public string SummaryLine =>
        $"PASS {Items.Count(i => i.Pass)}/{Items.Count} · FATAL {FatalFails} · PROBLEM {ProblemFails} · DESYNC {DesyncFails}";
}

public static class HealthCheckService
{
    private static readonly string[] RazomScripts =
    {
        @"scripts\xrr_core.script",
        @"scripts\xrr_net_client.script",
        @"scripts\xrr_net_server.script",
        @"scripts\xrr_callbacks.script",
    };

    private static readonly string[] NetStack =
    {
        "steam_api64.dll",
        "GameNetworkingSockets.dll",
    };

    public const string IncompleteBaseHint =
        "Serve base Anomaly 1.5.3 + xrRazom nella stessa cartella MMX-Net " +
        "(bin + gamedata + xrRazom-release.txt). Usa Installer con "Copia da Anomaly+xrRazom" +
        "oppure copia quelle cartelle. L'overlay da solo non basta.";

    // MMX-Net: messaggio chiaro su fingerprint desync (join rifiutato)
    public const string FingerprintHint =
        "Fingerprint config/spawn diverso tra PC = join rifiutato. Tutti devono avere " +
        "stesso pack MMX-Net (stessa versione overlay) e stessa base Anomaly+xrRazom.";

    public static HealthReport Run(Install inst)
    {
        var r = new HealthReport();
        var n = 0;
        void Add(CheckSev sev, bool pass, string msg) =>
            r.Items.Add(new CheckItem(++n, sev, pass, msg));

        Add(CheckSev.Info, true, "Root: " + inst.Root);

        var exe = inst.ResolveClientExe();
        Add(CheckSev.Fatal, File.Exists(exe), "client: " + Path.GetFileName(exe));
        Add(CheckSev.Fatal, File.Exists(inst.Fsgame), "fsgame.ltx");
        Add(CheckSev.Fatal, Directory.Exists(inst.Bin), "cartella bin\\");
        Add(CheckSev.Fatal, Directory.Exists(inst.Gamedata), "cartella gamedata\\");

        var binId = File.Exists(inst.SteamAppIdFile) ? SafeRead(inst.SteamAppIdFile) : "";
        var rootIdFile = Path.Combine(inst.Root, "steam_appid.txt");
        var rootId = File.Exists(rootIdFile) ? SafeRead(rootIdFile) : "";
        var steamIdOk = binId == SteamService.AppId;
        Add(CheckSev.Fatal, steamIdOk,
            steamIdOk
                ? $"Steam = Call of Pripyat (App ID {SteamService.AppId})"
                : $"steam_appid.txt in bin = '{binId}' (serve {SteamService.AppId} per inviti)");
        Add(CheckSev.Info, rootId == SteamService.AppId || string.IsNullOrEmpty(rootId),
            rootId == SteamService.AppId
                ? "steam_appid anche in root (cwd)"
                : "steam_appid root assente — lo scrive il PLAY");

        var missingNetOrRazom = false;
        foreach (var dll in NetStack)
        {
            var p = Path.Combine(inst.Bin, dll);
            var ok = File.Exists(p);
            if (!ok) missingNetOrRazom = true;
            Add(CheckSev.Fatal, ok,
                ok ? "bin\\" + dll : "bin\\" + dll + " MANCANTE — base Anomaly/xrRazom incompleta");
        }

        foreach (var rel in RazomScripts)
        {
            var p = Path.Combine(inst.Gamedata, rel);
            var ok = File.Exists(p);
            if (!ok) missingNetOrRazom = true;
            Add(CheckSev.Fatal, ok,
                ok ? "xrRazom: " + rel : "xrRazom MANCANTE: " + rel);
        }

        var protoOk = false;
        var protoMsg = "xrRazom-release.txt assente";
        if (File.Exists(inst.RazomRelease))
        {
            var txt = File.ReadAllText(inst.RazomRelease);
            protoOk = Regex.IsMatch(txt, @"protocol\s+89", RegexOptions.IgnoreCase);
            protoMsg = protoOk ? "protocol 89 (xrRazom 1.3)" : "protocol NON 89 — amici sullo stesso bundle";
        }
        else missingNetOrRazom = true;
        Add(CheckSev.Fatal, File.Exists(inst.RazomRelease) && protoOk, protoMsg);

        if (missingNetOrRazom)
            Add(CheckSev.Fatal, false, "Install incompleta: " + IncompleteBaseHint);

        var ver = inst.ReadVersion();
        Add(CheckSev.Info, File.Exists(inst.VersionJson),
            $"pack {ver.Name} {ver.Version} ({ver.Channel})");

        var fpHit = RecentLogHasFingerprintIssue(inst);
        Add(CheckSev.Desync, !fpHit,
            fpHit
                ? "log: fingerprint/config mismatch — " + FingerprintHint
                : "nessun fingerprint mismatch recente nei log");

        Add(CheckSev.Info, true,
            Directory.Exists(Path.Combine(inst.Root, "MT"))
                ? "cartella MT presente — tutti i PC stesso engine (ST o MT)"
                : "engine ST (niente MT overlay)");

        var dbgCmd = false;
        try
        {
            if (File.Exists(inst.CommandLineTxt))
                dbgCmd = File.ReadAllText(inst.CommandLineTxt)
                    .Contains("-dbg", StringComparison.OrdinalIgnoreCase);
        }
        catch { /* ignore */ }
        Add(CheckSev.Info, !dbgCmd,
            dbgCmd ? "commandline ha -dbg (profilo amici lo toglie all'avvio)" : "commandline senza -dbg");

        var dbgCfg = false;
        try
        {
            if (File.Exists(inst.LauncherCfg))
                dbgCfg = File.ReadAllLines(inst.LauncherCfg)
                    .Any(l => l.Trim().Equals("DBG", StringComparison.OrdinalIgnoreCase));
        }
        catch { /* ignore */ }
        Add(CheckSev.Info, !dbgCfg,
            dbgCfg ? "AnomalyLauncher.cfg = DBG (profilo amici → NODBG)" : "AnomalyLauncher.cfg senza DBG");

        var eaBackpack = false;
        try
        {
            if (File.Exists(inst.AxrOptions))
            {
                var axr = File.ReadAllText(inst.AxrOptions);
                eaBackpack = Regex.IsMatch(axr,
                    @"EA_settings/enable_backpack_addon\s*=\s*true",
                    RegexOptions.IgnoreCase);
            }
        }
        catch { /* ignore */ }
        Add(CheckSev.Desync, !eaBackpack,
            eaBackpack
                ? "EA backpack ON — desync/HUD (il launcher lo spegne al PLAY)"
                : "EA backpack off (coop-safe)");

        if (Directory.Exists(Path.Combine(inst.Gamedata, "shaders", "r3")))
        {
            var heat = File.Exists(Path.Combine(inst.Gamedata, "shaders", "r3", "heatvision.ps"));
            Add(CheckSev.Info, true,
                heat
                    ? "shaders r3 heatvision presente"
                    : "shaders r3 parziali (stub in log) — non blocca Steam/join se tutti uguali");
        }

        Add(CheckSev.Problem, !RecentLogHasFatal(inst),
            RecentLogHasFatal(inst)
                ? "log recente con FATAL/assert — vedi appdata\\logs"
                : "nessun FATAL recente nei log");

        Add(CheckSev.Problem, SteamService.FindCallOfPripyatManifest() != null,
            SteamService.FindCallOfPripyatManifest() != null
                ? "Call of Pripyat (41700) in libreria Steam"
                : "CoP 41700 assente/non attivata — se Steam chiede codice riscatto, attiva/compra CoP; intanto HOST usa avvio diretto -steam");

        Add(CheckSev.Info, SteamService.IsSteamRunning(), SteamService.StatusText());

        try
        {
            var user = Path.Combine(inst.AppData, "user.ltx");
            if (File.Exists(user))
            {
                var ut = File.ReadAllText(user);
                var steamHost = Regex.IsMatch(ut, @"xrr_host_steam\s+on", RegexOptions.IgnoreCase);
                var sess = Regex.IsMatch(ut, @"xrr_host_session\s+on", RegexOptions.IgnoreCase);
                Add(CheckSev.Info, steamHost,
                    steamHost
                        ? "xrr_host_steam ON (P2P Steam / inviti overlay)"
                        : "xrr_host_steam OFF — HOST del launcher lo accende");
                Add(CheckSev.Info, true,
                    sess
                        ? "xrr_host_session ON (host auto in partita — usa HOST sul tuo PC)"
                        : "xrr_host_session OFF (ok per join; HOST del launcher lo accende)");
            }
        }
        catch { /* ignore */ }

        return r;
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch { return "?"; }
    }

    private static bool RecentLogHasFatal(Install inst) =>
        RecentLogMatches(inst,
            @"^FATAL ERROR\b|Assertion failed|Data verification failed|version mismatch|access violation");

    private static bool RecentLogHasFingerprintIssue(Install inst) =>
        RecentLogMatches(inst,
            @"fingerprint|config mismatch|spawn mismatch|Different version|version mismatch");

    private static bool RecentLogMatches(Install inst, string pattern)
    {
        try
        {
            if (!Directory.Exists(inst.Logs)) return false;
            var logs = Directory.GetFiles(inst.Logs, "xray_*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(2);
            foreach (var log in logs)
            {
                var fi = new FileInfo(log);
                using var fs = fi.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var start = Math.Max(0, fi.Length - 200_000);
                fs.Seek(start, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                var tail = sr.ReadToEnd();
                if (Regex.IsMatch(tail, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                    return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }
}
