using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Services;
using ServerMaster.Services.FinalShell;

namespace ServerMaster.UI
{
    /// <summary>
    /// 从 FinalShell 批量导入连接：支持「导出的 .json 文件」或「conn 数据目录」。
    /// 清单可勾选、自动去重、密码自动解密并转 DPAPI 加密。
    /// </summary>
    public class ImportFinalShellForm : Form
    {
        private readonly AppConfig _cfg;
        private TextBox _txtSource;
        private TextBox _txtGroup;
        private ListView _list;
        private Label _lblSummary;
        private List<FinalShellEntry> _entries = new List<FinalShellEntry>();

        /// <summary>导入完成后由 MainForm 读取。</summary>
        public int ImportedCount { get; private set; }
        public int SkippedDuplicateCount { get; private set; }

        public ImportFinalShellForm(AppConfig cfg)
        {
            _cfg = cfg;

            Text = "从 FinalShell 导入连接";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(680, 560);
            MinimumSize = new Size(620, 500);
            Font = new Font("Microsoft YaHei UI", 9F);

            BuildUi();

            // 预填默认 conn 目录并自动解析
            string appDataConn = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "finalshell", "conn");
            if (Directory.Exists(appDataConn))
            {
                _txtSource.Text = appDataConn;
                ParseSource();
            }

