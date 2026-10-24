using System;
using System.Windows.Forms;
using ServerMaster.Services;
using ServerMaster.Win32;

namespace ServerMaster
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                AppLogger.Error("UI 线程异常", e.Exception);
                MessageBox.Show("发生未处理异常:\n" + e.Exception.Message + "\n\n详情见日志目录。",
                    "ServerMaster", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                AppLogger.Error("未处理异常", e.ExceptionObject as Exception);
            };

            Application.Run(new UI.MainForm());
        }
    }
}
