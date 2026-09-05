using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WarThunderUIDGuard;

/// <summary>
/// A lightweight, owner-drawn button with a glass-like surface. Animations only
/// run while the visual state is changing, so idle buttons do not keep timers alive.
/// </summary>
internal sealed class GlassButton : Button
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const double HoverDurationMilliseconds = 150d;
    private const double PressDurationMilliseconds = 80d;
    private const double ShimmerDurationMilliseconds = 620d;

    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 16 };
    private Color _accentColor = UiTheme.Primary;
    private bool _isPrimary;
    private bool _isDefault;
    private bool _mousePressed;
    private bool _keyboardPressed;
    private bool _shimmerActive;
    private float _hoverProgress;
    private float _hoverTarget;
    private float _pressProgress;
    private float _pressTarget;
    private float _shimmerProgress;
    private long _lastAnimationTimestamp;

    public GlassButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);

        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        AccessibleRole = AccessibleRole.PushButton;

        _animationTimer.Tick += HandleAnimationTick;
    }

    [Category("Appearance")]
    [Description("Accent color used by the glass surface and border.")]
    public Color AccentColor
    {
        get => _accentColor;
        set
        {
            if (_accentColor == value)
            {
                return;
            }

            _accentColor = value;
            Invalidate();
        }
    }

    [Category("Appearance")]
    [DefaultValue(false)]
    [Description("Uses a filled accent surface with white text when enabled.")]
    public bool IsPrimary
    {
        get => _isPrimary;
        set
        {
            if (_isPrimary == value)
            {
                return;
            }

            _isPrimary = value;
            Invalidate();
        }
    }

    public override void NotifyDefault(bool value)
    {
        base.NotifyDefault(value);
        if (_isDefault == value)
        {
            return;
        }

        _isDefault = value;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hoverTarget = 1f;

        if (CanAnimate())
        {
            _shimmerProgress = 0f;
            _shimmerActive = true;
        }

        StartAnimationOrSnap();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mousePressed = false;
        _hoverTarget = 0f;
        _pressTarget = _keyboardPressed ? 1f : 0f;
        _shimmerActive = false;
        StartAnimationOrSnap();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        _mousePressed = true;
        _pressTarget = 1f;
        StartAnimationOrSnap();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        if (mevent.Button != MouseButtons.Left)
        {
            return;
        }

        _mousePressed = false;
        _pressTarget = _keyboardPressed ? 1f : 0f;
        StartAnimationOrSnap();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture || !_mousePressed)
        {
            return;
        }

        _mousePressed = false;
        _pressTarget = _keyboardPressed ? 1f : 0f;
        StartAnimationOrSnap();
    }

    protected override void OnKeyDown(KeyEventArgs kevent)
    {
        base.OnKeyDown(kevent);
        if (!Enabled || kevent.Alt || kevent.Control || kevent.Shift ||
            (kevent.KeyCode != Keys.Space && kevent.KeyCode != Keys.Enter))
        {
            return;
        }

        _keyboardPressed = true;
        _pressTarget = 1f;
        StartAnimationOrSnap();
    }

    protected override void OnKeyUp(KeyEventArgs kevent)
    {
        base.OnKeyUp(kevent);
        if (kevent.KeyCode != Keys.Space && kevent.KeyCode != Keys.Enter)
        {
            return;
        }

        _keyboardPressed = false;
        _pressTarget = _mousePressed ? 1f : 0f;
        StartAnimationOrSnap();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _keyboardPressed = false;
        _pressTarget = _mousePressed ? 1f : 0f;
        StartAnimationOrSnap();
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled)
        {
            ResetInteractionState();
        }

        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            ResetInteractionState();
        }

        Invalidate();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        StopAnimation();
        base.OnHandleDestroyed(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Invalidate();
    }

    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        if (SystemInformation.HighContrast)
        {
            SnapToTargets(stopShimmer: true);
        }

        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        // Transparent owner-drawn Button backgrounds are not reliably composed by
        // WinForms when several transparent TableLayoutPanels sit between this
        // control and an owner-drawn card. Paint the card's very subtle vertical
        // surface gradient directly so the shadow reserve and rounded corners can
        // never expose the native control's black backing bitmap.
        var (top, bottom) = ResolveBackdropColors();
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        if (top == bottom || ClientSize.Height == 1)
        {
            pevent.Graphics.Clear(top);
            return;
        }

        using var background = new LinearGradientBrush(ClientRectangle, top, bottom, 90f);
        pevent.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // ButtonBase can skip WM_ERASEBKGND on partial invalidation. Always cover
        // the complete owner-drawn surface before composing translucent shadows.
        OnPaintBackground(e);
        if (Width < 6 || Height < 6)
        {
            return;
        }

        if (SystemInformation.HighContrast)
        {
            PaintHighContrast(e.Graphics);
            return;
        }

        var graphicsState = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var scale = DeviceDpi / 96f;
            var inset = Math.Max(1, (int)Math.Round(scale));
            var shadowDepth = Math.Max(2, (int)Math.Round(3f * scale));
            var pressOffset = (int)Math.Round(_pressProgress * Math.Max(1f, scale));
            var radius = Math.Max(6, (int)Math.Round(11f * scale));
            var bodyBounds = new Rectangle(
                inset,
                inset + pressOffset,
                Math.Max(1, Width - (inset * 2) - 1),
                Math.Max(1, Height - (inset * 2) - shadowDepth - 1));

            DrawShadow(e.Graphics, bodyBounds, radius, shadowDepth);

            using var bodyPath = UiTheme.CreateRoundedPath(bodyBounds, radius);
            var (topColor, bottomColor, borderColor, textColor) = ResolvePalette();
            using (var fill = new LinearGradientBrush(bodyBounds, topColor, bottomColor, 90f))
            {
                e.Graphics.FillPath(fill, bodyPath);
            }

            DrawGlassHighlight(e.Graphics, bodyBounds, bodyPath);
            DrawShimmer(e.Graphics, bodyBounds, bodyPath);

            using (var borderPen = new Pen(borderColor, _isDefault ? Math.Max(2f, scale * 1.6f) : Math.Max(1f, scale)))
            {
                borderPen.Alignment = PenAlignment.Inset;
                e.Graphics.DrawPath(borderPen, bodyPath);
            }

            var innerBounds = Rectangle.Inflate(bodyBounds, -Math.Max(1, (int)Math.Round(scale)), -Math.Max(1, (int)Math.Round(scale)));
            if (innerBounds.Width > 2 && innerBounds.Height > 2)
            {
                using var innerPath = UiTheme.CreateRoundedPath(innerBounds, Math.Max(3, radius - Math.Max(1, (int)Math.Round(scale))));
                using var innerPen = new Pen(Color.FromArgb(_isPrimary ? 76 : 190, Color.White), Math.Max(1f, scale));
                innerPen.Alignment = PenAlignment.Inset;
                e.Graphics.DrawPath(innerPen, innerPath);
            }

            DrawContent(e.Graphics, bodyBounds, textColor, pressOffset);
        }
        finally
        {
            e.Graphics.Restore(graphicsState);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAnimation();
            _animationTimer.Tick -= HandleAnimationTick;
            _animationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PaintHighContrast(Graphics graphics)
    {
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        var state = !Enabled
            ? ButtonState.Inactive
            : (_mousePressed || _keyboardPressed) ? ButtonState.Pushed : ButtonState.Normal;
        ControlPaint.DrawButton(graphics, bounds, state);

        var textBounds = Rectangle.Inflate(bounds, -Scale(7), -Scale(3));
        var textColor = Enabled ? SystemColors.ControlText : SystemColors.GrayText;
        TextRenderer.DrawText(graphics, Text, Font, textBounds, textColor, TextFlags());

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -Scale(4), -Scale(4)), textColor, SystemColors.Control);
        }
    }

    private void DrawShadow(Graphics graphics, Rectangle bodyBounds, int radius, int shadowDepth)
    {
        var fade = 1f - (_pressProgress * 0.55f);
        var hoverBoost = 1f + (_hoverProgress * 0.45f);
        var farShadow = bodyBounds;
        farShadow.Offset(0, shadowDepth);
        using (var path = UiTheme.CreateRoundedPath(farShadow, radius))
        using (var brush = new SolidBrush(Color.FromArgb((int)(14f * fade * hoverBoost), UiTheme.TextPrimary)))
        {
            graphics.FillPath(brush, path);
        }

        var nearShadow = bodyBounds;
        nearShadow.Offset(0, Math.Max(1, shadowDepth / 2));
        using var nearPath = UiTheme.CreateRoundedPath(nearShadow, radius);
        using var nearBrush = new SolidBrush(Color.FromArgb((int)(12f * fade * hoverBoost), AccentColor));
        graphics.FillPath(nearBrush, nearPath);
    }

    private void DrawGlassHighlight(Graphics graphics, Rectangle bodyBounds, GraphicsPath bodyPath)
    {
        var saved = graphics.Save();
        try
        {
            graphics.SetClip(bodyPath);
            var highlightBounds = bodyBounds;
            highlightBounds.Height = Math.Max(1, (int)Math.Round(bodyBounds.Height * 0.58f));
            var alpha = _isPrimary
                ? 58 + (int)Math.Round(_hoverProgress * 18f)
                : 205 + (int)Math.Round(_hoverProgress * 25f);
            using var highlight = new LinearGradientBrush(
                highlightBounds,
                Color.FromArgb(Math.Min(235, alpha), Color.White),
                Color.FromArgb(0, Color.White),
                90f);
            graphics.FillRectangle(highlight, highlightBounds);
        }
        finally
        {
            graphics.Restore(saved);
        }
    }

    private void DrawShimmer(Graphics graphics, Rectangle bodyBounds, GraphicsPath bodyPath)
    {
        if (!_shimmerActive || _shimmerProgress <= 0f || _shimmerProgress >= 1f)
        {
            return;
        }

        var saved = graphics.Save();
        try
        {
            graphics.SetClip(bodyPath);
            var travel = bodyBounds.Width * 1.7f;
            var centerX = bodyBounds.Left - (bodyBounds.Width * 0.35f) + (travel * _shimmerProgress);
            var bandWidth = Math.Max(10f, bodyBounds.Width * 0.16f);
            var slant = bodyBounds.Height * 0.35f;
            var points = new[]
            {
                new PointF(centerX - bandWidth + slant, bodyBounds.Top),
                new PointF(centerX + bandWidth + slant, bodyBounds.Top),
                new PointF(centerX + bandWidth - slant, bodyBounds.Bottom),
                new PointF(centerX - bandWidth - slant, bodyBounds.Bottom)
            };
            using var shimmer = new SolidBrush(Color.FromArgb(_isPrimary ? 36 : 74, Color.White));
            graphics.FillPolygon(shimmer, points);

            var coreWidth = bandWidth * 0.24f;
            var corePoints = new[]
            {
                new PointF(centerX - coreWidth + slant, bodyBounds.Top),
                new PointF(centerX + coreWidth + slant, bodyBounds.Top),
                new PointF(centerX + coreWidth - slant, bodyBounds.Bottom),
                new PointF(centerX - coreWidth - slant, bodyBounds.Bottom)
            };
            using var core = new SolidBrush(Color.FromArgb(_isPrimary ? 26 : 48, Color.White));
            graphics.FillPolygon(core, corePoints);
        }
        finally
        {
            graphics.Restore(saved);
        }
    }

    private void DrawContent(Graphics graphics, Rectangle bodyBounds, Color textColor, int pressOffset)
    {
        var textBounds = Rectangle.Inflate(bodyBounds, -Scale(8), -Scale(3));
        textBounds.Offset(0, pressOffset > 0 ? 1 : 0);
        TextRenderer.DrawText(graphics, Text, Font, textBounds, textColor, TextFlags());

        if (Focused && ShowFocusCues)
        {
            var focusBounds = Rectangle.Inflate(bodyBounds, -Scale(4), -Scale(4));
            var focusColor = _isPrimary ? Color.FromArgb(220, Color.White) : EnsureDarkTextContrast(AccentColor);
            ControlPaint.DrawFocusRectangle(graphics, focusBounds, focusColor, Color.Transparent);
        }
    }

    private TextFormatFlags TextFlags()
    {
        var flags = TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.PreserveGraphicsClipping;

        if (!UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }
        else if (!ShowKeyboardCues)
        {
            flags |= TextFormatFlags.HidePrefix;
        }

        return flags;
    }

    private (Color Top, Color Bottom, Color Border, Color Text) ResolvePalette()
    {
        if (!Enabled)
        {
            var disabledTop = Blend(UiTheme.Surface, SystemColors.Control, 0.45f);
            var disabledBottom = Blend(disabledTop, UiTheme.Border, 0.28f);
            return (disabledTop, disabledBottom, UiTheme.Border, SystemColors.GrayText);
        }

        if (IsPrimary)
        {
            var baseColor = EnsureWhiteTextContrast(AccentColor, 5.35d);
            var top = Blend(baseColor, Color.White, 0.035f + (_hoverProgress * 0.025f));
            top = EnsureWhiteTextContrast(top, 4.65d);
            var bottom = Blend(baseColor, Color.Black, 0.09f + (_pressProgress * 0.055f));
            var border = Blend(baseColor, Color.Black, 0.18f);
            return (top, bottom, border, Color.White);
        }

        var coolWhite = Color.FromArgb(248, 251, 255);
        var topColor = Blend(Color.White, AccentColor, 0.035f + (_hoverProgress * 0.03f));
        var bottomColor = Blend(coolWhite, AccentColor, 0.095f + (_hoverProgress * 0.045f) + (_pressProgress * 0.025f));
        var borderColor = Color.FromArgb(
            220,
            Blend(AccentColor, Color.White, 0.58f - (_hoverProgress * 0.10f)));
        return (topColor, bottomColor, borderColor, UiTheme.TextPrimary);
    }

    private (Color Top, Color Bottom) ResolveBackdropColors()
    {
        var topWithinAncestor = Top;
        for (Control? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is UiCardPanel card)
            {
                var cardSurfaceBottom = Color.FromArgb(250, 252, 255);
                var surfaceHeight = Math.Max(1, card.ClientSize.Height - Scale(5));
                var topRatio = Math.Clamp(topWithinAncestor / (float)surfaceHeight, 0f, 1f);
                var bottomRatio = Math.Clamp((topWithinAncestor + Height) / (float)surfaceHeight, 0f, 1f);
                return (
                    Blend(UiTheme.Surface, cardSurfaceBottom, topRatio),
                    Blend(UiTheme.Surface, cardSurfaceBottom, bottomRatio));
            }

            if (ancestor.BackColor.A == 255)
            {
                return (ancestor.BackColor, ancestor.BackColor);
            }

            topWithinAncestor += ancestor.Top;
        }

        return (UiTheme.Background, UiTheme.Background);
    }

    private void HandleAnimationTick(object? sender, EventArgs e)
    {
        if (!CanAnimate())
        {
            SnapToTargets(stopShimmer: true);
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = _lastAnimationTimestamp == 0
            ? _animationTimer.Interval
            : Math.Min(50d, Stopwatch.GetElapsedTime(_lastAnimationTimestamp, now).TotalMilliseconds);
        _lastAnimationTimestamp = now;

        _hoverProgress = Approach(_hoverProgress, _hoverTarget, (float)(elapsedMilliseconds / HoverDurationMilliseconds));
        _pressProgress = Approach(_pressProgress, _pressTarget, (float)(elapsedMilliseconds / PressDurationMilliseconds));
        if (_shimmerActive)
        {
            _shimmerProgress += (float)(elapsedMilliseconds / ShimmerDurationMilliseconds);
            if (_shimmerProgress >= 1f)
            {
                _shimmerProgress = 1f;
                _shimmerActive = false;
            }
        }

        Invalidate();
        if (!_shimmerActive && NearlyEqual(_hoverProgress, _hoverTarget) && NearlyEqual(_pressProgress, _pressTarget))
        {
            _hoverProgress = _hoverTarget;
            _pressProgress = _pressTarget;
            StopAnimation();
        }
    }

    private void StartAnimationOrSnap()
    {
        if (!CanAnimate())
        {
            SnapToTargets(stopShimmer: true);
            return;
        }

        if (!_animationTimer.Enabled)
        {
            _lastAnimationTimestamp = Stopwatch.GetTimestamp();
            _animationTimer.Start();
        }

        Invalidate();
    }

    private bool CanAnimate() =>
        IsHandleCreated &&
        Visible &&
        Enabled &&
        !SystemInformation.HighContrast &&
        ClientAreaAnimationsEnabled();

    private void ResetInteractionState()
    {
        _mousePressed = false;
        _keyboardPressed = false;
        _hoverTarget = 0f;
        _pressTarget = 0f;
        SnapToTargets(stopShimmer: true);
    }

    private void SnapToTargets(bool stopShimmer)
    {
        _hoverProgress = _hoverTarget;
        _pressProgress = _pressTarget;
        if (stopShimmer)
        {
            _shimmerActive = false;
            _shimmerProgress = 0f;
        }

        StopAnimation();
        Invalidate();
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        _lastAnimationTimestamp = 0;
    }

    private int Scale(int logicalPixels) => Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96d));

    private static float Approach(float value, float target, float amount)
    {
        if (value < target)
        {
            return Math.Min(target, value + amount);
        }

        return Math.Max(target, value - amount);
    }

    private static bool NearlyEqual(float left, float right) => Math.Abs(left - right) < 0.001f;

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + ((to.A - from.A) * amount)),
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static Color EnsureWhiteTextContrast(Color color, double minimumRatio)
    {
        var result = Color.FromArgb(255, color.R, color.G, color.B);
        for (var step = 0; step < 20 && ContrastRatio(result, Color.White) < minimumRatio; step++)
        {
            result = Blend(result, Color.Black, 0.08f);
        }

        return result;
    }

    private static Color EnsureDarkTextContrast(Color color)
    {
        var candidate = Color.FromArgb(255, color.R, color.G, color.B);
        return ContrastRatio(candidate, Color.White) >= 3d
            ? candidate
            : Blend(candidate, Color.Black, 0.42f);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var lighter = Math.Max(RelativeLuminance(first), RelativeLuminance(second));
        var darker = Math.Min(RelativeLuminance(first), RelativeLuminance(second));
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte component)
        {
            var value = component / 255d;
            return value <= 0.04045d
                ? value / 12.92d
                : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linearize(color.R)) +
               (0.7152d * Linearize(color.G)) +
               (0.0722d * Linearize(color.B));
    }

    private static bool ClientAreaAnimationsEnabled()
    {
        if (!SystemInformation.UIEffectsEnabled)
        {
            return false;
        }

        var enabled = 0;
        return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref enabled, 0) || enabled != 0;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref int value, uint update);
}
