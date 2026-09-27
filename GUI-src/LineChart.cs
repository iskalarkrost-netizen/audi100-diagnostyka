using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Audi100Diag;

/// <summary>Prosty wykres przebiegu w czasie (bez zewnętrznych bibliotek).</summary>
internal sealed class LineChart : Control
{
    private readonly List<(DateTime Time, double Value)> _points = [];

    public LineChart()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Font = Theme.Small;
        MinimumSize = new Size(240, 160);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Capacity { get; set; } = 900;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Unit { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "Brak danych – uruchom pomiar";

    public void Add(double value)
    {
        _points.Add((DateTime.Now, value));
        if (_points.Count > Capacity)
        {
            _points.RemoveRange(0, _points.Count - Capacity);
        }
        Invalidate();
    }

    public void Clear()
    {
        _points.Clear();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using var borderPen = new Pen(Theme.Border);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        using var titleBrush = new SolidBrush(Theme.Text);
        using var mutedBrush = new SolidBrush(Theme.Muted);
        var title = string.IsNullOrEmpty(Unit) ? Title : $"{Title} [{Unit}]";
        g.DrawString(title, Theme.Bold, titleBrush, 12, 8);

        var plot = new Rectangle(64, 48, Width - 64 - 20, Height - 48 - 30);
        if (plot.Width < 20 || plot.Height < 20)
        {
            return;
        }

        if (_points.Count == 0)
        {
            var size = g.MeasureString(EmptyText, Font);
            g.DrawString(EmptyText, Font, mutedBrush,
                plot.Left + (plot.Width - size.Width) / 2, plot.Top + (plot.Height - size.Height) / 2);
            return;
        }

        var min = _points.Min(p => p.Value);
        var max = _points.Max(p => p.Value);
        if (max - min < 1e-9)
        {
            min -= 1;
            max += 1;
        }
        var pad = (max - min) * 0.12;
        min -= pad;
        max += pad;

        // Siatka i opisy osi Y
        using var gridPen = new Pen(Color.FromArgb(234, 236, 239));
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Top + plot.Height * i / 4f;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var v = max - (max - min) * i / 4;
            var label = v.ToString(Math.Abs(max - min) < 10 ? "0.0" : "0", CultureInfo.CurrentCulture);
            var size = g.MeasureString(label, Font);
            g.DrawString(label, Font, mutedBrush, plot.Left - size.Width - 6, y - size.Height / 2);
        }

        var t0 = _points[0].Time;
        var span = Math.Max(1.0, (_points[^1].Time - t0).TotalSeconds);
        g.DrawString($"−{span:0} s", Font, mutedBrush, plot.Left, plot.Bottom + 6);
        var now = "teraz";
        g.DrawString(now, Font, mutedBrush, plot.Right - g.MeasureString(now, Font).Width, plot.Bottom + 6);

        PointF Map((DateTime Time, double Value) p) => new(
            (float)(plot.Left + (p.Time - t0).TotalSeconds / span * plot.Width),
            (float)(plot.Bottom - (p.Value - min) / (max - min) * plot.Height));

        using var linePen = new Pen(Theme.Accent, 2f) { LineJoin = LineJoin.Round };
        if (_points.Count > 1)
        {
            g.DrawLines(linePen, _points.Select(Map).ToArray());
        }

        var last = Map(_points[^1]);
        using var dot = new SolidBrush(Theme.Accent);
        g.FillEllipse(dot, last.X - 4, last.Y - 4, 8, 8);
        var lastText = _points[^1].Value.ToString("0.#", CultureInfo.CurrentCulture);
        var lastSize = g.MeasureString(lastText, Theme.Bold);
        var lx = Math.Min(last.X - lastSize.Width / 2, plot.Right - lastSize.Width);
        var ly = last.Y - lastSize.Height - 6 < plot.Top ? last.Y + 6 : last.Y - lastSize.Height - 6;
        g.DrawString(lastText, Theme.Bold, titleBrush, lx, ly);
    }
}

/// <summary>Kafelek z jedną wartością pomiarową; kliknięcie wybiera go na wykres.</summary>
internal sealed class ValueTile : CardPanel
{
    private readonly Label _caption = Ui.Caption("");
    private readonly Label _value = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        Font = Theme.H1,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "—",
    };
    private bool _selected;

    public ValueTile(string caption)
    {
        _caption.Text = caption;
        _caption.Dock = DockStyle.Top;
        Padding = new Padding(12, 8, 12, 8);
        Height = 88;
        Cursor = Cursors.Hand;
        Controls.Add(_value);
        Controls.Add(_caption);
        _value.Click += (_, _) => OnClick(EventArgs.Empty);
        _caption.Click += (_, _) => OnClick(EventArgs.Empty);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            BorderColor = value ? Theme.Accent : Theme.Border;
            BackColor = value ? Theme.AccentSoft : Theme.Panel;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption.Text;
        set => _caption.Text = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value.Text;
        set => _value.Text = value;
    }
}
