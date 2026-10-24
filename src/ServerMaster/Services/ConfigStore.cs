using System;
using System.IO;
using System.Web.Script.Serialization;
using ServerMaster.Models;

namespace ServerMaster.Services
{
    /// <summary>配置持久化：%APPDATA%\ServerMaster\config.json（临时文件+替换，保证原子性）。</summary>
    public static class ConfigStore
    {
        public static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServerMaster");
        public static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigFile)) return NewConfig();
                JavaScriptSerializer ser = new JavaScriptSerializer();
                AppConfig cfg = ser.Deserialize<AppConfig>(File.ReadAllText(ConfigFile));
                if (cfg == null) return NewConfig();
                if (cfg.Servers == null) cfg.Servers = new System.Collections.Generic.List<ServerInfo>();
                if (cfg.Groups == null) cfg.Groups = new System.Collections.Generic.List<string>();
                if (cfg.Settings == null) cfg.Settings = new AppSettings();
                return cfg;
            }
            catch (Exception ex)
            {
                AppLogger.Error("读取配置失败，使用空白配置", ex);
                return NewConfig();
            }
        }

        public static void Save(AppConfig cfg)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(cfg);
                string tmp = ConfigFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(ConfigFile)) File.Delete(ConfigFile);
                File.Move(tmp, ConfigFile);
            }
            catch (Exception ex)
            {
                AppLogger.Error("保存配置失败", ex);
                throw;
            }
        }

        private static AppConfig NewConfig()
        {
            AppConfig cfg = new AppConfig();
            cfg.Groups.Add("默认分组");
            return cfg;
        }
    }
}
