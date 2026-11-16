using System.Drawing.Drawing2D;

namespace ScreenRemind.UI;

/// <summary>
/// 截图标注：矩形（框选）、箭头、手绘圈选、文字、马赛克，支持撤销。
/// 标注以「形状列表」保存，撤销只是去掉最后一个形状，不复制位图（4K 图复制一次就是 33 MB）。
/// </summary>
public sealed class AnnotateForm : Form
{
    public enum Tool { Rect, Arrow, Freehand, Text, Mosaic }

    private abstract class Shape
    {
        public abstract void Render(Graphics g, Bitmap src, float scale);
    }

    private sealed class RectShape : Shape
    {
        public RectangleF R;
        public Color Color = Color.FromArgb(235, 45, 45);

        public override void Render(Graphics g, Bitmap src, float scale)
        {
            using var pen = new Pen(Color, 4f / Math.Max(scale, 0.01f));
            g.DrawRectangle(pen, R.X, R.Y, R.Width, R.Height);
        }
    }

    private sealed class ArrowShape : Shape
    {
        public PointF A;
        public PointF B;
        public Color Color = Color.FromArgb(235, 45, 45);

        public override void Render(Graphics g, Bitmap src, float scale)
        {
            var w = 4f / Math.Max(scale, 0.01f);
            using var pen = new Pen(Color, w) { EndCap = LineCap.Round, StartCap = LineCap.Round };
            g.DrawLine(pen, A, B);

            var angle = Math.Atan2(B.Y - A.Y, B.X - A.X);
            var headLen = 16f / Math.Max(scale, 0.01f);
            var spread = 0.45;
            var p1 = new PointF(B.X - headLen * (float)Math.Cos(angle - spread), B.Y - headLen * (float)Math.Sin(angle - spread));
            var p2 = new PointF(B.X - headLen * (float)Math.Cos(angle + spread), B.Y - headLen * (float)Math.Sin(angle + spread));
            using var brush = new SolidBrush(Color);
            g.FillPolygon(brush, new[] { B, p1, p2 });
        }
    }

    /// <summary>手绘圈选：按住鼠标随手画一圈，松手即成型。</summary>
    private sealed class FreehandShape : Shape
    {
        public readonly List<PointF> Points = new();
        public Color Color = Color.FromArgb(235, 45, 45);

        public override void Render(Graphics g, Bitmap src, float scale)
        {
            if (Points.Count < 2) return;
            using var pen = new Pen(Color, 4f / Math.Max(scale, 0.01f))
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(pen, Points.ToArray());
        }
    }

    private sealed class TextShape : Shape    {
        public PointF P;
        public string Text = string.Empty;
        public Color Color = Color.FromArgb(235, 45, 45);

        public override void Render(Graphics g, Bitmap src, float scale)
        {
            var px = 22f / Math.Max(scale, 0.01f);
            using var font = new Font(Theme.FontFamily, px, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color);
            g.DrawString(Text, font, brush, P);
        }
    }

    private sealed class MosaicShape : Shape
    {
        public RectangleF R;

        public override void Render(Graphics g, Bitmap src, float scale)
        {
            if (R.Width < 2 || R.Height < 2) return;
            var block = Math.Max(4f, 14f / Math.Max(scale, 0.01f));
            var rect = Rectangle.Round(R);
            rect.Intersect(new Rectangle(0, 0, src.Width, src.Height));

            for (var y = rect.Top; y < rect.Bottom; y += (int)block)
            {
                for (var x = rect.Left; x < rect.Right; x += (int)block)
                {
                    var w = Math.Min((int)block, rect.Right - x);
                    var h = Math.Min((int)block, rect.Bottom - y);
                    if (w <= 0 || h <= 0) continue;

                    // 取块中心像素着色 → 最近邻马赛克，够用且快
                    var cx = Math.Clamp(x + w / 2, 0, src.Width - 1);
                    var cy = Math.Clamp(y + h / 2, 0, src.Height - 1);
                    using var brush = new SolidBrush(src.GetPixel(cx, cy));
                    g.FillRectangle(brush, x, y, w, h);
                }
            }
        }
    }

    private readonly Bitmap _source;
    private readonly List<Shape> _shapes = new();
    private readonly List<Shape> _redo = new();
    private readonly AnnotateCanvas _canvas;
    private readonly Dictionary<Tool, Button> _toolButtons = new();
    private Button? _undoBtn;
    private Button? _redoBtn;
    private Button? _clearBtn;
    private Tool _tool = Tool.Rect;

    /// <summary>完成后的合成图（调用方负责释放）</summary>
    public Bitmap? Result { get; private set; }

    public bool HadAnnotation => _shapes.Count > 0;

