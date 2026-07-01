#nullable disable
namespace LabelPrinter;

partial class SettingsForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblHost = new Label();
        lblWsUrl = new Label();
        txtWsUrl = new TextBox();
        chkEnableWebSocket = new CheckBox();
        tlpFormats = new TableLayoutPanel();
        chkRunAtStartup = new CheckBox();
        chkAllowLan = new CheckBox();
        btnSave = new Button();
        lblLog = new Label();
        txtLog = new TextBox();
        SuspendLayout();
        //
        // lblHost
        //
        lblHost.AutoSize = true;
        lblHost.Location = new Point(16, 16);
        lblHost.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        lblHost.Text = "本机地址: ...";
        //
        // lblWsUrl
        //
        lblWsUrl.AutoSize = true;
        lblWsUrl.Location = new Point(16, 46);
        lblWsUrl.Text = "WebSocket:";
        //
        // txtWsUrl
        //
        txtWsUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtWsUrl.Location = new Point(170, 42);
        txtWsUrl.Size = new Size(674, 23);
        //
        // chkEnableWebSocket
        //
        chkEnableWebSocket.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        chkEnableWebSocket.AutoSize = true;
        chkEnableWebSocket.Location = new Point(110, 44);
        chkEnableWebSocket.Text = "启用";
        chkEnableWebSocket.CheckedChanged += (_, _) => txtWsUrl.Enabled = chkEnableWebSocket.Checked;
        //
        // tlpFormats
        //
        tlpFormats.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        tlpFormats.Location = new Point(16, 76);
        tlpFormats.Size = new Size(828, 132);
        tlpFormats.ColumnCount = 9;
        tlpFormats.RowCount = 1;
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));    // default radio
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));    // size
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));   // call URL
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));   // printer
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));    // type
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));    // port
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));    // enabled
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));    // test
        tlpFormats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));    // spacer (absorbs extra width)
        //
        // chkRunAtStartup
        //
        chkRunAtStartup.AutoSize = true;
        chkRunAtStartup.Location = new Point(16, 220);
        chkRunAtStartup.Text = "开机自启";
        //
        // chkAllowLan
        //
        chkAllowLan.AutoSize = true;
        chkAllowLan.Location = new Point(110, 220);
        chkAllowLan.Text = "允许局域网访问 (需管理员)";
        //
        // btnSave
        //
        btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnSave.Location = new Point(748, 214);
        btnSave.Size = new Size(96, 28);
        btnSave.Text = "保存并应用";
        btnSave.Click += BtnSave_Click;
        //
        // lblLog
        //
        lblLog.AutoSize = true;
        lblLog.Location = new Point(16, 252);
        lblLog.Text = "Log:";
        //
        // txtLog
        //
        txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtLog.Font = new Font("Consolas", 9F);
        txtLog.Location = new Point(16, 272);
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Size = new Size(828, 150);
        //
        // SettingsForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(860, 440);
        Controls.Add(txtLog);
        Controls.Add(lblLog);
        Controls.Add(btnSave);
        Controls.Add(chkAllowLan);
        Controls.Add(chkRunAtStartup);
        Controls.Add(tlpFormats);
        Controls.Add(chkEnableWebSocket);
        Controls.Add(txtWsUrl);
        Controls.Add(lblWsUrl);
        Controls.Add(lblHost);
        MinimumSize = new Size(800, 440);
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "ControlCode Label Printer - 设置";
        FormClosing += SettingsForm_FormClosing;
        ResumeLayout(false);
        PerformLayout();
    }

    private Label lblHost;
    private Label lblWsUrl;
    private TextBox txtWsUrl;
    private CheckBox chkEnableWebSocket;
    private TableLayoutPanel tlpFormats;
    private CheckBox chkRunAtStartup;
    private CheckBox chkAllowLan;
    private Button btnSave;
    private Label lblLog;
    private TextBox txtLog;
}
