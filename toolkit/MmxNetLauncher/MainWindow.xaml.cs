using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MmxNetLauncher;

public partial class MainWindow : Window
{
    private readonly Install _inst;
    private readonly GameService _game;
    private bool _busy;
    private bool _updateAvailable;
    private UpdateManifest? _pendingRemote;
#if DEVKIT
    private DevPublishConfig _pubCfg = new();
#endif

    public MainWindow()
    {
        InitializeComponent();
        _inst = Install.Discover();
        _game = new GameService(_inst);

        UseSteamBox.IsChecked = _inst.GetConfigBool("useSteam", true);
        UseSteamBox.Checked += (_, _) => _inst.SetConfigBool("useSteam", true);
        UseSteamBox.Unchecked += (_, _) => _inst.SetConfigBool("useSteam", false);

        try
        {
            _game.PrepareSteamAsCallOfPripyat(UseSteamBox.IsChecked == true);
            _game.ApplyFriendsProfile();
            _game.ApplyCoopSafeOptions();
        }
        catch { /* ignore */ }

#if DEVKIT
        InitDevkitUi();
#endif

        RefreshChrome();
        RunHealthUi();
        Log($"Install: {_inst.Root}");
        Log(_inst.IsValid ? "Install valida." : "ATTENZIONE: install non valida.");
#if DEVKIT
        Log("Build DEVKIT — pannello Pubblica aggiornamento attivo.");
#else
        Log("Build utente — solo check/download aggiornamenti + HOST/PLAY.");
#endif
        Log("IMPORTANTE: HOST/PLAY avvia Call of Pripyat (41700) via Steam + bridge → Anomaly.");
        Log("Prima volta: Steam può riavviarsi per scrivere le Launch Options CoP.");
        Log("In partita: tip hosting → Shift+Tab → Friends → Invite. Amico: Join Game (non Connect IP).");
        Log("Rischio: Launch Options CoP puntano ad Anomaly — per vanilla crea ac_steam_redirect.off e svuota le opzioni.");

        Loaded += async (_, _) =>
        {
            if (_inst.GetConfigBool("checkUpdatesOnStart", true))
                await CheckUpdatesAsync(promptIfAvailable: true, silentIfCurrent: true);
        };
    }

#if DEVKIT
    private void InitDevkitUi()
    {
        DevPanel.Visibility = Visibility.Visible;
        Title = Title.Replace("MMX-Net", "MMX-Net DEVKIT");
        _pubCfg = PublishService.Load(_inst);
        var v = _inst.ReadVersion();
        DevVersionBox.Text = v.Version;
        DevChannelBox.Text = v.Channel;
        DevNotesBox.Text = v.Notes;
        DevFeedUrlBox.Text = _pubCfg.FeedBaseUrl;
        DevSyncBox.Text = _pubCfg.PublishTarget;
        DevAutoBumpBox.IsChecked = _pubCfg.AutoBumpPatch;
        RefreshDevChangesUi();
    }