    public AnnotateForm(Bitmap source, Tool initialTool = Tool.Rect)
    {
        _source = source;
        Theme.ApplyForm(this);
        Text = "标注截图";
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Theme.ViewerBg;

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        var maxW = (int)(area.Width * 0.86);
        var maxH = (int)(area.Height * 0.86);
        var w = Math.Min(source.Width + 24, maxW);
        var h = Math.Min(source.Height + 108, maxH);
        ClientSize = new Size(Math.Max(680, w), Math.Max(480, h));

        // 先建画布，后面的按钮回调会用到
        _canvas = new AnnotateCanvas(source, _shapes) { Dock = DockStyle.Fill };
        _canvas.TextClick += OnTextClick;
        _canvas.ShapeCommitted += (_, _) =>
        {
            // 画了新的一笔，之前撤销掉的内容就不能再重做了
            _redo.Clear();
            UpdateHistoryButtons();
        };

        var bar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Theme.Card, Padding = new Padding(12, 9, 12, 9) };

        var x = 12;
        void AddTool(Tool tool, string text)
        {
            var b = Theme.GhostButton(text);
            b.Width = 62;
            b.Height = 32;
            b.Left = x;
            b.Top = 10;
            b.Click += (_, _) => SelectTool(tool);
            bar.Controls.Add(b);
            _toolButtons[tool] = b;
            x += 66;
        }

        AddTool(Tool.Rect, "框选");
        AddTool(Tool.Arrow, "箭头");
        AddTool(Tool.Freehand, "圈选");
        AddTool(Tool.Text, "文字");
        AddTool(Tool.Mosaic, "马赛克");

        x += 12;
        _undoBtn = Theme.GhostButton("撤销");
        _undoBtn.Width = 62;
        _undoBtn.Height = 32;
        _undoBtn.Left = x;
        _undoBtn.Top = 10;
        _undoBtn.Click += (_, _) => Undo();
        bar.Controls.Add(_undoBtn);
        x += 66;

        _redoBtn = Theme.GhostButton("重做");
        _redoBtn.Width = 62;
        _redoBtn.Height = 32;
        _redoBtn.Left = x;
        _redoBtn.Top = 10;
        _redoBtn.Click += (_, _) => Redo();
        bar.Controls.Add(_redoBtn);
        x += 66;

        var clear = Theme.GhostButton("清空");
        clear.Width = 62;
        clear.Height = 32;
        clear.Left = x;
        clear.Top = 10;
        clear.Click += (_, _) => ClearAll();
        bar.Controls.Add(clear);
        _clearBtn = clear;

        UpdateHistoryButtons();

        var tip = new Label
        {
            Text = "拖拽绘制 · 圈选按住随手画 · 文字点一下再输入　|　Ctrl+Z 撤销、Ctrl+Y 重做、Esc 退出",
            AutoSize = false,
            Dock = DockStyle.Bottom,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            ForeColor = Theme.SubText,
            BackColor = Theme.Card
        };

        var cancel = Theme.GhostButton("取消");
        cancel.Width = 76;
        cancel.SetBounds(0, 10, 76, 32);
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        bar.Controls.Add(cancel);
        Theme.PinRight(cancel, 24);

        var done = Theme.PrimaryButton("完成");
        done.Width = 86;
        done.SetBounds(0, 10, 86, 32);
        done.Click += (_, _) => Compose();
        bar.Controls.Add(done);
        Theme.PinRight(done, 110);

        Controls.Add(_canvas);
        Controls.Add(tip);
        Controls.Add(bar);

