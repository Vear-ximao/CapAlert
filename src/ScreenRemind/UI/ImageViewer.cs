using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ScreenRemind.UI;

/// <summary>
/// 原图查看控件：滚轮跟光标缩放、拖拽平移、双击切 1:1、旋转、上/下一张。
/// 只在「显示层」缩放，磁盘上的原图永远是原始像素，绝不回写。
/// </summary>
public sealed class ImageViewer : Control
{
    private Bitmap? _source;          // 原始位图（外部传入，本控件不负责释放，除非 takeOwnership）
    private Bitmap? _display;         // 旋转后的显示副本（本控件自建，需释放）
    private bool _ownsSource;
    private float _zoom = 1f;
    private PointF _offset;
    private bool _fitMode = true;
    private int _rotation;            // 0/1/2/3 → 0°/90°/180°/270°

    private bool _panning;
    private Point _panStart;
    private PointF _panOrigin;

    public const float MinZoom = 0.1f;
    public const float MaxZoom = 8f;

    public event EventHandler<string>? ZoomChanged;

    public ImageViewer()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.ViewerBg;
        TabStop = true;
    }

    public bool HasImage => _display != null;
    public float Zoom => _zoom;

    public void SetImage(Bitmap? bmp, bool takeOwnership = false)
    {
        if (_ownsSource) _source?.Dispose();
        _display?.Dispose();

        _source = bmp;
        _ownsSource = takeOwnership;
        _rotation = 0;
        RebuildDisplay();
        _fitMode = true;
        ApplyFit();
        Invalidate();
    }

    private void RebuildDisplay()
    {
        _display?.Dispose();
        _display = null;
        if (_source == null) return;

        _display = (Bitmap)_source.Clone();
        for (var i = 0; i < _rotation % 4; i++)
        {
            _display.RotateFlip(RotateFlipType.Rotate90FlipNone);
        }
    }

    public void ZoomToFit()
    {
        _fitMode = true;
        ApplyFit();
        Invalidate();
    }

    public void ZoomToActual()
    {
        _fitMode = false;
        _zoom = 1f;
        CenterImage();
        Invalidate();
        RaiseZoom();
    }

    public void Rotate()
    {
        _rotation = (_rotation + 1) % 4;
        RebuildDisplay();
        if (_fitMode) ApplyFit();
        else CenterImage();
        Invalidate();
    }

    /// <summary>上/下一张由宿主处理，控件只发请求。</summary>
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;

    private void ApplyFit()
    {
        if (_display == null) return;
        if (Width <= 0 || Height <= 0) return;

        var scale = Math.Min((float)Width / _display.Width, (float)Height / _display.Height);
        // 小图不放大，避免模糊；大图缩到能看全
        _zoom = Math.Clamp(Math.Min(scale, 1f), MinZoom, MaxZoom);
        CenterImage();
        RaiseZoom();
    }

    private void CenterImage()
    {
        if (_display == null) return;
        _offset = new PointF(
            (Width - _display.Width * _zoom) / 2f,
            (Height - _display.Height * _zoom) / 2f);
    }

    private void RaiseZoom() => ZoomChanged?.Invoke(this, $"{_zoom * 100:F0}%");

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.ViewerBg);
        if (_display == null)
        {
            using var br = new SolidBrush(Color.FromArgb(150, 156, 168));
            using var f = new Font(Theme.FontFamily, 10F);
            var text = "原图已丢失（文件可能被移动或删除）";
            var size = e.Graphics.MeasureString(text, f);
            e.Graphics.DrawString(text, f, br, (Width - size.Width) / 2, (Height - size.Height) / 2);
            return;
        }

        e.Graphics.InterpolationMode = _zoom >= 1f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = _zoom >= 1f ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
        e.Graphics.SmoothingMode = SmoothingMode.None;

        var dest = new RectangleF(_offset.X, _offset.Y, _display.Width * _zoom, _display.Height * _zoom);
        e.Graphics.DrawImage(_display, dest);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_display == null) return;

        var factor = (ModifierKeys & Keys.Control) != 0 ? 2f : 1.15f;
        var target = e.Delta > 0 ? _zoom * factor : _zoom / factor;
        target = Math.Clamp(target, MinZoom, MaxZoom);
        if (Math.Abs(target - _zoom) < 0.0001f) return;

        // 关键：让光标下的那个像素点在缩放前后停在原地
        var imageX = (e.X - _offset.X) / _zoom;
        var imageY = (e.Y - _offset.Y) / _zoom;
        _zoom = target;
        _offset = new PointF(e.X - imageX * _zoom, e.Y - imageY * _zoom);

        _fitMode = false;
        Invalidate();
        RaiseZoom();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && _display != null)
        {
            _panning = true;
            _panStart = e.Location;
            _panOrigin = _offset;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_panning) return;
        _offset = new PointF(_panOrigin.X + (e.X - _panStart.X), _panOrigin.Y + (e.Y - _panStart.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning)
        {
            _panning = false;
            Cursor = Cursors.Default;
        }
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        if (_display == null) return;
        if (Math.Abs(_zoom - 1f) < 0.01f) ZoomToFit();
        else ZoomToActual();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Add or Keys.Oemplus:
                ZoomStep(1.15f);
                return true;
            case Keys.Subtract or Keys.OemMinus:
                ZoomStep(1 / 1.15f);
                return true;
            case Keys.D0 or Keys.NumPad0:
                ZoomToFit();
                return true;
            case Keys.D1 or Keys.NumPad1:
                ZoomToActual();
                return true;
            case Keys.Left:
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.Right:
                NextRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case Keys.R:
                Rotate();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ZoomStep(float factor)
    {
        if (_display == null) return;
        var center = new Point(Width / 2, Height / 2);
        var imageX = (center.X - _offset.X) / _zoom;
        var imageY = (center.Y - _offset.Y) / _zoom;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _offset = new PointF(center.X - imageX * _zoom, center.Y - imageY * _zoom);
        _fitMode = false;
        Invalidate();
        RaiseZoom();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_display == null) return;
        if (_fitMode) ApplyFit();
        Invalidate();
    }

    /// <summary>导出当前显示内容（含旋转）为位图，供「另存为」之类使用。</summary>
    public Bitmap? SnapshotDisplay() => _display == null ? null : (Bitmap)_display.Clone();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _display?.Dispose();
            if (_ownsSource) _source?.Dispose();
        }
        base.Dispose(disposing);
    }
}
