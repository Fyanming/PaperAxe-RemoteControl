# 纸伐远控软件 · PaperAxe RemoteControl

> 纸笺 · 局域网远控软件 — WPF 实现，远控/被控一体

![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)
![WPF](https://img.shields.io/badge/WPF-Desktop-orange)

## ✨ 功能特性

- 🔄 **远控/被控一体** — 同一应用既是远控端也是被控端，Tab 一键切换
- 🖥️ **远程画面传输** — GDI+ BitBlt 屏幕捕获，10 FPS 推送，JPEG q60 编码
- 🎮 **三种模式**：远程控制 / 观看模式 / 文件传输
- 📁 **文件传输** — 拖拽 / 点选，TCP 分片传输（64KB / 片）
- 🔔 **被控通知** — 系统右下角气泡："你已被 {远控端IP} 远控"
- 🔇 **音量归零** — 远控建立时被控端系统音量自动归零，断开时恢复
- 🔊 **音频转发** — 通过 IPolicyConfig COM 切换默认输出至虚拟音频设备
- 🎨 **纸感禅意 UI** — 极简暖白配色 + 暖橙强调色 + Fraunces 衬线字体

## 🏗️ 架构

```
LanRemoteControl/
├── App.xaml(.cs)              # 应用入口 + 全局资源
├── MainWindow.xaml(.cs)       # 主窗口（Tab 切换容器）
├── Styles/Theme.xaml          # 纸感禅意主题资源
├── Views/
│   ├── ControllerView.xaml    # 远控端面板
│   └── ControlledView.xaml    # 被控端面板
├── Core/
│   ├── NetworkServer.cs       # 被控端 TCP 监听 + 画面推送
│   ├── NetworkClient.cs       # 远控端 TCP 连接 + 帧接收
│   ├── ScreenCapture.cs       # GDI+ 屏幕捕获 → JPEG
│   ├── AudioDeviceManager.cs  # 音量归零 + 设备切换（NAudio）
│   ├── PolicyConfigCOM.cs     # IPolicyConfig COM 互操作
│   └── ToastNotifier.cs       # 系统右下角通知
└── Models/Models.cs           # 数据模型
```

## 🔧 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | .NET 8 WPF |
| 音频 | NAudio 2.2.1 + Core Audio API |
| 设备切换 | IPolicyConfig COM (mmdeviceapi) |
| 通知 | System.Windows.Forms.NotifyIcon |
| 屏幕捕获 | System.Drawing.Graphics.CopyFromScreen |
| 通信 | TcpClient / TcpListener (局域网) |

## 🚀 快速开始

### 环境要求
- .NET 8 SDK
- Windows 10 / 11
- 管理员权限（切换系统默认音频设备需要）

### 构建运行
```powershell
cd LanRemoteControl
dotnet build
dotnet run
```

## 📖 使用流程

1. **被控端**：打开应用 → 切换到"被控端" Tab → 点击"开启被控" → 监听端口 `7321`
2. **远控端**：打开应用 → 输入被控端局域网 IP → 点击"建立连接"
3. 选择模式：远程控制 / 观看模式 / 文件传输
4. 被控端断开 → 远控端自动重置 UI；远控端断开 → 被控端自动恢复音量与默认音频设备

## 📡 通信协议

### 文本指令（UTF-8 + `\n` 结尾）
```
MODE|Control    # 切换至远程控制模式
MODE|View       # 切换至观看模式
MODE|File       # 切换至文件传输模式
PING            # 心跳探测
PONG            # 心跳回应
```

### 帧推送（文本头 + 二进制）
```
FRAME|<size>\n  + <size> 字节 JPEG 数据
```

### 文件传输
```
FILE|<name>|<size>\n  + 文件二进制流  +  \nENDFILE\n
```

## ⚠️ 注意事项

1. **虚拟音频设备**：`LFBolt Virtual Cable` 需配合虚拟音频驱动（如 VB-Cable 或自研驱动）。代码仅检测设备存在性，不安装驱动。
2. **管理员权限**：`app.manifest` 已声明 `requireAdministrator`，切换系统默认音频设备必须提权。
3. **屏幕捕获性能**：当前为 10 FPS / 1280×720 / JPEG q60，局域网下流畅。
4. **画面传输安全**：本工具仅用于局域网内远程协助，未做端到端加密，请勿在公网使用。

## 📜 许可证

MIT License © 2026 Fyanming
