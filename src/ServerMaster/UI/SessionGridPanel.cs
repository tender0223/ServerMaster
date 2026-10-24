using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ServerMaster.UI
{
    /// <summary>
    /// 多画面网格布局引擎：自动近方阵 / 固定列数；支持单画面满幅放大模式。
    /// </summary>
    public class SessionGridPanel : Panel
    {
        private readonly List<SessionTile> _tiles = new List<SessionTile>();
        private SessionTile _zoomed;

        /// <summary>0=自动(⌈√n⌉列)，1~4=固定列数。</summary>
        public int MaxColumns { get; set; }

        public SessionTile Zoomed { get { return _zoomed; } }
        public IList<SessionTile> Tiles { get { return _tiles; } }

        public SessionGridPanel()
        {
            BackColor = System.Drawing.Color.FromArgb(24, 24, 27);
            Dock = DockStyle.Fill;
            Padding = new Padding(0);
        }

        public void AddTile(SessionTile tile)
        {
            _tiles.Add(tile);
            Controls.Add(tile);
            Relayout();
        }

        public void RemoveTile(SessionTile tile)
        {
            if (_zoomed == tile) _zoomed = null;
            _tiles.Remove(tile);
            Controls.Remove(tile);
            if (_zoomed != null) _zoomed.SetZoomed(true);
            Relayout();
        }

        /// <summary>双击标题/放大按钮：切换单画面满幅模式。</summary>
        public void ToggleZoom(SessionTile tile)
        {
            if (_zoomed == tile)
            {
                _zoomed.SetZoomed(false);
                _zoomed = null;
                foreach (SessionTile t in _tiles) t.Visible = true;
            }
            else
            {
                if (_zoomed != null) _zoomed.SetZoomed(false);
                _zoomed = tile;
                tile.SetZoomed(true);
                foreach (SessionTile t in _tiles) t.Visible = (t == tile);
            }
            Relayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Relayout();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        private void Relayout()
        {
            if (_tiles.Count == 0) return;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            if (_zoomed != null)
            {
                _zoomed.SetBounds(0, 0, w, h);
                _zoomed.BringToFront();
                return;
            }

            int n = _tiles.Count;
            int cols = MaxColumns > 0 ? MaxColumns : (int)Math.Ceiling(Math.Sqrt(n));
            if (cols < 1) cols = 1;
            if (cols > n) cols = n;
            int rows = (int)Math.Ceiling(n / (double)cols);

            const int gap = 4;
            int tw = Math.Max(80, (w - gap * (cols + 1)) / cols);
            int th = Math.Max(60, (h - gap * (rows + 1)) / rows);

            for (int i = 0; i < n; i++)
            {
                int r = i / cols;
                int c = i % cols;
                _tiles[i].Visible = true;
                _tiles[i].SetBounds(gap + c * (tw + gap), gap + r * (th + gap), tw, th);
            }
        }
    }
}
