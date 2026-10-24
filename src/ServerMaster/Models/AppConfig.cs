using System.Collections.Generic;

namespace ServerMaster.Models
{
    public class AppSettings
    {
        /// <summary>0=自动(近方阵)，1~4=固定列数。</summary>
        public int MaxColumns { get; set; }
        public int DefaultColorDepth { get; set; }
        public bool ConfirmCloseAll { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        /// <summary>拖文件到分格时直接 SMB 上传到的远端目录（空=默认 C:\Users\Public\Desktop）。</summary>
        public string DragDropRemoteDir { get; set; }

        public AppSettings()
        {
            MaxColumns = 0;
            DefaultColorDepth = 16;
            ConfirmCloseAll = true;
            WindowWidth = 1400;
            WindowHeight = 900;
            DragDropRemoteDir = "";
        }
    }

    /// <summary>config.json 根对象。</summary>
    public class AppConfig
    {
        public List<ServerInfo> Servers { get; set; }
        public List<string> Groups { get; set; }
        public AppSettings Settings { get; set; }

        public AppConfig()
        {
            Servers = new List<ServerInfo>();
            Groups = new List<string>();
            Settings = new AppSettings();
        }
    }
}
