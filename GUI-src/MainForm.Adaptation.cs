using System.Globalization;
using System.Text;

namespace Audi100Diag;

// Zakładka "Obroty / adaptacja": odczyt i podgląd kanału, test bez zapisu, zapis.
internal sealed partial class MainForm
{
    private const string EngineEcu = "4A0907473A";
    private const int RpmWindowSize = 60;

    private readonly NumericUpDown _numChannel = Ui.Number(0, 99, 1, 70);
    private readonly Label _lblRpm = new() { AutoSize = true, Font = Theme.Big, Text = "—", Margin = new Padding(0, 0, 0, 0) };
    private readonly Label _lblChannelValue = Ui.Value("—", Theme.H1);
    private readonly Label _lblChannelBytes = Ui.Caption("");
    private readonly Label _lblRpmStats = Ui.Paragraph("", 360);
    private readonly Label _lblAdaptHint = Ui.Paragraph("", 360);
    private readonly LineChart _rpmChart = new() { Dock = DockStyle.Fill, Title = "Obroty silnika", Unit = "obr/min" };
    private readonly NumericUpDown _numTestValue = Ui.Number(0, 65535, 5, 80);
    private readonly NumericUpDown _numTestSeconds = Ui.Number(5, 300, 30, 70);
    private readonly List<double> _rpmWindow = [];
    private Button? _btnWatch;

    private bool _watchRunning;
    private bool _watchRestart;
    private int _watchChannel;

    private TabPage BuildAdaptationTab()
    {
        var page = Ui.Page("Obroty / adaptacja");

        var btnRead = Ui.Button("Odczytaj",async (_, _) => await ReadChannelAsync((int)_numChannel.Value, $"Odczyt kanału {(int)_numChannel.Value}"), ButtonKind.Primary);
        _btnWatch = Ui.Button("▶  Podgląd na żywo", async (_, _) => await ToggleWatchAsync());
        _readActions.Add(btnRead);
        _numChannel.ValueChanged += (_, _) => OnChannelChanged();

        var readCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var readCol = Ui.Column(
            Ui.Heading("Odczyt (bez zmian w sterowniku)"),
            Ui.Field("Kanał adaptacji", _numChannel),
            Ui.Row(btnRead, _btnWatch),
            Ui.Caption("Obroty [obr/min]"), _lblRpm,
            Ui.Caption("Wartość kanału"), _lblChannelValue, _lblChannelBytes,
            _lblRpmStats);
        readCard.Controls.Add(readCol);

        var btnProbe = Ui.Button("Testuj bez zapisu…", async (_, _) => await ProbeAsync());
        var btnSave = Ui.Button("Zapisz na stałe…", async (_, _) => await SaveAdaptationFlowAsync(), ButtonKind.Danger);
        _writeActions.AddRange([btnProbe, btnSave]);

        var writeCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var writeCol = Ui.Column(
            Ui.Heading("Zmiana wartości (tryb serwisowy)"),
            Ui.Row(Ui.Field("Nowa wartość", _numTestValue), Ui.Field("Czas testu [s]", _numTestSeconds)),
            Ui.Row(btnProbe, btnSave),
            Ui.Paragraph(
                "Test podtrzymuje wartość i pokazuje obroty, ale nie wysyła zapisu. " +
                "Przed i po teście program odczytuje kanał i w razie potrzeby proponuje przywrócenie. " +
                "Po zapisie sprawdza kodowanie sterownika.", 360));
        writeCard.Controls.Add(writeCol);

        var hintCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.WarnSoft, BorderColor = Color.FromArgb(240, 214, 160) };
        hintCard.Controls.Add(_lblAdaptHint);
        _lblAdaptHint.ForeColor = Theme.Text;

        var left = Ui.Stack(readCard, writeCard, hintCard);
        left.Margin = new Padding(0, 0, 12, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(_rpmChart, 1, 0);
        page.Controls.Add(layout);

        RefreshAdaptationHints();
        return page;
    }

    private void RefreshAdaptationHints()
    {
        var channel = (int)_numChannel.Value;
        if (_ident?.PartNumber == EngineEcu && channel == 1)
        {
            _lblAdaptHint.Text =
                $"{EngineEcu}, kanał 01 = obroty biegu jałowego (pomiary z 27.09.2026):\n" +
                "•  zakres 0–7 – większe wartości sterownik przycina do 7\n" +
                "•  fabrycznie 5 ≈ 825 obr/min, 0 ≈ 781 obr/min na ciepłym silniku\n" +
                "•  ok. 6 obr/min na krok – 680 obr/min jest poza zasięgiem kanału\n" +
                "•  obroty ustawiaj na rozgrzanym silniku (zimny ma własną mapę)";
        }
        else
        {
            _lblAdaptHint.Text =
                "Znaczenie kanału zależy od sterownika. Obroty są pokazywane, gdy odpowiedź " +
                "sterownika zawiera pomiar typu 1 (obr/min) – tak jest w kanale 01 silnika 4A0907473A.";
        }
        if (_runner is { SupportsStopFile: false })
        {
            _lblAdaptHint.Text += "\n\nPodgląd na żywo wymaga kw1281test-FIXED3.exe.";
        }
    }

