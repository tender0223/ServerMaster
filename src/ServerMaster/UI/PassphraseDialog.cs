using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServerMaster.UI
{
    /// <summary>输入便携配置的访问密码；导出时需二次确认，导入时仅输入一次。</summary>
    public class PassphraseDialog : Form
    {
        private readonly bool _confirm;
        private readonly TextBox _txtPwd;
        private readonly TextBox _txtConfirm;
        private readonly CheckBox _chkShow;

        public string Passphrase { get; private set; }

        public PassphraseDialog(string title, string caption, bool askConfirm, string okText)
        {
            _confirm = askConfirm;
            Passphrase = "";

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(460, askConfirm ? 292 : 238);

            Panel header = new Panel();
            header.SetBounds(0, 0, 460, 4);
            header.BackColor = Color.FromArgb(0, 102, 170);
            Controls.Add(header);

            Label lblCap = new Label();
            lblCap.Text = caption;
            lblCap.SetBounds(20, 18, 420, 40);
            lblCap.ForeColor = Color.FromArgb(70, 70, 75);
            Controls.Add(lblCap);

            Label lblPwd = new Label();
            lblPwd.Text = "访问密码";
            lblPwd.AutoSize = true;
            lblPwd.Location = new Point(20, 68);
            Controls.Add(lblPwd);

            _txtPwd = new TextBox();
            _txtPwd.UseSystemPasswordChar = true;
            _txtPwd.SetBounds(20, 90, 420, 26);
            Controls.Add(_txtPwd);

            int y = 130;
            _txtConfirm = new TextBox();
            if (askConfirm)
            {
                Label lblCfm = new Label();
                lblCfm.Text = "确认密码";
                lblCfm.AutoSize = true;
                lblCfm.Location = new Point(20, y);
                Controls.Add(lblCfm);

                _txtConfirm.UseSystemPasswordChar = true;
                _txtConfirm.SetBounds(20, y + 22, 420, 26);
                Controls.Add(_txtConfirm);
                y += 62;
            }

            _chkShow = new CheckBox();
            _chkShow.Text = "显示密码";
            _chkShow.AutoSize = true;
            _chkShow.Location = new Point(20, y);
            _chkShow.CheckedChanged += delegate
            {
                _txtPwd.UseSystemPasswordChar = !_chkShow.Checked;
                _txtConfirm.UseSystemPasswordChar = !_chkShow.Checked;
            };
            Controls.Add(_chkShow);

            Label lblHint = new Label();
            lblHint.Text = askConfirm
                ? "至少 6 位。密码只用于加密本文件；忘记后无法恢复。"
                : "密码区分大小写，由导出方提供。";
            lblHint.AutoSize = true;
            lblHint.ForeColor = Color.FromArgb(110, 110, 115);
            lblHint.Location = new Point(20, y + 28);
            Controls.Add(lblHint);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.SetBounds(356, ClientSize.Height - 46, 84, 30);
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            Button btnOk = new Button();
            btnOk.Text = okText;
            btnOk.SetBounds(262, ClientSize.Height - 46, 84, 30);
            btnOk.Click += btnOk_Click;
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            ActiveControl = _txtPwd;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            string pwd = _txtPwd.Text;
            if (pwd.Length == 0)
            {
                MessageBox.Show(this, "请输入访问密码。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtPwd.Focus();
                return;
            }
            if (_confirm)
            {
                if (pwd.Length < 6)
                {
                    MessageBox.Show(this, "访问密码至少 6 位。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _txtPwd.Focus();
                    return;
                }
                if (pwd != _txtConfirm.Text)
                {
                    MessageBox.Show(this, "两次输入的密码不一致。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _txtConfirm.Focus();
                    _txtConfirm.SelectAll();
                    return;
                }
            }
            Passphrase = pwd;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
