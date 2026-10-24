using System;
using System.IO;

namespace ServerMaster.Services
{
    /// <summary>极简文件日志：%LOCALAPPDATA%\ServerMaster\logs\yyyy-MM-dd.log</summary>
    public static class AppLogger
    {
        private static readonly object _sync = new object();
        private static readonly string _dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "ServerMaster", "logs");

        public static void Info(string msg) { Write("INFO ", msg, null); }
        public static void Warn(string msg) { Write("WARN ", msg, null); }
        public static void Error(string msg) { Write("ERROR", msg, null); }
        public static void Error(string msg, Exception ex) { Write("ERROR", msg, ex); }

        private static void Write(string level, string msg, Exception ex)
        {
            try
            {
                lock (_sync)
                {
                    Directory.CreateDirectory(_dir);
                    string file = Path.Combine(_dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                    string line = string.Format("{0} [{1}] {2}",
                        DateTime.Now.ToString("HH:mm:ss.fff"), level, msg);
                    if (ex != null) line += Environment.NewLine + ex;
                    File.AppendAllText(file, line + Environment.NewLine);
                }
            }
            catch
            {
                // 日志失败不影响主流程
            }
        }
    }
}
