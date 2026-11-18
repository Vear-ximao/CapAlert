using System.Drawing.Drawing2D;

namespace ScreenRemind.UI;

/// <summary>
/// 任务列表为空时的引导面板：用 GDI 自绘一张轻量插画 + 三步上手文案，避免引入图片资源。
/// 有任务时由外部把 Visible 置 false，列表照常显示。
/// </summary>
public sealed class EmptyStatePanel : Panel
{
    private const string HotkeyText = "Ctrl + Alt + S";

    public EmptyStatePanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var w = ClientSize.Width;
        var h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;

        // 插画中心略微上移，给底部文案留位置
        var cx = w / 2f;
        var cy = h / 2f - 36f;

        DrawIllustration(g, cx, cy);

        // 主标题
        using var titleFont = new Font(Theme.FontFamily, 15F, FontStyle.Bold);
        using var hintFont = new Font(Theme.FontFamily, 10F);
        using var keyFont = new Font(Theme.FontFamily, 10F, FontStyle.Bold);

        const string title = "这里还空空的，没有提醒任务";
        const string line1 = "按快捷键 ";
        const string line2 = " 截一张图，设好提醒时间，到点它会自动弹出。";

        var titleY = cy + 92f;
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(0, (int)titleY, w, 30),
            Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);

        // 第二行由三段组成，高亮快捷键
        var fullLine = line1 + HotkeyText + line2;
        var totalWidth = TextRenderer.MeasureText(g, fullLine, hintFont, Size.Empty,
            TextFormatFlags.NoPadding).Width;
        var keyWidth = TextRenderer.MeasureText(g, HotkeyText, keyFont, Size.Empty,
            TextFormatFlags.NoPadding).Width;
        var prefixWidth = TextRenderer.MeasureText(g, line1, hintFont, Size.Empty,
            TextFormatFlags.NoPadding).Width;

        var lineY = titleY + 38f;
        var x = cx - totalWidth / 2f;
        TextRenderer.DrawText(g, line1, hintFont, new Rectangle((int)x, (int)lineY, prefixWidth + 2, 24),
            Theme.SubText, TextFormatFlags.Left | TextFormatFlags.NoPadding);
        x += prefixWidth;

        // 快捷键画成浅色胶囊底 + 主色文字
        var keyRect = new Rectangle((int)x, (int)lineY + 1, keyWidth + 14, 22);
        using (var path = RoundedRect(keyRect, 11))
        using (var brush = new SolidBrush(Theme.PreviewBg))
        {
            g.FillPath(brush, path);
        }
        TextRenderer.DrawText(g, HotkeyText, keyFont, keyRect,
            Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        x += keyWidth + 14;

        TextRenderer.DrawText(g, line2, hintFont, new Rectangle((int)x, (int)lineY, w - (int)x, 24),
            Theme.SubText, TextFormatFlags.Left | TextFormatFlags.NoPadding);

        const string tip = "也可以截图后点「存图并设提醒」，或稍后在托盘菜单里撤销。";
        TextRenderer.DrawText(g, tip, hintFont, new Rectangle(0, (int)lineY + 32, w, 24),
            Theme.MutedText, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>画一张「图片 + 铃铛 + 时钟」的抽象插画，呼应品牌图标。</summary>
    private static void DrawIllustration(Graphics g, float cx, float cy)
    {
        // 圆角相框底
        var frame = new RectangleF(cx - 62f, cy - 52f, 124f, 96f);
        using (var path = RoundedRect(frame, 14f))
        using (var brush = new LinearGradientBrush(frame, Theme.PreviewBg, Theme.Bg, LinearGradientMode.Vertical))
        using (var pen = new Pen(Theme.Border, 2f))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        // 相框里的「山 + 太阳」图片符号
        using (var sunBrush = new SolidBrush(Theme.Accent))
        {
            g.FillEllipse(sunBrush, cx + 18f, cy - 28f, 16f, 16f);
        }
        using (var mountainBrush = new SolidBrush(Color.FromArgb(180, 200, 235)))
        {
            g.FillPolygon(mountainBrush, new[]
            {
                new PointF(cx - 44f, cy + 26f),
                new PointF(cx - 12f, cy - 10f),
                new PointF(cx + 14f, cy + 26f)
            });
            g.FillPolygon(mountainBrush, new[]
            {
                new PointF(cx - 2f, cy + 26f),
                new PointF(cx + 22f, cy + 2f),
                new PointF(cx + 46f, cy + 26f)
            });
        }

        // 右下角叠一个铃铛 + 时钟圆盘，呼应品牌图标
        var badge = new RectangleF(cx + 30f, cy + 18f, 44f, 44f);
        using (var badgePath = new GraphicsPath())
        using (var badgeBrush = new SolidBrush(Theme.Accent))
        {
            badgePath.AddEllipse(badge);
            g.FillPath(badgeBrush, badgePath);
        }
        using (var white = new Pen(Color.White, 2.4f))
        {
            // 铃铛轮廓（简化钟形）
            g.DrawArc(white, badge.X + 12f, badge.Y + 11f, 20f, 20f, 180f, 180f);
            g.DrawLine(white, badge.X + 12f, badge.Y + 21f, badge.X + 12f, badge.Y + 26f);
            g.DrawLine(white, badge.X + 32f, badge.Y + 21f, badge.X + 32f, badge.Y + 26f);
            g.DrawArc(white, badge.X + 18f, badge.Y + 29f, 8f, 6f, 0f, 180f);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180f, 90f);
        path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270f, 90f);
        path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius) =>
        RoundedRect((RectangleF)rect, radius);
}
