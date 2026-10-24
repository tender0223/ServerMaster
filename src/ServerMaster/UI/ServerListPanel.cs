using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ServerMaster.Models;

namespace ServerMaster.UI
{
    /// <summary>
    /// 左侧服务器管理面板：分组树 + 勾选批量连接 + 右键操作（分组/服务器区分）+ 搜索过滤。
    /// 支持按组删除、全部删除。
    /// </summary>
    public class ServerListPanel : Panel
    {
        private readonly TextBox _txtSearch;
        private readonly TreeView _tree;
        private readonly Button _btnAdd;
        private readonly Button _btnEdit;
        private readonly Button _btnDelete;
        private readonly Button _btnConnect;
        private readonly Button _btnDisconnect;
        private readonly Button _btnDeleteAll;
        private readonly ContextMenuStrip _serverMenu;
        private readonly ContextMenuStrip _groupMenu;
        private AppConfig _cfg;
        private string _filter = "";
        private readonly Dictionary<string, bool> _checkState = new Dictionary<string, bool>();

        public event Action AddRequested;
        public event Action<ServerInfo> EditRequested;
        public event Action<ServerInfo> DeleteRequested;
        public event Action<ServerInfo> ConnectRequested;
        public event Action<ServerInfo> DisconnectRequested;
        /// <summary>连接整组服务器（参数：分组名）。</summary>
        public event Action<string> ConnectGroupRequested;
        /// <summary>删除某分组下的全部服务器（参数：分组名）。</summary>
        public event Action<string> DeleteGroupRequested;
        /// <summary>删除全部服务器。</summary>
        public event Action DeleteAllRequested;
        /// <summary>删除勾选的服务器（组节点勾选会联动组内全部服务器）。</summary>
        public event Action<List<ServerInfo>> DeleteCheckedRequested;

        public ServerListPanel()
        {
            Dock = DockStyle.Fill;

            Label lblSearch = new Label();
            lblSearch.Text = "搜索:";
            lblSearch.AutoSize = true;
            lblSearch.Location = new Point(8, 10);
            Controls.Add(lblSearch);

            _txtSearch = new TextBox();
            _txtSearch.Location = new Point(52, 6);
            _txtSearch.Width = 180;
            _txtSearch.TextChanged += delegate { _filter = _txtSearch.Text.Trim(); Rebind(); };
            Controls.Add(_txtSearch);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Location = new Point(4, 32);
            buttons.Size = new Size(240, 56);

            _btnAdd = MakeButton("添加", delegate { if (AddRequested != null) AddRequested(); });
            _btnConnect = MakeButton("连接", delegate { OnConnectSelected(); });
            _btnDisconnect = MakeButton("断开", delegate { OnDisconnectSelected(); });
            _btnEdit = MakeButton("编辑", delegate { OnEditSelected(); });
            _btnDelete = MakeButton("删除勾选", delegate { OnDeleteChecked(); });
            _btnDeleteAll = MakeButton("全部删除", delegate { if (DeleteAllRequested != null) DeleteAllRequested(); });
            buttons.Controls.Add(_btnAdd);
            buttons.Controls.Add(_btnConnect);
            buttons.Controls.Add(_btnDisconnect);
            buttons.Controls.Add(_btnEdit);
            buttons.Controls.Add(_btnDelete);
            buttons.Controls.Add(_btnDeleteAll);
            Controls.Add(buttons);

            _tree = new TreeView();
            _tree.Location = new Point(0, 92);
            _tree.Size = new Size(250, 400);
            _tree.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _tree.CheckBoxes = true;
            _tree.ShowLines = true;
            _tree.HideSelection = false;
            _tree.ItemHeight = 22;
            _tree.AfterCheck += tree_AfterCheck;
            _tree.NodeMouseDoubleClick += tree_NodeMouseDoubleClick;

            // 服务器节点右键菜单
            _serverMenu = new ContextMenuStrip();
            _serverMenu.Items.Add("连接", null, delegate { OnConnectSelected(); });
            _serverMenu.Items.Add("断开", null, delegate { OnDisconnectSelected(); });
            _serverMenu.Items.Add(new ToolStripSeparator());
            _serverMenu.Items.Add("编辑…", null, delegate { OnEditSelected(); });
            _serverMenu.Items.Add("删除", null, delegate { OnDeleteSelected(); });

            // 分组节点右键菜单
            _groupMenu = new ContextMenuStrip();
            _groupMenu.Items.Add("连接该组服务器", null, delegate
            {
                string g = SelectedGroup();
                if (g != null && ConnectGroupRequested != null) ConnectGroupRequested(g);
            });
            _groupMenu.Items.Add(new ToolStripSeparator());
            _groupMenu.Items.Add("删除该组服务器…", null, delegate
            {
                string g = SelectedGroup();
                if (g != null && DeleteGroupRequested != null) DeleteGroupRequested(g);
            });

            _tree.ContextMenuStrip = _serverMenu;
            _tree.NodeMouseClick += delegate(object s, TreeNodeMouseClickEventArgs e)
            {
                _tree.SelectedNode = e.Node;
                if (e.Button == MouseButtons.Right)
                    _tree.ContextMenuStrip = e.Node.Tag is ServerInfo ? _serverMenu : _groupMenu;
            };

            Controls.Add(_tree);

            // 树在按钮下方自适应
            _tree.SetBounds(0, 92, Width, Math.Max(60, Height - 92));
            Resize += delegate { _tree.SetBounds(0, 92, Width, Math.Max(60, Height - 92)); _txtSearch.Width = Math.Max(60, Width - 60); };
        }

