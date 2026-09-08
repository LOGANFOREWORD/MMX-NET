using System.Diagnostics;
using System.IO;
using System.Windows;
using MmxNetShared;

namespace MmxNetLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            // Pending self-replace: esci subito e lascia allo script sostituire l'exe.
            if (File.Exists(exe + FileOverlay.PendingSuffix))
            {
                FileOverlay.RestartWithPendingReplace(exe, Environment.ProcessId);
                Shutdown();
                return;
            }

            // Altri *.new (dll, script, …) applicabili ora che non siamo locked su di essi.
            var root = Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(root))
                FileOverlay.TryApplyPendingUnder(root, skipExactPath: exe);
        }

        base.OnStartup(e);
    }
}
