namespace LabelPrinter.Printing;

public enum LabelPrintType
{
    Epl,
    Zpl,
    Text
}

/// <summary>
/// One label size and where/how it prints. Each format owns a REST port
/// and is bound to a printer (a Windows printer name or an "LPT*" port).
/// </summary>
public sealed class LabelFormat
{
    public string Size { get; set; } = "";            // "4x2" / "4x3" / "4x6" (fixed set)
    public string Alias { get; set; } = "";           // WebSocket routing key
    public string PrinterName { get; set; } = "";      // Windows printer OR "LPT1"/"LPT2"/"LPT3"
    public LabelPrintType PrintType { get; set; } = LabelPrintType.Epl;
    public int Port { get; set; }                      // dedicated REST port
    public bool Enabled { get; set; } = true;
    public bool IsDefault { get; set; }                // UI highlight only
}
