using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace Nexus.Tests;

public class LogoGenerator
{
    [Fact]
    public void GenerateNexusLogoAssets()
    {
        string assetsDir = @"C:\Users\Mabar\OneDrive\Desktop\NEXUS\Nexus.App\Assets";
        string artifactDir = @"C:\Users\Mabar\.gemini\antigravity\brain\23334fb5-3203-4227-b97b-1392b80d400d";
        Directory.CreateDirectory(assetsDir);
        Directory.CreateDirectory(artifactDir);

        int size = 512;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        // Subtle glowing backdrop squircle
        using (var pathBg = GetRoundedRect(new RectangleF(24, 24, 464, 464), 96))
        using (var brushBg = new LinearGradientBrush(
            new PointF(24, 24), new PointF(488, 488),
            Color.FromArgb(240, 14, 18, 26), Color.FromArgb(250, 7, 9, 14)))
        using (var penBgBorder = new Pen(Color.FromArgb(60, 0, 210, 255), 2f))
        {
            g.FillPath(brushBg, pathBg);
            g.DrawPath(penBgBorder, pathBg);
        }

        // Exact Official NEXUS 3D Prism Emblem Geometry (from User SVG)
        // Left Face: Vertical Chamber (Electric Blue to Bright Cyan)
        PointF[] leftFace = new[]
        {
            new PointF(42.667f, 127.979f),
            new PointF(264.363f, 0f),
            new PointF(264.363f, 512f),
            new PointF(42.667f, 384f)
        };

        // Right / Bottom Face: Isometric Plane (Deep Blue to Neon Purple / Lilac)
        PointF[] rightFace = new[]
        {
            new PointF(264.363f, 512f),
            new PointF(42.667f, 384f),
            new PointF(264.363f, 256f),
            new PointF(486.08f, 384f)
        };

        // 1. Render Left Face: Base Dark + Vibrant Cyan/Blue Gradient
        using (var brushLeft = new LinearGradientBrush(
            new PointF(293.291f, 483.029f), new PointF(99.669f, 79.893f),
            Color.FromArgb(255, 0, 78, 255), Color.FromArgb(255, 0, 255, 255)))
        {
            g.FillPolygon(Brushes.Black, leftFace);
            g.FillPolygon(brushLeft, leftFace);
        }

        // 2. Render Right Face: Deep Blue to Purple / Lilac
        using (var brushRight = new LinearGradientBrush(
            new PointF(42.667f, 384f), new PointF(486.08f, 384f),
            Color.FromArgb(255, 0, 0, 170), Color.FromArgb(255, 167, 141, 255)))
        {
            g.FillPolygon(brushRight, rightFace);
        }

        // 3. Central Ridge / Spine and Subtle Highlights
        using (var penRidge = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f))
        {
            g.DrawLine(penRidge, 264.363f, 256f, 264.363f, 512f);
            g.DrawLine(penRidge, 42.667f, 384f, 264.363f, 256f);
            g.DrawLine(penRidge, 264.363f, 256f, 486.08f, 384f);
        }

        // Save High-Res PNGs
        string png512 = Path.Combine(assetsDir, "nexus_logo.png");
        string artifactPng = Path.Combine(artifactDir, "nexus_app_logo_preview.png");
        bmp.Save(png512, ImageFormat.Png);
        bmp.Save(artifactPng, ImageFormat.Png);

        // Generate Multi-Resolution ICO File (16, 32, 48, 64, 128, 256)
        string icoPath = Path.Combine(assetsDir, "nexus_logo.ico");
        SaveAsIco(bmp, icoPath);

        Assert.True(File.Exists(png512));
        Assert.True(File.Exists(icoPath));
        Assert.True(File.Exists(artifactPng));
    }

    private static GraphicsPath GetRoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SaveAsIco(Bitmap srcBitmap, string outputPath)
    {
        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICO Header
        bw.Write((short)0);           // Reserved, must be 0
        bw.Write((short)1);           // Image type: 1 = ICO
        bw.Write((short)sizes.Length);// Number of images

        // Prepare image data buffers
        var imageDataList = new List<byte[]>();
        foreach (int sz in sizes)
        {
            using var resized = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(resized);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(srcBitmap, 0, 0, sz, sz);

            using var ms = new MemoryStream();
            resized.Save(ms, ImageFormat.Png);
            imageDataList.Add(ms.ToArray());
        }

        // Calculate offset where first image data starts (Header: 6 bytes + 16 bytes per directory entry)
        int dataOffset = 6 + (16 * sizes.Length);

        // Write ICO Directory Entries
        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            byte[] data = imageDataList[i];

            bw.Write((byte)(sz >= 256 ? 0 : sz)); // Width (0 means 256)
            bw.Write((byte)(sz >= 256 ? 0 : sz)); // Height
            bw.Write((byte)0);                     // Color palette
            bw.Write((byte)0);                     // Reserved
            bw.Write((short)1);                    // Color planes
            bw.Write((short)32);                   // Bits per pixel
            bw.Write((int)data.Length);            // Image data size
            bw.Write((int)dataOffset);             // Image data offset

            dataOffset += data.Length;
        }

        // Write Image Data blocks
        foreach (var data in imageDataList)
        {
            bw.Write(data);
        }
    }
}
