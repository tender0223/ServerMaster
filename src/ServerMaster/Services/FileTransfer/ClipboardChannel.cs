using System.Collections.Specialized;
using System.Windows.Forms;

namespace ServerMaster.Services.FileTransfer
{
    /// <summary>
    /// 文件传输通道②：RDP 剪贴板重定向。
    /// 把本地文件放入剪贴板(CF_HDROP)，RDP 剪贴板重定向会自动同步到远程会话，
    /// 在远程桌面里 Ctrl+V 即可获得文件。配合"广播输入"可一次粘贴到多台。
    /// 必须在 UI 线程调用。
    /// </summary>
    public static class ClipboardChannel
    {
        public static void PushFiles(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            StringCollection sc = new StringCollection();
            sc.AddRange(paths);
            Clipboard.SetFileDropList(sc);
        }
    }
}
