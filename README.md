# LabelPrinter

ControlCode 标签打印客户端 —— Windows 系统托盘程序。

接收 RMA 服务推送的打印指令，通过 RAW 方式发送到本地标签机（Zebra / Eltron 等），也支持 LPT 并口直连。支持三种固定标签尺寸（4×2 / 4×3 / 4×6），每种尺寸可独立绑定打印机、REST 端口与打印类型。

## 界面预览
<img width="1452" height="862" alt="image" src="https://github.com/user-attachments/assets/b797d9e9-5d49-49f2-83e9-dbec7c83d51d" />


设置界面：顶部显示本机局域网 IP，每行一种尺寸，含默认标记、调用链接（`http://ip:端口/LabelPrint`）、打印机、类型、端口、启用开关与独立测试按钮。

## 下载

不需要自己编译：在 [Releases 页面](https://github.com/FBD-Groups/LabelPrinter/releases/latest) 下载最新的 `LabelPrinter-win-x64.zip`，解压后直接运行 `LabelPrinter.exe` 即可——自包含发布，已内置 .NET 8 运行时，目标机器无需额外安装任何东西。

当前推荐版本：**[v1.1.2](https://github.com/FBD-Groups/LabelPrinter/releases/tag/v1.1.2)**（PDFium 渲染修空白页 + 连扫队列）。

## 环境要求

从源码构建（非必须，仅当你要修改代码时）：

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 功能

| 功能 | 说明 |
|------|------|
| 多尺寸支持 | 固定三种尺寸 4×2 / 4×3 / 4×6，每种独立配置打印机、REST 端口、打印类型（EPL/ZPL/文本/PDF）与启用开关；其中一种可标记为默认（仅用于设置界面高亮，不影响路由） |
| WebSocket 客户端 | 连接 RMA 服务，接收 `LabelPrint` 消息，按别名路由到对应尺寸的打印机 |
| REST 本地接口 | 每个启用的尺寸各自监听一个端口，`POST /LabelPrint`，供本机脚本或其他程序调用 |
| 系统托盘 | 后台常驻，托盘图标显示 WebSocket 连接状态 |
| 设置界面 | 逐尺寸选择打印机 / 类型 / 端口 / 启用状态，显示本机局域网 IP，支持逐尺寸测试打印 |
| MZL Lodop 兼容 | 模拟 C-Lodop 本机服务，MZL「打印运单」无需改网页；支持 http（8000/18000）与 https（8443/8444） |
| 打印队列 | Lodop 任务先入队再回 `200 Queued`，后台串行打印；崩溃/重启可从磁盘续打，避免连扫翻页丢单 |
| 开机自启 | 写入当前用户注册表 `Run` 项 |
| 自动重连 | WebSocket 断线后按配置间隔自动重试 |
| 日志 | 运行日志（成功/失败）写入 `logs/labelprinter-<日期>.log`；设置页可回看当天日志；失败另有可重打清单 |

## 架构

```
RMA Server (WebSocket)              MZL 浏览器 (C-Lodop API)
        │  LabelPrint|…                      │  localhost:8000 / :8443
        ▼                                    ▼
┌─────────────────────────────────────────────────────┐
│  LabelPrinter（系统托盘）                              │
│   REST :48210–48212  │  Lodop 兼容 + 打印队列         │
└───────────────────────┬─────────────────────────────┘
                        │ RAW / LPT / GDI(PDF)
                        ▼
               标签机 / Windows 打印机
```

## 快速开始

### 构建

```powershell
dotnet build -c Release
```

输出：`bin\Release\net8.0-windows10.0.19041.0\LabelPrinter.exe`

### 运行

1. 运行 `LabelPrinter.exe`（单实例，重复启动会提示已在托盘运行）
2. 在系统托盘（任务栏右下角 **^**）找到图标
3. 双击图标或右键 **设置…** 打开配置窗口
4. 设置窗口顶部显示本机局域网 IP，供其他机器配置 REST 调用地址参考
5. 为每种尺寸（4×2 / 4×3 / 4×6）选择打印机、打印类型、端口、是否启用，填写 WebSocket 地址，点击 **保存**

每一行尺寸都有独立的 **测试** 按钮，发送与当前打印类型（EPL/ZPL/文本/PDF）匹配的样张，验证打印机是否正常。

## 配置

配置文件位于 exe 同目录的 `appsettings.json`，也可在设置界面修改后自动保存。

```json
{
  "LabelPrinter": {
    "LabelPrinterUrl": "ws://your-rma-host:2012/websocket",
    "EnableWebSocket": false,
    "AllowLanAccess": false,
    "ReconnectDelaySeconds": 5,
    "WebSocketConnectTimeoutSeconds": 10,
    "RunAtStartup": false,
    "LabelFormats": [
      { "Size": "4x2", "Alias": "4x2", "PrinterName": "", "PrintType": "Epl", "Port": 48210, "Enabled": true, "IsDefault": false },
      { "Size": "4x3", "Alias": "4x3", "PrinterName": "", "PrintType": "Epl", "Port": 48211, "Enabled": true, "IsDefault": false },
      { "Size": "4x6", "Alias": "4x6", "PrinterName": "", "PrintType": "Epl", "Port": 48212, "Enabled": true, "IsDefault": true }
    ],
    "LodopCompat": {
      "Enabled": false,
      "PrinterName": ""
    }
  }
}
```

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `LabelPrinterUrl` | RMA WebSocket 地址 | `ws://localhost:2012/websocket` |
| `EnableWebSocket` | 是否启用 WebSocket 客户端 | `false` |
| `AllowLanAccess` | REST 监听地址：`true` 绑定 `http://+:<port>/`（局域网内其他机器可访问，需以管理员身份运行或提前执行 `netsh http add urlacl`）；`false` 仅绑定 `http://localhost:<port>/` | `false` |
| `ReconnectDelaySeconds` | WebSocket 断线重连间隔（秒） | `5` |
| `WebSocketConnectTimeoutSeconds` | WebSocket 连接超时（秒） | `10` |
| `RunAtStartup` | 是否开机自启 | `false` |
| `LabelFormats` | 标签尺寸列表，固定三项（4x2 / 4x3 / 4x6），见下表 | — |
| `LodopCompat` | MZL C-Lodop 兼容：`Enabled` / `PrinterName`（建议选能打 PDF 的 Windows 打印机） | 关闭 |

`LabelFormats` 每一项的字段：

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `Size` | 标签尺寸标识，固定为 `4x2` / `4x3` / `4x6` | — |
| `Alias` | WebSocket 消息中的别名，用于路由到该尺寸 | 同 `Size` |
| `PrinterName` | 目标打印机：Windows 打印机名称，或并口 `LPT1` / `LPT2` / `LPT3` | 空 |
| `PrintType` | 打印类型：`Epl` / `Zpl` / `Text` / `Pdf` | `Epl` |
| `Port` | 该尺寸独立的 REST 监听端口 | `4x2`=48210，`4x3`=48211，`4x6`=48212 |
| `Enabled` | 是否启用该尺寸（禁用则不监听 REST，也不会被 WebSocket 路由到） | `true` |
| `IsDefault` | 是否为默认尺寸（仅设置界面单选高亮，不影响打印路由） | 仅 `4x6` 为 `true` |

## 消息格式

### WebSocket

服务端推送文本消息，支持两种格式：

```
LabelPrint {data}
LabelPrint|{alias}|{data}
```

- 多条打印任务可用空行分隔，每段作为一个独立打印作业发送
- `alias` 必须与某个**已启用**尺寸的 `Alias` 匹配（不区分大小写），命中后使用该尺寸的 `PrinterName` 打印
- 若 `alias` 不匹配任何已启用尺寸（包括省略 `alias` 的 `LabelPrint {data}` 形式），该任务会被**跳过**并记录警告日志，不存在回退到默认尺寸的逻辑

### REST

**端点：** `POST http://<host>:<port>/LabelPrint`，其中 `<port>` 决定了打印目标尺寸/打印机（默认 4x2=`48210`，4x3=`48211`，4x6=`48212`，以设置界面或 `appsettings.json` 中的实际值为准）。`<host>` 为 `localhost`（默认）或本机 IP / `+`（开启 `AllowLanAccess` 后）。

**方式一：纯文本**

```
Content-Type: text/plain

N
A20,20,0,4,1,1,N,"Test"
P1
```

**方式二：JSON**

```json
{
  "epl": "N\nA20,20,0,4,1,1,N,\"Test\"\nP1\n"
}
```

请求体中若包含 `alias` 字段会被忽略——REST 请求已经通过监听端口选定了尺寸和打印机，不再需要别名路由。

**响应：** `200 OK` / `400` / `500`，正文为纯文本。

> 打印类型（EPL/ZPL/文本/PDF）是每个尺寸的独立配置，同时决定测试样张的内容和发送方式：
> - **EPL / ZPL**：以 **RAW** 方式把指令字节原样透传给打印机（只有真实标签机 Zebra/Eltron 等能解析），多个标签用空行分隔会拆成多个任务。
> - **文本**：通过打印机的 GDI 驱动渲染成页面（`PrintDocument`），因此在任意 Windows 打印机（Microsoft Print to PDF、激光打印机、标签机）上都能正常打印，而不仅限于标签机；换页符 `\f` 分页。（LPT 并口无驱动，仍按原始字节写入。）
> - **PDF**：请求体须是 PDF 文件字节的 **Base64** 文本。程序用 Windows 自带的 PDF 渲染把每一页画成位图，再走打印机的 GDI 驱动输出（与 Edge 里点打印同一路径），因此普通标签机也能打出 PDF 样张。不支持直接打到 LPT 并口。仓库自带 [`samples/sample-label.pdf`](samples/sample-label.pdf) 可用于测试。
>
> 测试按钮与通过 WebSocket / REST 收到的真实任务，都按对应尺寸的打印类型走上述规则。

### 注意事项：设置类型必须与调用方一致

本机设置里每一行的 **类型**，必须和调用方发来的内容语言一致；程序**不会**根据类型去改写或转换请求体。

| 要点 | 说明 |
|------|------|
| 原样转发 | JSON 字段名 `epl` 只是历史命名，取出的字符串原样发送；不会因为端口设成 ZPL 就把 EPL 转成 ZPL。 |
| EPL ↔ ZPL | 两者发送路径相同（都是 RAW）。端口设为 ZPL 却发送 EPL（或反过来）时，接口仍可能返回 `200 OK`、日志显示成功，但打印机不识别指令，**纸上无输出**。 |
| 文本 / PDF | 发送路径不同（走渲染）。调用方须按该端口类型提交对应格式（纯文本，或 PDF 的 Base64）。 |
| 测试按钮 | 按**该行当前选中的类型**生成样张并发送，与真实 REST / WebSocket 任务同一套规则。测试能出纸，说明该类型与打印机匹配。 |

配置前请确认：该端口绑定的打印机实际工作模式（如驱动名带 `BPL-Z` 多为 ZPL）→ 设置里选同一类型 → 调用方发同一语言的指令。

## MZL / C-Lodop 兼容

MZL「打印运单」走浏览器里的 C-Lodop 脚本。启用设置里的 **MZL 兼容** 行后，本程序会在本机提供与 C-Lodop 相同的入口，网页**不用改**即可把 PDF 打到所选打印机。

| 站点协议 | 本机入口 |
|----------|----------|
| http（如 test.shipswithus.com） | `http://localhost:8000` / `:18000` |
| https（如 fbd.shipswithus.com） | `https://localhost.lodop.net:8443` / `:8444` |

**连扫不丢单：** 收到打印请求后立即入队并返回 `Queued`，后台串行下载 PDF 再打印；队列持久化到 `logs/lodop-print-queue.json`，异常退出后重启可续打。

**日志与失败补打：**

| 位置 | 说明 |
|------|------|
| 设置 → 运行日志 | 当天成功/失败主日志（来自 `logs/labelprinter-<日期>.log`） |
| 设置 → 失败日志 | 未处理失败清单：筛选、全选、打印选中项、清除选中项 |
| `logs/lodop-failures-*.txt` | 日审计（只追加）；超过 30 天的 txt 会自动清理 |
| `logs/lodop-print-failures.json` | 未处理失败池（跨天保留，直到清除或重打入队） |

**使用注意：**

- 浏览器与 LabelPrinter 须在**同一台电脑**；请卸载本机真实 C-Lodop，避免抢端口
- https 站点首次会安装自签证书；若脚本加载失败，**重启浏览器**后再试
- 新版 Chrome 若仍拦本机访问：`chrome://flags/#local-network-access-check` → Disabled
- 「打印成功」表示已交给 Windows 打印队列，不等于纸一定已出

## 测试

### REST（PowerShell，纯文本，以 4x6 默认端口 48212 为例）

```powershell
Invoke-WebRequest `
  -Uri "http://localhost:48212/LabelPrint" `
  -Method POST `
  -ContentType "text/plain" `
  -Body "N`nA20,20,0,4,1,1,N,`"Test`"`nP1`n"
```

### REST（JSON）

```powershell
$body = @{ epl = "N`nA20,20,0,4,1,1,N,`"Test`"`nP1`n" } | ConvertTo-Json
Invoke-WebRequest `
  -Uri "http://localhost:48212/LabelPrint" `
  -Method POST `
  -ContentType "application/json" `
  -Body $body
```

### REST（curl，端口决定尺寸/打印机）

访问哪个端口就打到哪种尺寸绑定的打印机，无需在请求里指定别名：

```bash
# 4x2 → 端口 48210（纯文本）
curl -X POST http://localhost:48210/LabelPrint \
  -H "Content-Type: text/plain" \
  --data-binary $'N\nA20,20,0,4,1,1,N,"Test"\nP1\n'

# 4x6 → 端口 48212（JSON）
curl -X POST http://localhost:48212/LabelPrint \
  -H "Content-Type: application/json" \
  -d '{"epl":"N\nA20,20,0,4,1,1,N,\"Test\"\nP1\n"}'

# 该尺寸打印类型设为 PDF 时 → 请求体为 PDF 文件的 Base64（示例文件见 samples/sample-label.pdf）
curl -X POST http://localhost:48212/LabelPrint \
  -H "Content-Type: text/plain" \
  --data-binary "$(base64 -w0 samples/sample-label.pdf)"
```

> 从其他机器调用时，把 `localhost` 换成设置界面顶部显示的本机 IP，并确保勾选了 **允许局域网访问**（需管理员）。

## 托盘菜单

| 菜单项 | 说明 |
|--------|------|
| 设置… | 打开配置窗口 |
| 重新连接 | 按当前配置重启 WebSocket / REST 服务 |
| 退出 | 关闭程序 |

托盘图标悬停提示显示 WebSocket 状态：`WS:已连接` / `WS:未连接` / `WS:off`。

## 项目结构

```
LabelPrinter/
├── Program.cs / TrayApplicationContext.cs / SettingsForm.cs / Config.cs
├── PrintHostService.cs / FileLog.cs
├── Services/
│   ├── WebSocketPrintListener.cs / RestPrintListener.cs
│   ├── LodopCompatListener.cs      # C-Lodop 兼容 HTTP/HTTPS
│   ├── LodopPrintQueue.cs          # 入队 + 串行打印 + 落盘续打
│   ├── LodopQueueStore.cs / LodopFailureStore.cs / LodopFailureReport.cs
│   ├── LodopCompatCertificate.cs / LodopLoopbackHttpsServer.cs
│   └── NetworkHelper.cs / BrowserLocalAccessPolicy.cs
└── Printing/                       # PrintModel、RAW、LPT、样张生成等
```

## 常见问题

**MZL「打印运单」没反应**

- 确认设置里 **MZL 兼容** 已启用并选了打印机，点该行 **测试**
- 浏览器与本程序同一台电脑；不要同时装真实 C-Lodop
- https 站点：确认证书已信任，必要时重启浏览器
- Chrome 仍拦截本机：见上文 `local-network-access-check`
- 打开设置 → 运行日志 / 失败日志，或查看 `logs/labelprinter-<日期>.log`

**REST 接口无法访问**

- 确认对应尺寸的 `Enabled` 为 `true`（禁用的尺寸不会监听端口）
- 确认使用的是该尺寸对应的端口（默认 4x2=`48210`，4x3=`48211`，4x6=`48212`）
- 默认 `AllowLanAccess` 为 `false`，仅监听 `http://localhost:<port>/`，其他机器无法访问
- 若需其他机器访问，将 `AllowLanAccess` 设为 `true`（此时绑定 `http://+:<port>/`），并以管理员身份运行，或提前执行 `netsh http add urlacl url=http://+:<port>/ user=Everyone`
- 查看 `logs/labelprinter-<日期>.log`，端口被占用或权限不足时监听会失败并记录错误

**WebSocket 一直未连接**

- 检查 RMA 服务地址与端口
- 查看 `logs/labelprinter-<日期>.log` 中的错误信息
- 托盘右键 **重新连接** 手动触发

**打印任务被跳过 / 没有打印**

- WebSocket 消息中的 `alias` 必须与某个**已启用**尺寸的 `Alias` 完全匹配（不区分大小写），否则任务会被跳过并记录警告日志——不会自动落到默认尺寸
- 在设置中确认该尺寸已选择正确的打印机（Windows 打印机名称，或 `LPT1`/`LPT2`/`LPT3`）
- 使用对应尺寸行的 **测试** 按钮验证驱动与 RAW/LPT 打印是否正常

## 许可证

ControlCode 内部使用。
