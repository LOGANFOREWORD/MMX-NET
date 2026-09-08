using System.IO;

namespace MmxNetShared;

/// <summary>
/// Rimuove exe/artifact del branding AnomalyCoop sostituiti da MMX-Net.
/// Solo path espliciti in root install — mai AnomalyLauncher.exe, bin, db, save, appdata.
/// </summary>
public static class LegacyCleanup
{
    /// <summary>Path relativi alla root install (file solo, non cartelle).</summary>
    public static readonly string[] RelativePaths =
    {
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
    };

    /// <summary>
    /// Cancella i file legacy presenti. Ritorna i nomi relativi rimossi (per log).
    /// </summary>
    public static IReadOnlyList<string> RemoveFromInstallRoot(string installRoot)
    {
        var removed = new List<string>();
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            return removed;

        var root = Path.GetFullPath(installRoot);
        foreach (var rel in RelativePaths)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                // Solo file sotto root (no traversal)
                var full = Path.GetFullPath(path);
                if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) &&
                    !full.Equals(root, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!File.Exists(full)) continue;
                File.Delete(full);
                removed.Add(rel.Replace('/', '\\'));
            }
            catch
            {
                /* file bloccato / ACL — non bloccare update/install */
            }
        }

        return removed;
    }
}
