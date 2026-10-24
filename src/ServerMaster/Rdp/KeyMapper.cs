using System.Collections.Generic;
using System.Windows.Forms;

namespace ServerMaster.Rdp
{
    /// <summary>
    /// Keys(虚拟键) → PS/2 Set-1 扫描码 映射。
    /// 用于把主控会话的按键镜像注入到其他 RDP 会话（IMsRdpClientNonScriptable.SendKeys）。
    /// </summary>
    public static class KeyMapper
    {
        private static readonly Dictionary<Keys, int> _scan = new Dictionary<Keys, int>(120);
        private static readonly HashSet<Keys> _extended = new HashSet<Keys>();

        static KeyMapper()
        {
            Add(Keys.Escape, 0x01); Add(Keys.Back, 0x0E); Add(Keys.Tab, 0x0F);
            Add(Keys.Enter, 0x1C); Add(Keys.Space, 0x39); Add(Keys.CapsLock, 0x3A);

            Add(Keys.A, 0x1E); Add(Keys.B, 0x30); Add(Keys.C, 0x2E); Add(Keys.D, 0x20);
            Add(Keys.E, 0x12); Add(Keys.F, 0x21); Add(Keys.G, 0x22); Add(Keys.H, 0x23);
            Add(Keys.I, 0x17); Add(Keys.J, 0x24); Add(Keys.K, 0x25); Add(Keys.L, 0x26);
            Add(Keys.M, 0x32); Add(Keys.N, 0x31); Add(Keys.O, 0x18); Add(Keys.P, 0x19);
            Add(Keys.Q, 0x10); Add(Keys.R, 0x13); Add(Keys.S, 0x1F); Add(Keys.T, 0x14);
            Add(Keys.U, 0x16); Add(Keys.V, 0x2F); Add(Keys.W, 0x11); Add(Keys.X, 0x2D);
            Add(Keys.Y, 0x15); Add(Keys.Z, 0x2C);

            Add(Keys.D0, 0x0B); Add(Keys.D1, 0x02); Add(Keys.D2, 0x03); Add(Keys.D3, 0x04);
            Add(Keys.D4, 0x05); Add(Keys.D5, 0x06); Add(Keys.D6, 0x07); Add(Keys.D7, 0x08);
            Add(Keys.D8, 0x09); Add(Keys.D9, 0x0A);

            Add(Keys.F1, 0x3B); Add(Keys.F2, 0x3C); Add(Keys.F3, 0x3D); Add(Keys.F4, 0x3E);
            Add(Keys.F5, 0x3F); Add(Keys.F6, 0x40); Add(Keys.F7, 0x41); Add(Keys.F8, 0x42);
            Add(Keys.F9, 0x43); Add(Keys.F10, 0x44); Add(Keys.F11, 0x57); Add(Keys.F12, 0x58);

            Add(Keys.LShiftKey, 0x2A); Add(Keys.RShiftKey, 0x36);
            Add(Keys.LControlKey, 0x1D); Add(Keys.RControlKey, 0x1D, true);
            Add(Keys.LMenu, 0x38); Add(Keys.RMenu, 0x38, true);
            Add(Keys.LWin, 0x5B, true); Add(Keys.RWin, 0x5C, true);
            Add(Keys.Apps, 0x5D, true);

            Add(Keys.Left, 0x4B, true); Add(Keys.Up, 0x48, true);
            Add(Keys.Right, 0x4D, true); Add(Keys.Down, 0x50, true);
            Add(Keys.Home, 0x47, true); Add(Keys.End, 0x4F, true);
            Add(Keys.Prior, 0x49, true); Add(Keys.Next, 0x51, true);
            Add(Keys.Insert, 0x52, true); Add(Keys.Delete, 0x53, true);
            Add(Keys.PrintScreen, 0x37, true); Add(Keys.NumLock, 0x45, true);
            Add(Keys.Scroll, 0x46); Add(Keys.Pause, 0x45);

            Add(Keys.OemSemicolon, 0x27); Add(Keys.Oemplus, 0x0D);
            Add(Keys.Oemcomma, 0x33); Add(Keys.OemMinus, 0x0C);
            Add(Keys.OemPeriod, 0x34); Add(Keys.OemQuestion, 0x35);
            Add(Keys.Oemtilde, 0x29); Add(Keys.OemOpenBrackets, 0x1A);
            Add(Keys.OemPipe, 0x2B); Add(Keys.OemCloseBrackets, 0x1B);
            Add(Keys.OemQuotes, 0x28);

            Add(Keys.NumPad0, 0x52); Add(Keys.NumPad1, 0x4F); Add(Keys.NumPad2, 0x50);
            Add(Keys.NumPad3, 0x51); Add(Keys.NumPad4, 0x4B); Add(Keys.NumPad5, 0x4C);
            Add(Keys.NumPad6, 0x4D); Add(Keys.NumPad7, 0x47); Add(Keys.NumPad8, 0x48);
            Add(Keys.NumPad9, 0x49);
            Add(Keys.Multiply, 0x37); Add(Keys.Add, 0x4E);
            Add(Keys.Subtract, 0x4A); Add(Keys.Decimal, 0x53); Add(Keys.Divide, 0x35, true);
        }

        private static void Add(Keys key, int scanCode)
        {
            Add(key, scanCode, false);
        }

        private static void Add(Keys key, int scanCode, bool isExtended)
        {
            _scan[key] = scanCode;
            if (isExtended) _extended.Add(key);
        }

        public static bool TryGetScanCode(Keys key, out int scanCode, out bool isExtended)
        {
            isExtended = false;
            scanCode = 0;
            int found;
            if (!_scan.TryGetValue(key, out found)) return false;
            scanCode = found;
            isExtended = _extended.Contains(key);
            return true;
        }
    }
}
