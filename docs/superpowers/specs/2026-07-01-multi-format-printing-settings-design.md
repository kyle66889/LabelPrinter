# 设计文档：多尺寸标签打印与设置界面改进

- 日期：2026-07-01
- 项目：LabelPrinter（ControlCode 标签打印客户端）
- 状态：已批准设计，待写实现计划

## 背景

当前程序是一个**字节中继**：RMA 服务器（WebSocket）或本机脚本（REST）发来完整的 EPL 指令，程序原样转发给打印机（`PrintModel.PrintBarcode` → `RawPrinterHelper` / `LptPrinter`）。配置里只有单台打印机、单个 REST 端点、全局 LPT 开关。

本次改进要支持**多种标签尺寸各自独立打印**，并改进设置界面。

## 需求（源自用户）

1. 支持三种标签尺寸：4×2、4×3、4×6，可选一种作为默认。
2. 界面显示本机 IP 地址和端口；默认端口选不常用的。
3. 每种尺寸各有一个测试按钮。
4. 可选择打印类型：EPL / ZPL / 普通文本。

## 核心架构决策

经讨论确定：

- **每个尺寸 = 一个专属 REST 端口 + 绑定一台物理打印机**。调用方靠"访问哪个端口"决定打印哪种尺寸/发到哪台机器，无需在报文里指定。
- **三种尺寸固定**（4×2 / 4×3 / 4×6），不可增删；每种可独立配置打印机、端口、类型、启用。
- **打印类型按尺寸各自设置**（不同机型本就是不同语言，如 Zebra=ZPL、Eltron=EPL）。
- **默认尺寸只做 UI 高亮/预选**，不影响任何路由。
- **WebSocket 保留**，靠消息里的 `alias` 匹配尺寸的别名来选打印机。
- 程序仍是**字节中继**：真实任务原样转发，不改写内容。

## 详细设计

### 1. 配置模型（`Config.cs`）

新增固定三条 `LabelFormat` 列表。每条字段：

| 字段 | 类型 | 说明 | 默认 |
|------|------|------|------|
| `Size` | string | 固定名 `4x2` / `4x3` / `4x6`（不可增删） | — |
| `Alias` | string | WebSocket 路由用，匹配消息中的 alias | 同 `Size` |
| `PrinterName` | string | 绑定的目标：Windows 打印机名 **或** `LPT1`/`LPT2`/`LPT3` | 空 |
| `PrintType` | enum `LabelPrintType` | `Epl` / `Zpl` / `Text` | `Epl` |
| `Port` | int | 专属 REST 监听端口 | 4×2=48210, 4×3=48211, 4×6=48212 |
| `Enabled` | bool | 是否开这个端口 | true |
| `IsDefault` | bool | 仅 UI 高亮，无功能作用 | 4×6=true |

端口默认值选择理由：48210–48212 位于已注册端口之上、Windows 临时端口段（49152+）之下，极少与常见服务或临时端口冲突。

全局字段保留：`LabelPrinterUrl`、`EnableWebSocket`、`RunAtStartup`、`ReconnectDelaySeconds`、`WebSocketConnectTimeoutSeconds`。

全局字段新增：`AllowLanAccess`（bool，默认 false）。

**移除**的旧字段：`PrinterName`、`PrinterAlias`、`UseLptPrinter`、`LptPort`、`RestListenPrefix`、`EnableRestEndpoint`（其职责被 `LabelFormat` 列表 + `AllowLanAccess` 取代）。

**向后兼容迁移**：`AppConfig.Load()` 若发现旧结构（存在旧 `PrinterName` 而无 `LabelFormats`），自动生成三条尺寸，并把旧 `PrinterName` 填入默认那条（4×6）；`UseLptPrinter=true` 时把旧 `LptPort` 作为 4×6 的 `PrinterName`。`Save()` 一律写新结构。

### 2. REST —— 每尺寸一个监听器

- `RestPrintListener` 改为**每个 `Enabled` 的尺寸起一个监听实例**，各自独占 `Port`，路径仍为 `POST /LabelPrint`。
- 打到某端口的任务 → 转发到该尺寸绑定的打印机，**字节原样转发**，不改写内容。
- 请求体仍支持两种：纯文本（整个 body 即指令）或 JSON `{"epl":"..."}`。因为端口已决定目标打印机，**REST 不再需要 body 里的 alias**（若带了也忽略）。
- 绑定地址由 `AllowLanAccess` 决定：
  - `false` → `http://localhost:{port}/`（仅本机可访问）
  - `true` → `http://+:{port}/`（其他机器可访问，需管理员权限或预先 `netsh http add urlacl`）
