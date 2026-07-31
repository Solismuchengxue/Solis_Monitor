#nullable enable

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using LibreHardwareMonitor.UI.Themes;

namespace LibreHardwareMonitor.UI;

internal enum SolisDialogKind
{
    Information,
    Success,
    Warning,
    Error,
    Danger
}

internal enum SolisButtonKind
{
    Primary,
    Secondary,
    Ghost,
    Danger
}

internal readonly record struct SolisUiPalette(
    Color Canvas,
    Color Surface,
    Color SurfaceRaised,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color Accent,
    Color Success,
    Color Warning,
    Color Danger)
{
    public static SolisUiPalette Current
    {
        get
        {
            Color background = Theme.Current.BackgroundColor;
            Color foreground = Theme.Current.ForegroundColor;
            bool dark = IsDark(background);
            return new SolisUiPalette(
                dark ? Color.FromArgb(20, 23, 34) : Color.FromArgb(244, 247, 251),
                dark ? Color.FromArgb(31, 36, 50) : Color.White,
                dark ? Color.FromArgb(39, 45, 61) : Color.FromArgb(237, 242, 248),
                dark ? Color.FromArgb(61, 70, 91) : Color.FromArgb(205, 214, 226),
                foreground,
                Blend(background, foreground, dark ? 0.66f : 0.60f),
                SystemColors.Highlight,
                Color.FromArgb(72, 199, 116),
                Color.FromArgb(242, 176, 52),
                Color.FromArgb(221, 72, 85));
        }
    }

    private static bool IsDark(Color color)
    {
        double luminance =
            color.R * 0.2126 +
            color.G * 0.7152 +
            color.B * 0.0722;
        return luminance < 128;
    }

    internal static Color Blend(Color first, Color second, float amount)
    {
        amount = Math.Max(0, Math.Min(1, amount));
        return Color.FromArgb(
            (int)Math.Round(first.R + (second.R - first.R) * amount),
            (int)Math.Round(first.G + (second.G - first.G) * amount),
            (int)Math.Round(first.B + (second.B - first.B) * amount));
    }
}

internal class SolisDialogForm : Form
{
    protected SolisDialogForm(string title, Size clientSize)
    {
        Palette = SolisUiPalette.Current;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Palette.Canvas;
        ClientSize = clientSize;
        Font = new Font("Segoe UI Variable Text", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        ForeColor = Palette.TextPrimary;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = title;
    }

    protected SolisUiPalette Palette { get; }

    protected TableLayoutPanel CreateDialogHeader(
        string glyph,
        string title,
        string description)
    {
        var header = new TableLayoutPanel
        {
            BackColor = Palette.Canvas,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new SolisDialogIcon(glyph, Palette.Accent)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 12, 8)
        }, 0, 0);

        var text = new TableLayoutPanel
        {
            BackColor = Palette.Canvas,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        Label titleLabel = CreateLabel(
            title,
            17,
            FontStyle.Bold,
            Palette.TextPrimary,
            ContentAlignment.BottomLeft);
        titleLabel.BackColor = Palette.Canvas;
        Label descriptionLabel = CreateLabel(
            description,
            9,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.TopLeft);
        descriptionLabel.BackColor = Palette.Canvas;
        text.Controls.Add(titleLabel, 0, 0);
        text.Controls.Add(descriptionLabel, 0, 1);
        header.Controls.Add(text, 1, 0);
        return header;
    }