    private void DevRefresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshDevChangesUi();
            Log("Refresh modifiche pack: " + DevPublishStatus.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Refresh", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshDevChangesUi()
    {
        var detect = DevChangeDetectService.Detect(_inst);
        DevPublishStatus.Text = detect.SummaryLine;

        if (detect.HasChanges)
        {
            DevChangesBadge.Visibility = Visibility.Visible;
            DevChangesText.Text = detect.HasSnapshot
                ? "Ci sono modifiche non pubblicate"
                : "Primo publish — pack pronto da caricare";
            var lines = detect.Changes.Take(8)
                .Select(c => "• " + c.Kind + ": " + c.RelPath);
            var more = detect.Changes.Count > 8 ? "\n… +" + (detect.Changes.Count - 8) + " altri" : "";
            DevChangesList.Text = string.Join("\n", lines) + more;
            DevChangesList.Visibility = Visibility.Visible;
            DevPublishBtn.Opacity = 1.0;
            DevPublishBtn.FontWeight = FontWeights.Bold;

            if (detect.HasSnapshot && DevAutoBumpBox.IsChecked == true)
            {
                var localVer = _inst.ReadVersion().Version;
                var current = DevVersionBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(current) ||
                    UpdateService.CompareVersions(current, localVer) <= 0)
                    DevVersionBox.Text = DevChangeDetectService.SuggestPatchBump(localVer);
            }
        }
        else
        {
            DevChangesBadge.Visibility = Visibility.Collapsed;
            DevChangesList.Visibility = Visibility.Collapsed;
            DevPublishBtn.Opacity = 0.92;
            DevPublishBtn.FontWeight = FontWeights.Normal;
        }

        if (string.IsNullOrWhiteSpace(_pubCfg.FeedBaseUrl) && string.IsNullOrWhiteSpace(DevFeedUrlBox.Text))
        {
            DevPublishStatus.Text +=
                "\nPrima volta: imposta FeedBaseUrl (GitHub raw …/dist/update/) o PublishTarget (cartella sync).";
        }
    }

