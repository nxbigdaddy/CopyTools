# CopyTools

CopyTools 是面向 Windows 10/11 x64 的文件复制、目录同步、版本备份和自动化工具。1.1.0 在完整功能基线上加入一体化标题栏、左侧工作区导航和带真实界面截图的本地 CHM 使用帮助；所有危险操作都先形成可审查计划，并通过隔离区、版本保留、阈值和竞态检查保护用户数据。

## 主要能力

- 复制与同步：普通复制、增量复制、单向同步、镜像同步、累加同步、移动同步、更新同步、双向同步、仅校验。
- 备份与恢复：完全备份、历史增量备份、时间点恢复、选择性恢复、覆盖前版本、AES-256-GCM 加密压缩包、可选 VSS 一致性快照。
- 安全执行：操作预览、逐项排除、覆盖确认、删除数量/比例阈值、危险路径和空间校验、原子替换、源/目标竞态检查、隔离与恢复。
- 可靠性：暂停、继续、取消、失败重试、设备可用性等待、未完成任务恢复、大文件分块续传、SHA-256 严格比较和完成校验。
- 自动化：每日/每周/每月/固定间隔、应用启动、目录变化、路径或可移动设备可用触发；任务队列、优先级、分组、依赖和前后置受控命令。
- 存储：本地/移动磁盘、UNC/映射驱动器、MTP、FTP/FTPS、SFTP、WebDAV、S3 兼容对象存储、阿里云 OSS、腾讯云 COS、OneDrive、Google Drive、Dropbox、百度网盘。
- 管理与集成：SQLite 任务和运行历史、文本/CSV/JSON 日志导出、邮件/Webhook 通知、Windows Credential Manager、插件式存储和校验扩展、配置导入导出、重复文件检测。
- 使用方式：WPF 桌面端、系统托盘、CLI、Windows Service、便携包和自解压安装程序。
- 使用帮助：单击标题栏“帮助”、侧边栏帮助按钮或按 `F1`，打开随软件发布的离线 CHM；包含目录、索引、全文搜索和 6 张真实界面截图。

完整逐项状态见 [功能列表](docs/文件复制同步工具-功能列表.md)，历次实现与验证见 [迭代记录](docs/迭代开发记录.md)。

## 开发与运行环境

- Windows 10/11 x64
- .NET 8 SDK（本机已实际构建）
- PowerShell 5.1 或更高版本
- Visual Studio 2022 可选，命令行即可完成构建和发布

启动桌面端：

```powershell
.\scripts\run.ps1
```

也可以双击 `start.bat`。

## 构建与测试

```powershell
.\verify.bat
```

或者：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

验证脚本执行 Release 全解决方案构建，并把警告视为错误，然后运行离线核心回归测试。当前冻结基线为 **0 警告、0 错误、88/88 测试通过**。

## CLI 与后台服务

查看命令行帮助：

```powershell
dotnet run --project .\src\CopyTools.Cli -- --help
```

发布后安装服务需要管理员 PowerShell：

```powershell
.\scripts\install-service.ps1
```

卸载服务：

```powershell
.\scripts\uninstall-service.ps1
```

桌面端默认数据在 `%LOCALAPPDATA%\CopyTools`；Windows Service 以系统身份运行，默认数据在 `%ProgramData%\CopyTools`。先从桌面端“应用设置”导出配置，再执行包内服务程序的 `--import-config FILE --data-dir "%ProgramData%\CopyTools"`，即可把任务和计划部署到服务。服务会执行到期计划及自动触发任务，日志按设置的级别和保留天数清理。

## 发布

生成自包含的 Windows x64 便携包、安装程序、更新清单和校验文件：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Version 1.1.0
```

输出位于 `artifacts\release`。发布脚本会先运行 `scripts\build-help.ps1` 编译并检查 CHM，再验证安装包内嵌载荷、拒绝磁盘根目录、执行隔离安装、运行服务自检和 CLI 帮助命令，并生成 `SHA256SUMS.txt`。

## 解决方案结构

```text
src/
├─ CopyTools.Domain/          领域模型、任务、计划和设置
├─ CopyTools.Engine/          扫描、同步策略、安全复制、备份和恢复
├─ CopyTools.Application/     工作流、调度、队列、恢复和日志导出
├─ CopyTools.Infrastructure/  SQLite、存储适配、凭据、VSS、通知和插件
├─ CopyTools.Desktop/         WPF 桌面界面与系统托盘
├─ CopyTools.Cli/             命令行入口
├─ CopyTools.Service/         Windows Service 后台宿主
└─ CopyTools.Installer/       自解压安装与卸载程序

