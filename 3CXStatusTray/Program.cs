using System.Threading;
using System.Windows.Forms;

namespace _3CXStatusTray;

internal static class Program
{
    // Local\ scopes the mutex to the current Windows session, which is the
    // correct scope here - two users can legitimately each run their own
    // tray (fast user switching, RDP), but a single user should only get
    // one instance on their desk.
    private const string SingleInstanceMutexName = @"Local\3CXStatusTray.SingleInstance";

    // Must be a static field so the GC keeps the Mutex alive for the full
    // process lifetime. A local in Main would be eligible for collection
    // once Application.Run returns (or earlier under aggressive GC).
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "3CX Status Tray is already running - look for its icon in the notification area.",
                "3CX Status Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MyApplicationContext());
    }
}
