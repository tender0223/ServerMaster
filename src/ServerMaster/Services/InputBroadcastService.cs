using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Rdp;
using ServerMaster.Win32;

namespace ServerMaster.Services
{
    /// <summary>
    /// 输入广播服务（IMessageFilter，只旁听、永不拦截）：
    /// 识别发往"主控会话"的键盘/鼠标 Windows 消息，镜像到所有"广播目标"会话。
    ///   键盘：提取扫描码 → 目标会话 RDP 官方 SendKeys 注入；
    ///   鼠标：坐标按目标分格尺寸线性换算 → PostMessage 注入。
    /// 主控会话本身始终正常响应（广播是叠加，不是抢占）。
    /// </summary>
    public class InputBroadcastService : IMessageFilter
    {
        private readonly List<RdpSession> _targets = new List<RdpSession>();
        private IntPtr _masterRoot = IntPtr.Zero;
        private DateTime _lastMoveCast = DateTime.MinValue;

        /// <summary>广播总开关（工具栏切换）。</summary>
        public bool Enabled { get; set; }

        /// <summary>主控会话（最近点击/聚焦的分格）。</summary>
        public RdpSession Master { get; private set; }

        public void SetMaster(RdpSession session)
        {
            Master = session;
            _masterRoot = (session == null) ? IntPtr.Zero : session.Handle;
        }

        public void SetTargets(IEnumerable<RdpSession> sessions)
        {
            _targets.Clear();
            if (sessions == null) return;
            foreach (RdpSession s in sessions)
            {
                if (s != null && s != Master && s.State == SessionState.Connected)
                    _targets.Add(s);
            }
        }

        public int TargetCount { get { return _targets.Count; } }

        public bool PreFilterMessage(ref Message m)
        {
            if (!Enabled || _masterRoot == IntPtr.Zero || _targets.Count == 0) return false;

            int msg = m.Msg;
            bool isKey = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_KEYUP
                      || msg == NativeMethods.WM_SYSKEYDOWN || msg == NativeMethods.WM_SYSKEYUP;
            bool isMouse = (msg >= NativeMethods.WM_MOUSEMOVE && msg <= NativeMethods.WM_XBUTTONUP);
            if (!isKey && !isMouse) return false;

            // 消息目标必须属于主控 RDP 控件窗口树（控件本体或其内部子窗口）。
            // 注意不能用 GA_ROOT：那会走到顶层窗体，主控控件是中间子窗口，永远匹配不上。
            if (m.HWnd == IntPtr.Zero) return false;
            if (!BelongsToMaster(m.HWnd)) return false;

            if (isKey) CastKey(m);
            else CastMouse(m);

            return false; // 永不拦截：主控会话照常接收输入
        }

        /// <summary>窗口是否属于主控控件（自身或其子孙窗口）。</summary>
        private bool BelongsToMaster(IntPtr hwnd)
        {
            IntPtr p = hwnd;
            while (p != IntPtr.Zero)
            {
                if (p == _masterRoot) return true;
                p = NativeMethods.GetParent(p);
            }
            return false;
        }

        private void CastKey(Message m)
        {
            int vk = m.WParam.ToInt32() & 0xFF;
            long lp = m.LParam.ToInt64();
            int scan = (int)((lp >> 16) & 0xFF);
            bool extended = (lp & (1L << 24)) != 0;
            bool keyUp = (m.Msg == NativeMethods.WM_KEYUP || m.Msg == NativeMethods.WM_SYSKEYUP);

            if (scan == 0)
            {
                // 部分注入按键无扫描码，用虚拟键兜底映射
                int s;
                bool ext;
                if (!KeyMapper.TryGetScanCode((Keys)vk, out s, out ext)) return;
                scan = s;
                extended = ext;
            }

            foreach (RdpSession t in _targets) t.SendScanCode(scan, extended, keyUp);
        }

        private void CastMouse(Message m)
        {
            int msg = m.Msg;

            if (msg == NativeMethods.WM_MOUSEMOVE)
            {
                // 空移动按 30ms 节流；拖拽(有按键按下)时不节流，保证轨迹连续
                int keys = m.WParam.ToInt32() & 0xFFFF;
                bool dragging = (keys & (NativeMethods.MK_LBUTTON | NativeMethods.MK_RBUTTON | NativeMethods.MK_MBUTTON)) != 0;
                if (!dragging && (DateTime.Now - _lastMoveCast).TotalMilliseconds < 30) return;
                _lastMoveCast = DateTime.Now;
            }

            if (Master == null) return;
            Size masterSize = Master.GetClientSize();
            if (masterSize.Width <= 0 || masterSize.Height <= 0) return;

            if (msg == NativeMethods.WM_MOUSEWHEEL)
            {
                // 滚轮 lParam 是屏幕坐标，全局有效，直接转发
                foreach (RdpSession t in _targets)
                {
                    if (t.Handle != IntPtr.Zero)
                        NativeMethods.PostMessage(t.Handle, msg, m.WParam, m.LParam);
                }
                return;
            }

            // lParam 客户区坐标：消息目标窗口(可能是 ActiveX 内部子窗口) → 主控件客户区
            NativeMethods.POINT pt = new NativeMethods.POINT();
            pt.X = NativeMethods.GET_X_LPARAM(m.LParam);
            pt.Y = NativeMethods.GET_Y_LPARAM(m.LParam);
            NativeMethods.MapWindowPoints(m.HWnd, _masterRoot, ref pt, 1);
            if (pt.X < 0) pt.X = 0;
            if (pt.Y < 0) pt.Y = 0;

            foreach (RdpSession t in _targets)
            {
                IntPtr hwnd = t.Handle;
                if (hwnd == IntPtr.Zero) continue;
                Size ts = t.GetClientSize();
                if (ts.Width <= 0 || ts.Height <= 0) continue;

                // SmartSizing 为线性缩放，坐标按比例换算即可
                int tx = pt.X * ts.Width / masterSize.Width;
                int ty = pt.Y * ts.Height / masterSize.Height;
                if (tx > ts.Width - 1) tx = ts.Width - 1;
                if (ty > ts.Height - 1) ty = ts.Height - 1;

                NativeMethods.PostMessage(hwnd, msg, m.WParam, NativeMethods.MAKELPARAM(tx, ty));
            }
        }
    }
}
