using System.Diagnostics;
using System.IO;

namespace MmxNetShared;

/// <summary>
/// Copia overlay con overwrite; se destinazione bloccata scrive <c>.new</c>
/// e permette self-replace del launcher al riavvio.
/// </summary>
public static class FileOverlay
{
    public const string PendingSuffix = ".new";

    /// <summary>
    /// Copia con overwrite. Se il file di destinazione è locked, scrive
    /// <paramref name="dst"/> + <c>.new</c> e ritorna true (pending).
    /// </summary>
    public static bool CopyOverwriteOrPending(string src, string dst, out string? pendingPath)
    {
        pendingPath = null;
        var dir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        try
        {
            File.Copy(src, dst, overwrite: true);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            pendingPath = dst + PendingSuffix;
            File.Copy(src, pendingPath, overwrite: true);
            return true;
        }
    }

    /// <summary>
    /// Applica file <c>*.new</c> sotto root (escluso l'exe del processo corrente, ancora locked).
    /// </summary>
    public static IReadOnlyList<string> TryApplyPendingUnder(string root, string? skipExactPath = null)
    {
        var applied = new List<string>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return applied;

        root = Path.GetFullPath(root);
        skipExactPath = string.IsNullOrWhiteSpace(skipExactPath)
            ? null
            : Path.GetFullPath(skipExactPath);

        foreach (var pending in Directory.EnumerateFiles(root, "*" + PendingSuffix, SearchOption.AllDirectories))
        {
            var target = pending[..^PendingSuffix.Length];
            if (skipExactPath != null &&
                target.Equals(skipExactPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Copy(pending, target, overwrite: true);
                File.Delete(pending);
                applied.Add(Path.GetRelativePath(root, target));
            }
            catch
            {
                /* ancora locked — riprova al prossimo avvio / self-replace */
            }
        }

        return applied;
    }

    /// <summary>
    /// Se esiste <c>exe.new</c>, avvia uno script che dopo l'uscita del PID
    /// sostituisce l'exe e riavvia. Altrimenti avvio normale. Ritorna true se ha schedulato replace.
    /// </summary>
    public static bool RestartWithPendingReplace(string? exePath, int currentPid)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return false;

        var pending = exePath + PendingSuffix;
        if (!File.Exists(pending))
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            return false;
        }

        var dir = Path.GetDirectoryName(exePath)!;
        var bat = Path.Combine(dir, "ac_self_replace.cmd");
        var exeName = Path.GetFileName(exePath);
        var pendingName = Path.GetFileName(pending);
        // Attende uscita PID, move /Y .new → exe, riavvia, pulisce script.
        var script =
            $"""
            @echo off
            setlocal
            :wait
            timeout /t 1 /nobreak >nul
            tasklist /FI "PID eq {currentPid}" 2>nul | find "{currentPid}" >nul && goto wait
            :retry
            move /Y "{pendingName}" "{exeName}" >nul 2>&1
            if exist "{pendingName}" (
              timeout /t 1 /nobreak >nul
              goto retry
            )
            start "" "{exeName}"
            del "%~f0"
            """;
        File.WriteAllText(bat, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = bat,
            WorkingDirectory = dir,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        });
        return true;
    }
}
