using LabelPrinter.Printing;
using LabelPrinter.Services;

namespace LabelPrinter;

public sealed class PrintHostService : IDisposable
{
    private AppConfig _config = null!;
    private PrintModel? _printModel;
    private readonly List<RestPrintListener> _restListeners = new();
    private WebSocketPrintListener? _webSocketListener;
    private LodopCompatListener? _lodopCompatListener;

    public event Action<string>? LogMessage;

    public bool IsWebSocketConnected => _webSocketListener?.IsConnected == true;

    public void Start(AppConfig config)
    {
        Stop();
        _config = config;
        _printModel = new PrintModel();
        void Log(string msg) => LogMessage?.Invoke(msg);

        foreach (var format in _config.LabelFormats.Where(f => f.Enabled))
        {
            var listener = new RestPrintListener(format, _config.AllowLanAccess, _printModel, Log);
            listener.Start();
            _restListeners.Add(listener);
        }

        if (_config.EnableWebSocket)
        {
            _webSocketListener = new WebSocketPrintListener(_config, _printModel, Log);
            _webSocketListener.Start();
        }

        if (_config.LodopCompat.Enabled)
        {
            // http://test/fbd.shipswithus.com → localhost:8000 is blocked by Chrome unless
            // the browser allows that origin local/loopback access (see BrowserLocalAccessPolicy).
            BrowserLocalAccessPolicy.TryApply(Log);
            _lodopCompatListener = new LodopCompatListener(_config.LodopCompat, _printModel, Log);
            _lodopCompatListener.Start();
        }

        var ports = string.Join(", ", _config.LabelFormats.Where(f => f.Enabled).Select(f => $"{f.Size}:{f.Port}"));
        var lodopStatus = !_config.LodopCompat.Enabled
            ? "off"
            : _lodopCompatListener!.BoundPorts.Count > 0
                ? string.Join("+", _lodopCompatListener.BoundPorts)
                : "FAILED (see log above)";
        LogMessage?.Invoke($"Running. Ports: {ports}. Lodop-compat: {lodopStatus}.");
    }

    public void Restart(AppConfig config) => Start(config);

    public void Stop()
    {
        if (_webSocketListener != null)
            _webSocketListener.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _webSocketListener = null;

        foreach (var listener in _restListeners)
            listener.Dispose();
        _restListeners.Clear();

        _lodopCompatListener?.Dispose();
        _lodopCompatListener = null;

        _printModel = null;
    }

    public void Dispose() => Stop();
}
