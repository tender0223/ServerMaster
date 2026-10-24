using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Rdp;
using ServerMaster.Services;
using ServerMaster.Services.FileTransfer;

namespace ServerMaster.UI
{
    /// <summary>主窗口：菜单/工具栏 + 服务器列表 + 多画面网格 + 状态栏。</summary>
    public class MainForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly ConnectionManager _conn;
        private readonly InputBroadcastService _broadcast;
        private ServerListPanel _listPanel;
        private SessionGridPanel _grid;
        private readonly Dictionary<string, SessionTile> _tiles = new Dictionary<string, SessionTile>();

        private ToolStrip _toolbar;
        private ToolStripButton _btnBroadcast;
        private ToolStripComboBox _cmbLayout;
        private StatusStrip _status;
        private ToolStripStatusLabel _lblConn;
        private ToolStripStatusLabel _lblBcast;
        private ToolStripStatusLabel _lblMsg;
        private Timer _uiTimer;

        public MainForm()
        {
            Text = "ServerMaster 多服务器批量管理";
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = new Size(960, 620);
            Size = new Size(1400, 900);

            _cfg = ConfigStore.Load();
            _conn = new ConnectionManager();
            _broadcast = new InputBroadcastService();
            Application.AddMessageFilter(_broadcast);

            BuildMenu();
            BuildToolbar();
            BuildBody();
            BuildStatus();

            _listPanel.Rebind(_cfg);
            _grid.MaxColumns = _cfg.Settings.MaxColumns;
            SelectLayoutCombo(_cfg.Settings.MaxColumns);

            _uiTimer = new Timer();
            _uiTimer.Interval = 2000;
            _uiTimer.Tick += delegate { UpdateStatus(); };
            _uiTimer.Start();

            Load += delegate
            {
                if (_cfg.Settings.WindowWidth > 0 && _cfg.Settings.WindowHeight > 0)
                    Size = new Size(_cfg.Settings.WindowWidth, _cfg.Settings.WindowHeight);
                UpdateStatus();
            };
        }

        #region UI 构建