    private bool EnsurePublishDestinations()
    {
        _pubCfg.FeedBaseUrl = DevFeedUrlBox.Text.Trim();
        _pubCfg.PublishTarget = DevSyncBox.Text.Trim();
        if (PublishService.HasPublishDestination(_pubCfg))
            return true;

        var ask = MessageBox.Show(
            "Mancano destinazioni publish.\n\n" +
            "Serve almeno uno tra:\n" +
            "• FeedBaseUrl — URL feed pubblico (raw …/MMX-NET-feed/main/)\n" +
            "• PublishTarget — clone locale MMX-NET-feed (dopo CARICA: auto-push se c'è .git)\n\n" +
            "Codice su MMX-NET privata; update zip su MMX-NET-feed pubblica (amici senza PAT).\n" +
            "Solo owner write su entrambe. Setup: toolkit\\setup_update_feed_repo.ps1\n\n" +
            "Vuoi inserirli ora? (verranno salvati in ac_dev_publish.json)",
            "Devkit — Configura feed",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (ask != MessageBoxResult.Yes)
            return false;

        var feed = PromptSimple(
            "URL feed (updateFeedUrl amici)\n" +
            "Es. https://raw.githubusercontent.com/LOGANFOREWORD/MMX-NET-feed/main/\n" +
            "Pubblico: nessun PAT. Lascia vuoto se solo sync locale.",
            DevFeedUrlBox.Text);
        if (feed == null) return false;

        var sync = PromptSimple(
            "Clone feed / cartella sync (PublishTarget)\n" +
            "Es. F:\\Anomaly Coop\\dist\\update-feed — dopo CARICA parte auto-push\n" +
            "Lascia vuoto se usi solo URL online.",
            DevSyncBox.Text);
        if (sync == null) return false;

        DevFeedUrlBox.Text = feed.Trim();
        DevSyncBox.Text = sync.Trim();
        _pubCfg.FeedBaseUrl = DevFeedUrlBox.Text;
        _pubCfg.PublishTarget = DevSyncBox.Text;

        if (!PublishService.HasPublishDestination(_pubCfg))
        {
            MessageBox.Show(
                "Serve almeno FeedBaseUrl oppure PublishTarget per procedere.",
                "Devkit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        PublishService.Save(_inst, _pubCfg);
        return true;
    }

    private static string? PromptSimple(string message, string defaultValue)
    {
        var dlg = new Window
        {
            Title = "Devkit — Configura",
            Width = 560,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x12)),
            Owner = Application.Current.MainWindow,
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 12,
        });
        var box = new TextBox
        {
            Text = defaultValue ?? "",
            Height = 30,
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
        };
        panel.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        string? result = null;
        var ok = new Button { Content = "OK", Width = 80, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Annulla", Width = 80, Height = 28 };
        ok.Click += (_, _) => { result = box.Text; dlg.DialogResult = true; };
        cancel.Click += (_, _) => { dlg.DialogResult = false; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dlg.Content = panel;
        box.Focus();
        return dlg.ShowDialog() == true ? result : null;
    }

    private async void DevPublish_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            if (!EnsurePublishDestinations())
            {
                StatusLine.Text = "Carica annullato — configura feed/sync.";
                return;
            }

            var detect = DevChangeDetectService.Detect(_inst);
            var versionText = DevVersionBox.Text.Trim();
            var local = _inst.ReadVersion();

            if (detect.HasChanges && DevAutoBumpBox.IsChecked == true)
            {
                var suggested = DevChangeDetectService.SuggestPatchBump(
                    string.IsNullOrWhiteSpace(versionText) ? local.Version : versionText);
                if (UpdateService.CompareVersions(suggested, local.Version) > 0 &&
                    UpdateService.CompareVersions(versionText, local.Version) <= 0)
                {
                    var bumpAsk = MessageBox.Show(
                        $"Ci sono modifiche non pubblicate.\n\n" +
                        $"Versione locale: {local.Version}\n" +
                        $"Proposta patch: {suggested}\n\n" +
                        "Usare il bump automatico?",
                        "Devkit — Bump versione",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    if (bumpAsk == MessageBoxResult.Cancel) return;
                    if (bumpAsk == MessageBoxResult.Yes)
                    {
                        versionText = suggested;
                        DevVersionBox.Text = suggested;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(versionText))
                throw new InvalidOperationException("Indica una versione (es. 0.1.2).");

            if (UpdateService.CompareVersions(versionText, local.Version) <= 0 && detect.HasChanges)
            {
                var force = MessageBox.Show(
                    $"La versione {versionText} non è maggiore della locale ({local.Version}).\n" +
                    "Gli amici già aggiornati non vedranno il popup.\n\n" +
                    "Continuare comunque?",
                    "Devkit — Versione",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (force != MessageBoxResult.Yes) return;
            }

            _busy = true;
            SetBusyUi(true, "Caricamento aggiornamento…");

            var notes = DevNotesBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(notes))
                notes = "Aggiornamento " + versionText + " — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            var version = new PackVersion
            {
                Name = local.Name,
                Version = versionText,
                Channel = string.IsNullOrWhiteSpace(DevChannelBox.Text) ? local.Channel : DevChannelBox.Text.Trim(),
                Protocol = local.Protocol,
                Engine = local.Engine,
                Notes = notes,
            };

            _pubCfg.FeedBaseUrl = DevFeedUrlBox.Text.Trim();
            _pubCfg.PublishTarget = DevSyncBox.Text.Trim();
            _pubCfg.AbsolutePackageUrl = true;
            _pubCfg.AutoBumpPatch = DevAutoBumpBox.IsChecked == true;

            var progress = new Progress<string>(s =>
            {
                StatusLine.Text = s;
                DevPublishStatus.Text = s;
                Log(s);
            });

            var result = await Task.Run(() => PublishService.Publish(_inst, version, _pubCfg, progress))
                .ConfigureAwait(true);

            RefreshChrome();
            RefreshDevChangesUi();

            var feedUrl = result.SuggestedFeedUrl;
            DevPublishStatus.Text =
                $"Pubblicato {result.Version.Version} → {result.OutDir}\n" +
                $"SHA-256: {result.Sha256}\n" +
                $"URL feed amici: {feedUrl}" +
                (result.SyncTarget != null ? "\nSync: " + result.SyncTarget : "") +
                (result.PushFeedMessage != null ? "\n" + result.PushFeedMessage : "") +
                (result.FeedCheckMessage != null ? "\n" + result.FeedCheckMessage : "");

            Log($"Publish OK: {result.ZipPath}");
            Log("URL updateFeedUrl amici: " + feedUrl);
            if (!string.IsNullOrWhiteSpace(result.PushFeedMessage))
                Log(result.PushFeedMessage);

            try { Clipboard.SetText(feedUrl); } catch { /* ignore */ }

            MessageBox.Show(
                $"Pubblicato. Gli amici con updateFeedUrl={feedUrl}\n" +
                "(feed pubblico MMX-NET-feed, senza PAT) vedranno il popup all'avvio.\n\n" +
                $"Versione: {result.Version.Version}\n" +
                $"Cartella: {result.OutDir}" +
                (result.SyncTarget != null ? "\nSync: " + result.SyncTarget : "") +
                (result.PushFeedMessage != null ? "\n\n" + result.PushFeedMessage : "") +
                (result.FeedCheckMessage != null ? "\n\n" + result.FeedCheckMessage : ""),
                "Devkit — Carica",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusLine.Text = "Pubblicato " + result.Version.Version + " — feed: " + feedUrl;
        }
        catch (Exception ex)
        {
            Log("ERR publish: " + ex.Message);
            DevPublishStatus.Text = "Errore: " + ex.Message;
            MessageBox.Show(ex.Message, "Carica aggiornamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusLine.Text = "Publish fallito.";
        }
        finally
        {
            _busy = false;
            SetBusyUi(false, null);
        }
    }

    private void DevOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _pubCfg.FeedBaseUrl = DevFeedUrlBox.Text.Trim();
            _pubCfg.PublishTarget = DevSyncBox.Text.Trim();
            var dir = PublishService.ResolveOutDir(_inst, _pubCfg);
            PublishService.OpenFolder(dir);
            Log("Aperta cartella: " + dir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Apri cartella", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DevCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var url = PublishService.NormalizeFeedUrl(DevFeedUrlBox.Text);
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                "Inserisci l'URL pubblico della cartella feed (es. …/dist/update/).",
                "Copia URL",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        try
        {
            Clipboard.SetText(url);
            DevPublishStatus.Text = "URL copiato: " + url;
            Log("URL feed copiato: " + url);
            StatusLine.Text = "URL feed copiato negli appunti.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copia URL", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
#else
    private void DevPublish_Click(object sender, RoutedEventArgs e) { }
    private void DevOpenFolder_Click(object sender, RoutedEventArgs e) { }
    private void DevCopyUrl_Click(object sender, RoutedEventArgs e) { }
    private void DevRefresh_Click(object sender, RoutedEventArgs e) { }
#endif

    private void RefreshChrome()
    {
        var v = _inst.ReadVersion();
#if DEVKIT
        Title = $"MMX-Net {v.Version} — DEVKIT";
#else
        Title = $"MMX-Net {v.Version}";
#endif
        TitleLabel.Text = Title;
        SteamBadge.Text = SteamService.StatusText();
        PackInfo.Text = _inst.PackStatus() + "\n" +
                        "Exe: " + Path.GetFileName(_inst.ResolveClientExe()) + "\n" +
                        "Root: " + _inst.Root;
        if (_updateAvailable && _pendingRemote != null)
        {
            SetUpdateAvailableUi(true, _pendingRemote);
        }
        else
        {
            StatusLine.Text = _inst.IsValid
                ? "Pronto — stesso pack su tutti, poi HOST o PLAY."
                : "Install incompleta — controlla bin\\ e fsgame.ltx";
        }
    }

    private void RunHealthUi()
    {
        var report = HealthCheckService.Run(_inst);
        HealthSummary.Text = report.SummaryLine;
        HealthList.ItemsSource = report.Items.Select(i => new HealthRow(i)).ToList();
        if (!report.OkToLaunch)
            StatusLine.Text = "HEALTH FATAL — PLAY bloccato finché non risolvi.";
    }

    private async void Play_Click(object sender, RoutedEventArgs e) =>
        await LaunchAsync("PLAY", hostHint: false);

    private async void Host_Click(object sender, RoutedEventArgs e) =>
        await LaunchAsync("HOST", hostHint: true);

    private void Health_Click(object sender, RoutedEventArgs e)
    {
        RunHealthUi();
        RefreshChrome();
        Log("Health refresh: " + HealthSummary.Text);
    }

    private async void Update_Click(object sender, RoutedEventArgs e) =>
        await CheckUpdatesAsync(promptIfAvailable: true, silentIfCurrent: false);

    /// <summary>
    /// Evidenzia AGGIORNA in giallo (stile PLAY) finché c'è un update remoto non applicato.
    /// </summary>
    private void SetUpdateAvailableUi(bool available, UpdateManifest? remote = null, string? status = null)
    {
        _updateAvailable = available;
        _pendingRemote = available ? remote : null;

        var accent = (Style)FindResource("OutlineAccentBtn");
        var outline = (Style)FindResource("OutlineBtn");
        ApplyUpdateBtn.Style = available ? accent : outline;
        ApplyUpdateBtn.FontWeight = available ? FontWeights.Bold : FontWeights.Normal;
        ApplyUpdateBtn.ToolTip = available && remote != null
            ? $"Update {remote.Version} disponibile — clicca per installare"
            : "Scarica overlay MMX-Net se c'è una versione nuova";

        if (status != null)
            StatusLine.Text = status;
        else if (available && remote != null)
            StatusLine.Text = $"Update {remote.Version} disponibile — premi AGGIORNA.";
    }

    private async Task CheckUpdatesAsync(bool promptIfAvailable, bool silentIfCurrent)
    {
        if (_busy) return;
        try
        {
            _busy = true;
            SetBusyUi(true, "Controllo aggiornamenti…");
            var result = await UpdateService.CheckAsync(_inst).ConfigureAwait(true);
#if DEVKIT
            // Test UI senza abbassare ac_version: set MMX_NET_SIMULATE_UPDATE=1
            if (!result.UpdateAvailable &&
                string.Equals(Environment.GetEnvironmentVariable("MMX_NET_SIMULATE_UPDATE"), "1",
                    StringComparison.Ordinal))
            {
                var simVer = result.Local.Version;
                var parts = (simVer ?? "0.0.0").Split('.');
                if (parts.Length >= 3 && int.TryParse(parts[^1], out var patch))
                    parts[^1] = (patch + 1).ToString();
                else
                    parts = new[] { simVer ?? "0", "0", "1" };
                var fakeRemote = result.Remote ?? new UpdateManifest
                {
                    Name = result.Local.Name,
                    Channel = result.Local.Channel,
                    Protocol = result.Local.Protocol,
                    Engine = result.Local.Engine,
                    Notes = "SIMULATE (MMX_NET_SIMULATE_UPDATE=1) — non scaricare.",
                };
                fakeRemote.Version = string.Join(".", parts);
                fakeRemote.Notes = string.IsNullOrWhiteSpace(fakeRemote.Notes)
                    ? "SIMULATE — non applicare."
                    : fakeRemote.Notes + "\n[SIMULATE]";
                result = new UpdateService.CheckResult
                {
                    Ok = true,
                    UpdateAvailable = true,
                    Message = $"SIMULATE update {fakeRemote.Version} (locale {result.Local.Version}).",
                    Local = result.Local,
                    Remote = fakeRemote,
                };
                Log("MMX_NET_SIMULATE_UPDATE=1 — popup/UI forzati senza bump reale.");
            }
#endif
            Log(result.Message);

            if (!result.Ok)
            {
                // silentIfCurrent nasconde solo "sei aggiornato"; errori feed privato
                // (404 senza PAT) vanno comunque spiegati con passi chiari.
                var showErr = !silentIfCurrent ||
                    result.Message.Contains("updateFeedToken", StringComparison.OrdinalIgnoreCase) ||
                    result.Message.Contains("repo GitHub privata", StringComparison.OrdinalIgnoreCase) ||
                    result.Message.Contains("FEED_PRIVATO", StringComparison.OrdinalIgnoreCase);
                if (showErr)
                {
                    MessageBox.Show(
                        result.Message,
                        "Aggiornamenti",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                StatusLine.Text = result.Message.Replace("\n", " ").Trim();
                if (StatusLine.Text.Length > 160)
                    StatusLine.Text = StatusLine.Text[..157] + "…";
                return;
            }

            if (!result.UpdateAvailable || result.Remote == null)
            {
                SetUpdateAvailableUi(false, status: result.Message);
                if (!silentIfCurrent)
                    MessageBox.Show(result.Message, "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Evidenzia subito (anche se il popup è disattivato): giallo come PLAY.
            SetUpdateAvailableUi(true, result.Remote, result.Message);

            if (!promptIfAvailable) return;

            var notes = string.IsNullOrWhiteSpace(result.Remote.Notes) ? "" : "\n\n" + result.Remote.Notes;
            var ask = MessageBox.Show(
                $"Nuovo aggiornamento disponibile.\n\n" +
                $"Locale: {result.Local.Version}\n" +
                $"Remoto: {result.Remote.Version} ({result.Remote.Channel})" +
                notes +
                "\n\nInstallare ora?",
                "MMX-Net — Aggiornamento",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            // Dopo il popup resta giallo finché non si applica (o un check successivo dice "ok").
            SetUpdateAvailableUi(true, result.Remote,
                $"Update {result.Remote.Version} disponibile — premi AGGIORNA.");

            if (ask != MessageBoxResult.Yes) return;

#if DEVKIT
            if ((result.Remote.Notes ?? "").Contains("[SIMULATE]", StringComparison.Ordinal) ||
                (result.Remote.Notes ?? "").Contains("SIMULATE (MMX_NET_SIMULATE_UPDATE", StringComparison.Ordinal))
            {
                MessageBox.Show(
                    "SIMULATE attivo: nessun download. Togli MMX_NET_SIMULATE_UPDATE per un update reale.",
                    "Aggiornamenti",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
#endif

            if (_game.IsOurGameRunning())
            {
                MessageBox.Show(
                    "Chiudi Anomaly prima di applicare l'aggiornamento (overlay a gioco aperto rischia desync/file bloccati).",
                    "Aggiornamento",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            SetBusyUi(true, "Download aggiornamento…");
            var progress = new Progress<string>(s =>
            {
                StatusLine.Text = s;
                Log(s);
            });
            var applied = await UpdateService.DownloadAndApplyAsync(_inst, result.Remote, progress).ConfigureAwait(true);
            SetUpdateAvailableUi(false);
            RefreshChrome();
            RunHealthUi();
            Log($"Aggiornamento applicato: {applied.Version}");
            StatusLine.Text = "Aggiornato a " + applied.Version;

            var restartMsg = File.Exists((Environment.ProcessPath ?? "") + MmxNetShared.FileOverlay.PendingSuffix)
                ? $"Aggiornamento {applied.Version} installato.\n\nIl launcher era in uso: al riavvio verrà sostituito automaticamente.\n\nRiavviare ora?"
                : $"Aggiornamento {applied.Version} installato.\n\nRiavviare il launcher?";
            var restart = MessageBox.Show(
                restartMsg,
                "MMX-Net",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (restart == MessageBoxResult.Yes)
                RestartLauncher();
        }
        catch (Exception ex)
        {
            Log("ERR update: " + ex.Message);
            MessageBox.Show(ex.Message, "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusLine.Text = "Aggiornamento fallito.";
            // Se c'era un update pendente, ripristina evidenziazione.
            if (_pendingRemote != null)
                SetUpdateAvailableUi(true, _pendingRemote);
        }
        finally
        {
            _busy = false;
            SetBusyUi(false, null);
            ApplyUpdateBtn.IsEnabled = true;
        }
    }

    private void RestartLauncher()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return;

            MmxNetShared.FileOverlay.RestartWithPendingReplace(exe, Environment.ProcessId);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log("Riavvio launcher fallito: " + ex.Message);
        }
    }

    private async Task LaunchAsync(string label, bool hostHint)
    {
        if (_busy) return;
        try
        {
            _busy = true;
            SetBusyUi(true, label + "…");

            var report = await Task.Run(() => HealthCheckService.Run(_inst)).ConfigureAwait(true);
            HealthSummary.Text = report.SummaryLine;
            HealthList.ItemsSource = report.Items.Select(i => new HealthRow(i)).ToList();
            if (!report.OkToLaunch)
            {
                var fatal = string.Join("\n", report.Items.Where(i => !i.Pass && i.Sev == CheckSev.Fatal).Select(i => "• " + i.Msg));
                throw new InvalidOperationException("Health FATAL — avvio bloccato:\n" + fatal);
            }

            var steam = UseSteamBox.IsChecked == true;
            if (!steam)
            {
                throw new InvalidOperationException(
                    "Per inviti Shift+Tab serve «Usa Steam».\n" +
                    "Senza Steam resta solo Connect IP (LAN) — non è il flusso MMX.");
            }

            var args = await Task.Run(() =>
                _game.PlayClient(steam, stripDebug: true, asHost: hostHint)).ConfigureAwait(true);
            Log($"{label} → {args}");
            Log("Steam: Call of Pripyat (41700) via -applaunch + bridge → Anomaly.");
            Log("Prima volta: Steam può riavviarsi per scrivere le Launch Options CoP.");
            if (hostHint)
            {
                Log("HOST: in partita aspetta tip hosting → Shift+Tab → Invite amico.");
                Log("NON usare Connect IP.");
            }
            else
                Log("PLAY: Join Game da Friends sull'host (niente menu IP).");
            StatusLine.Text = "In Steam risulti su Call of Pripyat (41700). Invita da Friends.";
            RefreshChrome();
        }
        catch (Exception ex)
        {
            Log("ERR: " + ex.Message);
            MessageBox.Show(ex.Message, label, MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusLine.Text = label + " fallito.";
        }
        finally
        {
            _busy = false;
            SetBusyUi(false, null);
        }
    }

    private void SetBusyUi(bool busy, string? status)
    {
        UseSteamBox.IsEnabled = !busy;
        ApplyUpdateBtn.IsEnabled = !busy;
        UpdateBtn.IsEnabled = !busy;
#if DEVKIT
        DevPublishBtn.IsEnabled = !busy;
        DevOpenFolderBtn.IsEnabled = !busy;
        DevCopyUrlBtn.IsEnabled = !busy;
        DevRefreshBtn.IsEnabled = !busy;
#endif
        if (status != null) StatusLine.Text = status;
    }

    private void Log(string line)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        LogBox.AppendText($"[{ts}] {line}\n");
        LogBox.ScrollToEnd();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void TitleMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void TitleClose_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class HealthRow
{
    public string Sev { get; }
    public string Msg { get; }
    public Brush SevBrush { get; }
    public Brush MsgBrush { get; }

    public HealthRow(CheckItem i)
    {
        Sev = i.Pass ? "OK" : i.Sev.ToString().ToUpperInvariant();
        Msg = i.Msg;
        SevBrush = i.Pass
            ? (Brush)Application.Current.FindResource("Ok")
            : i.Sev switch
            {
                CheckSev.Fatal => (Brush)Application.Current.FindResource("Danger"),
                CheckSev.Desync => (Brush)Application.Current.FindResource("Accent"),
                _ => (Brush)Application.Current.FindResource("Muted"),
            };
        MsgBrush = i.Pass
            ? (Brush)Application.Current.FindResource("Muted")
            : (Brush)Application.Current.FindResource("Text");
    }
}
