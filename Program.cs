using System;
using System.Windows.Forms;

namespace ErgTrainer
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Classic WinForms startup
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new MainForm());
        }
    }
}
