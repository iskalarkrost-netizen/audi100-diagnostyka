using System.Diagnostics;

namespace Audi100Diag;

// Zakładki "AutoScan" i "Log".
internal sealed partial class MainForm
{
    private readonly ListView _lvScan = new()
    {
        View = View.Details,
        FullRowSelect = true,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly Label _lblScanProgress = Ui.Paragraph("", 700);
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        Font = Theme.Mono,
        BackColor = Theme.Panel,
        BorderStyle = BorderStyle.None,
    };

    private TabPage BuildScanTab()
    {
        var page = Ui.Page("AutoScan");

        var btnScan = Ui.Button("Skanuj wszystkie adresy", async (_, _) => await ScanAsync(), ButtonKind.Primary);
        _readActions.Add(btnScan);

        _lvScan.Columns.Add("Adres", 80);
        _lvScan.Columns.Add("Sterownik", 260);
        _lvScan.Columns.Add("Wynik", 360);
        _lvScan.ItemActivate += (_, _) => UseScannedAddress();

        var header = Ui.Column(
            Ui.Heading("Skan magistrali"),
            Ui.Paragraph(
                "Wybudza po kolei wszystkie adresy (tylko odczyt). Trwa kilka minut. " +
                "Dwuklik na wyniku ustawia ten adres w pasku u góry.", 700),
            Ui.Row(btnScan),
            _lblScanProgress);
        header.Dock = DockStyle.Top;

        var card = new CardPanel { Dock = DockStyle.Fill };
        card.Controls.Add(_lvScan);
        card.Controls.Add(header);
        page.Controls.Add(card);
        return page;
    }

    private async Task ScanAsync()
    {
        _lvScan.Items.Clear();
        _lblScanProgress.Text = "Start…";
        var result = await RunToolAsync("AutoScan", ["AutoScan"], onLine: OnScanLine, absorbState: false);
        if (result is null)
        {
            _lblScanProgress.Text = "";
            return;
        }

        ShowScan(KwOutput.ParseScan(result.Lines));
    }

    private void ShowScan(ScanResult scan)
    {
        _lvScan.Items.Clear();
        void AddRow(int address, string outcome)
        {
            var name = ControllerNames.Known.FirstOrDefault(k => k.Address == address).Name ?? "";
            var item = new ListViewItem(address.ToString("X2")) { Tag = address };
            item.SubItems.Add(name);
            item.SubItems.Add(outcome);
            _lvScan.Items.Add(item);
        }
        foreach (var a in scan.Kwp1281)
        {
            AddRow(a, "odpowiada (KW1281)");
        }
        foreach (var a in scan.Kwp2000)
        {
            AddRow(a, "odpowiada (KWP2000)");
        }
        foreach (var a in scan.Partial)
        {
            AddRow(a, "wybudza się, ale komunikacja się sypie (spróbuj R6 = 0)");
        }

        _lblScanProgress.Text = !scan.Finished
            ? "Skan przerwany – wyniki niepełne."
            : _lvScan.Items.Count == 0
                ? "Żaden sterownik nie odpowiedział – zapłon włączony?"
                : $"Zakończono. Odpowiedziało: {_lvScan.Items.Count}.";
    }

    private void OnScanLine(string line)
    {
        if (KwOutput.ParseScanProgress(line) is int address)
        {
            _lblScanProgress.Text = $"Sprawdzam adres {ControllerNames.Describe(address)}…";
        }
    }

    private void UseScannedAddress()
    {
        if (_lvScan.SelectedItems.Count == 0 || _lvScan.SelectedItems[0].Tag is not int address || _runner?.IsBusy == true)
        {
            return;
        }
        _cbAddress.Text = ControllerNames.Describe(address);
        OnAddressChanged();
        _tabs.SelectedIndex = 0;
    }

    private TabPage BuildLogTab()
    {
        var page = Ui.Page("Log");

        var btnCopy = Ui.Button("Kopiuj", (_, _) =>
        {
            if (_log.TextLength > 0)
            {
                Clipboard.SetText(_log.Text);
            }
        });
        var btnSave = Ui.Button("Zapisz jako…", (_, _) => SaveLog());
        var btnClear = Ui.Button("Wyczyść", (_, _) => _log.Clear());
        var btnToolLog = Ui.Button("Otwórz KW1281Test.log", (_, _) =>
        {
            var path = Path.Combine(Path.GetDirectoryName(_runner?.ExePath ?? AppContext.BaseDirectory)!, "KW1281Test.log");
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        });

        var header = Ui.Column(
            Ui.Paragraph("Pełne wyjście narzędzia z tej sesji. Narzędzie dopisuje też wszystko do KW1281Test.log obok siebie.", 800),
            Ui.Row(btnCopy, btnSave, btnClear, btnToolLog));
        header.Dock = DockStyle.Top;

        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        card.Controls.Add(_log);
        page.Controls.Add(card);
        page.Controls.Add(header);
        return page;
    }

    private void AppendLog(string line)
    {
        if (_log.TextLength > 600_000)
        {
            _log.Text = _log.Text[200_000..];
        }
        _log.AppendText(line + Environment.NewLine);
    }

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog
        {
            FileName = $"audi100-log-{DateTime.Now:yyyy-MM-dd_HH-mm}.txt",
            Filter = "Plik tekstowy|*.txt",
            InitialDirectory = AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _log.Text);
        }
    }
}
