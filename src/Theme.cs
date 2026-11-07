using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Editrainer;

static class Theme
{
    const uint RenderFullContent = 2, RedrawEverything = 0x0001 | 0x0004 | 0x0080 | 0x0100 | 0x0400; // Invalidate, erase, children, now, frame.
    const int SetRedrawMessage = 0x000B, ImmersiveDarkMode = 20;
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr target, uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr window, IntPtr area, IntPtr region, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>Nothing reaches the screen until the whole change is done, so it is seen as a single step.</summary>
    public static void WithoutRedraw(Control window, Action change)
    {
        SendMessage(window.Handle, SetRedrawMessage, 0, 0);
        try { change(); }
        finally
        {
            SendMessage(window.Handle, SetRedrawMessage, 1, 0);
            RedrawWindow(window.Handle, 0, 0, RedrawEverything);
        }
    }

    // Windows Forms only dresses the title bar when a window is created.
    public static void DressTitleBar(Form form)
    {
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, ImmersiveDarkMode, ref dark, sizeof(int));
    }

    // DrawToBitmap repaints native controls by hand and gets the dark ones wrong: this is what Windows shows.
    public static Bitmap Capture(Form form)
    {
        var bitmap = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var target = graphics.GetHdc();
        PrintWindow(form.Handle, target, RenderFullContent);
        graphics.ReleaseHdc(target);
        return bitmap;
    }

    public static bool IsDark { get; private set; }
    public static Color Background => IsDark ? Color.FromArgb(21, 25, 30) : Color.FromArgb(242, 245, 248);
    public static Color Surface => IsDark ? Color.FromArgb(32, 37, 44) : Color.White;
    public static Color Ink => IsDark ? Color.FromArgb(232, 237, 242) : Color.FromArgb(27, 40, 56);
    public static Color Muted => IsDark ? Color.FromArgb(160, 172, 185) : Color.FromArgb(102, 117, 133);
    public static Color Accent => IsDark ? Color.FromArgb(52, 196, 170) : Color.FromArgb(10, 126, 110);
    public static Color OnAccent => IsDark ? Color.FromArgb(10, 32, 28) : Color.White;
    public static Color AccentLight => IsDark ? Color.FromArgb(27, 66, 59) : Color.FromArgb(226, 245, 238);
    public static Color Line => IsDark ? Color.FromArgb(62, 70, 80) : Color.FromArgb(220, 228, 235);
    public static Color Danger => IsDark ? Color.FromArgb(244, 124, 130) : Color.FromArgb(185, 52, 58);
    public static Color MarkingBlue => IsDark ? Color.FromArgb(110, 176, 255) : Color.FromArgb(32, 104, 200);
    public static Color MarkingPink => IsDark ? Color.FromArgb(255, 140, 190) : Color.FromArgb(205, 50, 120);

    public static void Use(bool dark)
    {
        IsDark = dark;
#pragma warning disable WFO5001 // Windows Forms still marks its dark mode as experimental, and the app is built for this one version.
        Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);
#pragma warning restore WFO5001
    }

    public static Font Font(float size = 10, bool bold = false) => new(Strings.Current.Font, size, bold ? FontStyle.Bold : FontStyle.Regular);
    public static Label Label(string text = "", float size = 10, bool bold = false, Color? color = null) => new() {
        Text = text, Font = Font(size, bold), ForeColor = color ?? Ink, AutoSize = false,
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3),
    };
    public static Button Button(string text, bool primary = false) => new AppButton {
        Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(90, 34), Padding = new Padding(12, 4, 12, 4),
        Font = Font(9, true), ForeColor = primary ? OnAccent : Ink,
        BackColor = primary ? Accent : Surface, FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = primary ? 0 : 1, BorderColor = Line },
        Cursor = Cursors.Hand, Margin = new Padding(4),
    };
    public static ComboBox Combo() => Dress(new ComboBox {
        Font = Font(10), DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
        FlatStyle = FlatStyle.Standard, Margin = new Padding(4, 6, 4, 4), IntegralHeight = false,
        DropDownHeight = 300,
    });

    // The one control the system's dark mode leaves pale.
    public static ComboBox Dress(ComboBox box)
    {
        if (IsDark) { box.FlatStyle = FlatStyle.Flat; box.BackColor = Surface; box.ForeColor = Ink; }
        return box;
    }
}

// The system's grey for a disabled button is close to invisible on the dark theme.
sealed class AppButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Enabled || !Theme.IsDark) return;
        using var fill = new SolidBrush(BackColor == Theme.Accent ? Theme.Line : BackColor);
        using var line = new Pen(Theme.Line);
        e.Graphics.FillRectangle(fill, ClientRectangle);
        e.Graphics.DrawRectangle(line, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}

