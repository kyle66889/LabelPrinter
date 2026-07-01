using System.Drawing.Printing;
using LabelPrinter.Printing;
using LabelPrinter.Services;

namespace LabelPrinter;

public partial class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly PrintHostService _host;
    private readonly List<FormatRow> _rows = new();
    private readonly List<string> _printerChoices = new();
    private string _localIp = "127.0.0.1";

    public event Action<AppConfig>? ConfigSaved;

    public SettingsForm(AppConfig config, PrintHostService host)
    {
        _config = config;
        _host = host;
        InitializeComponent();
        LoadUi();
        _host.LogMessage += AppendLog;
    }

    private void LoadUi()
    {
        _localIp = NetworkHelper.GetLocalIPv4();
        lblHost.Text = $"本机地址: {_localIp}";

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

        BuildHeaderRow();
        foreach (var format in _config.LabelFormats)
            AddFormatRow(format);
    }

    private void BuildHeaderRow()
    {
        string[] headers = { "默认", "尺寸", "调用链接", "打印机", "类型", "端口", "启用", "" };
        for (var col = 0; col < headers.Length; col++)
        {
            var lbl = new Label
            {
                Text = headers[col],
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(3, 3, 3, 3)
            };
            tlpFormats.Controls.Add(lbl, col, 0);
        }
    }

    private void AddFormatRow(LabelFormat format)
    {
        var rowIndex = tlpFormats.RowCount;
        tlpFormats.RowCount = rowIndex + 1;
        tlpFormats.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

        var rdoDefault = new RadioButton { AutoSize = true, Checked = format.IsDefault, Anchor = AnchorStyles.Left };
        var lblSize = new Label { Text = format.Size, AutoSize = true, Anchor = AnchorStyles.Left };

        var numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Math.Clamp(format.Port, 1, 65535), Anchor = AnchorStyles.Left, Width = 70 };

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
        cboType.Items.AddRange(new object[] { "EPL", "ZPL", "文本" });
        cboType.SelectedIndex = (int)format.PrintType;

        var chkEnabled = new CheckBox { Checked = format.Enabled, AutoSize = true, Anchor = AnchorStyles.Left };

        var btnTest = new Button { Text = "测试", Anchor = AnchorStyles.Left, Width = 56 };

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

    private string BuildUrl(int port) => $"http://{_localIp}:{port}/LabelPrint";

    private void TestRow(FormatRow row)
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
        try
        {
            new PrintModel().PrintTo(sample, printerName, type);
            AppendLog($"Test [{row.Size}/{type}] sent to {printerName}.");
        }
        catch (Exception ex)
        {
            AppendLog($"Test [{row.Size}] failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        foreach (var row in _rows)
        {
            var format = _config.LabelFormats.First(f => f.Size == row.Size);
            format.PrinterName = (string?)row.Printer.SelectedItem ?? "";
            format.PrintType = (LabelPrintType)row.Type.SelectedIndex;
            format.Port = (int)row.Port.Value;
            format.Enabled = row.Enabled.Checked;
            format.IsDefault = row.Default.Checked;
        }
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
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
