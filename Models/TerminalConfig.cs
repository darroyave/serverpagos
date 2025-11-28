using System.Text.Json.Serialization;

namespace PAXTransactionServer.Models;

/// <summary>
/// Configuración de terminal para persistencia en archivo JSON
/// </summary>
public class TerminalConfig
{
    [JsonPropertyName("terminalId")]
    public string TerminalId { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; set; } = string.Empty;
    
    [JsonPropertyName("port")]
    public int Port { get; set; } = 10009;
    
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Archivo de configuración de terminales
/// </summary>
public class TerminalsConfigFile
{
    [JsonPropertyName("terminals")]
    public List<TerminalConfig> Terminals { get; set; } = new();
    
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