// Drawn rather than typed: a font's gear depends on what the machine has installed and blurs at this size.
sealed class GearButton : Button
{
    const int Teeth = 8;
    const float Corner = 11, TipRadius = .30f, RootRadius = .225f, HubRadius = .095f, StrokeWidth = .042f;
    bool hovered, pressed;

    public GearButton()
    {
        FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool focusRing = Focused && ShowFocusCues;
        var (plate, edge, mark) = pressed ? (Theme.Accent, Theme.Accent, Theme.OnAccent)
            : hovered ? (Theme.AccentLight, Theme.Accent, Theme.Accent)
            : (Theme.Surface, focusRing ? Theme.Accent : Theme.Line, Theme.Ink);
        var graphics = e.Graphics;
        graphics.Clear(Parent!.BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = RoundedPlate();
        using var fill = new SolidBrush(plate);
        using var border = new Pen(edge, focusRing ? 2 : 1);
        graphics.FillPath(fill, shape); graphics.DrawPath(border, shape);
        float size = Math.Min(Width, Height), hub = size * HubRadius;
        var center = new PointF(Width / 2f, Height / 2f);
        using var stroke = new Pen(mark, size * StrokeWidth) { LineJoin = LineJoin.Round };
        graphics.DrawPolygon(stroke, GearOutline(center, size * TipRadius, size * RootRadius));
        graphics.DrawEllipse(stroke, center.X - hub, center.Y - hub, hub * 2, hub * 2);
    }

    GraphicsPath RoundedPlate()
    {
        float diameter = Corner * 2, right = Width - 1, bottom = Height - 1;
        var path = new GraphicsPath();
        path.AddArc(.5f, .5f, diameter, diameter, 180, 90);
        path.AddArc(right - diameter - .5f, .5f, diameter, diameter, 270, 90);
        path.AddArc(right - diameter - .5f, bottom - diameter - .5f, diameter, diameter, 0, 90);
        path.AddArc(.5f, bottom - diameter - .5f, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Each tooth starts wide at the root and narrows towards the tip.
    static PointF[] GearOutline(PointF center, float tip, float root)
    {
        double step = 2 * Math.PI / Teeth, wide = step * .30, narrow = step * .17;
        PointF At(float radius, double angle) => new(center.X + radius * (float)Math.Cos(angle), center.Y + radius * (float)Math.Sin(angle));
        return Enumerable.Range(0, Teeth).Select(i => i * step - Math.PI / 2).SelectMany(a => new[] {
            At(root, a - wide), At(tip, a - narrow), At(tip, a + narrow), At(root, a + wide),
        }).ToArray();
    }
}

sealed class SpriteStore(string root) : IDisposable
{
    readonly Dictionary<(ushort, string, bool, bool), Image?> cache = [];
    readonly Dictionary<Bitmap, Rectangle> filledAreas = [];

    /// <summary>The closest picture to what was asked: a form's own image wins over the base species' one.</summary>
    public Image? Get(ushort id, string variant = "", bool artwork = false, bool shiny = false)
    {
        var key = (id, variant, artwork, shiny);
        if (cache.TryGetValue(key, out var existing)) return existing;
        var result = (variant.Length == 0 ? null : Find($"{id}-{variant}.png", artwork, shiny)) ?? Find(id + ".png", artwork, shiny) ?? Placeholder();
        cache[key] = result;
        return result;
    }

    /// <summary>The part of a picture that isn't transparent.</summary>
    public Rectangle FilledArea(Bitmap image)
    {
        if (filledAreas.TryGetValue(image, out var known)) return known;
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).A == 0) continue;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        var area = right < 0 ? new Rectangle(Point.Empty, image.Size) : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        filledAreas[image] = area;
        return area;
    }

    Image? Find(string file, bool artwork, bool shiny)
    {
        var found = Load(file, artwork, shiny);
        if (found == null && shiny) found = Load(file, artwork, false);
        if (found == null && artwork) found = Load(file, false, shiny);
        if (found == null && artwork && shiny) found = Load(file, false, false);
        return found;
    }

    Image? Load(string file, bool artwork, bool shiny)
    {
        var path = Path.Combine(root, "Assets", artwork ? "artwork" : "sprites", shiny ? "shiny" : "", file);
        if (!File.Exists(path)) return null;
        try { using var image = Image.FromFile(path); return new Bitmap(image); }
        catch (ArgumentException) { return null; }
    }

    // Species without an image yet show the app's icon rather than an empty box.
    Image? Placeholder()
    {
        var path = Path.Combine(root, "Assets", "app.png");
        if (!File.Exists(path)) return null;
        try { using var image = Image.FromFile(path); return new Bitmap(image); }
        catch (ArgumentException) { return null; }
    }
    public void Dispose() { foreach (var image in cache.Values) image?.Dispose(); cache.Clear(); filledAreas.Clear(); }
}