    protected SolisDialogCard CreateCard(Padding? padding = null)
    {
        return new SolisDialogCard(Palette)
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = padding ?? new Padding(18, 16, 18, 16)
        };
    }

    protected Label CreateLabel(
        string text,
        float size = 9.5f,
        FontStyle style = FontStyle.Regular,
        Color? color = null,
        ContentAlignment alignment = ContentAlignment.MiddleLeft)
    {
        return new Label
        {
            AutoEllipsis = true,
            BackColor = Palette.Surface,
            Dock = DockStyle.Fill,
            Font = new Font(
                size >= 13 ? "Segoe UI Variable Display" : "Segoe UI Variable Text",
                size,
                style,
                GraphicsUnit.Point),
            ForeColor = color ?? Palette.TextPrimary,
            Margin = Padding.Empty,
            Text = text,
            TextAlign = alignment
        };
    }

    protected SolisDialogButton CreateButton(
        string text,
        SolisButtonKind kind = SolisButtonKind.Secondary,
        int width = 144)
    {
        return new SolisDialogButton(Palette, kind)
        {
            AutoSize = false,
            Cursor = Cursors.Hand,
            Font = new Font(
                "Segoe UI Variable Text",
                9.5f,
                FontStyle.Regular,
                GraphicsUnit.Point),
            Height = 40,
            Margin = new Padding(10, 0, 0, 0),
            Text = text,
            Width = width
        };
    }

    protected void StyleTextBox(TextBoxBase textBox)
    {
        textBox.BackColor = Palette.SurfaceRaised;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.ForeColor = Palette.TextPrimary;
    }

    protected void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.BackColor = Palette.Surface;
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.ForeColor = Palette.TextPrimary;
    }

    protected FlowLayoutPanel CreateFooter(params Control[] controls)
    {
        var footer = new FlowLayoutPanel
        {
            BackColor = Palette.Canvas,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = Padding.Empty,
            Padding = new Padding(0, 10, 0, 0),
            WrapContents = false
        };
        footer.Controls.AddRange(controls);
        return footer;
    }
}

internal static class SolisDialog
{
    public static DialogResult Show(
        IWin32Window? owner,
        string title,
        string message,
        SolisDialogKind kind = SolisDialogKind.Information,
        string primaryText = "确定",
        string? secondaryText = null,
        bool cancelIsDefault = false)
    {
        using var dialog = new SolisMessageDialog(
            title,
            message,
            kind,
            primaryText,
            secondaryText,
            cancelIsDefault);
        return dialog.ShowDialog(owner);
    }

    public static DialogResult Confirm(
        IWin32Window? owner,
        string title,
        string message,
        string primaryText = "确认",
        string secondaryText = "取消",
        bool danger = false)
    {
        return Show(
            owner,
            title,
            message,
            danger ? SolisDialogKind.Danger : SolisDialogKind.Warning,
            primaryText,
            secondaryText,
            true);
    }
}

internal static class SolisLegacyDialogTheme
{
    public static void Apply(Form form)
    {
        SolisUiPalette palette = SolisUiPalette.Current;
        form.BackColor = palette.Canvas;
        form.ForeColor = palette.TextPrimary;
        form.Font = new Font(
            "Segoe UI Variable Text",
            9.5f,
            FontStyle.Regular,
            GraphicsUnit.Point);
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowIcon = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;

        foreach (Control control in Descendants(form))
        {
            control.ForeColor = palette.TextPrimary;
            switch (control)
            {
                case Button button:
                    bool primary =
                        ReferenceEquals(form.AcceptButton, button) ||
                        button.DialogResult is DialogResult.OK or DialogResult.Yes;
                    button.BackColor = primary ? palette.Accent : palette.SurfaceRaised;
                    button.ForeColor = primary ? Color.White : palette.TextPrimary;
                    button.Cursor = Cursors.Hand;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = primary
                        ? palette.Accent
                        : palette.Border;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.MouseOverBackColor =
                        SolisUiPalette.Blend(
                            button.BackColor,
                            Color.White,
                            primary ? 0.08f : 0.10f);
                    button.FlatAppearance.MouseDownBackColor =
                        SolisUiPalette.Blend(button.BackColor, Color.Black, 0.16f);
                    button.MinimumSize = new Size(112, 36);
                    button.UseVisualStyleBackColor = false;
                    break;
                case TextBoxBase textBox:
                    textBox.BackColor = palette.SurfaceRaised;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox comboBox:
                    comboBox.BackColor = palette.SurfaceRaised;
                    comboBox.FlatStyle = FlatStyle.Flat;
                    break;
                case NumericUpDown numeric:
                    numeric.BackColor = palette.SurfaceRaised;
                    numeric.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = palette.Surface;
                    grid.BorderStyle = BorderStyle.None;
                    grid.EnableHeadersVisualStyles = false;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = palette.SurfaceRaised;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.TextPrimary;
                    grid.DefaultCellStyle.BackColor = palette.Surface;
                    grid.DefaultCellStyle.ForeColor = palette.TextPrimary;
                    grid.DefaultCellStyle.SelectionBackColor =
                        SolisUiPalette.Blend(palette.SurfaceRaised, palette.Accent, 0.34f);
                    grid.DefaultCellStyle.SelectionForeColor = palette.TextPrimary;
                    grid.GridColor = palette.Border;
                    grid.RowHeadersDefaultCellStyle.BackColor = palette.SurfaceRaised;
                    grid.RowHeadersDefaultCellStyle.ForeColor = palette.TextSecondary;
                    break;
                case LinkLabel link:
                    link.BackColor = link.Parent?.BackColor ?? palette.Canvas;
                    link.LinkColor = palette.Accent;
                    link.VisitedLinkColor = palette.Accent;
                    break;
                case GroupBox:
                case Panel:
                    control.BackColor = palette.Surface;
                    break;
                case Label:
                case CheckBox:
                case RadioButton:
                    control.BackColor = control.Parent?.BackColor ?? palette.Canvas;
                    break;
                default:
                    control.BackColor = control.Parent?.BackColor ?? palette.Canvas;
                    break;
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(
        Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child))
                yield return nested;
        }
    }
}

