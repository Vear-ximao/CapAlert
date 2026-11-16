using System.Drawing.Imaging;
using ScreenRemind.Core;

namespace ScreenRemind.UI;

/// <summary>
/// 全屏冻结框选：先把整块虚拟桌面抓成位图铺满全屏，再在上面拖框。
/// 因为窗口客户区尺寸严格等于底图像素尺寸，鼠标坐标就是图片像素坐标 —— 所见即所裁，不存在缩放偏移。
/// </summary>
public sealed class RegionSelectorForm : Form
{
    private readonly Bitmap _screen;
    private readonly Rectangle? _windowRect;
    private readonly Point _screenOrigin;
    private Point _origin;
    private Rectangle _rect;
    private bool _dragging;
    private bool _hasSelection;

    /// <summary>裁切结果（调用方负责释放）</summary>
    public Bitmap? Result { get; private set; }

    public CaptureSource ResultSource { get; private set; } = CaptureSource.Region;

    /// <param name="screen">整块虚拟桌面的冻结画面</param>
    /// <param name="virtualBounds">虚拟桌面在屏幕坐标中的位置</param>
    /// <param name="windowRect">调用前抓到的前台窗口矩形（屏幕坐标），用于「按 W 截当前窗口」</param>
    public RegionSelectorForm(Bitmap screen, Rectangle virtualBounds, Rectangle? windowRect = null)
    {
        _screen = screen;
        _windowRect = windowRect;
        _screenOrigin = virtualBounds.Location;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;

        Bounds = virtualBounds;
        ClientSize = screen.Size;
        Location = virtualBounds.Location;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImageUnscaled(_screen, 0, 0);

        // 整体压暗
        using (var dim = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
        {
            g.FillRectangle(dim, ClientRectangle);
        }

        if (_hasSelection && _rect.Width > 0 && _rect.Height > 0)
        {
            // 选区还原成原始亮度
            var saved = g.Clip;
            g.SetClip(_rect);
            g.DrawImageUnscaled(_screen, 0, 0);
            g.Clip = saved;

            using var pen = new Pen(Theme.Accent, 2);
            g.DrawRectangle(pen, _rect);

            // 四角把手
            using var handle = new SolidBrush(Theme.Accent);
            const int hs = 6;
            foreach (var pt in new[]
                     {
                         new Point(_rect.Left, _rect.Top),
                         new Point(_rect.Right, _rect.Top),
                         new Point(_rect.Left, _rect.Bottom),
                         new Point(_rect.Right, _rect.Bottom)
                     })
            {
                g.FillRectangle(handle, pt.X - hs / 2, pt.Y - hs / 2, hs, hs);
            }

            DrawSizeLabel(g, _rect);
        }
        else
        {
            DrawHint(g);
        }
    }

    private void DrawSizeLabel(Graphics g, Rectangle rect)
    {
        var text = $"{rect.Width} × {rect.Height}";
        using var font = new Font(Theme.FontFamily, 9F);
        var size = g.MeasureString(text, font);

        var x = rect.Right + 10;
        var y = rect.Bottom + 10;
        if (x + size.Width + 12 > ClientSize.Width) x = (int)(rect.Right - size.Width - 12);
        if (y + size.Height + 8 > ClientSize.Height) y = (int)(rect.Bottom - size.Height - 10);

        using var bg = new SolidBrush(Color.FromArgb(220, 20, 22, 28));
        g.FillRectangle(bg, x, y, size.Width + 10, size.Height + 6);
        using var fg = new SolidBrush(Color.White);
        g.DrawString(text, font, fg, x + 5, y + 3);
    }

    private void DrawHint(Graphics g)
    {
        var text = _windowRect.HasValue
            ? "拖动鼠标框选　·　回车截整屏　·　按 W 截当前窗口　·　Esc / 右键取消"
            : "拖动鼠标框选要截的区域　·　直接回车截整个屏幕　·　Esc / 右键取消";
        using var font = new Font(Theme.FontFamily, 10F);
        var size = g.MeasureString(text, font);
        var x = (ClientSize.Width - size.Width) / 2;
        const int y = 28;

        using var bg = new SolidBrush(Color.FromArgb(215, 20, 22, 28));
        g.FillRectangle(bg, x - 16, y - 10, size.Width + 32, size.Height + 20);
        using var fg = new SolidBrush(Color.White);
        g.DrawString(text, font, fg, x, y);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        _dragging = true;
        _origin = e.Location;
        _rect = new Rectangle(e.Location, Size.Empty);
        _hasSelection = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var x = Math.Min(_origin.X, e.X);
        var y = Math.Min(_origin.Y, e.Y);
        var w = Math.Abs(e.X - _origin.X);
        var h = Math.Abs(e.Y - _origin.Y);
        _rect = new Rectangle(x, y, w, h);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false;

        if (_rect.Width < 3 || _rect.Height < 3)
        {
            // 手误点一下不算选区，继续等
            _hasSelection = false;
            Invalidate();
            return;
        }
        CaptureRegion(_rect, CaptureSource.Region);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        else if (e.KeyCode == Keys.Enter)
        {
            // 便利口子：没拖框就直接回车 = 截整个屏幕
            var rect = _hasSelection && _rect.Width >= 3
                ? _rect
                : new Rectangle(0, 0, _screen.Width, _screen.Height);
            CaptureRegion(rect, _hasSelection && _rect.Width >= 3 ? CaptureSource.Region : CaptureSource.FullScreen);
        }
        else if (e.KeyCode == Keys.W && _windowRect.HasValue)
        {
            // 截当前窗口：直接用已冻结的画面裁切，不重新抓屏，保证与看到的一致
            var win = _windowRect.Value;
            win.Offset(-_screenOrigin.X, -_screenOrigin.Y);
            CaptureRegion(win, CaptureSource.Window);
        }
    }

    private void CaptureRegion(Rectangle rect, CaptureSource source)
    {
        var safe = Rectangle.Intersect(rect, new Rectangle(0, 0, _screen.Width, _screen.Height));
        if (safe.Width < 1 || safe.Height < 1)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        Result = _screen.Clone(safe, PixelFormat.Format32bppArgb);
        ResultSource = source;
        DialogResult = DialogResult.OK;
        Close();
    }
}
