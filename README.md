# PCL MC 下载功能恢复工具

独立的 Windows 小工具，用于恢复 **PCL 2.99.0.0 正式版**中被关闭的 Minecraft 下载入口和整合包安装，并在处理后启动 PCL。由第三方制作，与 PCL 官方无关。

## 使用

1. 下载并运行 `PCL-McDownload-Restore.exe`。
2. 工具会查找同目录的 PCL、正在运行的 PCL，以及桌面、开始菜单和任务栏快捷方式指向的 PCL。如果没有找到，选择 `Plain Craft Launcher 2.exe`。
3. 工具核对程序的 SHA-256。只有与官方 2.99.0.0 正式版完全一致时才会修改；它会先关闭该 PCL，备份原文件，恢复下载功能，再启动 PCL。

备份文件保存在 PCL 原目录，名称为 `Plain Craft Launcher 2.exe.pcl2-2.99-original.bak`。如需还原，关闭 PCL，把备份文件改回 `Plain Craft Launcher 2.exe`。

本工具只修改所选的 PCL 可执行文件。其他版本、二次修改版或未来新版会被拒绝；需要针对新版重新审核和制作补丁。PCL 后续自动更新也可能覆盖修改。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建脚本从官方仓库下载 2.14.0.0 正式版作为恢复用的代码来源，从 NuGet 下载 Mono.Cecil；两份下载都经过 SHA-256 校验。构建产物位于 `dist/PCL-McDownload-Restore.exe`。仓库不保存 PCL 程序文件。

源码仅适配当前经过核验的 2.99.0.0 正式版。用户应自行确认使用与分发方式符合 [PCL 的许可及使用指南](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)。