internal sealed class SolisMessageDialog : SolisDialogForm
{
    public SolisMessageDialog(
        string title,
        string message,
        SolisDialogKind kind,
        string primaryText,
        string? secondaryText,
        bool cancelIsDefault)
        : base(title, GetDialogSize(message))
    {
        string glyph = kind switch
        {
            SolisDialogKind.Success => "\uE73E",
            SolisDialogKind.Warning => "\uE7BA",
            SolisDialogKind.Error => "\uEA39",
            SolisDialogKind.Danger => "\uE7BA",
            _ => "\uE946"
        };
        Color accent = kind switch
        {
            SolisDialogKind.Success => Palette.Success,
            SolisDialogKind.Warning => Palette.Warning,
            SolisDialogKind.Error => Palette.Danger,
            SolisDialogKind.Danger => Palette.Danger,
            _ => Palette.Accent
        };

        var root = new TableLayoutPanel
        {
            BackColor = Palette.Canvas,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 18),
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        TableLayoutPanel header = CreateDialogHeader(glyph, title, GetKindDescription(kind));
        foreach (SolisDialogIcon icon in FindControls<SolisDialogIcon>(header))
            icon.AccentColor = accent;
        root.Controls.Add(header, 0, 0);

        SolisDialogCard card = CreateCard();
        Label body = CreateLabel(
            message,
            10,
            FontStyle.Regular,
            Palette.TextPrimary,
            ContentAlignment.MiddleLeft);
        body.AutoEllipsis = false;
        body.BackColor = Palette.Surface;
        card.Controls.Add(body);
        root.Controls.Add(card, 0, 1);

        SolisDialogButton primary = CreateButton(
            primaryText,
            kind == SolisDialogKind.Danger
                ? SolisButtonKind.Danger
                : SolisButtonKind.Primary);
        primary.DialogResult = DialogResult.OK;
        Button? secondary = null;
        if (!string.IsNullOrWhiteSpace(secondaryText))
        {
            secondary = CreateButton(secondaryText, SolisButtonKind.Secondary);
            secondary.DialogResult = DialogResult.Cancel;
        }
        root.Controls.Add(
            secondary is null
                ? CreateFooter(primary)
                : CreateFooter(primary, secondary),
            0,
            2);

        Controls.Add(root);
        AcceptButton = cancelIsDefault && secondary is not null ? secondary : primary;
        CancelButton = secondary;
        ActiveControl = cancelIsDefault && secondary is not null ? secondary : primary;
    }

    private static string GetKindDescription(SolisDialogKind kind) => kind switch
    {
        SolisDialogKind.Success => "操作已完成",
        SolisDialogKind.Warning => "请确认后继续",
        SolisDialogKind.Error => "操作未完成",
        SolisDialogKind.Danger => "此操作会更改现有配置",
        _ => "Solis Monitor"
    };

    private static Size GetDialogSize(string message)
    {
        int length = message?.Length ?? 0;
        return length switch
        {
            > 220 => new Size(640, 500),
            > 110 => new Size(620, 430),
            _ => new Size(540, 320)
        };
    }

