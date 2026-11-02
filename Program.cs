namespace LabelPrinter;

static class Program
{
    // Global\ guards against a second instance across ALL sessions (fast-user-switch / RDP),
    // which matters because instances share the install dir's config and REST ports. But
    // creating a Global\ object needs SeCreateGlobalPrivilege, which a non-elevated
    // interactive session can be denied — so we fall back to a per-session Local\ guard.
    private const string GlobalMutexName = "Global\\ControlCodeLabelPrinterTray";
    private const string LocalMutexName = "Local\\ControlCodeLabelPrinterTray";

    [STAThread]
    static void Main()
    {
        // Catch UI-thread exceptions ourselves so a stray exception logs + warns and the
        // tray app keeps running, instead of the whole process disappearing silently.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatal("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleFatal("background", e.ExceptionObject as Exception);

        // Load just for the persisted language: this sets L.Current so the "already
        // running" dialog below matches the user's chosen language even before the tray
        // context (which reloads the full config) exists.
        AppConfig.Load();

        if (!TryBecomeSingleInstance(out var mutex) && !TryReplacePreviousInstance(out mutex))
            return;

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            mutex?.Dispose();
        }
    }

    /// <summary>
    /// Acquires the single-instance guard. Returns false only when another instance
    /// already holds it (caller should exit). Mutex creation runs BEFORE the message
    /// loop, so a thrown exception here would bypass Application.ThreadException and take
    /// the process down; instead we degrade Global\ → Local\ → no guard so startup never
    /// crashes on a locked-down / non-elevated session.
    /// </summary>
    private static bool TryBecomeSingleInstance(out Mutex? mutex)
    {
        foreach (var name in new[] { GlobalMutexName, LocalMutexName })
        {
            try
            {
                var candidate = new Mutex(true, name, out var createdNew);
                if (createdNew)
                {
                    mutex = candidate;
                    return true;
                }

                // Another instance already owns this guard.
                candidate.Dispose();
                mutex = null;
                return false;
            }
            catch (Exception ex)
            {
                FileLog.Write($"Single-instance mutex '{name}' unavailable: {ex.Message}");
            }
        }

        // Couldn't create any guard (e.g. privilege denied); allow startup rather than
        // refusing to run. Worst case is a duplicate instance, not a crash.
        mutex = null;
        return true;
    }

    /// <summary>
    /// Another instance holds the guard. Offers to close it so this (newer) build can start;
    /// returns true only when the guard was re-acquired. Never closes anything without a "Yes".
    /// </summary>
    private static bool TryReplacePreviousInstance(out Mutex? mutex)
    {
        mutex = null;
        var title = L.T("tray.balloon.title");
        var others = PreviousInstance.FindOthers();
        try
        {
            Mutex? acquired = null;
            var result = PreviousInstance.TakeOver(
                others,
                confirm: description =>
                {
                    var answer = MessageBox.Show(
                        L.F("app.replace.prompt", description),
                        title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                    if (answer == DialogResult.Yes)
                        FileLog.Write($"Closing previous instance(s): {description.ReplaceLineEndings(" | ")}");
                    return answer == DialogResult.Yes;
                },
                tryAcquireLock: () => TryBecomeSingleInstance(out acquired),
                stopWait: TimeSpan.FromSeconds(8),
                lockAttempts: 10,
                lockDelay: TimeSpan.FromMilliseconds(300));

            switch (result.Outcome)
            {
                case TakeoverOutcome.Taken:
                    mutex = acquired;
                    return true;

                case TakeoverOutcome.Declined:
                    return false;

                case TakeoverOutcome.NothingFound:
                    // Held by something we can't see or identify (e.g. another session): just say so.
                    MessageBox.Show(L.T("app.alreadyRunning"), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;

                default:
                    var reason = result.Outcome == TakeoverOutcome.LockStillHeld
                        ? L.T("app.replace.stillHeld")
                        : result.Detail;
                    FileLog.Write($"Could not take over from previous instance: {reason}");
                    MessageBox.Show(L.F("app.replace.failed", reason), title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
            }
        }
        finally
        {
            foreach (var handle in others)
                handle.Dispose();
        }
    }

    private static void HandleFatal(string source, Exception? ex)
    {
        FileLog.Write($"Unhandled exception ({source}): {ex}");
        try
        {
            MessageBox.Show(
                ex?.Message ?? "Unknown error",
                L.T("tray.balloon.title"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Nothing more we can do if even the dialog fails.
        }
    }
}
