# 参与贡献

感谢你愿意花时间。这个项目很小，规则也就几条。

## 报 Bug

开 Issue 时请带上：

1. Windows 版本（如 Windows 10 22H2 / Windows 11 23H2）与屏幕缩放比例（100% / 125% / 150%）
2. 复现步骤，越具体越好
3. `<数据目录>\logs\` 下对应日期的日志
4. 跑一次 `截图提醒.exe --check`，把生成的 `logs\selfcheck_*.txt` 附上

## 提 PR

- 先开 Issue 说清要改什么，避免白做
- 从 `main` 拉分支，一个 PR 只做一件事
- 提交前本地跑通：

```bash
dotnet build src/ScreenRemind/ScreenRemind.csproj -c Release
dotnet run --project tests/SelfTest/SelfTest.csproj
```

- 改动涉及重复规则、时间计算、存储格式时，**必须补 `tests/SelfTest/Tests.cs` 里的断言**，
  并在 PR 描述里写清边界（月末、闰年、跨年、睡眠唤醒）

## 代码约定

- 分层不要破：`UI` 不碰文件；`Core` 不碰控件；P/Invoke 只出现在 `Platform/NativeMethods.cs`
- 关键逻辑写**中文注释**，尤其是不直观的地方（DPI、钩子、时间边界）
- 不要为了"看起来高级"堆抽象；复杂度必须换来收益
- 提交信息用一句话说清做了什么，中文英文都行

## 不接受的方向

- 加入联网、云同步、账号体系、遥测（这个工具的核心卖点就是离线与轻）
- 引入需要安装运行时的依赖（会破坏"单文件便携"这个前提）
- 改动后体积大幅增加的重型框架
