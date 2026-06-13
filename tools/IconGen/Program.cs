// IconGen — throwaway tool to produce Assets/app.ico from the PMR/CM wordmark.
// Renders the DrawingGroup at 16, 32, 48, 64, 128, 256 px (STA/WPF RenderTargetBitmap)
// and assembles the results into a multi-resolution PNG-compressed .ico file.
// NOT part of the shipped solution — run once, then leave or delete.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// WPF rendering requires an STA thread.
var sta = new Thread(() =>
{
    try
    {
        var drawing = BuildDrawing();
        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        var entries = new List<(int size, byte[] png)>();

        foreach (int sz in sizes)
        {
            entries.Add((sz, RenderSize(drawing, sz)));
            Console.WriteLine($"  Rendered {sz}x{sz}");
        }

        string outPath = Path.GetFullPath(
            Path.Combine(Directory.GetCurrentDirectory(),
                @"..\..\src\EWSR_PMR_ModApp.UI\Assets\app.ico"));
        WriteIco(entries, outPath);

        var fi = new FileInfo(outPath);
        Console.WriteLine($"OK  app.ico → {outPath}  ({fi.Length:N0} bytes)");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAILED: {ex}");
        Environment.Exit(1);
    }
});
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
sta.Join();

// ─────────────────────────────────────────────────────────────
// Build the full DrawingGroup matching the SVG layered structure.
// Each SVG <g transform="translate(tx,ty) scale(1,-1)"> becomes a
// DrawingGroup with MatrixTransform(1,0,0,-1,tx,ty).
// ─────────────────────────────────────────────────────────────
static DrawingGroup BuildDrawing()
{
    const string pmrFigures =
        "M53.83,240.59 L156.80,240.59 Q194.65,240.59 216.00,223.27 " +
        "Q237.34,205.94 237.34,175.16 Q237.34,156.29 230.23,139.27 " +
        "Q223.16,122.31 209.96,109.57 Q195.63,95.85 175.78,89.67 " +
        "Q155.98,83.48 126.02,83.48 L85.08,83.48 L68.99,0.00 L6.91,0.00 " +
        "L53.83,240.59 Z " +
        "M107.30,195.63 L94.10,128.44 L128.44,128.44 Q150.36,128.44 161.96,138.75 " +
        "Q173.56,149.07 173.56,168.71 Q173.56,181.76 165.26,188.67 " +
        "Q156.96,195.63 141.33,195.63 L107.30,195.63 Z " +
        "M295.69,240.59 L371.90,240.59 L402.84,112.82 L484.05,240.59 " +
        "L563.30,240.59 L516.43,0.00 L457.60,0.00 L491.94,177.58 " +
        "L408.46,46.25 L375.10,46.25 L341.94,177.58 L307.60,0.00 " +
        "L248.77,0.00 L295.69,240.59 Z " +
        "M691.42,133.91 Q712.51,133.91 723.39,143.70 Q734.27,153.55 734.27,172.58 " +
        "Q734.27,184.49 726.79,190.06 Q719.32,195.63 703.18,195.63 " +
        "L677.55,195.63 L665.33,133.91 L691.42,133.91 Z " +
        "M657.08,91.06 L639.24,0.00 L577.16,0.00 L624.08,240.59 " +
        "L715.76,240.59 Q755.41,240.59 775.83,225.74 Q796.30,210.94 796.30,182.07 " +
        "Q796.30,152.93 780.16,134.06 Q764.07,115.19 736.54,112.46 " +
        "Q749.12,109.88 757.47,99.62 Q765.88,89.41 772.17,68.32 " +
        "L792.64,0.00 L730.92,0.00 L712.87,59.76 Q707.35,77.65 699.52,84.36 " +
        "Q691.73,91.06 676.93,91.06 L657.08,91.06 Z";

    const string cmFigures =
        "M199.96,11.29 Q177.74,3.40 157.27,-0.62 Q136.80,-4.69 118.44,-4.69 " +
        "Q69.92,-4.69 40.89,21.91 Q11.91,48.52 11.91,92.35 " +
        "Q11.91,121.17 21.09,146.70 Q30.32,172.27 48.16,193.36 " +
        "Q69.30,218.16 99.00,231.52 Q128.75,244.92 163.09,244.92 " +
        "Q181.76,244.92 200.58,240.49 Q219.45,236.05 238.94,227.03 " +
        "L228.99,177.27 Q213.52,189.03 197.85,194.49 Q182.22,199.96 164.38,199.96 " +
        "Q127.15,199.96 101.99,171.29 Q76.88,142.62 76.88,99.57 " +
        "Q76.88,71.88 92.35,56.05 Q107.82,40.27 135.04,40.27 " +
        "Q151.28,40.27 169.59,46.15 Q187.89,52.03 210.58,64.61 L199.96,11.29 Z " +
        "M296.01,240.59 L372.22,240.59 L403.16,112.82 L484.37,240.59 " +
        "L563.62,240.59 L516.75,0.00 L457.92,0.00 L492.26,177.58 " +
        "L408.78,46.25 L375.42,46.25 L342.27,177.58 L307.92,0.00 " +
        "L249.09,0.00 L296.01,240.59 Z";

    var gold  = new SolidColorBrush(Color.FromRgb(0xB9, 0x9A, 0x5D));
    var white = new SolidColorBrush(Colors.White);
    var black = new SolidColorBrush(Color.FromRgb(0x05, 0x05, 0x05));

    gold.Freeze(); white.Freeze(); black.Freeze();

    var root = new DrawingGroup();
    root.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 1200, 800));

    // Helper: creates a group with SVG translate(tx,ty) scale(1,-1)
    DrawingGroup Group(double tx, double ty, double opacity = 1.0)
    {
        var g = new DrawingGroup();
        g.Transform = new MatrixTransform(1, 0, 0, -1, tx, ty);
        if (opacity < 1.0) g.Opacity = opacity;
        return g;
    }

    // Helper: stroke-only GeometryDrawing
    GeometryDrawing Stroke(Geometry geom, Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return new GeometryDrawing(null, pen, geom);
    }

    // Helper: fill + stroke GeometryDrawing
    GeometryDrawing Fill(Geometry geom, Brush fillBrush, Brush strokeBrush, double thickness)
    {
        var pen = new Pen(strokeBrush, thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return new GeometryDrawing(fillBrush, pen, geom);
    }

    // Parse geometry (nonzero fill rule to match SVG default)
    PathGeometry Geom(string figures)
    {
        var pg = PathGeometry.CreateFromGeometry(Geometry.Parse(figures));
        pg.FillRule = FillRule.Nonzero;
        return pg;
    }

    // ── PMR row ──────────────────────────────────────────────
    // Layer 1: gold outer glow
    var pmrGlow = Group(216.40, 350.00);
    pmrGlow.Children.Add(Stroke(Geom(pmrFigures), gold, 36));
    root.Children.Add(pmrGlow);

    // Layer 2: white outline + black fill (same transform)
    var pmrBody = Group(198.40, 330.00);
    pmrBody.Children.Add(Stroke(Geom(pmrFigures), white, 44));
    pmrBody.Children.Add(Fill(Geom(pmrFigures), black, black, 4));
    root.Children.Add(pmrBody);

    // Layer 3: gold hairline at 0.95 opacity
    var pmrHair = Group(205.40, 338.00, 0.95);
    pmrHair.Children.Add(Stroke(Geom(pmrFigures), gold, 8));
    root.Children.Add(pmrHair);

    // ── CM row ───────────────────────────────────────────────
    // Layer 1: gold outer glow
    var cmGlow = Group(330.23, 725.00);
    cmGlow.Children.Add(Stroke(Geom(cmFigures), gold, 36));
    root.Children.Add(cmGlow);

    // Layer 2: white outline + black fill
    var cmBody = Group(312.23, 705.00);
    cmBody.Children.Add(Stroke(Geom(cmFigures), white, 44));
    cmBody.Children.Add(Fill(Geom(cmFigures), black, black, 4));
    root.Children.Add(cmBody);

    // Layer 3: gold hairline at 0.95 opacity
    var cmHair = Group(319.23, 713.00, 0.95);
    cmHair.Children.Add(Stroke(Geom(cmFigures), gold, 8));
    root.Children.Add(cmHair);

    return root;
}

