using System.Collections.Concurrent;
using System.Text.Json;
using POSLinkCore.CommunicationSetting;
using POSLinkAdmin;
using POSLinkAdmin.Util;
using POSLinkSemiIntegration;
using POSLinkSemiIntegration.Transaction;
using POSLinkSemiIntegration.Util;
using PAXTransactionServer.Models;
using Serilog;

namespace PAXTransactionServer.Services;

/// <summary>
/// Gestor de terminales PAX - Maneja pool de hasta 35 terminales con persistencia
/// </summary>
public class TerminalManager : IDisposable
{
    private readonly ConcurrentDictionary<string, TerminalConnection> _terminals;
    private readonly TerminalSettings _settings;
    private readonly SemaphoreSlim _semaphore;
    private readonly string _terminalsFilePath;
    private readonly TransactionLogger _transactionLogger;
    private bool _disposed;

    public TerminalManager(TerminalSettings settings, LogSettings logSettings, string terminalsFilePath = "terminales.json")
    {
        _settings = settings;
        _terminals = new ConcurrentDictionary<string, TerminalConnection>(StringComparer.OrdinalIgnoreCase);
        _semaphore = new SemaphoreSlim(35); // Máximo 35 terminales
        _terminalsFilePath = terminalsFilePath;
        _transactionLogger = new TransactionLogger(logSettings);
        
        // 🔥 CARGAR TERMINALES GUARDADAS AL INICIAR
        LoadTerminalsFromFile();
    }

    /// <summary>
    /// Registra una nueva terminal y la guarda en archivo
    /// </summary>
    public async Task<bool> RegisterTerminalAsync(TerminalInfo terminalInfo)
    {
        if (!await _semaphore.WaitAsync(0))
        {
            Log.Warning($"Máximo de terminales alcanzado (35). No se puede registrar {terminalInfo.TerminalId}");
            return false;
        }

        try
        {
            var connection = new TerminalConnection(terminalInfo, _settings);
            
            if (_terminals.TryAdd(terminalInfo.TerminalId, connection))
            {
                Log.Information($"Terminal registrada: {terminalInfo.TerminalId} - {terminalInfo.IpAddress}:{terminalInfo.Port}");
                
                // 🔥 GUARDAR EN ARCHIVO PARA PERSISTENCIA
                SaveTerminalsToFile();
                
                return true;
            }
            else
            {
                _semaphore.Release();
                Log.Warning($"Terminal {terminalInfo.TerminalId} ya existe");
                return false;
            }
        }
        catch (Exception ex)
        {
            _semaphore.Release();
            Log.Error(ex, $"Error registrando terminal {terminalInfo.TerminalId}");
            return false;
        }
    }

    /// <summary>
    /// Elimina una terminal y actualiza el archivo
    /// </summary>
    public async Task<bool> UnregisterTerminalAsync(string terminalId)
    {
        if (_terminals.TryRemove(terminalId, out var connection))
        {
            await connection.DisconnectAsync();
            _semaphore.Release();
            Log.Information($"Terminal eliminada: {terminalId}");
            
            // 🔥 ACTUALIZAR ARCHIVO
            SaveTerminalsToFile();
            
            return true;
        }
        return false;
    }

    /// <summary>
    /// Obtiene una terminal por ID
    /// </summary>
    public TerminalConnection? GetTerminal(string terminalId)
    {
        _terminals.TryGetValue(terminalId, out var terminal);
        return terminal;
    }

    /// <summary>
    /// Lista todas las terminales
    /// </summary>
    public IEnumerable<TerminalInfo> GetAllTerminals()
    {
        return _terminals.Values.Select(t => t.TerminalInfo);
    }

    /// <summary>
    /// Procesa una transacción en la terminal especificada
    /// </summary>
    public async Task<TransactionResponse> ProcessTransactionAsync(TransactionRequest request)
    {
        // Validaciones básicas
        if (string.IsNullOrWhiteSpace(request.TerminalId))
        {
            Log.Warning("Transacción rechazada: TerminalId vacío");
            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = false,
                ResultCode = "ERROR",
                Message = "TerminalId es requerido"
            };
        }

