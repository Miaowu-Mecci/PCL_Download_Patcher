# Plain Craft Launcher (PCL) myPCL 一次性下载修复补丁

本仓库仅包含独立的 Windows 修复补丁及其源码和构建流程。

第三方工具，恢复已适配官方 PCL 2.99.0.0 的原版下载、整合包安装和相关入口。
原作者：龙腾猫跃；[原项目](https://github.com/Meloong-Git/PCL)，[赞助原作者](https://meloong.com/afd/a/LTCat)。

## 使用

1. 完全退出 PCL，包括托盘图标。
2. 运行 `PCL-myPCL-DownloadPatcher.exe`，选择官方 PCL 的 EXE。
3. 点击“备份并修复”。成功后直接运行原位置的 PCL，以后无需再运行补丁。

原文件自动保存为同目录的 `原文件名.exe.bak`；已有备份不会被覆盖。
需要还原时，退出 PCL，再用该备份替换修复后的 EXE。
重复选择已经修复的文件，会提示无需重复操作。

需要 Windows 和 .NET Framework 4.8，无需 Python 或额外 DLL。
补丁离线运行，不启动 PCL、不注入进程。文件正在运行、只读、被锁定或版本不匹配时会停止修改。

## 适配与验证

仅支持本次检查的官方 EXE，不能保证其他同版本构建或后续版本可用。

输入 SHA256：`6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386`

修复后 SHA256：`d11739846d2b6162c8ccc219144c8efff875f7e6055ecf2c2e711b98c7a14abd`

修复结果与已生成的官方 EXE 修复副本逐字节一致：调整 6 个下载相关方法，其余 5993 个方法和内嵌资源未变。
这些方法已完成静态 IL、跳转、异常处理边界及求值栈校验；PCL 实际联网下载仍需在 Windows 上验证。

内嵌 `patch.bin` 是 5051 字节的 COPY/INSERT 配方，从用户原文件复制未改内容，生成并校验完整结果。
先在同目录生成临时文件并验证，随后使用 Windows `File.Replace` 原子替换并备份。
不支持时不会尝试猜测位置或盲目修改。

## 构建与测试

在具有 .NET Framework 4.8 开发工具的 Windows 上：

```powershell
msbuild PclDownloadPatcher.csproj /p:Configuration=Release
Start-Process .\bin\Release\PCL-myPCL-DownloadPatcher.exe -ArgumentList '--self-test', 'patcher-tests.txt' -Wait
Get-Content patcher-tests.txt
```

自测涵盖修复、备份、重复运行、未知文件、已有备份、只读、锁定、损坏配方及 GUI 创建。
