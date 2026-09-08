using System.IO;
using System.IO.Compression;
using System.Windows;
using Microsoft.Win32;
using MmxNetShared;

namespace MmxNetInstaller;

public partial class MainWindow : Window
{
    private const string OverlayZipName = "ac-overlay.zip";
    /// <summary>Nome cartella di installazione fisso (coincide con DefaultRoot del launcher).</summary>
    private const string InstallFolderName = "Anomaly Coop";

    public MainWindow()
    {
        InitializeComponent();
        PathBox.Text = DefaultInstallPath();
        StatusText.Text = "Pronto. Installa nella cartella «Anomaly Coop» (base Anomaly già presente lì).";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Scegli la root o la cartella «Anomaly Coop»",
            Multiselect = false,
        };
        try
        {
            var cur = PathBox.Text.Trim();
            if (Directory.Exists(cur)) dlg.InitialDirectory = cur;
            else if (Directory.Exists(Path.GetDirectoryName(cur)))
                dlg.InitialDirectory = Path.GetDirectoryName(cur)!;
        }
        catch { /* ignore */ }

        if (dlg.ShowDialog() == true)
        {
            var chosen = dlg.FolderName;
            var resolved = EnsureAnomalyCoopFolder(chosen);
            if (!string.Equals(
                    Path.GetFileName(chosen.TrimEnd('\\', '/')),
                    InstallFolderName,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(chosen.TrimEnd('\\', '/'), resolved, StringComparison.OrdinalIgnoreCase))
            {
                var use = MessageBox.Show(
                    "La cartella selezionata non si chiama «" + InstallFolderName + "».\n\n" +
                    "Percorso proposto:\n" + resolved + "\n\n" +
                    "Usare questa cartella? (No = annulla)",
                    "MMX-Net",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (use != MessageBoxResult.Yes)
                    return;
            }
            PathBox.Text = resolved;
        }
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Cartella Anomaly coop da copiare",
            Multiselect = false,
        };
        if (dlg.ShowDialog() == true)
            SourceBox.Text = dlg.FolderName;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InstallBtn.IsEnabled = false;
            BrowseBtn.IsEnabled = false;

            var dest = EnsureAnomalyCoopFolder(PathBox.Text.Trim());
            PathBox.Text = dest;

            if (string.IsNullOrWhiteSpace(dest))
                throw new InvalidOperationException("Indica un percorso di installazione.");

            if (!IsAnomalyCoopFolder(dest) &&
                MessageBox.Show(
                    "Il percorso non termina con «" + InstallFolderName + "».\n\n" +
                    "Consigliato: installa in una cartella chiamata «" + InstallFolderName + "»\n" +
                    "(base Anomaly già presente lì).\n\nContinuare comunque?",
                    "MMX-Net",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            Directory.CreateDirectory(dest);
            StatusText.Text = "Preparazione…";

            if (CopyFromBox.IsChecked == true)
            {
                var src = SourceBox.Text.Trim();
                if (!Directory.Exists(src))
                    throw new DirectoryNotFoundException("Sorgente Anomaly non trovata: " + src);
                if (!File.Exists(Path.Combine(src, "fsgame.ltx")) || !Directory.Exists(Path.Combine(src, "bin")))
                    throw new InvalidOperationException("La sorgente non sembra un'install Anomaly (manca fsgame.ltx o bin\\).");

                StatusText.Text = "Copia Anomaly base (può richiedere diversi minuti)…";
                await Task.Run(() => CopyTree(src, dest, skipNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "appdata", "BACKUP-DI APPDATA", "toolkit", "tools", "MT"
                })).ConfigureAwait(true);
            }

            var zip = FindOverlayZip();
            if (zip == null)
                throw new FileNotFoundException(
                    "Pacchetto overlay non trovato. Metti «ac-overlay.zip» accanto all'installer " +
                    "(o in sottocartella pack\\).");

            StatusText.Text = "Applicazione overlay MMX-Net…";
            var pendingOverlay = await Task.Run(() => ApplyZip(zip, dest)).ConfigureAwait(true);

            StatusText.Text = "Pulizia file legacy AnomalyCoop…";
            var removed = await Task.Run(() => LegacyCleanup.RemoveFromInstallRoot(dest)).ConfigureAwait(true);
            foreach (var name in removed)
                StatusText.Text = "Rimosso legacy: " + name;
            if (removed.Count > 0)
                StatusText.Text = $"Rimossi {removed.Count} file legacy AnomalyCoop.";

            WriteReadme(dest);
            EnsureConfig(dest);
            CopyUninstaller(dest);
            TryCreateUninstallShortcut(dest);

            var incomplete = GetIncompleteBaseWarnings(dest);
            if (incomplete.Count > 0)
            {
                StatusText.Text = "Installazione overlay OK — ATTENZIONE: base Anomaly coop incompleta.";
                MessageBox.Show(
                    "Overlay installato in:\n" + dest +
                    "\n\nATTENZIONE — install incompleta (mancano pezzi della base):\n• " +
                    string.Join("\n• ", incomplete) +
                    "\n\nServe Anomaly 1.5.3 coop completa nella stessa cartella «Anomaly Coop» " +
                    "(bin + gamedata + file di release protocollo).\n" +
                    "Rilancia l'Installer con «Copia da install esistente» oppure copia quelle cartelle, " +
                    "poi HEALTH nel launcher non deve avere FATAL.",
                    "MMX-Net — base mancante",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            StatusText.Text = incomplete.Count > 0
                ? "Overlay OK — completa la base Anomaly coop prima di giocare."
                : "Installazione completata.";
            var legacyNote = removed.Count > 0
                ? "\n\nPulizia legacy:\n• " + string.Join("\n• ", removed)
                : "";
            var pendingNote = pendingOverlay.Count > 0
                ? "\n\nFile in uso (scritti come .new — chiudi il launcher e riaprilo per applicare):\n• " +
                  string.Join("\n• ", pendingOverlay)
                : "";
            var launch = MessageBox.Show(
                "Overlay MMX-Net installato in:\n" + dest +
                (incomplete.Count > 0
                    ? "\n\n(Base ancora incompleta — HEALTH segnalerà FATAL finché non copi la base Anomaly coop.)"
                    : "") +
                legacyNote +
                pendingNote +
                "\n\nAprire il launcher ora?",
                "MMX-Net",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (launch == MessageBoxResult.Yes)
            {
                var exe = Path.Combine(dest, "MMX-Net-Launcher.exe");
                if (File.Exists(exe) || File.Exists(exe + FileOverlay.PendingSuffix))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Errore: " + ex.Message;
            MessageBox.Show(ex.Message, "Installer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            InstallBtn.IsEnabled = true;
            BrowseBtn.IsEnabled = true;
        }
    }

    private static string DefaultInstallPath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs))
            return Path.Combine(docs, InstallFolderName);
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        return Path.Combine(drive, "Games", InstallFolderName);
    }

    private static bool IsAnomalyCoopFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return Path.GetFileName(path.TrimEnd('\\', '/'))
            .Equals(InstallFolderName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Forza il nome cartella «Anomaly Coop»: se manca, usa la sottocartella o rinomina da MMX-Net.
    /// </summary>
    private static string EnsureAnomalyCoopFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        path = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(path);
        if (name.Equals(InstallFolderName, StringComparison.OrdinalIgnoreCase))
            return path;
        // Migrazione path legacy MMX-Net → stessa parent\Anomaly Coop
        if (name.Equals("MMX-Net", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(path);
            return string.IsNullOrEmpty(parent)
                ? InstallFolderName
                : Path.Combine(parent, InstallFolderName);
        }
        return Path.Combine(path, InstallFolderName);
    }

    private static string? FindOverlayZip()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, OverlayZipName),
            Path.Combine(exeDir, "pack", OverlayZipName),
            Path.Combine(AppContext.BaseDirectory, OverlayZipName),
            Path.Combine(AppContext.BaseDirectory, "pack", OverlayZipName),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static List<string> ApplyZip(string zipPath, string dest)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ac_inst_" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(zipPath, tmp);
        try
        {
            return CopyTree(tmp, dest, skipNames: null);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    private static List<string> CopyTree(string src, string dst, HashSet<string>? skipNames)
    {
        src = Path.GetFullPath(src);
        dst = Path.GetFullPath(dst);
        Directory.CreateDirectory(dst);

        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            var top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (skipNames != null && skipNames.Contains(top)) continue;
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }

        var pending = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (skipNames != null && skipNames.Contains(top)) continue;
            var target = Path.Combine(dst, rel);
            if (FileOverlay.CopyOverwriteOrPending(file, target, out _))
                pending.Add(rel.Replace('/', '\\'));
        }

        return pending;
    }

    private static void WriteReadme(string dest)
    {
        var path = Path.Combine(dest, "LEGGIMI_MmxNet.txt");
        var text =
            """
            MMX-Net — installazione
            ============================

            Cartella dedicata
            -----------------
            Installa nella cartella "Anomaly Coop" (base Anomaly già presente lì).
            Non installare l'overlay sopra Anomaly vanilla che usi in singleplayer:
            fingerprint e pack devono essere uguali tra host e amici.

            Requisiti
            ---------
            - Anomaly 1.5.3 coop nella stessa cartella (bin, db, gamedata, fsgame.ltx, file di release protocollo)
            - Steam + Call of Pripyat (App ID 41700) per Shift+Tab / inviti
            - Stessa versione pack (ac_version.json) su tutti i PC
            - Il launcher usa la cartella dell'exe (qualsiasi disco), non un path fisso

            Se HEALTH segnala FATAL su steam_api64 / GameNetworkingSockets / script coop /
            file di release protocollo: l'install e' incompleta (solo overlay). Usa
            «Copia da install esistente» nell'Installer oppure copia quelle cartelle.

            Avvio
            -----
            1. Apri Steam (obbligatorio).
            2. Avvia MMX-Net-Launcher.exe
            3. HEALTH deve essere senza FATAL
            4. HOST (chi ospita) o PLAY (chi entra da Join Game)
               — la prima volta Steam può riavviarsi per scrivere le Launch Options
                 di Call of Pripyat (bridge → Anomaly). Accetta e riprova HOST/PLAY.
            5. In Steam devi risultare su «Call of Pripyat», non su uno shortcut Non-Steam.
            6. Inviti: Shift+Tab → Friends → Invite / Join Game (niente Connect IP).

            Nota Steam / CoP vanilla
            ------------------------
            Le Launch Options di Call of Pripyat (41700) vengono impostate sul bridge
            Anomaly così Join Game non apre stcop vanilla. Se ti serve anche CoP originale:
            crea il file ac_steam_redirect.off nella cartella Anomaly Coop e svuota le
            Launch Options di CoP in Steam → Proprietà.

            Aggiornamenti
            -------------
            Nel launcher: icona ⬇ o pulsante AGGIORNA.
            Serve updateFeedUrl in ac_config.json (raw GitHub MMX-NET-feed, pubblico).
            Nessun PAT: il codice resta su MMX-NET privata; gli update sono sulla repo feed.
            Vedi FEED_PRIVATO_AMICI.md. All'avvio compare il popup se c'e' una versione nuova.

            Disinstallazione
            ----------------
            Esegui MMX-Net-Uninstaller.exe nella cartella Anomaly Coop
            (o scorciatoia Desktop «MMX-Net Disinstalla»). Rimuove solo i file
            MMX-Net; non tocca AnomalyLauncher / db / bin vanilla.

            """;
        File.WriteAllText(path, text);
    }

    private static void CopyUninstaller(string dest)
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var name = "MMX-Net-Uninstaller.exe";
        var candidates = new[]
        {
            Path.Combine(exeDir, name),
            Path.Combine(AppContext.BaseDirectory, name),
        };
        var src = candidates.FirstOrDefault(File.Exists);
        if (src == null) return;
        var dst = Path.Combine(dest, name);
        try
        {
            File.Copy(src, dst, true);
        }
        catch
        {
            FileOverlay.CopyOverwriteOrPending(src, dst, out _);
        }
    }

