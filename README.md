# PCL MC 下载功能恢复工具

独立的 Windows 补丁，用于恢复 PCL 中被关闭的 Minecraft 下载入口和整合包安装。由第三方制作，与 PCL 官方无关。

原项目 Plain Craft Launcher 的作者是龙腾猫跃：[支持原作者](https://afdian.net/@LTCat)。本仓库保留了 [PCL 原项目的许可与使用指南](PCL-LICENCE.txt)。

[下载已编译的 Windows 补丁](download/PCL-McDownload-Restore.exe)：下载后可直接使用，无需构建。

## 使用

1. 将 `PCL-McDownload-Restore.exe` 放到 `Plain Craft Launcher 2.exe` 所在目录。
2. 关闭 PCL，双击补丁运行一次。仅复制文件不会自动修改 PCL。
3. 补丁会核对程序身份和下载代码结构、备份原文件并恢复下载功能。完成后，按原来的方式打开 PCL。

备份文件保存在 PCL 原目录，内容是打补丁前的原版程序。名称会写明 `original` 和版本号，例如 `Plain Craft Launcher 2.exe.original-2.99.0.0.bak`。每次需要重新打补丁时都会保留旧备份。如需还原，关闭 PCL，复制对应备份文件并重命名为 `Plain Craft Launcher 2.exe`，替换当前程序；保留 `.bak` 文件以便再次还原。

本工具只读取和修改同目录下的 `Plain Craft Launcher 2.exe`。补丁可以离线运行，需要 Windows 和 .NET Framework 4.8（与当前 PCL 的运行要求一致）。已在 Windows 上测试公开仓库的 2.99.0.0 构建和正式发行构建。其他 Windows 电脑如果满足上述条件，且 PCL 下载代码结构相同，预计可以使用；尚未逐台测试，也未验证 Wine、Linux 或 macOS。后续版本若保留相同的下载代码结构，也可运行此补丁；若相关代码改变，补丁会拒绝修改，需要更新补丁规则。PCL 更新后可能覆盖修改，此时需再次运行补丁。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建脚本从官方仓库下载 2.14.0.0 正式版作为恢复用的代码来源，从 NuGet 下载 Mono.Cecil；两份下载都经过 SHA-256 校验。补丁内以压缩形式存放恢复用的代码来源，运行时解压，因此可以离线使用。构建产物位于 `dist/PCL-McDownload-Restore.exe`。仓库不保存独立的 PCL 程序文件。

源码仅适配当前经过核验的下载代码结构。用户应自行确认使用与分发方式符合 [PCL 的许可及使用指南](PCL-LICENCE.txt)。