        if (request.Amount <= 0)
        {
            Log.Warning($"Transacción rechazada: Monto inválido ${request.Amount}");
            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = false,
                ResultCode = "ERROR",
                Message = "El monto debe ser mayor a 0"
            };
        }

        if (request.Amount > 999999.99m)
        {
            Log.Warning($"Transacción rechazada: Monto muy alto ${request.Amount}");
            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = false,
                ResultCode = "ERROR",
                Message = "El monto excede el máximo permitido ($999,999.99)"
            };
        }

        var terminal = GetTerminal(request.TerminalId);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        TransactionResponse response;

        try
        {
            if (terminal == null)
            {
                Log.Warning($"Transacción rechazada: Terminal {request.TerminalId} no encontrada");
                response = new TransactionResponse
                {
                    TransactionId = request.TransactionId,
                    TerminalId = request.TerminalId,
                    Success = false,
                    ResultCode = "ERROR",
                    Message = $"Terminal {request.TerminalId} no encontrada"
                };
            }
            else
            {
                response = await terminal.ProcessTransactionAsync(request);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error no controlado procesando transacción {request.TransactionId}");
            response = new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = false,
                ResultCode = "ERROR",
                Message = $"Error interno: {ex.Message}"
            };
        }
        finally
        {
            stopwatch.Stop();
        }

        // 🔥 LOGUEAR TRANSACCIÓN
        _transactionLogger.LogTransaction(request, response, stopwatch.Elapsed.TotalMilliseconds);

        return response;
    }

    /// <summary>
    /// Verifica el estado de todas las terminales
    /// </summary>
    public async Task CheckTerminalsHealthAsync()
    {
        var tasks = _terminals.Values.Select(t => t.CheckHealthAsync());
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Carga terminales desde archivo JSON al iniciar el servidor
    /// </summary>
    private void LoadTerminalsFromFile()
    {
        try
        {
            if (!File.Exists(_terminalsFilePath))
            {
                Log.Information($"📄 Archivo {_terminalsFilePath} no existe. Se creará al registrar terminales.");
                return;
            }

            var json = File.ReadAllText(_terminalsFilePath);
            var config = JsonSerializer.Deserialize<TerminalsConfigFile>(json);

            if (config == null || !config.Terminals.Any())
            {
                Log.Information("📄 Archivo de terminales vacío");
                return;
            }

            Log.Information($"📂 Cargando {config.Terminals.Count} terminales desde {_terminalsFilePath}...");

            int loaded = 0;
            foreach (var terminalConfig in config.Terminals.Where(t => t.Enabled))
            {
                var terminalInfo = new TerminalInfo
                {
                    TerminalId = terminalConfig.TerminalId,
                    Name = terminalConfig.Name,
                    IpAddress = terminalConfig.IpAddress,
                    Port = terminalConfig.Port,
                    Status = TerminalStatus.Disconnected
                };

                var connection = new TerminalConnection(terminalInfo, _settings);
                
                if (_terminals.TryAdd(terminalInfo.TerminalId, connection))
                {
                    if (!_semaphore.Wait(0))
                    {
                        Log.Warning($"Límite de terminales alcanzado al cargar {terminalInfo.TerminalId}");
                        _terminals.TryRemove(terminalInfo.TerminalId, out _);
                        break;
                    }
                    loaded++;
                    Log.Information($"  ✅ Terminal cargada: {terminalConfig.TerminalId} ({terminalConfig.IpAddress}:{terminalConfig.Port})");
                }
            }

            Log.Information($"✅ {loaded} terminales cargadas desde archivo");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"❌ Error cargando terminales desde {_terminalsFilePath}");
        }
    }

    /// <summary>
    /// Guarda las terminales actuales en archivo JSON
    /// </summary>
    private void SaveTerminalsToFile()
    {
        try
        {
            var config = new TerminalsConfigFile
            {
                LastUpdated = DateTime.Now,
                Terminals = _terminals.Values
                    .Select(t => new TerminalConfig
                    {
                        TerminalId = t.TerminalInfo.TerminalId,
                        Name = t.TerminalInfo.Name,
                        IpAddress = t.TerminalInfo.IpAddress,
                        Port = t.TerminalInfo.Port,
                        Enabled = true
                    })
                    .OrderBy(t => t.TerminalId)
                    .ToList()
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(_terminalsFilePath, json);

            Log.Debug($"💾 Terminales guardadas en {_terminalsFilePath} ({config.Terminals.Count} terminales)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"❌ Error guardando terminales en {_terminalsFilePath}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 🔥 GUARDAR ESTADO FINAL ANTES DE CERRAR
        Log.Information("💾 Guardando estado de terminales antes de cerrar...");
        SaveTerminalsToFile();

        foreach (var terminal in _terminals.Values)
        {
            terminal.Dispose();
        }
        
        _terminals.Clear();
        _terminals.Clear();
        _semaphore?.Dispose();
        _transactionLogger?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Representa una conexión a una terminal PAX
/// </summary>
public class TerminalConnection : IDisposable
{
    public TerminalInfo TerminalInfo { get; }
    private readonly TerminalSettings _settings;
    private readonly POSLinkSemi _posLink;
    private Terminal? _terminal;
    private TcpSetting? _tcpSetting;
    private readonly SemaphoreSlim _transactionLock;
    private bool _isConnected;
    private bool _disposed;

    public TerminalConnection(TerminalInfo terminalInfo, TerminalSettings settings)
    {
        TerminalInfo = terminalInfo;
        _settings = settings;
        _posLink = POSLinkSemi.GetPOSLinkSemi();
        _transactionLock = new SemaphoreSlim(1, 1);
        _isConnected = false;
        
        // Inicializar estado de la terminal como Disconnected
        TerminalInfo.Status = TerminalStatus.Disconnected;
    }

    /// <summary>
    /// Conecta con la terminal PAX
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        Log.Debug($"[ConnectAsync] INICIO - Terminal: {TerminalInfo.TerminalId}");
        // NOTA: No usamos semaforo aqui porque ConnectAsync es llamado desde ProcessTransactionAsync
        // que ya tiene el semaforo adquirido. Usar semaforo aqui causaria DEADLOCK.
        
        if (_isConnected)
        {
            Log.Debug($"[ConnectAsync] Terminal YA conectada, retornando true");
            return true;
        }

        try
        {
            Log.Debug($"[ConnectAsync] Creando TcpSetting para {TerminalInfo.IpAddress}:{TerminalInfo.Port} timeout={_settings.DefaultTimeout}ms");
            _tcpSetting = new TcpSetting(
                TerminalInfo.IpAddress,
                TerminalInfo.Port,
                _settings.DefaultTimeout
            );

            // Obtener terminal del SDK POSLink
            Log.Debug($"[ConnectAsync] Llamando _posLink.GetTerminal...");
            _terminal = _posLink.GetTerminal(_tcpSetting);
            Log.Debug($"[ConnectAsync] GetTerminal retorno: {(_terminal != null ? "OK (objeto creado)" : "NULL (sin crear objeto)")}");

            if (_terminal == null)
            {
                TerminalInfo.Status = TerminalStatus.Error;
                Log.Error($"[ConnectAsync] ERROR CRITICO: _posLink.GetTerminal retorno NULL");
                Log.Error($"[ConnectAsync] Verifica que el SDK POSLink este correctamente instalado");
                Log.Error($"[ConnectAsync] IP={TerminalInfo.IpAddress}, Port={TerminalInfo.Port}");
                return false;
            }

            TerminalInfo.Status = TerminalStatus.Connected;
            TerminalInfo.LastConnected = DateTime.Now;
            _isConnected = true;

            Log.Information($"Terminal {TerminalInfo.TerminalId} conectada - {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            Log.Debug($"[ConnectAsync] FIN EXITOSO");
            return true;
        }
        catch (Exception ex)
        {
            TerminalInfo.Status = TerminalStatus.Error;
            Log.Error(ex, $"[ConnectAsync] ERROR conectando terminal {TerminalInfo.TerminalId}");
            return false;
        }
    }

    /// <summary>
    /// Desconecta de la terminal
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _transactionLock.WaitAsync();
        try
        {
            if (_terminal != null)
            {
                // Aquí podrías agregar limpieza si es necesario
                _terminal = null;
            }
            
            _tcpSetting = null;
            _isConnected = false;
            TerminalInfo.Status = TerminalStatus.Disconnected;
            
            Log.Information($"Desconectado de terminal {TerminalInfo.TerminalId}");
        }
        finally
        {
            _transactionLock.Release();
        }
    }

    /// <summary>
    /// Procesa una transacción
    /// </summary>
    public async Task<TransactionResponse> ProcessTransactionAsync(TransactionRequest request)
    {
        Log.Debug($"[ProcessTransactionAsync] INICIO - TxID: {request.TransactionId}, Terminal: {TerminalInfo.TerminalId}");
        await _transactionLock.WaitAsync();
        Log.Debug($"[ProcessTransactionAsync] Semaforo adquirido");
        
        try
        {
            Log.Debug($"[ProcessTransactionAsync] Verificando conexion... _isConnected={_isConnected}");
            
            if (!_isConnected)
            {
                Log.Debug($"[ProcessTransactionAsync] Terminal NO conectada, llamando ConnectAsync...");
                if (!await ConnectAsync())
                {
                    Log.Warning($"[ProcessTransactionAsync] ConnectAsync FALLO");
                    return CreateErrorResponse(request, "No se pudo conectar con la terminal");
                }
                Log.Debug($"[ProcessTransactionAsync] ConnectAsync EXITOSO");
            }
            else
            {
                Log.Debug($"[ProcessTransactionAsync] Terminal YA conectada");
            }

            TerminalInfo.Status = TerminalStatus.Processing;
            Log.Information($"Procesando transacción {request.TransactionId} en terminal {TerminalInfo.TerminalId}");
            Log.Debug($"[ProcessTransactionAsync] Llamando ExecuteTransactionAsync...");

            var response = await ExecuteTransactionAsync(request);
            
            Log.Debug($"[ProcessTransactionAsync] ExecuteTransactionAsync completado, Success={response.Success}");
            
            TerminalInfo.LastTransaction = DateTime.Now;
            TerminalInfo.TransactionCount++;
            TerminalInfo.Status = TerminalStatus.Connected;

            Log.Debug($"[ProcessTransactionAsync] FIN - Retornando respuesta");
            return response;
        }
        catch (Exception ex)
        {
            TerminalInfo.Status = TerminalStatus.Error;
            Log.Error(ex, $"[ProcessTransactionAsync] ERROR procesando transacción {request.TransactionId}");
            return CreateErrorResponse(request, ex.Message);
        }
        finally
        {
            _transactionLock.Release();
            Log.Debug($"[ProcessTransactionAsync] Semaforo liberado");
        }
    }

    /// <summary>
    /// Ejecuta la transacción específica según el tipo
    /// </summary>
    private async Task<TransactionResponse> ExecuteTransactionAsync(TransactionRequest request)
    {
        Log.Debug($"[ExecuteTransactionAsync] INICIO - Tipo: {request.Type}");
        
        if (_tcpSetting == null || !_isConnected)
        {
            Log.Warning($"[ExecuteTransactionAsync] Terminal no conectada");
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        // Simular procesamiento asíncrono
        await Task.Delay(100);

        Log.Debug($"[ExecuteTransactionAsync] Llamando proceso especifico para {request.Type}...");
        
        // Aquí implementarías la lógica real según el tipo de transacción
        var response = request.Type switch
        {
            TransactionType.Sale => await ProcessSaleAsync(request),
            TransactionType.Void => await ProcessVoidAsync(request),
            TransactionType.Refund => await ProcessRefundAsync(request),
            TransactionType.Auth => await ProcessAuthAsync(request),
            TransactionType.Capture => await ProcessCaptureAsync(request),
            TransactionType.Balance => await ProcessBalanceAsync(request),
            TransactionType.BatchClose => await ProcessBatchCloseAsync(request),
            _ => CreateErrorResponse(request, "Tipo de transacción no soportado")
        };
        
        Log.Debug($"[ExecuteTransactionAsync] FIN - Success: {response.Success}");
        return response;
    }

    private async Task<TransactionResponse> ProcessSaleAsync(TransactionRequest request)
    {
        Log.Information($"Ejecutando venta: ${request.Amount} en terminal {TerminalInfo.TerminalId}");
        Log.Debug($"[ProcessSaleAsync] Verificando estado: _terminal={(_terminal != null ? "OK" : "NULL")}, _isConnected={_isConnected}");
        
        if (_terminal == null || !_isConnected)
        {
            Log.Warning($"[ProcessSaleAsync] FALLO verificacion: _terminal={(_terminal == null ? "NULL" : "OK")}, _isConnected={_isConnected}");
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        Log.Debug($"[ProcessSaleAsync] Verificacion OK, preparando DoCreditRequest...");
        DoCreditResponse? response = null;

        try
        {
            // Preparar request POSLink según SDK
            var creditRequest = new DoCreditRequest();
            
            // Configurar información de monto
            creditRequest.AmountInformation = new AmountRequest
            {
                TransactionAmount = ((int)(request.Amount * 100)).ToString()
            };
            
            // Configurar información de traza
            creditRequest.TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.ReferenceNumber ?? Guid.NewGuid().ToString(),
                InvoiceNumber = request.Invoice ?? DateTime.Now.ToString("yyyyMMddHHmmss")
            };
            
            // Configurar tipo de transacción como venta
            creditRequest.TransactionType = POSLinkAdmin.Const.TransactionType.Sale;

            Log.Information($"Enviando venta al PAX: ${request.Amount} - Terminal {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            Log.Debug($"[ProcessSaleAsync] ANTES de llamar SDK DoCredit - Esperando terminal...");
            Log.Debug($"[ProcessSaleAsync] IMPORTANTE: Terminal PAX debe mostrar INSERT CARD o PRESENT CARD");
            
            // Ejecutar transacción REAL en terminal físico usando SDK
            // CRÍTICO: Capturar TODAS las excepciones del SDK POSLink
            try
            {
                Log.Debug($"[ProcessSaleAsync] Llamando _terminal.Transaction.DoCredit...");
                var result = await Task.Run(() => _terminal.Transaction.DoCredit(creditRequest, out response));
                Log.Debug($"[ProcessSaleAsync] SDK DoCredit COMPLETO - Result: {result}");
                Log.Debug($"SDK DoCredit retornó: {result}");
            }
            catch (TimeoutException tex)
            {
                Log.Error(tex, "Timeout comunicando con terminal PAX");
                return CreateErrorResponse(request, "Timeout: El terminal no respondió a tiempo");
            }
            catch (System.Net.Sockets.SocketException sockEx)
            {
                Log.Error(sockEx, $"Error de red con terminal PAX: {sockEx.Message}");
                TerminalInfo.Status = TerminalStatus.Error;
                _isConnected = false;
                return CreateErrorResponse(request, $"Error de red: {sockEx.Message}");
            }
            catch (InvalidOperationException ioEx)
            {
                Log.Error(ioEx, $"Operación inválida en SDK PAX: {ioEx.Message}");
                return CreateErrorResponse(request, $"Error de configuración: {ioEx.Message}");
            }
            catch (Exception sdkEx)
            {
                Log.Error(sdkEx, $"Error crítico del SDK POSLink: {sdkEx.GetType().Name} - {sdkEx.Message}");
                Log.Error($"StackTrace: {sdkEx.StackTrace}");
                return CreateErrorResponse(request, $"Error del SDK: {sdkEx.Message}");
            }
            
            // Procesar respuesta según POSLink SDK
            if (response == null)
            {
                Log.Warning("[ProcessSaleAsync] SDK retornó respuesta nula");
                Log.Warning("[ProcessSaleAsync] POSIBLE CAUSA: Terminal ocupado o perdió conexión");
                
                // Marcar como desconectado para forzar reconexión
                _isConnected = false;
                TerminalInfo.Status = TerminalStatus.Disconnected;
                
                return CreateErrorResponse(request, "Terminal no respondió (respuesta nula). Reintente en unos segundos.");
            }

            Log.Debug($"[ProcessSaleAsync] Respuesta recibida, analizando...");
            Log.Debug($"[ProcessSaleAsync] HostInformation: {(response.HostInformation != null ? "OK" : "NULL")}");
            Log.Debug($"[ProcessSaleAsync] TraceInformation: {(response.TraceInformation != null ? "OK" : "NULL")}");
            Log.Debug($"[ProcessSaleAsync] AccountInformation: {(response.AccountInformation != null ? "OK" : "NULL")}");

            bool isSuccess = response.HostInformation != null &&
                           (response.HostInformation.HostResponseCode == "000000" || 
                            response.HostInformation.HostResponseCode == "00");
            
            string resultCode = "ERROR";
            string resultMessage = "Sin respuesta del procesador";
            
            if (response.HostInformation != null)
            {
                resultCode = response.HostInformation.HostResponseCode ?? "ERROR";
                resultMessage = response.HostInformation.HostResponseMessage ?? "Sin mensaje";
                Log.Debug($"[ProcessSaleAsync] HostResponseCode: {resultCode}");
            }
            else
            {
                Log.Warning("[ProcessSaleAsync] HostInformation es NULL - Terminal no procesó la transacción");
                Log.Warning("[ProcessSaleAsync] CAUSA: Terminal ocupado, en timeout, o necesita reconexión");
                
                // Marcar como desconectado para forzar reconexión en próxima transacción
                _isConnected = false;
                TerminalInfo.Status = TerminalStatus.Error;
                
                return CreateErrorResponse(request, "Terminal ocupado o no disponible. Reintente en unos segundos.");
            }
            
            Log.Information($"Respuesta PAX: {resultCode} - {resultMessage}");

            if (!isSuccess)
            {
                Log.Warning($"Transacción rechazada: {resultMessage}");
            }

            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = isSuccess,
                ResultCode = resultCode,
                Message = isSuccess ? "Transacción aprobada" : $"Rechazada: {resultMessage}",
                ApprovalCode = response.TraceInformation?.ReferenceNumber,
                ReferenceNumber = response.TraceInformation?.ReferenceNumber,
                Amount = request.Amount,
                ResponseData = new Dictionary<string, string>
                {
                    ["HostResponse"] = response.HostInformation?.HostResponseMessage ?? "",
                    ["CardType"] = response.AccountInformation?.CardType.ToString() ?? "",
                    ["CardNumber"] = response.AccountInformation?.Account ?? "",
                    ["EntryMode"] = response.AccountInformation?.EntryMode.ToString() ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            // Catch-all para cualquier otra excepción no prevista
            Log.Error(ex, $"Error inesperado en ProcessSaleAsync: {ex.GetType().Name} - {ex.Message}");
            Log.Error($"StackTrace: {ex.StackTrace}");
            TerminalInfo.Status = TerminalStatus.Error;
            return CreateErrorResponse(request, $"Error crítico: {ex.Message}");
        }
    }

    private async Task<TransactionResponse> ProcessVoidAsync(TransactionRequest request)
    {
        Log.Information($"Ejecutando anulación en terminal {TerminalInfo.TerminalId}");
        
        if (_terminal == null || !_isConnected)
        {
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        DoCreditResponse? response = null;

        try
        {
            var creditRequest = new DoCreditRequest();
            
            // Para Void, el monto puede ser 0 o el original, pero PAX suele requerirlo
            creditRequest.AmountInformation = new AmountRequest
            {
                TransactionAmount = ((int)(request.Amount * 100)).ToString()
            };
            
            creditRequest.TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.ReferenceNumber ?? Guid.NewGuid().ToString(),
                InvoiceNumber = request.Invoice ?? DateTime.Now.ToString("yyyyMMddHHmmss")
            };
            
            creditRequest.TransactionType = POSLinkAdmin.Const.TransactionType.Void;

            Log.Information($"Enviando anulación al PAX - Terminal {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            
            try
            {
                var result = await Task.Run(() => _terminal.Transaction.DoCredit(creditRequest, out response));
                Log.Debug($"SDK DoCredit (Void) retornó: {result}");
            }
            catch (Exception sdkEx)
            {
                Log.Error(sdkEx, $"Error en SDK POSLink (Void): {sdkEx.Message}");
                return CreateErrorResponse(request, $"Error del SDK: {sdkEx.Message}");
            }
            
            if (response == null || response.HostInformation == null)
            {
                return CreateErrorResponse(request, "Sin respuesta del terminal");
            }

            bool isSuccess = response.HostInformation.HostResponseCode == "000000" || 
                           response.HostInformation.HostResponseCode == "00";

            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = isSuccess,
                ResultCode = response.HostInformation.HostResponseCode ?? "ERROR",
                Message = response.HostInformation.HostResponseMessage ?? (isSuccess ? "Anulación aprobada" : "Anulación rechazada"),
                ReferenceNumber = response.TraceInformation?.ReferenceNumber,
                ResponseData = new Dictionary<string, string>
                {
                    ["HostResponse"] = response.HostInformation.HostResponseMessage ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error procesando anulación {request.TransactionId}");
            return CreateErrorResponse(request, ex.Message);
        }
    }

    private async Task<TransactionResponse> ProcessRefundAsync(TransactionRequest request)
    {
        Log.Information($"Ejecutando devolución: ${request.Amount} en terminal {TerminalInfo.TerminalId}");
        
        if (_terminal == null || !_isConnected)
        {
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        DoCreditResponse? response = null;

        try
        {
            var creditRequest = new DoCreditRequest();
            
            creditRequest.AmountInformation = new AmountRequest
            {
                TransactionAmount = ((int)(request.Amount * 100)).ToString()
            };
            
            creditRequest.TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.ReferenceNumber ?? Guid.NewGuid().ToString(),
                InvoiceNumber = request.Invoice ?? DateTime.Now.ToString("yyyyMMddHHmmss")
            };
            
            creditRequest.TransactionType = POSLinkAdmin.Const.TransactionType.Return;

            Log.Information($"Enviando devolución al PAX - Terminal {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            
            try
            {
                var result = await Task.Run(() => _terminal.Transaction.DoCredit(creditRequest, out response));
                Log.Debug($"SDK DoCredit (Return) retornó: {result}");
            }
            catch (Exception sdkEx)
            {
                Log.Error(sdkEx, $"Error en SDK POSLink (Return): {sdkEx.Message}");
                return CreateErrorResponse(request, $"Error del SDK: {sdkEx.Message}");
            }
            
            if (response == null || response.HostInformation == null)
            {
                return CreateErrorResponse(request, "Sin respuesta del terminal");
            }

            bool isSuccess = response.HostInformation.HostResponseCode == "000000" || 
                           response.HostInformation.HostResponseCode == "00";

            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = isSuccess,
                ResultCode = response.HostInformation.HostResponseCode ?? "ERROR",
                Message = response.HostInformation.HostResponseMessage ?? (isSuccess ? "Devolución aprobada" : "Devolución rechazada"),
                ApprovalCode = response.TraceInformation?.ReferenceNumber,
                Amount = request.Amount,
                ResponseData = new Dictionary<string, string>
                {
                    ["HostResponse"] = response.HostInformation.HostResponseMessage ?? "",
                    ["CardType"] = response.AccountInformation?.CardType.ToString() ?? "",
                    ["CardNumber"] = response.AccountInformation?.Account ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error procesando devolución {request.TransactionId}");
            return CreateErrorResponse(request, ex.Message);
        }
    }

    private async Task<TransactionResponse> ProcessAuthAsync(TransactionRequest request)
    {
        Log.Information($"Ejecutando autorización: ${request.Amount} en terminal {TerminalInfo.TerminalId}");
        
        if (_terminal == null || !_isConnected)
        {
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        DoCreditResponse? response = null;

        try
        {
            var creditRequest = new DoCreditRequest();
            
            creditRequest.AmountInformation = new AmountRequest
            {
                TransactionAmount = ((int)(request.Amount * 100)).ToString()
            };
            
            creditRequest.TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.ReferenceNumber ?? Guid.NewGuid().ToString(),
                InvoiceNumber = request.Invoice ?? DateTime.Now.ToString("yyyyMMddHHmmss")
            };
            
            creditRequest.TransactionType = POSLinkAdmin.Const.TransactionType.Authorization;

            Log.Information($"Enviando autorización al PAX - Terminal {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            
            try
            {
                var result = await Task.Run(() => _terminal.Transaction.DoCredit(creditRequest, out response));
                Log.Debug($"SDK DoCredit (Auth) retornó: {result}");
            }
            catch (Exception sdkEx)
            {
                Log.Error(sdkEx, $"Error en SDK POSLink (Auth): {sdkEx.Message}");
                return CreateErrorResponse(request, $"Error del SDK: {sdkEx.Message}");
            }
            
            if (response == null || response.HostInformation == null)
            {
                return CreateErrorResponse(request, "Sin respuesta del terminal");
            }

            bool isSuccess = response.HostInformation.HostResponseCode == "000000" || 
                           response.HostInformation.HostResponseCode == "00";

            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = isSuccess,
                ResultCode = response.HostInformation.HostResponseCode ?? "ERROR",
                Message = response.HostInformation.HostResponseMessage ?? (isSuccess ? "Autorización aprobada" : "Autorización rechazada"),
                ApprovalCode = response.TraceInformation?.ReferenceNumber,
                Amount = request.Amount,
                ResponseData = new Dictionary<string, string>
                {
                    ["HostResponse"] = response.HostInformation.HostResponseMessage ?? "",
                    ["CardType"] = response.AccountInformation?.CardType.ToString() ?? "",
                    ["CardNumber"] = response.AccountInformation?.Account ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error procesando autorización {request.TransactionId}");
            return CreateErrorResponse(request, ex.Message);
        }
    }

    private async Task<TransactionResponse> ProcessCaptureAsync(TransactionRequest request)
    {
        Log.Information($"Ejecutando captura en terminal {TerminalInfo.TerminalId}");
        
        if (_terminal == null || !_isConnected)
        {
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        DoCreditResponse? response = null;

        try
        {
            var creditRequest = new DoCreditRequest();
            
            creditRequest.AmountInformation = new AmountRequest
            {
                TransactionAmount = ((int)(request.Amount * 100)).ToString()
            };
            
            creditRequest.TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.ReferenceNumber ?? Guid.NewGuid().ToString(),
                InvoiceNumber = request.Invoice ?? DateTime.Now.ToString("yyyyMMddHHmmss")
            };
            
            creditRequest.TransactionType = POSLinkAdmin.Const.TransactionType.PostAuthorization;

            Log.Information($"Enviando captura al PAX - Terminal {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            
            try
            {
                var result = await Task.Run(() => _terminal.Transaction.DoCredit(creditRequest, out response));
                Log.Debug($"SDK DoCredit (Capture) retornó: {result}");
            }
            catch (Exception sdkEx)
            {
                Log.Error(sdkEx, $"Error en SDK POSLink (Capture): {sdkEx.Message}");
                return CreateErrorResponse(request, $"Error del SDK: {sdkEx.Message}");
            }
            
            if (response == null || response.HostInformation == null)
            {
                return CreateErrorResponse(request, "Sin respuesta del terminal");
            }

            bool isSuccess = response.HostInformation.HostResponseCode == "000000" || 
                           response.HostInformation.HostResponseCode == "00";

            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = isSuccess,
                ResultCode = response.HostInformation.HostResponseCode ?? "ERROR",
                Message = response.HostInformation.HostResponseMessage ?? (isSuccess ? "Captura aprobada" : "Captura rechazada"),
                ApprovalCode = response.TraceInformation?.ReferenceNumber,
                Amount = request.Amount,
                ResponseData = new Dictionary<string, string>
                {
                    ["HostResponse"] = response.HostInformation.HostResponseMessage ?? "",
                    ["CardType"] = response.AccountInformation?.CardType.ToString() ?? "",
                    ["CardNumber"] = response.AccountInformation?.Account ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error procesando captura {request.TransactionId}");
            return CreateErrorResponse(request, ex.Message);
        }
    }

    private async Task<TransactionResponse> ProcessBalanceAsync(TransactionRequest request)
    {
        Log.Information($"Consultando balance en terminal {TerminalInfo.TerminalId}");
        
        if (_terminal == null || !_isConnected)
        {
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        DoCreditResponse? response = null;

        try
        {
            var creditRequest = new DoCreditRequest();
            
            // Balance Inquiry suele requerir monto 0 o no importa
            creditRequest.AmountInformation = new AmountRequest
            {
                TransactionAmount = "0"
            };
            
            creditRequest.TraceInformation = new TraceRequest
            {
                EcrReferenceNumber = request.ReferenceNumber ?? Guid.NewGuid().ToString(),
                InvoiceNumber = request.Invoice ?? DateTime.Now.ToString("yyyyMMddHHmmss")
            };
            
            creditRequest.TransactionType = POSLinkAdmin.Const.TransactionType.Inquiry;

            Log.Information($"Enviando consulta de balance al PAX - Terminal {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            
            try
            {
                var result = await Task.Run(() => _terminal.Transaction.DoCredit(creditRequest, out response));
                Log.Debug($"SDK DoCredit (Balance) retornó: {result}");
            }
            catch (Exception sdkEx)
            {
                Log.Error(sdkEx, $"Error en SDK POSLink (Balance): {sdkEx.Message}");
                return CreateErrorResponse(request, $"Error del SDK: {sdkEx.Message}");
            }
            
            if (response == null || response.HostInformation == null)
            {
                return CreateErrorResponse(request, "Sin respuesta del terminal");
            }

            bool isSuccess = response.HostInformation.HostResponseCode == "000000" || 
                           response.HostInformation.HostResponseCode == "00";

            // Intentar obtener el balance de la respuesta
            string balance = "0.00";
            if (response.AmountInformation != null)
            {
                // Asumiendo que el balance viene en algun campo de AmountInformation o AccountInformation
                // POSLink suele devolver RemainingBalance
                // Por ahora no lo parseamos especificamente sin ver la definicion de AmountResponse
            }

            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = isSuccess,
                ResultCode = response.HostInformation.HostResponseCode ?? "ERROR",
                Message = response.HostInformation.HostResponseMessage ?? (isSuccess ? "Consulta exitosa" : "Consulta fallida"),
                ResponseData = new Dictionary<string, string>
                {
                    ["HostResponse"] = response.HostInformation.HostResponseMessage ?? "",
                    ["CardType"] = response.AccountInformation?.CardType.ToString() ?? "",
                    ["CardNumber"] = response.AccountInformation?.Account ?? ""
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error procesando balance {request.TransactionId}");
            return CreateErrorResponse(request, ex.Message);
        }
    }

    private async Task<TransactionResponse> ProcessBatchCloseAsync(TransactionRequest request)
    {
        Log.Information($"Ejecutando cierre de lote en terminal {TerminalInfo.TerminalId}");
        
        if (_terminal == null || !_isConnected)
        {
            return CreateErrorResponse(request, "Terminal no conectada");
        }

        BatchResponse? response = null;

        try
        {
            var batchRequest = new BatchRequest();
            
            // Configurar tipo de cierre
            batchRequest.TransType = POSLinkAdmin.Const.BatchType.BatchClose;
            
            // Algunos sistemas requieren EDC Type
            batchRequest.EdcType = POSLinkAdmin.Const.EdcType.All;

            Log.Information($"Enviando BatchClose al PAX - Terminal {TerminalInfo.IpAddress}:{TerminalInfo.Port}");
            
            try
            {
                var result = await Task.Run(() => _terminal.Batch.BatchClose(batchRequest, out response));
                Log.Debug($"SDK BatchClose retornó: {result}");
            }
            catch (Exception sdkEx)
            {
                Log.Error(sdkEx, $"Error en SDK POSLink (BatchClose): {sdkEx.Message}");
                return CreateErrorResponse(request, $"Error del SDK: {sdkEx.Message}");
            }
            
            if (response == null || response.HostInformation == null)
            {
                return CreateErrorResponse(request, "Sin respuesta del terminal");
            }

            bool isSuccess = response.HostInformation.HostResponseCode == "000000" || 
                           response.HostInformation.HostResponseCode == "00";

            return new TransactionResponse
            {
                TransactionId = request.TransactionId,
                TerminalId = request.TerminalId,
                Success = isSuccess,
                ResultCode = response.HostInformation.HostResponseCode ?? "ERROR",
                Message = response.HostInformation.HostResponseMessage ?? (isSuccess ? "Cierre de lote exitoso" : "Cierre de lote fallido"),
                ResponseData = new Dictionary<string, string>
                {
                    ["HostResponse"] = response.HostInformation.HostResponseMessage ?? "",
                    ["TotalCount"] = response.TotalCount.ToString(),
                    ["TotalAmount"] = response.TotalAmount.ToString()
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error procesando BatchClose {request.TransactionId}");
            return CreateErrorResponse(request, ex.Message);
        }
    }

    /// <summary>
    /// Verifica el estado de la terminal
    /// </summary>
    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            if (!_isConnected)
            {
                if (_settings.EnableAutoReconnect)
                {
                    return await ConnectAsync();
                }
                return false;
            }
            
            return true;
        }
        catch
        {
            TerminalInfo.Status = TerminalStatus.Error;
            return false;
        }
    }

    private TransactionResponse CreateErrorResponse(TransactionRequest request, string message)
    {
        return new TransactionResponse
        {
            TransactionId = request.TransactionId,
            TerminalId = request.TerminalId,
            Success = false,
            ResultCode = "ERROR",
            Message = message
        };
    }

    private string GenerateApprovalCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }

    private string GenerateReferenceNumber()
    {
        return DateTime.Now.ToString("yyyyMMddHHmmss") + Random.Shared.Next(1000, 9999);
    }

    public void Dispose()
    {
        if (_disposed) return;

        DisconnectAsync().Wait();
        _transactionLock?.Dispose();
        _disposed = true;
    }
}