    private static void TryCreateUninstallShortcut(string dest)
    {
        try
        {
            var uninstaller = Path.Combine(dest, "MMX-Net-Uninstaller.exe");
            if (!File.Exists(uninstaller)) return;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) return;
            var linkPath = Path.Combine(desktop, "MMX-Net Disinstalla.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            var shell = Activator.CreateInstance(shellType);
            if (shell == null) return;
            var shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { linkPath });
            if (shortcut == null) return;
            var st = shortcut.GetType();
            st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { uninstaller });
            st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { dest });
            st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut,
                new object[] { "Disinstalla overlay MMX-Net (non tocca Anomaly base)" });
            st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        }
        catch { /* scorciatoia opzionale */ }
    }

    private static void EnsureConfig(string dest)
    {
        var cfg = Path.Combine(dest, "ac_config.json");
        if (File.Exists(cfg)) return;

        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var template = Path.Combine(exeDir, "ac_config.user.json");
        if (File.Exists(template))
        {
            File.Copy(template, cfg, false);
            return;
        }

        File.WriteAllText(cfg,
            """
            {
              "useSteam": true,
              "friendsProfile": true,
              "autoUpdateGame": false,
              "preferDirectSteam": false,
              "checkUpdatesOnStart": true,
              "updateChannel": "dev",
              "updateFeedUrl": ""
            }
            """);
    }

    private static List<string> GetIncompleteBaseWarnings(string dest)
    {
        var missing = new List<string>();
        void Need(string rel)
        {
            if (!File.Exists(Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar))))
                missing.Add(rel.Replace('/', '\\'));
        }

        Need("fsgame.ltx");
        Need("xrRazom-release.txt");
        Need("bin/steam_api64.dll");
        Need("bin/GameNetworkingSockets.dll");
        Need("gamedata/scripts/xrr_core.script");
        Need("gamedata/scripts/xrr_net_client.script");
        Need("gamedata/scripts/xrr_net_server.script");
        Need("gamedata/scripts/xrr_callbacks.script");

        var hasClient = Directory.Exists(Path.Combine(dest, "bin")) &&
                        Directory.EnumerateFiles(Path.Combine(dest, "bin"), "anomaly*.exe").Any();
        if (!hasClient)
            missing.Add("bin\\anomaly*.exe (client Anomaly)");

        return missing;
    }
}
