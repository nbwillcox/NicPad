using NicPad.Core;

namespace NicPad.UI;

public class ThemedToolStripRenderer : ToolStripProfessionalRenderer
{
    public Color HoverBackColor { get; set; } = SystemColors.Highlight;
    public Color HoverForeColor { get; set; } = SystemColors.HighlightText;
    public Color PressedBackColor { get; set; } = SystemColors.HotTrack;

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // No default bottom border/gradient line.
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Item is ToolStripButton button && (button.Selected || button.Pressed || button.Checked))
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            var color = button.Pressed || button.Selected ? (button.Pressed ? PressedBackColor : HoverBackColor) : Blend(HoverBackColor, e.ToolStrip?.BackColor ?? SystemColors.Control);
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, rect);
            return;
        }

        base.OnRenderButtonBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if ((e.Item is ToolStripButton button && (button.Selected || button.Pressed))
            || (e.Item is ToolStripMenuItem menuItem && menuItem.Selected && menuItem.Enabled))
        {
            e.TextColor = HoverForeColor;
        }

        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Item.Selected && e.Item.Enabled)
        {
            var rect = new Rectangle(Point.Empty, e.Item.Size);
            using var brush = new SolidBrush(HoverBackColor);
            e.Graphics.FillRectangle(brush, rect);
            return;
        }

        base.OnRenderMenuItemBackground(e);
    }

    private static Color Blend(Color a, Color b) =>
        Color.FromArgb((a.R + (b.R * 3)) / 4, (a.G + (b.G * 3)) / 4, (a.B + (b.B * 3)) / 4);
}

public class ThemedToolStrip : ToolStrip
{
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, 0, Height - 2, Width, 2);
    }
}

public class ThemedMenuStrip : MenuStrip
{
    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(brush, 0, Height - 2, Width, 2);
    }
}

/// <summary>Owner-drawn tabs with a close button, hover highlight and drag-to-reorder.</summary>
public class ThemedTabControl : TabControl
{
    private const int WmPaint = 0x000F;

    private int hoverCloseIndex = -1;
    private int dragIndex = -1;
    private Point dragStart;