// ─────────────────────────────────────────────────────────────
// Render drawing to a square PNG byte array.
// Scales the 1200×800 wordmark to fit (letters-only, transparent bg).
// ─────────────────────────────────────────────────────────────
static byte[] RenderSize(DrawingGroup drawing, int size)
{
    // Scale 1200×800 to fit inside size×size, keeping aspect ratio (3:2 is wider).
    double scale = size / 1200.0;
    double scaledH = 800.0 * scale;          // = 2/3 * size
    double offsetY = (size - scaledH) / 2.0; // centre vertically (offsetX = 0)

    var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
    var dv  = new DrawingVisual();
    using (var dc = dv.RenderOpen())
    {
        dc.PushTransform(new TranslateTransform(0, offsetY));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawDrawing(drawing);
        dc.Pop();
        dc.Pop();
    }
    rtb.Render(dv);

    using var ms = new MemoryStream();
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(rtb));
    enc.Save(ms);
    return ms.ToArray();
}

// ─────────────────────────────────────────────────────────────
// Write a multi-resolution PNG-compressed .ico file.
// Format: ICONDIR (6 bytes) + N×ICONDIRENTRY (16 bytes each) + PNG data.
// PNG compression is valid for all sizes on Windows Vista+; the 256 px
// entry MUST be PNG-compressed per the Windows shell requirement.
// ─────────────────────────────────────────────────────────────
static void WriteIco(List<(int size, byte[] png)> entries, string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);

    int count      = entries.Count;
    int headerSize = 6 + count * 16;

    // ICONDIR
    bw.Write((ushort)0);     // Reserved
    bw.Write((ushort)1);     // Type: 1 = ICO
    bw.Write((ushort)count);

    // ICONDIRENTRY array — compute image offsets first
    int offset = headerSize;
    for (int i = 0; i < count; i++)
    {
        int sz  = entries[i].size;
        byte wh = sz >= 256 ? (byte)0 : (byte)sz; // 0 means 256 in ICO spec
        bw.Write(wh);                             // Width
        bw.Write(wh);                             // Height
        bw.Write((byte)0);                        // ColorCount (0 = >8-bit)
        bw.Write((byte)0);                        // Reserved
        bw.Write((ushort)1);                      // Planes
        bw.Write((ushort)32);                     // BitCount
        bw.Write((uint)entries[i].png.Length);    // BytesInRes
        bw.Write((uint)offset);                   // ImageOffset from file start
        offset += entries[i].png.Length;
    }

    // PNG payloads
    foreach (var (_, png) in entries)
        bw.Write(png);
}