        private void BuildMenu()
        {
            MenuStrip menu = new MenuStrip();

            ToolStripMenuItem mFile = new ToolStripMenuItem("文件(&F)");
            mFile.DropDownItems.Add("导入配置…", null, delegate { ImportConfig(); });
            mFile.DropDownItems.Add("导出配置…", null, delegate { ExportConfig(); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("退出", null, delegate { Close(); });
            menu.Items.Add(mFile);

            ToolStripMenuItem mTools = new ToolStripMenuItem("工具(&T)");
            mTools.DropDownItems.Add("从 FinalShell 导入…", null, delegate { ImportFinalShell(); });
            mTools.DropDownItems.Add("批量文件传输…", null, delegate { OpenTransfer(); });
            mTools.DropDownItems.Add("设置拖放上传目录…", null, delegate { SetDragDropDir(); });
            mTools.DropDownItems.Add("设置默认远端分辨率…", null, delegate { SetDefaultResolution(); });
            mTools.DropDownItems.Add("打开配置目录", null, delegate { OpenDir(ConfigStore.ConfigDir); });
            mTools.DropDownItems.Add("打开日志目录", null,
                delegate { OpenDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ServerMaster", "logs")); });
            menu.Items.Add(mTools);

            ToolStripMenuItem mHelp = new ToolStripMenuItem("帮助(&H)");
            mHelp.DropDownItems.Add("查看设计文档", null, delegate
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new string[]
                {
                    Path.Combine(baseDir, "docs", "设计文档.md"),
                    Path.Combine(baseDir, "设计文档.md"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "docs", "设计文档.md"))
                };
                foreach (string c in candidates)
                {
                    if (File.Exists(c)) { Process.Start(c); return; }
                }
                MessageBox.Show(this, "设计文档未随程序分发，可在 GitHub 仓库 docs/ 目录查看", "提示");
            });
            mHelp.DropDownItems.Add("关于 ServerMaster", null, delegate
            {
                MessageBox.Show(this,
                    "ServerMaster 多服务器批量管理工具 v0.2.1\n\n" +
                    "多画面 RDP 同屏 · 键鼠同步广播 · 批量/直传文件传输\n" +
                    "FinalShell 导入 · 按组/批量删除\n" +
                    "基于微软官方 RDP ActiveX (mstscax)\n" +
                    ".NET Framework 4.8 · Windows 10/Server 2016+\n\n" +
                    "开源许可: MIT · 详见 LICENSE 文件",
                    "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            menu.Items.Add(mHelp);

            MainMenuStrip = menu;
            Controls.Add(menu);
        }

        private void BuildToolbar()
        {
            _toolbar = new ToolStrip();
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;
            _toolbar.Dock = DockStyle.Top;

            _toolbar.Items.Add("＋ 添加服务器", null, delegate { ShowEdit(null); });
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add("▶ 全部连接", null, delegate { ConnectCheckedOrAll(); });
            _toolbar.Items.Add("■ 全部断开", null, delegate { DisconnectAll(); });
            _toolbar.Items.Add(new ToolStripSeparator());

            ToolStripLabel lblLayout = new ToolStripLabel("布局:");
            _toolbar.Items.Add(lblLayout);
            _cmbLayout = new ToolStripComboBox();
            _cmbLayout.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLayout.Width = 90;
            _cmbLayout.Items.Add("自动");
            _cmbLayout.Items.Add("1 列");
            _cmbLayout.Items.Add("2 列");
            _cmbLayout.Items.Add("3 列");
            _cmbLayout.Items.Add("4 列");
            _cmbLayout.SelectedIndex = 0;
            _cmbLayout.SelectedIndexChanged += delegate
            {
                int cols = _cmbLayout.SelectedIndex; // 0=自动
                _grid.MaxColumns = cols;
                _cfg.Settings.MaxColumns = cols;
                ConfigStore.Save(_cfg);
            };
            _toolbar.Items.Add(_cmbLayout);
            _toolbar.Items.Add(new ToolStripSeparator());

            _btnBroadcast = new ToolStripButton("🔴 广播输入: 关");
            _btnBroadcast.CheckOnClick = true;
            _btnBroadcast.Click += delegate
            {
                _broadcast.Enabled = _btnBroadcast.Checked;
                _btnBroadcast.Text = _btnBroadcast.Checked ? "🔴 广播输入: 开" : "🔴 广播输入: 关";
                RefreshBroadcastTargets();
                AppLogger.Info("广播输入 " + (_btnBroadcast.Checked ? "开启" : "关闭"));
                if (_btnBroadcast.Checked && _broadcast.TargetCount == 0)
                    ShowMsg("广播已开启：请再勾选目标分格标题栏的「广播」复选框，之后在主控分格的键鼠会同步到所有勾选分格");
            };
            _toolbar.Items.Add(_btnBroadcast);
            _toolbar.Items.Add("全选广播", null, delegate { SetAllBroadcast(true); });
            _toolbar.Items.Add("清除广播", null, delegate { SetAllBroadcast(false); });
            _toolbar.Items.Add(new ToolStripSeparator());

            _toolbar.Items.Add("📦 文件传输", null, delegate { OpenTransfer(); });

            Controls.Add(_toolbar);
        }

        private void BuildBody()
        {
            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterDistance = 260;

            _listPanel = new ServerListPanel();
            _listPanel.AddRequested += delegate { ShowEdit(null); };
            _listPanel.EditRequested += delegate(ServerInfo s) { ShowEdit(s); };
            _listPanel.DeleteRequested += delegate(ServerInfo s) { DeleteServer(s); };
            _listPanel.ConnectRequested += delegate(ServerInfo s) { ConnectServer(s); };
            _listPanel.DisconnectRequested += delegate(ServerInfo s) { DisconnectServer(s); };
            _listPanel.ConnectGroupRequested += delegate(string g) { ConnectGroupServers(g); };
            _listPanel.DeleteGroupRequested += delegate(string g) { DeleteGroupServers(g); };
            _listPanel.DeleteAllRequested += delegate { DeleteAllServers(); };
            _listPanel.DeleteCheckedRequested += delegate(List<ServerInfo> targets) { DeleteCheckedServers(targets); };
            split.Panel1.Controls.Add(_listPanel);

            _grid = new SessionGridPanel();
            split.Panel2.Controls.Add(_grid);

            Controls.Add(split);
            split.BringToFront();
        }

        private void BuildStatus()
        {
            _status = new StatusStrip();
            _lblConn = new ToolStripStatusLabel("已连接 0/0");
            _lblBcast = new ToolStripStatusLabel("广播: 关");
            _lblMsg = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _status.Items.Add(_lblConn);
            _status.Items.Add(new ToolStripStatusLabel("|"));
            _status.Items.Add(_lblBcast);
            _status.Items.Add(new ToolStripStatusLabel("|"));
            _status.Items.Add(_lblMsg);
            Controls.Add(_status);
        }

        private void SelectLayoutCombo(int maxColumns)
        {
            if (maxColumns >= 0 && maxColumns < _cmbLayout.Items.Count)
                _cmbLayout.SelectedIndex = maxColumns;
        }

        #endregion

        #region 连接管理

        private void ConnectServer(ServerInfo s)
        {
            if (string.IsNullOrEmpty(s.Host))
            {
                ShowMsg("服务器 [" + s.DisplayName + "] 缺少主机地址");
                return;
            }

            RdpSession existing = _conn.FindByServer(s);
            if (existing != null)
            {
                if (existing.State == SessionState.Connected)
                {
                    ShowMsg("[" + s.DisplayName + "] 已连接");
                    return;
                }
                // 存在但断开/失败 → 关掉旧会话重连
                CloseSession(existing);
            }

            // 远端分辨率固定（服务器桌面分辨率不被改变，正在运行的程序窗口不受影响），
            // 画面按分格大小等比缩放显示（SmartSizing 纯显示端缩放）
            int w = s.DesktopWidth > 0 ? s.DesktopWidth : _cfg.Settings.DesktopWidth;
            int h = s.DesktopHeight > 0 ? s.DesktopHeight : _cfg.Settings.DesktopHeight;

            RdpSession session = _conn.Create(s);
            session.StateChanged += session_StateChanged;
            session.DisconnectedWithReason += session_DisconnectedWithReason;

            SessionTile tile = new SessionTile(session);
            tile.CloseRequested += delegate(SessionTile t) { CloseSession(t.Session); };
            tile.ZoomToggled += delegate(SessionTile t) { _grid.ToggleZoom(t); };
            tile.MasterActivated += tile_MasterActivated;
            tile.FilesDropped += tile_FilesDropped;
            tile.BroadcastTargetChanged += delegate(SessionTile t) { RefreshBroadcastTargets(); };

            _tiles[s.Id] = tile;
            _grid.AddTile(tile);
            tile.AttachRdpControl();
            tile.SetState(SessionState.Connecting, "");

            session.Connect(w, h);
            ShowMsg("正在连接 [" + s.DisplayName + "]…");
        }

        private void ConnectCheckedOrAll()
        {
            List<ServerInfo> targets = _listPanel.GetCheckedServers();
            if (targets.Count == 0) targets = _cfg.Servers;
            int n = 0;
            foreach (ServerInfo s in targets)
            {
                ConnectServer(s);
                n++;
                if (n >= 16) break; // 同屏建议上限
            }
        }

        private void DisconnectServer(ServerInfo s)
        {
            RdpSession session = _conn.FindByServer(s);
            if (session != null) CloseSession(session);
        }

        private void DisconnectAll()
        {
            if (_conn.Sessions.Count > 0 && _cfg.Settings.ConfirmCloseAll)
            {
                DialogResult r = MessageBox.Show(this,
                    "确定断开全部 " + _conn.Sessions.Count + " 个会话？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            RdpSession[] sessions = new RdpSession[_conn.Sessions.Count];
            _conn.Sessions.CopyTo(sessions, 0);
            foreach (RdpSession s in sessions) CloseSession(s);
            UpdateStatus();
        }

        /// <summary>连接某分组下的全部服务器。</summary>
        private void ConnectGroupServers(string group)
        {
            int n = 0;
            foreach (ServerInfo s in _cfg.Servers)
            {
                if (s.Group != group) continue;
                ConnectServer(s);
                n++;
                if (n >= 16)
                {
                    ShowMsg("同屏建议上限 16 路，已停止继续连接");
                    break;
                }
            }
            if (n == 0) ShowMsg("分组 [" + group + "] 内没有服务器");
        }

        /// <summary>删除某分组下的全部服务器（同时断开其会话）。</summary>
        private void DeleteGroupServers(string group)
        {
            List<ServerInfo> targets = new List<ServerInfo>();
            foreach (ServerInfo s in _cfg.Servers)
                if (s.Group == group) targets.Add(s);

            if (targets.Count == 0)
            {
                _cfg.Groups.Remove(group);
                ConfigStore.Save(_cfg);
                _listPanel.Rebind(_cfg);
                ShowMsg("已移除空分组 [" + group + "]");
                return;
            }

            DialogResult r = MessageBox.Show(this,
                "确定删除分组 [" + group + "] 下的 " + targets.Count + " 台服务器？\n对应会话将同时断开，此操作不可撤销。",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            foreach (ServerInfo s in targets)
            {
                RdpSession session = _conn.FindByServer(s);
                if (session != null) CloseSession(session);
                _cfg.Servers.Remove(s);
            }
            bool stillUsed = false;
            foreach (ServerInfo s in _cfg.Servers)
                if (s.Group == group) stillUsed = true;
            if (!stillUsed) _cfg.Groups.Remove(group);

            ConfigStore.Save(_cfg);
            _listPanel.Rebind(_cfg);
            ShowMsg("已删除分组 [" + group + "] 的 " + targets.Count + " 台服务器");
        }

        /// <summary>删除勾选的服务器（勾选组节点 = 勾选组内全部；无勾选时删除当前选中）。</summary>
        private void DeleteCheckedServers(List<ServerInfo> targets)
        {
            DialogResult r = MessageBox.Show(this,
                "确定删除选中的 " + targets.Count + " 台服务器？\n对应会话将同时断开，此操作不可撤销。",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            foreach (ServerInfo s in targets)
            {
                RdpSession session = _conn.FindByServer(s);
                if (session != null) CloseSession(session);
                _cfg.Servers.Remove(s);
            }
            // 清理已无服务器的分组
            List<string> empty = new List<string>();
            foreach (string g in _cfg.Groups)
            {
                bool used = false;
                foreach (ServerInfo s in _cfg.Servers)
                    if (s.Group == g) { used = true; break; }
                if (!used) empty.Add(g);
            }
            foreach (string g in empty) _cfg.Groups.Remove(g);
            if (_cfg.Groups.Count == 0) _cfg.Groups.Add("默认分组");

            ConfigStore.Save(_cfg);
            _listPanel.Rebind(_cfg);
            ShowMsg("已删除 " + targets.Count + " 台服务器");
        }

        /// <summary>删除全部服务器（同时断开全部会话并清空分组列表）。</summary>
        private void DeleteAllServers()
        {
            if (_cfg.Servers.Count == 0)
            {
                ShowMsg("服务器清单已为空");
                return;
            }
            DialogResult r = MessageBox.Show(this,
                "确定删除全部 " + _cfg.Servers.Count + " 台服务器？\n对应会话将同时断开，分组列表同时清空，此操作不可撤销。",
                "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            ServerInfo[] all = _cfg.Servers.ToArray();
            foreach (ServerInfo s in all)
            {
                RdpSession session = _conn.FindByServer(s);
                if (session != null) CloseSession(session);
            }
            // 清理不对应任何清单条目的孤儿会话
            RdpSession[] leftovers = new RdpSession[_conn.Sessions.Count];
            _conn.Sessions.CopyTo(leftovers, 0);
            foreach (RdpSession s in leftovers) CloseSession(s);

            _cfg.Servers.Clear();
            _cfg.Groups.Clear();
            _cfg.Groups.Add("默认分组");
            ConfigStore.Save(_cfg);
            _listPanel.Rebind(_cfg);
            ShowMsg("已删除全部服务器");
        }

        private void CloseSession(RdpSession session)
        {
            SessionTile tile;
            if (_tiles.TryGetValue(session.Server.Id, out tile))
            {
                _tiles.Remove(session.Server.Id);
            }
            _conn.Close(session);
            if (tile != null) _grid.RemoveTile(tile);
            RefreshBroadcastTargets();
            UpdateStatus();
        }

        private void session_StateChanged(RdpSession session)
        {
            SessionTile tile;
            if (!_tiles.TryGetValue(session.Server.Id, out tile)) return;
            if (tile.IsHandleCreated)
            {
                tile.BeginInvoke((MethodInvoker)delegate
                {
                    tile.SetState(session.State, session.StateNote);
                    if (session.State == SessionState.Failed)
                        ShowMsg("[" + session.Server.DisplayName + "] 连接失败: " + session.StateNote);
                    else if (session.State == SessionState.Connected)
                        ShowMsg("[" + session.Server.DisplayName + "] 已连接");
                });
            }
            else
            {
                tile.SetState(session.State, session.StateNote);
            }
            RefreshBroadcastTargets();
        }

        private void session_DisconnectedWithReason(RdpSession session, int reason)
        {
            AppLogger.Info(string.Format("[{0}] 断开原因代码 {1}", session.Server.DisplayName, reason));
        }

        #endregion

        #region 广播

        private void tile_MasterActivated(SessionTile tile)
        {
            foreach (KeyValuePair<string, SessionTile> kv in _tiles)
                if (kv.Value != tile) kv.Value.ClearMasterHighlight();
            _broadcast.SetMaster(tile.Session);
            RefreshBroadcastTargets();
        }

        private void tile_FilesDropped(SessionTile tile, string[] paths)
        {
            // 直接 SMB 完整传输到目标目录（不再是只放剪贴板）；失败才降级为剪贴板方案
            ServerInfo server = tile.Session.Server;
            string remoteDir = string.IsNullOrEmpty(_cfg.Settings.DragDropRemoteDir)
                ? "C:\\Users\\Public\\Desktop" : _cfg.Settings.DragDropRemoteDir;
            ShowMsg("[" + server.DisplayName + "] 开始上传 " + paths.Length + " 项 → " + remoteDir + " …");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    SmbChannel.Connect(server);
                    try
                    {
                        int count = 0;
                        foreach (string p in paths)
                            UploadPathRecursive(server, p, remoteDir, ref count);
                        RunOnUi(delegate
                        {
                            ShowMsg("[" + server.DisplayName + "] 上传完成 ✓ 共 " + count +
                                    " 个文件 → " + remoteDir + "（远端刷新即可看到）");
                        });
                    }
                    finally { SmbChannel.Disconnect(server); }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("拖放直传失败(" + server.DisplayName + ")", ex);
                    RunOnUi(delegate
                    {
                        ShowMsg("[" + server.DisplayName + "] 直接上传失败: " + ex.Message +
                                " —— 已改存剪贴板，可在远端 Ctrl+V 粘贴");
                        try { ClipboardChannel.PushFiles(paths); } catch { }
                    });
                }
            });
        }

        /// <summary>递归上传文件/文件夹（保持相对目录结构）。</summary>
        private void UploadPathRecursive(ServerInfo server, string path, string remoteDir, ref int count)
        {
            if (System.IO.Directory.Exists(path))
            {
                string sub = Path.Combine(remoteDir, Path.GetFileName(path));
                foreach (string f in System.IO.Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories))
                {
                    string rel = f.Substring(path.Length).TrimStart('\\', '/');
                    string relDir = System.IO.Path.GetDirectoryName(rel);
                    string dstDir = (relDir == null || relDir.Length == 0) ? sub : Path.Combine(sub, relDir);
                    ShowMsgUi("[" + server.DisplayName + "] 上传中 " + Path.GetFileName(f));
                    SmbChannel.UploadFile(server, f, dstDir, null);
                    count++;
                }
            }
            else if (System.IO.File.Exists(path))
            {
                ShowMsgUi("[" + server.DisplayName + "] 上传中 " + Path.GetFileName(path));
                SmbChannel.UploadFile(server, path, remoteDir, null);
                count++;
            }
        }

        private void ShowMsgUi(string msg)
        {
            RunOnUi(delegate { ShowMsg(msg); });
        }

        private void RunOnUi(MethodInvoker action)
        {
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch { }
        }

        /// <summary>设置默认远端桌面分辨率（固定值；连接时不再改变服务器桌面分辨率）。</summary>
        private void SetDefaultResolution()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "默认远端桌面分辨率";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(400, 130);
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                Label l1 = new Label();
                l1.Text = "连接时使用的固定分辨率（画面按分格等比缩放显示）:";
                l1.SetBounds(12, 10, 380, 20);
                dlg.Controls.Add(l1);

                Label lW = new Label();
                lW.Text = "宽:";
                lW.SetBounds(12, 42, 24, 20);
                dlg.Controls.Add(lW);
                TextBox txtW = new TextBox();
                txtW.SetBounds(40, 39, 70, 24);
                txtW.Text = _cfg.Settings.DesktopWidth.ToString();
                dlg.Controls.Add(txtW);

                Label lH = new Label();
                lH.Text = "高:";
                lH.SetBounds(124, 42, 24, 20);
                dlg.Controls.Add(lH);
                TextBox txtH = new TextBox();
                txtH.SetBounds(152, 39, 70, 24);
                txtH.Text = _cfg.Settings.DesktopHeight.ToString();
                dlg.Controls.Add(txtH);

                Label l2 = new Label();
                l2.Text = "建议设成你平时远程桌面使用的分辨率，程序窗口位置即可保持不变";
                l2.AutoSize = true;
                l2.ForeColor = Color.FromArgb(110, 110, 118);
                l2.SetBounds(12, 70, 380, 18);
                dlg.Controls.Add(l2);

                Button ok = new Button();
                ok.Text = "确定";
                ok.SetBounds(212, 96, 80, 26);
                ok.DialogResult = DialogResult.OK;
                dlg.Controls.Add(ok);

                Button ca = new Button();
                ca.Text = "取消";
                ca.SetBounds(300, 96, 80, 26);
                ca.DialogResult = DialogResult.Cancel;
                dlg.Controls.Add(ca);
                dlg.AcceptButton = ok;
                dlg.CancelButton = ca;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    int w, h;
                    if (int.TryParse(txtW.Text.Trim(), out w) && int.TryParse(txtH.Text.Trim(), out h) &&
                        w >= 640 && w <= 4096 && h >= 480 && h <= 2160)
                    {
                        _cfg.Settings.DesktopWidth = w;
                        _cfg.Settings.DesktopHeight = h;
                        ConfigStore.Save(_cfg);
                        ShowMsg("默认远端分辨率已设为 " + w + "×" + h + "（对之后的新连接生效）");
                    }
                    else
                    {
                        MessageBox.Show(this, "分辨率无效（宽 640-4096，高 480-2160）", "验证",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        /// <summary>设置拖放直传的远端目标目录。</summary>
        private void SetDragDropDir()
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "拖放上传目标目录";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(430, 112);
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                Label l = new Label();
                l.Text = "拖文件到分格时，直接完整传输(SMB)到服务器的该目录:";
                l.SetBounds(12, 10, 410, 20);
                dlg.Controls.Add(l);

                TextBox t = new TextBox();
                t.SetBounds(12, 36, 406, 24);
                t.Text = string.IsNullOrEmpty(_cfg.Settings.DragDropRemoteDir)
                    ? "C:\\Users\\Public\\Desktop" : _cfg.Settings.DragDropRemoteDir;
                dlg.Controls.Add(t);

                Button ok = new Button();
                ok.Text = "确定";
                ok.SetBounds(242, 70, 80, 26);
                ok.DialogResult = DialogResult.OK;
                dlg.Controls.Add(ok);

                Button ca = new Button();
                ca.Text = "取消";
                ca.SetBounds(330, 70, 80, 26);
                ca.DialogResult = DialogResult.Cancel;
                dlg.Controls.Add(ca);
                dlg.AcceptButton = ok;
                dlg.CancelButton = ca;

                if (dlg.ShowDialog(this) == DialogResult.OK && t.Text.Trim().Length > 0)
                {
                    _cfg.Settings.DragDropRemoteDir = t.Text.Trim();
                    ConfigStore.Save(_cfg);
                    ShowMsg("拖放上传目录已设为 " + _cfg.Settings.DragDropRemoteDir);
                }
            }
        }

        private void RefreshBroadcastTargets()
        {
            List<RdpSession> targets = new List<RdpSession>();
            foreach (SessionTile t in _grid.Tiles)
            {
                if (t.IsBroadcastTarget && t.Session.State == SessionState.Connected)
                    targets.Add(t.Session);
            }
            _broadcast.SetTargets(targets);
            UpdateStatus();
        }

        private void SetAllBroadcast(bool value)
        {
            foreach (SessionTile t in _grid.Tiles)
                t.SetBroadcastTarget(value, false);
            RefreshBroadcastTargets();
        }

        #endregion

        #region 服务器配置编辑

        private void ImportFinalShell()
        {
            using (ImportFinalShellForm f = new ImportFinalShellForm(_cfg))
            {
                if (f.ShowDialog(this) == DialogResult.OK && f.ImportedCount > 0)
                {
                    try { ConfigStore.Save(_cfg); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "导入内容已加入清单，但保存配置失败: " + ex.Message,
                            "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    _listPanel.Rebind(_cfg);
                    ShowMsg("FinalShell 导入完成: " + f.ImportedCount + " 台" +
                            (f.SkippedDuplicateCount > 0 ? " (跳过重复 " + f.SkippedDuplicateCount + " 台)" : ""));
                }
            }
        }

        private void ShowEdit(ServerInfo existing)
        {
            using (ServerEditDialog dlg = new ServerEditDialog(existing, _cfg.Groups))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result == null) return;
                ServerInfo result = dlg.Result;

                if (existing == null)
                {
                    _cfg.Servers.Add(result);
                }
                else
                {
                    int idx = _cfg.Servers.IndexOf(existing);
                    if (idx >= 0) _cfg.Servers[idx] = result; else _cfg.Servers.Add(result);
                    // 主机/端口/凭据变化 → 旧会话失效，断开重连由用户操作
                    RdpSession session = _conn.FindByServer(result);
                    if (session != null) CloseSession(session);
                }
                if (!_cfg.Groups.Contains(result.Group)) _cfg.Groups.Add(result.Group);

                ConfigStore.Save(_cfg);
                _listPanel.Rebind(_cfg);
                ShowMsg(existing == null ? "已添加 [" + result.DisplayName + "]" : "已保存 [" + result.DisplayName + "]");
            }
        }

        private void DeleteServer(ServerInfo s)
        {
            DialogResult r = MessageBox.Show(this, "确定删除服务器 [" + s.DisplayName + "]？", "确认",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            RdpSession session = _conn.FindByServer(s);
            if (session != null) CloseSession(session);
            _cfg.Servers.Remove(s);
            ConfigStore.Save(_cfg);
            _listPanel.Rebind(_cfg);
        }

        private void ImportConfig()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "导入配置(将覆盖当前清单)";
                d.Filter = "ServerMaster 配置|config.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    DisconnectAll();
                    File.Copy(d.FileName, ConfigStore.ConfigFile, true);
                    ReloadConfig();
                    ShowMsg("配置已导入(注意: 密码密文仅原机器可解，需重新录入)");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导入失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ExportConfig()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "导出配置";
                d.FileName = "config.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ConfigStore.Save(_cfg);
                    File.Copy(ConfigStore.ConfigFile, d.FileName, true);
                    MessageBox.Show(this,
                        "已导出。注意: 服务器密码为 DPAPI 加密，仅在本机本 Windows 账户下可解，导入其他机器后需重新录入密码。",
                        "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ReloadConfig()
        {
            AppConfig fresh = ConfigStore.Load();
            _cfg.Servers = fresh.Servers;
            _cfg.Groups = fresh.Groups;
            _cfg.Settings = fresh.Settings;
            _grid.MaxColumns = _cfg.Settings.MaxColumns;
            SelectLayoutCombo(_cfg.Settings.MaxColumns);
            _listPanel.Rebind(_cfg);
        }

        #endregion

        #region 杂项

        private void OpenTransfer()
        {
            FileTransferForm f = new FileTransferForm(_cfg);
            f.Show(this);
        }

        private void OpenDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "错误");
            }
        }

        private void UpdateStatus()
        {
            int total = _conn.Sessions.Count;
            _lblConn.Text = "已连接 " + _conn.ConnectedCount + "/" + total;
            _lblBcast.Text = "广播: " + (_broadcast.Enabled ? "开 (" + _broadcast.TargetCount + " 目标)" : "关");
        }

        private void ShowMsg(string msg)
        {
            _lblMsg.Text = msg;
            AppLogger.Info(msg);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            int connected = 0;
            foreach (RdpSession s in _conn.Sessions)
                if (s.State == SessionState.Connected) connected++;
            if (connected > 0 && e.CloseReason == CloseReason.UserClosing && _cfg.Settings.ConfirmCloseAll)
            {
                DialogResult r = MessageBox.Show(this,
                    "还有 " + connected + " 个会话连接中，退出将断开全部连接。确定退出？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
            }

            _cfg.Settings.WindowWidth = Width;
            _cfg.Settings.WindowHeight = Height;
            try { ConfigStore.Save(_cfg); } catch { }

            Application.RemoveMessageFilter(_broadcast);
            _conn.CloseAll();
        }

        #endregion
    }
}
