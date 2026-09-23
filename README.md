# Falco CLI

为 Windows 10/11 打造的系统优化与清理命令行工具。GUI 图形界面版为付费软件（见下文）。

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](./LICENSE)

## 免费版与付费版

| 版本 | 形态 | 许可 | 获取方式 |
|------|------|------|----------|
| **Falco CLI**（本仓库） | 命令行工具 | GPL-3.0，免费开源 | `git clone` 即用 |
| **Falco GUI**（图形界面版） | 图形界面：状态仪表盘、一键优化、深度清理、软件管理、磁盘分析、托盘 HUD | 专有软件，付费使用 | 联系作者获取安装包 |

GUI 版为闭源商业软件，不随本仓库分发源代码；本仓库仅包含免费开源的命令行版。

> ⚠️ **安装 GUI 版时的提示（过渡说明）**：安装程序暂未做数字签名。运行安装包时若 Windows 弹出蓝色 SmartScreen"已保护你的电脑"，请点"更多信息"→"仍要运行"；UAC 提示"未知发布者"属正常现象，点"是"继续安装即可。

## 功能

- **status**：健康度评分（0–100）+ CPU / 内存 / 磁盘 / 温度 / 开机时长快照，`-Json` 机器可读
- **tweaks**：六个系统优化项（传递优化 / 后台应用 / 视觉效果 / 活动时间 / SysMain / Windows Search）的查看、备份、应用与还原，写入前回读验证、失败自动回滚
- **clean**：临时文件 / Windows Update 缓存 / 缩略图缓存 / 回收站的扫描与清理
- **purge**：构建产物（node_modules / target / build 等）扫描，7 天活跃、含密钥、嵌套仓库自动跳过
- **large**：大文件扫描

温度数据优先来自 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（可选依赖，`lib/` 目录），缺失时自动降级为 ACPI 热区。

## 使用

```powershell
# 系统状态与健康度
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 status -Json

# 优化项
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks list
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks apply safe        # 低风险四项
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks apply AH-001 -AHStart 8 -AHEnd 22
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks revert <Id|all>

# 清理
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean scan
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean run -Items temp,thumbs -Yes

# 构建产物与大文件
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 purge D:\code -Days 7
powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 large C:\ -MinMB 500 -Top 20
```

### 权限说明

`status` / `tweaks list` / `clean scan` / `purge` / `large` 为只读或无需提权；`tweaks apply / revert`、`clean run` 中的 WU 缓存清理需要**管理员权限**。所有修改执行前自动备份到 `C:\ProgramData\Falco\Backups`，还原脚本为 `Restore-Falco.ps1`。

## 项目结构

```
Falco/
├── Falco-CLI.ps1                   # 命令行工具（引擎内嵌，单文件）
└── lib/LibreHardwareMonitor/       # 温度/硬件传感器库（可选，缺失时自动降级）
```

## 许可证

本项目基于 [GPL-3.0](./LICENSE) 许可证开源。

`lib/LibreHardwareMonitor/` 下的第三方组件 [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（v0.9.6）遵循 **Mozilla Public License 2.0（MPL-2.0）** 许可，作为独立的可选传感器提供方与本项目分离：这些文件保留其原始许可与声明（见 `lib/LibreHardwareMonitor/NOTICE.txt`），不受本项目 GPL-3.0 条款影响。

## 免责声明

本工具会修改系统注册表与服务配置。虽然所有修改均有备份且可恢复，仍建议：

1. 首次使用前手动创建一个系统还原点
2. 重要数据自行做好备份
3. 恢复默认设置后建议重启系统
