using System.IO;
using System.Windows;
using Microsoft.Win32;
using MmxNetShared;

namespace MmxNetUninstaller;

public partial class MainWindow : Window
{
    private const string InstallFolderName = "Anomaly Coop";

    public MainWindow()
    {
        InitializeComponent();
        PathBox.Text = GuessInstallPath();
        StatusText.Text = "Seleziona la cartella Anomaly Coop e conferma la disinstallazione.";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Cartella «Anomaly Coop» da ripulire",
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
            PathBox.Text = dlg.FolderName;
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UninstallBtn.IsEnabled = false;
            BrowseBtn.IsEnabled = false;

            var dest = PathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(dest) || !Directory.Exists(dest))
                throw new DirectoryNotFoundException("Cartella non trovata: " + dest);

            var confirm = MessageBox.Show(
                "Rimuovere SOLO i file della mod MMX-Net da:\n" + dest +
                "\n\nVerranno cancellati launcher/bridge/config/script xrr e overlay pack.\n" +
                "NON verranno cancellati AnomalyLauncher, db/ né i bin engine vanilla.\n\n" +
                "Continuare?",
                "MMX-Net — conferma disinstallazione",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
            {
                StatusText.Text = "Annullato.";
                return;
            }

            StatusText.Text = "Rimozione file MMX-Net…";
            var removed = await Task.Run(() => ModUninstall.RemoveFromInstallRoot(dest)).ConfigureAwait(true);

            TryRemoveDesktopShortcut();

            if (removed.Count == 0)
            {
                StatusText.Text = "Nessun file MMX-Net trovato (già pulito?).";
                MessageBox.Show(
                    "Nessun file MMX-Net trovato in:\n" + dest,
                    "MMX-Net",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            StatusText.Text = $"Rimossi {removed.Count} file.";
            MessageBox.Show(
                $"Rimossi {removed.Count} file MMX-Net da:\n{dest}\n\n• " +
                string.Join("\n• ", removed.Take(40)) +
                (removed.Count > 40 ? $"\n• … (+{removed.Count - 40})" : "") +
                "\n\nAnomaly base (AnomalyLauncher / db / bin vanilla) non è stata toccata.",
                "MMX-Net — disinstallazione completata",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Se l'uninstaller vive nella cartella install, esci (file già cancellato o in uso)
            var selfDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            if (string.Equals(Path.GetFullPath(selfDir).TrimEnd('\\'),
                    Path.GetFullPath(dest).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Errore: " + ex.Message;
            MessageBox.Show(ex.Message, "Uninstaller", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UninstallBtn.IsEnabled = true;
            BrowseBtn.IsEnabled = true;
        }
    }

    private static string GuessInstallPath()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "AnomalyLauncher.exe")) ||
            File.Exists(Path.Combine(exeDir, "MMX-Net-Launcher.exe")) ||
            File.Exists(Path.Combine(exeDir, "fsgame.ltx")))
            return exeDir;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs))
            return Path.Combine(docs, InstallFolderName);
        return exeDir;
    }

    private static void TryRemoveDesktopShortcut()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var link = Path.Combine(desktop, "MMX-Net Uninstall.lnk");
            if (File.Exists(link)) File.Delete(link);
            link = Path.Combine(desktop, "MMX-Net Disinstalla.lnk");
            if (File.Exists(link)) File.Delete(link);
        }
        catch { /* ignore */ }
    }
}
