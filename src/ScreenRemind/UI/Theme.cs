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

    public static readonly string FontFamily = "Microsoft YaHei UI";

    public static Font Base => new(FontFamily, 9F);
    public static Font BaseBold => new(FontFamily, 9F, FontStyle.Bold);
    public static Font Small => new(FontFamily, 8F);
    public static Font Title => new(FontFamily, 13F, FontStyle.Bold);
    public static Font Subtitle => new(FontFamily, 10.5F, FontStyle.Bold);

    public static void ApplyForm(Form form)
    {
        form.BackColor = Bg;
        form.ForeColor = Text;
        form.Font = Base;
        form.AutoScaleMode = AutoScaleMode.None;
    }

    public static Button PrimaryButton(string text)
    {
        var b = new Button
        {
            Text = text,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = BaseBold,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        return b;
    }

    public static Button GhostButton(string text)
    {
        var b = new Button
        {
            Text = text,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = Card,
            ForeColor = Text,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = Border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Bg;
        return b;
    }

    public static Button DangerButton(string text)
    {
        var b = GhostButton(text);
        b.ForeColor = Danger;
        return b;
    }

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SubText,
        Font = Base
    };

    public static Label Body(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Text,
        Font = Base
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
