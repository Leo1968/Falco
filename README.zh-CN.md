# Falco

[English](./README.md) | 简体中文

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](./LICENSE)
[![Platform](https://img.shields.io/badge/Windows%2010%2F11-blue)](../../releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)](https://dotnet.microsoft.com/download/dotnet/8.0)

**Falco** 是一款面向 Windows 10/11 的开源系统保养工具箱，包含两个版本，共享同一个设计哲学：**每一次修改都有备份，每一次操作都可以撤销。**

| | [Falco GUI](#falco-gui-图形界面版) | [Falco CLI](#falco-cli-命令行版) |
|---|---|---|
| 形态 | 桌面应用（C#/WPF，.NET 8） | 单文件 PowerShell 脚本 |
| 适合 | 日常使用——一眼掌握，一键搞定 | 脚本化、快速检查、自动化 |
| 许可 | GPL-3.0 | GPL-3.0 |
| 获取 | [Releases 下载安装包](../../releases) 或 [从源码构建](#从源码构建) | `git clone` 即用 |

## Falco GUI（图形界面版）

- **状态仪表盘**——健康度评分（0–100）、CPU / GPU / 内存 / 磁盘 / 网络实时图表、CPU 与 GPU 温度、高占用进程
- **立即加速**——修剪进程工作集释放内存，不影响运行中的程序
- **全面体检**——十项检查加权评分，附修复建议
- **深度清理**——临时文件、各类缓存、安装包残留，删除一律进回收站
- **开机管理**——启动项启用 / 禁用 / 删除，先备份再操作
- **一键优化**——六项系统优化（传递优化、后台应用、视觉效果、活动时间、SysMain、Windows Search），写入前回读验证、失败自动回滚
- **系统分析**——服务与大文件扫描
- **World Art Gallery 名画展**——轮换展示全球最著名的 50 幅公有领域名画，来自博物馆开放获取影像（华盛顿国家美术馆、Wikimedia Commons）
- **隼徽菜单**——设置（语言、主题、开机自启）、关于、检查更新
- **深浅双主题 · 中英双语界面**

## Falco CLI（命令行版）

- **status**：健康度评分（0–100）+ CPU / 内存 / 磁盘 / 温度 / 开机时长快照，`-Json` 机器可读
- **tweaks**：同一套六项系统优化的查看、备份、应用与还原，写入前回读验证、失败自动回滚
- **clean**：临时文件 / Windows Update 缓存 / 缩略图缓存 / 回收站的扫描与清理
- **purge**：构建产物（node_modules / target / build 等）扫描，7 天活跃、含密钥、嵌套仓库自动跳过
- **large**：大文件扫描

```powershell
# 系统状态与健康度
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 status -Json

# 应用低风险四项优化
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks apply safe

# 清理临时文件与缩略图缓存
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean run -Items temp,thumbs -Yes

# 扫描构建产物与大文件
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 purge D:\code -Days 7
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 large C:\ -MinMB 500 -Top 20
```

完整命令参考：[User-Guide.md](./User-Guide.md) · [中文指南](./使用指南.md)

## 从源码构建

**Falco GUI**——需要 Windows 上的 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：

```powershell
# 自包含 + ReadyToRun 的便携目录，无运行时依赖
dotnet publish src/Falco.App/Falco.App.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o src/Falco.App/release
```

运行 `src\Falco.App\release\Falco.exe` 即可。如需安装包，安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php) 后执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-2.0.ps1 -Version 2.4.9
```

**Falco CLI**——无需构建，克隆即用。温度数据优先使用内置的 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（`lib/` 目录，可选），缺失时自动降级为 ACPI 热区。

## 说明

- 读取 CPU 温度需要以管理员身份运行——这是 Windows 的安全机制，并非 Falco 的限制；无权限时界面会明确提示。
- 安装包暂未数字签名：SmartScreen 弹窗请点「更多信息 → 仍要运行」。
- 所有优化 / 清理 / 修改执行前自动备份到 `C:\ProgramData\Falco\Backups`，并附还原脚本 `Restore-Falco.ps1`。

## 项目结构

```
Falco/
├── Falco-CLI.ps1                 # CLI 版——单文件 PowerShell 脚本
├── User-Guide.md / 使用指南.md    # CLI 使用指南（英文 / 中文）
├── lib/LibreHardwareMonitor/     # CLI 可选传感器库（MPL-2.0）
├── assets/                       # GUI 构建资源（应用图标、地球贴图、NGA 开放数据索引）
├── src/Falco.App/                # GUI 版——C#/WPF（.NET 8）源码
│   ├── Views/                    #   页面与对话框
│   ├── Services/                 #   指标采集、系统优化、清理、博物馆 API、更新检查
│   ├── Resources/                #   主题字典 + 中英界面文案
│   └── Falco.App.csproj
├── Falco-Setup-2.0.iss           # GUI 的 Inno Setup 安装包脚本
├── Build-2.0.ps1                 # 一键构建：升版本 → 发布 → 打包
└── LICENSE                       # GPL-3.0
```

## 许可证

Falco 基于 [GPL-3.0](./LICENSE) 开源。

内置的 [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（v0.9.6，`lib/LibreHardwareMonitor/`）遵循 **Mozilla Public License 2.0（MPL-2.0）**，作为独立的可选传感器提供方：这些文件保留其原始许可与声明（见 `lib/LibreHardwareMonitor/NOTICE.txt`），不受本项目 GPL-3.0 条款影响。

World Art Gallery 展示的画作均为博物馆开放获取计划的公有领域影像（华盛顿国家美术馆开放数据、Wikimedia Commons）。

## 免责声明

Falco 会修改系统注册表与服务配置。虽然所有修改均有备份且可恢复，仍建议：

1. 首次使用前手动创建系统还原点
2. 重要数据自行做好备份
3. 恢复默认设置后重启系统

请自行斟酌使用风险。

| 深色主题 | 浅色主题 |
|---|---|
| ![dark](src/docs/screenshots/status-dark.png) | ![light](src/docs/screenshots/status-light.png) |

