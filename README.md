# Falco

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](./LICENSE)
为 Windows 10/11 打造的深度清理、应用管理与系统监控图形工具。


## 当前功能

### 状态 — 实时监控仪表盘
健康度 / CPU / GPU / 内存 / 温度 / 磁盘 / 网络 / 电源计划八卡片仪表盘。每张卡片为「图标+标题+徽章 → 大数值 → 图表 → 脚注」四段式布局，首列（健康度 / 温度）与其余列宽 14:9；徽章显示实时信息（CPU 温度、内存压力、温度状态、磁盘总容量、网卡名）。

- **健康度**：0–100 综合分 + 一句话状态提示（如「磁盘占用偏高」），标题行展示 CPU 型号 / 内存 / 系统版本标签
- **进程列表**：名称 / PID / CPU（迷你柱条 + 百分比）/ 内存四列左右对齐铺满整行，按 CPU 占用排序
- **logo 菜单**：点击导航栏隼形 Logo 弹出「设置 / 运行诊断 / 检查更新 / 关于 Falco」——运行诊断会对系统配置做只读体检（隐私 / 性能 / 更新 / 清理 / 备份五个维度各 0–100 分）并弹窗报告结果，评分项与优化页共享同一套 Detect 判断标准

### 温度监测 — LibreHardwareMonitor 集成（可选）
内置 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 传感器库（`lib/LibreHardwareMonitor/`）：库存在时直接读取 CPU / GPU 温度传感器（显示在 CPU 徽章、温度卡片）；库缺失或无可用传感器时自动降级为 ACPI 热区温度，再不可得则显示"—"，全程不影响其它功能。

### 命令行版 — Falco-CLI
与 GUI 共享同一引擎与数据目录（`C:\ProgramData\Falco`）的命令行版，子命令风格对齐 `mo` CLI：

```powershell
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 status -Json
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks list
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks apply safe          # 低风险四项
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks revert <Id|all>
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean scan
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean run -Items temp,thumbs -Yes
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 purge D:\code -Days 7
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 large C:\ -MinMB 500 -Top 20
```

子命令：`status` / `tweaks`（list, backup, apply, revert）/ `clean`（scan, run）/ `purge` / `large` / `help`。应用与还原自动先备份；高风险项（禁用 SysMain / Windows Search）需 `-Yes` 确认；`-Json` 可机器读取。引擎函数在运行时从 `Falco.ps1` 提取，与 GUI 永远保持同一套逻辑。

### 优化 — 三档一键优化 + 状态化单项管理
这是 Windows 用户的高频需求，因此保留：

| 模式 | 目标 | 说明 |
|------|------|------|
| 🟢 安全 | 几乎无副作用，适合所有电脑 | 传递优化禁 P2P、视觉特效性能优先、活动时间 8–22 点、清理临时文件 |
| 🟡 性能 | 减少后台 CPU / 内存 / 磁盘 I/O | 加禁止商店应用后台运行 + SysMain 硬件适配建议 |
| 🔴 开发机 | 面向开发场景，最大限度减少后台干扰 | 保留 Windows Search 与 SysMain，避免影响索引与编译 I/O |

高级选项为 **SophiApp 式状态列表**（对标 Sophia Script 的成对函数）：每个优化项（传递优化 / 后台应用 / 视觉效果 / 活动时间 / SysMain / Windows Search）实时显示当前状态（已优化 / 默认 / 具体值），行内按钮单项**应用**（高风险项先确认）与**还原**——还原按修改记录精确恢复（注册表写回原值、新增则移除、服务恢复原启动类型），修改记录持久化在 `tweaks-state.json`。

**漂移检测**（对标 Winrift）：Windows Update / 组策略刷新把设置改回时，状态列表会标出"⚠ 已漂移"并显示当前值，点「应用」一键重新生效；托盘每 10 分钟静默比对一次，发现漂移气泡提醒、点击直达优化页。

### 清理 — 先扫描后清理
- **系统层**：临时文件 / Windows Update 缓存 / 回收站 / 缩略图缓存
- **构建产物（对标 `mo purge`）**：扫描 node_modules / target / venv / build / dist 等可重建目录，按项目分组展示，删除进回收站；7 天内有写入、含密钥文件、嵌套 Git 仓库的目录自动跳过并注明原因
- **应用与开发缓存（对标 `mo clean`）**：Chrome / Edge / Firefox 浏览器缓存与 npm / pnpm / pip / NuGet / Gradle 开发缓存；正在运行的程序整类跳过并说明原因，路径白名单（`~\.config\falco\whitelist.txt`，右键添加）长期保护，清理为永久删除、程序自动重建
- **安装包清理（对标 `mo installer`）**：扫描下载与桌面目录中的 .exe / .msi / .msix / .msp 安装包及 50MB 以上的 .zip 分发包；30 天前下载的默认勾选，删除进回收站

### 软件 — 应用管理
开机启动项查看 + 已安装应用管理（按大小排序、一键卸载）+ **卸载残留清理**（对标 `mo uninstall`）：卸载后扫描遗留的数据目录（AppData / ProgramData）、失效卸载注册表项、指向不存在目标的快捷方式；与已安装应用同厂商的目录标注"疑似共享"默认不勾选，目录删除进回收站、注册表删除前自动备份。

