using System;
using System.Threading;
using System.Windows.Forms;

namespace LightMonitor;

static class Program
{
    private const string MutexName = "Global\\LightMonitor_SingleInstance_v1";
    private static Mutex? _appMutex;

    [STAThread]
    static void Main()
    {
        // ── Safety Guard 1: prevent double-launch ────────────────────────
        _appMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            _appMutex.Dispose();
            return; // already running — exit silently
        }

        // ── Safety Guard 2: global exception sink (log, never crash) ─────
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Write($"[UNHANDLED] {e.ExceptionObject}");

        Application.ThreadException += (_, e) =>
            Logger.Write($"[THREAD_EX] {e.Exception}");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Install the WinForms synchronization context BEFORE constructing TrayApp.
            // Application.Run() would do this automatically, but TrayApp is constructed
            // as the argument — so we must do it here to avoid a null SyncContext crash.
            SynchronizationContext.SetSynchronizationContext(
                new System.Windows.Forms.WindowsFormsSynchronizationContext());

            using var app = new TrayApp();
            Application.Run(app);   // blocks until app.ExitThread() is called
        }
        catch (Exception ex)
        {
            Logger.Write($"[STARTUP_FAIL] {ex}");
        }
        finally
        {
            _appMutex.ReleaseMutex();
            _appMutex.Dispose();
        }
    }
}
