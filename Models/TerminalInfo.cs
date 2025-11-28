namespace PAXTransactionServer.Models;

/// <summary>
/// Información de terminal PAX
/// </summary>
public class TerminalInfo
{
    public string TerminalId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Name { get; set; } = string.Empty;
    public TerminalStatus Status { get; set; }
    public DateTime LastConnected { get; set; }
    public DateTime? LastTransaction { get; set; }
    public int TransactionCount { get; set; }
}

public enum TerminalStatus
{
    Disconnected,
    Connected,
    Processing,
    Error,
    Maintenance
}

