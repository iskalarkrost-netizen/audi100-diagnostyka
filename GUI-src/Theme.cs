using System.ComponentModel;

namespace Audi100Diag;

internal static class Theme
{
    public static readonly Color Back = Color.FromArgb(243, 244, 246);
    public static readonly Color Panel = Color.White;
    public static readonly Color Text = Color.FromArgb(31, 35, 40);
    public static readonly Color Muted = Color.FromArgb(96, 103, 112);
    public static readonly Color Border = Color.FromArgb(214, 217, 222);
    public static readonly Color Accent = Color.FromArgb(0, 95, 184);
    public static readonly Color AccentSoft = Color.FromArgb(229, 239, 250);
    public static readonly Color Ok = Color.FromArgb(26, 127, 55);
    public static readonly Color Warn = Color.FromArgb(166, 99, 0);
    public static readonly Color WarnSoft = Color.FromArgb(255, 244, 222);
    public static readonly Color Danger = Color.FromArgb(193, 39, 45);
    public static readonly Color DangerSoft = Color.FromArgb(253, 234, 234);

    public static readonly Font Ui = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font H2 = new("Segoe UI Semibold", 12f);
    public static readonly Font H1 = new("Segoe UI Semibold", 16f);
    public static readonly Font Big = new("Segoe UI Semibold", 30f);
    public static readonly Font Mono = new("Consolas", 9.75f);
}

internal enum ButtonKind { Normal, Primary, Danger }

/// <summary>Panel z cienką ramką - podstawowy "kafelek" interfejsu.</summary>
internal class CardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Border;

    public CardPanel()
    {
        BackColor = Theme.Panel;
        Padding = new Padding(16);
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal static class Ui
{
    public static Button Button(string text, EventHandler onClick, ButtonKind kind = ButtonKind.Normal)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 34),
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(0, 0, 8, 8),
            FlatStyle = FlatStyle.Flat,
            Font = Theme.Bold,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        switch (kind)
        {
            case ButtonKind.Primary:
                b.BackColor = Theme.Accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = Theme.Accent;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 80, 160);
                break;
            case ButtonKind.Danger:
                b.BackColor = Theme.Panel;
                b.ForeColor = Theme.Danger;
                b.FlatAppearance.BorderColor = Theme.Danger;
                b.FlatAppearance.MouseOverBackColor = Theme.DangerSoft;
                break;
            default:
                b.BackColor = Theme.Panel;
                b.ForeColor = Theme.Text;
                b.FlatAppearance.BorderColor = Theme.Border;
                b.FlatAppearance.MouseOverBackColor = Theme.AccentSoft;
                break;
        }
        // Płaski przycisk w stanie nieaktywnym zostawia kolory aktywnego - wyszarzamy ręcznie.
        var enabledBack = b.BackColor;
        var enabledFore = b.ForeColor;
        var enabledBorder = b.FlatAppearance.BorderColor;
        b.EnabledChanged += (_, _) =>
        {
            b.BackColor = b.Enabled ? enabledBack : Theme.Back;
            b.ForeColor = b.Enabled ? enabledFore : Theme.Muted;
            b.FlatAppearance.BorderColor = b.Enabled ? enabledBorder : Theme.Border;
        };
        b.Click += onClick;
        return b;
    }

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Theme.Muted,
        Font = Theme.Small,
        Margin = new Padding(0, 0, 0, 2),
    };

    public static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.H2,
        Margin = new Padding(0, 0, 0, 8),
    };

    public static Label Paragraph(string text, int maxWidth = 520) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(maxWidth, 0),
        ForeColor = Theme.Muted,
        Margin = new Padding(0, 0, 0, 8),
    };

    public static Label Value(string text = "—", Font? font = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Theme.Bold,
        Margin = new Padding(0, 0, 0, 10),
    };

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
        };
        row.Controls.AddRange(controls);
        return row;
    }

    public static FlowLayoutPanel Column(params Control[] controls)
    {
        var col = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
        };
        col.Controls.AddRange(controls);
        return col;
    }

    /// <summary>Podpis nad kontrolką (np. "Kanał" nad polem liczbowym).</summary>
    public static Control Field(string caption, Control control)
    {
        var col = Column(Caption(caption), control);
        col.Margin = new Padding(0, 0, 14, 6);
        return col;
    }

    public static NumericUpDown Number(int min, int max, int value, int width = 80) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Width = width,
        TextAlign = HorizontalAlignment.Right,
        Margin = new Padding(0, 0, 0, 0),
    };

    /// <summary>
    /// Układa karty jedna pod drugą z odstępem (dokowanie ignoruje Margin).
    /// Kolejność argumentów = kolejność od góry.
    /// </summary>
    public static Panel Stack(params Control[] cards)
    {
        var stack = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        // Dock=Top układa od ostatnio dodanej - dodajemy od dołu.
        for (var i = cards.Length - 1; i >= 0; i--)
        {
            cards[i].Dock = DockStyle.Top;
            stack.Controls.Add(cards[i]);
            if (i > 0)
            {
                stack.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 12 });
            }
        }
        return stack;
    }

    public static TabPage Page(string title)
    {
        var page = new TabPage(title)
        {
            BackColor = Theme.Back,
            Padding = new Padding(12),
        };
        return page;
    }
}
