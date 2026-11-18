using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CadenceSongSync.IconGen;

public static class IconBuilder
{
    public static void GenerateAppIcon(string outputPath)
    {
        int[] sizes = [16, 24, 32, 48, 256];
        string baseDir = Path.Combine(AppContext.BaseDirectory, "assets", "images");
        if (!Directory.Exists(baseDir))
        {
            baseDir = Path.Combine(Directory.GetCurrentDirectory(), "assets", "images");
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICO Header
        bw.Write((short)0); // reserved
        bw.Write((short)1); // type 1 = ICO
        bw.Write((short)sizes.Length); // count

        var pngStreams = new MemoryStream[sizes.Length];

        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            using var bmp = LoadOrRenderFrame(size, baseDir);
            pngStreams[i] = new MemoryStream();
            bmp.Save(pngStreams[i], ImageFormat.Png);
        }

        int offset = 6 + (sizes.Length * 16);
        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            byte bSize = (byte)(size >= 256 ? 0 : size);
            bw.Write(bSize); // width
            bw.Write(bSize); // height
            bw.Write((byte)0); // color count
            bw.Write((byte)0); // reserved
            bw.Write((short)1); // color planes
            bw.Write((short)32); // bpp
            bw.Write((int)pngStreams[i].Length); // size
            bw.Write(offset); // offset

            offset += (int)pngStreams[i].Length;
        }

        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngStreams[i].ToArray());
            pngStreams[i].Dispose();
        }

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(outputPath, ms.ToArray());
    }

    private static Bitmap LoadOrRenderFrame(int size, string baseDir)
    {
        string specificPng = Path.Combine(baseDir, $"icon_{size}x{size}.png");
        string master512 = Path.Combine(baseDir, "icon_512x512.png");
        string png32 = Path.Combine(baseDir, "icon_32x32.png");
        string png64 = Path.Combine(baseDir, "icon_64x64.png");

        string? srcFile = null;
        if (File.Exists(specificPng))
        {
            srcFile = specificPng;
        }
        else if (size == 24 && File.Exists(png32))
        {
            srcFile = png32;
        }
        else if (size == 48 && File.Exists(png64))
        {
            srcFile = png64;
        }
        else if (File.Exists(master512))
        {
            srcFile = master512;
        }

        if (srcFile != null)
        {
            try
            {
                using var src = (Bitmap)Image.FromFile(srcFile);
                var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(dest);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
                return dest;
            }
            catch { }
        }

        // Procedural fallback if PNGs missing
        var fallback = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var gfx = Graphics.FromImage(fallback);
        gfx.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(
            new PointF(0, 0),
            new PointF(size, size),
            Color.FromArgb(14, 165, 233),
            Color.FromArgb(99, 102, 241)
        );
        gfx.FillEllipse(brush, 1, 1, size - 2, size - 2);
        return fallback;
    }
}