    private void OnChannelChanged()
    {
        RefreshAdaptationHints();
        if (_watchRunning && (int)_numChannel.Value != _watchChannel)
        {
            _watchRestart = true;
            _runner?.RequestStop(clean: true);
        }
    }

    private async Task<AdaptationReading?> ReadChannelAsync(int channel, string title)
    {
        var result = await RunToolAsync(title, ["AdaptationRead", channel.ToString(CultureInfo.InvariantCulture)]);
        if (result is null || result.Stopped || result.Problem is not null)
        {
            return null;
        }
        var reading = KwOutput.LastAdaptation(result.Lines);
        if (reading is not null)
        {
            ShowAdaptation(reading);
        }
        return reading;
    }

    private async Task ToggleWatchAsync()
    {
        if (_watchRunning)
        {
            _watchRestart = false;
            _runner?.RequestStop(clean: true);
            return;
        }
        if (_runner is null || _runner.IsBusy || !_runner.SupportsStopFile)
        {
            return;
        }

        _watchRunning = true;
        _rpmWindow.Clear();
        _rpmChart.Clear();
        try
        {
            ToolResult? result;
            do
            {
                _watchRestart = false;
                _watchChannel = (int)_numChannel.Value;
                result = await RunToolAsync(
                    $"Podgląd kanału {_watchChannel} (tylko odczyt)",
                    ["AdaptationWatch", _watchChannel.ToString(CultureInfo.InvariantCulture), "3600"],
                    isLoop: true, onLine: OnAdaptationLine);
            }
            // Po godzinie narzędzie kończy samo - wtedy wznawiamy, chyba że użytkownik zatrzymał.
            while (result is not null && result.Problem is null && (_watchRestart || !result.Stopped) && !_closeRequested);
        }
        finally
        {
            _watchRunning = false;
            UpdateUiState();
        }
    }

    private void OnAdaptationLine(string line)
    {
        if (KwOutput.ParseAdaptation(line) is { } reading)
        {
            ShowAdaptation(reading);
        }
    }

    private void ShowAdaptation(AdaptationReading reading)
    {
        _lblChannelValue.Text = reading.Value.ToString(CultureInfo.CurrentCulture);
        _lblChannelBytes.Text = $"kanał {reading.Channel:D2}, odpowiedź: {reading.Hex}";
        if (reading.Rpm is not double rpm)
        {
            _lblRpm.Text = "—";
            return;
        }

        _lblRpm.Text = rpm.ToString("0", CultureInfo.CurrentCulture);
        _rpmChart.Add(rpm);
        _rpmWindow.Add(rpm);
        if (_rpmWindow.Count > RpmWindowSize)
        {
            _rpmWindow.RemoveAt(0);
        }
        _lblRpmStats.Text = RpmStatsText();
    }

    private string RpmStatsText() =>
        _rpmWindow.Count == 0
            ? ""
            : string.Format(CultureInfo.CurrentCulture,
                "Ostatnie {0} próbek: średnio {1:0.0}, min {2:0.0}, max {3:0.0} obr/min",
                _rpmWindow.Count, _rpmWindow.Average(), _rpmWindow.Min(), _rpmWindow.Max());

    private async Task ProbeAsync()
    {
        var channel = (int)_numChannel.Value;
        var value = (int)_numTestValue.Value;
        var seconds = (int)_numTestSeconds.Value;

        var ok = Confirm(
            $"Test kanału {channel} = {value} bez zapisu?",
            $"Sterownik: {IdentText()}\n\n" +
            $"Wartość testowa będzie podtrzymywana przez {seconds} s, a obroty pokazywane na bieżąco. " +
            "Zapis (AdaptationSave) NIE jest wysyłany.\n\n" +
            "Uwaga: w 4A0907473A ostatnia wartość testowa została w sterowniku także po rozłączeniu " +
            "(27.09.2026). Dlatego program odczyta kanał przed testem, sprawdzi go po teście " +
            "i zaproponuje przywrócenie.",
            "Testuj");
        if (!ok)
        {
            return;
        }

        var before = await ReadChannelAsync(channel, $"Odczyt kanału {channel} przed testem");
        if (before is null)
        {
            return;
        }

        _rpmWindow.Clear();
        _rpmChart.Clear();
        var result = await RunToolAsync(
            $"Test kanału {channel} = {value} (bez zapisu)",
            ["AdaptationProbe", channel.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture), seconds.ToString(CultureInfo.InvariantCulture)],
            isWrite: true, isLoop: true, onLine: OnAdaptationLine);
        if (result is null)
        {
            return;
        }
        if (KwOutput.LastAdaptation(result.Lines) is { } accepted && accepted.Value != value)
        {
            AppendLog($"ℹ Sterownik przyjął {accepted.Value} zamiast {value} – ograniczenie zakresu kanału.");
        }
        var stats = RpmStatsText();

