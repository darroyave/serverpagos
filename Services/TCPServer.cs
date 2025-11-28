using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using PAXTransactionServer.Models;
using Serilog;

namespace PAXTransactionServer.Services;

/// <summary>
/// Servidor TCP que maneja múltiples conexiones concurrentes
/// </summary>
public class TCPServer : IDisposable
{
    private readonly ServerSettings _settings;
    private readonly TerminalManager _terminalManager;
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients;
    private bool _isRunning;
    private bool _disposed;

    public bool IsRunning => _isRunning;
    public int ConnectedClients => _clients.Count;

    public TCPServer(ServerSettings settings, TerminalManager terminalManager)
    {
        _settings = settings;
        _terminalManager = terminalManager;
        _clients = new ConcurrentDictionary<string, ClientConnection>();
    }

    /// <summary>
    /// Inicia el servidor TCP
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
        {
            Log.Warning("El servidor ya está en ejecución");
            return;
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _settings.ServerPort);
            _listener.Start();
            _isRunning = true;

            Log.Information($"🚀 Servidor TCP iniciado en puerto {_settings.ServerPort}");
            Log.Information($"📡 Máximo de conexiones: {_settings.MaxConnections}");

            // Iniciar tarea de monitoreo de salud
            _ = Task.Run(() => MonitorHealthAsync(_cancellationTokenSource.Token));

