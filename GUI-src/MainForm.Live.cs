using System.Globalization;

namespace Audi100Diag;

// Zakładka "Dane na żywo": grupy pomiarowe (GroupRead) w pętli.
internal sealed partial class MainForm
{
    private readonly NumericUpDown _numGroup = Ui.Number(1, 255, 1, 70);
    private readonly ValueTile[] _tiles = [new("Pole 1"), new("Pole 2"), new("Pole 3"), new("Pole 4")];
    private readonly LineChart _liveChart = new() { Dock = DockStyle.Fill, Title = "Pole 1" };
    private readonly Label _lblLiveInfo = Ui.Paragraph("", 640);
    private Button? _btnLive;

    private bool _liveRunning;
    private bool _liveRestart;
    private int _liveGroup;
    private int _chartField;
    private int _liveSamples;
    private DateTime _firstSampleAt;

    private TabPage BuildLiveTab()
    {
        var page = Ui.Page("Dane na żywo");

        _btnLive = Ui.Button("▶  Start", async (_, _) => await ToggleLiveAsync(), ButtonKind.Primary);
        _btnLive.Margin = new Padding(8, 18, 8, 0);
        var prev = Ui.Button("◀", (_, _) => { if (_numGroup.Value > _numGroup.Minimum) _numGroup.Value--; });
        var next = Ui.Button("▶", (_, _) => { if (_numGroup.Value < _numGroup.Maximum) _numGroup.Value++; });
        prev.Margin = new Padding(0, 0, 4, 0);
        next.Margin = new Padding(4, 0, 0, 0);
        _numGroup.Margin = new Padding(0, 5, 0, 0);
        _numGroup.ValueChanged += (_, _) => OnGroupChanged();

        var groupPicker = Ui.Row(prev, _numGroup, next);
        groupPicker.WrapContents = false;
        _lblLiveInfo.Margin = new Padding(12, 26, 0, 0);
        var top = Ui.Row(Ui.Field("Grupa pomiarowa", groupPicker), _btnLive, _lblLiveInfo);
        top.WrapContents = false;
        top.Dock = DockStyle.Fill;

        var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
        for (var i = 0; i < _tiles.Length; i++)
        {
            var index = i;
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            _tiles[i].Dock = DockStyle.Fill;
            _tiles[i].Margin = new Padding(0, 0, i < _tiles.Length - 1 ? 10 : 0, 0);
            _tiles[i].Click += (_, _) => SelectChartField(index);
            tiles.Controls.Add(_tiles[i], i, 0);
        }
        _tiles[0].Selected = true;

        var note = Ui.Paragraph(
            "Kliknij kafelek, żeby pokazać go na wykresie. Zmiana grupy w trakcie pomiaru łączy się ponownie (ok. 3 s). " +
            "Jednostki podaje sam sterownik.", 900);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _liveChart.Margin = new Padding(0, 10, 0, 6);
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(tiles, 0, 1);
        layout.Controls.Add(_liveChart, 0, 2);
        layout.Controls.Add(note, 0, 3);
        page.Controls.Add(layout);
        return page;
    }

    private async Task ToggleLiveAsync()
    {
        if (_liveRunning)
        {
            _liveRestart = false;
            _runner?.RequestStop(clean: true);
            return;
        }
        if (_runner is null || _runner.IsBusy)
        {
            return;
        }

        _liveRunning = true;
        try
        {
            do
            {
                _liveRestart = false;
                _liveGroup = (int)_numGroup.Value;
                _liveSamples = 0;
                foreach (var tile in _tiles)
                {
                    tile.Value = "—";
                }
                _liveChart.Clear();
                _liveChart.Title = $"Grupa {_liveGroup} – pole {_chartField + 1}";
                _lblLiveInfo.Text = $"Łączę się, grupa {_liveGroup}…";

                var result = await RunToolAsync(
                    $"Dane na żywo – grupa {_liveGroup}",
                    ["GroupRead", _liveGroup.ToString(CultureInfo.InvariantCulture)],
                    isLoop: true, samples: 1_000_000, onLine: OnLiveLine);
                if (result is null)
                {
                    break;
                }
                if (result.Problem is not null)
                {
                    _lblLiveInfo.Text = result.Problem;
                    break;
                }
            }
            while (_liveRestart && !_closeRequested);
        }
        finally
        {
            _liveRunning = false;
            if (_lblLiveInfo.Text.StartsWith("Łączę", StringComparison.Ordinal))
            {
                _lblLiveInfo.Text = "";
            }
            UpdateUiState();
        }
    }

    private void OnGroupChanged()
    {
        if (_liveRunning && (int)_numGroup.Value != _liveGroup)
        {
            _liveRestart = true;
            _runner?.RequestStop(clean: true);
        }
    }

    private void OnLiveLine(string line)
    {
        var reading = KwOutput.ParseGroup(line);
        if (reading is null)
        {
            return;
        }
        if (!reading.Available)
        {
            foreach (var tile in _tiles)
            {
                tile.Value = "—";
            }
            _lblLiveInfo.Text = $"Grupa {reading.Group} jest niedostępna w tym sterowniku.";
            return;
        }

        if (_liveSamples++ == 0)
        {
            _firstSampleAt = DateTime.Now;
        }
        for (var i = 0; i < _tiles.Length; i++)
        {
            // Narzędzie formatuje część wartości bez zaokrąglenia (np. 985,6000000000001 rpm).
            _tiles[i].Value = i >= reading.Fields.Count
                ? "—"
                : KwOutput.TryNumber(reading.Fields[i], out var v, out var u)
                    ? $"{v.ToString("0.#", CultureInfo.CurrentCulture)} {u}".Trim()
                    : reading.Fields[i];
        }
        if (_chartField < reading.Fields.Count
            && KwOutput.TryNumber(reading.Fields[_chartField], out var value, out var unit))
        {
            _liveChart.Unit = unit;
            _liveChart.Add(value);
        }

        var seconds = Math.Max(0.5, (DateTime.Now - _firstSampleAt).TotalSeconds);
        _lblLiveInfo.Text = $"Grupa {reading.Group}  •  odczytów: {_liveSamples}  •  {(_liveSamples - 1) / seconds:0.0} na sekundę";
    }

    private void SelectChartField(int index)
    {
        _chartField = index;
        for (var i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].Selected = i == index;
        }
        _liveChart.Clear();
        _liveChart.Unit = "";
        _liveChart.Title = $"Grupa {(int)_numGroup.Value} – pole {index + 1}";
    }

    private void UpdateLiveButton()
    {
        if (_btnLive is null)
        {
            return;
        }
        _btnLive.Text = _liveRunning ? "■  Zatrzymaj" : "▶  Start";
        _btnLive.Enabled = _runner is not null && !_closeRequested && (_liveRunning || !_runner.IsBusy);
    }
}
