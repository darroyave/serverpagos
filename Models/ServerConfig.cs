namespace PAXTransactionServer.Models;

/// <summary>
/// Configuración del servidor
/// </summary>
public class ServerConfig
{
    public ServerSettings ServerSettings { get; set; } = new();
    public TerminalSettings TerminalSettings { get; set; } = new();
    public LogSettings LogSettings { get; set; } = new();
}

public class ServerSettings
{
    public int ServerPort { get; set; } = 8888;
    public int MaxConnections { get; set; } = 35;
    public int ConnectionTimeout { get; set; } = 300000;
    public int TransactionTimeout { get; set; } = 120000;
}

public class TerminalSettings
{
    public int DefaultPort { get; set; } = 10009;
    public int DefaultTimeout { get; set; } = 60000;
    public bool EnableAutoReconnect { get; set; } = true;
    public int ReconnectInterval { get; set; } = 5000;
}

public class LogSettings
{
    public string LogPath { get; set; } = "./Logs";
    public string LogFileName { get; set; } = "PAXServer";
    public int RetentionDays { get; set; } = 30;
    public string LogLevel { get; set; } = "Debug";
}

