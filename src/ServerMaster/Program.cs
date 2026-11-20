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
            // 单文件分发：AxMSTSCLib / MSTSCLib 以嵌入资源随 exe 携带
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                string name = new System.Reflection.AssemblyName(args.Name).Name;
                if (name != "AxMSTSCLib" && name != "MSTSCLib") return null;
                using (System.IO.Stream st = typeof(Program).Assembly.GetManifestResourceStream("ServerMaster." + name + ".dll"))
                {
                    if (st == null) return null;
                    byte[] buf = new byte[st.Length];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = st.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return System.Reflection.Assembly.Load(buf);
                }
            };

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
