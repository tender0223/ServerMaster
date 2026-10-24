using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using AxMSTSCLib;

// RDP 注入实验台 v6 (合并版, 需解锁+用户在场):
//  E1 前台+SetFocus+keybd_event Ctrl+Esc / E2 真实点击+keybd_event / E3 SendKeys API Win / E4 PostMessage
//  每步截图 + PrtSc 剪贴板金丝雀 + E5 记事本打字回读(终极逻辑验证)
class RdpInjectLab6
{
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint f, UIntPtr x);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr x);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScanW(char c);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint vk, uint t);
    [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(int f, bool inh, uint acc);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int idx, System.Text.StringBuilder s, int n, out int need);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool at);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    const uint KEYUP = 2;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }

    static Form _form;
    static AxMsRdpClient9NotSafeForScripting _ax;
    static TextBox _probe;
    static MSTSCLib.IMsRdpClientNonScriptable _ns;

    [STAThread]
    static void Main()
    {
        // S0: 环境自检
        IntPtr desk = OpenInputDesktop(0, false, 0x00FF);
        string dname = "?";
        if (desk != IntPtr.Zero)
        {
            var sb = new System.Text.StringBuilder(256); int n;
            GetUserObjectInformation(desk, 2, sb, 256, out n);
            dname = sb.ToString(); CloseDesktop(desk);
        }
        Console.WriteLine("session desktop=" + dname);
        if (dname != "Default") { Console.WriteLine("LOCKED-ABORT"); return; }

        var ser = new JavaScriptSerializer();
        var cfg = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServerMaster", "config.json")));
        var servers = (System.Collections.ArrayList)cfg["Servers"];
        System.Collections.Generic.Dictionary<string, object> sv = null;
        foreach (System.Collections.Generic.Dictionary<string, object> s in servers)
            if (Convert.ToString(s["Name"]) == "北少云2") { sv = s; break; }
        if (sv == null) { Console.WriteLine("server-not-found"); return; }
        string host = Convert.ToString(sv["Host"]);
        int port = Convert.ToInt32(sv["Port"]);
        string user = Convert.ToString(sv["Username"]);
        string pwd = ServerMaster.Services.PasswordProtector.Unprotect(Convert.ToString(sv["EncryptedPassword"]));
        Console.WriteLine("target: " + host + ":" + port);

        string savedClip = null;
        try { savedClip = Clipboard.GetText(); } catch { }

        SetProcessDPIAware();
        Application.EnableVisualStyles();
        _form = new Form();
        _form.Text = "RDP-LAB6";
        _form.StartPosition = FormStartPosition.Manual;
        _form.Location = new Point(0, 0);
        _form.Size = new Size(960, 720);
        _form.TopMost = true;
        _ax = new AxMsRdpClient9NotSafeForScripting();
        _ax.Dock = DockStyle.Fill;
        _probe = new TextBox();
        _probe.Dock = DockStyle.Bottom;
        _probe.Height = 36;
        _probe.Font = new Font("Consolas", 14);
        _form.Controls.Add(_probe);
        _form.Controls.Add(_ax);
        _form.Show();
        _form.Activate();
        Application.DoEvents();
        _ns = (MSTSCLib.IMsRdpClientNonScriptable)_ax.GetOcx();

        // S1: 抢前台
        if (!EnsureForeground()) { Console.WriteLine("FG-ABORT fg=0x" + GetForegroundWindow().ToString("X")); return; }
        Console.WriteLine("FG-OK");

        // S2: 本地对照 (keybd_event -> 本地TextBox)
        SetFocus(_probe.Handle);
        Thread.Sleep(250);
        RealType("ab1");
        Thread.Sleep(350);
        Console.WriteLine("PROBE=[" + _probe.Text + "]");

        // 连接
        _ax.Server = host; _ax.UserName = user;
        _ax.DesktopWidth = 920; _ax.DesktopHeight = 600; _ax.ColorDepth = 16;
        var a2 = _ax.AdvancedSettings2;
        a2.RDPPort = port; a2.ClearTextPassword = pwd; a2.SmartSizing = true;
        a2.GrabFocusOnConnect = false;
        _ax.Connect();
        DateTime until = DateTime.Now.AddSeconds(40);
        bool ok = false;
        while (DateTime.Now < until)
        {
            Application.DoEvents(); Thread.Sleep(100);
            try { if (_ax.Connected == 1) { ok = true; break; } } catch { }
        }
        Console.WriteLine("connected: " + ok);
        if (!ok) return;
        Thread.Sleep(6000); Application.DoEvents();
        try
        {
            var ss = (MSTSCLib.IMsRdpClientSecuredSettings2)_ax.SecuredSettings;
            ss.KeyboardHookMode = 1;
            Console.WriteLine("KHM=" + ss.KeyboardHookMode);
        }
        catch (Exception ex) { Console.WriteLine("KHM-FAIL: " + ex.Message); }
        IntPtr h = _ax.Handle;
        Console.WriteLine("atl=0x" + h.ToString("X"));
        Shot("0-baseline");

        // ===== E1: 前台+SetFocus+keybd_event Ctrl+Esc =====
        Console.WriteLine(EnsureForeground() ? "E1 FG-OK" : "E1 FG-FAIL");
        SetFocus(h); Thread.Sleep(350);
        CtrlEscReal();
        WaitShot("e1");
        EscReal();
        WaitShot("e1c");
        CanaryKeybd();

        // ===== E2: 真实点击控件(远端任务栏时钟区) + keybd_event =====
        Console.WriteLine(EnsureForeground() ? "E2 FG-OK" : "E2 FG-FAIL");
        RECT r; GetWindowRect(h, out r);
        RealClickAt(r.R - 40, r.B - 12);
        Thread.Sleep(900);
        CtrlEscReal();
        WaitShot("e2");
        EscReal();
        WaitShot("e2c");

        // ===== E3: SetFocus + SendKeys API (Win) =====
        Console.WriteLine(EnsureForeground() ? "E3 FG-OK" : "E3 FG-FAIL");
        SetFocus(h); Thread.Sleep(350);
        SkKey(0x5B, false, 0); // Win down
        Thread.Sleep(2500); Application.DoEvents();
        Shot("e3");
        SkKey(0x5B, true, 0); // Win up
        Thread.Sleep(400);
        SkEsc();
        WaitShot("e3c");
        CanarySendKeys();

        // ===== E4: 焦点在别处 + PostMessage =====
        SetFocus(_probe.Handle); Thread.Sleep(350);
        Console.WriteLine("E4 start");
        PostKey(h, 0x11, 0x1D, false); Thread.Sleep(70);
        PostKey(h, 0x1B, 0x01, false); Thread.Sleep(90);
        PostKey(h, 0x1B, 0x01, true); Thread.Sleep(70);
        PostKey(h, 0x11, 0x1D, true);
        WaitShot("e4");
        PostKey(h, 0x1B, 0x01, false); Thread.Sleep(60);
        PostKey(h, 0x1B, 0x01, true);
        WaitShot("e4c");

        // ===== E5: SendKeys 记事本打字回读 (终极验证) =====
        Console.WriteLine(EnsureForeground() ? "E5 FG-OK" : "E5 FG-FAIL");
        SetFocus(h); Thread.Sleep(350);
        try { Clipboard.Clear(); } catch { }
        Thread.Sleep(500);
        SkCtrlEsc();
        Thread.Sleep(1200);
        SkType("notepad");
        Thread.Sleep(500);
        SkKey(0x1C, false, 0); SkKey(0x1C, true, 0); // Enter
        Thread.Sleep(8000); Application.DoEvents();
        SkType("INJPROOFC");
        Thread.Sleep(600);
        Shot("e5-typed");
        SkCtrlKey(0x1E); Thread.Sleep(300); // Ctrl+A
        SkCtrlKey(0x2E); Thread.Sleep(300); // Ctrl+C
        Thread.Sleep(2500); Application.DoEvents();
        string clip = null;
        try { clip = Clipboard.GetText(); } catch { }
        Console.WriteLine("E5-CLIP=[" + (clip == null ? "<null>" : clip) + "]");
        bool e5ok = clip != null && clip.Contains("INJPROOFC");
        Console.WriteLine(e5ok ? "E5-VERDICT: PASS (SendKeys notepad roundtrip OK)" : "E5-VERDICT: FAIL");
        if (e5ok)
        {
            SkAltF4(); Thread.Sleep(900); SkAltN(); Thread.Sleep(900);
            Shot("e5-closed");
        }

        if (savedClip != null) { try { Clipboard.SetText(savedClip); } catch { } }
        Console.WriteLine("ALL-DONE");
        Thread.Sleep(1200);
        try { _ax.Disconnect(); } catch { }
        _form.Close();
    }

    static bool EnsureForeground()
    {
        if (GetForegroundWindow() == _form.Handle) return true;
        IntPtr fg = GetForegroundWindow();
        uint myTid = GetCurrentThreadId();
        uint fgTid = 0; uint pid;
        if (fg != IntPtr.Zero) fgTid = GetWindowThreadProcessId(fg, out pid);
        bool attached = fgTid != 0 && fgTid != myTid && AttachThreadInput(myTid, fgTid, true);
        try
        {
            keybd_event(0x12, 0x38, 0, UIntPtr.Zero);
            keybd_event(0x12, 0x38, KEYUP, UIntPtr.Zero);
            SetForegroundWindow(_form.Handle);
            Application.DoEvents(); Thread.Sleep(120); Application.DoEvents();
        }
        finally { if (attached) AttachThreadInput(myTid, fgTid, false); }
        if (GetForegroundWindow() == _form.Handle) return true;
        RealClickAt(_form.Left + _form.Width / 2, _form.Top + 10);
        Application.DoEvents(); Thread.Sleep(300); Application.DoEvents();
        return GetForegroundWindow() == _form.Handle;
    }

    static void RealClickAt(int x, int y)
    {
        Rectangle sb = Screen.PrimaryScreen.Bounds;
        uint nx = (uint)(x * 65536 / sb.Width);
        uint ny = (uint)(y * 65536 / sb.Height);
        mouse_event(0x8000 | 0x0001, nx, ny, 0, UIntPtr.Zero);
        Thread.Sleep(70);
        mouse_event(0x8000 | 0x0002, nx, ny, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(0x8000 | 0x0004, nx, ny, 0, UIntPtr.Zero);
        Console.WriteLine("real-click " + x + "," + y);
    }

    static void CtrlEscReal()
    {
        keybd_event(0x11, 0x1D, 0, UIntPtr.Zero);
        keybd_event(0x1B, 0x01, 0, UIntPtr.Zero);
        keybd_event(0x1B, 0x01, KEYUP, UIntPtr.Zero);
        keybd_event(0x11, 0x1D, KEYUP, UIntPtr.Zero);
        Thread.Sleep(2200); Application.DoEvents();
    }

    static void EscReal()
    {
        keybd_event(0x1B, 0x01, 0, UIntPtr.Zero);
        keybd_event(0x1B, 0x01, KEYUP, UIntPtr.Zero);
        Thread.Sleep(1300); Application.DoEvents();
    }

    static void RealType(string text)
    {
        foreach (char c in text)
        {
            short ks = VkKeyScanW(c);
            byte vk = (byte)(ks & 0xFF);
            byte scan = (byte)MapVirtualKey(vk, 0);
            keybd_event(vk, scan, 0, UIntPtr.Zero);
            Thread.Sleep(35);
            keybd_event(vk, scan, KEYUP, UIntPtr.Zero);
            Thread.Sleep(55); Application.DoEvents();
        }
    }

    // ---- SendKeys API ----
    static void SkKey(int scan, bool up, int ext)
    {
        bool u = up;
        int data = (scan & 0xFF) | ((ext != 0) ? 0xE000 : 0);
        _ns.SendKeys(1, ref u, ref data);
    }

    static void SkEsc()
    {
        SkKey(0x01, false, 0); Thread.Sleep(60);
        SkKey(0x01, true, 0);
        Thread.Sleep(1300); Application.DoEvents();
    }

    static void SkCtrlEsc()
    {
        SkKey(0x1D, false, 0); Thread.Sleep(70);
        SkKey(0x01, false, 0); Thread.Sleep(100);
        SkKey(0x01, true, 0); Thread.Sleep(70);
        SkKey(0x1D, true, 0);
    }

    static void SkType(string text)
    {
        foreach (char c in text)
        {
            short ks = VkKeyScanW(c);
            byte vk = (byte)(ks & 0xFF);
            byte scan = (byte)MapVirtualKey(vk, 0);
            SkKey(scan, false, 0); Thread.Sleep(45);
            SkKey(scan, true, 0); Thread.Sleep(65);
        }
    }

    static void SkCtrlKey(int scan)
    {
        SkKey(0x1D, false, 0); Thread.Sleep(70);
        SkKey(scan, false, 0); Thread.Sleep(90);
        SkKey(scan, true, 0); Thread.Sleep(70);
        SkKey(0x1D, true, 0);
    }

    static void SkAltF4()
    {
        SkKey(0x38, false, 0); Thread.Sleep(70);
        SkKey(0x3E, false, 1); Thread.Sleep(90);
        SkKey(0x3E, true, 1); Thread.Sleep(70);
        SkKey(0x38, true, 0);
    }

    static void SkAltN()
    {
        byte nscan = (byte)MapVirtualKey(0x4E, 0);
        SkKey(0x38, false, 0); Thread.Sleep(70);
        SkKey(nscan, false, 0); Thread.Sleep(90);
        SkKey(nscan, true, 0); Thread.Sleep(70);
        SkKey(0x38, true, 0);
    }

    // ---- PostMessage ----
    static void PostKey(IntPtr h, int vk, int scan, bool up)
    {
        long lp = 1 | (scan << 16);
        if (up) lp |= (1L << 30) | (1L << 31);
        int msg = up ? WM_KEYUP : WM_KEYDOWN;
        PostMessage(h, msg, (IntPtr)vk, (IntPtr)lp);
    }

    // ---- PrtSc 金丝雀 ----
    static void CanaryKeybd()
    {
        try { Clipboard.Clear(); } catch { }
        Thread.Sleep(400);
        keybd_event(0x2C, 0x37, 0, UIntPtr.Zero);
        Thread.Sleep(70);
        keybd_event(0x2C, 0x37, KEYUP, UIntPtr.Zero);
        Thread.Sleep(2500); Application.DoEvents();
        CheckImage("canary-keybd");
    }

    static void CanarySendKeys()
    {
        try { Clipboard.Clear(); } catch { }
        Thread.Sleep(400);
        SkKey(0x37, false, 0);
        Thread.Sleep(80);
        SkKey(0x37, true, 0);
        Thread.Sleep(2500); Application.DoEvents();
        CheckImage("canary-sk");
    }

    static void CheckImage(string tag)
    {
        bool has = false;
        try { has = Clipboard.ContainsImage(); } catch { }
        Console.WriteLine(tag + ": " + (has ? "IMAGE-YES" : "IMAGE-NO"));
        if (has)
        {
            try
            {
                Image img = Clipboard.GetImage();
                if (img != null)
                {
                    img.Save("C:\\smlab\\lab6-" + tag + ".png", ImageFormat.Png);
                    Console.WriteLine(tag + ": saved " + img.Width + "x" + img.Height);
                }
            }
            catch (Exception ex) { Console.WriteLine(tag + " save-fail: " + ex.Message); }
        }
    }

    static void WaitShot(string name)
    {
        for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(200); }
        Shot(name);
    }

    static void Shot(string name)
    {
        try
        {
            Rectangle r = _ax.RectangleToScreen(_ax.ClientRectangle);
            using (Bitmap bmp = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size);
                bmp.Save("C:\\smlab\\lab6-" + name + ".png", ImageFormat.Png);
            }
            Console.WriteLine("shot: " + name);
        }
        catch (Exception ex) { Console.WriteLine("shot-fail " + name + ": " + ex.Message); }
    }
}
