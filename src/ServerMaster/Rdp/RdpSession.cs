using System;
using System.Drawing;
using System.Windows.Forms;
using AxMSTSCLib;
using MSTSCLib;
using ServerMaster.Models;
using ServerMaster.Services;

namespace ServerMaster.Rdp
{
    /// <summary>
    /// 单个 RDP 会话：封装微软官方 ActiveX 控件 (mstscax)。
    /// 负责：连接参数、连接/断开、状态机、意外掉线自动重连、扫描码注入(键盘广播)。
    /// </summary>
    public class RdpSession : IDisposable
    {
        private readonly AxMsRdpClient9NotSafeForScripting _rdp;
        private IMsRdpClientNonScriptable _ns;
        private readonly Timer _reconnectTimer;
        private int _retryCount;
        private bool _userDisconnect;
        private int _lastW = 1024;
        private int _lastH = 640;
        private bool _disposed;

        public ServerInfo Server { get; private set; }
        public SessionState State { get; private set; }
        public string StateNote { get; private set; }

        /// <summary>RDP 控件宿主（放入 UI 分格的 Control）。</summary>
        public Control Host { get { return _rdp; } }

        /// <summary>主窗口句柄（鼠标消息广播目标/主控判定根窗口）。</summary>
        public IntPtr Handle
        {
            get
            {
                if (_rdp == null || !_rdp.IsHandleCreated) return IntPtr.Zero;
                return _rdp.Handle;
            }
        }

        public event Action<RdpSession> StateChanged;
        public event Action<RdpSession, int> DisconnectedWithReason;

        public RdpSession(ServerInfo server)
        {
            if (server == null) throw new ArgumentNullException("server");
            Server = server;
            State = SessionState.Disconnected;
            StateNote = "";

            _rdp = new AxMsRdpClient9NotSafeForScripting();
            _rdp.Enabled = true;

            _rdp.OnConnected += new EventHandler(rdp_OnConnected);
            _rdp.OnLoginComplete += new EventHandler(rdp_OnLoginComplete);
            _rdp.OnLogonError += new AxMSTSCLib.IMsTscAxEvents_OnLogonErrorEventHandler(rdp_OnLogonError);
            _rdp.OnFatalError += new AxMSTSCLib.IMsTscAxEvents_OnFatalErrorEventHandler(rdp_OnFatalError);
            _rdp.OnDisconnected += new AxMSTSCLib.IMsTscAxEvents_OnDisconnectedEventHandler(rdp_OnDisconnected);

            _reconnectTimer = new Timer();
            _reconnectTimer.Interval = 5000;
            _reconnectTimer.Tick += new EventHandler(reconnectTimer_Tick);
        }

        public void Connect(int desktopWidth, int desktopHeight)
        {
            _userDisconnect = false;
            _retryCount = 0;
            _lastW = Math.Max(640, desktopWidth);
            _lastH = Math.Max(480, desktopHeight);
            ApplySettingsAndConnect();
        }

        public void Disconnect()
        {
            _userDisconnect = true;
            _reconnectTimer.Stop();
            try
            {
                if (State == SessionState.Connected || State == SessionState.Connecting || State == SessionState.Reconnecting)
                    _rdp.Disconnect();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("断开连接异常(" + Server.DisplayName + "): " + ex.Message);
            }
            SetState(SessionState.Disconnected, "");
        }

