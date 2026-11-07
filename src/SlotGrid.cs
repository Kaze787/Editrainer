using System.ComponentModel;
using System.Drawing.Drawing2D;
using static Editrainer.Strings;

namespace Editrainer;

/// <summary>Pokémon laid out like the game's box screen: a place stays visible with nobody in it.</summary>
sealed class SlotGrid : ScrollableControl
{
    const int Gap = 4, SmallestTile = 32, LevelHeight = 15, Corner = 8, SpriteMargin = 3, MarkerSize = 8;
    readonly SpriteStore sprites;
    readonly ToolTip tips;
    readonly bool showsLevel;
    readonly string emptyText;
    List<PokemonEntry?> slots = [];
    HashSet<PokemonEntry>? matches;
    PokemonEntry? selected;
    int columns = 6, tile = 1, hovered = -1, pressed = -1, dropOver = -1;
    Point pressedAt;
    bool dragging, copying;

    public SlotGrid(SpriteStore sprites, ToolTip tips, bool showsLevel = false, string emptyText = "")
    {
        this.sprites = sprites; this.tips = tips; this.showsLevel = showsLevel; this.emptyText = emptyText;
        Dock = DockStyle.Fill; Margin = Padding.Empty; BackColor = Theme.Surface; Font = Theme.Font(8, true); TabStop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public event Action<PokemonEntry>? Picked;
    /// <summary>A Pokémon was dropped: its grid and place, the grid and place it landed on, and whether Ctrl asks for a copy.</summary>
    public event Action<SlotGrid, int, SlotGrid, int, bool>? Dropped;
    /// <summary>A right click on a place, or on nothing (-1), and where it happened.</summary>
    public event Action<int, Point>? MenuRequested;
    /// <summary>The other grid a dragged Pokémon can land on.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal SlotGrid? Partner { get; set; }
    /// <summary>Whether what the grid shows are real places, which Pokémon can be dragged between.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] internal bool Movable { get; set; }
    public IReadOnlyList<PokemonEntry?> Slots => slots;
    int TileHeight => tile + (showsLevel ? LogicalToDeviceUnits(LevelHeight) : 0);

    /// <summary>Shows these places. Given matches, everyone else fades.</summary>
    public void SetSlots(IEnumerable<PokemonEntry?> places, int columnCount, IEnumerable<PokemonEntry>? found = null)
    {
        var next = places.ToList();
        if (!next.SequenceEqual(slots)) AutoScrollPosition = Point.Empty;
        slots = next; columns = columnCount; matches = found?.ToHashSet();
        Arrange();
    }

    public void Highlight(PokemonEntry? entry)
    {
        selected = entry;
        Reveal(entry);
        Invalidate();
    }

    // Tiles grow with the window until they fill it, and only scroll once they would get too small.
    void Arrange()
    {
        int rows = Math.Max(1, (slots.Count + columns - 1) / columns);
        int label = TileHeight - tile;
        int wide = (ClientSize.Width - Gap * (columns - 1)) / columns;
        int tall = (ClientSize.Height - Gap * (rows - 1)) / rows - label;
        tile = Math.Max(1, Math.Min(wide, Math.Max(tall, SmallestTile)));
        AutoScrollMinSize = new Size(0, rows * TileHeight + (rows - 1) * Gap);
        Invalidate();
    }

    Rectangle SlotBounds(int index)
    {
        int left = (ClientSize.Width - columns * tile - (columns - 1) * Gap) / 2;
        return new(left + index % columns * (tile + Gap), AutoScrollPosition.Y + index / columns * (TileHeight + Gap), tile, TileHeight);
    }

    void Reveal(PokemonEntry? entry)
    {
        int index = entry == null ? -1 : slots.IndexOf(entry);
        if (index < 0) return;
        var place = SlotBounds(index);
        int scrolled = -AutoScrollPosition.Y;
        if (place.Top < 0) AutoScrollPosition = new Point(0, scrolled + place.Top);
        else if (place.Bottom > ClientSize.Height) AutoScrollPosition = new Point(0, scrolled + place.Bottom - ClientSize.Height);
    }

    int IndexAt(Point point)
    {
        for (int i = 0; i < slots.Count; i++) if (SlotBounds(i).Contains(point)) return i;
        return -1;
    }

    (SlotGrid Grid, int Index)? DropTarget(Point screen)
    {
        foreach (var grid in new[] { this, Partner })
            if (grid is { Visible: true, Movable: true } && grid.RectangleToScreen(grid.ClientRectangle).Contains(screen)
                && grid.IndexAt(grid.PointToClient(screen)) is >= 0 and var index) return (grid, index);
        return null;
    }

    void ShowDropTarget((SlotGrid Grid, int Index)? target)
    {
        foreach (var grid in new[] { this, Partner })
        {
            int over = target is { } place && place.Grid == grid ? place.Index : -1;
            if (grid == null || grid.dropOver == over) continue;
            grid.dropOver = over; grid.Invalidate();
        }
    }

    void EndDrag()
    {
        ShowDropTarget(null);
        pressed = -1; dragging = false; copying = false;
    }

    // Tiles first: the base class would otherwise add a scrollbar for the size they had before.
    protected override void OnResize(EventArgs e) { Arrange(); base.OnResize(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int index = IndexAt(e.Location);
        pressed = e.Button == MouseButtons.Left && Movable && index >= 0 && slots[index] != null ? index : -1;
        pressedAt = e.Location;
        // A right click acts on the place under it, without changing who is selected.
        if (e.Button != MouseButtons.Right && index >= 0 && slots[index] is { } entry) Picked?.Invoke(entry);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) { MenuRequested?.Invoke(IndexAt(e.Location), e.Location); return; }
        bool dropped = dragging, copy = copying || ModifierKeys.HasFlag(Keys.Control);
        int from = pressed;
        var target = DropTarget(PointToScreen(e.Location));
        EndDrag();
        Cursor = IndexAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        if (dropped && target is { } place) Dropped?.Invoke(this, from, place.Grid, place.Index, copy);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (pressed >= 0 && e.Button != MouseButtons.Left) EndDrag();
        var drag = SystemInformation.DragSize;
        if (pressed >= 0 && !dragging) dragging = Math.Abs(e.X - pressedAt.X) >= drag.Width / 2 || Math.Abs(e.Y - pressedAt.Y) >= drag.Height / 2;
        if (dragging)
        {
            var target = DropTarget(PointToScreen(e.Location));
            ShowDropTarget(target);
            Cursor = target == null ? Cursors.No : Cursors.Hand;
            return;
        }
        int index = IndexAt(e.Location);
        if (index == hovered) return;
        hovered = index;
        var under = index >= 0 ? slots[index] : null;
        Cursor = under == null ? Cursors.Default : Cursors.Hand;
        tips.SetToolTip(this, under != null ? $"{under.DisplayName}  ·  {under.SpeciesName}  ·  {T("Nv. {0}", under.Edit.Level)}  ·  {under.Location}"
            : Movable && index >= 0 ? T("Arrastra un Pokémon aquí, o usa el clic derecho para clonar o importar.") : "");
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    // Arrow keys skip empty places, the way the old list had none to stop at.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { EndDrag(); return; }
        int step = e.KeyCode switch { Keys.Left => -1, Keys.Right => 1, Keys.Up => -columns, Keys.Down => columns, _ => 0 };
        if (step == 0) return;
        e.Handled = true;
        int from = selected == null ? -1 : slots.IndexOf(selected);
        for (int i = from + step; i >= 0 && i < slots.Count; i += step)
            if (slots[i] is { } next) { Picked?.Invoke(next); return; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.Half;
        if (slots.Count == 0)
            TextRenderer.DrawText(g, emptyText, Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        for (int i = 0; i < slots.Count; i++)
        {
            var place = SlotBounds(i);
            if (place.IntersectsWith(e.ClipRectangle)) DrawSlot(g, place, slots[i], i == dropOver);
        }
    }

    void DrawSlot(Graphics g, Rectangle place, PokemonEntry? entry, bool dropTarget)
    {
        var corner = new Size(Corner, Corner);
        bool chosen = dropTarget || entry != null && entry == selected;
        var outline = new Rectangle(place.X, place.Y, place.Width - 1, place.Height - 1);
        using (var fill = new SolidBrush(chosen ? Theme.AccentLight : entry == null ? Theme.Surface : Theme.Background)) g.FillRoundedRectangle(fill, outline, corner);
        using (var line = new Pen(chosen ? Theme.Accent : Theme.Line, chosen ? 2 : 1)) g.DrawRoundedRectangle(line, outline, corner);
        if (entry == null) return;
        DrawSprite(g, new Rectangle(place.X + SpriteMargin, place.Y + SpriteMargin, tile - 2 * SpriteMargin, tile - 2 * SpriteMargin), entry);
        if (showsLevel)
            TextRenderer.DrawText(g, T("Nv. {0}", entry.Edit.Level), Font, new Rectangle(place.X, place.Y + tile - 1, place.Width, place.Height - tile), Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (entry.Edit.IsShiny) TextRenderer.DrawText(g, "✦", Font, new Point(place.X + SpriteMargin, place.Y + 1), Theme.Accent, TextFormatFlags.NoPadding);
        if (entry.Dirty || entry.Moved)
        {
            using var dot = new SolidBrush(Theme.Accent);
            g.FillEllipse(dot, place.Right - MarkerSize - SpriteMargin - 1, place.Y + SpriteMargin, MarkerSize, MarkerSize);
        }
        if (matches != null && !matches.Contains(entry))
        {
            using var veil = new SolidBrush(Color.FromArgb(200, BackColor));
            g.FillRoundedRectangle(veil, place, corner);
        }
    }

    // Sprites are mostly empty margin, so only what is drawn on them gets scaled to the tile.
    void DrawSprite(Graphics g, Rectangle area, PokemonEntry entry)
    {
        if (sprites.Get(entry.Original.Species, entry.ImageVariant, shiny: entry.Edit.IsShiny) is not Bitmap image) return;
        var filled = sprites.FilledArea(image);
        float scale = Math.Min((float)area.Width / filled.Width, (float)area.Height / filled.Height);
        float width = filled.Width * scale, height = filled.Height * scale;
        g.InterpolationMode = scale > 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, new RectangleF(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height), filled, GraphicsUnit.Pixel);
    }

    // For the interaction checks, which have no real mouse or keyboard.
    internal void PressSlot(int index)
    {
        var place = SlotBounds(index);
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, place.X + place.Width / 2, place.Y + place.Height / 2, 0));
    }
    internal void PressKey(Keys key) => OnKeyDown(new KeyEventArgs(key));

    /// <summary>A whole drag, press to release, from one of this grid's places to one of a grid's.</summary>
    internal void DragSlot(int from, SlotGrid target, int to, bool copy = false)
    {
        var start = SlotBounds(from); start.Offset(start.Width / 2, start.Height / 2);
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
        var goal = target.SlotBounds(to); goal.Offset(goal.Width / 2, goal.Height / 2);
        var end = PointToClient(target.PointToScreen(goal.Location));
        OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, end.X, end.Y, 0));
        copying = copy;
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
    }
}
