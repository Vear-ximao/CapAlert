namespace ScreenRemind.UI;

/// <summary>配色、字号、控件样式的单一事实来源。改外观只改这里。</summary>
public static class Theme
{
    // 浅色主题（与常见 Windows 浅色桌面一致）
    public static readonly Color Bg = Color.FromArgb(245, 247, 251);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color SubText = Color.FromArgb(107, 114, 128);
    public static readonly Color Accent = Color.FromArgb(38, 108, 246);
    public static readonly Color AccentHover = Color.FromArgb(24, 88, 208);
    public static readonly Color Danger = Color.FromArgb(220, 53, 69);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color ViewerBg = Color.FromArgb(32, 36, 44);
    public static readonly Color MaskBg = Color.FromArgb(18, 20, 26);
    public static readonly Color WarnBg = Color.FromArgb(255, 247, 237);
    public static readonly Color WarnText = Color.FromArgb(180, 83, 9);

    // 从各窗体收敛出来的颜色：说明文字对比度更高，深色查看器上的文字更清楚
    public static readonly Color MutedText = Color.FromArgb(92, 102, 120);
    public static readonly Color ViewerText = Color.FromArgb(195, 202, 214);
    public static readonly Color ViewerOverlay = Color.FromArgb(190, 24, 28, 36);
    public static readonly Color PreviewBg = Color.FromArgb(238, 244, 255);
    public static readonly Color ThumbBg = Color.FromArgb(235, 238, 243);
    public static readonly Color CalendarOff = Color.FromArgb(238, 241, 246);
    public static readonly Color CalendarMuted = Color.FromArgb(128, 138, 156);
    public static readonly Color SuccessHover = Color.FromArgb(16, 140, 60);

    // 圆角与间距（统一留白节奏；WinForms 原生控件圆角有限，先统一尺度）
    public const int Radius = 8;
    public const int RadiusSm = 4;
    public const int SpaceXs = 4;
    public const int SpaceSm = 8;
    public const int SpaceMd = 12;
    public const int SpaceLg = 20;

    public static readonly string FontFamily = "Microsoft YaHei UI";

    // 静态缓存：过去每次访问属性都 new Font，拖窗口/刷新列表会持续分配 GDI 句柄。
    // 缩放场景统一走 UiZoom.GetFont 的缓存，这里只放固定字阶。
    public static readonly Font Small = new(FontFamily, 9F);
    public static readonly Font Base = new(FontFamily, 9F);
    public static readonly Font BaseBold = new(FontFamily, 9F, FontStyle.Bold);
    public static readonly Font Body = new(FontFamily, 10F);
    public static readonly Font Title = new(FontFamily, 14F, FontStyle.Bold);
    public static readonly Font Subtitle = new(FontFamily, 10.5F, FontStyle.Bold);

    // 三级字阶对应的语义色
    public static readonly Color TextPrimary = Text;
    public static readonly Color TextSecondary = MutedText;
    public static readonly Color TextHint = SubText;

    private static Icon? _appIcon;

    /// <summary>品牌图标（嵌入资源）。托盘与所有窗口标题栏共用同一实例，避免重复创建 GDI 句柄。</summary>
    public static Icon AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            using var stream = typeof(Theme).Assembly.GetManifestResourceStream("ScreenRemind.Assets.app.ico");
            _appIcon = stream != null ? new Icon(stream) : SystemIcons.Application;
            return _appIcon;
        }
    }

    public static void ApplyForm(Form form)
    {
        form.BackColor = Bg;
        form.ForeColor = Text;
        form.Font = Base;
        form.AutoScaleMode = AutoScaleMode.None;
        form.Icon = AppIcon;
    }

    /// <summary>主操作：实心主色、更高的点击区、明确的悬停反馈。</summary>
    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = BaseBold,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Padding = new Padding(SpaceSm, 0, SpaceSm, 0)
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 76, 186);
        return b;
    }

    /// <summary>次要操作：白底描边幽灵按钮。</summary>
    public static Button GhostButton(string text)
    {
        var b = new Button
        {
            Text = text,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Card,
            ForeColor = Text,
            Font = Base,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Padding = new Padding(SpaceSm, 0, SpaceSm, 0)
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Bg;
        return b;
    }

    /// <summary>危险操作：红色描边 + 淡红悬停，与普通次要操作拉开距离。</summary>
    public static Button DangerButton(string text)
    {
        var b = GhostButton(text);
        b.ForeColor = Danger;
        b.FlatAppearance.BorderColor = Color.FromArgb(248, 190, 196);
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 241, 242);
        return b;
    }

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextSecondary,
        Font = Small
    };

    /// <summary>
    /// 把控件钉在父容器右侧。
    /// 不用 Anchor 是因为：面板在构造时还没被 Dock 铺开，Anchor 会按当时那个很小的宽度
    /// 算出负的右边距，结果控件被推到窗口外（实测踩过）。改成按真实宽度重排，稳。
    /// </summary>
    public static void PinRight(Control child, int rightMargin)
    {
        if (child.Parent is not { } parent) return;

        void Place()
        {
            var left = parent.ClientSize.Width - child.Width - rightMargin;
            child.Left = Math.Max(0, left);
        }

        parent.SizeChanged += (_, _) => Place();
        parent.HandleCreated += (_, _) => Place();
        Place();
    }
}
