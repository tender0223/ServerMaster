using System;
using System.Drawing;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Rdp;
using ServerMaster.Services;

namespace ServerMaster.UI
{
    /// <summary>
    /// 单个会话分格：标题栏(名称/状态/广播勾选/放大/关闭) + RDP 画面主体。
    /// 支持拖放文件(推送到会话剪贴板)、双击标题放大、点击成为主控。
    /// </summary>
    public class SessionTile : Panel
    {
        private static readonly Color HeaderNormal = Color.FromArgb(60, 60, 66);
        private static readonly Color HeaderMaster = Color.FromArgb(0, 122, 204);
        private static readonly Color HeaderReceiving = Color.FromArgb(12, 112, 92);
        private static readonly Color AccentReceiving = Color.FromArgb(0, 214, 170);

        private readonly Panel _header;
        private readonly Panel _accent;
        private readonly Label _lblName;
        private readonly Label _lblStatus;
        private readonly CheckBox _chkBroadcast;
        private readonly Button _btnZoom;
        private readonly Button _btnClose;
        private readonly Panel _body;
        private readonly Label _placeholder;
        private readonly ToolTip _tip;
        private bool _isMaster;
        private bool _receiving;
        private SessionState _state;
        private string _stateNote;

        public RdpSession Session { get; private set; }
        public bool IsBroadcastTarget { get { return _chkBroadcast.Checked; } }

        public event Action<SessionTile> CloseRequested;
        public event Action<SessionTile> ZoomToggled;
        public event Action<SessionTile> MasterActivated;
        public event Action<SessionTile, string[]> FilesDropped;
        public event Action<SessionTile> BroadcastTargetChanged;