        var after = await ReadChannelAsync(channel, $"Kontrola kanału {channel} po teście");
        if (after is null)
        {
            Warn("Nie udało się sprawdzić kanału po teście",
                $"Odczytaj kanał {channel} ręcznie. Przed testem było: {before.Value}.");
            return;
        }
        if (after.Value == before.Value)
        {
            Inform("Test zakończony", $"Kanał {channel} ma z powrotem wartość {before.Value}.\n\n{stats}");
            return;
        }

        var restore = Confirm(
            "Wartość testowa została w sterowniku",
            $"Przed testem: {before.Value}\nTeraz: {after.Value}\n\n{stats}\n\n" +
            $"Przywrócić {before.Value}? To wymaga zapisu (AdaptationSave) – po nim program sprawdzi kodowanie.",
            $"Przywróć {before.Value}");
        if (restore)
        {
            await SaveAdaptationAsync(channel, before.Value);
        }
        else
        {
            Warn("Kanał pozostaje zmieniony",
                $"Kanał {channel} = {after.Value}. Przywrócisz go przyciskiem „Zapisz na stałe…” z wartością {before.Value}.");
        }
    }

    private async Task SaveAdaptationFlowAsync()
    {
        var channel = (int)_numChannel.Value;
        var value = (int)_numTestValue.Value;

        var current = await ReadChannelAsync(channel, $"Odczyt kanału {channel} przed zapisem");
        if (current is null)
        {
            return;
        }
        if (current.Value == value)
        {
            Inform("Nic do zapisania", $"Kanał {channel} ma już wartość {value}.");
            return;
        }

        var ok = Confirm(
            $"Zapisać na stałe kanał {channel} = {value}?",
            $"Sterownik: {IdentText()}\nObecnie: {current.Value}  →  nowa wartość: {value}\n\n" +
            "Wartość trafi do pamięci trwałej sterownika i przetrwa wyłączenie zapłonu.\n\n" +
            "Znany problem 4A0907473A: zapis adaptacji przesuwa bajty kodowania. Narzędzie sprawdza " +
            "kodowanie po zapisie i samo je przywraca, a ten program dodatkowo porówna je z referencyjnym.",
            "Zapisz");
        if (ok)
        {
            await SaveAdaptationAsync(channel, value);
        }
    }

    private async Task SaveAdaptationAsync(int channel, int value)
    {
        var result = await RunToolAsync(
            $"Zapis kanału {channel} = {value}",
            ["AdaptationSave", channel.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture)],
            isWrite: true);
        if (result is null || result.Stopped)
        {
            return;
        }
        var guard = KwOutput.ParseCodingGuard(result.Lines);

        // Kontrola na nowym połączeniu - odczyt odświeża też kodowanie.
        var check = await ReadChannelAsync(channel, $"Kontrola kanału {channel} po zapisie");

        var report = new StringBuilder();
        report.AppendLine(check is null
            ? "Nie udało się odczytać kanału po zapisie."
            : check.Value == value
                ? $"✔ Kanał {channel} = {check.Value}"
                : $"⚠ Kanał {channel} = {check.Value} (wysłano {value}) – sterownik mógł przyciąć wartość do swojego zakresu.");
        report.AppendLine(guard switch
        {
            CodingGuard.Intact => "✔ Kodowanie nienaruszone",
            CodingGuard.ChangedAndRestored => "✔ Zapis zmienił kodowanie – narzędzie je przywróciło i sprawdziło",
            CodingGuard.ChangedNotRestored => "⚠ Zapis zmienił kodowanie i NIE zostało przywrócone – przejdź do zakładki Kodowanie",
            _ => "⚠ Narzędzie nie zgłosiło kontroli kodowania – sprawdź zakładkę Kodowanie",
        });
        if (CodingDiffersFromReference)
        {
            report.AppendLine("⚠ Obecne kodowanie różni się od referencyjnego – zakładka Kodowanie");
        }

        var success = check?.Value == value
                      && (guard is CodingGuard.Intact or CodingGuard.ChangedAndRestored)
                      && !CodingDiffersFromReference;
        Inform(success ? "Zapis zakończony" : "Zapis wymaga uwagi", report.ToString(), success);
    }

    private void UpdateWatchButton()
    {
        if (_btnWatch is null)
        {
            return;
        }
        _btnWatch.Text = _watchRunning ? "■  Zatrzymaj podgląd" : "▶  Podgląd na żywo";
        _btnWatch.Enabled = _runner is { SupportsStopFile: true } && !_closeRequested
                            && (_watchRunning || !_runner.IsBusy);
    }
}