        private void ApplySettingsAndConnect()
        {
            SetState(SessionState.Connecting, "");
            try
            {
                // 兼容 "host:port" 写法：mstscax 的 Server 属性拒绝带端口，
                // 必须拆分后把端口设到 RDPPort（否则抛 E_INVALIDARG）
                string effectiveHost;
                int portFromHost;
                HostPort.Split(Server.Host, out effectiveHost, out portFromHost);
                int effectivePort = portFromHost > 0 ? portFromHost : Server.Port;

                _rdp.Server = effectiveHost;
                _rdp.UserName = Server.Username;
                if (!string.IsNullOrEmpty(Server.Domain)) _rdp.Domain = Server.Domain;
                _rdp.DesktopWidth = _lastW;
                _rdp.DesktopHeight = _lastH;
                _rdp.ColorDepth = NormalizeColorDepth(Server.ColorDepth);

                IMsRdpClientAdvancedSettings a2 = _rdp.AdvancedSettings2;
                a2.RDPPort = effectivePort;
                string pwd = PasswordProtector.Unprotect(Server.EncryptedPassword);
                if (!string.IsNullOrEmpty(pwd)) a2.ClearTextPassword = pwd;
                a2.SmartSizing = true;          // 核心：远端画面自动缩放适配分格大小
                a2.BitmapPersistence = 0;
                a2.GrabFocusOnConnect = false;   // 批量连接时不抢焦点
                a2.EnableWindowsKey = 1;
                a2.RedirectPrinters = false;
                a2.RedirectPorts = false;
                a2.RedirectSmartCards = false;
                a2.RedirectDrives = Server.RedirectDrives;

                IMsRdpClientAdvancedSettings4 a5 = _rdp.AdvancedSettings5;
                a5.DisplayConnectionBar = false;

                // 以下为可选增强参数：个别系统版本不支持时降级继续，不让连接整体失败
                IMsRdpClientAdvancedSettings6 a7 = _rdp.AdvancedSettings7;
                TrySet("ConnectToAdministerServer", delegate { a7.ConnectToAdministerServer = Server.ConnectToConsole; });
                TrySet("EnableCredSspSupport", delegate { a7.EnableCredSspSupport = true; });
                TrySet("RelativeMouseMode", delegate { a7.RelativeMouseMode = false; });
                TrySet("RedirectClipboard", delegate { a7.RedirectClipboard = true; });
                TrySet("WarnAboutClipboardRedirection", delegate
                {
                    IMsRdpClientNonScriptable5 ns5 = (IMsRdpClientNonScriptable5)_rdp.GetOcx();
                    ns5.WarnAboutClipboardRedirection = false;
                });

                AppLogger.Info(string.Format("连接 {0} ({1}:{2})", Server.DisplayName, effectiveHost, effectivePort));
                _rdp.Connect();
            }
            catch (Exception ex)
            {
                AppLogger.Error("发起连接失败(" + Server.DisplayName + ")", ex);
                SetState(SessionState.Failed, ex.Message);
            }
        }

        private void TrySet(string name, Action setter)
        {
            try
            {
                setter();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("参数 " + name + " 设置失败(已跳过): " + ex.Message);
            }
        }

        private static int NormalizeColorDepth(int cd)
        {
            if (cd == 8 || cd == 15 || cd == 16 || cd == 24 || cd == 32) return cd;
            return 16;
        }

        public Size GetClientSize()
        {
            if (_rdp == null) return Size.Empty;
            return _rdp.ClientSize;
        }

        /// <summary>
        /// 向远端会话注入一次按键：PostMessage 投递完整 WM_KEYDOWN/UP（与真实键入同一处理路径）。
        /// lParam 格式：bit0-15 重复数=1，bit16-23 扫描码，bit24 扩展键，bit30/31 已按下/释放。
        /// </summary>
        public void PostVirtualKey(int vk, int scanCode, bool extended, bool keyUp)
        {
            IntPtr hwnd = Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                // Alt/F10 走 WM_SYSKEY*，其余走 WM_KEY*
                bool sys = vk == 0x12 || (vk >= 0xA4 && vk <= 0xA5) || vk == 0x79;
                int msg = keyUp
                    ? (sys ? Win32.NativeMethods.WM_SYSKEYUP : Win32.NativeMethods.WM_KEYUP)
                    : (sys ? Win32.NativeMethods.WM_SYSKEYDOWN : Win32.NativeMethods.WM_KEYDOWN);
                long lp = 1 | ((scanCode & 0xFF) << 16);
                if (extended) lp |= 1L << 24;
                if (keyUp) lp |= (1L << 30) | (1L << 31);
                Win32.NativeMethods.PostMessage(hwnd, msg, (IntPtr)vk, (IntPtr)lp);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("PostVirtualKey 失败(" + Server.DisplayName + "): " + ex.Message);
            }
        }