- 绑定失败（如权限不足）时，日志输出明确提示（提及管理员 / urlacl），不崩溃，其他端口继续工作。
- 一个宿主管理多个监听器：新增 `RestPrintListenerHost`（或在 `PrintHostService` 内）统一 Start/Stop 所有尺寸的监听器。

### 3. WebSocket

- 保留单个 WebSocket 客户端连接 RMA 服务器。
- 收到 `LabelPrint|alias|epl` 或 `LabelPrint {epl}` → 解析 alias。
- alias 与某尺寸的 `Alias` 匹配（忽略大小写）→ 发到该尺寸绑定的打印机。
- **alias 缺失或匹配不上 → 记一条警告日志并跳过**（不打印、不猜测目标）。默认尺寸不参与兜底。
- `LabelPrintMessageParser` 已能解析 alias，无需改动；路由逻辑放在消费侧（`WebSocketPrintListener` 或 `PrintModel`）。

### 4. 打印类型的作用范围

因真实任务原样转发，`PrintType` **不改写**进站数据。它只用于两处：

1. **测试按钮**：按类型生成对应语言的样张。
2. **界面显示**：标示该端口/打印机是什么语言。

真实 REST/WebSocket 任务的指令由调用方/服务器自带。

### 5. 界面（`SettingsForm`，表格式布局）

用 `TableLayoutPanel` 手工排 3 行真实控件（比 `DataGridView` 单元格编辑更稳，且只有 3 行）。

- **顶部**：`本机地址 192.168.x.x` —— 自动探测局域网 IPv4，窗口打开时刷新；多网卡时取主 IP，其余可在悬停提示列出。
- **WebSocket 行**：地址文本框 + `启用` 勾选（勾选控制地址框可用）。
- **尺寸表格**，列：`默认(单选按钮) | 尺寸 | 打印机(下拉) | 类型(下拉) | 端口 | 启用 | 测试`，共 3 行。
  - 打印机下拉：列出 `PrinterSettings.InstalledPrinters` + `LPT1`/`LPT2`/`LPT3`。
  - 类型下拉：EPL / ZPL / 文本。
  - 端口：数字输入（校验 1–65535）。
  - 默认单选：三行互斥，仅高亮。
- **底部**：`开机自启` 勾选、`允许局域网访问（需管理员）` 勾选、`保存并应用` 按钮。
- **日志框**：只读多行，保持现状。

### 6. 测试按钮

每行"测试"→ 先套用该行当前设置，按该行 **类型 + 尺寸** 生成样张，发到该行打印机（Windows 或 LPT）。

样张尺寸按 203 dpi 换算点数：4in=812、2in=406、3in=609、6in=1218。

- **EPL**：`N` + `q{宽点}` + `Q{高点},{gap}` + `A{x},{y},0,4,1,1,N,"TEST {size}"` + `P1`
- **ZPL**：`^XA^PW{宽点}^LL{高点}^FO50,50^A0N,40,40^FDTEST {size}^FS^XZ`
- **文本**：几行纯文本（含尺寸名与时间戳）+ 换页符 `\f`

生成逻辑独立成一个纯函数类（如 `SampleLabelGenerator`），便于单元测试。

## 涉及文件

- `Config.cs` —— 新增 `LabelFormat`、`LabelPrintType`、迁移与读写。
- `Services/RestPrintListener.cs` —— 改为按尺寸多实例；新增宿主管理。
- `Services/WebSocketPrintListener.cs` —— alias→尺寸 路由，匹配不上跳过。
- `Printing/PrintModel.cs` —— 目标可为 Windows 打印机或 LPT（按 `PrinterName` 前缀判断）。
- `Printing/SampleLabelGenerator.cs` —— 新增，样张生成。
- `SettingsForm.cs` / `SettingsForm.Designer.cs` —— 表格式布局、IP 显示、每行测试、LAN 开关。
- `PrintHostService.cs` —— 编排多监听器。
- 新增网络辅助：探测本机 IPv4。
- `README.md` / `appsettings.json` —— 更新文档与示例。

## 测试计划

**单元测试**
- alias → 尺寸 映射（含匹配不上返回空）。
- `SampleLabelGenerator`：3 类型 × 3 尺寸的输出正确（含点数换算）。
- 配置加载/保存往返；旧版结构迁移正确。
- 本机 IPv4 探测返回非空。

**手动测试**
- 三个测试按钮各自出对应尺寸样张。
- `POST` 到三个端口分别进对应打印机。
- WebSocket alias 命中路由正确；alias 匹配不上时日志有警告且不打印。
- `AllowLanAccess` 开关：关时仅本机、开时其他机器可访问（并验证权限不足时的日志提示）。

## 非目标（YAGNI）

- 不做自定义尺寸增删（固定三种）。
- 不做客户端标签模板引擎（不改写真实任务内容）。
- 不做默认尺寸参与路由兜底。
