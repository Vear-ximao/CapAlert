// 自检工程只把 Core/*.cs 编进程序集，而 ReminderAction 定义在 UI 层（UI/ReminderForm.cs）。
// 这里提供同命名空间、同成员顺序的最小桩，使 Core/RepeatFlow.ResolveAction 可在 SelfTest 中编译并断言（R2）。
// 正式程序集不会编译本文件，运行时的真实类型仍是 UI/ReminderForm.cs 中的定义。
namespace ScreenRemind.UI;

public enum ReminderAction { None, Acknowledged, Snoozed, Renew }