    private static System.Collections.Generic.IEnumerable<T> FindControls<T>(
        Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (T nested in FindControls<T>(child))
                yield return nested;
        }
    }
}

internal sealed class SolisDialogCard : Panel
{
    private readonly SolisUiPalette _palette;

    public SolisDialogCard(SolisUiPalette palette)
    {
        _palette = palette;
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        using GraphicsPath path = CreateRoundedRectangle(bounds, 10);
        using var brush = new SolidBrush(_palette.Surface);
        using var pen = new Pen(_palette.Border);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width <= 1 || Height <= 1)
            return;

        using GraphicsPath path = CreateRoundedRectangle(
            new Rectangle(0, 0, Width, Height),
            10);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class SolisDialogButton : Button
{
    private readonly SolisUiPalette _palette;
    private readonly SolisButtonKind _kind;
    private bool _hovered;
    private bool _pressed;

    public SolisDialogButton(SolisUiPalette palette, SolisButtonKind kind)
    {
        _palette = palette;
        _kind = kind;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color baseFill = _kind switch
        {
            SolisButtonKind.Primary => _palette.Accent,
            SolisButtonKind.Danger => _palette.Danger,
            SolisButtonKind.Ghost => _palette.Canvas,
            _ => _palette.SurfaceRaised
        };
        Color foreground = _kind is SolisButtonKind.Primary or SolisButtonKind.Danger
            ? Color.White
            : _palette.TextPrimary;
        Color border = _kind switch
        {
            SolisButtonKind.Primary => _palette.Accent,
            SolisButtonKind.Danger => _palette.Danger,
            SolisButtonKind.Ghost => _palette.Border,
            _ => _palette.Accent
        };
        if (!Enabled)
        {
            baseFill = SolisUiPalette.Blend(_palette.Surface, _palette.TextSecondary, 0.10f);
            foreground = _palette.TextSecondary;
            border = _palette.Border;
        }
        else if (_pressed)
        {
            baseFill = SolisUiPalette.Blend(baseFill, Color.Black, 0.18f);
        }
        else if (_hovered)
        {
            baseFill = SolisUiPalette.Blend(baseFill, Color.White, 0.08f);
        }

        e.Graphics.Clear(Parent?.BackColor ?? _palette.Canvas);
        Rectangle bounds = ClientRectangle;
        bounds.Width -= 1;
        bounds.Height -= 1;
        using GraphicsPath path = CreateRoundedRectangle(bounds, 7);
        using var brush = new SolidBrush(baseFill);
        using var pen = new Pen(border);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        Rectangle textBounds = ClientRectangle;
        textBounds.X += Padding.Left;
        textBounds.Y += Padding.Top;
        textBounds.Width = Math.Max(0, textBounds.Width - Padding.Horizontal);
        textBounds.Height = Math.Max(0, textBounds.Height - Padding.Vertical);
        TextFormatFlags flags = TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft =>
                TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight =>
                TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        flags |= TextAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight =>
                TextFormatFlags.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight =>
                TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter
        };
        flags |= Text.Contains('\n')
            ? TextFormatFlags.WordBreak
            : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            foreground,
            Color.Transparent,
            flags);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class SolisDialogIcon : Control
{
    private readonly Font _font = new(
        "Segoe Fluent Icons",
        17,
        FontStyle.Regular,
        GraphicsUnit.Point);

    public SolisDialogIcon(string glyph, Color accentColor)
    {
        Glyph = glyph;
        AccentColor = accentColor;
        DoubleBuffered = true;
    }

    public Color AccentColor { get; set; }

    public string Glyph { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int diameter = Math.Max(0, Math.Min(ClientSize.Width, ClientSize.Height) - 4);
        var badge = new Rectangle(
            (ClientSize.Width - diameter) / 2,
            (ClientSize.Height - diameter) / 2,
            diameter,
            diameter);
        using var brush = new SolidBrush(Color.FromArgb(44, AccentColor));
        e.Graphics.FillEllipse(brush, badge);
        TextRenderer.DrawText(
            e.Graphics,
            Glyph,
            _font,
            badge,
            AccentColor,
            Color.Transparent,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _font.Dispose();
        base.Dispose(disposing);
    }
}