            AllowDrop = true;
            DragEnter += delegate(object s, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Length > 0)
                {
                    _txtSource.Text = paths[0];
                    ParseSource();
                }
            };
        }

        private void BuildUi()
        {
            Label l1 = new Label();
            l1.Text = "来源:";
            l1.AutoSize = true;
            l1.Location = new Point(12, 15);
            Controls.Add(l1);

            _txtSource = new TextBox();
            _txtSource.SetBounds(58, 12, 382, 24);
            _txtSource.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_txtSource);

            Button btnFile = new Button();
            btnFile.Text = "浏览文件…";
            btnFile.SetBounds(446, 11, 100, 26);
            btnFile.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnFile.Click += btnFile_Click;
            Controls.Add(btnFile);

            Button btnDir = new Button();
            btnDir.Text = "浏览目录…";
            btnDir.SetBounds(552, 11, 100, 26);
            btnDir.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnDir.Click += btnDir_Click;
            Controls.Add(btnDir);

            Label l2 = new Label();
            l2.Text = "分组:";
            l2.AutoSize = true;
            l2.Location = new Point(12, 47);
            Controls.Add(l2);

            _txtGroup = new TextBox();
            _txtGroup.SetBounds(58, 44, 240, 24);
            Controls.Add(_txtGroup);

            Label lHint = new Label();
            lHint.Text = "留空 = 按 FinalShell 原目录/分组还原";
            lHint.AutoSize = true;
            lHint.ForeColor = Color.FromArgb(110, 110, 118);
            lHint.Location = new Point(308, 47);
            Controls.Add(lHint);

            Label l3 = new Label();
            l3.Text = "连接清单 (勾选要导入的条目):";
            l3.AutoSize = true;
            l3.Location = new Point(12, 76);
            Controls.Add(l3);

            _list = new ListView();
            _list.SetBounds(12, 96, 656, 350);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _list.View = View.Details;
            _list.CheckBoxes = true;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Columns.Add("名称", 170);
            _list.Columns.Add("协议", 60);
            _list.Columns.Add("主机", 160);
            _list.Columns.Add("端口", 60);
            _list.Columns.Add("用户名", 110);
            _list.Columns.Add("密码", 86);
            _list.ItemChecked += delegate { UpdateSummary(); };
            Controls.Add(_list);

            Button btnAll = new Button();
            btnAll.Text = "全选";
            btnAll.SetBounds(12, 454, 60, 26);
            btnAll.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnAll.Click += delegate { SetAllChecked(true); };
            Controls.Add(btnAll);

            Button btnNone = new Button();
            btnNone.Text = "全不选";
            btnNone.SetBounds(78, 454, 70, 26);
            btnNone.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnNone.Click += delegate { SetAllChecked(false); };
            Controls.Add(btnNone);

            Button btnParse = new Button();
            btnParse.Text = "重新解析";
            btnParse.SetBounds(154, 454, 84, 26);
            btnParse.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnParse.Click += delegate { ParseSource(); };
            Controls.Add(btnParse);

            _lblSummary = new Label();
            _lblSummary.Text = "请选择 FinalShell 导出文件或 conn 目录";
            _lblSummary.AutoSize = true;
            _lblSummary.ForeColor = Color.FromArgb(90, 90, 96);
            _lblSummary.Location = new Point(248, 459);
            _lblSummary.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(_lblSummary);

            Button btnImport = new Button();
            btnImport.Text = "导入所选";
            btnImport.SetBounds(12, 492, 110, 32);
            btnImport.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnImport.Click += btnImport_Click;
            Controls.Add(btnImport);

            Button btnClose = new Button();
            btnClose.Text = "关闭";
            btnClose.SetBounds(588, 492, 80, 32);
            btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnClose.DialogResult = DialogResult.Cancel;
            Controls.Add(btnClose);
            CancelButton = btnClose;
        }

        private void btnFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "选择 FinalShell 导出的连接文件";
                d.Filter = "FinalShell 导出 (*.json)|*.json|所有文件 (*.*)|*.*";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    _txtSource.Text = d.FileName;
                    ParseSource();
                }
            }
        }

        private void btnDir_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择 FinalShell 的 conn 数据目录\n(通常在 %APPDATA%\\finalshell\\conn，或安装目录下的 finalshell\\conn)";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    _txtSource.Text = d.SelectedPath;
                    ParseSource();
                }
            }
        }

        private void ParseSource()
        {
            string src = _txtSource.Text.Trim();
            _entries = new List<FinalShellEntry>();
            if (src.Length == 0)
            {
                LoadEntries(_entries);
                return;
            }
            try
            {
                if (Directory.Exists(src))
                    _entries = FinalShellImporter.ParseDirectory(src);
                else if (File.Exists(src))
                    _entries = FinalShellImporter.ParseFile(src);
                else
                {
                    _lblSummary.Text = "路径不存在";
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "解析失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            LoadEntries(_entries);
        }

        private void LoadEntries(List<FinalShellEntry> entries)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            HashSet<string> seen = new HashSet<string>(); // 批内去重(FinalShell 可能有移动后的残留文件)
            foreach (FinalShellEntry e in entries)
            {
                ListViewItem it = new ListViewItem(e.Name);
                it.SubItems.Add(e.Protocol.Length == 0 ? "-" : e.Protocol);
                it.SubItems.Add(e.Host);
                it.SubItems.Add(e.Port > 0 ? e.Port.ToString() : "-");
                it.SubItems.Add(e.Username.Length == 0 ? "-" : e.Username);
                string pwd;
                if (!e.HasPassword) pwd = "无";
                else pwd = e.PasswordDecoded == null ? "需手动录入" : "已解密";
                it.SubItems.Add(pwd);
                it.Tag = e;

                bool exists = Exists(e.Host, e.Port);
                string dupKey = NormHost(e.Host) + "|" + e.Port;
                bool inBatch = !seen.Add(dupKey);
                it.Checked = !exists && !inBatch;
                if (exists || inBatch)
                {
                    it.ForeColor = Color.Gray;
                    it.Text = e.Name + (inBatch ? "（批内重复）" : "（已存在）");
                }
                _list.Items.Add(it);
            }
            _list.EndUpdate();
            UpdateSummary();
            if (entries.Count == 0)
                _lblSummary.Text = "未解析到连接 (请确认是 FinalShell 导出的 .json 或 conn 目录)";
        }

        private void SetAllChecked(bool value)
        {
            _list.BeginUpdate();
            foreach (ListViewItem it in _list.Items) it.Checked = value;
            _list.EndUpdate();
        }

        private void UpdateSummary()
        {
            int rdp = 0, ssh = 0, other = 0, badPwd = 0;
            foreach (FinalShellEntry e in _entries)
            {
                if (e.Protocol == "RDP") rdp++;
                else if (e.Protocol == "SSH") ssh++;
                else other++;
                if (e.HasPassword && e.PasswordDecoded == null) badPwd++;
            }
            int checkedCount = 0;
            foreach (ListViewItem it in _list.Items)
                if (it.Checked) checkedCount++;

            _lblSummary.Text = string.Format(
                "共 {0} 条 (RDP {1} · SSH {2} · 其他 {3}) · 已勾选 {4} 条{5}",
                _entries.Count, rdp, ssh, other, checkedCount,
                badPwd > 0 ? " · " + badPwd + " 条密码需手动录入" : "");
            _lblSummary.ForeColor = badPwd > 0 ? Color.FromArgb(190, 120, 20) : Color.FromArgb(90, 90, 96);
        }

        /// <summary>主机+端口去重（双方都做 host:port 归一化）。</summary>
        private bool Exists(string host, int port)
        {
            string h = NormHost(host);
            foreach (ServerInfo s in _cfg.Servers)
            {
                if (NormHost(s.Host) == h && (port == 0 || s.Port == port)) return true;
            }
            return false;
        }

        private static string NormHost(string raw)
        {
            if (raw == null) return "";
            string h;
            int p;
            HostPort.Split(raw.Trim(), out h, out p);
            return h.ToLowerInvariant();
        }

        private void btnImport_Click(object sender, EventArgs e)
        {
            if (_list.CheckedItems.Count == 0)
            {
                MessageBox.Show(this, "请先勾选要导入的连接。", "提示");
                return;
            }

            string userGroup = _txtGroup.Text.Trim();
            int imported = 0, dup = 0, badPwd = 0;
            HashSet<string> seen = new HashSet<string>(); // 批内去重
            foreach (ListViewItem it in _list.CheckedItems)
            {
                FinalShellEntry en = (FinalShellEntry)it.Tag;
                if (en.Host.Length == 0) continue;
                string dupKey = NormHost(en.Host) + "|" + en.Port;
                if (Exists(en.Host, en.Port) || !seen.Add(dupKey)) { dup++; continue; }

                ServerInfo s = new ServerInfo();
                s.Name = en.Name;
                s.Host = en.Host;
                s.Port = en.Port > 0 ? en.Port : 3389;
                s.Username = en.Username.Length > 0 ? en.Username : "Administrator";
                if (en.HasPassword && en.PasswordDecoded != null && en.PasswordDecoded.Length > 0)
                    s.EncryptedPassword = PasswordProtector.Protect(en.PasswordDecoded);
                if (en.HasPassword && en.PasswordDecoded == null) badPwd++;

                string grp = userGroup;
                if (grp.Length == 0)
                    grp = en.SourceGroup.Length > 0 ? en.SourceGroup : "FinalShell导入";
                s.Group = grp;
                s.Notes = "FinalShell导入" + (en.Protocol.Length > 0 ? "(" + en.Protocol + ")" : "") +
                          (en.Description.Length > 0 ? " " + en.Description : "");
                s.ColorDepth = _cfg.Settings.DefaultColorDepth;
                s.AutoReconnect = true;

                if (!_cfg.Groups.Contains(s.Group)) _cfg.Groups.Add(s.Group);
                _cfg.Servers.Add(s);
                imported++;
            }

            ImportedCount = imported;
            SkippedDuplicateCount = dup;
            string msg = string.Format("导入 {0} 台服务器。", imported);
            if (dup > 0) msg += string.Format("\n跳过已存在 {0} 台。", dup);
            if (badPwd > 0) msg += string.Format("\n其中 {0} 台密码无法解密，连接时需手动输入。", badPwd);
            MessageBox.Show(this, msg, "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
