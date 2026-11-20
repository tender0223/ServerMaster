using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Services;

namespace ServerMaster.UI
{
    /// <summary>
    /// 按文本批量添加服务器：每行一台，格式 ip:端口|账号|密码|备注|分组。
    /// 端口省略默认 3389，账号省略默认 Administrator，空行与 # 开头的行忽略。
    /// </summary>
    public class BulkAddForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly TextBox _txt;
        private readonly Label _lblStat;

        /// <summary>添加完成后由 MainForm 读取。</summary>
        public int AddedCount { get; private set; }
        public int SkippedDuplicateCount { get; private set; }
        public int InvalidCount { get; private set; }

        public BulkAddForm(AppConfig cfg)
        {
            _cfg = cfg;

            Text = "文本批量添加服务器";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(680, 480);
            MinimumSize = new Size(560, 400);

            Label l1 = new Label();
            l1.Text = "每行一台，格式：ip:端口|账号|密码|备注|分组";
            l1.AutoSize = true;
            l1.Location = new Point(14, 12);
            Controls.Add(l1);

            Label l2 = new Label();
            l2.Text = "端口省略默认 3389；账号省略默认 Administrator；密码可留空；备注、分组可省略；以 # 开头的行不处理。";
            l2.AutoSize = true;
            l2.ForeColor = Color.FromArgb(110, 110, 115);
            l2.Location = new Point(14, 34);
            Controls.Add(l2);

            Label l3 = new Label();
            l3.Text = "192.168.1.10:3389|Administrator|P@ssw0rd|Web服务器01|生产环境";
            l3.AutoSize = true;
            l3.Font = new Font("Consolas", 9F);
            l3.ForeColor = Color.FromArgb(0, 102, 170);
            l3.Location = new Point(14, 56);
            Controls.Add(l3);

            _txt = new TextBox();
            _txt.Multiline = true;
            _txt.ScrollBars = ScrollBars.Both;
            _txt.AcceptsReturn = true;
            _txt.WordWrap = false;
            _txt.Font = new Font("Consolas", 9F);
            _txt.SetBounds(14, 84, 652, 330);
            _txt.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _txt.TextChanged += delegate { UpdateStat(); };
            Controls.Add(_txt);

            _lblStat = new Label();
            _lblStat.Text = "待添加 0 行";
            _lblStat.AutoSize = true;
            _lblStat.ForeColor = Color.FromArgb(110, 110, 115);
            _lblStat.Location = new Point(14, 426);
            _lblStat.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            Controls.Add(_lblStat);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(576, 420, 90, 30);
            btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            Button btnOk = new Button();
            btnOk.Text = "添加";
            btnOk.SetBounds(478, 420, 90, 30);
            btnOk.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnOk.Click += btnAdd_Click;
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            ActiveControl = _txt;
        }

        private void UpdateStat()
        {
            int total = 0, skipped = 0;
            foreach (string raw in _txt.Lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) { skipped++; continue; }
                total++;
            }
            _lblStat.Text = string.Format("待添加 {0} 行（空行/注释 {1} 行）", total, skipped);
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            List<ServerInfo> parsed = new List<ServerInfo>();
            List<int> bad = new List<int>();
            HashSet<string> seen = new HashSet<string>();
            string[] lines = _txt.Lines;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                ServerInfo s = ParseLine(line);
                if (s == null) { bad.Add(i + 1); continue; }

                string key = s.Host.ToLowerInvariant() + "|" + s.Port;
                if (!seen.Add(key) || Exists(s.Host, s.Port))
                {
                    SkippedDuplicateCount++;
                    continue;
                }
                parsed.Add(s);
            }

            InvalidCount = bad.Count;
            if (parsed.Count == 0)
            {
                string msg = "没有可添加的服务器。";
                if (bad.Count > 0) msg += "\n格式不正确的行：" + FormatLines(bad);
                MessageBox.Show(this, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (bad.Count > 0)
            {
                DialogResult r = MessageBox.Show(this,
                    "有 " + bad.Count + " 行格式不正确，将忽略这些行继续添加。\n" +
                    "行号：" + FormatLines(bad) + "\n\n继续？",
                    "格式提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            foreach (ServerInfo s in parsed)
            {
                if (!_cfg.Groups.Contains(s.Group)) _cfg.Groups.Add(s.Group);
                _cfg.Servers.Add(s);
            }
            AddedCount = parsed.Count;

            MessageBox.Show(this,
                string.Format("已添加 {0} 台服务器。", AddedCount) +
                (SkippedDuplicateCount > 0 ? string.Format("\n跳过已存在 {0} 台。", SkippedDuplicateCount) : ""),
                "添加完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string FormatLines(List<int> lines)
        {
            int max = Math.Min(lines.Count, 10);
            string s = "";
            for (int i = 0; i < max; i++)
            {
                if (i > 0) s += ", ";
                s += lines[i];
            }
            if (lines.Count > max) s += " …";
            return s;
        }

        /// <summary>解析一行；格式不合法返回 null。分隔符兼容全角 ｜。</summary>
        private ServerInfo ParseLine(string line)
        {
            string[] parts = line.Replace('｜', '|').Split(new char[] { '|' }, 5);
            string hp = parts[0].Trim().Replace('：', ':');
            if (hp.Length == 0) return null;

            bool bracketed = hp[0] == '[';
            string host;
            int port;
            HostPort.Split(hp, out host, out port);
            host = host.Trim();
            // 非 [IPv6] 形式拆完仍有冒号，说明端口不是数字
            if (host.Length == 0 || (!bracketed && host.IndexOf(':') >= 0)) return null;
            if (port <= 0) port = 3389;
            if (port > 65535) return null;

            ServerInfo s = new ServerInfo();
            s.Host = host;
            s.Port = port;
            s.Username = parts.Length > 1 ? parts[1].Trim() : "";
            if (s.Username.Length == 0) s.Username = "Administrator";

            string pwd = parts.Length > 2 ? parts[2] : "";
            if (pwd.Length > 0) s.EncryptedPassword = PasswordProtector.Protect(pwd);

            s.Name = parts.Length > 3 ? parts[3].Trim() : "";
            s.Group = parts.Length > 4 ? parts[4].Trim() : "";
            if (s.Group.Length == 0) s.Group = "默认分组";
            s.ColorDepth = _cfg.Settings.DefaultColorDepth;
            s.AutoReconnect = true;
            return s;
        }

        private bool Exists(string host, int port)
        {
            string h = host.ToLowerInvariant();
            foreach (ServerInfo s in _cfg.Servers)
            {
                string sh;
                int sp;
                HostPort.Split(s.Host, out sh, out sp);
                if (sh.ToLowerInvariant() == h && s.Port == port) return true;
            }
            return false;
        }
    }
}
