using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Services;

namespace ServerMaster.UI
{
    /// <summary>新增/编辑服务器对话框。密码留空 = 保留原密码。</summary>
    public class ServerEditDialog : Form
    {
        private readonly TextBox _txtName;
        private readonly PasteAwareTextBox _txtHost;
        private readonly TextBox _txtPort;
        private readonly TextBox _txtDomain;
        private readonly TextBox _txtUser;
        private readonly TextBox _txtPass;
        private readonly ComboBox _cmbGroup;
        private readonly ComboBox _cmbColor;
        private readonly CheckBox _chkConsole;
        private readonly CheckBox _chkDrives;
        private readonly CheckBox _chkReconnect;
        private readonly TextBox _txtNotes;
        private readonly ServerInfo _existing;
        private readonly List<string> _groups;

        /// <summary>点击保存后的结果对象。</summary>
        public ServerInfo Result { get; private set; }

        public ServerEditDialog(ServerInfo existing, List<string> groups)
        {
            _existing = existing;
            _groups = groups ?? new List<string>();

            Text = existing == null ? "添加服务器" : "编辑服务器 - " + existing.DisplayName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(380, 430);
            Font = new Font("Microsoft YaHei UI", 9F);

            int y = 14;
            Label l;
            const int labelX = 16;
            const int inputX = 96;
            const int inputW = 262;

            l = MakeLabel("名称:", labelX, y + 3); Controls.Add(l);
            _txtName = MakeInput(inputX, y, inputW); Controls.Add(_txtName);
            y += 32;
            l = MakeLabel("主机/IP:", labelX, y + 3); Controls.Add(l);
            _txtHost = new PasteAwareTextBox();
            _txtHost.SetBounds(inputX, y, inputW, 24);
            _txtHost.TextChanged += txtHost_TextChanged;
            Controls.Add(_txtHost);
            y += 32;
            l = MakeLabel("端口:", labelX, y + 3); Controls.Add(l);
            _txtPort = MakeInput(inputX, y, 80); _txtPort.Text = "3389"; Controls.Add(_txtPort);
            y += 32;
            l = MakeLabel("域(可选):", labelX, y + 3); Controls.Add(l);
            _txtDomain = MakeInput(inputX, y, inputW); Controls.Add(_txtDomain);
            y += 32;
            l = MakeLabel("用户名:", labelX, y + 3); Controls.Add(l);
            _txtUser = MakeInput(inputX, y, inputW); _txtUser.Text = "Administrator"; Controls.Add(_txtUser);
            y += 32;
            l = MakeLabel("密码:", labelX, y + 3); Controls.Add(l);
            _txtPass = MakeInput(inputX, y, inputW);
            _txtPass.UseSystemPasswordChar = true;
            Controls.Add(_txtPass);
            y += 32;
            l = MakeLabel("分组:", labelX, y + 3); Controls.Add(l);
            _cmbGroup = new ComboBox();
            _cmbGroup.DropDownStyle = ComboBoxStyle.DropDown; // 可输入新分组
            _cmbGroup.SetBounds(inputX, y, inputW, 24);
            foreach (string g in _groups) _cmbGroup.Items.Add(g);
            Controls.Add(_cmbGroup);
            y += 34;
            l = MakeLabel("色彩深度:", labelX, y + 3); Controls.Add(l);
            _cmbColor = new ComboBox();
            _cmbColor.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbColor.SetBounds(inputX, y, 120, 24);
            _cmbColor.Items.Add("8 位 (最省带宽)");
            _cmbColor.Items.Add("15 位");
            _cmbColor.Items.Add("16 位 (推荐多画面)");
            _cmbColor.Items.Add("24 位");
            _cmbColor.Items.Add("32 位 (单画面)");
            _cmbColor.SelectedIndex = 2;
            Controls.Add(_cmbColor);
            y += 34;
            _chkConsole = new CheckBox();
            _chkConsole.Text = "连接到控制台会话 (ConnectToAdministerServer)";
            _chkConsole.SetBounds(labelX, y, 340, 22);
            Controls.Add(_chkConsole);
            y += 26;
            _chkDrives = new CheckBox();
            _chkDrives.Text = "重定向本地磁盘到远程会话 (远程可见 \\\\tsclient)";
            _chkDrives.SetBounds(labelX, y, 340, 22);
            Controls.Add(_chkDrives);
            y += 26;
            _chkReconnect = new CheckBox();
            _chkReconnect.Text = "意外掉线自动重连 (最多5次)";
            _chkReconnect.Checked = true;
            _chkReconnect.SetBounds(labelX, y, 340, 22);
            Controls.Add(_chkReconnect);
            y += 28;
            l = MakeLabel("备注:", labelX, y + 3); Controls.Add(l);
            _txtNotes = MakeInput(inputX, y, inputW);
            _txtNotes.Height = 40;
            Controls.Add(_txtNotes);
            y += 58;

            Button btnOk = new Button();
            btnOk.Text = "保存";
            btnOk.SetBounds(inputX, y, 88, 30);
            btnOk.Click += btnOk_Click;
            Controls.Add(btnOk);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(inputX + 96, y, 88, 30);
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);
            CancelButton = btnCancel;

