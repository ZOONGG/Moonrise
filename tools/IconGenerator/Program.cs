using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: IconGenerator <branding-output-directory>");
    return 2;
}

var outputDirectory = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputDirectory);

var applicationSizes = new[] { 16, 24, 32, 48, 64, 128, 256, 512 };
var traySizes = new[] { 16, 20, 24, 32 };

foreach (var size in applicationSizes)
{
    SavePng(
        Render(size, DrawApplicationIcon),
        Path.Combine(outputDirectory, $"moonrise-icon-{size}.png"));
}

foreach (var size in traySizes)
{
    SavePng(
        Render(size, DrawTrayIcon),
        Path.Combine(outputDirectory, $"moonrise-tray-{size}.png"));
}

WriteIcon(
    Path.Combine(outputDirectory, "moonrise-icon.ico"),
    new[] { 16, 24, 32, 48, 64, 128, 256 },
    DrawApplicationIcon);
WriteIcon(
    Path.Combine(outputDirectory, "moonrise-tray.ico"),
    traySizes,
    DrawTrayIcon);

Console.WriteLine($"Generated Moonrise raster and Windows icon assets in {outputDirectory}");
return 0;

static Bitmap Render(int size, Action<Graphics> draw)
{
    const int masterSize = 512;
    using var master = new Bitmap(masterSize, masterSize, PixelFormat.Format32bppArgb);
    using (var graphics = Graphics.FromImage(master))
    {
        Configure(graphics);
        graphics.Clear(Color.Transparent);
        draw(graphics);
    }

    var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var target = Graphics.FromImage(result);
    Configure(target);
    target.Clear(Color.Transparent);
    target.DrawImage(master, new Rectangle(0, 0, size, size));
    return result;
}

static void Configure(Graphics graphics)
{
    graphics.SmoothingMode = SmoothingMode.AntiAlias;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.CompositingMode = CompositingMode.SourceOver;
    graphics.CompositingQuality = CompositingQuality.HighQuality;
}

static void DrawApplicationIcon(Graphics graphics)
{
    using var background = new LinearGradientBrush(
        new PointF(75, 55),
        new PointF(430, 460),
        Color.FromArgb(255, 18, 20, 58),
        Color.FromArgb(255, 61, 24, 104));
    graphics.FillEllipse(background, 8, 8, 496, 496);

    using var rim = new Pen(Color.FromArgb(255, 116, 78, 196), 10);
    graphics.DrawEllipse(rim, 13, 13, 486, 486);

    using var moonOuter = new GraphicsPath();
    moonOuter.AddEllipse(120, 62, 286, 310);
    using var moonInner = new GraphicsPath();
    moonInner.AddEllipse(226, 34, 240, 250);
    using var crescent = new Region(moonOuter);
    crescent.Exclude(moonInner);
    using var moonFill = new LinearGradientBrush(
        new PointF(150, 88),
        new PointF(370, 352),
        Color.FromArgb(255, 231, 219, 255),
        Color.FromArgb(255, 164, 99, 255));
    graphics.FillRegion(moonFill, crescent);

    var horizonState = graphics.Save();
    using var discClip = new GraphicsPath();
    discClip.AddEllipse(8, 8, 496, 496);
    graphics.SetClip(discClip, CombineMode.Intersect);
    using var horizon = new GraphicsPath();
    horizon.AddBezier(61, 374, 148, 298, 363, 298, 451, 374);
    horizon.AddLine(new PointF(451, 374), new PointF(451, 451));
    horizon.AddLine(new PointF(451, 451), new PointF(61, 451));
    horizon.CloseFigure();
    using var horizonFill = new LinearGradientBrush(
        new PointF(90, 330),
        new PointF(420, 444),
        Color.FromArgb(245, 20, 13, 52),
        Color.FromArgb(250, 38, 18, 78));
    graphics.FillPath(horizonFill, horizon);

    using var horizonGlow = new Pen(Color.FromArgb(65, 81, 226, 230), 15)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };
    using var horizonLine = new Pen(
        new LinearGradientBrush(
            new PointF(68f, 350f),
            new PointF(449f, 350f),
            Color.FromArgb(255, 139, 91, 255),
            Color.FromArgb(255, 86, 221, 225)),
        8)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };
    using var horizonCurve = new GraphicsPath();
    horizonCurve.AddBezier(65, 371, 153, 299, 359, 299, 447, 371);
    graphics.DrawPath(horizonGlow, horizonCurve);
    graphics.DrawPath(horizonLine, horizonCurve);
    graphics.Restore(horizonState);
}

static void DrawTrayIcon(Graphics graphics)
{
    using var background = new SolidBrush(Color.FromArgb(255, 9, 11, 32));
    graphics.FillEllipse(background, 30, 30, 452, 452);
    using var rim = new Pen(Color.FromArgb(255, 118, 87, 235), 14);
    graphics.DrawEllipse(rim, 37, 37, 438, 438);

    using var moonOuter = new GraphicsPath();
    moonOuter.AddEllipse(116, 76, 282, 298);
    using var moonInner = new GraphicsPath();
    moonInner.AddEllipse(228, 48, 236, 246);
    using var crescent = new Region(moonOuter);
    crescent.Exclude(moonInner);
    using var moon = new SolidBrush(Color.FromArgb(255, 238, 232, 255));
    graphics.FillRegion(moon, crescent);

    using var horizon = new Pen(Color.FromArgb(255, 75, 224, 226), 18)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round
    };
    using var curve = new GraphicsPath();
    curve.AddBezier(72, 390, 156, 318, 356, 318, 440, 390);
    graphics.DrawPath(horizon, curve);
}

static void SavePng(Bitmap bitmap, string path)
{
    using (bitmap)
    {
        bitmap.Save(path, ImageFormat.Png);
    }
}

static void WriteIcon(string path, IReadOnlyList<int> sizes, Action<Graphics> draw)
{
    var images = sizes.Select(size =>
    {
        using var bitmap = Render(size, draw);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }).ToArray();

    using var output = File.Create(path);
    using var writer = new BinaryWriter(output);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Length);

    var offset = 6 + (images.Length * 16);
    for (var index = 0; index < images.Length; index++)
    {
        var size = sizes[index];
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)(size == 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(images[index].Length);
        writer.Write(offset);
        offset += images[index].Length;
    }

    foreach (var image in images)
    {
        writer.Write(image);
    }
}
