using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace IconCraft;

/// <summary>
/// 把一张 PNG 母版做成 Windows 应用图标：
/// 1) 按 Alpha 自动裁掉四周留白（原图主体只占中间一小块，直接缩放会糊）；
/// 2) 补等量留白进正方形画布，保证 16px 时不贴边；
/// 3) 生成 16/24/32/48/64/128/256 七档 ICO；
/// 4) 另出一张对比预览（白底 + 深色底 × 各尺寸，含 3 倍放大），便于人工确认。
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        // 附加模式：从 exe 里把内嵌图标抠出来看，用于确认图标真的换上了
        if (args.Length == 3 && args[0] == "--from-exe")
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(args[1]);
                if (icon == null)
                {
                    Console.WriteLine("没有取到图标（路径对吗？）");
                    return 3;
                }

                using var bmp = icon.ToBitmap();
                // 放大到 160 便于肉眼核对
                using var big = new Bitmap(160, 160);
                using (var g = Graphics.FromImage(big))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(bmp, 0, 0, 160, 160);
                }
                big.Save(args[2], ImageFormat.Png);
                Console.WriteLine($"已提取内嵌图标 {bmp.Width}×{bmp.Height} → {args[2]}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("提取失败：" + ex.Message);
                return 4;
            }
        }

        // 附加模式：生成程序自带的默认图标（蓝底圆角 + 白色取景环 + 红点）
        // 用途：第三方图标被撤下时能一键还原，不依赖任何外部 PNG
        if (args.Length >= 2 && args[0] == "--default")
        {
            var icoPath = args[1];
            var previewPath = args.Length > 2 ? args[2] : Path.ChangeExtension(icoPath, ".preview.png");

            using var defaults = DrawDefault(1024);
            WriteIco(defaults, icoPath);
            WritePreview(defaults, previewPath, "内置默认图标");

            Console.WriteLine($"ICO  -> {icoPath} ({new FileInfo(icoPath).Length / 1024.0:F1} KB)");
            Console.WriteLine($"预览 -> {previewPath}");
            return 0;
        }

        if (args.Length < 3)
        {
            Console.WriteLine("用法：IconCraft <源PNG> <输出ICO> <预览PNG> [标签] [留白比例=0.06]");
            Console.WriteLine("      IconCraft --from-exe <exe路径> <输出PNG>");
            Console.WriteLine("      IconCraft --default <输出ICO> [预览PNG]");
            return 1;
        }

        var source = args[0];
        var icoOut = args[1];
        var previewOut = args[2];
        var label = args.Length > 3 ? args[3] : "icon";
        var padding = args.Length > 4 ? double.Parse(args[4]) : 0.06;

        if (!File.Exists(source))
        {
            Console.WriteLine("源文件不存在：" + source);
            return 2;
        }

        using var master = Normalize(source, padding);
        Console.WriteLine($"{label}：母版归一化 {master.Width}×{master.Height}");

        WriteIco(master, icoOut);
        WritePreview(master, previewOut, label);

        Console.WriteLine($"ICO  -> {icoOut} ({new FileInfo(icoOut).Length / 1024.0:F1} KB)");
        Console.WriteLine($"预览 -> {previewOut}");
        return 0;
    }

    /// <summary>程序内置图标：蓝色渐变圆角方块 + 白色取景环 + 红点（提醒）。纯代码绘制，无外部依赖。</summary>
    private static Bitmap DrawDefault(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var margin = size * 0.04f;
        var rect = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);
        var radius = size * 0.24f;
        var d = radius * 2f;

        using (var path = new GraphicsPath())
        {
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            using var brush = new LinearGradientBrush(rect,
                Color.FromArgb(38, 108, 246), Color.FromArgb(22, 66, 176), 55f);
            g.FillPath(brush, path);
        }

        var ringWidth = Math.Max(1f, size * 0.085f);
        var pad = size * 0.26f;
        using (var pen = new Pen(Color.White, ringWidth))
        {
            g.DrawEllipse(pen, pad, pad, size - 2 * pad, size - 2 * pad);
        }

        var dotR = size * 0.085f;
        using (var white = new SolidBrush(Color.White))
        {
            g.FillEllipse(white, size / 2f - dotR, size / 2f - dotR, dotR * 2, dotR * 2);
        }

        if (size >= 24)
        {
            var rr = size * 0.115f;
            var cx = size * 0.755f;
            var cy = size * 0.245f;
            using var red = new SolidBrush(Color.FromArgb(255, 77, 79));
            g.FillEllipse(red, cx - rr, cy - rr, rr * 2, rr * 2);
        }

        return bmp;
    }

    /// <summary>找非透明像素外接矩形，裁掉四周空白后居中放进正方形画布。</summary>
    private static Bitmap Normalize(string path, double paddingRatio)
    {
        using var src = new Bitmap(path);
        var box = AlphaBounds(src);
        using var cropped = src.Clone(box, PixelFormat.Format32bppArgb);

        var side = (int)Math.Ceiling(Math.Max(cropped.Width, cropped.Height) * (1.0 + paddingRatio * 2));
        var canvas = new Bitmap(side, side, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(cropped, (side - cropped.Width) / 2, (side - cropped.Height) / 2,
                cropped.Width, cropped.Height);
        }
        return canvas;
    }

    private static Rectangle AlphaBounds(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] bytes;
        int stride;
        try
        {
            stride = data.Stride;
            bytes = new byte[stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < bmp.Height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < bmp.Width; x++)
            {
                if (bytes[row + x * 4 + 3] < 24) continue;   // 几乎全透明的忽略
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < 0 ? rect : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Bitmap Resize(Bitmap src, int size)
    {
        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, size, size));
        return dst;
    }

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>按 ICO 规范打包：目录项 + PNG 压缩条目（Vista 起支持）。</summary>
    private static void WriteIco(Bitmap master, string outPath)
    {
        var pngs = new List<byte[]>();
        foreach (var s in Sizes)
        {
            using var r = Resize(master, s);
            pngs.Add(ToPng(r));
        }

        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)Sizes.Length);

        var offset = 6 + 16 * Sizes.Length;
        for (var i = 0; i < Sizes.Length; i++)
        {
            var dim = Sizes[i] >= 256 ? (byte)0 : (byte)Sizes[i];
            bw.Write(dim);
            bw.Write(dim);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write((uint)pngs[i].Length);
            bw.Write((uint)offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs) bw.Write(png);
    }

    /// <summary>对比预览：上行白底实际尺寸，下行深色底 3 倍放大。</summary>
    private static void WritePreview(Bitmap master, string outPath, string label)
    {
        int[] show = { 16, 24, 32, 48, 64, 128 };
        const int cell = 150;
        var width = 20 + show.Length * cell;
        const int height = 330;

        using var sheet = new Bitmap(width, height);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.White);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var font = new Font("Microsoft YaHei UI", 9f);
        using var small = new Font("Microsoft YaHei UI", 7.5f);
        using var darkText = new SolidBrush(Color.FromArgb(60, 60, 60));
        using var lightText = new SolidBrush(Color.FromArgb(225, 225, 225));

        g.DrawString(label, font, darkText, 12, 6);

        for (var i = 0; i < show.Length; i++)
        {
            using var icon = Resize(master, show[i]);
            g.DrawImage(icon, 20 + i * cell + (cell - show[i]) / 2, 40);
            g.DrawString(show[i] + "px", small, darkText, 20 + i * cell + 8, 40 + show[i] + 6);
        }

        using (var dark = new SolidBrush(Color.FromArgb(32, 36, 44)))
        {
            g.FillRectangle(dark, 0, 160, width, height - 160);
        }

        for (var i = 0; i < show.Length; i++)
        {
            using var icon = Resize(master, show[i]);
            using var big = new Bitmap(show[i] * 3, show[i] * 3);
            using (var gb = Graphics.FromImage(big))
            {
                gb.InterpolationMode = InterpolationMode.NearestNeighbor;
                gb.PixelOffsetMode = PixelOffsetMode.Half;
                gb.DrawImage(icon, 0, 0, show[i] * 3, show[i] * 3);
            }
            g.DrawImage(big, 20 + i * cell + (cell - show[i] * 3) / 2, 178);
            g.DrawString(show[i] + "px ×3", small, lightText, 20 + i * cell + 8, 180);
        }

        sheet.Save(outPath, ImageFormat.Png);
    }
}
