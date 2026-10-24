using System;
using System.ComponentModel;
using System.IO;
using ServerMaster.Models;

namespace ServerMaster.Services.FileTransfer
{
    /// <summary>
    /// 文件传输通道①：SMB 管理共享。
    /// WNetAddConnection2 以目标服务器管理员凭据建立 IPC$ 会话后，
    /// 直接对 \\主机\c$\... 执行流式复制；结束即断开，不驻留凭据。
    /// </summary>
    public static class SmbChannel
    {
        private const int RESOURCETYPE_DISK = 1;

        /// <summary>建立到目标主机的凭据会话（幂等；1219 冲突时先清除旧会话重试）。</summary>
        public static void Connect(ServerInfo server)
        {
            string remote = "\\\\" + server.Host + "\\IPC$";
            string user = BuildUserName(server);
            string password = PasswordProtector.Unprotect(server.EncryptedPassword);

            Win32.NativeMethods.NETRESOURCE nr = new Win32.NativeMethods.NETRESOURCE();
            nr.dwType = RESOURCETYPE_DISK;
            nr.lpRemoteName = remote;

            int rc = Win32.NativeMethods.WNetAddConnection2W(nr, password, user, 0);
            if (rc == 1219) // 已存在使用其他凭据的会话
            {
                Win32.NativeMethods.WNetCancelConnection2W(remote, 0, true);
                rc = Win32.NativeMethods.WNetAddConnection2W(nr, password, user, 0);
            }
            if (rc != 0)
                throw new IOException("连接 " + server.Host + " 凭据会话失败: " + FriendlyError(rc));
        }

        public static void Disconnect(ServerInfo server)
        {
            try
            {
                Win32.NativeMethods.WNetCancelConnection2W("\\\\" + server.Host + "\\IPC$", 0, true);
            }
            catch { }
        }

        /// <summary>"C:\Deploy" → "\\host\c$\Deploy"</summary>
        public static string ToRemotePath(ServerInfo server, string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath) || windowsPath.Length < 2 || windowsPath[1] != ':')
                throw new ArgumentException("远端路径必须是类似 C:\\Deploy 的 Windows 绝对路径");
            string drive = char.ToLowerInvariant(windowsPath[0]).ToString();
            string rest = windowsPath.Substring(2).TrimStart('\\');
            string remote = "\\\\" + server.Host + "\\" + drive + "$";
            if (rest.Length > 0) remote += "\\" + rest;
            return remote;
        }

        /// <summary>上传单文件（自动创建远端目录）。</summary>
        public static void UploadFile(ServerInfo server, string srcLocalFile, string dstRemoteDir,
                                      Action<long, long> progress)
        {
            string dstDir = ToRemotePath(server, dstRemoteDir);
            if (!dstDir.StartsWith("\\\\")) throw new ArgumentException("远端目录无效");
            Directory.CreateDirectory(dstDir);
            string dst = Path.Combine(dstDir, Path.GetFileName(srcLocalFile));
            CopyWithProgress(srcLocalFile, dst, progress);
        }

        /// <summary>下载单文件（远端路径形如 C:\Logs\a.log）。</summary>
        public static void DownloadFile(ServerInfo server, string srcRemoteFile, string dstLocalFile,
                                        Action<long, long> progress)
        {
            string src = ToRemotePath(server, srcRemoteFile);
            string dstDir = Path.GetDirectoryName(dstLocalFile);
            if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
            CopyWithProgress(src, dstLocalFile, progress);
        }

        private static void CopyWithProgress(string src, string dst, Action<long, long> progress)
        {
            const int bufSize = 64 * 1024;
            byte[] buf = new byte[bufSize];
            long total = 0;
            long len = 0;
            try { len = new FileInfo(src).Length; } catch { }

            using (FileStream inStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream outStream = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = inStream.Read(buf, 0, bufSize)) > 0)
                {
                    outStream.Write(buf, 0, read);
                    total += read;
                    if (progress != null) progress(total, len);
                }
            }
        }

        private static string BuildUserName(ServerInfo server)
        {
            if (!string.IsNullOrEmpty(server.Domain))
                return server.Domain + "\\" + server.Username;
            // 远端本机账户用 HOST\user 形式，避免域定位歧义
            return server.Host + "\\" + server.Username;
        }

        private static string FriendlyError(int rc)
        {
            switch (rc)
            {
                case 5: return "拒绝访问(5)：请检查账号密码与本地安全策略";
                case 53: return "找不到网络路径(53)：网络不通或 445 端口未开放/管理共享被禁用";
                case 64: return "指定的网络名不再可用(64)：445 端口连接被重置——NAT VPS/云防火墙通常不转发 SMB 端口，请改用「批量推送(经RDP)」";
                case 86: case 1326: return "凭据错误(账号或密码不正确)";
                case 1219: return "凭据冲突(1219)：该主机已存在其他 Windows 凭据会话";
                case 1203: return "无法连接网络驱动器(1203)";
                default: return new Win32Exception(rc).Message + " (" + rc + ")";
            }
        }
    }
}
