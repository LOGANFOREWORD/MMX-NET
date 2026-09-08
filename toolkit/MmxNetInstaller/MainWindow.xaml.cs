using System.IO;
using System.IO.Compression;
using System.Windows;
using Microsoft.Win32;

namespace MmxNetInstaller;

public partial class MainWindow : Window
{
    private const string OverlayZipName = "ac-overlay.zip";

    public MainWindow()
    {
        InitializeComponent();
        var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        PathBox.Text = Path.Combine(drive, "Games", "MMX-Net");
        StatusText.Text = "Pronto. Scegli la cartella «MMX-Net» e installa l'overlay.";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Scegli (o crea) la cartella MMX-Net",
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
            PathBox.Text = EnsureMmxNetName(dlg.FolderName);
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

            var dest = EnsureMmxNetName(PathBox.Text.Trim());
            PathBox.Text = dest;

            if (string.IsNullOrWhiteSpace(dest))
                throw new InvalidOperationException("Indica un percorso di installazione.");

            if (!dest.EndsWith("MMX-Net", StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(
                    "Il percorso non termina con «MMX-Net».\n\n" +
                    "Consigliato: crea una cartella a parte chiamata MMX-Net.\n\nContinuare comunque?",
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
            await Task.Run(() => ApplyZip(zip, dest)).ConfigureAwait(true);

            WriteReadme(dest);
            EnsureConfig(dest);

            var incomplete = GetIncompleteBaseWarnings(dest);
            if (incomplete.Count > 0)
            {
                StatusText.Text = "Installazione overlay OK — ATTENZIONE: base Anomaly coop incompleta.";
                MessageBox.Show(
                    "Overlay installato in:\n" + dest +
                    "\n\nATTENZIONE — install incompleta (mancano pezzi della base):\n• " +
                    string.Join("\n• ", incomplete) +
                    "\n\nServe Anomaly 1.5.3 coop completa nella stessa cartella MMX-Net " +
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
            var launch = MessageBox.Show(
                "Overlay MMX-Net installato in:\n" + dest +
                (incomplete.Count > 0
                    ? "\n\n(Base ancora incompleta — HEALTH segnalerà FATAL finché non copi la base Anomaly coop.)"
                    : "") +
                "\n\nAprire il launcher ora?",
                "MMX-Net",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (launch == MessageBoxResult.Yes)
            {
                var exe = Path.Combine(dest, "MMX-Net-Launcher.exe");
                if (File.Exists(exe))
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

    private static string EnsureMmxNetName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        path = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(path);
        if (name.Equals("MMX-Net", StringComparison.OrdinalIgnoreCase))
            return path;
        if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, "fsgame.ltx")))
            return Path.Combine(path, "MMX-Net");
        return path;
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

    private static void ApplyZip(string zipPath, string dest)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ac_inst_" + Guid.NewGuid().ToString("N"));
        ZipFile.ExtractToDirectory(zipPath, tmp);
        try
        {
            CopyTree(tmp, dest, skipNames: null);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* ignore */ }
        }
    }

    private static void CopyTree(string src, string dst, HashSet<string>? skipNames)
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

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (skipNames != null && skipNames.Contains(top)) continue;
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
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
            Usa una cartella a parte chiamata "MMX-Net".
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
            1. Avvia MMX-Net-Launcher.exe
            2. HEALTH deve essere senza FATAL
            3. HOST (chi ospita) o PLAY (chi entra da Join Game)

            Aggiornamenti
            -------------
            Nel launcher: icona ⬇ o pulsante AGGIORNA.
            Serve updateFeedUrl in ac_config.json (URL pubblico della cartella
            dist/update/ di Logan: GitHub raw, itch, server statico).
            Se il feed e' configurato, all'avvio compare il popup e puoi scaricare.

            """;
        File.WriteAllText(path, text);
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
