using System.Text;
using ScreenRemind.Core;

namespace SelfTest;

/// <summary>
/// 无界面自检：重复规则、数据恢复、路径、备份、免打扰、密码。
/// 每个用例都断言具体值，不写「应该没问题」。
/// </summary>
internal static class Program
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 所有文件操作都在临时目录里做，绝不碰真实数据
        var sandbox = Path.Combine(Path.GetTempPath(), "ScreenRemindSelfTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sandbox);
        Environment.SetEnvironmentVariable("SCREENREMIND_HOME", sandbox);

        Console.WriteLine($"沙箱目录：{sandbox}\n");

        Run("重复规则", RepeatEngineTests.Run);
        Run("数据存储", StorageTests.Run);
        Run("路径规则", PathTests.Run);
        Run("快捷键组合", HotkeyTests.Run);
        Run("免打扰时段", QuietTests.Run);
        Run("密码", PasswordTests.Run);
        Run("备份还原", BackupTests.Run);
        Run("锚点与出口逻辑", AnchorFlowTests.Run);
        Run("第二轮残留修复", Round2FixTests.Run);

        Console.WriteLine($"\n===== 通过 {_pass}　失败 {_fail} =====");

        try { Directory.Delete(sandbox, true); } catch { }

        return _fail == 0 ? 0 : 1;
    }

    private static void Run(string group, Action action)
    {
        Console.WriteLine($"— {group}");
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Fail($"{group} 抛出异常：{ex.GetType().Name} {ex.Message}");
        }
        Console.WriteLine();
    }

    public static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _pass++;
            Console.WriteLine($"  PASS  {name}");
        }
        else
        {
            Fail($"{name}　{detail}");
        }
    }

    public static void Equal(string name, object? expected, object? actual)
    {
        var ok = Equals(expected, actual);
        Check(name, ok, ok ? null : $"期望 {expected ?? "null"}，实际 {actual ?? "null"}");
    }

    private static void Fail(string message)
    {
        _fail++;
        Console.WriteLine($"  FAIL  {message}");
    }
}
