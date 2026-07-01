# LabelPrinter

ControlCode 标签打印客户端 —— Windows 系统托盘程序。

接收 RMA 服务推送的打印指令，通过 RAW 方式发送到本地标签机（Zebra / Eltron 等），也支持 LPT 并口直连。支持三种固定标签尺寸（4×2 / 4×3 / 4×6），每种尺寸可独立绑定打印机、REST 端口与打印类型。

## 环境要求

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 功能

| 功能 | 说明 |
|------|------|
| 多尺寸支持 | 固定三种尺寸 4×2 / 4×3 / 4×6，每种独立配置打印机、REST 端口、打印类型（EPL/ZPL/文本）与启用开关；其中一种可标记为默认（仅用于设置界面高亮，不影响路由） |
| WebSocket 客户端 | 连接 RMA 服务，接收 `LabelPrint` 消息，按别名路由到对应尺寸的打印机 |
| REST 本地接口 | 每个启用的尺寸各自监听一个端口，`POST /LabelPrint`，供本机脚本或其他程序调用 |
| 系统托盘 | 后台常驻，托盘图标显示 WebSocket 连接状态 |
| 设置界面 | 逐尺寸选择打印机 / 类型 / 端口 / 启用状态，显示本机局域网 IP，支持逐尺寸测试打印 |
| 开机自启 | 写入当前用户注册表 `Run` 项 |
| 自动重连 | WebSocket 断线后按配置间隔自动重试 |
| 日志 | 运行日志写入 `logs/labelprinter.log` |

## 架构

```
RMA Server (WebSocket)
        │  LabelPrint|<alias>|<data>
        ▼
┌────────────────────────┐   REST POST :48210/LabelPrint (4x2)
│  LabelPrinter          │◄─ REST POST :48211/LabelPrint (4x3)
│  (系统托盘)              │◄─ REST POST :48212/LabelPrint (4x6)
└───────────┬────────────┘
            │ RAW / LPT（按尺寸各自绑定的打印机）
            ▼
   标签机 (Zebra / Eltron …)
```

## 快速开始

### 构建

```powershell
dotnet build -c Release
```

输出：`bin\Release\net8.0-windows\LabelPrinter.exe`

### 运行

1. 运行 `LabelPrinter.exe`（单实例，重复启动会提示已在托盘运行）
2. 在系统托盘（任务栏右下角 **^**）找到图标
3. 双击图标或右键 **设置…** 打开配置窗口
4. 设置窗口顶部显示本机局域网 IP，供其他机器配置 REST 调用地址参考
5. 为每种尺寸（4×2 / 4×3 / 4×6）选择打印机、打印类型、端口、是否启用，填写 WebSocket 地址，点击 **保存**

每一行尺寸都有独立的 **测试** 按钮，发送与当前打印类型（EPL/ZPL/文本）匹配的样张，验证打印机是否正常。

## 配置

配置文件位于 exe 同目录的 `appsettings.json`，也可在设置界面修改后自动保存。

```json
{
  "LabelPrinter": {
    "LabelPrinterUrl": "ws://your-rma-host:2012/websocket",
    "EnableWebSocket": true,
    "AllowLanAccess": false,
    "ReconnectDelaySeconds": 5,
    "WebSocketConnectTimeoutSeconds": 10,
    "RunAtStartup": false,
    "LabelFormats": [
      { "Size": "4x2", "Alias": "4x2", "PrinterName": "", "PrintType": "Epl", "Port": 48210, "Enabled": true, "IsDefault": false },
      { "Size": "4x3", "Alias": "4x3", "PrinterName": "", "PrintType": "Epl", "Port": 48211, "Enabled": true, "IsDefault": false },
      { "Size": "4x6", "Alias": "4x6", "PrinterName": "", "PrintType": "Epl", "Port": 48212, "Enabled": true, "IsDefault": true }
    ]
  }
}
```

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `LabelPrinterUrl` | RMA WebSocket 地址 | `ws://localhost:2012/websocket` |
| `EnableWebSocket` | 是否启用 WebSocket 客户端 | `true` |
| `AllowLanAccess` | REST 监听地址：`true` 绑定 `http://+:<port>/`（局域网内其他机器可访问，需以管理员身份运行或提前执行 `netsh http add urlacl`）；`false` 仅绑定 `http://localhost:<port>/` | `false` |
| `ReconnectDelaySeconds` | WebSocket 断线重连间隔（秒） | `5` |
| `WebSocketConnectTimeoutSeconds` | WebSocket 连接超时（秒） | `10` |
| `RunAtStartup` | 是否开机自启 | `false` |
| `LabelFormats` | 标签尺寸列表，固定三项（4x2 / 4x3 / 4x6），见下表 | — |