    public ThemedTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Normal;
    }

    public event EventHandler<int>? CloseRequested;
    public event EventHandler<int>? ContextMenuRequested;
    public event EventHandler? TabsReordered;

    public ThemePalette Palette { get; set; } = Themes.Light;

    public Rectangle GetCloseButtonRect(int index)
    {
        var tabRect = GetTabRect(index);
        var size = LogicalToDeviceUnits(16);
        return new Rectangle(tabRect.Right - size - LogicalToDeviceUnits(6), tabRect.Top + ((tabRect.Height - size) / 2), size, size);
    }

    /// <summary>Moves a tab without recreating it (keeps editor state and preview alive).</summary>
    public void MoveTab(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= TabCount || to >= TabCount)
        {
            return;
        }

        SuspendLayout();
        var page = TabPages[from];
        TabPages.RemoveAt(from);
        TabPages.Insert(to, page);
        SelectedTab = page;
        ResumeLayout();
        TabsReordered?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Index < 0 || e.Index >= TabCount)
        {
            return;
        }

        var p = Palette;
        var tabRect = GetTabRect(e.Index);
        var isSelected = SelectedIndex == e.Index;

        using (var brush = new SolidBrush(isSelected ? p.TabSelected : p.TabBack))
        {
            e.Graphics.FillRectangle(brush, tabRect);
        }

        using (var pen = new Pen(p.TabBorder))
        {
            e.Graphics.DrawRectangle(pen, tabRect.X, tabRect.Y, tabRect.Width - 1, tabRect.Height - 1);
            if (!isSelected)
            {
                e.Graphics.DrawLine(pen, tabRect.Left, tabRect.Bottom - 1, tabRect.Right, tabRect.Bottom - 1);
            }
        }

        var pad = LogicalToDeviceUnits(8);
        var textRect = new Rectangle(tabRect.X + pad, tabRect.Y + LogicalToDeviceUnits(2), tabRect.Width - LogicalToDeviceUnits(28), tabRect.Height - LogicalToDeviceUnits(2));
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, textRect, p.TabText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var closeRect = GetCloseButtonRect(e.Index);
        if (e.Index == hoverCloseIndex)
        {
            using var hover = new SolidBrush(Color.FromArgb(60, p.TabText));
            e.Graphics.FillRectangle(hover, closeRect);
        }

        using var xPen = new Pen(p.TabClose, Math.Max(1.5f, DeviceDpi / 64f));
        var inset = LogicalToDeviceUnits(4);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawLine(xPen, closeRect.Left + inset, closeRect.Top + inset, closeRect.Right - inset, closeRect.Bottom - inset);
        e.Graphics.DrawLine(xPen, closeRect.Right - inset, closeRect.Top + inset, closeRect.Left + inset, closeRect.Bottom - inset);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        using var brush = new SolidBrush(Palette.Chrome);
        var height = TabCount > 0 ? GetTabRect(0).Bottom + 2 : 25;
        e.Graphics.FillRectangle(brush, 0, 0, Width, height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        var index = TabIndexAt(e.Location);
        if (index < 0)
        {
            return;
        }

        if (e.Button == MouseButtons.Left && !GetCloseButtonRect(index).Contains(e.Location))
        {
            dragIndex = index;
            dragStart = e.Location;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);

        var index = TabIndexAt(e.Location);
        var hover = index >= 0 && GetCloseButtonRect(index).Contains(e.Location) ? index : -1;
        if (hover != hoverCloseIndex)
        {
            hoverCloseIndex = hover;
            Invalidate();
        }

        if (dragIndex >= 0 && e.Button == MouseButtons.Left)
        {
            var drag = SystemInformation.DragSize;
            if (Math.Abs(e.X - dragStart.X) < drag.Width && Math.Abs(e.Y - dragStart.Y) < drag.Height)
            {
                return;
            }

            if (index >= 0 && index != dragIndex)
            {
                MoveTab(dragIndex, index);
                dragIndex = index;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseUp(e);
        var wasDragging = dragIndex;
        dragIndex = -1;
        var index = TabIndexAt(e.Location);
        if (index < 0)
        {
            return;
        }

        if (e.Button == MouseButtons.Left && wasDragging < 0 && GetCloseButtonRect(index).Contains(e.Location))
        {
            CloseRequested?.Invoke(this, index);
        }
        else if (e.Button == MouseButtons.Middle)
        {
            CloseRequested?.Invoke(this, index);
        }
        else if (e.Button == MouseButtons.Right)
        {
            SelectedIndex = index;
            ContextMenuRequested?.Invoke(this, index);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverCloseIndex != -1)
        {
            hoverCloseIndex = -1;
            Invalidate();
        }
    }

    protected override void OnKeyDown(KeyEventArgs ke)
    {
        ArgumentNullException.ThrowIfNull(ke);
        if (ke.KeyCode == Keys.Apps || (ke.Shift && ke.KeyCode == Keys.F10))
        {
            ContextMenuRequested?.Invoke(this, SelectedIndex);
            ke.Handled = true;
            return;
        }

        base.OnKeyDown(ke);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmPaint && IsHandleCreated)
        {
            // The native control repaints a light strip behind the tabs; paint over it after it's done.
            BeginInvoke(PaintHeaderBackground);
        }
    }

    private int TabIndexAt(Point location)
    {
        for (var i = 0; i < TabCount; i++)
        {
            if (GetTabRect(i).Contains(location))
            {
                return i;
            }
        }

        return -1;
    }

    private void PaintHeaderBackground()
    {
        if (TabCount == 0 || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            using var g = CreateGraphics();
            using var brush = new SolidBrush(Palette.Chrome);
            var headerHeight = GetTabRect(0).Bottom + 4;
            g.FillRectangle(brush, 0, 0, Width, headerHeight);

            // The native control also draws a light frame around the page area; cover it with the theme color.
            var page = DisplayRectangle;
            using var frame = new SolidBrush(Palette.Separator);
            g.FillRectangle(frame, 0, headerHeight, Math.Max(0, page.Left), Height - headerHeight);
            g.FillRectangle(frame, page.Right, headerHeight, Math.Max(0, Width - page.Right), Height - headerHeight);
            g.FillRectangle(frame, 0, page.Bottom, Width, Math.Max(0, Height - page.Bottom));
            g.FillRectangle(frame, 0, headerHeight, Width, Math.Max(0, page.Top - headerHeight));

            for (var i = 0; i < TabCount; i++)
            {
                using var args = new DrawItemEventArgs(g, Font, GetTabRect(i), i,
                    i == SelectedIndex ? DrawItemState.Selected : DrawItemState.Default, ForeColor, BackColor);
                OnDrawItem(args);
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed between BeginInvoke and execution.
        }
    }
}