        public SessionTile(RdpSession session)
        {
            Session = session;

            BackColor = Color.FromArgb(32, 32, 36);
            Padding = new Padding(0);

            // ---- 标题栏 ----
            _header = new Panel();
            _header.Dock = DockStyle.Top;
            _header.Height = 28;
            _header.BackColor = HeaderNormal;

            _accent = new Panel();
            _accent.BackColor = AccentReceiving;
            _accent.SetBounds(0, 0, 4, _header.Height);
            _accent.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _accent.Visible = false;
            _header.Controls.Add(_accent);

            _lblName = new Label();
            _lblName.Text = session.Server.DisplayName;
            _lblName.ForeColor = Color.White;
            _lblName.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            _lblName.AutoSize = true;
            _lblName.Location = new Point(8, 6);
            _lblName.Cursor = Cursors.Hand;
            _header.Controls.Add(_lblName);

            _lblStatus = new Label();
            _lblStatus.Text = "未连接";
            _lblStatus.ForeColor = Color.FromArgb(180, 180, 185);
            _lblStatus.Font = new Font("Microsoft YaHei UI", 8.25F);
            _lblStatus.AutoSize = true;
            _lblStatus.Location = new Point(200, 7);
            _header.Controls.Add(_lblStatus);

            _chkBroadcast = new CheckBox();
            _chkBroadcast.Text = "广播";
            _chkBroadcast.ForeColor = Color.FromArgb(235, 235, 240);
            _chkBroadcast.AutoSize = true;
            _chkBroadcast.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _chkBroadcast.Location = new Point(0, 4);
            _chkBroadcast.CheckedChanged += OnBroadcastCheckedChanged;
            _header.Controls.Add(_chkBroadcast);

            _tip = new ToolTip();
            _tip.SetToolTip(_chkBroadcast, "勾选后，开启工具栏「广播输入」时，本分格同步接收主控分格的键鼠操作");

            _btnZoom = new Button();
            _btnZoom.Text = "▣";
            _btnZoom.FlatStyle = FlatStyle.Flat;
            _btnZoom.FlatAppearance.BorderSize = 0;
            _btnZoom.ForeColor = Color.FromArgb(220, 220, 225);
            _btnZoom.BackColor = Color.Transparent;
            _btnZoom.Size = new Size(28, 22);
            _btnZoom.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnZoom.TabStop = false;
            _btnZoom.Click += delegate { if (ZoomToggled != null) ZoomToggled(this); };
            _header.Controls.Add(_btnZoom);

            _btnClose = new Button();
            _btnClose.Text = "✕";
            _btnClose.FlatStyle = FlatStyle.Flat;
            _btnClose.FlatAppearance.BorderSize = 0;
            _btnClose.ForeColor = Color.FromArgb(235, 160, 160);
            _btnClose.BackColor = Color.Transparent;
            _btnClose.Size = new Size(28, 22);
            _btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnClose.TabStop = false;
            _btnClose.Click += delegate { if (CloseRequested != null) CloseRequested(this); };
            _header.Controls.Add(_btnClose);

            _header.MouseDoubleClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && ZoomToggled != null) ZoomToggled(this);
            };
            _lblName.MouseDoubleClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && ZoomToggled != null) ZoomToggled(this);
            };
            _header.MouseClick += delegate { ActivateMaster(); };
            _lblName.MouseClick += delegate { ActivateMaster(); };

            Controls.Add(_header);

            // ---- 主体 ----
            _body = new Panel();
            _body.Dock = DockStyle.Fill;
            _body.BackColor = Color.Black;
            _body.AllowDrop = true;
            _body.DragEnter += body_DragEnter;
            _body.DragDrop += body_DragDrop;
            Controls.Add(_body);
            _body.BringToFront();

            _placeholder = new Label();
            _placeholder.Text = "未连接";
            _placeholder.ForeColor = Color.FromArgb(130, 130, 138);
            _placeholder.Dock = DockStyle.Fill;
            _placeholder.TextAlign = ContentAlignment.MiddleCenter;
            _body.Controls.Add(_placeholder);

            // 点击主体任意处（含 RDP 画面）→ 成为主控
            _body.MouseClick += delegate { ActivateMaster(); };
            Click += delegate { ActivateMaster(); };

            PositionHeaderItems();
        }

        private void PositionHeaderItems()
        {
            // 右对齐: 关闭/放大/广播；状态标签跟随名称
            int right = _header.Width;
            _btnClose.Location = new Point(right - _btnClose.Width - 2, 3);
            _btnZoom.Location = new Point(_btnClose.Left - _btnZoom.Width, 3);
            _chkBroadcast.Location = new Point(_btnZoom.Left - _chkBroadcast.Width - 6, 4);
            int statusRight = _chkBroadcast.Left - 6;
            _lblStatus.Location = new Point(statusRight - _lblStatus.Width, 7);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionHeaderItems();
        }

        /// <summary>把 RDP 控件装入画面区（分格加入网格并显示后调用）。</summary>
        public void AttachRdpControl()
        {
            Control c = Session.Host;
            c.Dock = DockStyle.Fill;
            c.GotFocus += delegate { ActivateMaster(); };
            _body.Controls.Add(c);
            c.BringToFront();
            _placeholder.Visible = false;
        }

        private void ActivateMaster()
        {
            _isMaster = true;
            UpdateHeaderColor();
            UpdateStatusText();
            if (MasterActivated != null) MasterActivated(this);
        }

        /// <summary>取消主控高亮（另一分格成为主控时由外部调用）。</summary>
        public void ClearMasterHighlight()
        {
            _isMaster = false;
            UpdateHeaderColor();
            UpdateStatusText();
        }

        /// <summary>广播接收视觉：全局广播开启且本分格是接收目标时高亮标题栏。</summary>
        public void SetBroadcastVisual(bool receiving, bool master)
        {
            _receiving = receiving;
            _isMaster = master;
            UpdateHeaderColor();
            UpdateStatusText();
            PositionHeaderItems();
        }

        /// <summary>设置标题栏显示内容：0=备注名，1=不显示，2=主机名，3=主机IP+端口。</summary>
        public void ApplyTitleMode(int mode)
        {
            string host;
            int port;
            HostPort.Split(Session.Server.Host, out host, out port);
            if (port <= 0) port = Session.Server.Port;

            switch (mode)
            {
                case 1:
                    _lblName.Text = "";
                    _lblName.Visible = false;
                    break;
                case 2:
                    _lblName.Text = host;
                    _lblName.Visible = true;
                    break;
                case 3:
                    _lblName.Text = host + ":" + port;
                    _lblName.Visible = true;
                    break;
                default:
                    _lblName.Text = Session.Server.DisplayName;
                    _lblName.Visible = true;
                    break;
            }
            PositionHeaderItems();
        }

        private void UpdateHeaderColor()
        {
            if (_isMaster) _header.BackColor = HeaderMaster;
            else if (_receiving) _header.BackColor = HeaderReceiving;
            else _header.BackColor = HeaderNormal;

            _accent.Visible = _receiving && !_isMaster;
            _chkBroadcast.ForeColor = _receiving ? AccentReceiving : Color.FromArgb(235, 235, 240);
        }

        public void SetBroadcastTarget(bool value, bool raiseEvent)
        {
            if (_chkBroadcast.Checked == value) return;
            // 程序设置时不重复触发改动事件由调用方统一刷新
            _chkBroadcast.CheckedChanged -= OnBroadcastCheckedChanged;
            _chkBroadcast.Checked = value;
            _chkBroadcast.CheckedChanged += OnBroadcastCheckedChanged;
            if (raiseEvent && BroadcastTargetChanged != null) BroadcastTargetChanged(this);
        }

        private void OnBroadcastCheckedChanged(object sender, EventArgs e)
        {
            if (BroadcastTargetChanged != null) BroadcastTargetChanged(this);
        }

        public void SetState(SessionState st, string note)
        {
            _state = st;
            _stateNote = note ?? "";
            UpdateStatusText();
            PositionHeaderItems();
        }

        private void UpdateStatusText()
        {
            string suffix = string.IsNullOrEmpty(_stateNote) ? "" : " · " + _stateNote;
            if (_receiving) suffix += " · 接收广播";
            else if (_isMaster) suffix += " · 主控";

            switch (_state)
            {
                case SessionState.Disconnected:
                    _lblStatus.Text = "○ 未连接" + suffix;
                    _lblStatus.ForeColor = Color.FromArgb(170, 170, 176);
                    break;
                case SessionState.Connecting:
                    _lblStatus.Text = "◐ 连接中" + suffix;
                    _lblStatus.ForeColor = Color.FromArgb(235, 200, 90);
                    break;
                case SessionState.Connected:
                    _lblStatus.Text = "● 已连接" + suffix;
                    _lblStatus.ForeColor = Color.FromArgb(120, 220, 130);
                    break;
                case SessionState.Reconnecting:
                    _lblStatus.Text = "◐ 重连中" + suffix;
                    _lblStatus.ForeColor = Color.FromArgb(235, 200, 90);
                    break;
                case SessionState.Failed:
                    _lblStatus.Text = "● 失败" + suffix;
                    _lblStatus.ForeColor = Color.FromArgb(240, 120, 120);
                    break;
            }
            PositionHeaderItems();
        }

        public void SetZoomed(bool zoomed)
        {
            _btnZoom.Text = zoomed ? "⤡" : "▣";
        }

        public void SetZoomedButtonText(string text)
        {
            _btnZoom.Text = text;
        }

        private void body_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void body_DragDrop(object sender, DragEventArgs e)
        {
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null && paths.Length > 0 && FilesDropped != null)
                FilesDropped(this, paths);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            // 分格关闭时解除事件，防止泄漏
            if (Session != null && Session.Host != null)
            {
                try { Controls.Remove(Session.Host); } catch { }
            }
            base.OnHandleDestroyed(e);
        }
    }
}