        /// <summary>向远端会话注入一次按键（扫描码，SendKeys 接口）。</summary>
        public void SendScanCode(int scanCode, bool extended, bool keyUp)
        {
            try
            {
                if (_ns == null) _ns = (IMsRdpClientNonScriptable)_rdp.GetOcx();
                bool up = keyUp;
                int scan = scanCode & 0xFF;

                // 文档格式：低字节=扫描码，扩展键高字节=0xE0 前缀 (0xE0nn)
                int data = extended ? (scan | 0xE000) : scan;
                _ns.SendKeys(1, ref up, ref data);

                // 兼容格式：若控件按 WM_KEYDOWN lParam 解释(扫描码在16-23位)，补发一次。
                // 两种编码的低字节/16-23位必有一个为 0，被忽略的那个不会产生按键，无重复风险。
                int data2 = scan << 16 | (extended ? (1 << 24) : 0);
                _ns.SendKeys(1, ref up, ref data2);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("SendKeys 注入失败(" + Server.DisplayName + "): " + ex.Message);
            }
        }

        #region 事件处理（状态机）

        private void rdp_OnConnected(object sender, EventArgs e)
        {
            SetState(SessionState.Connected, "");
            // 组合键转发：默认 KeyboardHookMode=0(仅全屏模式转发),窗口模式下
            // Win/Ctrl+Esc/Alt+Tab 会被本地吃掉不进远端 —— 广播场景必须设为1(全部转发远端)
            TrySet("KeyboardHookMode", delegate
            {
                IMsRdpClientSecuredSettings2 ss = (IMsRdpClientSecuredSettings2)_rdp.SecuredSettings;
                ss.KeyboardHookMode = 1;
            });
        }

        private void rdp_OnLoginComplete(object sender, EventArgs e)
        {
            AppLogger.Info("登录成功: " + Server.DisplayName);
        }

        private void rdp_OnLogonError(object sender, IMsTscAxEvents_OnLogonErrorEvent e)
        {
            // 0/-5 等非致命代码会被控件自动处理，仅记录
            if (e.lError == 0) return;
            AppLogger.Warn(string.Format("登录错误({0}): 代码 {1}", Server.DisplayName, e.lError));
            SetState(SessionState.Failed, "登录失败 代码 " + e.lError);
        }

        private void rdp_OnFatalError(object sender, IMsTscAxEvents_OnFatalErrorEvent e)
        {
            AppLogger.Error(string.Format("致命错误({0}): 代码 {1}", Server.DisplayName, e.errorCode));
            SetState(SessionState.Failed, "错误 代码 " + e.errorCode);
        }

        private void rdp_OnDisconnected(object sender, IMsTscAxEvents_OnDisconnectedEvent e)
        {
            _reconnectTimer.Stop();
            int reason = e.discReason;

            if (DisconnectedWithReason != null) DisconnectedWithReason(this, reason);

            if (!_userDisconnect && Server.AutoReconnect && reason != 260 /* 260=客户端主动断开 */)
            {
                AppLogger.Warn(string.Format("意外掉线({0}) 代码 {1}，进入自动重连", Server.DisplayName, reason));
                SetState(SessionState.Reconnecting, "掉线 代码 " + reason);
                _retryCount = 0;
                _reconnectTimer.Start();
            }
            else
            {
                SetState(SessionState.Disconnected, reason == 260 ? "" : "断开 代码 " + reason);
            }
        }

        private void reconnectTimer_Tick(object sender, EventArgs e)
        {
            _reconnectTimer.Stop();
            if (_userDisconnect) return;
            if (_retryCount >= 5)
            {
                SetState(SessionState.Failed, "自动重连失败(已尝试5次)");
                return;
            }
            _retryCount++;
            SetState(SessionState.Reconnecting, "第 " + _retryCount + "/5 次重连…");
            ApplySettingsAndConnect();
        }

        private void SetState(SessionState st, string note)
        {
            State = st;
            StateNote = note ?? "";
            AppLogger.Info(string.Format("{0} → {1} {2}", Server.DisplayName, st, StateNote));
            if (StateChanged != null) StateChanged(this);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _userDisconnect = true;
            try { _reconnectTimer.Stop(); _reconnectTimer.Dispose(); } catch { }
            try
            {
                if (State == SessionState.Connected) _rdp.Disconnect();
            }
            catch { }
            try { _rdp.Dispose(); } catch { }
        }
    }
}