        SelectTool(initialTool);
    }

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        _canvas.CurrentTool = tool;
        _canvas.Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;

        foreach (var (t, b) in _toolButtons)
        {
            var active = t == tool;
            b.BackColor = active ? Theme.Accent : Theme.Card;
            b.ForeColor = active ? Color.White : Theme.Text;
            b.FlatAppearance.BorderColor = active ? Theme.Accent : Theme.Border;
        }
    }

    private void OnTextClick(object? sender, Point imagePoint)
    {
        var text = InputDialog.Prompt(this, "添加文字", "输入要标注的文字：", string.Empty);
        if (string.IsNullOrWhiteSpace(text)) return;

        _shapes.Add(new TextShape { P = imagePoint, Text = text });
        _redo.Clear();
        _canvas.Invalidate();
        UpdateHistoryButtons();
    }

    private void Compose()
    {
        var output = (Bitmap)_source.Clone();
        using (var g = Graphics.FromImage(output))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            foreach (var shape in _shapes)
            {
                shape.Render(g, _source, 1f);
            }
        }
        Result = output;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 快捷键走 ProcessCmdKey 而不是 OnKeyDown：
    /// OnKeyDown 要依赖焦点落在哪个控件上（点在按钮上就可能收不到），
    /// ProcessCmdKey 是命令键的统一入口，只要窗体是活动窗口就一定先到这里。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (HandleShortcut(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>快捷键的具体处理，抽出来便于自检直接调用（不依赖真实键盘消息）。</summary>
    private bool HandleShortcut(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.Z:
            case Keys.Control | Keys.U:
                Undo();
                return true;

            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z:
                Redo();
                return true;

            case Keys.Control | Keys.Delete:
                ClearAll();
                return true;

            case Keys.Escape:
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
        }

        return false;
    }

    // ---- 仅用于自动化自检：验证撤销/重做确实生效，不参与正式流程 ----
    internal int DebugShapeCount => _shapes.Count;
    internal int DebugRedoCount => _redo.Count;
    internal void DebugAddRect() => _shapes.Add(new RectShape { R = new RectangleF(10, 10, 80, 60) });
    internal void DebugShortcut(Keys keys) => HandleShortcut(keys);

    private void Undo()
    {
        if (_shapes.Count == 0) return;
        _redo.Add(_shapes[^1]);
        _shapes.RemoveAt(_shapes.Count - 1);
        _canvas.Invalidate();
        UpdateHistoryButtons();
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _shapes.Add(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        _canvas.Invalidate();
        UpdateHistoryButtons();
    }

    private void ClearAll()
    {
        if (_shapes.Count == 0 && _redo.Count == 0) return;
        _redo.InsertRange(0, _shapes);
        _shapes.Clear();
        _canvas.Invalidate();
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        if (_undoBtn is not null) _undoBtn.Enabled = _shapes.Count > 0;
        if (_redoBtn is not null) _redoBtn.Enabled = _redo.Count > 0;
        if (_clearBtn is not null) _clearBtn.Enabled = _shapes.Count > 0 || _redo.Count > 0;
    }

    /// <summary>画布：等比适配显示，鼠标坐标换算成图片像素坐标后交给形状保存。</summary>
    private sealed class AnnotateCanvas : Control
    {
        private readonly Bitmap _base;
        private readonly List<Shape> _shapes;
        private Shape? _draft;
        private PointF _start;
        private bool _dragging;
        private float _scale = 1f;
        private PointF _offset;

        public Tool CurrentTool { get; set; } = Tool.Rect;

        public event EventHandler<Point>? TextClick;

        /// <summary>一笔画完并入库（用于清空重做栈、刷新撤销按钮）。</summary>
        public event EventHandler? ShapeCommitted;

        public AnnotateCanvas(Bitmap baseImage, List<Shape> shapes)
        {
            _base = baseImage;
            _shapes = shapes;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.ViewerBg;
            Cursor = Cursors.Cross;
        }

        private void ComputeFit()
        {
            var s = Math.Min((float)Width / _base.Width, (float)Height / _base.Height);
            _scale = Math.Clamp(Math.Min(s, 1f), 0.05f, 1f);
            _offset = new PointF((Width - _base.Width * _scale) / 2f, (Height - _base.Height * _scale) / 2f);
        }

        private PointF ToImage(Point p) => new((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);

        protected override void OnPaint(PaintEventArgs e)
        {
            ComputeFit();
            var g = e.Graphics;
            g.Clear(Theme.ViewerBg);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_base, new RectangleF(_offset.X, _offset.Y, _base.Width * _scale, _base.Height * _scale));

            var state = g.Save();
            g.TranslateTransform(_offset.X, _offset.Y);
            g.ScaleTransform(_scale, _scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var shape in _shapes) shape.Render(g, _base, _scale);
            _draft?.Render(g, _base, _scale);
            g.Restore(state);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // 让键盘焦点落到画布上，Ctrl+Z 这类快捷键才不会被按钮挡住
            Focus();
            if (e.Button != MouseButtons.Left) return;

            var p = ToImage(e.Location);
            if (CurrentTool == Tool.Text)
            {
                TextClick?.Invoke(this, Point.Round(p));
                return;
            }

            _dragging = true;
            _start = p;
            _draft = CurrentTool switch
            {
                Tool.Rect => new RectShape { R = new RectangleF(p.X, p.Y, 0, 0) },
                Tool.Arrow => new ArrowShape { A = p, B = p },
                Tool.Freehand => CreateFreehand(p),
                Tool.Mosaic => new MosaicShape { R = new RectangleF(p.X, p.Y, 0, 0) },
                _ => null
            };
        }

        private static FreehandShape CreateFreehand(PointF start)
        {
            var shape = new FreehandShape();
            shape.Points.Add(start);
            return shape;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _draft == null) return;

            var p = ToImage(e.Location);
            var rect = new RectangleF(
                Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y),
                Math.Abs(p.X - _start.X), Math.Abs(p.Y - _start.Y));

            switch (_draft)
            {
                case RectShape r: r.R = rect; break;
                case MosaicShape m: m.R = rect; break;
                case ArrowShape a: a.B = p; break;
                case FreehandShape f:
                    // 采样间隔太小会攒出上万个点，隔一点距离再记
                    if (f.Points.Count == 0 ||
                        Math.Abs(f.Points[^1].X - p.X) + Math.Abs(f.Points[^1].Y - p.Y) > 1.5f)
                    {
                        f.Points.Add(p);
                    }
                    break;
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging || _draft == null) return;
            _dragging = false;

            var valid = _draft switch
            {
                RectShape r => r.R.Width > 3 && r.R.Height > 3,
                MosaicShape m => m.R.Width > 3 && m.R.Height > 3,
                ArrowShape a => Math.Abs(a.B.X - a.A.X) + Math.Abs(a.B.Y - a.A.Y) > 6,
                FreehandShape f => f.Points.Count > 2,
                _ => false
            };

            if (valid)
            {
                _shapes.Add(_draft);
                ShapeCommitted?.Invoke(this, EventArgs.Empty);
            }
            _draft = null;
            Invalidate();
        }
    }
}
