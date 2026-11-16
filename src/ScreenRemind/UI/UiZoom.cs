namespace ScreenRemind.UI;

/// <summary>
/// 让窗体内容随窗口大小整体缩放（自定义缩放）。
/// 做法：按「设计坐标」拍快照，缩放时一律从快照重算，而不是在已缩放的结果上再乘 ——
/// 避免反复拉伸后位置越滚越偏。字体走缓存，不会因为拖窗口把 GDI 句柄耗光。
/// </summary>
public sealed class UiZoom
{
    private const float Step = 0.05f;
    private const float MinScale = 1f;
    private const float MaxScale = 2.6f;

    private static readonly Dictionary<string, Font> FontCache = new();

    private readonly Form _form;
    private readonly Control _content;
    private readonly List<Item> _items = new();
    private Size _baseContent;
    private float _scale = 1f;

    private sealed class Item
    {
        public Control Control = null!;
        public Rectangle Bounds;
        public float FontSize;
        public FontStyle FontStyle;
        public string FontFamily = string.Empty;
    }

    public float Scale => _scale;

    public UiZoom(Form form, Control content)
    {
        _form = form;
        _content = content;
        form.Resize += (_, _) => FitToWindow();
    }

    /// <summary>在当前（未缩放）状态下拍快照。布局变动后必须重新调用。</summary>
    public void Capture()
    {
        _items.Clear();
        _baseContent = _content.Size;
        Walk(_content);
        _scale = 1f;
    }

    private void Walk(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            // TabPage 的大小由 TabControl 管理，只递归进去处理它的子控件
            if (child is TabPage page)
            {
                Walk(page);
                continue;
            }

            // 停靠控件由布局引擎负责，不参与绝对坐标缩放
            if (child.Dock != DockStyle.None) continue;

            _items.Add(new Item
            {
                Control = child,
                Bounds = child.Bounds,
                FontSize = child.Font.SizeInPoints,
                FontStyle = child.Font.Style,
                FontFamily = child.Font.FontFamily.Name
            });

            Walk(child);
        }
    }

    /// <summary>按窗口大小自动算缩放比（取宽高比例的较小值，保证不裁切）。</summary>
    public void FitToWindow()
    {
        if (_baseContent.Width <= 0 || _baseContent.Height <= 0) return;

        // 留 8px 余量，防止量化取整后内容比客户区高出几像素导致底部被切
        var w = _form.ClientSize.Width - _content.Left * 2 - 8;
        var h = _form.ClientSize.Height - _content.Top * 2 - 8;
        if (w <= 0 || h <= 0) return;

        var raw = Math.Min((float)w / _baseContent.Width, (float)h / _baseContent.Height);
        // 量化到 5%，避免拖动窗口时每像素都重排一遍
        var quantized = (float)Math.Round(raw / Step) * Step;
        Apply(quantized);
    }

    public void Apply(float scale, bool force = false)
    {
        scale = Math.Clamp(scale, MinScale, MaxScale);
        if (!force && Math.Abs(scale - _scale) < 0.001f) return;

        foreach (var item in _items)
        {
            var control = item.Control;
            if (control.IsDisposed) continue;

            var b = item.Bounds;
            if (!control.AutoSize)
            {
                control.Size = new Size(S(b.Width), S(b.Height));
            }
            control.Location = new Point(S(b.X), S(b.Y));

            var font = GetFont(item.FontFamily, item.FontSize * scale, item.FontStyle);
            if (!ReferenceEquals(control.Font, font)) control.Font = font;
        }

        _content.Size = new Size(S(_baseContent.Width), S(_baseContent.Height));
        _scale = scale;

        int S(int value) => (int)Math.Round(value * scale);
    }

    private static Font GetFont(string family, float size, FontStyle style)
    {
        var key = $"{family}|{size:F1}|{(int)style}";
        if (FontCache.TryGetValue(key, out var cached)) return cached;

        Font font;
        try
        {
            font = new Font(family, size, style);
        }
        catch
        {
            font = new Font(Theme.FontFamily, size, style);
        }
        FontCache[key] = font;
        return font;
    }
}
