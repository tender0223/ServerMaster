# AI-CONTEXT — ServerMaster 项目接手指南（给 AI / 新开发者）

> 本文件专为"另一个 AI / 开发者拿到压缩包后能直接读懂、构建、运行、继续开发"而写。
> 读完本文件 + README.md 即可开工，无需其他背景。

## 1. 项目是什么

**ServerMaster**：Windows 多服务器批量管理工具（WinForms，单进程，零安装）。

核心能力：
- 在一个窗口里同时显示多个 Windows 服务器的 RDP 远程桌面（分格平铺）；
- 「广播/同步器」：在主控分格打字/点鼠标，实时镜像到其他勾选的分格（多服务器同步操作）；
- 文件传输：SMB 管理共享直传 + 「批量推送(经RDP)」（剪贴板+Ctrl+V 注入，NAT VPS 可用）；
- FinalShell 配置批量导入（工具栏「＋ 添加服务器」下拉 →「从 FinalShell 导入…」；含 **FinalShell 4.x 密码算法解密**，30/30 实测）；
- 文本批量添加：工具栏「＋ 添加服务器」下拉 →「文本批量添加…」，每行 `ip:端口|账号|密码|备注|分组`（端口缺省 3389、账号缺省 Administrator、分组缺省 默认分组；支持全角 `｜`/`：`、`#` 注释、批内与配置内去重、非法行按行号提示；密码立即 DPAPI 加密）；
- 按组管理、拖拽换组、组重命名、勾选删除、DPAPI 本地密码加密存储；
- 配置迁移：文件 → 导出配置 ▸ 两种模式——**普通导出**保留 DPAPI 密文（换机导入需逐台重输密码）；**便携导出**（`.smc`）用访问密码经 PBKDF2-SHA256(10 万次) + AES-256-CBC 加密整份清单并附 HMAC-SHA256，另一台电脑输入同一密码导入后密码自动转为本机 DPAPI，直接可用；
- 一键连接：左侧「连接/断开/删除」勾选优先（多台批量）、未勾选时作用于选中项（单台编辑走右键菜单）；工具栏「连接全部」连清单内全部（16 路上限），「全部删除」在文件菜单；
- 界面记忆：分格标题可切换「备注名/不显示/主机名/主机 IP+端口」，左侧栏可显隐（工具栏「服务器栏」或 视图菜单），组内按名称自然排序（北少云2 在 北少云10 前），窗口位置/大小与侧栏宽度自动记忆；

当前版本：**v0.4.1**（更新记录见 `CHANGELOG.md`；本包不含 .git）。

## 2. 立即运行（无需构建）

```
build\ServerMaster.exe        ← 已编译好，双击即用
```
- 依赖：.NET Framework 4.8（Win10/Server 自带）；`AxMSTSCLib`/`MSTSCLib` 已嵌入 exe 并从内存加载，单文件即可运行（`build\` 下的两个 DLL 仅 VS 构建/排错备用）。
- 首次运行配置存于 `%APPDATA%\ServerMaster\config.json`，日志在 `%LOCALAPPDATA%\ServerMaster\logs`。

## 2.1 分发与数据位置

- 对外分发只需 `build\ServerMaster.exe` 一个文件（约 1.5 MB）：两个互操作 DLL 已作为资源嵌入，运行时从内存加载，不再需要同目录 DLL；压缩包见 `发布\ServerMaster-单文件版.zip`。
- 服务器清单、分组、DPAPI 密码密文、窗口布局只存 `%APPDATA%\ServerMaster\config.json`，日志存 `%LOCALAPPDATA%\ServerMaster\logs`，**都不在 exe / 发布包内**，发给别人不会带出任何账号信息。
- 密码用 DPAPI（当前 Windows 账户）加密，换机器或换账户无法解密；即便误发 config.json，对方也读不到明文密码。需要换机直用时走「文件 → 导出配置 → 便携导出」：整份清单（含密码）用访问密码加密为 `.smc`，对方输入同一密码导入后立即转为其本机 DPAPI 密文。

## 3. 立即构建（无需 Visual Studio / dotnet SDK）

```
build.cmd
```
- 用 Windows 自带的 `csc.exe`（.NET Framework 4.x）离线编译，产物在 `build\`。
- 应用图标：`src\ServerMaster\app.ico`（16/20/24/32/40/48/64/96/128/256 多尺寸），编译时 `/win32icon` 嵌入 exe（资源管理器显示），`/resource` 以 `ServerMaster.app.ico` 供窗口/任务栏图标使用。
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
    PortableConfig.cs        便携配置：PBKDF2-SHA256 + AES-256-CBC + HMAC-SHA256 加密导出/导入
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
5. **密码**：DPAPI CurrentUser + entropy 加密存本地；FinalShell 导入、便携配置导入时只在内存解密后立即重加密，不落明文。便携文件本身是整体 AES 加密包（`.smc`），文件内没有任何字段明文。
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
- [x] 广播时目标分格无"正在接收"视觉提示（已加：接收分格标题栏深青底 + 左侧亮色竖条 + 状态栏"接收广播"，主控为蓝色底 + "主控"）；
- [ ] README 面向最终用户，本文件面向开发者，注意别写串。

## 8. 许可

MIT（见 LICENSE），允许任意修改与二次创作（保留版权声明即可）。