        private Button MakeButton(string text, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Padding = new Padding(4, 0, 4, 0);
            b.Margin = new Padding(1);
            b.Click += onClick;
            return b;
        }

        public void Rebind(AppConfig cfg)
        {
            _cfg = cfg;
            Rebind();
        }

        public void Rebind()
        {
            if (_cfg == null) return;
            _tree.BeginUpdate();
            try
            {
                // 保留勾选状态
                foreach (TreeNode n in _tree.Nodes) CollectCheckState(n);

                _tree.Nodes.Clear();

                List<string> groups = new List<string>(_cfg.Groups);
                foreach (ServerInfo s in _cfg.Servers)
                    if (!groups.Contains(s.Group)) groups.Add(s.Group);

                foreach (string g in groups)
                {
                    TreeNode gNode = new TreeNode(g);
                    gNode.Tag = g; // 分组节点 Tag = 分组名
                    gNode.ForeColor = Color.FromArgb(90, 120, 200);
                    bool anyShown = false;
                    foreach (ServerInfo s in _cfg.Servers)
                    {
                        if (s.Group != g) continue;
                        if (_filter.Length > 0 &&
                            s.DisplayName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            s.Host.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        TreeNode sNode = new TreeNode(s.DisplayName + "  (" + s.Host + ")");
                        sNode.Tag = s;
                        bool chk;
                        if (_checkState.TryGetValue(s.Id, out chk)) sNode.Checked = chk;
                        gNode.Nodes.Add(sNode);
                        anyShown = true;
                    }
                    if (anyShown || _filter.Length == 0) _tree.Nodes.Add(gNode);
                    if (anyShown) gNode.Expand();
                }
            }
            finally
            {
                _tree.EndUpdate();
            }
        }

        private void CollectCheckState(TreeNode node)
        {
            if (node.Tag is ServerInfo)
            {
                ServerInfo s = (ServerInfo)node.Tag;
                _checkState[s.Id] = node.Checked;
            }
            foreach (TreeNode c in node.Nodes) CollectCheckState(c);
        }

        private void tree_AfterCheck(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is ServerInfo) return;
            // 分组节点勾选 → 同步所有子服务器
            foreach (TreeNode c in e.Node.Nodes)
            {
                c.Checked = e.Node.Checked;
                ServerInfo s = c.Tag as ServerInfo;
                if (s != null) _checkState[s.Id] = e.Node.Checked;
            }
        }

        private void tree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            ServerInfo s = e.Node.Tag as ServerInfo;
            if (s != null && ConnectRequested != null) ConnectRequested(s);
        }

        private ServerInfo SelectedServer()
        {
            TreeNode n = _tree.SelectedNode;
            while (n != null && !(n.Tag is ServerInfo)) n = n.Parent;
            return n == null ? null : (ServerInfo)n.Tag;
        }

        /// <summary>当前选中的分组（分组节点自身，或服务器节点所属分组）；无选中返回 null。</summary>
        private string SelectedGroup()
        {
            TreeNode n = _tree.SelectedNode;
            while (n != null)
            {
                if (n.Tag is ServerInfo) return ((ServerInfo)n.Tag).Group;
                string g = n.Tag as string;
                if (g != null) return g;
                n = n.Parent;
            }
            return null;
        }

        private void OnConnectSelected()
        {
            ServerInfo s = SelectedServer();
            if (s != null && ConnectRequested != null) ConnectRequested(s);
        }

        private void OnDisconnectSelected()
        {
            ServerInfo s = SelectedServer();
            if (s != null && DisconnectRequested != null) DisconnectRequested(s);
        }

        private void OnEditSelected()
        {
            ServerInfo s = SelectedServer();
            if (s != null && EditRequested != null) EditRequested(s);
        }

        private void OnDeleteSelected()
        {
            ServerInfo s = SelectedServer();
            if (s != null && DeleteRequested != null) DeleteRequested(s);
        }

        /// <summary>删除按钮：删除所有勾选的服务器；没有任何勾选时删除当前选中的一台。</summary>
        private void OnDeleteChecked()
        {
            List<ServerInfo> targets = GetCheckedServers();
            if (targets.Count == 0)
            {
                ServerInfo sel = SelectedServer();
                if (sel != null) targets.Add(sel);
            }
            if (targets.Count > 0 && DeleteCheckedRequested != null) DeleteCheckedRequested(targets);
        }

        /// <summary>勾选的服务器（未勾选任何一台时由调用方决定是否返回全部）。</summary>
        public List<ServerInfo> GetCheckedServers()
        {
            List<ServerInfo> result = new List<ServerInfo>();
            foreach (TreeNode g in _tree.Nodes)
                foreach (TreeNode c in g.Nodes)
                {
                    ServerInfo s = c.Tag as ServerInfo;
                    if (s != null && c.Checked) result.Add(s);
                }
            return result;
        }
    }
}
