using System;
using System.Runtime.InteropServices;

namespace ServerMaster.Win32
{
    /// <summary>本项目用到的 Win32 API 与常量。</summary>
    internal static class NativeMethods
    {
        // ---- 键盘消息 ----
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        // ---- 鼠标消息 ----
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_LBUTTONDBLCLK = 0x0203;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_RBUTTONDBLCLK = 0x0206;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MBUTTONDBLCLK = 0x0209;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_XBUTTONUP = 0x020C;

        // ---- 鼠标 wParam 按键标志 ----
        public const int MK_LBUTTON = 0x0001;
        public const int MK_RBUTTON = 0x0002;
        public const int MK_SHIFT = 0x0004;
        public const int MK_CONTROL = 0x0008;
        public const int MK_MBUTTON = 0x0010;

        public const uint GA_ROOT = 2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref POINT lpPoint, uint cPoints);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanW(char ch);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        // ---- SMB 凭据会话 ----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public class NETRESOURCE
        {
            public int dwScope;
            public int dwType;          // 1 = RESOURCETYPE_DISK
            public int dwDisplayType;
            public int dwUsage;
            public string lpLocalName;
            public string lpRemoteName; // \\主机\IPC$
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetAddConnection2W(NETRESOURCE lpNetResource, string lpPassword,
                                                     string lpUserName, int dwFlags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        public static extern int WNetCancelConnection2W(string lpName, int dwFlags, bool fForce);

        // ---- lParam 工具 ----
        public static IntPtr MAKELPARAM(int low, int high)
        {
            return (IntPtr)(((high & 0xFFFF) << 16) | (low & 0xFFFF));
        }

        public static int GET_X_LPARAM(IntPtr lParam)
        {
            return (short)(lParam.ToInt64() & 0xFFFF);
        }

        public static int GET_Y_LPARAM(IntPtr lParam)
        {
            return (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        }
    }
}
