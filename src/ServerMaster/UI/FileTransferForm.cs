using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Services;
using ServerMaster.Services.FileTransfer;

namespace ServerMaster.UI
{
    /// <summary>
    /// 批量文件传输：
    ///  上传——勾选多台服务器，把本地文件/目录批量分发到相同远端路径 (SMB 管理共享通道)；
    ///  下载——从某台服务器拉取文件；
    ///  剪贴板——把所选文件推送到剪贴板，在任意远程会话 Ctrl+V (RDP 剪贴板通道)。
    /// </summary>
    public class FileTransferForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly HashSet<string> _connectedIds;
        /// <summary>经 RDP 剪贴板向勾选的已连接服务器广播 Ctrl+V（由主窗体提供）。</summary>
        private readonly Action<List<ServerInfo>> _broadcastPaste;
        private CheckedListBox _lstServers;
        private TextBox _txtRemoteDir;
        private ListBox _lstFiles;
        private List<string> _files = new List<string>();
        private string _baseDir = "";
        private ProgressBar _progress;
        private Label _lblStatus;
        private TextBox _txtLog;
        private TextBox _txtRemoteFile;
        private TextBox _txtLocalSave;
        private Button _btnStart;
        private Thread _worker;
        private volatile bool _cancel;

        public FileTransferForm(AppConfig cfg, HashSet<string> connectedIds,
                                Action<List<ServerInfo>> broadcastPaste)
        {
            _cfg = cfg;
            _connectedIds = connectedIds ?? new HashSet<string>();
            _broadcastPaste = broadcastPaste;

            Text = "批量文件传输";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 560);
            MinimumSize = new Size(680, 520);
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();
            LoadServers();
        }

        private void BuildUi()
        {
            Label l1 = new Label();
            l1.Text = "目标服务器(可多选):";
            l1.AutoSize = true;
            l1.Location = new Point(12, 12);
            Controls.Add(l1);

            _lstServers = new CheckedListBox();
            _lstServers.SetBounds(12, 32, 236, 170);
            _lstServers.CheckOnClick = true;
            _lstServers.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
            _lstServers.ItemCheck += delegate(object s, ItemCheckEventArgs e)
            {
                BeginInvoke((MethodInvoker)delegate { UpdateStartButton(); });
            };
            Controls.Add(_lstServers);

            Label lHintSel = new Label();
            lHintSel.Text = "已连接的服务器自动勾选";
            lHintSel.AutoSize = true;
            lHintSel.ForeColor = Color.FromArgb(110, 110, 118);
            lHintSel.Location = new Point(12, 204);
            Controls.Add(lHintSel);

            // ---- 上传区 ----
            GroupBox gUpload = new GroupBox();
            gUpload.Text = "上传 (本机 → 多台服务器相同路径)";
            gUpload.SetBounds(256, 8, 452, 200);
            gUpload.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(gUpload);

            Label l2 = new Label();
            l2.Text = "远端目录:";
            l2.AutoSize = true;
            l2.Location = new Point(10, 26);
            gUpload.Controls.Add(l2);

            _txtRemoteDir = new TextBox();
            _txtRemoteDir.Text = "C:\\Deploy";
            _txtRemoteDir.SetBounds(76, 22, 240, 24);
            gUpload.Controls.Add(_txtRemoteDir);

            _lstFiles = new ListBox();
            _lstFiles.SetBounds(10, 54, 280, 100);
            _lstFiles.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _lstFiles.AllowDrop = true;
            _lstFiles.DragEnter += files_DragEnter;
            _lstFiles.DragDrop += files_DragDrop;
            gUpload.Controls.Add(_lstFiles);

            Button btnAddFiles = new Button();
            btnAddFiles.Text = "添加文件";
            btnAddFiles.SetBounds(300, 54, 110, 24);
            btnAddFiles.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAddFiles.Click += btnAddFiles_Click;
            gUpload.Controls.Add(btnAddFiles);

            Button btnAddDir = new Button();
            btnAddDir.Text = "添加文件夹";
            btnAddDir.SetBounds(300, 82, 110, 24);
            btnAddDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnAddDir.Click += btnAddDir_Click;
            gUpload.Controls.Add(btnAddDir);

            Button btnClear = new Button();
            btnClear.Text = "清空列表";
            btnClear.SetBounds(300, 110, 110, 24);
            btnClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClear.Click += delegate { _files.Clear(); _baseDir = ""; _lstFiles.Items.Clear(); };
            gUpload.Controls.Add(btnClear);

            // 经 RDP 剪贴板批量推送：不依赖 445 端口，NAT VPS 也能用
            Button btnBroadcastPaste = new Button();
            btnBroadcastPaste.Text = "批量推送(经RDP)";
            btnBroadcastPaste.SetBounds(300, 138, 110, 24);
            btnBroadcastPaste.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBroadcastPaste.Click += btnBroadcastPaste_Click;
            gUpload.Controls.Add(btnBroadcastPaste);

            Button btnClip = new Button();
            btnClip.Text = "推送到剪贴板";
            btnClip.SetBounds(300, 166, 110, 24);
            btnClip.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClip.Click += btnClip_Click;
            gUpload.Controls.Add(btnClip);

            Label lHint = new Label();
            lHint.Text = "推送后切到远端会话 Ctrl+V 即可粘贴";
            lHint.AutoSize = true;
            lHint.ForeColor = Color.FromArgb(110, 110, 118);
            lHint.Location = new Point(10, 178);
            gUpload.Controls.Add(lHint);

            // ---- 下载区 ----
            GroupBox gDown = new GroupBox();
            gDown.Text = "下载 (服务器 → 本地)";
            gDown.SetBounds(12, 208, 696, 62);
            gDown.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(gDown);

            Label l3 = new Label();
            l3.Text = "远端文件:";
            l3.AutoSize = true;
            l3.Location = new Point(10, 28);
            gDown.Controls.Add(l3);

            _txtRemoteFile = new TextBox();
            _txtRemoteFile.Text = "C:\\Logs\\app.log";
            _txtRemoteFile.SetBounds(76, 24, 200, 24);
            gDown.Controls.Add(_txtRemoteFile);

            Label l4 = new Label();
            l4.Text = "保存到:";
            l4.AutoSize = true;
            l4.Location = new Point(286, 28);
            gDown.Controls.Add(l4);

            _txtLocalSave = new TextBox();
            _txtLocalSave.SetBounds(336, 24, 220, 24);
            gDown.Controls.Add(_txtLocalSave);

            Button btnBrowse = new Button();
            btnBrowse.Text = "…";
            btnBrowse.SetBounds(560, 23, 30, 25);
            btnBrowse.Click += delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    if (d.ShowDialog(this) == DialogResult.OK) _txtLocalSave.Text = d.SelectedPath;
                }
            };
            gDown.Controls.Add(btnBrowse);

            Button btnDownload = new Button();
            btnDownload.Text = "开始下载";
            btnDownload.SetBounds(594, 23, 90, 25);
            btnDownload.Click += btnDownload_Click;
            gDown.Controls.Add(btnDownload);

            // ---- 进度/日志/开始 ----
            _btnStart = new Button();
            _btnStart.Text = "开始上传";
            _btnStart.SetBounds(256, 278, 110, 30);
            _btnStart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _btnStart.Click += btnStart_Click;
            Controls.Add(_btnStart);

            Button btnCancel = new Button();
            btnCancel.Text = "取消传输";
            btnCancel.SetBounds(374, 278, 90, 30);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnCancel.Click += delegate { _cancel = true; Status("正在取消…"); };
            Controls.Add(btnCancel);

            _progress = new ProgressBar();
            _progress.SetBounds(12, 316, 696, 18);
            _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_progress);

            _lblStatus = new Label();
            _lblStatus.Text = "就绪";
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(12, 338);
            _lblStatus.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            Controls.Add(_lblStatus);

            _txtLog = new TextBox();
            _txtLog.Multiline = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.ReadOnly = true;
            _txtLog.Font = new Font("Consolas", 8.5F);
            _txtLog.SetBounds(12, 358, 696, 190);
            _txtLog.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(_txtLog);

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (_worker != null && _worker.IsAlive)
                {
                    _cancel = true;
                    if (e.CloseReason == CloseReason.UserClosing)
                    {
                        Log("已请求取消，窗口将在传输线程结束后关闭…");
                        e.Cancel = true;
                        return;
                    }
                }
            };
        }

        private void LoadServers()
        {
            foreach (ServerInfo s in _cfg.Servers)
                _lstServers.Items.Add(s, _connectedIds.Contains(s.Id));
            UpdateStartButton();
        }

        /// <summary>开始按钮实时显示本次目标台数。</summary>
        private void UpdateStartButton()
        {
            int n = _lstServers.CheckedItems.Count;
            _btnStart.Text = n > 0 ? "开始上传(" + n + "台)" : "开始上传";
        }

        private List<ServerInfo> GetSelectedServers()
        {
            List<ServerInfo> list = new List<ServerInfo>();
            foreach (object item in _lstServers.CheckedItems)
                list.Add((ServerInfo)item);
            return list;
        }

        #region 文件列表维护

        private void files_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void files_DragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            AddPaths(paths);
        }

        private void btnAddFiles_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Multiselect = true;
                d.Title = "选择要分发的文件";
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames);
            }
        }

        private void btnAddDir_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择要整体分发的文件夹";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    string[] dirs = Directory.GetDirectories(d.SelectedPath, "*", SearchOption.AllDirectories);
                    List<string> all = new List<string>();
                    all.AddRange(Directory.GetFiles(d.SelectedPath, "*", SearchOption.AllDirectories));
                    if (all.Count == 0)
                    {
                        MessageBox.Show(this, "该文件夹(含子目录)没有文件。", "提示");
                        return;
                    }
                    _baseDir = d.SelectedPath;
                    foreach (string f in all) AddFile(f);
                    RefreshList();
                }
            }
        }

        private void AddPaths(string[] paths)
        {
            if (paths == null) return;
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    if (_baseDir.Length == 0) _baseDir = p;
                    foreach (string f in Directory.GetFiles(p, "*", SearchOption.AllDirectories)) AddFile(f);
                }
                else if (File.Exists(p))
                {
                    AddFile(p);
                }
            }
            RefreshList();
        }

        private void AddFile(string f)
        {
            if (!_files.Contains(f)) _files.Add(f);
        }

        private void RefreshList()
        {
            _lstFiles.BeginUpdate();
            _lstFiles.Items.Clear();
            foreach (string f in _files) _lstFiles.Items.Add(f);
            _lstFiles.EndUpdate();
        }

        #endregion

        private void btnClip_Click(object sender, EventArgs e)
        {
            if (_files.Count == 0)
            {
                MessageBox.Show(this, "请先添加文件。", "提示");
                return;
            }
            ClipboardChannel.PushFiles(_files.ToArray());
            Status("已推送到剪贴板 —— 切换到远程会话 Ctrl+V 粘贴");
            Log("剪贴板推送 " + _files.Count + " 个文件");
        }

        /// <summary>
        /// 批量推送(经RDP)：文件入剪贴板 → 主窗体向勾选的已连接会话广播 Ctrl+V。
        /// 不依赖 445 端口，NAT VPS / SMB 被封的环境也能用。
        /// 文件将出现在每台服务器当前会话的聚焦窗口（无人操作时即桌面=桌面目录）。
        /// </summary>
        private void btnBroadcastPaste_Click(object sender, EventArgs e)
        {
            if (_files.Count == 0)
            {
                MessageBox.Show(this, "请先添加文件。", "提示");
                return;
            }
            if (_broadcastPaste == null)
            {
                MessageBox.Show(this, "当前无法使用批量推送。", "提示");
                return;
            }
            List<ServerInfo> targets = GetSelectedServers();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "请先勾选目标服务器（已连接的会自动勾选）。", "提示");
                return;
            }
            if (_worker != null && _worker.IsAlive)
            {
                MessageBox.Show(this, "SMB 传输进行中，请等待或取消后再推送。", "提示");
                return;
            }

            ClipboardChannel.PushFiles(_files.ToArray());
            Log("批量推送：已把 " + _files.Count + " 个文件放入剪贴板，1.5 秒后向勾选的已连接服务器广播 Ctrl+V…");
            Status("批量推送中（文件将出现在远端桌面/当前聚焦窗口）…");
            _broadcastPaste(targets);
        }

        #region 上传

        private void btnStart_Click(object sender, EventArgs e)
        {
            List<ServerInfo> targets = GetSelectedServers();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "请先勾选至少一台目标服务器。", "提示");
                return;
            }
            if (_files.Count == 0)
            {
                MessageBox.Show(this, "请先添加要上传的文件/文件夹。", "提示");
                return;
            }
            if (_worker != null && _worker.IsAlive) return;

            string remoteDir = _txtRemoteDir.Text.Trim();
            string baseDir = _baseDir;
            string[] files = _files.ToArray();
            ServerInfo[] servers = targets.ToArray();
            long totalBytes = 0;
            foreach (string f in files)
            {
                try { totalBytes += new FileInfo(f).Length; } catch { }
            }
            long grandTotal = totalBytes * servers.Length;

            _cancel = false;
            _btnStart.Enabled = false;
            SetProgress(0, (int)Math.Min(int.MaxValue, grandTotal), 0);

            _worker = new Thread(delegate()
            {
                RunUploads(servers, files, baseDir, remoteDir, grandTotal);
            });
            _worker.IsBackground = true;
            _worker.Start();
        }

        private void RunUploads(ServerInfo[] servers, string[] files, string baseDir, string remoteDir, long grandTotal)
        {
            long doneBytes = 0;
            int okServers = 0, failServers = 0;
            foreach (ServerInfo s in servers)
            {
                if (_cancel) break;
                Log("[" + s.DisplayName + "] 连接凭据会话…");
                try
                {
                    SmbChannel.Connect(s);
                }
                catch (Exception ex)
                {
                    Log("[" + s.DisplayName + "] 连接失败: " + ex.Message);
                    failServers++;
                    continue;
                }
                try
                {
                    foreach (string file in files)
                    {
                        if (_cancel) break;
                        string rel = "";
                        if (baseDir.Length > 0 && file.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                        {
                            rel = file.Substring(baseDir.Length).TrimStart('\\', '/');
                        }
                        string dstDir = remoteDir;
                        if (rel.Length > 0)
                        {
                            string relDir = Path.GetDirectoryName(rel);
                            if (!string.IsNullOrEmpty(relDir)) dstDir = Path.Combine(remoteDir, relDir);
                        }
                        Log("[" + s.DisplayName + "] 上传 " + Path.GetFileName(file));
                        long fileDoneBase = doneBytes;
                        try
                        {
                            SmbChannel.UploadFile(s, file, dstDir, delegate(long done, long len)
                            {
                                SetProgress(0, (int)Math.Min(int.MaxValue, grandTotal),
                                            (int)Math.Min(int.MaxValue, fileDoneBase + done));
                                Status(s.DisplayName + ": " + Path.GetFileName(file) + " " + FormatBytes(done) + "/" + FormatBytes(len));
                            });
                        }
                        catch (Exception ex)
                        {
                            Log("[" + s.DisplayName + "] 失败: " + ex.Message);
                        }
                        try { doneBytes = fileDoneBase + new FileInfo(file).Length; } catch { doneBytes = fileDoneBase; }
                    }
                    if (!_cancel) { okServers++; Log("[" + s.DisplayName + "] 完成 ✓"); }
                }
                finally
                {
                    SmbChannel.Disconnect(s);
                }
            }
            string summary = string.Format("传输结束: 成功 {0} 台, 失败 {1} 台{2}",
                okServers, failServers, _cancel ? " (已取消)" : "");
            Log(summary);
            Status(summary);
            UiInvoke(delegate { _btnStart.Enabled = true; SetProgress(0, 100, 100); });
        }

        #endregion

        #region 下载

        private void btnDownload_Click(object sender, EventArgs e)
        {
            List<ServerInfo> targets = GetSelectedServers();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "请先勾选一台源服务器。", "提示");
                return;
            }
            string remoteFile = _txtRemoteFile.Text.Trim();
            string localDir = _txtLocalSave.Text.Trim();
            if (remoteFile.Length == 0 || localDir.Length == 0)
            {
                MessageBox.Show(this, "请填写远端文件路径与本地保存目录。", "提示");
                return;
            }
            if (_worker != null && _worker.IsAlive) return;

            ServerInfo s = targets[0];
            string dst = Path.Combine(localDir, Path.GetFileName(remoteFile));
            _cancel = false;

            _worker = new Thread(delegate()
            {
                Log("[" + s.DisplayName + "] 下载 " + remoteFile + " → " + dst);
                try
                {
                    SmbChannel.Connect(s);
                    try
                    {
                        SmbChannel.DownloadFile(s, remoteFile, dst, delegate(long done, long len)
                        {
                            SetProgress(0, (int)Math.Min(int.MaxValue, len), (int)Math.Min(int.MaxValue, done));
                            Status("下载中 " + FormatBytes(done) + "/" + FormatBytes(len));
                        });
                        Log("[" + s.DisplayName + "] 下载完成 ✓ → " + dst);
                        Status("下载完成: " + dst);
                    }
                    finally { SmbChannel.Disconnect(s); }
                }
                catch (Exception ex)
                {
                    Log("[" + s.DisplayName + "] 下载失败: " + ex.Message);
                    Status("下载失败: " + ex.Message);
                }
            });
            _worker.IsBackground = true;
            _worker.Start();
        }

        #endregion

        #region 线程安全的 UI 更新

        private void UiInvoke(MethodInvoker action)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        private void Log(string msg)
        {
            string line = string.Format("{0} {1}", DateTime.Now.ToString("HH:mm:ss"), msg);
            UiInvoke(delegate
            {
                _txtLog.AppendText(line + Environment.NewLine);
                AppLogger.Info("[文件传输] " + msg);
            });
        }

        private void Status(string msg)
        {
            UiInvoke(delegate { _lblStatus.Text = msg; });
        }

        private void SetProgress(int min, int max, int value)
        {
            UiInvoke(delegate
            {
                if (max <= 0) max = 1;
                _progress.Minimum = min;
                _progress.Maximum = max;
                if (value < min) value = min;
                if (value > max) value = max;
                _progress.Value = value;
            });
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024 * 1024 * 1024) return string.Format("{0:F2}GB", bytes / 1024.0 / 1024 / 1024);
            if (bytes >= 1024 * 1024) return string.Format("{0:F2}MB", bytes / 1024.0 / 1024);
            if (bytes >= 1024) return string.Format("{0:F1}KB", bytes / 1024.0);
            return bytes + "B";
        }

        #endregion
    }
}