### 托盘 HUD — 常驻系统托盘
托盘图标 Tooltip 实时显示 CPU / 内存占用（复用指标采集数据，零额外开销）；双击图标恢复主窗口；右键菜单支持打开主界面、暂停/恢复监控、切换"关闭按钮最小化到托盘"、退出。关闭主窗口默认最小化到托盘继续监控（首次会气泡提示，可在右键菜单关闭该行为）。

### 分析 — 透视系统
系统信息 / 关键服务状态 / **目录占用浏览器**（双击行下钻子目录、面包屑显示当前路径、⬆ 返回上级、自动跳过 junction）/ 大文件扫描（结果可导出 JSON，对齐 `--json`）/ 备份与日志入口。目录占用与大文件列表支持点击表头按名称 / 大小 / 文件数排序（大小列按数值排序）。

## 快速开始

> 🐣 **第一次使用？请阅读 [User-Guide.md](./User-Guide.md)**——面向新手的逐步说明：两种启动模式、五个页面各自怎么用、哪些操作放心做、出问题如何一键恢复。

### 环境要求

- Windows 10 / 11
- Windows PowerShell 5.1+（系统自带，无需安装任何东西）
- PowerShell 7+（可选；`FalcoLauncher.vbs` 启动器会优先使用）

### 运行方式

**方式一**：双击 `FalcoLauncher.vbs` —— 优先查找 PowerShell 7 静默启动，未安装则自动回退系统自带 PowerShell

**方式二**：右键 `Falco.ps1` → "使用 PowerShell 运行"

**方式三**：命令行运行

```powershell
powershell -ExecutionPolicy Bypass -File .\Falco.ps1
```

**安装包**：用 [Inno Setup 6](https://jrsoftware.org/isinfo.php) 编译 `Falco-Setup.iss`，生成 `dist/Falco-1.2.0.exe`——安装到 `%ProgramFiles%\Falco`、创建开始菜单项并附带使用指南。

### 权限说明

以**管理员身份**运行可获得全部功能；非管理员模式下仅监控与分析可用，优化 / 清理按钮自动禁用，界面顶部提供一键提权按钮。

脚本启动时自动处理 STA 线程与高分屏 DPI 缩放，双击直开即可。

## 安全与恢复机制

Falco 的核心原则：**每一次修改、每一次删除都必须可撤销，且修改后必须验证生效。**

- 任何优化操作执行前，自动导出涉及的注册表键（`reg export`）与服务状态（JSON）到备份目录
- **修改后回读验证**（V2 验证引擎）：注册表/服务每次修改后立即回读比对，不一致自动重试一次，仍失败则回滚为修改前的值并在日志标 FAIL；一键优化结束输出"N 项验证通过 / M 项失败已回滚"汇总
- 自动生成恢复脚本 `Restore-Falco.ps1`，一键还原所有已备份的注册表与服务设置
- 删除类操作优先进回收站，所有操作写入日志 `Falco.log`，界面内可实时查看

运行时数据统一存放在 `C:\ProgramData\Falco\`：

```
C:\ProgramData\Falco\
├── Backups\              # 注册表导出 (.reg) 与服务状态备份 (.json)
├── Restore-Falco.ps1   # 自动生成的恢复脚本
├── tweaks-state.json     # 优化项修改记录（期望值/原始值，供单项还原与漂移检测）
├── Falco.log           # 操作日志
└── state.json            # 备份时间与硬件快照
```

## 项目结构

```
Falco/
├── Falco.ps1                       # 主程序（单文件，含全部功能）
├── FalcoLauncher.vbs               # 启动器（优先 PowerShell 7，静默启动）
├── Falco-CLI.ps1                   # 命令行版（与 GUI 共享引擎）
├── Falco-Setup.iss                 # Inno Setup 6 安装包脚本
├── falco.ico                       # 应用图标（多尺寸）
├── falco logo.png                  # Logo 原图
├── User-Guide.md                   # 新手使用指南（英文）
├── assets/                         # 界面素材
└── lib/LibreHardwareMonitor/       # 温度/硬件传感器库（可选，缺失时自动降级）
```

## 设计参考与致谢

- [Mole](https://github.com/tw93/Mole) —— 模块划分、界面风格与"修改前先备份、删除可撤销"的安全理念

## 许可证

本项目基于 [GPL-3.0](./LICENSE) 许可证开源。

`lib/LibreHardwareMonitor/` 下的第三方组件 [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（v0.9.6）遵循 **Mozilla Public License 2.0（MPL-2.0）** 许可，作为独立的可选传感器提供方与本项目分离：这些文件保留其原始许可与声明（见 `lib/LibreHardwareMonitor/NOTICE.txt`），不受本项目 GPL-3.0 条款影响。

## 免责声明

本工具会修改系统注册表与服务配置。虽然所有修改均有备份且可恢复，仍建议：

1. 首次使用前手动创建一个系统还原点
2. 重要数据自行做好备份
3. 恢复默认设置后建议重启系统
