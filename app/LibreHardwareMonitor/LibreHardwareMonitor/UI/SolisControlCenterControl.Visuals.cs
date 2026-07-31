#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl
{
    private sealed class SolisDashboardPanel : TableLayoutPanel
    {
        public SolisDashboardPanel()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
        }

        public Color BorderColor { get; set; } = Color.Transparent;

        public int CornerRadius { get; set; } = 10;

        public Color FillColor { get; set; } = Color.Transparent;

        public Color FillColorEnd { get; set; } = Color.Transparent;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 1 || bounds.Height <= 1)
                return;

            bounds.Width -= 1;
            bounds.Height -= 1;
            using GraphicsPath path = CreateRoundedRectangle(bounds, CornerRadius);
            Color end = FillColorEnd == Color.Transparent ? FillColor : FillColorEnd;
            using var brush = new LinearGradientBrush(
                bounds,
                FillColor,
                end,
                LinearGradientMode.Vertical);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (BorderColor == Color.Transparent || Width <= 1 || Height <= 1)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            using GraphicsPath path = CreateRoundedRectangle(bounds, CornerRadius);
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 1 || Height <= 1)
                return;

            using GraphicsPath path = CreateRoundedRectangle(
                new Rectangle(0, 0, Width, Height),
                CornerRadius);
            Region?.Dispose();
            Region = new Region(path);
        }
    }

    private sealed class SolisFluentIcon : Control
    {
        private readonly Font _iconFont;

        public SolisFluentIcon(string glyph, float iconSize = 16)
        {
            DoubleBuffered = true;
            Glyph = glyph;
            _iconFont = new Font(
                "Segoe Fluent Icons",
                iconSize,
                FontStyle.Regular,
                GraphicsUnit.Point);
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
        }

        public Color BadgeColor { get; set; } = Color.Transparent;

        public Color GlyphColor { get; set; } = SystemColors.Highlight;

        public string Glyph { get; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = ClientRectangle;
            bounds.Inflate(-1, -1);
            if (BadgeColor != Color.Transparent)
            {
                using var badgeBrush = new SolidBrush(BadgeColor);
                e.Graphics.FillEllipse(badgeBrush, bounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Glyph,
                _iconFont,
                ClientRectangle,
                GlyphColor,
                Color.Transparent,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _iconFont.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class SolisNavigationButton : Button
    {
        private readonly Font _iconFont;
        private bool _hovered;

        public SolisNavigationButton(string text, string glyph)
        {
            DoubleBuffered = true;
            Text = text;
            Glyph = glyph;
            _iconFont = new Font(
                "Segoe Fluent Icons",
                13,
                FontStyle.Regular,
                GraphicsUnit.Point);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public Color AccentColor { get; set; } = SystemColors.Highlight;

        public Color BaseColor { get; set; } = SystemColors.Control;

        public Color HoverColor { get; set; } = SystemColors.ControlLight;

        public Color InactiveTextColor { get; set; } = SystemColors.ControlText;

        public string Glyph { get; }

        public bool Selected { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BaseColor);
            Rectangle background = ClientRectangle;
            background.Inflate(-2, -2);

            if (Selected || _hovered)
            {
                using GraphicsPath path = CreateRoundedRectangle(background, 8);
                using var brush = new SolidBrush(Selected ? AccentColor : HoverColor);
                e.Graphics.FillPath(brush, path);
            }

            Color foreground = Selected ? Color.White : InactiveTextColor;
            Rectangle iconBounds = new(16, 0, 30, Height);
            TextRenderer.DrawText(
                e.Graphics,
                Glyph,
                _iconFont,
                iconBounds,
                foreground,
                Color.Transparent,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine);
            Rectangle textBounds = new(56, 0, Math.Max(0, Width - 68), Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                foreground,
                Color.Transparent,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _iconFont.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class SolisProgressBar : Control
    {
        private int _value;

        public SolisProgressBar()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
        }

        public Color AccentColor { get; set; } = SystemColors.Highlight;

        public Color TrackColor { get; set; } = SystemColors.ControlDark;

        public int Maximum { get; set; } = 100;

        public int Minimum { get; set; }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(Minimum, Math.Min(Maximum, value));
                if (_value == clamped)
                    return;

                _value = clamped;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(ResolveControlBackground(Parent, BackColor));
            Rectangle track = ClientRectangle;
            int verticalInset = Math.Max(4, (Height - 8) / 2);
            track.Inflate(-1, -verticalInset);
            if (track.Width <= 0 || track.Height <= 0)
                return;

            using GraphicsPath trackPath = CreateRoundedRectangle(track, track.Height / 2);
            using var trackBrush = new SolidBrush(TrackColor);
            e.Graphics.FillPath(trackBrush, trackPath);

            double ratio = Maximum <= Minimum
                ? 0
                : (Value - Minimum) / (double)(Maximum - Minimum);
            int width = (int)Math.Round(track.Width * ratio);
            if (width <= 0)
                return;

            Rectangle progress = new(track.X, track.Y, Math.Max(track.Height, width), track.Height);
            progress.Width = Math.Min(progress.Width, track.Width);
            using GraphicsPath progressPath = CreateRoundedRectangle(progress, progress.Height / 2);
            using var progressBrush = new SolidBrush(AccentColor);
            e.Graphics.FillPath(progressBrush, progressPath);
        }
    }

    private sealed class SolisSlider : Control
    {
        private int _value;

        public SolisSlider()
        {
            DoubleBuffered = true;
            TabStop = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
        }

        public event EventHandler? ValueChanged;

        public Color AccentColor { get; set; } = SystemColors.Highlight;

        public Color DisabledColor { get; set; } = SystemColors.ControlDark;

        public int LargeChange { get; set; } = 10;

        public int Maximum { get; set; } = 100;

        public int Minimum { get; set; }

        public int SmallChange { get; set; } = 1;

        public Color ThumbColor { get; set; } = Color.White;

        public Color TrackColor { get; set; } = SystemColors.ControlDark;

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(Minimum, Math.Min(Maximum, value));
                if (_value == clamped)
                    return;

                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Down:
                    Value -= SmallChange;
                    e.Handled = true;
                    break;
                case Keys.Right:
                case Keys.Up:
                    Value += SmallChange;
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    Value -= LargeChange;
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    Value += LargeChange;
                    e.Handled = true;
                    break;
                case Keys.Home:
                    Value = Minimum;
                    e.Handled = true;
                    break;
                case Keys.End:
                    Value = Maximum;
                    e.Handled = true;
                    break;
            }

            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            SetValueFromPointer(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) != 0)
                SetValueFromPointer(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value += e.Delta > 0 ? SmallChange : -SmallChange;
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(ResolveControlBackground(Parent, BackColor));

            const int thumbSize = 14;
            Rectangle track = new(
                thumbSize / 2,
                Math.Max(0, (Height - 6) / 2),
                Math.Max(1, Width - thumbSize),
                6);
            using (GraphicsPath trackPath = CreateRoundedRectangle(track, 3))
            using (var trackBrush = new SolidBrush(Enabled ? TrackColor : DisabledColor))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
            }

            double ratio = Maximum <= Minimum
                ? 0
                : (Value - Minimum) / (double)(Maximum - Minimum);
            int progressWidth = Math.Max(0, (int)Math.Round(track.Width * ratio));
            if (progressWidth > 0)
            {
                Rectangle progress = new(track.X, track.Y, progressWidth, track.Height);
                using GraphicsPath progressPath = CreateRoundedRectangle(progress, 3);
                using var progressBrush = new SolidBrush(Enabled ? AccentColor : DisabledColor);
                e.Graphics.FillPath(progressBrush, progressPath);
            }

            int thumbX = track.X + progressWidth - thumbSize / 2;
            thumbX = Math.Max(0, Math.Min(Width - thumbSize, thumbX));
            Rectangle thumb = new(
                thumbX,
                Math.Max(0, (Height - thumbSize) / 2),
                thumbSize,
                thumbSize);
            using var thumbBrush = new SolidBrush(Enabled ? ThumbColor : DisabledColor);
            e.Graphics.FillEllipse(thumbBrush, thumb);
        }

        private void SetValueFromPointer(int x)
        {
            if (!Enabled || Maximum <= Minimum)
                return;

            const int thumbSize = 14;
            int trackWidth = Math.Max(1, Width - thumbSize);
            double ratio = Math.Max(
                0,
                Math.Min(1, (x - thumbSize / 2d) / trackWidth));
            Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
        }
    }

    private sealed class SolisStatusLabel : Label
    {
        public Color CheckingColor { get; set; } = SystemColors.Highlight;

        public Color FaultColor { get; set; } = Color.FromArgb(221, 72, 85);

        public Color NormalColor { get; set; } = Color.FromArgb(72, 199, 116);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color accent = (Tag as string) switch
            {
                "service-normal" => NormalColor,
                "service-fault" => FaultColor,
                _ => CheckingColor
            };
            Size measured = TextRenderer.MeasureText(
                e.Graphics,
                Text,
                Font,
                new Size(Math.Max(0, Width - 20), Height),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Rectangle pill = new(
                0,
                Math.Max(0, (Height - Math.Min(28, Height)) / 2),
                Math.Min(Width, measured.Width + 22),
                Math.Min(28, Height));
            if (pill.Width > 0 && pill.Height > 0)
            {
                using GraphicsPath path = CreateRoundedRectangle(pill, pill.Height / 2);
                using var brush = new SolidBrush(Color.FromArgb(40, accent));
                e.Graphics.FillPath(brush, path);
            }
            Rectangle textBounds = new(
                11,
                0,
                Math.Max(0, Width - 18),
                Height);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                accent,
                Color.Transparent,
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(
        Rectangle bounds,
        int radius)
    {
        int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ResolveControlBackground(
        Control? control,
        Color fallback)
    {
        for (Control? current = control; current is not null; current = current.Parent)
        {
            if (current is SolisDashboardPanel dashboard &&
                dashboard.FillColor != Color.Transparent)
            {
                return dashboard.FillColor;
            }
        }

        for (Control? current = control; current is not null; current = current.Parent)
        {
            if (current.BackColor != Color.Transparent &&
                current.BackColor.A == byte.MaxValue)
            {
                return current.BackColor;
            }
        }

        return fallback;
    }
}