`LabelFormats` 每一项的字段：

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `Size` | 标签尺寸标识，固定为 `4x2` / `4x3` / `4x6` | — |
| `Alias` | WebSocket 消息中的别名，用于路由到该尺寸 | 同 `Size` |
| `PrinterName` | 目标打印机：Windows 打印机名称，或并口 `LPT1` / `LPT2` / `LPT3` | 空 |
| `PrintType` | 打印类型：`Epl` / `Zpl` / `Text` | `Epl` |
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

> 打印类型（EPL/ZPL/文本）是每个尺寸的独立配置，仅影响设置界面里 **测试** 按钮生成的样张内容；通过 WebSocket 或 REST 收到的真实打印数据会原样透传给打印机，不做任何格式转换。

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
├── Program.cs                 # 入口，单实例 Mutex
├── TrayApplicationContext.cs    # 系统托盘与生命周期
├── SettingsForm.cs              # 设置界面（逐尺寸配置行）
├── Config.cs                    # appsettings.json 读写，含旧版单打印机配置迁移
├── PrintHostService.cs          # 打印服务编排，按启用的尺寸各起一个 REST 监听
├── StartupRegistration.cs       # 开机自启注册表
├── Services/
│   ├── WebSocketPrintListener.cs   # 按 alias 路由到对应尺寸
│   ├── RestPrintListener.cs        # 每个 LabelFormat 一个实例，绑定各自端口
│   ├── LabelPrintMessageParser.cs
│   └── NetworkHelper.cs            # 获取本机局域网 IPv4，供设置界面显示
└── Printing/
    ├── PrintModel.cs            # 打印任务分块与打印调度
    ├── RawPrinterHelper.cs      # Windows RAW 打印
    ├── LptPrinter.cs            # LPT 并口输出
    ├── LabelFormat.cs           # 尺寸/打印机/端口/类型 的数据模型
    └── SampleLabelGenerator.cs  # 生成设置界面"测试"按钮用的样张（按 PrintType 与尺寸）
```

## 常见问题

**REST 接口无法访问**

- 确认对应尺寸的 `Enabled` 为 `true`（禁用的尺寸不会监听端口）
- 确认使用的是该尺寸对应的端口（默认 4x2=`48210`，4x3=`48211`，4x6=`48212`）
- 默认 `AllowLanAccess` 为 `false`，仅监听 `http://localhost:<port>/`，其他机器无法访问
- 若需其他机器访问，将 `AllowLanAccess` 设为 `true`（此时绑定 `http://+:<port>/`），并以管理员身份运行，或提前执行 `netsh http add urlacl url=http://+:<port>/ user=Everyone`
- 查看 `logs/labelprinter.log`，端口被占用或权限不足时监听会失败并记录错误

**WebSocket 一直未连接**

- 检查 RMA 服务地址与端口
- 查看 `logs/labelprinter.log` 中的错误信息
- 托盘右键 **重新连接** 手动触发

**打印任务被跳过 / 没有打印**

- WebSocket 消息中的 `alias` 必须与某个**已启用**尺寸的 `Alias` 完全匹配（不区分大小写），否则任务会被跳过并记录警告日志——不会自动落到默认尺寸
- 在设置中确认该尺寸已选择正确的打印机（Windows 打印机名称，或 `LPT1`/`LPT2`/`LPT3`）
- 使用对应尺寸行的 **测试** 按钮验证驱动与 RAW/LPT 打印是否正常

## 许可证

ControlCode 内部使用。
