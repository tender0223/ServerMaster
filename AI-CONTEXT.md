# AI-CONTEXT — ServerMaster 项目接手指南（给 AI / 新开发者）

> 本文件专为"另一个 AI / 开发者拿到压缩包后能直接读懂、构建、运行、继续开发"而写。
> 读完本文件 + README.md 即可开工，无需其他背景。

## 1. 项目是什么

**ServerMaster**：Windows 多服务器批量管理工具（WinForms，单进程，零安装）。

核心能力：
- 在一个窗口里同时显示多个 Windows 服务器的 RDP 远程桌面（分格平铺）；
- 「广播/同步器」：在主控分格打字/点鼠标，实时镜像到其他勾选的分格（多服务器同步操作）；
- 文件传输：SMB 管理共享直传 + 「批量推送(经RDP)」（剪贴板+Ctrl+V 注入，NAT VPS 可用）；
- FinalShell 配置批量导入（含 **FinalShell 4.x 密码算法解密**，30/30 实测）；
- 按组管理、拖拽换组、组重命名、勾选删除、DPAPI 本地密码加密存储。

当前版本：**v0.3.1**（原仓库 git tag；本包不含 .git）。

## 2. 立即运行（无需构建）

```
build\ServerMaster.exe        ← 已编译好，双击即用
```
- 依赖：.NET Framework 4.8（Win10/Server 自带）、包内 `build\AxMSTSCLib.dll`、`build\MSTSCLib.dll`（需与 exe 同目录）。
- 首次运行配置存于 `%APPDATA%\ServerMaster\config.json`，日志在 `%LOCALAPPDATA%\ServerMaster\logs`。

## 3. 立即构建（无需 Visual Studio / dotnet SDK）

```
build.cmd
```
- 用 Windows 自带的 `csc.exe`（.NET Framework 4.x）离线编译，产物在 `build\`。
- **热替换机制**：若 exe 被运行中的实例锁定，新构建存为 `build\ServerMaster.new.exe`，关掉应用后重跑 build.cmd 自动换名。
- 构建后验证方法：字节级搜索 exe（UTF-16LE）中新增的字符串字面量，确认新代码真的编进去了。

## 4. 目录地图

```
src\ServerMaster\
  Program.cs                 入口
  Models\                    ServerInfo / AppConfig / SessionState
  Rdp\RdpSession.cs          ★核心：mstscax 封装（连接/状态机/自动重连/按键注入）
  Rdp\KeyMapper.cs           VK→扫描码兜底表
  Services\
    ConfigStore.cs           配置读写 (JSON, %APPDATA%)
    ConnectionManager.cs     会话池
    HostPort.cs              host:port 拆分（mstscax 的 Server 属性拒绝带端口！）
    InputBroadcastService.cs ★核心：键盘(WH_KEYBOARD_LL)+鼠标(IMessageFilter)广播
    AppLogger.cs             日志
    FinalShell\              FinalShell 导入 + 4.x 密码解密(DES/MD5/java-Random 复刻)
    FileTransfer\            SMB 通道 + RDP 剪贴板通道
  UI\                        MainForm / SessionTile / SessionGridPanel / 各对话框
  Win32\NativeMethods.cs     P/Invoke 集中地
lib\                         AxImp 生成的互操作程序集（编译必需）
docs\设计文档.md              完整设计文档
blog\00-09*.md               研发过程博客系列（09=键盘注入攻坚战，最重要的一篇）
labs\lab6.cs                 注入验证实验台源码（调试工具，见其文件头注释）
build\                       编译产物
release\                     历史发布包
```

## 5. 关键技术约束（踩过的坑，别再踩）

1. **语言版本是 C#5**（老 csc）：不能用 `$"..."`、`?.`、`=>` 属性、内联 `out var`、字符串插值。
2. **build.cmd 必须纯 ASCII**：cmd.exe 按 OEM 代码页解析批处理，中文注释会损坏脚本。
3. **mstscax 的 `Server` 属性拒绝 `host:port`**（抛 E_INVALIDARG），必须拆分，端口设 `AdvancedSettings2.RDPPort`。
4. **远程分辨率固定 + SmartSizing**：`DesktopWidth/Height` 用固定值（默认1920×1080），显示端靠 `SmartSizing=true` 缩放，不能按分格大小改远端分辨率（会挤压远端窗口）。
5. **密码**：DPAPI CurrentUser + entropy 加密存本地；FinalShell 导入时只在内存解密后立即重加密，不落明文。
6. **键盘注入（v0.3.1 攻克，务必读 blog\09）**：
   - `KeyboardHookMode` 默认 0 = 仅全屏转发组合键，**窗口化控件里 Win/Ctrl+Esc/Alt+Tab 会被本地吃掉**，必须在 `OnConnected` 后设 `SecuredSettings.KeyboardHookMode=1`；
   - 有效配方 = 本程序前台 + `SetFocus(目标控件)` + `keybd_event`(带扫描码) + down/up 间隔 40–90ms；
   - `keybd_event` 注入的是系统全局输入流（发给"前台线程的焦点窗口"），**本程序不在前台时严禁注入**（安全闸会拦）；
   - `PostMessage(WM_KEYDOWN)` 对 mstscax 无效（实验证实），别再试。
7. **SMB 直传**走 `\\host\IPC$`/`c$`（WNetAddConnection2），NAT VPS 常见错误 64（445 未转发）→ 降级用 RDP 剪贴板通道。
8. COM 事件参数类名以 AxImp 生成的为准（如 `IMsTscAxEvents_OnDisconnectedEvent`），AxMSTSCLib 与 MSTSCLib 命名空间同名委托需全限定。

## 6. 如何验证注入链路（改了注入相关代码后必做）

1. 运行程序 → 连接一台服务器 → 勾选分格标题栏「广播」→ 工具栏 **🧪注入测试**；
2. 它会向远端发送 `Ctrl+Esc → notepad → 回车`，**远端弹出记事本 = 链路正常**；
3. 广播打字验证：主控分格里打字，其他分格同步出现；
4. 看日志 `%LOCALAPPDATA%\ServerMaster\logs`（钩子捕获/镜像/安全闸都有遥测）。
5. 深度排查可用 `labs\lab6.cs`（需 lib 互操作 dll + 一台真实服务器；连接参数读自本机 %APPDATA% 配置，按需改）。

## 7. 已知问题 / 下一步（接手者清单）

- [ ] 鼠标镜像（IMessageFilter+PostMessage 路径）未做同等强度的端到端验证；
- [ ] 部分服务器连接失败：登录错误 -1、掉线 516（证书）、1287（会话数冲突）——多为服务端配置，可加"重试/接管会话"策略；
- [ ] 注入节奏可按目标数量自适应（当前固定 40ms/键）；
- [ ] 广播时目标分格无"正在接收"视觉提示；
- [ ] README 面向最终用户，本文件面向开发者，注意别写串。

## 8. 许可

MIT（见 LICENSE），允许任意修改与二次创作（保留版权声明即可）。
