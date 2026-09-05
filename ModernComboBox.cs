namespace WarThunderUIDGuard;

/// <summary>Keep the collapsed value neutral while retaining native keyboard and drop-down behavior.</summary>
internal sealed class ModernComboBox : ComboBox
{
    public ModernComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        UpdateItemHeight();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;

        // Windows also marks the collapsed value as Selected when the control
        // retains focus. Only an actual row in the open list needs a selection fill.
        var valueField = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var selectedRow = !valueField && (e.State & DrawItemState.Selected) != 0;
        var background = selectedRow ? UiTheme.Selection : BackColor;
        var foreground = Enabled ? ForeColor : SystemColors.GrayText;
        if (SystemInformation.HighContrast)
        {
            background = selectedRow ? SystemColors.Highlight : SystemColors.Window;
            foreground = !Enabled ? SystemColors.GrayText :
                selectedRow ? SystemColors.HighlightText : SystemColors.WindowText;
        }

        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var inset = Math.Max(3, (int)Math.Round(5 * DeviceDpi / 96d));
        var textBounds = Rectangle.Inflate(e.Bounds, -inset, 0);
        var text = e.Index >= 0 && e.Index < Items.Count ? GetItemText(Items[e.Index]) : Text;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
        TextRenderer.DrawText(e.Graphics, text, Font, textBounds, foreground, flags);

        if (valueField && Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics,
                Rectangle.Inflate(e.Bounds, -1, -1), foreground, background);
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateItemHeight();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateItemHeight();
        Invalidate();
    }

    private void UpdateItemHeight() => ItemHeight = Font.Height +
        Math.Max(4, (int)Math.Round(6 * DeviceDpi / 96d));
}
