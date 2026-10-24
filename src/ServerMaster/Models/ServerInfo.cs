using System;

namespace ServerMaster.Models
{
    /// <summary>单台服务器连接配置。</summary>
    public class ServerInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Domain { get; set; }
        public string Username { get; set; }
        /// <summary>DPAPI 加密后的密码密文（仅本机本账户可解）。</summary>
        public string EncryptedPassword { get; set; }
        public string Group { get; set; }
        /// <summary>true=连接到服务器的控制台(管理)会话。</summary>
        public bool ConnectToConsole { get; set; }
        /// <summary>RDP 色彩深度 8/15/16/24/32，多路同屏建议 16。</summary>
        public int ColorDepth { get; set; }
        /// <summary>重定向本地磁盘到远程会话(远程可见 \\tsclient)。</summary>
        public bool RedirectDrives { get; set; }
        public bool AutoReconnect { get; set; }
        public string Notes { get; set; }
        /// <summary>固定远端分辨率（0=跟随全局默认）。分辨率不随分格/窗口变化，只做显示端缩放。</summary>
        public int DesktopWidth { get; set; }
        public int DesktopHeight { get; set; }

        public ServerInfo()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Host = "";
            Port = 3389;
            Domain = "";
            Username = "Administrator";
            EncryptedPassword = "";
            Group = "默认分组";
            ColorDepth = 16;
            RedirectDrives = false;
            AutoReconnect = true;
            Notes = "";
            DesktopWidth = 0;
            DesktopHeight = 0;
        }

        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Name) ? Host : Name; }
        }

        public ServerInfo Clone()
        {
            ServerInfo c = new ServerInfo();
            c.Id = Id; c.Name = Name; c.Host = Host; c.Port = Port; c.Domain = Domain;
            c.Username = Username; c.EncryptedPassword = EncryptedPassword; c.Group = Group;
            c.ConnectToConsole = ConnectToConsole; c.ColorDepth = ColorDepth;
            c.RedirectDrives = RedirectDrives; c.AutoReconnect = AutoReconnect; c.Notes = Notes;
            c.DesktopWidth = DesktopWidth; c.DesktopHeight = DesktopHeight;
            return c;
        }
    }
}