            // 填充既有值
            if (_existing != null)
            {
                _txtName.Text = _existing.Name;
                _txtHost.Text = _existing.Host;
                _txtPort.Text = _existing.Port.ToString();
                _txtDomain.Text = _existing.Domain;
                _txtUser.Text = _existing.Username;
                _txtPass.Text = ""; // 留空=保留
                _cmbGroup.Text = _existing.Group;
                switch (_existing.ColorDepth)
                {
                    case 8: _cmbColor.SelectedIndex = 0; break;
                    case 15: _cmbColor.SelectedIndex = 1; break;
                    case 24: _cmbColor.SelectedIndex = 3; break;
                    case 32: _cmbColor.SelectedIndex = 4; break;
                    default: _cmbColor.SelectedIndex = 2; break;
                }
                _chkConsole.Checked = _existing.ConnectToConsole;
                _chkDrives.Checked = _existing.RedirectDrives;
                _chkReconnect.Checked = _existing.AutoReconnect;
                _txtNotes.Text = _existing.Notes;
            }
            else
            {
                _cmbGroup.Text = _groups.Count > 0 ? _groups[0] : "默认分组";
            }
        }

        private Label MakeLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        private TextBox MakeInput(int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.SetBounds(x, y, w, 24);
            return t;
        }

        /// <summary>
        /// 粘贴 "主机:端口" 时自动拆分：主机栏只留主机，端口自动填入端口栏。
        /// 只在检测到粘贴操作时触发，不影响手动逐字输入。
        /// </summary>
        private void txtHost_TextChanged(object sender, EventArgs e)
        {
            if (!_txtHost.PasteOccurred) return;
            _txtHost.PasteOccurred = false;
            string hostOnly;
            int port;
            if (HostPort.Split(_txtHost.Text, out hostOnly, out port))
            {
                _txtHost.Text = hostOnly;
                _txtPort.Text = port.ToString();
                _txtHost.SelectionStart = _txtHost.TextLength;
            }
        }

        /// <summary>能感知粘贴动作的输入框（Ctrl+V / 右键粘贴都会触发 WM_PASTE）。</summary>
        private class PasteAwareTextBox : TextBox
        {
            public bool PasteOccurred;

            private const int WM_PASTE = 0x0302;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_PASTE) PasteOccurred = true;
                base.WndProc(ref m);
            }
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            string hostInput = _txtHost.Text.Trim();
            if (hostInput.Length == 0)
            {
                MessageBox.Show(this, "请填写主机名或 IP。", "验证", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 支持 "host:port" 一并填入主机栏：自动拆分并回显到端口栏
            string hostOnly;
            int portFromHost;
            HostPort.Split(hostInput, out hostOnly, out portFromHost);

            int port;
            bool portOk = int.TryParse(_txtPort.Text.Trim(), out port) && port >= 1 && port <= 65535;
            if (portFromHost >= 1 && portFromHost <= 65535)
            {
                port = portFromHost;
                portOk = true;
            }
            if (!portOk)
            {
                MessageBox.Show(this, "端口无效 (1-65535)。", "验证", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (hostOnly.Length == 0)
            {
                MessageBox.Show(this, "主机名无效。", "验证", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _txtHost.Text = hostOnly;
            _txtPort.Text = port.ToString();

            int[] depths = new int[] { 8, 15, 16, 24, 32 };
            int colorDepth = depths[Math.Max(0, _cmbColor.SelectedIndex)];

            ServerInfo r = _existing == null ? new ServerInfo() : _existing.Clone();
            r.Name = _txtName.Text.Trim();
            r.Host = hostOnly;
            r.Port = port;
            r.Domain = _txtDomain.Text.Trim();
            r.Username = string.IsNullOrEmpty(_txtUser.Text.Trim()) ? "Administrator" : _txtUser.Text.Trim();
            if (_txtPass.Text.Length > 0)
                r.EncryptedPassword = PasswordProtector.Protect(_txtPass.Text);
            r.Group = string.IsNullOrEmpty(_cmbGroup.Text.Trim()) ? "默认分组" : _cmbGroup.Text.Trim();
            r.ColorDepth = colorDepth;
            r.ConnectToConsole = _chkConsole.Checked;
            r.RedirectDrives = _chkDrives.Checked;
            r.AutoReconnect = _chkReconnect.Checked;
            r.Notes = _txtNotes.Text.Trim();

            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
