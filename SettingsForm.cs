using System.Drawing.Printing;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LabelPrinter.Printing;
using LabelPrinter.Services;

namespace LabelPrinter;

public partial class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly PrintHostService _host;
    private readonly List<FormatRow> _rows = new();
    private readonly List<string> _printerChoices = new();
    private readonly List<Label> _headerLabels = new();
    private string _localIp = "127.0.0.1";
    private HashSet<string> _renderedFailureIds = new();

    // Lodop-compat row controls — kept separate from FormatRow/_rows since this row has no
    // Size/Alias/PrintType/Port (see LodopCompatConfig for why).
    private Label _lodopSizeLabel = null!;
    private ComboBox _lodopPrinterCombo = null!;
    private CheckBox _lodopEnabledCheckBox = null!;
    private Button _lodopTestButton = null!;
    private Button btnRetryFailed = null!;
    private Button btnClearFailed = null!;
    private ComboBox cboFailureFilter = null!;
    private bool _failureFilterTodayOnly;
    private bool _lastFailureFilterTodayOnly;

    public event Action<AppConfig>? ConfigSaved;

    public SettingsForm(AppConfig config, PrintHostService host)
    {
        _config = config;
        _host = host;
        InitializeComponent();
        cboLanguage.SelectedIndexChanged += (_, _) =>
        {
            if (cboLanguage.SelectedIndex < 0)
                return;
            L.SetLanguage(cboLanguage.SelectedIndex == 1 ? AppLanguage.En : AppLanguage.Zh);
        };
        L.LanguageChanged += ApplyLanguage;
        // Seed from today's disk log first — the TextBox is otherwise empty after
        // close/reopen or process restart, while failures already reload from JSON.
        SeedRunLogFromDisk();
        LoadUi();
        _host.LogMessage += AppendLog;
        // The failure tab can be populated before its TabPage/ListView are ever shown
        // (e.g. it isn't the initially-selected tab); force one more repaint once the
        // form actually has a handle so the first row doesn't sit un-painted.
        Load += (_, _) => RefreshFailureList(force: true);
        // Re-read disk when switching back to 运行日志 so OK + FAIL lines written while
        // the form was closed (or on another view) always show in the main log pane.
        tabLog.SelectedIndexChanged += (_, _) =>
        {
            if (tabLog.SelectedTab == tabRunLog)
                SeedRunLogFromDisk();
        };
    }

    private void SeedRunLogFromDisk()
    {
        var text = FileLog.TryReadToday();
        if (string.IsNullOrEmpty(text))
            return;

        // Preserve live-only lines that might not have flushed yet by only replacing
        // when disk content is at least as long (normal case after FileLog.Write).
        if (txtLog.TextLength > 0 && text.Length < txtLog.TextLength)
            return;

        txtLog.Text = text;
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
    }

    private void LoadUi()
    {
        _localIp = NetworkHelper.GetLocalIPv4();

        foreach (string name in PrinterSettings.InstalledPrinters)
            _printerChoices.Add(name);
        _printerChoices.Add("LPT1");
        _printerChoices.Add("LPT2");
        _printerChoices.Add("LPT3");

        txtWsUrl.Text = _config.LabelPrinterUrl;
        chkEnableWebSocket.Checked = _config.EnableWebSocket;
        txtWsUrl.Enabled = chkEnableWebSocket.Checked;
        chkRunAtStartup.Checked = _config.RunAtStartup;
        chkAllowLan.Checked = _config.AllowLanAccess;
        cboLanguage.SelectedIndex = L.Current == AppLanguage.En ? 1 : 0;

        BuildHeaderRow();
        foreach (var format in _config.LabelFormats)
            AddFormatRow(format);
        AddLodopCompatRow(_config.LodopCompat);
        BuildRetryFailedButton();
        // Belt-and-suspenders in case a designer reopen reorders Controls: keep Fill under Top.
        tabFailures.Controls.SetChildIndex(lvFailures, 0);
        tabFailures.Controls.SetChildIndex(pnlFailureToolbar, 1);

        // Daily audit txt stays; unresolved json is never pruned here.
        var pruned = LodopFailureReport.PruneOldAuditFiles(
            Path.Combine(AppContext.BaseDirectory, "logs"), keepDays: 30);
        if (pruned > 0)
            AppendLog($"Pruned {pruned} Lodop failure audit file(s) older than 30 days.");

        FitFormatsTable();
        ApplyLanguage();
        RefreshFailureList(force: true);
    }

    /// <summary>
    /// Built the same way as row.Test/_lodopTestButton — a Designer-declared Button with
    /// AutoSize measured against the live system font came out taller than its parent
    /// panel and spilled over the list below it. Constructing it in code with its own
    /// fixed Font, straight into SizeFailureToolbarButton(), avoids that entirely.
    /// </summary>
    private void BuildRetryFailedButton()
    {
        const int btnH = FailureToolbarButtonHeight;

        btnRetryFailed = new Button
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 9F),
            Size = new Size(120, btnH),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = Padding.Empty,
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btnRetryFailed.Click += BtnRetryFailed_Click;

        btnClearFailed = new Button
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 9F),
            Size = new Size(120, btnH),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = Padding.Empty,
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.System,
            UseVisualStyleBackColor = true,
            TextAlign = ContentAlignment.MiddleCenter
        };
        btnClearFailed.Click += BtnClearFailed_Click;

        cboFailureFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 130,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Font = new Font("Segoe UI", 9F),
            FlatStyle = FlatStyle.System
        };
        cboFailureFilter.SelectedIndexChanged += (_, _) =>
        {
            _failureFilterTodayOnly = cboFailureFilter.SelectedIndex == 1;
            RefreshFailureList(force: true);
        };

        pnlFailureToolbar.Controls.Add(cboFailureFilter);
        pnlFailureToolbar.Controls.Add(btnClearFailed);
        pnlFailureToolbar.Controls.Add(btnRetryFailed);
        pnlFailureToolbar.Resize += (_, _) => LayoutFailureToolbar();
        LayoutFailureToolbar();
    }

    private const int FailureToolbarButtonHeight = 28;

    private void LayoutFailureToolbar()
    {
        if (btnRetryFailed is null || btnClearFailed is null || cboFailureFilter is null)
            return;

        var btnH = FailureToolbarButtonHeight;
        var y = Math.Max(4, (pnlFailureToolbar.ClientSize.Height - btnH) / 2);

        SizeFailureToolbarButton(btnRetryFailed);
        SizeFailureToolbarButton(btnClearFailed);

        btnRetryFailed.Top = y;
        btnClearFailed.Top = y;
        btnRetryFailed.Left = pnlFailureToolbar.ClientSize.Width - btnRetryFailed.Width - 8;
        btnClearFailed.Left = btnRetryFailed.Left - btnClearFailed.Width - 8;

        // ComboBox height is font-driven; center against the button band.
        cboFailureFilter.Top = y + Math.Max(0, (btnH - cboFailureFilter.Height) / 2);
        cboFailureFilter.Left = chkSelectAllFailures.Right + 12;

        chkSelectAllFailures.Top = y + Math.Max(0, (btnH - chkSelectAllFailures.Height) / 2);
    }

    private static void SizeFailureToolbarButton(Button button)
    {
        var textWidth = TextRenderer.MeasureText(
            string.IsNullOrEmpty(button.Text) ? "清除选中项" : button.Text,
            button.Font).Width;
        button.AutoSize = false;
        button.Size = new Size(Math.Max(96, textWidth + 28), FailureToolbarButtonHeight);
    }

    private void BuildHeaderRow()
    {
        EnsureRowStyle(0, SizeType.Absolute, HeaderRowHeight);

        for (var col = 0; col < 8; col++)
        {
            var lbl = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 4, 3, 2),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _headerLabels.Add(lbl);
            tlpFormats.Controls.Add(lbl, col, 0);
        }
    }

    private void AddFormatRow(LabelFormat format)
    {
        var rowIndex = tlpFormats.RowCount;
        tlpFormats.RowCount = rowIndex + 1;
        EnsureRowStyle(rowIndex, SizeType.Absolute, DataRowHeight);

        var rdoDefault = new RadioButton { AutoSize = true, Checked = format.IsDefault, Anchor = AnchorStyles.Left };
        var lblSize = new Label { Text = format.Size, AutoSize = true, Anchor = AnchorStyles.Left };

        var numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(format.Port, 1, 65535), Anchor = AnchorStyles.Left, Width = 90 };

        var txtUrl = new TextBox
        {
            ReadOnly = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = BuildUrl((int)numPort.Value),
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.None
        };
        numPort.ValueChanged += (_, _) => txtUrl.Text = BuildUrl((int)numPort.Value);

        var cboPrinter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownWidth = 320 // narrow box, but show full printer names when opened
        };
        foreach (var choice in _printerChoices)
            cboPrinter.Items.Add(choice);
        var idx = cboPrinter.Items.IndexOf(format.PrinterName);
        if (idx < 0 && !string.IsNullOrEmpty(format.PrinterName))
            idx = cboPrinter.Items.Add(format.PrinterName); // keep an unknown/offline printer selectable
        cboPrinter.SelectedIndex = idx >= 0 ? idx : (cboPrinter.Items.Count > 0 ? 0 : -1);

        var cboType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        cboType.Items.AddRange(new object[] { "EPL", "ZPL", L.T("type.text"), "PDF" });
        cboType.SelectedIndex = (int)format.PrintType;

        var chkEnabled = new CheckBox { Checked = format.Enabled, AutoSize = true, Anchor = AnchorStyles.Left };

        // Fixed height — TableLayoutPanel + Button.AutoSize makes the LAST row's button
        // grow to fill leftover panel height (the huge 4x6 Test you just saw).
        var btnTest = new Button
        {
            Text = L.T("btn.test"),
            AutoSize = false,
            Font = new Font("Segoe UI", 8F),
            Size = new Size(72, 26),
            Margin = new Padding(3, 3, 3, 3),
            Padding = new Padding(8, 0, 8, 0),
            FlatStyle = FlatStyle.Standard,
            UseVisualStyleBackColor = true,
            Anchor = AnchorStyles.Left
        };
        SizeTestButton(btnTest);

        var row = new FormatRow(format.Size, rdoDefault, lblSize, txtUrl, cboPrinter, cboType, numPort, chkEnabled, btnTest);
        btnTest.Click += (_, _) => TestRow(row);
        _rows.Add(row);

        tlpFormats.Controls.Add(rdoDefault, 0, rowIndex);
        tlpFormats.Controls.Add(lblSize, 1, rowIndex);
        tlpFormats.Controls.Add(txtUrl, 2, rowIndex);
        tlpFormats.Controls.Add(cboPrinter, 3, rowIndex);
        tlpFormats.Controls.Add(cboType, 4, rowIndex);
        tlpFormats.Controls.Add(numPort, 5, rowIndex);
        tlpFormats.Controls.Add(chkEnabled, 6, rowIndex);
        tlpFormats.Controls.Add(btnTest, 7, rowIndex);
    }

    /// <summary>
    /// One extra row in the same tlpFormats grid for the C-Lodop compatibility shim
    /// (stands in for a real C-Lodop install so callers like MZL's lodop_print.js can
    /// print PDFs through LabelPrinter unchanged — see Services/LodopCompatListener).
    /// It isn't a label size, so there's no Default radio and Type/Port are fixed
    /// read-only labels rather than editable controls — MZL's lodop_print.js has
    /// 8000/18000 hardcoded, so a user-editable port here would just silently stop
    /// working, but it's still shown so it's not a mystery why there's no control there.
    /// </summary>
    private void AddLodopCompatRow(LodopCompatConfig config)
    {
        var rowIndex = tlpFormats.RowCount;
        tlpFormats.RowCount = rowIndex + 1;
        EnsureRowStyle(rowIndex, SizeType.Absolute, DataRowHeight);

        _lodopSizeLabel = new Label { Text = L.T("lodop.label"), AutoSize = true, Anchor = AnchorStyles.Left };

        var txtUrl = new TextBox
        {
            ReadOnly = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Text = "http://localhost:8000",
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.None
        };

        _lodopPrinterCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            DropDownWidth = 320
        };
        foreach (var choice in _printerChoices)
            _lodopPrinterCombo.Items.Add(choice);
        var idx = _lodopPrinterCombo.Items.IndexOf(config.PrinterName);
        if (idx < 0 && !string.IsNullOrEmpty(config.PrinterName))
            idx = _lodopPrinterCombo.Items.Add(config.PrinterName);
        _lodopPrinterCombo.SelectedIndex = idx >= 0 ? idx : (_lodopPrinterCombo.Items.Count > 0 ? 0 : -1);

        // Read-only — type and ports are fixed (see class remarks on AddLodopCompatRow),
        // but still shown so it's not a mystery why there's no dropdown/NumericUpDown here.
        var lblType = new Label
        {
            Text = "PDF", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText
        };
        var lblPorts = new Label
        {
            Text = "8000/18000 + 8443/8444", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText
        };

        _lodopEnabledCheckBox = new CheckBox { Checked = config.Enabled, AutoSize = true, Anchor = AnchorStyles.Left };

        _lodopTestButton = new Button
        {
            Text = L.T("btn.test"),
            AutoSize = false,
            Font = new Font("Segoe UI", 8F),
            Size = new Size(72, 26),
            Margin = new Padding(3, 3, 3, 3),
            Padding = new Padding(8, 0, 8, 0),
            FlatStyle = FlatStyle.Standard,
            UseVisualStyleBackColor = true,
            Anchor = AnchorStyles.Left
        };
        SizeTestButton(_lodopTestButton);
        _lodopTestButton.Click += (_, _) => TestLodopRow();

        tlpFormats.Controls.Add(_lodopSizeLabel, 1, rowIndex);
        tlpFormats.Controls.Add(txtUrl, 2, rowIndex);
        tlpFormats.Controls.Add(_lodopPrinterCombo, 3, rowIndex);
        tlpFormats.Controls.Add(lblType, 4, rowIndex);
        tlpFormats.Controls.Add(lblPorts, 5, rowIndex);
        tlpFormats.Controls.Add(_lodopEnabledCheckBox, 6, rowIndex);
        tlpFormats.Controls.Add(_lodopTestButton, 7, rowIndex);
    }

    private async void TestLodopRow()
    {
        var printerName = (string?)_lodopPrinterCombo.SelectedItem ?? "";
        if (string.IsNullOrWhiteSpace(printerName))
        {
            MessageBox.Show(this, "请先为 Lodop 兼容行选择打印机。", "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _lodopTestButton.Enabled = false;
        var originalText = _lodopTestButton.Text;
        _lodopTestButton.Text = L.T("btn.testing");
        SizeTestButton(_lodopTestButton);

        // Exercise the REAL path (JS-equivalent HTTP call -> fetch pdfUrl -> PrintTo), not
        // a shortcut straight to PrintModel — the bugs worth catching here are exactly the
        // ones a shortcut would hide (absolute URL, CORS, JSON body).
        LodopCompatListener? tempListener = null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            int port;

            // Prefer the already-running tray listener when MZL compat is enabled —
            // a second bind on 8000/18000 always fails and used to falsely blame C-Lodop.
            var livePort = await TryFindOurLodopCompatPortAsync(http);
            if (livePort is int existing)
            {
                port = existing;
                AppendLog($"Lodop-compat test: using live listener on {port}.");
            }
            else
            {
                var tempConfig = new LodopCompatConfig { PrinterName = printerName };
                tempListener = new LodopCompatListener(tempConfig, new PrintModel(), AppendLog);
                await Task.Run(() => tempListener.Start());

                if (tempListener.BoundPorts.Count == 0)
                {
                    AppendLog("Lodop-compat test: could not bind 8000 or 18000 — is a real C-Lodop install still running?");
                    MessageBox.Show(
                        this,
                        "无法启动兼容服务：8000 和 18000 端口都被占用，且当前不是本程序的兼容服务。请先确认真实 C-Lodop 已完全卸载/停止后再测试。",
                        "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                port = tempListener.BoundPorts[0];
            }

            var content = new StringContent(
                $$"""{"pdfUrl":"http://localhost:{{port}}/_test_sample.pdf"}""",
                Encoding.UTF8, "application/json");
            var response = await http.PostAsync($"http://localhost:{port}/lodop_print", content);

            if (response.IsSuccessStatusCode)
            {
                AppendLog("Lodop-compat test: printed sample PDF.");
                MessageBox.Show(this, "测试成功。", "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if ((int)response.StatusCode == 503)
            {
                AppendLog("Lodop-compat test: printer busy (503).");
                MessageBox.Show(this, "打印机忙，请稍后重试。", "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                var text = await response.Content.ReadAsStringAsync();
                AppendLog($"Lodop-compat test failed: {(int)response.StatusCode} {text}");
                MessageBox.Show(this, text, "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Lodop-compat test failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            tempListener?.Dispose();
            _lodopTestButton.Text = originalText;
            SizeTestButton(_lodopTestButton);
            _lodopTestButton.Enabled = true;
        }
    }

    /// <summary>
    /// Returns a port if localhost already serves our Lodop-compat JS (live tray listener),
    /// otherwise null. Distinguishes us from a real C-Lodop so Test never POSTs blindly.
    /// </summary>
    private static async Task<int?> TryFindOurLodopCompatPortAsync(HttpClient http)
    {
        foreach (var port in new[] { 8000, 18000 })
        {
            try
            {
                var js = await http.GetStringAsync($"http://localhost:{port}/CLodopfuncs.js");
                if (LodopCompatListener.LooksLikeOurClodopFuncsJs(js))
                    return port;
            }
            catch
            {
                // port down or not ours
            }
        }

        return null;
    }

    // Designer Absolute sizes get AutoScale'd; RowStyles we add at runtime do not.
    // Derive heights from the live font so DPI won't clip headers or leave a hollow gap.
    private float HeaderRowHeight => Math.Max(26, Font.Height + 10);
    private float DataRowHeight => Math.Max(32, Font.Height + 18);

    private void EnsureRowStyle(int index, SizeType sizeType, float height)
    {
        while (tlpFormats.RowStyles.Count <= index)
            tlpFormats.RowStyles.Add(new RowStyle(sizeType, height));
        tlpFormats.RowStyles[index] = new RowStyle(sizeType, height);
    }

    /// <summary>
    /// Shrink the formats table to exactly its Absolute rows, then park the controls
    /// below it so we don't keep the old Y=260 gap from when the panel was taller.
    /// </summary>
    private void FitFormatsTable()
    {
        float total = 0;
        for (var i = 0; i < tlpFormats.RowCount && i < tlpFormats.RowStyles.Count; i++)
            total += tlpFormats.RowStyles[i].Height;
        tlpFormats.Height = (int)Math.Ceiling(total) + 2;

        var y = tlpFormats.Bottom + 14;
        chkRunAtStartup.Top = y;
        btnSave.Top = y - 2;
        chkAllowLan.Top = chkRunAtStartup.Bottom + 6;
        tabLog.Top = chkAllowLan.Bottom + 12;
    }

    private string BuildUrl(int port) => $"http://{_localIp}:{port}/LabelPrint";

    private static void SizeTestButton(Button button)
    {
        var textWidth = TextRenderer.MeasureText(button.Text, button.Font).Width;
        button.AutoSize = false;
        button.Size = new Size(Math.Max(72, textWidth + 24), 26);
    }

    private async void TestRow(FormatRow row)
    {
        ApplyUiToConfig();
        var printerName = (string?)row.Printer.SelectedItem ?? "";
        if (string.IsNullOrWhiteSpace(printerName))
        {
            MessageBox.Show(this, "请先为该尺寸选择打印机。", "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var type = (LabelPrintType)row.Type.SelectedIndex;
        var sample = SampleLabelGenerator.Generate(type, row.Size);

        // Printer I/O (OpenPrinter/WritePrinter, PrintDocument.Print) is synchronous and can
        // block for seconds if the printer is asleep/slow to respond. Run it off the UI thread
        // so the window stays responsive, and flip the button so the click's effect is visible
        // immediately instead of looking like nothing happened.
        row.Test.Enabled = false;
        var originalText = row.Test.Text;
        row.Test.Text = L.T("btn.testing");
        SizeTestButton(row.Test);
        try
        {
            await Task.Run(() => new PrintModel().PrintTo(sample, printerName, type));
            AppendLog($"Test [{row.Size}/{type}] sent to {printerName}.");
        }
        catch (Exception ex)
        {
            AppendLog($"Test [{row.Size}] failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            row.Test.Text = originalText;
            SizeTestButton(row.Test);
            row.Test.Enabled = true;
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        ApplyUiToConfig();

        var errors = _config.ValidateFormats();
        if (errors.Count > 0)
        {
            var msg = string.Join(Environment.NewLine, errors);
            AppendLog($"Save blocked: {msg}");
            MessageBox.Show(this, msg, "配置有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ConfigSaved?.Invoke(_config);
        AppendLog("Settings saved.");
        MessageBox.Show(this, "已保存并重新连接。", "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ApplyUiToConfig()
    {
        _config.LabelPrinterUrl = txtWsUrl.Text.Trim();
        _config.EnableWebSocket = chkEnableWebSocket.Checked;
        _config.RunAtStartup = chkRunAtStartup.Checked;
        _config.AllowLanAccess = chkAllowLan.Checked;
        _config.Language = L.Code(L.Current);

        foreach (var row in _rows)
        {
            var format = _config.LabelFormats.First(f => f.Size == row.Size);
            format.PrinterName = (string?)row.Printer.SelectedItem ?? "";
            format.PrintType = (LabelPrintType)row.Type.SelectedIndex;
            format.Port = (int)row.Port.Value;
            format.Enabled = row.Enabled.Checked;
            format.IsDefault = row.Default.Checked;
        }

        _config.LodopCompat.PrinterName = (string?)_lodopPrinterCombo.SelectedItem ?? "";
        _config.LodopCompat.Enabled = _lodopEnabledCheckBox.Checked;
    }

    private void ApplyLanguage()
    {
        Text = $"{L.T("title")}  v{Application.ProductVersion}";
        lblHost.Text = $"{L.T("host")}: {_localIp}";
        lblWsUrl.Text = L.T("websocket");
        chkEnableWebSocket.Text = L.T("enable");
        chkRunAtStartup.Text = L.T("chk.runAtStartup");
        chkAllowLan.Text = L.T("chk.allowLan");
        btnSave.Text = L.T("btn.save");
        lblLanguage.Text = L.T("language");
        // Keep the Language label tucked against the combo on the right edge.
        lblLanguage.Left = cboLanguage.Left - lblLanguage.PreferredWidth - 8;
        lblLanguage.Top = cboLanguage.Top + (cboLanguage.Height - lblLanguage.PreferredHeight) / 2;
        tabRunLog.Text = L.T("log.tab.run");
        lvFailures.Columns[0].Text = L.T("col.failTime");
        lvFailures.Columns[1].Text = L.T("col.failReason");
        lvFailures.Columns[2].Text = L.T("col.failFile");
        lvFailures.Columns[3].Text = L.T("col.failDetail");
        btnRetryFailed.Text = L.T("btn.retryFailed");
        btnClearFailed.Text = L.T("btn.clearFailed");
        chkSelectAllFailures.Text = L.T("chk.selectAllFailures");
        var filterIndex = cboFailureFilter.SelectedIndex < 0 ? 0 : cboFailureFilter.SelectedIndex;
        cboFailureFilter.Items.Clear();
        cboFailureFilter.Items.Add(L.T("fail.filter.all"));
        cboFailureFilter.Items.Add(L.T("fail.filter.today"));
        cboFailureFilter.SelectedIndex = filterIndex;
        LayoutFailureToolbar();
        RefreshFailureList(force: true);

        string[] headers =
        {
            L.T("col.default"), L.T("col.size"), L.T("col.url"), L.T("col.printer"),
            L.T("col.type"), L.T("col.port"), L.T("col.enabled"), ""
        };
        for (var i = 0; i < _headerLabels.Count && i < headers.Length; i++)
            _headerLabels[i].Text = headers[i];

        foreach (var row in _rows)
        {
            if (row.Test.Enabled)
            {
                row.Test.Text = L.T("btn.test");
                SizeTestButton(row.Test);
            }

            var selected = row.Type.SelectedIndex;
            row.Type.Items.Clear();
            row.Type.Items.AddRange(new object[] { "EPL", "ZPL", L.T("type.text"), "PDF" });
            row.Type.SelectedIndex = selected;
        }

        _lodopSizeLabel.Text = L.T("lodop.label");
        if (_lodopTestButton.Enabled)
        {
            _lodopTestButton.Text = L.T("btn.test");
            SizeTestButton(_lodopTestButton);
        }

        var langIndex = L.Current == AppLanguage.En ? 1 : 0;
        if (cboLanguage.SelectedIndex != langIndex)
            cboLanguage.SelectedIndex = langIndex;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        RefreshFailureList();
    }

    /// <summary>
    /// Re-reads logs/lodop-print-failures.json and repaints the failure tab, including
    /// the pending count in its title. Called on every log line, so it skips rebuilding
    /// the list (and losing the operator's checkbox picks) unless the failure set itself
    /// actually changed; <paramref name="force"/> overrides that for language switches.
    /// </summary>
    private void RefreshFailureList(bool force = false)
    {
        var all = LodopFailureStore.For(LodopFailureStore.DefaultPath).Load();
        // Tab badge = full unresolved pool (cross-day), not the filtered view count.
        tabFailures.Text = $"{L.T("log.tab.failures")} ({all.Count})";

        var visible = _failureFilterTodayOnly
            ? all.Where(j => LodopFailedJobExtensions.IsOnLocalDay(j.Timestamp, DateTime.Today)).ToList()
            : all.ToList();

        var ids = visible.Select(f => f.Id).ToHashSet();
        if (!force && ids.SetEquals(_renderedFailureIds) && _lastFailureFilterTodayOnly == _failureFilterTodayOnly)
            return;
        _renderedFailureIds = ids;
        _lastFailureFilterTodayOnly = _failureFilterTodayOnly;

        chkSelectAllFailures.Checked = false;

        lvFailures.BeginUpdate();
        lvFailures.Items.Clear();
        foreach (var job in visible.OrderByDescending(j => j.Timestamp))
        {
            var item = new ListViewItem(job.Timestamp);
            item.SubItems.Add(ReasonLabel(job.Reason));
            item.SubItems.Add(LodopFailureReport.FileNameFromUrl(job.PdfUrl));
            item.SubItems.Add(job.Detail ?? "");
            item.Tag = job;
            lvFailures.Items.Add(item);
        }
        lvFailures.EndUpdate();
        lvFailures.Refresh();
    }

    private void ChkSelectAllFailures_CheckedChanged(object? sender, EventArgs e)
    {
        foreach (ListViewItem item in lvFailures.Items)
            item.Checked = chkSelectAllFailures.Checked;
    }

    private static string ReasonLabel(string reason)
    {
        var key = $"fail.{reason}";
        var label = L.T(key);
        return label == key ? reason : label;
    }

    /// <summary>
    /// Re-submits checked failures through the same HTTP path MZL would use (not a
    /// shortcut straight to PrintModel), so a retry exercises fetch+print exactly like a
    /// fresh print — and only clears an entry from the failure store once the live
    /// listener has actually accepted it back into its queue.
    /// </summary>
    private async void BtnRetryFailed_Click(object? sender, EventArgs e)
    {
        var selected = lvFailures.CheckedItems.Cast<ListViewItem>()
            .Select(i => (LodopFailedJob)i.Tag!)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, L.T("msg.selectFailures"), "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        btnRetryFailed.Enabled = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var port = await TryFindOurLodopCompatPortAsync(http);
            if (port is null)
            {
                MessageBox.Show(this, L.T("msg.lodopNotRunning"), "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var store = LodopFailureStore.For(LodopFailureStore.DefaultPath);
            foreach (var job in selected)
            {
                try
                {
                    var content = new StringContent(
                        JsonSerializer.Serialize(new { pdfUrl = job.PdfUrl }),
                        Encoding.UTF8, "application/json");
                    var response = await http.PostAsync($"http://localhost:{port}/lodop_print", content);
                    if (response.IsSuccessStatusCode)
                    {
                        store.Remove(job.Id);
                        AppendLog($"Retry: re-queued '{job.PdfUrl}'.");
                    }
                    else
                    {
                        AppendLog($"Retry rejected for '{job.PdfUrl}': HTTP {(int)response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Retry failed for '{job.PdfUrl}': {ex.Message}");
                }
            }
        }
        finally
        {
            btnRetryFailed.Enabled = true;
            RefreshFailureList();
        }
    }

    private void BtnClearFailed_Click(object? sender, EventArgs e)
    {
        var selected = lvFailures.CheckedItems.Cast<ListViewItem>()
            .Select(i => (LodopFailedJob)i.Tag!)
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show(this, L.T("msg.selectFailuresClear"), "Label Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var store = LodopFailureStore.For(LodopFailureStore.DefaultPath);
        foreach (var job in selected)
        {
            store.Remove(job.Id);
            AppendLog($"Cleared failed job '{job.PdfUrl}'.");
        }

        RefreshFailureList(force: true);
    }

    private void SettingsForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _host.LogMessage -= AppendLog;
        L.LanguageChanged -= ApplyLanguage;
        base.OnFormClosed(e);
    }

    private sealed record FormatRow(
        string Size,
        RadioButton Default,
        Label SizeLabel,
        TextBox Url,
        ComboBox Printer,
        ComboBox Type,
        NumericUpDown Port,
        CheckBox Enabled,
        Button Test);
}
