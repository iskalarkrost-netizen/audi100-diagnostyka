using System.Diagnostics;
using System.Globalization;

namespace Audi100Diag;

internal sealed partial class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private ToolRunner? _runner;

    // Pasek połączenia
    private readonly ComboBox _cbPort = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, DropDownWidth = 360 };
    private readonly ComboBox _cbBaud = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 76 };
    private readonly ComboBox _cbAddress = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 180, DropDownWidth = 240 };
    private readonly NumericUpDown _numR6 = Ui.Number(0, 200, 0, 60);
    private readonly CheckBox _chkService = new()
    {
        Text = "Tryb serwisowy",
        AutoSize = true,
        Font = Theme.Bold,
        Margin = new Padding(0, 22, 14, 0),
    };
    private readonly Button _btnStop;
    private readonly Button _btnPrograms;
    private readonly ContextMenuStrip _programsMenu = new();

    // Pasek stanu
    private readonly StatusStrip _status = new() { SizingGrip = false, BackColor = Theme.Panel };
    private readonly ToolStripStatusLabel _stPort = new();
    private readonly ToolStripStatusLabel _stMessage = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _stConflict = new() { ForeColor = Theme.Danger };
    private readonly ToolStripStatusLabel _stTool = new() { ForeColor = Theme.Muted };

    private readonly TabControl _tabs = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Point(16, 6),
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };

    /// <summary>Przyciski wymagające wolnego narzędzia.</summary>
    private readonly List<Control> _readActions = [];
    /// <summary>Przyciski zapisujące do sterownika - dodatkowo wymagają trybu serwisowego.</summary>
    private readonly List<Control> _writeActions = [];

    private IReadOnlyList<PortInfo> _ports = [];
    private string _busyTitle = "";
    private DateTime _busySince;
    private Action<string>? _lineConsumer;
    private bool _closeRequested;

    // Ostatni znany stan sterownika (każde polecenie wybudza sterownik i wypisuje ECU + kodowanie)
    private EcuIdent? _ident;
    private CodingInfo? _coding;

    public MainForm()
    {
        Text = "Audi 100 C4 – diagnostyka KW1281";
        Font = Theme.Ui;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1220, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;

        _btnStop = Ui.Button("■  Stop", (_, _) => StopCurrent(), ButtonKind.Danger);
        _btnStop.Margin = new Padding(0, 18, 0, 0);
        _btnPrograms = Ui.Button("Programy  ▾", (sender, _) =>
        {
            var button = (Control)sender!;
            _programsMenu.Show(button, new Point(0, button.Height));
        });
        _btnPrograms.Margin = new Padding(0, 18, 8, 0);

        _tabs.TabPages.Add(BuildControllerTab());
        _tabs.TabPages.Add(BuildLiveTab());
        _tabs.TabPages.Add(BuildAdaptationTab());
        _tabs.TabPages.Add(BuildCodingTab());
        _tabs.TabPages.Add(BuildScanTab());
        _tabs.TabPages.Add(BuildLogTab());

        // Kolejność dokowania: najpierw wypełnienie, potem krawędzie.
        Controls.Add(_tabs);
        Controls.Add(BuildConnectionBar());
        Controls.Add(BuildStatusBar());

        BuildProgramsMenu();
        LoadSettingsIntoUi();
        UpdateUiState();

        _timer.Tick += (_, _) => OnTimer();
        Load += async (_, _) => await InitToolAsync();
        FormClosing += OnFormClosing;
    }

    // ───────────────────────────── budowa paska połączenia i stanu

    private Control BuildConnectionBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Theme.Panel, Padding = new Padding(14, 8, 14, 6) };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };

        _cbBaud.Items.AddRange(["4800", "9600", "10400"]);
        foreach (var (address, _) in ControllerNames.Known)
        {
            _cbAddress.Items.Add(ControllerNames.Describe(address));
        }
        _cbAddress.SelectedIndexChanged += (_, _) => OnAddressChanged();
        _cbAddress.Leave += (_, _) => OnAddressChanged();

        var tips = new ToolTip();
        tips.SetToolTip(_numR6, "Opóźnienie potwierdzenia bajtu. Sterownik silnika 4A0907473A wymaga 0 ms.");
        tips.SetToolTip(_cbBaud, "Audi 100 C4: 4800 (VAG-COM/DIAG_PL próbują 9600/10400 i dlatego nie łączą).");
        tips.SetToolTip(_chkService, "Odblokowuje zapisy do sterownika: kasowanie błędów, adaptację i kodowanie.");

        _chkService.CheckedChanged += (_, _) => OnServiceModeChanged();

        var left = Ui.Row(
            Ui.Field("Port", _cbPort),
            Ui.Field("Prędkość [baud]", _cbBaud),
            Ui.Field("Sterownik (adres)", _cbAddress),
            Ui.Field("R6 [ms]", _numR6));
        left.Dock = DockStyle.Fill;
        left.WrapContents = false;

        var right = Ui.Row(_chkService, _btnPrograms, _btnStop);
        right.Dock = DockStyle.Right;
        right.WrapContents = false;

        bar.Controls.Add(left);
        bar.Controls.Add(right);
        return bar;
    }

    private Control BuildStatusBar()
    {
        _stPort.Font = Theme.Bold;
        _status.Items.AddRange([_stPort, new ToolStripSeparator(), _stMessage, _stConflict, _stTool]);
        return _status;
    }

    private void BuildProgramsMenu()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var baseDir = AppContext.BaseDirectory;

        void Add(string text, string path, string? workingDir = null, bool isFolder = false)
        {
            var item = new ToolStripMenuItem(text) { Enabled = isFolder ? Directory.Exists(path) : File.Exists(path) };
            item.Click += (_, _) => LaunchExternal(path, workingDir, isProgram: !isFolder && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            _programsMenu.Items.Add(item);
        }

        Add("DIAG_PL (VAG-COM 311 PL)", @"C:\INSTRUKCJE\Vagzainst\DIAG_PL\DIAG_PL\DIAG_PL.exe", @"C:\INSTRUKCJE\Vagzainst\DIAG_PL\DIAG_PL");
        Add("VAG-COM 409.1", @"C:\Program Files (x86)\VAG-COM\VagCom.exe", @"C:\Program Files (x86)\VAG-COM");
        Add("SILNIK.bat (konsola)", Path.Combine(baseDir, "SILNIK.bat"), baseDir);
        _programsMenu.Items.Add(new ToolStripSeparator());
        Add("Folder Audi100-VAG", baseDir, isFolder: true);
        Add("Dokumentacja: VAG-diagnostyka.txt", Path.Combine(desktop, "VAG-diagnostyka.txt"));
        Add("Raport ostatniej sesji", Path.Combine(desktop, "ostatnia sesja", "RAPORT-SESJI.md"));
        Add("Log narzędzia: KW1281Test.log", Path.Combine(baseDir, "KW1281Test.log"));
    }

    private void LaunchExternal(string path, string? workingDir, bool isProgram)
    {
        if (isProgram && _runner?.IsBusy == true)
        {
            Warn("Najpierw zatrzymaj bieżącą operację", "Oba programy potrzebują tego samego portu COM.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = workingDir ?? "" });
            if (isProgram)
            {
                SetMessage($"Uruchomiono {Path.GetFileName(path)} – zamknij go przed użyciem tego okna (blokuje port).", Theme.Warn);
            }
        }
        catch (Exception ex)
        {
            Warn("Nie udało się otworzyć", ex.Message);
        }
    }

    // ───────────────────────────── ustawienia

    private void LoadSettingsIntoUi()
    {
        _cbBaud.Text = _settings.Baud.ToString(CultureInfo.InvariantCulture);
        _cbAddress.Text = ControllerNames.Describe(_settings.Address);
        _numR6.Value = Math.Clamp(_settings.R6, 0, 200);
        RefreshPorts();
    }

    private void SaveConnectionSettings()
    {
        if (SelectedPort is { } port)
        {
            _settings.Port = port;
        }
        if (TryGetBaud(out var baud))
        {
            _settings.Baud = baud;
        }
        if (TryGetAddress(out var address))
        {
            _settings.Address = address;
        }
        _settings.R6 = (int)_numR6.Value;
        _settings.Save();
    }

    private string? SelectedPort => (_cbPort.SelectedItem as PortInfo)?.Name;

    private bool TryGetBaud(out int baud) =>
        int.TryParse(_cbBaud.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out baud)
        && baud is >= 240 and <= 115200;

    private bool TryGetAddress(out int address)
    {
        var token = _cbAddress.Text.Trim().Split(' ', '–', '-')[0];
        return int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address)
               && address is >= 0 and <= 0x7F;
    }

    private int _lastAddress = -1;

    private void OnAddressChanged()
    {
        if (!TryGetAddress(out var address) || address == _lastAddress)
        {
            return;
        }
        _lastAddress = address;
        // Informacje o poprzednim sterowniku przestają obowiązywać.
        _ident = null;
        _coding = null;
        RefreshControllerCard();
        RefreshCodingTab();
        ClearFaultList("Nie odczytano");
        RefreshAdaptationHints();
    }

    private void OnServiceModeChanged()
    {
        if (_chkService.Checked)
        {
            var ok = Confirm(
                "Włączyć tryb serwisowy?",
                "Tryb serwisowy odblokowuje polecenia, które ZAPISUJĄ do sterownika: kasowanie błędów, " +
                "test i zapis adaptacji oraz kodowanie.\n\n" +
                "Każdy zapis i tak wymaga osobnego potwierdzenia, a program sprawdza go ponownym odczytem.",
                "Włącz");
            if (!ok)
            {
                _chkService.Checked = false;
                return;
            }
        }
        _chkService.ForeColor = _chkService.Checked ? Theme.Danger : Theme.Text;
        UpdateUiState();
    }

    // ───────────────────────────── narzędzie

    private async Task InitToolAsync()
    {
        _runner = await ToolRunner.CreateAsync();
        if (_runner is null)
        {
            _stTool.Text = "Brak kw1281test-FIXED*.exe";
            _stTool.ForeColor = Theme.Danger;
            Warn("Nie znaleziono narzędzia",
                "Program szuka kw1281test-FIXED3.exe (lub FIXED2 / FIXED) w swoim folderze " +
                "oraz w Pulpit\\Audi100-VAG.");
        }
        else
        {
            _runner.LineReceived += OnToolLine;
            _stTool.Text = _runner.SupportsStopFile
                ? $"Narzędzie: {_runner.ToolName}"
                : $"Narzędzie: {_runner.ToolName} (starsze – bez podglądu na żywo)";
        }
        UpdateUiState();
        RefreshAdaptationHints();
        _timer.Start();
    }

    private void OnToolLine(string line)
    {
        AppendLog(line);
        _lineConsumer?.Invoke(line);
    }

    /// <summary>
    /// Uruchamia jedno polecenie kw1281test na wybranym porcie/sterowniku.
    /// Zwraca null, jeśli polecenie nie wystartowało.
    /// </summary>
    private async Task<ToolResult?> RunToolAsync(
        string title, string[] command, bool isWrite = false, bool isLoop = false,
        int? samples = null, Action<string>? onLine = null, bool absorbState = true)
    {
        if (_runner is null || _runner.IsBusy || _closeRequested)
        {
            return null;
        }
        var conflicts = PortScanner.ConflictingProcesses(null);
        if (conflicts.Count > 0)
        {
            Warn("Port jest zajęty", $"Działa: {string.Join(", ", conflicts)}.\n\nZamknij ten program – dwa programy nie mogą używać portu COM jednocześnie.");
            return null;
        }
        if (SelectedPort is not { } port)
        {
            Warn("Wybierz port", "Nie wybrano portu COM.");
            return null;
        }
        if (!TryGetBaud(out var baud))
        {
            Warn("Nieprawidłowa prędkość", "Podaj prędkość w baudach, np. 4800.");
            return null;
        }
        if (!TryGetAddress(out var address))
        {
            Warn("Nieprawidłowy adres", "Adres sterownika podaj szesnastkowo, np. 01 albo 08.");
            return null;
        }

        var args = new List<string> { port, baud.ToString(CultureInfo.InvariantCulture), address.ToString("X2") };
        args.AddRange(command);
        var env = new Dictionary<string, string>
        {
            ["KW1281_R6"] = ((int)_numR6.Value).ToString(CultureInfo.InvariantCulture),
        };
        if (samples is int n)
        {
            env["KW1281_SAMPLES"] = n.ToString(CultureInfo.InvariantCulture);
        }
        SaveConnectionSettings();

        _busyTitle = title;
        _busySince = DateTime.Now;
        _lineConsumer = onLine;
        AppendLog("");
        AppendLog($"▶ {DateTime.Now:HH:mm:ss}  {title}   [{string.Join(" ", args)}, R6={env["KW1281_R6"]} ms]");

        ToolResult result;
        try
        {
            var task = _runner.RunAsync(args, env, isWrite, isLoop);
            UpdateUiState();
            SetMessage($"⏳ {title}…", Theme.Accent);
            result = await task;
        }
        catch (Exception ex)
        {
            AppendLog($"✖ Nie udało się uruchomić narzędzia: {ex.Message}");
            SetMessage($"Nie udało się uruchomić narzędzia: {ex.Message}", Theme.Danger);
            return null;
        }
        finally
        {
            _lineConsumer = null;
            _busyTitle = "";
            UpdateUiState();
        }

        var problem = result.Stopped ? null : KwOutput.Diagnose(result.Lines);
        result = result with { Problem = problem };
        if (absorbState)
        {
            AbsorbControllerState(result.Lines);
        }

        var seconds = result.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture);
        if (result.Stopped)
        {
            SetMessage($"Zatrzymano: {title} ({seconds} s)", Theme.Muted);
        }
        else if (problem is null)
        {
            SetMessage($"✔ {title} ({seconds} s)", Theme.Ok);
        }
        else
        {
            SetMessage($"✖ {title}: {problem}", Theme.Danger);
        }
        AppendLog($"■ {title}: {(result.Stopped ? "zatrzymano" : problem ?? "OK")} ({seconds} s)");

        if (_closeRequested)
        {
            BeginInvoke(Close);
        }
        return result;
    }

    private void StopCurrent()
    {
        if (_runner?.IsBusy != true)
        {
            return;
        }
        _liveRestart = false;
        _watchRestart = false;

        var cleanStop = _runner.CurrentIsLoop && _runner.SupportsStopFile;
        if (_runner.CurrentIsWrite && !cleanStop)
        {
            var kill = Confirm(
                "Przerwać zapis?",
                "Trwa zapis do sterownika. Przerwanie w połowie może zostawić go w nieokreślonym stanie. " +
                "Zapis trwa zwykle kilka sekund – lepiej poczekać.",
                "Przerwij mimo to");
            if (!kill)
            {
                return;
            }
        }
        SetMessage("Zatrzymuję…", Theme.Muted);
        _runner.RequestStop(clean: cleanStop);
    }

    /// <summary>Każde polecenie wybudza sterownik i wypisuje jego numer oraz kodowanie.</summary>
    private void AbsorbControllerState(IReadOnlyList<string> lines)
    {
        var ident = KwOutput.ParseIdent(lines);
        var coding = KwOutput.ParseCoding(lines);
        if (ident is not null)
        {
            _ident = ident;
        }
        if (coding is not null)
        {
            _coding = coding;
        }
        if (ident is null && coding is null)
        {
            return;
        }

        if (_ident is not null && _coding is not null
            && !_settings.ReferenceCoding.ContainsKey(_ident.PartNumber))
        {
            _settings.ReferenceCoding[_ident.PartNumber] = new CodingRef(_coding.Coding, _coding.Workshop);
            _settings.Save();
            AppendLog($"ℹ Zapamiętano kodowanie referencyjne {_ident.PartNumber}: {_coding.Coding} / {_coding.Workshop}");
        }
        RefreshControllerCard();
        RefreshCodingTab();
        RefreshAdaptationHints();
    }

    private CodingRef? ReferenceFor(EcuIdent? ident) =>
        ident is not null && _settings.ReferenceCoding.TryGetValue(ident.PartNumber, out var r) ? r : null;

    private bool CodingDiffersFromReference =>
        _coding is not null && ReferenceFor(_ident) is { } r
        && (r.Coding != _coding.Coding || r.Workshop != _coding.Workshop);

    // ───────────────────────────── stan interfejsu

    private void UpdateUiState()
    {
        var ready = _runner is not null && !_runner.IsBusy && !_closeRequested;
        foreach (var c in _readActions)
        {
            c.Enabled = ready;
        }
        foreach (var c in _writeActions)
        {
            c.Enabled = ready && _chkService.Checked;
        }
        _btnStop.Enabled = _runner?.IsBusy == true;
        var idle = _runner?.IsBusy != true;
        _cbPort.Enabled = _cbBaud.Enabled = _cbAddress.Enabled = _numR6.Enabled = idle;
        UpdateLiveButton();
        UpdateWatchButton();
    }

    private void OnTimer()
    {
        if (!_cbPort.DroppedDown)
        {
            RefreshPorts();
        }

        var conflicts = PortScanner.ConflictingProcesses(_runner?.CurrentProcessId);
        _stConflict.Text = conflicts.Count > 0 ? $"⚠ Port blokuje: {string.Join(", ", conflicts)}" : "";

        if (_busyTitle.Length > 0)
        {
            SetMessage($"⏳ {_busyTitle}… {(DateTime.Now - _busySince).TotalSeconds:0} s", Theme.Accent);
        }
    }

    private void RefreshPorts()
    {
        var ports = PortScanner.Scan().ToList();
        var selected = SelectedPort ?? _settings.Port;
        if (!string.IsNullOrEmpty(selected) && ports.All(p => p.Name != selected))
        {
            ports.Insert(0, new PortInfo(selected, "niepodłączony", false, false));
        }
        if (!ports.SequenceEqual(_ports))
        {
            _ports = ports;
            _cbPort.BeginUpdate();
            _cbPort.Items.Clear();
            foreach (var p in ports)
            {
                _cbPort.Items.Add(p);
            }
            _cbPort.SelectedItem = ports.FirstOrDefault(p => p.Name == selected) ?? ports.FirstOrDefault();
            _cbPort.EndUpdate();
        }

        var current = _cbPort.SelectedItem as PortInfo;
        if (current is null)
        {
            _stPort.Text = "Brak portów COM";
            _stPort.ForeColor = Theme.Danger;
        }
        else if (current.Present)
        {
            _stPort.Text = current.IsFtdi ? $"● {current.Name}: kabel podłączony" : $"● {current.Name}: {current.Description}";
            _stPort.ForeColor = current.IsFtdi ? Theme.Ok : Theme.Warn;
        }
        else
        {
            _stPort.Text = $"○ {current.Name}: kabel odłączony";
            _stPort.ForeColor = Theme.Danger;
        }
    }

    private void SetMessage(string text, Color color)
    {
        _stMessage.Text = text;
        _stMessage.ForeColor = color;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveConnectionSettings();
        if (_runner?.IsBusy != true)
        {
            return;
        }
        if (_runner.CurrentIsWrite && !_runner.CurrentIsLoop)
        {
            Warn("Trwa zapis do sterownika", "Poczekaj kilka sekund na jego zakończenie i zamknij okno ponownie.");
            e.Cancel = true;
            return;
        }
        // Najpierw czyste zakończenie komunikacji, potem zamknięcie okna.
        e.Cancel = true;
        _closeRequested = true;
        _liveRestart = false;
        _watchRestart = false;
        _runner.RequestStop(clean: _runner.CurrentIsLoop);
        UpdateUiState();
    }

    // ───────────────────────────── okna dialogowe

    private bool Confirm(string heading, string text, string confirmText = "Tak, wykonaj")
    {
        var yes = new TaskDialogButton(confirmText);
        var page = new TaskDialogPage
        {
            Caption = Text,
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Warning,
            Buttons = { yes, TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };
        return TaskDialog.ShowDialog(this, page) == yes;
    }

    private void Warn(string heading, string text) =>
        TaskDialog.ShowDialog(this, new TaskDialogPage
        {
            Caption = Text,
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Warning,
        });

    private void Inform(string heading, string text, bool ok = true) =>
        TaskDialog.ShowDialog(this, new TaskDialogPage
        {
            Caption = Text,
            Heading = heading,
            Text = text,
            Icon = ok ? TaskDialogIcon.ShieldSuccessGreenBar : TaskDialogIcon.ShieldErrorRedBar,
        });

    /// <summary>Obraz każdej zakładki do PNG (parametr --zrzut).</summary>
    public void SaveTabScreenshots(string directory)
    {
        Directory.CreateDirectory(directory);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-4000, -4000);
        ShowInTaskbar = false;
        Show();
        var deadline = DateTime.Now.AddSeconds(10);
        while (_runner is null && DateTime.Now < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(50);
        }
        LoadPreviewData();
        for (var i = 0; i < _tabs.TabPages.Count; i++)
        {
            _tabs.SelectedIndex = i;
            Application.DoEvents();
            using var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(Point.Empty, Size));
            var name = string.Concat(_tabs.TabPages[i].Text.Split(Path.GetInvalidFileNameChars())).Replace("  ", " ");
            bmp.Save(Path.Combine(directory, $"{i + 1}-{name}.png"));
        }
        Hide();
    }
}
