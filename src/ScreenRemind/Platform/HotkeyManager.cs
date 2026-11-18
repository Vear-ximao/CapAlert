using System.Runtime.InteropServices;
using System.Windows.Forms;
using ScreenRemind.Core;

namespace ScreenRemind.Platform;

public enum HotkeyEngine
{
    /// <summary>系统级注册，能事前发现占用，最稳</summary>
    RegisterHotKey,
    /// <summary>键盘钩子，支持多键/纯修饰键组合，但无法事前检测占用</summary>
    KeyboardHook
}

/// <summary>
/// 全局热键。用户录制任意组合（Ctrl+Alt、Ctrl+Alt+S、A+S、Ctrl+Shift+F1…），
/// 这里自动挑引擎：恰好「若干修饰键 + 1 个普通键」走 RegisterHotKey，其余走低级键盘钩子。
/// 同一个隐藏窗口还兼任：单实例唤醒广播、钩子触发的消息中转。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xB10;
    private const int WmAppFire = 0x8001;
    private const int WmKillFocus = 0x0087;
    private const int PressedTimeoutSeconds = 5;

    /// <summary>单实例唤醒：向所有顶层窗口广播，只有本程序的隐藏窗口认这条消息。</summary>
    public static readonly uint WakeMessage = NativeMethods.RegisterWindowMessage("ScreenRemind_Wake_9F3C");

    private readonly HiddenWindow _window;
    private readonly object _gate = new();
    private readonly HashSet<uint> _pressed = new();
    private NativeMethods.LowLevelKeyboardProc? _hookProc;   // 必须强引用，否则被 GC 回收后钩子失效且无报错
    private IntPtr _hook = IntPtr.Zero;
    private HashSet<uint> _hookKeys = new();
    private bool _matched;
    private DateTime _lastFire = DateTime.MinValue;
    private DateTime _lastKeyDown = DateTime.MinValue;
    private HotkeySetting _setting = new();

    public HotkeyEngine Engine { get; private set; } = HotkeyEngine.RegisterHotKey;

    /// <summary>热键被按下（已切回 UI 消息循环）</summary>
    public event EventHandler? Pressed;

    /// <summary>另一个实例要求唤起本实例</summary>
    public event EventHandler? WakeRequested;

    public HotkeyManager()
    {
        _window = new HiddenWindow(this);
    }

    public IntPtr Handle => _window.Handle;

    /// <summary>判断组合会走哪个引擎（供界面提前提示风险）。</summary>
    public static HotkeyEngine ChooseEngine(IReadOnlyList<uint> keys)
    {
        var plainCount = keys.Count(k => !HotkeyKeys.IsModifier(k));
        var mods = HotkeyKeys.ToRegisterModifiers(keys);
        return mods != 0 && plainCount == 1 ? HotkeyEngine.RegisterHotKey : HotkeyEngine.KeyboardHook;
    }

    /// <summary>应用一组热键。返回 false 时 error 里有可读原因，调用方应保留旧设置。</summary>
    public bool Apply(HotkeySetting setting, out string? error)
    {
        error = null;
        Detach();

        _setting = setting.Clone();
        var keys = _setting.EffectiveKeys();

        if (keys.Count < 2)
        {
            error = "至少要两个键，例如 Ctrl+Alt、Ctrl+Alt+S、A+S";
            return false;
        }

        var mods = HotkeyKeys.ToRegisterModifiers(keys);
        var plain = keys.Where(k => !HotkeyKeys.IsModifier(k)).ToList();
        Engine = ChooseEngine(keys);

        if (Engine == HotkeyEngine.RegisterHotKey)
        {
            if (!NativeMethods.RegisterHotKey(_window.Handle, HotkeyId, mods | NativeHotkey.ModNoRepeat, plain[0]))
            {
                var code = Marshal.GetLastWin32Error();
                error = code == 1409
                    ? $"{HotkeyText.Describe(keys)} 已被其他程序占用，请换一个"
                    : $"注册失败（错误码 {code}），请换一个组合";
                Log.Warn("RegisterHotKey 失败：" + error);
                return false;
            }

            Log.Info($"热键：{HotkeyText.Describe(keys)}（系统注册）");
            return true;
        }

        // 多键组合或纯修饰键组合：只能上键盘钩子
        _hookKeys = keys.ToHashSet();
        _hookProc = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            error = "键盘钩子安装失败（可能被安全软件拦截）。改用「Ctrl/Alt + 单个字母」这类组合会更稳";
            Log.Error("SetWindowsHookEx 失败：" + Marshal.GetLastWin32Error());
            _hookProc = null;
            _hookKeys.Clear();
            return false;
        }

        Log.Info($"热键：{HotkeyText.Describe(keys)}（键盘钩子）");
        return true;
    }

    /// <summary>探测组合现在能不能用（不改动已生效的热键）。钩子模式无法事前探测，直接返回可用。</summary>
    public static bool Probe(IReadOnlyList<uint> keys, out HotkeyEngine engine)
    {
        engine = ChooseEngine(keys);
        if (engine != HotkeyEngine.RegisterHotKey) return true;

        var probe = new HiddenWindow(null!);
        try
        {
            var mods = HotkeyKeys.ToRegisterModifiers(keys) | NativeHotkey.ModNoRepeat;
            var plain = keys.First(k => !HotkeyKeys.IsModifier(k));
            var ok = NativeMethods.RegisterHotKey(probe.Handle, HotkeyId + 1, mods, plain);
            if (ok) NativeMethods.UnregisterHotKey(probe.Handle, HotkeyId + 1);
            return ok;
        }
        catch
        {
            return false;
        }
        finally
        {
            probe.Dispose();
        }
    }

    private void Detach()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _hookProc = null;
        ClearPressed();
        lock (_gate)
        {
            _hookKeys.Clear();
        }
        if (_window.Handle != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, HotkeyId);
        }
    }

    /// <summary>清空半按状态。应用失焦、钩子卸载、按键超时都会调用（C-18）。</summary>
    private void ClearPressed()
    {
        lock (_gate)
        {
            _pressed.Clear();
            _matched = false;
        }
        _lastKeyDown = DateTime.MinValue;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var msg = wParam.ToInt32();
            var isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            var isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

            if (isDown)
            {
                // C-18：keyup 丢失（切出应用、远程桌面等）时，超时后清掉半按状态，避免误触发
                var now = DateTime.Now;
                if (_lastKeyDown != DateTime.MinValue && (now - _lastKeyDown).TotalSeconds > PressedTimeoutSeconds)
                {
                    ClearPressed();
                }
                _lastKeyDown = now;

                bool fire = false;
                lock (_gate)
                {
                    _pressed.Add(HotkeyKeys.Normalize(info.vkCode));
                    if (!_matched && _hookKeys.Count > 0 && _hookKeys.IsSubsetOf(_pressed))
                    {
                        _matched = true;
                        fire = true;
                    }
                }
                if (fire)
                {
                    // 绝不在钩子回调里做重活（截图要 100ms 级），只投递消息回 UI 循环
                    NativeMethods.PostMessage(_window.Handle, WmAppFire, IntPtr.Zero, IntPtr.Zero);
                }
            }
            else if (isUp)
            {
                lock (_gate)
                {
                    _pressed.Remove(HotkeyKeys.Normalize(info.vkCode));
                    if (_matched && !_hookKeys.IsSubsetOf(_pressed))
                    {
                        _matched = false;
                    }
                }
            }
        }

        // 始终放行：不吞键，避免影响正常输入
        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnHotkeyMessage()
    {
        var now = DateTime.Now;
        if ((now - _lastFire).TotalMilliseconds < 500) return;   // 防抖，避免长按连发
        _lastFire = now;
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Detach();
        _window.Dispose();
    }

    /// <summary>
    /// 隐藏的顶层窗口：接收 WM_HOTKEY 与广播唤醒消息。
    /// 注意不能用 HWND_MESSAGE 消息窗口——广播消息不会送达消息窗口。
    /// </summary>
    private sealed class HiddenWindow : NativeWindow, IDisposable
    {
        private readonly HotkeyManager? _owner;

        public HiddenWindow(HotkeyManager owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams
            {
                Caption = "ScreenRemindHidden",
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Style = 0,
                ExStyle = 0,
                Parent = IntPtr.Zero
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (_owner != null)
            {
                if (m.Msg == NativeMethods.WM_HOTKEY || m.Msg == WmAppFire)
                {
                    _owner.OnHotkeyMessage();
                    return;
                }
                if ((uint)m.Msg == WakeMessage)
                {
                    _owner.WakeRequested?.Invoke(_owner, EventArgs.Empty);
                    return;
                }
                // C-18：本程序窗口失焦时清掉半按状态，防止切走期间残留的按键组合误触发
                if (m.Msg == WmKillFocus)
                {
                    _owner.ClearPressed();
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}

/// <summary>键位的中文可读描述，设置界面与提示共用。</summary>
public static class HotkeyText
{
    public static string Describe(HotkeySetting setting) => Describe(setting.EffectiveKeys());

    public static string Describe(IReadOnlyList<uint> keys)
    {
        if (keys.Count == 0) return "（未设置）";
        return string.Join("+", keys.Select(KeyName));
    }

    public static string KeyName(uint vk)
    {
        if (vk == HotkeyKeys.Control) return "Ctrl";
        if (vk == HotkeyKeys.Alt) return "Alt";
        if (vk == HotkeyKeys.Shift) return "Shift";
        if (vk == HotkeyKeys.Win) return "Win";

        var key = (Keys)vk;
        return key switch
        {
            >= Keys.A and <= Keys.Z => key.ToString(),
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => "小键盘" + (key - Keys.NumPad0),
            >= Keys.F1 and <= Keys.F24 => key.ToString(),
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemPipe => "\\",
            Keys.OemQuestion => "/",
            Keys.Oemtilde => "`",
            Keys.Space => "空格",
            Keys.Enter => "回车",
            Keys.Tab => "Tab",
            Keys.Escape => "Esc",
            Keys.Back => "退格",
            Keys.Up => "↑",
            Keys.Down => "↓",
            Keys.Left => "←",
            Keys.Right => "→",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PgUp",
            Keys.PageDown => "PgDn",
            Keys.Insert => "Insert",
            Keys.Delete => "Delete",
            _ => key.ToString()
        };
    }

    /// <summary>能不能录进组合里（排除鼠标键、大小写锁定等无意义的键）。</summary>
    public static bool IsRecordable(uint vk)
    {
        var key = (Keys)vk;
        if (key is Keys.None or Keys.Capital or Keys.NumLock or Keys.Scroll or Keys.Apps) return false;
        return key is not (Keys.LButton or Keys.RButton or Keys.MButton or Keys.XButton1 or Keys.XButton2);
    }
}

/// <summary>进程级单实例：重复启动时唤起已有实例，不产生第二个进程。</summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\ScreenRemind_SingleInstance_9F3C";

    public static bool TryAcquire(out Mutex mutex)
    {
        mutex = new Mutex(true, MutexName, out var createdNew);
        return createdNew;
    }

    public static void SignalExisting()
    {
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, HotkeyManager.WakeMessage, IntPtr.Zero, IntPtr.Zero);
    }
}
