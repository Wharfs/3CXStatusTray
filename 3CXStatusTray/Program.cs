using System.Windows.Forms;

namespace _3CXStatusTray;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MyApplicationContext());
    }
}