tests/
└─ CopyTools.Core.Tests/      无第三方测试框架的离线回归套件
```

核心执行链：

```text
验证路径 → 扫描两端 → 生成可审查计划 → 风险确认 → 临时文件写入
→ 内容校验 → 原子提交/隔离 → 保存运行明细与同步基线
```

## 数据位置与安全边界

- 默认数据目录：`%LOCALAPPDATA%\CopyTools`。
- 任务、快照和历史保存于 `copytools.db`，设置保存于 `settings.json`；数据库不保存用户文件内容。
- 密码、令牌和私钥引用通过 Windows Credential Manager 保存，日志会脱敏敏感字段。
- 镜像、移动、更新和删除传播默认移入任务隔离区，不执行不可恢复的静默删除。
- 覆盖前可启用旧版本保留；版本数量和天数可独立配置。
- 预览后源文件变化或计划外目标出现时，执行器拒绝覆盖并记录失败。
- VSS 仅适用于本地 NTFS 卷并需要相应系统权限；远程账号、MTP 设备及 VSS 的端到端验证依赖用户自己的设备和凭据。

## 当前发布状态

1.1.0 体验升级已完成。一体化窗口、左侧导航、F1/标题栏/托盘帮助入口和图文 CHM 均经过真实 Windows 桌面检查。自动化测试、真实 WPF 复制烟测、托盘恢复重绘和发布包自检均已纳入交付验证。FTP/云盘/MTP/VSS 等外部集成已实现协议与错误处理，但在没有真实账号、设备或管理员权限的开发机上只进行本地/协议级验证；上线前应在目标环境补充连接验收。

迭代 12 已把 [功能增强建议](docs/功能增强建议列表.md) 全量落地：块级增量传输、采样校验、备份链自检/不可变/GFS/恢复演练、条件触发与维护窗口、任务链、防勒索与审计哈希链、MinIO/B2/Azure Blob/坚果云等存储、IM 通知、右键集成、CLI 机器输出与 PowerShell 模块，以及仪表盘/全局搜索/通知中心/失败聚合/存储趋势/健康评分等逻辑。离线核心回归由 88 项增至 **126 项**，Release 构建保持 0 警告 0 错误。

迭代 13 已把 [AI 辅助功能规划](docs/AI辅助功能规划.md) 的 19 项能力全量落地：可插拔 `IAiAssistant` 抽象（本地 Ollama + 云端 OpenAI 兼容 + 规则降级三实现）、自然语言建任务/模式推荐/过滤规则/对话式帮助、执行前风险解释/冲突建议/失败根因/重试建议、文件分类/相似检测/敏感识别/归档整理、空间预警/健康报告/调度建议/耗时预估/异常检测/演练解读/传输策略。所有服务均有确定性核心 + 可选 LLM 增强，无模型或网络时自动回退，AI 只做建议、不越过预览/隔离/阈值安全闭环。离线核心回归增至 **150 项**，Release 构建保持 0 警告 0 错误。

---

## 许可证

本项目采用 [MIT 许可证](LICENSE)。

## 第三方与上游声明

本项目包含或依赖第三方组件，其版权与许可证归各自作者所有，
详见仓库内的依赖清单与相关声明文件。若本项目基于其他开源项目二次开发，
已在正文中注明上游项目与所基于的版本，本项目为**非官方**衍生作品。

## 现状与限制

- 本项目为个人开发项目，按“现状”提供，不附带任何明示或暗示的担保。
- README 与代码注释中记录的验证结论仅针对当时的具体环境与版本，不代表在其他环境下同样成立。
- 涉及本地凭据的部分统一使用系统凭据库或环境变量，仓库内不含真实密钥；
  示例配置仅使用占位符。
