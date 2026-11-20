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
        private SplitContainer _split;
        private readonly Dictionary<string, SessionTile> _tiles = new Dictionary<string, SessionTile>();

        private ToolStrip _toolbar;
        private ToolStripButton _btnBroadcast;
        private ToolStripButton _btnSidebar;
        private ToolStripMenuItem _miShowSidebar;
        private ToolStripMenuItem[] _miTitleModes;
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
            LoadAppIcon();

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
                RestoreWindowPlacement();
                SetSidebarVisible(_cfg.Settings.ShowServerPanel, false);
                BeginInvoke((MethodInvoker)delegate { ApplyServerPanelWidth(); });
                UpdateStatus();
            };
        }

        /// <summary>优先用内嵌 ico（多尺寸），回退到 exe 关联图标。</summary>
        private void LoadAppIcon()
        {
            try
            {
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("ServerMaster.app.ico"))
                {
                    if (s != null) { Icon = new System.Drawing.Icon(s); return; }
                }
            }
            catch { }
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
        }

        #region UI 构建

        private void BuildMenu()
        {
            MenuStrip menu = new MenuStrip();

            ToolStripMenuItem mFile = new ToolStripMenuItem("文件(&F)");
            mFile.DropDownItems.Add("导入配置（.json / .smc）…", null, delegate { ImportConfig(); });
            ToolStripMenuItem mExport = new ToolStripMenuItem("导出配置");
            mExport.DropDownItems.Add("普通导出（换机需重输密码）…", null, delegate { ExportStandard(); });
            mExport.DropDownItems.Add("便携导出（换机导入即用）…", null, delegate { ExportPortable(); });
            mFile.DropDownItems.Add(mExport);
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("删除全部服务器…", null, delegate { DeleteAllServers(); });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("退出", null, delegate { Close(); });
            menu.Items.Add(mFile);

            ToolStripMenuItem mTools = new ToolStripMenuItem("工具(&T)");
            mTools.DropDownItems.Add("批量文件传输…", null, delegate { OpenTransfer(); });
            mTools.DropDownItems.Add("设置拖放上传目录…", null, delegate { SetDragDropDir(); });
            mTools.DropDownItems.Add("设置默认远端分辨率…", null, delegate { SetDefaultResolution(); });
            mTools.DropDownItems.Add("打开配置目录", null, delegate { OpenDir(ConfigStore.ConfigDir); });
            mTools.DropDownItems.Add("打开日志目录", null,
                delegate { OpenDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ServerMaster", "logs")); });
            menu.Items.Add(mTools);

            ToolStripMenuItem mView = new ToolStripMenuItem("视图(&V)");
            _miShowSidebar = new ToolStripMenuItem("显示服务器列表栏");
            _miShowSidebar.CheckOnClick = true;
            _miShowSidebar.Checked = _cfg.Settings.ShowServerPanel;
            _miShowSidebar.Click += delegate { SetSidebarVisible(_miShowSidebar.Checked, true); };
            mView.DropDownItems.Add(_miShowSidebar);
            mView.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem miTitle = new ToolStripMenuItem("分格标题显示");
            BuildTitleModeMenu(miTitle);
            mView.DropDownItems.Add(miTitle);
            menu.Items.Add(mView);

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
                    "ServerMaster 多服务器批量管理工具 v0.4.1\n\n" +
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

            ToolStripDropDownButton btnAdd = new ToolStripDropDownButton("＋ 添加服务器");
            btnAdd.DropDownItems.Add("添加单台服务器…", null, delegate { ShowEdit(null); });
            btnAdd.DropDownItems.Add("文本批量添加…", null, delegate { BulkAddServers(); });
            btnAdd.DropDownItems.Add(new ToolStripSeparator());
            btnAdd.DropDownItems.Add("从 FinalShell 导入…", null, delegate { ImportFinalShell(); });
            _toolbar.Items.Add(btnAdd);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add("▶ 连接全部", null, delegate { ConnectAllServers(); });
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

            _btnSidebar = new ToolStripButton("服务器栏");
            _btnSidebar.CheckOnClick = true;
            _btnSidebar.Checked = _cfg.Settings.ShowServerPanel;
            _btnSidebar.ToolTipText = "显示或隐藏左侧服务器列表栏";
            _btnSidebar.Click += delegate { SetSidebarVisible(_btnSidebar.Checked, true); };
            _toolbar.Items.Add(_btnSidebar);
            _toolbar.Items.Add(new ToolStripSeparator());

            _btnBroadcast = new ToolStripButton("🔴 广播输入: 关");
            _btnBroadcast.CheckOnClick = true;
            _btnBroadcast.Click += delegate
            {
                if (_btnBroadcast.Checked) _broadcast.Enable();
                else _broadcast.Disable();
                _btnBroadcast.Text = _btnBroadcast.Checked ? "🔴 广播输入: 开" : "🔴 广播输入: 关";
                RefreshBroadcastTargets();
                AppLogger.Info("广播输入 " + (_btnBroadcast.Checked ? "开启" : "关闭"));
                if (_btnBroadcast.Checked && _broadcast.TargetCount == 0)
                    ShowMsg("广播已开启：请再勾选目标分格标题栏的「广播」复选框，之后在主控分格的键鼠会同步到所有勾选分格");
            };
            _toolbar.Items.Add(_btnBroadcast);
            _toolbar.Items.Add("全选广播", null, delegate { SetAllBroadcast(true); });
            _toolbar.Items.Add("清除广播", null, delegate { SetAllBroadcast(false); });
            _toolbar.Items.Add("🧪注入测试", null, delegate { InjectionSelfTest(); });
            _toolbar.Items.Add(new ToolStripSeparator());

            _toolbar.Items.Add("📦 文件传输", null, delegate { OpenTransfer(); });

            Controls.Add(_toolbar);
        }

        private void BuildBody()
        {
            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.FixedPanel = FixedPanel.Panel1;
            _split.SplitterDistance = 260;

            _listPanel = new ServerListPanel();
            _listPanel.EditRequested += delegate(ServerInfo s) { ShowEdit(s); };
            _listPanel.DeleteRequested += delegate(ServerInfo s) { DeleteServer(s); };
            _listPanel.ConnectCheckedRequested += delegate { ConnectCheckedServers(); };
            _listPanel.ConnectRequested += delegate(ServerInfo s) { ConnectServer(s); };
            _listPanel.DisconnectRequested += delegate(ServerInfo s) { DisconnectServer(s); };
            _listPanel.DisconnectCheckedRequested += delegate(List<ServerInfo> targets) { DisconnectCheckedServers(targets); };
            _listPanel.ConnectGroupRequested += delegate(string g) { ConnectGroupServers(g); };
            _listPanel.DeleteGroupRequested += delegate(string g) { DeleteGroupServers(g); };
            _listPanel.DeleteCheckedRequested += delegate(List<ServerInfo> targets) { DeleteCheckedServers(targets); };
            _listPanel.MoveServerRequested += delegate(ServerInfo s, string g) { MoveServerToGroup(s, g); };
            _listPanel.RenameGroupRequested += delegate(string oldName, string newName) { RenameGroup(oldName, newName); };
            _split.Panel1.Controls.Add(_listPanel);

            _grid = new SessionGridPanel();
            _split.Panel2.Controls.Add(_grid);

            Controls.Add(_split);
            _split.BringToFront();
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

        private void BuildTitleModeMenu(ToolStripMenuItem parent)
        {
            string[] labels = new string[] { "备注名", "不显示", "主机名", "主机 IP + 端口" };
            _miTitleModes = new ToolStripMenuItem[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int mode = i;
                ToolStripMenuItem mi = new ToolStripMenuItem(labels[i]);
                mi.Checked = (_cfg.Settings.SessionTitleMode == mode);
                mi.Click += delegate { SetSessionTitleMode(mode); };
                _miTitleModes[i] = mi;
                parent.DropDownItems.Add(mi);
            }
        }

        /// <summary>设置分格标题显示内容：0=备注名，1=不显示，2=主机名，3=主机IP+端口。</summary>
        private void SetSessionTitleMode(int mode)
        {
            _cfg.Settings.SessionTitleMode = mode;
            if (_miTitleModes != null)
                for (int i = 0; i < _miTitleModes.Length; i++)
                    if (_miTitleModes[i] != null) _miTitleModes[i].Checked = (i == mode);

            ConfigStore.Save(_cfg);
            foreach (KeyValuePair<string, SessionTile> kv in _tiles)
                kv.Value.ApplyTitleMode(mode);
        }

        /// <summary>显示/隐藏左侧服务器列表栏（记住宽度与显隐状态）。</summary>
        private void SetSidebarVisible(bool visible, bool persist)
        {
            if (_split == null) return;
            if (visible)
            {
                _split.Panel1Collapsed = false;
                ApplyServerPanelWidth();
            }
            else
            {
                if (!_split.Panel1Collapsed) _cfg.Settings.ServerPanelWidth = _split.SplitterDistance;
                _split.Panel1Collapsed = true;
            }

            _cfg.Settings.ShowServerPanel = visible;
            if (_btnSidebar != null) _btnSidebar.Checked = visible;
            if (_miShowSidebar != null) _miShowSidebar.Checked = visible;
            if (persist) ConfigStore.Save(_cfg);
        }

        private void ApplyServerPanelWidth()
        {
            if (_split == null || _split.Panel1Collapsed || _split.Width <= 0) return;
            int w = _cfg.Settings.ServerPanelWidth;
            if (w < 120) w = 260;
            int max = _split.Width - _split.Panel2MinSize - _split.SplitterWidth;
            if (max < 120) return;
            if (w > max) w = max;
            try { _split.SplitterDistance = w; } catch { }
        }

        /// <summary>恢复上次的窗口位置/尺寸/最大化状态；位置不在任何屏幕内时交给系统默认摆放。</summary>
        private void RestoreWindowPlacement()
        {
            int w = _cfg.Settings.WindowWidth;
            int h = _cfg.Settings.WindowHeight;
            if (w > 0 && h > 0) Size = new Size(w, h);

            if (_cfg.Settings.WindowPositionSaved)
            {
                Rectangle r = new Rectangle(_cfg.Settings.WindowLeft, _cfg.Settings.WindowTop, Width, Height);
                if (IsBoundsVisibleOnScreen(r))
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = new Point(r.Left, r.Top);
                }
            }
            if (_cfg.Settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        private static bool IsBoundsVisibleOnScreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle inter = Rectangle.Intersect(screen.WorkingArea, bounds);
                if (inter.Width >= 80 && inter.Height >= 40) return true;
            }
            return false;
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
            tile.ApplyTitleMode(_cfg.Settings.SessionTitleMode);
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

        /// <summary>一键连接左侧已勾选的服务器；未勾选任何服务器时给出提示。</summary>
        private void ConnectCheckedServers()
        {
            List<ServerInfo> targets = _listPanel.GetCheckedServers();
            if (targets.Count == 0)
            {
                ShowMsg("请先在左侧勾选要连接的服务器");
                return;
            }

            int n = 0;
            foreach (ServerInfo s in targets)
            {
                ConnectServer(s);
                n++;
                if (n >= 16)
                {
                    ShowMsg("同屏建议上限 16 路，已停止继续连接");
                    break;
                }
            }
        }

        private void ConnectAllServers()
        {
            int n = 0;
            foreach (ServerInfo s in _cfg.Servers)
            {
                ConnectServer(s);
                n++;
                if (n >= 16)
                {
                    ShowMsg("同屏建议上限 16 路，已停止继续连接");
                    break;
                }
            }
        }

        private void DisconnectServer(ServerInfo s)
        {
            RdpSession session = _conn.FindByServer(s);
            if (session != null) CloseSession(session);
        }

        /// <summary>批量断开勾选的服务器；多台时按全局设置确认。</summary>
        private void DisconnectCheckedServers(List<ServerInfo> targets)
        {
            if (targets == null || targets.Count == 0) return;
            if (targets.Count > 1 && _cfg.Settings.ConfirmCloseAll)
            {
                DialogResult r = MessageBox.Show(this,
                    "确定断开勾选的 " + targets.Count + " 台服务器？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            foreach (ServerInfo s in targets) DisconnectServer(s);
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

        /// <summary>拖拽：把服务器移动到另一个分组。</summary>
        private void MoveServerToGroup(ServerInfo server, string newGroup)
        {
            if (server.Group == newGroup) return;
            string oldGroup = server.Group;
            server.Group = newGroup;
            if (!_cfg.Groups.Contains(newGroup)) _cfg.Groups.Add(newGroup);
            bool oldUsed = false;
            foreach (ServerInfo s in _cfg.Servers)
                if (s.Group == oldGroup) { oldUsed = true; break; }
            if (!oldUsed) _cfg.Groups.Remove(oldGroup);

            ConfigStore.Save(_cfg);
            _listPanel.Rebind(_cfg);
            ShowMsg("[" + server.DisplayName + "] 已移动到分组 [" + newGroup + "]");
        }

        /// <summary>重命名分组；新名已存在时合并两组，服务器与会话全部保留。</summary>
        private void RenameGroup(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(newName) || newName == oldName) return;

            int moved = 0;
            foreach (ServerInfo s in _cfg.Servers)
                if (s.Group == oldName) moved++;

            bool targetExists = _cfg.Groups.Contains(newName);
            if (!targetExists)
            {
                foreach (ServerInfo s in _cfg.Servers)
                    if (s.Group == newName) { targetExists = true; break; }
            }

            if (targetExists)
            {
                int targetCount = 0;
                foreach (ServerInfo s in _cfg.Servers)
                    if (s.Group == newName) targetCount++;

                DialogResult r = MessageBox.Show(this,
                    "分组 [" + newName + "] 已存在，将把 [" + oldName + "] 下的 " + moved +
                    " 台服务器并入其中（该组现有 " + targetCount + " 台）。\n" +
                    "合并后服务器和已建立的连接都会保留。继续？",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            foreach (ServerInfo s in _cfg.Servers)
                if (s.Group == oldName) s.Group = newName;

            // 去掉所有旧名残留（防止历史配置里存在重名条目），新名只保留一个
            int oldIdx = _cfg.Groups.IndexOf(oldName);
            _cfg.Groups.RemoveAll(delegate(string g) { return g == oldName; });
            if (!_cfg.Groups.Contains(newName))
            {
                if (oldIdx >= 0 && oldIdx <= _cfg.Groups.Count) _cfg.Groups.Insert(oldIdx, newName);
                else _cfg.Groups.Add(newName);
            }

            ConfigStore.Save(_cfg);
            _listPanel.Rebind(_cfg);
            if (targetExists)
                ShowMsg("已合并分组: [" + oldName + "] → [" + newName + "]，保留 " + moved + " 台服务器");
            else
                ShowMsg("分组已重命名: [" + oldName + "] → [" + newName + "]");
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
            List<string> names = new List<string>();
            foreach (SessionTile t in _grid.Tiles)
            {
                if (t.IsBroadcastTarget && t.Session.State == SessionState.Connected)
                {
                    targets.Add(t.Session);
                    names.Add(t.Session.Server.DisplayName);
                }
            }
            _broadcast.SetTargets(targets);

            foreach (SessionTile t in _grid.Tiles)
            {
                bool receiving = _broadcast.Enabled && t.IsBroadcastTarget
                                 && t.Session.State == SessionState.Connected
                                 && t.Session != _broadcast.Master;
                t.SetBroadcastVisual(receiving, t.Session == _broadcast.Master);
            }

            string sig = string.Join(",", names.ToArray());
            if (sig != _lastTargetSig)
            {
                _lastTargetSig = sig;
                AppLogger.Info("广播目标(" + targets.Count + "): " + (sig.Length > 0 ? sig : "无") +
                               " | 主控: " + (_broadcast.Master == null ? "无" : _broadcast.Master.Server.DisplayName));
            }
            UpdateStatus();
        }

        private string _lastTargetSig = "#init#";

        /// <summary>
        /// 注入自测：用实验台已验证的配方（本程序前台 + SetFocus 目标控件 +
        /// keybd_event 扫描码注入；组合键转发依赖随连接自动设置的 KeyboardHookMode=1）
        /// 向每台目标发送 Ctrl+Esc → notepad → 回车。
        /// 判定肉眼可见：远端分格弹出「记事本」= 键盘注入链路正常。
        /// </summary>
        private void InjectionSelfTest()
        {
            List<RdpSession> targets = new List<RdpSession>();
            foreach (SessionTile t in _grid.Tiles)
                if (t.IsBroadcastTarget && t.Session.State == SessionState.Connected)
                    targets.Add(t.Session);
            if (targets.Count == 0)
            {
                MessageBox.Show(this,
                    "请先：1) 连接至少一台服务器 2) 勾选其分格标题栏的「广播」复选框\n然后再点注入测试。",
                    "注入测试", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 前台确认: keybd_event 注入的是系统全局输入流,本程序不在前台时按键会打进别的程序
            IntPtr fg = Win32.NativeMethods.GetForegroundWindow();
            uint pid;
            uint fgTid = fg == IntPtr.Zero ? 0 : Win32.NativeMethods.GetWindowThreadProcessId(fg, out pid);
            bool fgMine = fgTid != 0 && fgTid == Win32.NativeMethods.GetCurrentThreadId();
            AppLogger.Info(string.Format("注入测试: 前台=0x{0:X} 本程序线程={1} 前台属于本程序={2}",
                fg.ToInt64(), Win32.NativeMethods.GetCurrentThreadId(), fgMine));
            if (!fgMine)
            {
                MessageBox.Show(this,
                    "当前前台窗口不是本程序（可能被其他窗口遮挡激活）。\n请点击本程序窗口任意位置后重试，避免按键注入到其他程序。",
                    "注入测试中止", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (RdpSession s in targets)
            {
                IntPtr h = s.Handle;
                if (h == IntPtr.Zero) continue;
                ShowMsg("注入测试: [" + s.Server.DisplayName + "] 发送 Ctrl+Esc → notepad → 回车，请观察该分格远端");
                AppLogger.Info("注入测试: [" + s.Server.DisplayName + "] keybd_event 已验证配方注入 notepad 流程");
                IntPtr prevFocus = Win32.NativeMethods.GetFocus();
                Win32.NativeMethods.SetFocus(h);
                System.Threading.Thread.Sleep(300);
                try
                {
                    // Esc 先关掉可能开着的开始菜单,保证起点一致
                    RealPress(0x1B, 0x01);
                    System.Threading.Thread.Sleep(600);
                    // Ctrl+Esc 打开远端开始菜单(KHM=1 已随连接设置,组合键才会转发远端)
                    RealHold(0x11, 0x1D, false);
                    RealPress(0x1B, 0x01);
                    RealHold(0x11, 0x1D, true);
                    System.Threading.Thread.Sleep(1200);
                    foreach (char c in "notepad") RealTypeChar(c);
                    System.Threading.Thread.Sleep(600);
                    RealPress(0x0D, 0x1C); // 回车启动记事本
                    System.Threading.Thread.Sleep(500);
                }
                finally
                {
                    IntPtr restore = prevFocus != IntPtr.Zero ? prevFocus : h;
                    Win32.NativeMethods.SetFocus(restore);
                }
                System.Threading.Thread.Sleep(200);
            }
            AppLogger.Info("注入测试完成: " + targets.Count + " 台。远端弹出记事本=注入链路(含组合键转发)正常");
            MessageBox.Show(this,
                "已向 " + targets.Count + " 台目标发送: Ctrl+Esc → notepad → 回车\n\n" +
                "判定：分格里远端弹出「记事本」= 键盘注入链路已打通，广播输入/批量粘贴即会生效。\n" +
                "若远端开了开始菜单但没弹记事本，或完全无反应，请把日志发我继续排查。",
                "注入测试已执行", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>keybd_event 完整单击: down + 70ms + up + 90ms（实验台验证的节奏）。</summary>
        private static void RealPress(int vk, int scan)
        {
            Win32.NativeMethods.keybd_event((byte)vk, (byte)scan, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(70);
            Win32.NativeMethods.keybd_event((byte)vk, (byte)scan, Win32.NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
            System.Threading.Thread.Sleep(90);
        }

        /// <summary>keybd_event 单独按下或抬起（组合键修饰键用），80ms 节奏。</summary>
        private static void RealHold(int vk, int scan, bool up)
        {
            uint flags = up ? Win32.NativeMethods.KEYEVENTF_KEYUP : 0;
            Win32.NativeMethods.keybd_event((byte)vk, (byte)scan, flags, UIntPtr.Zero);
            System.Threading.Thread.Sleep(80);
        }

        private static void RealTypeChar(char c)
        {
            short ks = Win32.NativeMethods.VkKeyScanW(c);
            int vk = ks & 0xFF;
            int scan = (int)Win32.NativeMethods.MapVirtualKey((uint)vk, 0);
            if (scan == 0)
            {
                int s2; bool ext;
                if (KeyMapper.TryGetScanCode((Keys)vk, out s2, out ext)) scan = s2;
            }
            if (scan != 0) RealPress(vk, scan);
        }

        private void SetAllBroadcast(bool value)
        {
            foreach (SessionTile t in _grid.Tiles)
                t.SetBroadcastTarget(value, false);
            RefreshBroadcastTargets();
        }

        #endregion

        #region 服务器配置编辑

        /// <summary>文本批量添加服务器（ip:端口|账号|密码|备注|分组）。</summary>
        private void BulkAddServers()
        {
            using (BulkAddForm f = new BulkAddForm(_cfg))
            {
                if (f.ShowDialog(this) != DialogResult.OK || f.AddedCount == 0) return;
                try
                {
                    ConfigStore.Save(_cfg);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "服务器已添加，但保存配置失败: " + ex.Message,
                        "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                _listPanel.Rebind(_cfg);
                ShowMsg("文本批量添加完成: " + f.AddedCount + " 台" +
                        (f.SkippedDuplicateCount > 0 ? " (跳过重复 " + f.SkippedDuplicateCount + " 台)" : ""));
            }
        }

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
                d.Title = "导入配置（自动识别普通 / 便携）";
                d.Filter = "ServerMaster 配置 (*.json;*.smc)|*.json;*.smc|普通配置 (*.json)|*.json|便携配置 (*.smc)|*.smc|所有文件 (*.*)|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (PortableConfig.IsPortableFile(d.FileName)) ImportPortable(d.FileName);
                    else ImportStandard(d.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导入失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private bool ConfirmOverwrite(string detail)
        {
            return MessageBox.Show(this,
                detail + "\n已建立的连接会断开，当前清单会被替换。继续？",
                "确认导入", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>普通配置：密码仍是本机 DPAPI 密文，换机后需重输。</summary>
        private void ImportStandard(string path)
        {
            AppConfig imported;
            try
            {
                imported = new System.Web.Script.Serialization.JavaScriptSerializer()
                    .Deserialize<AppConfig>(File.ReadAllText(path));
            }
            catch
            {
                MessageBox.Show(this, "文件不是有效的 ServerMaster 配置。", "导入失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (imported == null)
            {
                MessageBox.Show(this, "文件内容为空。", "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (imported.Servers == null) imported.Servers = new List<ServerInfo>();
            if (imported.Groups == null) imported.Groups = new List<string>();
            if (imported.Settings == null) imported.Settings = new AppSettings();

            if (!ConfirmOverwrite("将导入 " + imported.Servers.Count + " 台服务器、覆盖当前 " +
                                  _cfg.Servers.Count + " 台服务器。")) return;

            DisconnectAll();
            ConfigStore.Save(imported);
            ReloadConfig();

            int needInput = 0;
            foreach (ServerInfo s in _cfg.Servers)
                if (!string.IsNullOrEmpty(s.EncryptedPassword) && PasswordProtector.Unprotect(s.EncryptedPassword).Length == 0)
                    needInput++;
            if (needInput > 0)
            {
                MessageBox.Show(this,
                    "已导入 " + _cfg.Servers.Count + " 台服务器。\n\n其中 " + needInput +
                    " 台的密码来自其他电脑，本机无法解密，请在“编辑服务器”中重新输入密码后再连接。",
                    "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowMsg("配置已导入，" + needInput + " 台服务器需重新输入密码");
            }
            else
            {
                ShowMsg("配置已导入，共 " + _cfg.Servers.Count + " 台服务器");
            }
        }

        /// <summary>便携配置：输入访问密码解密，密码即刻转为本机 DPAPI 密文。</summary>
        private void ImportPortable(string path)
        {
            string pwd;
            using (PassphraseDialog dlg = new PassphraseDialog(
                "导入便携配置", "该文件已加密，请输入导出时设置的访问密码。", false, "解密"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                pwd = dlg.Passphrase;
            }

            PortableConfig.Payload payload;
            try
            {
                payload = PortableConfig.Read(path, pwd);
            }
            catch (InvalidDataException ex)
            {
                MessageBox.Show(this, ex.Message, "无法解密", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ConfirmOverwrite("将导入 " + payload.Servers.Count + " 台服务器、" + payload.Groups.Count +
                                  " 个分组，覆盖当前 " + _cfg.Servers.Count + " 台服务器。")) return;

            DisconnectAll();
            AppConfig imported = PortableConfig.ToLocal(payload, _cfg.Settings);
            ConfigStore.Save(imported);
            ReloadConfig();
            MessageBox.Show(this,
                "已导入 " + _cfg.Servers.Count + " 台服务器，密码已转为本机加密，可直接连接。",
                "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ShowMsg("便携配置已导入，共 " + _cfg.Servers.Count + " 台服务器");
        }

        /// <summary>普通导出：保留本机 DPAPI 密文，换机需重新输入密码。</summary>
        private void ExportStandard()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "普通导出配置（换机需重输密码）";
                d.Filter = "ServerMaster 配置 (*.json)|*.json|所有文件 (*.*)|*.*";
                d.DefaultExt = "json";
                d.AddExtension = true;
                d.FileName = "ServerMaster-配置-" + DateTime.Now.ToString("yyyyMMdd") + ".json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ConfigStore.Save(_cfg);
                    File.Copy(ConfigStore.ConfigFile, d.FileName, true);
                    MessageBox.Show(this,
                        "已导出 " + _cfg.Servers.Count + " 台服务器。\n\n" +
                        "密码仍是本机 Windows 账户加密的密文，换到其他电脑导入后无法解密，需要逐台重新输入密码。\n" +
                        "如需换机直接使用，请改用“便携导出（换机导入即用）”。",
                        "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>便携导出：整份清单用访问密码加密，任意电脑导入即可用。</summary>
        private void ExportPortable()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "便携导出配置（含加密密码）";
                d.Filter = "ServerMaster 便携配置 (*.smc)|*.smc|所有文件 (*.*)|*.*";
                d.DefaultExt = "smc";
                d.AddExtension = true;
                d.FileName = "ServerMaster-便携配置-" + DateTime.Now.ToString("yyyyMMdd") + ".smc";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string pwd;
                using (PassphraseDialog dlg = new PassphraseDialog(
                    "设置访问密码",
                    "便携配置文件包含服务器密码，将用此密码整体加密。导入方输入同一密码即可直接连接。",
                    true, "导出"))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    pwd = dlg.Passphrase;
                }

                try
                {
                    ConfigStore.Save(_cfg);
                    int unreadable;
                    int count = PortableConfig.Export(_cfg, d.FileName, pwd, out unreadable);
                    string msg = "已便携导出 " + count + " 台服务器（含密码）。\n\n" +
                                 "请通过安全渠道把文件与访问密码分别告知接收方；密码遗忘后文件无法恢复。";
                    if (unreadable > 0)
                        msg += "\n\n注意：有 " + unreadable + " 台服务器的密码在本机无法解密（可能来自其他电脑），" +
                               "导出时密码为空，接收方需手动补录。";
                    MessageBox.Show(this, msg, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            HashSet<string> connected = new HashSet<string>();
            foreach (RdpSession s in _conn.Sessions)
                if (s.State == SessionState.Connected) connected.Add(s.Server.Id);
            FileTransferForm f = new FileTransferForm(_cfg, connected, BroadcastPasteToServers);
            f.Show(this);
        }

        /// <summary>
        /// 批量推送(经RDP)：调用方已把文件放入剪贴板，等待 RDP 剪贴板同步后
        /// 向勾选的已连接会话注入 Ctrl+V，文件粘贴到远端聚焦窗口（无人操作时为桌面）。
        /// 不依赖 SMB/445，NAT VPS 可用。
        /// </summary>
        private void BroadcastPasteToServers(List<ServerInfo> servers)
        {
            List<RdpSession> targets = new List<RdpSession>();
            List<string> names = new List<string>();
            foreach (ServerInfo s in servers)
            {
                RdpSession session = _conn.FindByServer(s);
                if (session != null && session.State == SessionState.Connected)
                {
                    targets.Add(session);
                    names.Add(s.DisplayName);
                }
            }
            if (targets.Count == 0)
            {
                MessageBox.Show(this,
                    "勾选的服务器中没有已连接的会话。\n请先在主界面连接这些服务器，再执行批量推送。",
                    "批量推送", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowMsg("批量推送：勾选的服务器中没有已连接的会话");
                return;
            }

            Timer timer = new Timer();
            timer.Interval = 1500;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                // 前台安全闸: keybd_event 注入全局输入流,本程序不在前台时中止
                IntPtr fg = Win32.NativeMethods.GetForegroundWindow();
                uint pid;
                uint fgTid = fg == IntPtr.Zero ? 0 : Win32.NativeMethods.GetWindowThreadProcessId(fg, out pid);
                bool fgMine = fgTid != 0 && fgTid == Win32.NativeMethods.GetCurrentThreadId();
                if (!fgMine)
                {
                    AppLogger.Warn("批量推送中止: 前台窗口不属于本程序，Ctrl+V 可能注入其他程序");
                    ShowMsg("批量推送中止：本程序不在前台（请保持主窗口激活后重试）");
                    return;
                }
                // 焦点轮转 + keybd_event Ctrl+V（实验台验证节奏: 键间隔 60-90ms）
                foreach (RdpSession s in targets)
                {
                    IntPtr h = s.Handle;
                    if (h == IntPtr.Zero) continue;
                    IntPtr prevFocus = Win32.NativeMethods.GetFocus();
                    Win32.NativeMethods.SetFocus(h);
                    System.Threading.Thread.Sleep(250); // 等控件完成内部焦点切换
                    Win32.NativeMethods.keybd_event(0x11, 0x1D, 0, UIntPtr.Zero);          // Ctrl 按下
                    System.Threading.Thread.Sleep(70);
                    Win32.NativeMethods.keybd_event(0x56, 0x2F, 0, UIntPtr.Zero);          // V 按下
                    System.Threading.Thread.Sleep(80);
                    Win32.NativeMethods.keybd_event(0x56, 0x2F, Win32.NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);   // V 抬起
                    System.Threading.Thread.Sleep(70);
                    Win32.NativeMethods.keybd_event(0x11, 0x1D, Win32.NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);   // Ctrl 抬起
                    System.Threading.Thread.Sleep(150); // 等真实输入消化再切下一台
                    IntPtr restore = prevFocus != IntPtr.Zero ? prevFocus : h;
                    Win32.NativeMethods.SetFocus(restore);
                }
                AppLogger.Info("批量推送: 已向 " + targets.Count + " 台注入 Ctrl+V");
                ShowMsg("批量推送：已向 " + targets.Count + " 台(" + string.Join("、", names.ToArray()) +
                        ")发送粘贴指令，文件将出现在远端桌面/当前聚焦窗口");
                MessageBox.Show(this,
                    "已向 " + targets.Count + " 台服务器发送粘贴指令：\n" + string.Join("\n", names.ToArray()) +
                    "\n\n文件将粘贴到远端当前聚焦窗口（无人操作时即桌面目录）。\n若未出现，请确认远端桌面处于桌面状态、且本会话未禁用剪贴板。",
                    "批量推送已执行", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            timer.Start();
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
            if (_broadcast.Enabled)
                _lblBcast.Text = "广播: 开 (" + _broadcast.TargetCount + " 目标·已镜像 " + _broadcast.MirroredKeyCount + " 键)";
            else
                _lblBcast.Text = "广播: 关";
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

            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _cfg.Settings.WindowLeft = bounds.Left;
            _cfg.Settings.WindowTop = bounds.Top;
            _cfg.Settings.WindowWidth = bounds.Width;
            _cfg.Settings.WindowHeight = bounds.Height;
            _cfg.Settings.WindowPositionSaved = true;
            _cfg.Settings.WindowMaximized = (WindowState == FormWindowState.Maximized);
            if (_split != null)
            {
                _cfg.Settings.ShowServerPanel = !_split.Panel1Collapsed;
                if (!_split.Panel1Collapsed) _cfg.Settings.ServerPanelWidth = _split.SplitterDistance;
            }
            try { ConfigStore.Save(_cfg); } catch { }

            Application.RemoveMessageFilter(_broadcast);
            _broadcast.Disable();
            _conn.CloseAll();
        }

        #endregion
    }
}
