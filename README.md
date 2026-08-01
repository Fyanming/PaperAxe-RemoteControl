# 纸伐局域网远控

局域网远程控制与被控一体的桌面软件，基于 WPF + .NET 10。

![WPF](https://img.shields.io/badge/UI-WPF-blue)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![P2P](https://img.shields.io/badge/Network-P2P%20TCP-brightgreen)
![Encryption](https://img.shields.io/badge/Encryption-ECDH%2BAES--GCM-orange)

同一台机器上既可以做被控端，也可以做远控端。远控连接走局域网 P2P 直连 TCP，无需中转服务器；画面、键鼠、音频和文件传输全部走同一条加密通道。

## 核心亮点

- 同一程序同时具备远控端与被控端能力
- 被控端监听指定端口，远控端输入局域网 IP 自动连接
- ECDH 密钥协商 + AES-GCM 加密，未加密客户端无法建立会话
- 实时画面传输，画质与帧率可调，最高 60 FPS
- 远控窗口实时显示延迟，鼠标输入按 60Hz 合并发送
- 远控设置面板：隐私保护、声音传输开关
- 双向文件传输，带进度显示
- 提供自包含 .NET 10 安装程序，也提供带运行时检查的完整版安装包

## 功能特性

### 远控与被控

- 被控端启动后监听 `0.0.0.0:48666`（端口可改），输入本机 IP 即可被远控
- 远控端输入被控端局域网 IP 与端口即可连接
- 控制模式：转发鼠标键盘；观看模式：只看不操控
- 远控时新建全屏窗口，四周悬浮深色控制条

### 画面与延迟

- 画面支持低 / 中 / 高 / 极致四档画质
- 帧率调节上限 60 FPS
- 画面在后台解码并只渲染最新帧，避免 UI 卡顿
- Ping/Pong 实时测量延迟，50ms 内绿色、150ms 内黄色、更高红色

### 隐私保护

- 开启后被控端显示器持续熄屏，控制端画面不受影响
- 检测到已安装的 IDD 类虚拟显示器驱动时，可配合虚拟屏采集画面
- 断开远控或关闭程序后自动恢复显示器

### 声音传输

- 远控时自动切换虚拟音频输出设备（例如 VB-CABLE），回传被控端系统声音
- 未安装虚拟音频设备时自动降级为直接回传系统默认输出声音，不再静音系统
- 声音传输可在远控设置面板中随时关闭

### 文件传输

- 双向传输，发送端与接收端均显示进度
- 接收时弹出保存位置，支持 256KB 分块传输
- 传输文件同样走加密通道

## 快速开始

### 源码运行

```bash
dotnet run --project src/ZhifaRemote
```

或先构建：

```bash
dotnet build ZhifaLanRemote.slnx
```

### 安装包

运行 `scripts/build-installer.ps1` 会先发布自包含 .NET 10 的单文件程序，再用 Inno Setup 生成安装程序到 `dist/`。

安装程序支持：

- 快速安装到 `C:\Program Files\纸伐局域网远控`
- 自定义安装目录
- 安装进度条
- 无需联网下载 .NET 10
- 安装完成后自动创建桌面快捷方式

`installer/installer-debug.iss` 负责生成“完整版”安装包，安装前会检查 .NET 10 Desktop Runtime，未安装时提示并提供微软官方下载链接。

## 安全模型

- 连接建立时通过 ECDH 协商会话密钥，HKDF 派生 AES-256-GCM 密钥
- 每条消息独立随机 nonce，消息体整体加密
- 握手失败的连接不会进入会话，未加密客户端会被拒绝
- 画面、输入、音频、文件传输全程走同一条加密通道

## 音频虚拟设备说明

应用无法凭空安装声卡驱动。软件会自动识别系统里的虚拟输出设备（例如 VB-CABLE 的 `CABLE Input`），未安装时主界面提供下载引导。

## 目录结构

```text
src/ZhifaRemote/Services/   加密 P2P/TCP 通道、服务端/客户端、屏幕捕获、音频、输入注入、文件传输
src/ZhifaRemote/Themes/     毛玻璃主题与动画资源
src/ZhifaRemote/            MainWindow（工作台）、RemoteWindow（全屏远控窗口）
installer/                  Inno Setup 安装脚本
scripts/                    构建与静态扫描脚本
tests/                      协议、加密、传输与画面流冒烟测试
```

## 最近更新

近期重大更新与 Bug 修复请查看 [CHANGELOG.md](CHANGELOG.md)。
