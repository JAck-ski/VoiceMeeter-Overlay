using System;
using System.Windows.Forms;
using VoiceMeeter_Overlay;

internal static class Program
{
    /// <summary>
    /// Application entry point. Starts the overlay window.
    /// </summary>
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Form1());
    }
}
