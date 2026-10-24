using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ServerMaster.Models;
using ServerMaster.Rdp;
using ServerMaster.Win32;

namespace ServerMaster.Services
{
    /// <summary>
    /// 输入广播服务（同步器）。两条独立链路，全部"旁听、不拦截"：
    ///   键盘：WH_KEYBOARD_LL 低级钩子（系统级捕获，任何控件都吞不掉按键），
    ///         仅当主控 RDP 控件持有键盘焦点时，把 vk+扫描码镜像到目标会话；
    ///   鼠标：IMessageFilter 旁听主控控件窗口树的消息，坐标线性换算后 PostMessage。
    /// 注入统一用 PostMessage 投递完整 WM_KEYDOWN/WM_xBUTTONDOWN（与真实输入同一处理路径）。
    /// </summary>
    public class InputBroadcastService : IMessageFilter
    {
        private readonly List<RdpSession> _targets = new List<RdpSession>();
        private IntPtr _masterRoot = IntPtr.Zero;
        private DateTime _lastMoveCast = DateTime.MinValue;
        private IntPtr _hookId = IntPtr.Zero;
        private readonly NativeMethods.HookProc _hookProc; // 强引用防委托被 GC
        private int _dbgBudget;      // 每次开启广播后的调试日志条数预算
        private long _mirroredKeys;  // 已镜像按键计数(状态栏显示)

        /// <summary>已镜像的按键总数（用于状态栏实时反馈）。</summary>
        public long MirroredKeyCount { get { return _mirroredKeys; } }

        public InputBroadcastService()
        {
            _hookProc = new NativeMethods.HookProc(KeyboardHookProc);
        }

        /// <summary>广播总开关（工具栏切换）。</summary>
        public bool Enabled { get; private set; }

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

        #region 键盘：低级钩子捕获 + PostMessage 注入

        /// <summary>开启广播：安装 WH_KEYBOARD_LL 钩子（回调在本 UI 线程消息循环派发）。</summary>
        public void Enable()
        {
            if (Enabled) return;
            Enabled = true;
            _dbgBudget = 60;
            _mirroredKeys = 0;
            if (_hookId == IntPtr.Zero)
            {
                IntPtr mod = NativeMethods.GetModuleHandle(null);
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL,
                    _hookProc, mod, 0);
                AppLogger.Info("键盘广播钩子安装: " + (_hookId != IntPtr.Zero ? "OK" : "失败 err=" + Marshal.GetLastWin32Error()));
            }
        }

        public void Disable()
        {
            Enabled = false;
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                AppLogger.Info("键盘广播钩子卸载");
            }
        }

        private IntPtr KeyboardHookProc(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && Enabled && _targets.Count > 0 && _masterRoot != IntPtr.Zero)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_KEYUP ||
                    msg == NativeMethods.WM_SYSKEYDOWN || msg == NativeMethods.WM_SYSKEYUP)
                {
                    NativeMethods.KBDLLHOOKSTRUCT k =
                        (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    bool keyUp = (k.flags & NativeMethods.LLKHF_UP) != 0;
                    bool extended = (k.flags & NativeMethods.LLKHF_EXTENDED) != 0;
                    int vk = (int)k.vkCode;
                    int scan = (int)k.scanCode;
                    if (scan == 0)
                    {
                        int s;
                        bool ext;
                        if (KeyMapper.TryGetScanCode((Keys)vk, out s, out ext)) { scan = s; extended = ext; }
                    }

                    if (MasterHasFocus())
                    {
                        if (_dbgBudget > 0)
                        {
                            _dbgBudget--;
                            AppLogger.Info(string.Format(
                                "钩子按键 vk=0x{0:X2} scan=0x{1:X2} up={2} → 镜像到 {3} 个目标",
                                vk, scan, keyUp, _targets.Count));
                        }
                        _mirroredKeys++;
                        foreach (RdpSession t in _targets) t.PostVirtualKey(vk, scan, extended, keyUp);
                    }
                    else if (_dbgBudget > 0)
                    {
                        // 未镜像:记录焦点细节,便于定位主控判定失败原因
                        _dbgBudget--;
                        IntPtr fg = NativeMethods.GetForegroundWindow();
                        uint pid;
                        uint fgTid = NativeMethods.GetWindowThreadProcessId(fg, out pid);
                        IntPtr focus = NativeMethods.GetFocus();
                        AppLogger.Info(string.Format(
                            "钩子按键 vk=0x{0:X2} 未镜像(焦点不在主控): focus=0x{1:X} master=0x{2:X} fg=0x{3:X} 前台线程匹配={4}",
                            vk, focus.ToInt64(), _masterRoot.ToInt64(), fg.ToInt64(),
                            fgTid == NativeMethods.GetCurrentThreadId()));
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hookId, code, wParam, lParam);
        }

        /// <summary>主控判定：本程序在前台，且主控 RDP 控件（或其内部子窗口）持有键盘焦点。</summary>
        private bool MasterHasFocus()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            uint pid;
            uint tid = NativeMethods.GetWindowThreadProcessId(fg, out pid);
            if (tid != NativeMethods.GetCurrentThreadId()) return false;
            IntPtr focus = NativeMethods.GetFocus();
            if (focus == IntPtr.Zero) return false;
            return BelongsToMaster(focus);
        }

        #endregion

        #region 鼠标：消息过滤器旁听 + PostMessage 注入

        public bool PreFilterMessage(ref Message m)
        {
            if (!Enabled || _masterRoot == IntPtr.Zero || _targets.Count == 0) return false;

            int msg = m.Msg;
            bool isMouse = (msg >= NativeMethods.WM_MOUSEMOVE && msg <= NativeMethods.WM_XBUTTONUP);
            if (!isMouse) return false;

            if (m.HWnd == IntPtr.Zero) return false;
            if (!BelongsToMaster(m.HWnd)) return false;

            CastMouse(m);
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

        #endregion
    }
}
