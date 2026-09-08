using System.IO;

namespace MmxNetShared;

/// <summary>
/// File MMX-Net da rimuovere in uninstall. Solo path espliciti — mai AnomalyLauncher,
/// db/, bin engine vanilla (salvo DLL/script overlay elencati sotto).
/// </summary>
public static class ModUninstall
{
    /// <summary>Path relativi alla root install (file solo).</summary>
    public static readonly string[] RelativePaths =
    {
        // Launcher / installer / uninstaller MMX
        "MMX-Net-Launcher.exe",
        "MMX-Net-Launcher-dev.exe",
        "MMX-Net-Installer.exe",
        "MMX-Net-Uninstaller.exe",
        "MMX-Net-Launcher.pdb",
        "MMX-Net-Launcher-dev.pdb",
        "MMX-Net-Installer.pdb",
        "MMX-Net-Uninstaller.pdb",

        // Legacy branding (stesso set di LegacyCleanup)
        "AnomalyCoop-Launcher.exe",
        "AnomalyCoop-Launcher-dev.exe",
        "AnomalyCoop-Installer.exe",
        "AnomalyCoop-Launcher.pdb",
        "AnomalyCoop-Launcher-dev.pdb",
        "AnomalyCoop-Installer.pdb",
        "AnomalyCoop-Launcher.exe.pdb",
        "AnomalyCoop-Launcher-dev.exe.pdb",
        "AnomalyCoop-Installer.exe.pdb",
        "AnomalyCoop-Download.zip",
        "LEGGIMI_AnomalyCoop.txt",

        // Config / version / bridge Steam MMX
        "ac_version.json",
        "ac_config.json",
        "ac_config.user.json",
        "ac_steam_cop_bridge.cmd",
        "ac_steam_launch.args",
        "ac_steam_redirect.off",
        "steam_appid.txt",
        "LEGGIMI_MmxNet.txt",
        "LEGGIMI_MMX-Net.txt",

        // Pack root (protocollo overlay)
        "xrRazom-release.txt",

        // Overlay gamedata — pack include + default PackService
        "gamedata/configs/axr_options.ltx",
        "gamedata/configs/text/eng/ui_xrrazom.xml",
        "gamedata/configs/text/rus/ui_xrrazom.xml",
        "gamedata/configs/text/ukr/ui_xrrazom.xml",
        "gamedata/configs/ui/ui_mm_xrr_peer_faction.xml",
        "gamedata/scripts/xrr_core.script",
        "gamedata/scripts/xrr_net_client.script",
        "gamedata/scripts/xrr_net_server.script",
        "gamedata/scripts/xrr_callbacks.script",
        "gamedata/scripts/xrr_menu.script",
        "gamedata/scripts/xrr_ui_peer_faction.script",

        // DLL net overlay (pack optional repair) — non tocca AnomalyLauncher / db
        "bin/steam_api64.dll",
        "bin/GameNetworkingSockets.dll",
    };

    /// <summary>
    /// Cancella i file MMX-Net presenti. Ritorna i path relativi rimossi.
    /// </summary>
    public static IReadOnlyList<string> RemoveFromInstallRoot(string installRoot)
    {
        var removed = new List<string>();
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            return removed;

        var root = Path.GetFullPath(installRoot);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var rel in RelativePaths)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !full.Equals(root, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!File.Exists(full)) continue;
                File.Delete(full);
                removed.Add(rel.Replace('/', '\\'));

                // Pending overlay (.new) se presente
                var pending = full + FileOverlay.PendingSuffix;
                if (File.Exists(pending))
                {
                    File.Delete(pending);
                    removed.Add((rel + FileOverlay.PendingSuffix).Replace('/', '\\'));
                }
            }
            catch
            {
                /* file bloccato / ACL */
            }
        }

        return removed;
    }
}