            // Aceptar conexiones
            await AcceptClientsAsync(_cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error iniciando servidor TCP");
            _isRunning = false;
            throw;
        }
    }

    /// <summary>
    /// Detiene el servidor TCP
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        Log.Information("🛑 Deteniendo servidor TCP...");

        _cancellationTokenSource?.Cancel();
        _isRunning = false;

        // Desconectar todos los clientes
        var disconnectTasks = _clients.Values.Select(c => c.DisconnectAsync());
        await Task.WhenAll(disconnectTasks);
        
        _clients.Clear();
        _listener?.Stop();

        Log.Information("✅ Servidor TCP detenido");
    }

    /// <summary>
    /// Acepta conexiones de clientes
    /// </summary>
    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener != null)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                
                if (_clients.Count >= _settings.MaxConnections)
                {
                    Log.Warning("Máximo de conexiones alcanzado. Rechazando cliente");
                    tcpClient.Close();
                    continue;
                }

                _ = Task.Run(() => HandleClientAsync(tcpClient, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error aceptando cliente");
            }
        }
    }

    /// <summary>
    /// Maneja la comunicación con un cliente
    /// </summary>
    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString();
        var connection = new ClientConnection(clientId, tcpClient);
        
        if (!_clients.TryAdd(clientId, connection))
        {
            await connection.DisconnectAsync();
            return;
        }

        try
        {
            var endpoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            Log.Information($"✅ Cliente conectado: {clientId} desde {endpoint}");

            using var stream = tcpClient.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // Enviar mensaje de bienvenida
            var welcome = new
            {
                Type = "WELCOME",
                Message = "Conectado al servidor PAX Transaction",
                ClientId = clientId,
                ServerVersion = "1.0.0",
                MaxTerminals = 35
            };
            await writer.WriteLineAsync(JsonConvert.SerializeObject(welcome));

            // Procesar comandos
            while (!cancellationToken.IsCancellationRequested && tcpClient.Connected)
            {
                Log.Debug($"Cliente {clientId}: Esperando comando...");
                var line = await reader.ReadLineAsync();
                Log.Debug($"Cliente {clientId}: Recibido: {line?.Substring(0, Math.Min(100, line?.Length ?? 0))}");
                
                if (string.IsNullOrEmpty(line))
                {
                    Log.Debug($"Cliente {clientId}: Linea vacia, cerrando conexion");
                    break;
                }

                connection.LastActivity = DateTime.Now;
                Log.Debug($"Cliente {clientId}: Procesando comando...");
                var response = await ProcessCommandAsync(line);
                Log.Debug($"Cliente {clientId}: Enviando respuesta...");
                
                // Verificar si el cliente sigue conectado antes de enviar
                if (!tcpClient.Connected)
                {
                    Log.Warning($"Cliente {clientId}: Desconectado antes de enviar respuesta");
                    break;
                }
                
                try
                {
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(response));
                    Log.Debug($"Cliente {clientId}: Respuesta enviada");
                }
                catch (IOException ioEx)
                {
                    Log.Warning($"Cliente {clientId}: Error de I/O al enviar respuesta (cliente desconectado): {ioEx.Message}");
                    break;
                }
            }
        }
        catch (IOException ioEx)
        {
            Log.Warning($"Cliente {clientId}: Desconexión inesperada - {ioEx.Message}");
        }
        catch (ObjectDisposedException)
        {
            Log.Warning($"Cliente {clientId}: Stream ya cerrado");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error manejando cliente {clientId}");
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            await connection.DisconnectAsync();
            Log.Information($"❌ Cliente desconectado: {clientId}");
        }
    }

    /// <summary>
    /// Procesa comandos recibidos del cliente
    /// </summary>
    private async Task<object> ProcessCommandAsync(string commandJson)
    {
        try
        {
            var command = JsonConvert.DeserializeObject<Dictionary<string, object>>(commandJson);
            
            if (command == null || !command.ContainsKey("Command"))
            {
                return CreateErrorResponse("Comando inválido");
            }

            var commandType = command["Command"].ToString();

            return commandType?.ToUpper() switch
            {
                "REGISTER_TERMINAL" => await HandleRegisterTerminalAsync(command),
                "UNREGISTER_TERMINAL" => await HandleUnregisterTerminalAsync(command),
                "TRANSACTION" => await HandleTransactionAsync(command),
                "LIST_TERMINALS" => HandleListTerminals(),
                "TERMINAL_STATUS" => HandleTerminalStatus(command),
                "SERVER_STATUS" => HandleServerStatus(),
                "PING" => new { Type = "PONG", Timestamp = DateTime.Now },
                _ => CreateErrorResponse($"Comando no reconocido: {commandType}")
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error procesando comando");
            return CreateErrorResponse($"Error: {ex.Message}");
        }
    }

    private async Task<object> HandleRegisterTerminalAsync(Dictionary<string, object> command)
    {
        try
        {
            var terminalJson = JsonConvert.SerializeObject(command["Terminal"]);
            var terminal = JsonConvert.DeserializeObject<TerminalInfo>(terminalJson);

            if (terminal == null)
                return CreateErrorResponse("Datos de terminal inválidos");

            var success = await _terminalManager.RegisterTerminalAsync(terminal);

            return new
            {
                Type = "REGISTER_RESPONSE",
                Success = success,
                Message = success ? "Terminal registrada exitosamente" : "Error registrando terminal",
                TerminalId = terminal.TerminalId
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error registrando terminal: {ex.Message}");
        }
    }

    private async Task<object> HandleUnregisterTerminalAsync(Dictionary<string, object> command)
    {
        try
        {
            var terminalId = command["TerminalId"].ToString();
            if (string.IsNullOrEmpty(terminalId))
                return CreateErrorResponse("TerminalId requerido");

            var success = await _terminalManager.UnregisterTerminalAsync(terminalId);

            return new
            {
                Type = "UNREGISTER_RESPONSE",
                Success = success,
                Message = success ? "Terminal eliminada" : "Terminal no encontrada",
                TerminalId = terminalId
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error eliminando terminal: {ex.Message}");
        }
    }

    private async Task<object> HandleTransactionAsync(Dictionary<string, object> command)
    {
        try
        {
            Log.Debug("HandleTransactionAsync: Iniciando...");
            var requestJson = JsonConvert.SerializeObject(command["Request"]);
            Log.Debug($"HandleTransactionAsync: Request JSON: {requestJson?.Substring(0, Math.Min(100, requestJson?.Length ?? 0))}");
            
            var request = JsonConvert.DeserializeObject<TransactionRequest>(requestJson);
            Log.Debug($"HandleTransactionAsync: Request deserializado, TerminalId={request?.TerminalId}, Amount={request?.Amount}");

            if (request == null)
                return CreateErrorResponse("Datos de transacción inválidos");

            Log.Debug($"HandleTransactionAsync: Llamando a ProcessTransactionAsync...");
            var response = await _terminalManager.ProcessTransactionAsync(request);
            Log.Debug($"HandleTransactionAsync: ProcessTransactionAsync completado, Success={response.Success}");

            return new
            {
                Type = "TRANSACTION_RESPONSE",
                Response = response
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HandleTransactionAsync: Error procesando transacción");
            return CreateErrorResponse($"Error procesando transacción: {ex.Message}");
        }
    }

    private object HandleListTerminals()
    {
        var terminals = _terminalManager.GetAllTerminals();
        return new
        {
            Type = "TERMINAL_LIST",
            Count = terminals.Count(),
            Terminals = terminals
        };
    }

    private object HandleTerminalStatus(Dictionary<string, object> command)
    {
        try
        {
            var terminalId = command["TerminalId"].ToString();
            var terminal = _terminalManager.GetTerminal(terminalId);

            if (terminal == null)
                return CreateErrorResponse("Terminal no encontrada");

            return new
            {
                Type = "TERMINAL_STATUS",
                Terminal = terminal.TerminalInfo
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResponse($"Error obteniendo estado: {ex.Message}");
        }
    }

    private object HandleServerStatus()
    {
        var terminals = _terminalManager.GetAllTerminals().ToList();
        
        return new
        {
            Type = "SERVER_STATUS",
            Status = "RUNNING",
            Uptime = DateTime.Now,
            ConnectedClients = _clients.Count,
            RegisteredTerminals = terminals.Count,
            MaxConnections = _settings.MaxConnections,
            TerminalsByStatus = terminals.GroupBy(t => t.Status)
                .ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
    }

    /// <summary>
    /// Monitorea la salud del servidor y terminales
    /// </summary>
    private async Task MonitorHealthAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                // Verificar clientes inactivos
                var timeout = TimeSpan.FromMilliseconds(_settings.ConnectionTimeout);
                var inactiveClients = _clients.Values
                    .Where(c => DateTime.Now - c.LastActivity > timeout)
                    .ToList();

                foreach (var client in inactiveClients)
                {
                    Log.Warning($"Cliente inactivo detectado: {client.ClientId}");
                    _clients.TryRemove(client.ClientId, out _);
                    await client.DisconnectAsync();
                }

                // Verificar salud de terminales
                await _terminalManager.CheckTerminalsHealthAsync();

                Log.Debug($"Health check: {_clients.Count} clientes, {_terminalManager.GetAllTerminals().Count()} terminales");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error en monitoreo de salud");
            }
        }
    }

    private object CreateErrorResponse(string message)
    {
        return new
        {
            Type = "ERROR",
            Success = false,
            Message = message,
            Timestamp = DateTime.Now
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().Wait();
        _cancellationTokenSource?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Representa una conexión de cliente TCP
/// </summary>
internal class ClientConnection
{
    public string ClientId { get; }
    public TcpClient TcpClient { get; }
    public DateTime ConnectedAt { get; }
    public DateTime LastActivity { get; set; }

    public ClientConnection(string clientId, TcpClient tcpClient)
    {
        ClientId = clientId;
        TcpClient = tcpClient;
        ConnectedAt = DateTime.Now;
        LastActivity = DateTime.Now;
    }

    public async Task DisconnectAsync()
    {
        try
        {
            TcpClient?.Close();
            await Task.CompletedTask;
        }
        catch
        {
            // Ignorar errores al desconectar
        }
    }
}

