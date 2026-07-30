namespace LabelPrinter;

public enum AppLanguage
{
    Zh,
    En
}

/// <summary>
/// Tiny in-memory string table for the tray/settings UI. No .resx/satellite assemblies —
/// switching languages just updates <see cref="Current"/> and re-reads control .Text from
/// <see cref="T"/>, so it takes effect immediately without restarting the app.
/// </summary>
public static class L
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Zh;

    public static event Action? LanguageChanged;

    public static void SetLanguage(AppLanguage language)
    {
        if (Current == language)
            return;
        Current = language;
        LanguageChanged?.Invoke();
    }

    public static AppLanguage Parse(string? code) =>
        string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.En : AppLanguage.Zh;

    public static string Code(AppLanguage language) => language == AppLanguage.En ? "en" : "zh";

    private static readonly Dictionary<string, (string Zh, string En)> Map = new()
    {
        ["title"] = ("Label Printer Service - 设置", "Label Printer Service - Settings"),
        ["host"] = ("本机地址", "Local address"),
        ["websocket"] = ("WebSocket:", "WebSocket:"),
        ["enable"] = ("启用", "Enable"),
        ["col.default"] = ("默认", "Default"),
        ["col.size"] = ("尺寸", "Size"),
        ["col.url"] = ("调用链接", "Call URL"),
        ["col.printer"] = ("打印机", "Printer"),
        ["col.type"] = ("类型", "Type"),
        ["col.port"] = ("端口", "Port"),
        ["col.enabled"] = ("启用", "Enabled"),
        ["col.test"] = ("", ""),
        ["btn.test"] = ("测试", "Test"),
        ["btn.testing"] = ("打印中...", "Printing..."),
        ["type.text"] = ("文本", "Text"),
        ["chk.runAtStartup"] = ("开机自启", "Start with Windows"),
        ["chk.allowLan"] = ("允许局域网访问 (需管理员)", "Allow LAN access (admin required)"),
        ["btn.save"] = ("保存并应用", "Save && Apply"),
        ["language"] = ("Language", "Language"),
        ["log.label"] = ("Log:", "Log:"),
        ["log.tab.run"] = ("运行日志", "Run Log"),
        ["log.tab.failures"] = ("失败日志", "Failed Jobs"),
        ["lodop.label"] = ("MZL 兼容", "MZL Support"),

        ["btn.retryFailed"] = ("打印选中项", "Print Selected"),
        ["btn.clearFailed"] = ("清除选中项", "Clear Selected"),
        ["chk.selectAllFailures"] = ("全选", "Select All"),
        ["fail.filter.all"] = ("全部未处理", "All unresolved"),
        ["fail.filter.today"] = ("仅今天", "Today only"),
        ["col.failTime"] = ("时间", "Time"),
        ["col.failReason"] = ("原因", "Reason"),
        ["col.failFile"] = ("文件", "File"),
        ["col.failDetail"] = ("详情", "Detail"),
        ["msg.selectFailures"] = ("请先在列表中选择要重新打印的记录。", "Select one or more failed jobs to retry first."),
        ["msg.selectFailuresClear"] = ("请先在列表中选择要清除的记录。", "Select one or more failed jobs to clear first."),
        ["msg.lodopNotRunning"] = ("MZL 兼容服务未运行，无法重新打印。请先在上方启用并保存。", "The MZL compatibility service isn't running — enable and save it above first."),
        ["fail.busy_timeout"] = ("打印机长时间忙", "Printer busy too long"),
        ["fail.no_printer"] = ("未配置打印机", "No printer configured"),
        ["fail.fetch_failed"] = ("下载 PDF 失败", "Failed to fetch PDF"),
        ["fail.print_failed"] = ("打印失败", "Print failed"),
        ["fail.discarded_on_stop"] = ("服务停止时被丢弃", "Discarded when service stopped"),
        ["fail.queue_full"] = ("队列已满被拒绝", "Rejected — queue full"),

        ["tray.text"] = ("Label Printer", "Label Printer"),
        ["tray.ws.on"] = ("已连接", "connected"),
        ["tray.ws.off"] = ("未连接", "disconnected"),
        ["tray.ws.disabled"] = ("off", "off"),
        ["tray.menu.settings"] = ("设置...", "Settings..."),
        ["tray.menu.reconnect"] = ("重新连接", "Reconnect"),
        ["tray.menu.exit"] = ("退出", "Exit"),
        ["tray.balloon.title"] = ("Label Printer Service", "Label Printer Service"),
        ["tray.balloon.started"] = ("已在系统托盘运行。双击图标可打开设置。", "Running in the system tray. Double-click the icon to open settings."),
        ["tray.balloon.reconnected"] = ("已重新连接。", "Reconnected."),

        ["app.alreadyRunning"] = ("Label Printer 已在运行，请查看系统托盘（任务栏右下角 ^）。", "Label Printer is already running — check the system tray (bottom-right corner ^)."),
    };

    public static string T(string key) =>
        Map.TryGetValue(key, out var value) ? (Current == AppLanguage.En ? value.En : value.Zh) : key;
}
