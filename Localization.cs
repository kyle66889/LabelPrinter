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

        ["app.replace.prompt"] = (
            "检测到 Label Printer 已在运行：\n{0}\n\n是否关闭旧版并继续启动？\n（旧版正在打印的任务可能被中断。）",
            "Label Printer is already running:\n{0}\n\nClose the running copy and start this one?\n(A job being printed by it may be interrupted.)"),
        ["app.replace.failed"] = (
            "无法关闭旧版：{0}\n\n请在任务栏右下角托盘里右键旧版图标选择退出，或用管理员身份运行本程序。",
            "Could not close the running copy: {0}\n\nRight-click its icon in the system tray and choose Exit, or run this program as administrator."),
        ["app.replace.unknown"] = ("无法确认是否为本程序", "could not confirm it is this program"),
        ["app.replace.stillHeld"] = ("旧版已结束，但单实例锁还没释放，请稍后再试", "The old copy ended but the single-instance lock is still held — try again in a moment"),

        ["log.tab.selfcheck"] = ("自检日志", "Self-check Log"),
        ["btn.selfcheck.tip"] = ("MZL 兼容性自检", "MZL compatibility self-check"),
        ["sc.start"] = ("开始 MZL 兼容性自检...", "Starting MZL compatibility self-check..."),
        ["sc.env"] = ("LabelPrinter v{0}，Windows 用户 {1}", "LabelPrinter v{0}, Windows user {1}"),
        ["sc.solution"] = ("解决方案", "Fix"),
        ["sc.done"] = ("自检完成：{0} 项通过，{1} 项提示，{2} 项警告，{3} 项失败。", "Self-check finished: {0} OK, {1} info, {2} warnings, {3} failed."),
        ["sc.done.clean"] = (
            "没有发现明显问题。如果 MZL 仍无法打印：刷新 MZL 页面，点一次打印，再重新自检；并在该页面按 F12 查看 Console 报错。",
            "No obvious problem found. If MZL still doesn't print: refresh the MZL page, click print once, run the self-check again, and check the Console (F12) on that page for errors."),
        ["sc.copyHint"] = ("提示：点击本日志，Ctrl+A、Ctrl+C 可复制全部内容发给技术支持。", "Tip: click this log, then Ctrl+A / Ctrl+C to copy everything for support."),

        ["sc.svc.title"] = ("兼容服务", "Compat service"),
        ["sc.svc.ok"] = ("已监听 HTTP 8000/18000 和 HTTPS 8443/8444", "Listening on HTTP 8000/18000 and HTTPS 8443/8444"),
        ["sc.svc.none"] = ("服务没有运行（没有监听任何端口）", "Service is not running (no ports bound)"),
        ["sc.svc.none.fix"] = (
            "确认\"MZL 兼容\"行已勾选\"启用\"，点\"保存并应用\"。仍失败：先退出真实 C-Lodop，再重试，并查看运行日志里的 Lodop-compat 报错。",
            "Make sure the \"MZL Support\" row is enabled and click Save & Apply. If it still fails, quit any real C-Lodop, retry, and read the Lodop-compat errors in the run log."),
        ["sc.svc.partial"] = ("部分端口没有监听：{0}", "Some ports are not listening: {0}"),
        ["sc.svc.partial.fix"] = (
            "这些端口可能被真实 C-Lodop 或其他程序占用：退出/卸载 C-Lodop 后，点\"保存并应用\"重启服务。",
            "Those ports may be held by a real C-Lodop or another program: quit/uninstall C-Lodop, then click Save & Apply to restart the service."),

        ["sc.prn.title"] = ("打印机", "Printer"),
        ["sc.prn.ok"] = ("已选择 {0}", "Selected {0}"),
        ["sc.prn.empty"] = ("\"MZL 兼容\"行没有选择打印机", "No printer selected on the \"MZL Support\" row"),
        ["sc.prn.empty.fix"] = ("在\"MZL 兼容\"行选择标签打印机，点\"保存并应用\"。", "Pick the label printer on the \"MZL Support\" row and click Save & Apply."),
        ["sc.prn.missing"] = ("本机找不到打印机 \"{0}\"（未安装或已改名）", "Printer \"{0}\" is not installed on this PC (missing or renamed)"),
        ["sc.prn.missing.fix"] = (
            "在 Windows 设置里确认打印机已安装并在线，然后在\"MZL 兼容\"行重新选择，点\"保存并应用\"。",
            "Check in Windows Settings that the printer is installed and online, then re-select it on the \"MZL Support\" row and click Save & Apply."),

        ["sc.http.title"] = ("HTTP 脚本 (localhost:8000/18000)", "HTTP script (localhost:8000/18000)"),
        ["sc.http.ok"] = ("端口 {0} 正常返回本程序的 CLodopfuncs.js", "Port {0} serves our CLodopfuncs.js"),
        ["sc.http.foreign"] = ("端口 {0} 被其他程序占用（返回的不是本程序的脚本）", "Port {0} is held by another program (not serving our script)"),
        ["sc.http.foreign.fix"] = (
            "多半是真实 C-Lodop 还在运行：在托盘退出 C-Lodop（或在任务管理器结束 CLodop 进程），再点\"保存并应用\"。",
            "Most likely a real C-Lodop is still running: quit it from the tray (or end the CLodop process in Task Manager), then click Save & Apply."),
        ["sc.http.down"] = ("无法连接端口 {0}", "Cannot connect to port {0}"),
        ["sc.http.down.fix"] = ("服务没有监听这些端口，见上面的\"兼容服务\"检查。", "Nothing is listening there — see the \"Compat service\" check above."),

        ["sc.dns.title"] = ("域名 localhost.lodop.net", "Host name localhost.lodop.net"),
        ["sc.dns.ok"] = ("解析到 {0}", "Resolves to {0}"),
        ["sc.dns.fail"] = ("无法解析（{0}）", "Cannot be resolved ({0})"),
        ["sc.dns.wrong"] = ("解析到了 {0}，不是 127.0.0.1", "Resolves to {0}, not 127.0.0.1"),
        ["sc.dns.fix"] = (
            "MZL 的 https 页面通过这个域名访问本程序。用管理员身份编辑 C:\\Windows\\System32\\drivers\\etc\\hosts，加一行：\n127.0.0.1 localhost.lodop.net\n保存后重试。或让 IT 放行该域名（路由器/DNS 的\"DNS 重绑定保护\"会屏蔽解析到 127.0.0.1 的域名）。",
            "MZL's https pages reach this app through that name. As administrator, edit C:\\Windows\\System32\\drivers\\etc\\hosts and add the line:\n127.0.0.1 localhost.lodop.net\nthen retry. Or ask IT to allow the name (router/DNS \"DNS rebinding protection\" blocks names that resolve to 127.0.0.1)."),

        ["sc.proxy.title"] = ("系统代理", "System proxy"),
        ["sc.proxy.ok"] = ("访问 localhost.lodop.net 不会经过代理", "Requests to localhost.lodop.net do not go through a proxy"),
        ["sc.proxy.warn"] = (
            "系统代理 {0} 会接管对 localhost.lodop.net 的访问。浏览器只对 localhost 和 127.0.0.1 本身直连，这个域名不在其中，MZL 的 https 请求可能被送到代理而到不了本机",
            "The system proxy {0} takes over requests to localhost.lodop.net. Browsers only connect directly for localhost and 127.0.0.1 themselves, so MZL's https requests may be sent to the proxy and never reach this PC"),
        ["sc.proxy.fix"] = (
            "在 Windows 设置 → 网络和 Internet → 代理 → 手动设置代理里，在\"请勿对以下条目开头的地址使用代理服务器\"中加入：localhost.lodop.net;127.0.0.1\n如果用的是 PAC 脚本，请让 IT 在 PAC 里对 localhost.lodop.net 返回 DIRECT。",
            "In Windows Settings -> Network & internet -> Proxy -> Manual proxy setup, add to \"Don't use the proxy server for addresses beginning with\": localhost.lodop.net;127.0.0.1\nIf a PAC script is used, ask IT to return DIRECT for localhost.lodop.net."),
        ["sc.proxy.unknown"] = ("无法读取系统代理设置：{0}", "Could not read the system proxy settings: {0}"),

        ["sc.tls.title"] = ("HTTPS 证书 (8443/8444)", "HTTPS certificate (8443/8444)"),
        ["sc.tls.ok"] = ("端口 {0} 的证书受信任，脚本正常", "Certificate on port {0} is trusted and the script loads"),
        ["sc.tls.untrusted"] = ("证书不受信任：{0}", "Certificate is not trusted: {0}"),
        ["sc.tls.untrusted.fix"] = (
            "MZL 的 https 页面要求浏览器信任本程序的证书。重启 LabelPrinter，在弹出的 Windows 安全警告里点\"是\"安装证书。浏览器和 LabelPrinter 必须在同一个 Windows 用户下运行（当前用户：{0}）。\n仍不行：在浏览器打开 https://localhost.lodop.net:8443/CLodopfuncs.js，点\"高级\"→\"继续前往\"。",
            "MZL's https pages need the browser to trust this app's certificate. Restart LabelPrinter and click \"Yes\" on the Windows security warning to install it. The browser and LabelPrinter must run under the same Windows user (current user: {0}).\nIf that fails: open https://localhost.lodop.net:8443/CLodopfuncs.js in the browser and choose Advanced -> Proceed."),
        ["sc.tls.down"] = ("无法连接 HTTPS 端口：{0}", "Cannot connect to the HTTPS port: {0}"),
        ["sc.tls.down.fix"] = (
            "HTTPS 端口没有监听，见\"兼容服务\"检查；也可能是安全软件拦截了本机连接。",
            "The HTTPS port is not listening — see the \"Compat service\" check; security software may also be blocking local connections."),

        ["sc.br.title"] = ("浏览器 {0} {1}", "Browser {0} {1}"),
        ["sc.br.ok"] = ("已通过策略放行 MZL 访问本机", "MZL is allowed to reach this PC by policy"),
        ["sc.br.old"] = ("版本较旧，通常不会拦截 MZL 访问本机", "Older version — normally does not block MZL from reaching this PC"),
        ["sc.br.warn"] = (
            "没有放行策略，浏览器可能拦截 MZL 页面访问本机（如果已在站点设置里手动允许，可忽略）",
            "No allow policy; the browser may block the MZL page from reaching this PC (ignore if you already allowed it in site settings)"),
        ["sc.br.warn.fix"] = (
            "方式一（无需管理员）：打开 MZL 页面，点地址栏左边的图标，把\"本地网络\"/\"本机应用\"改为\"允许\"，刷新页面。\n方式二（需要管理员确认）：点本页上方的\"一键放行浏览器\"按钮。\n方式三：以管理员身份打开 PowerShell，手动运行：\n{0}\n方式二、三完成后，彻底关闭并重新打开浏览器。",
            "Option 1 (no admin): open the MZL page, click the icon left of the address bar, set \"Local network\"/\"Apps on device\" to Allow, refresh.\nOption 2 (needs admin approval): click the \"Allow browser\" button at the top of this page.\nOption 3: open PowerShell as administrator and run:\n{0}\nAfter option 2 or 3, fully close and reopen the browser."),

        ["btn.allowBrowser"] = ("一键放行浏览器（需管理员）", "Allow browser (admin)"),
        ["btn.allowBrowser.tip"] = (
            "弹出管理员确认窗口，把 MZL 网站加入 Chrome/Edge 的放行名单。只在自检提示浏览器需要放行时可用。",
            "Shows an admin confirmation and adds the MZL site to Chrome/Edge's allow list. Only enabled when the self-check says the browser needs it."),
        ["sc.fix.title"] = ("一键放行浏览器", "Allow browser"),
        ["sc.fix.start"] = ("正在请求管理员权限，请在弹出的窗口里点\"是\"...", "Asking for administrator permission — click \"Yes\" in the prompt..."),
        ["sc.fix.cancelled"] = ("已取消，没有做任何修改", "Cancelled — nothing was changed"),
        ["sc.fix.done"] = (
            "已写入放行策略。请彻底关闭所有 Chrome/Edge 窗口（包括托盘里的后台进程）再重新打开，然后点齿轮重新自检。",
            "Allow policy written. Fully close all Chrome/Edge windows (including background processes in the tray), reopen, then click the gear to re-run the self-check."),
        ["sc.fix.startFailed"] = ("无法启动管理员 PowerShell：{0}", "Could not start an elevated PowerShell: {0}"),
        ["sc.fix.notApplied"] = ("脚本已运行，但放行策略没有写入（退出码 {0}）", "The script ran but the allow policy was not written (exit code {0})"),
        ["sc.fix.itPolicy"] = (
            "可能被公司安全策略（AppLocker、禁止脚本、组策略）拦截。请让 IT 在 Chrome 和 Edge 的组策略里添加 LocalNetworkAccessAllowedForUrls，内容：\nhttps://[*.]shipswithus.com\nhttp://[*.]shipswithus.com",
            "It may be blocked by company security policy (AppLocker, script blocking, group policy). Ask IT to add LocalNetworkAccessAllowedForUrls to Chrome's and Edge's group policy with:\nhttps://[*.]shipswithus.com\nhttp://[*.]shipswithus.com"),

        ["sc.old.title"] = ("旧版 C-Lodop", "Old C-Lodop"),
        ["sc.old.none"] = ("没有发现安装或运行中的 C-Lodop", "No installed or running C-Lodop found"),
        ["sc.old.running"] = ("C-Lodop 正在运行：{0}。它会占用 8000/18000/8443/8444 端口，MZL 的请求会被它接走，而不是本程序", "C-Lodop is running: {0}. It holds ports 8000/18000/8443/8444, so MZL's requests go to it instead of this app"),
        ["sc.old.installed"] = ("C-Lodop 已安装但没在运行：{0}。它开机自启后会抢占端口，导致本程序监听失败", "C-Lodop is installed but not running: {0}. If it starts at login it will take the ports and this app can't listen"),
        ["sc.old.fix"] = (
            "1) 在任务栏右下角托盘找到 C-Lodop 图标，右键退出（找不到就在任务管理器结束 CLodop 开头的进程）。\n2) 打开 Windows 设置 → 应用 → 已安装的应用，找到 C-Lodop / Lodop，点卸载。\n3) 回到本程序，点\"保存并应用\"，再点齿轮重新自检。",
            "1) Right-click the C-Lodop icon in the system tray and exit (or end the CLodop* processes in Task Manager).\n2) Open Windows Settings -> Apps -> Installed apps, find C-Lodop / Lodop and uninstall it.\n3) Back in this app click Save & Apply, then click the gear to re-run the self-check."),
        ["sc.br.none.title"] = ("浏览器", "Browser"),
        ["sc.br.none"] = ("没有检测到 Chrome / Edge", "Chrome / Edge not detected"),

        ["sc.site.title"] = ("站点权限 {0}", "Site permission {0}"),
        ["sc.site.allow"] = ("允许", "Allow"),
        ["sc.site.block"] = ("阻止", "Block"),
        ["sc.site.ask"] = ("每次询问", "Ask"),
        ["sc.site.cat.local"] = ("本地网络", "Local network"),
        ["sc.site.cat.loopback"] = ("本机应用", "Apps on device"),
        ["sc.site.ok"] = ("MZL 站点已被允许访问本机（{0}）", "The MZL site is allowed to reach this PC ({0})"),
        ["sc.site.pending"] = ("MZL 站点的权限还不是\"允许\"（{0}）", "The MZL site permission is not \"Allow\" yet ({0})"),
        ["sc.site.blocked"] = ("MZL 站点被设为\"阻止\"访问本机：{0}", "The MZL site is set to \"Block\" for this PC: {0}"),
        ["sc.site.blocked.fix"] = (
            "这就是 MZL 点打印没反应的原因。打开 MZL 页面，点地址栏左边的图标，把\"本地网络\"/\"本机应用\"改为\"允许\"（或点\"重置权限\"，下次弹窗时选\"允许\"），然后刷新页面。\n（读取自浏览器配置文件，仅供参考。）",
            "This is why MZL's print button does nothing. Open the MZL page, click the icon left of the address bar and set \"Local network\"/\"Apps on device\" to Allow (or click Reset permissions and choose Allow at the next prompt), then refresh.\n(Read from the browser profile; advisory only.)"),
        ["sc.site.mixed"] = (
            "有的浏览器用户配置里 MZL 被设为\"阻止\"（{0}），有的已经允许：{1}。如果你打印时用的正是被阻止的那个配置，MZL 点打印就会没反应",
            "MZL is set to \"Block\" in some browser profiles ({0}) but allowed in others: {1}. If you print from a blocked profile, MZL's print button will do nothing"),
        ["sc.site.none"] = ("浏览器里还没有保存 MZL 站点的权限", "The browser has no saved permission for the MZL site yet"),
        ["sc.site.none.fix"] = (
            "首次打印时浏览器会弹出访问本地网络/本机应用的提示，请点\"允许\"。如果没有弹出且打不了印，手动在地址栏左边的图标里设为\"允许\"。\n（读取自浏览器配置文件，仅供参考。）",
            "On the first print the browser shows a local-network / apps-on-device prompt — click Allow. If none appears and printing fails, set it to Allow from the icon left of the address bar.\n(Read from the browser profile; advisory only.)"),

        ["sc.pol.title"] = ("程序自动设置浏览器", "App auto-configures browser"),
        ["sc.pol.ok"] = ("程序可以自动设置浏览器的放行策略", "The app can configure the browser allow policy by itself"),
        ["sc.pol.denied"] = (
            "这台电脑不允许程序自动设置浏览器（启动日志里的 Browser policy 报错就是这个）。不影响打印",
            "This PC does not let the app configure the browser by itself (that is the \"Browser policy\" line in the startup log). Printing is not affected"),
        ["sc.pol.denied.fix"] = (
            "不用处理，除非上面\"浏览器\"或\"站点权限\"项是 WARN/FAIL。那时按它们给出的方法放行即可。",
            "Nothing to do unless the \"Browser\" or \"Site permission\" checks above show WARN/FAIL — then follow the fix they give."),

        ["sc.req.title"] = ("浏览器请求", "Browser requests"),
        ["sc.req.none"] = ("自程序启动以来，没有收到任何来自浏览器页面的请求", "No request from a browser page has reached this app since it started"),
        ["sc.req.none.fix"] = (
            "1) 在 MZL 页面按 F5 刷新，点一次打印，再点齿轮重新自检。\n2) 仍为 0：请求被浏览器拦截，或域名/证书有问题，看上面的域名、证书、浏览器几项；在 MZL 页面按 F12 → Console，找 blocked / ERR_CERT / ERR_NAME_NOT_RESOLVED 报错。",
            "1) Refresh the MZL page (F5), click print once, then click the gear to re-run.\n2) Still zero: the browser is blocking the request, or the name/certificate is wrong — see the DNS, certificate and browser checks above; on the MZL page press F12 -> Console and look for blocked / ERR_CERT / ERR_NAME_NOT_RESOLVED."),
        ["sc.req.scriptOnly"] = ("浏览器已访问本程序（脚本 {0} 次，预检 {1} 次），但没有收到打印请求", "The browser reached this app (script {0}x, preflight {1}x) but no print request arrived"),
        ["sc.req.scriptOnly.fix"] = (
            "点一次打印后重新自检；仍没有：在 MZL 页面按 F12 → Console 看 lodop_print 报错（blocked 表示被浏览器拦截）。",
            "Click print once and re-run; if still none, press F12 -> Console on the MZL page and look for lodop_print errors (\"blocked\" means the browser stopped it)."),
        ["sc.req.ok"] = ("已收到 {0} 次打印请求（最近 {1}，来自 {2}）", "Received {0} print request(s) (latest {1}, from {2})"),
    };

    public static string T(string key) =>
        Map.TryGetValue(key, out var value) ? (Current == AppLanguage.En ? value.En : value.Zh) : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);
}
