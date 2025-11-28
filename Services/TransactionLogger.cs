using Serilog;
using Serilog.Core;
using PAXTransactionServer.Models;
using System.Text.Json;

namespace PAXTransactionServer.Services;

/// <summary>
/// Servicio dedicado para el logging de transacciones
/// </summary>
public class TransactionLogger : IDisposable
{
    private readonly Logger _transactionLogger;

    public TransactionLogger(LogSettings settings)
    {
        var logFile = Path.Combine(settings.LogPath, "transactions-.json");

        _transactionLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                formatter: new Serilog.Formatting.Json.JsonFormatter(),
                retainedFileCountLimit: settings.RetentionDays
            )
            .CreateLogger();
    }

    /// <summary>
    /// Registra una transacción completa
    /// </summary>
    public void LogTransaction(TransactionRequest request, TransactionResponse response, double durationMs)
    {
        var logEntry = new
        {
            Timestamp = DateTime.Now,
            TransactionId = request.TransactionId,
            TerminalId = request.TerminalId,
            Type = request.Type.ToString(),
            Amount = request.Amount,
            ResultCode = response.ResultCode,
            Success = response.Success,
            DurationMs = durationMs,
            Request = MaskRequest(request),
            Response = MaskResponse(response)
        };

        _transactionLogger.Information("{@Transaction}", logEntry);
    }

    private object MaskRequest(TransactionRequest request)
    {
        // Crear copia para no modificar el original
        var masked = new
        {
            request.TransactionId,
            request.TerminalId,
            request.Type,
            request.Amount,
            request.Invoice,
            request.ReferenceNumber,
            // Enmascarar datos sensibles si existen en AdditionalData
            AdditionalData = MaskDictionary(request.AdditionalData)
        };

        return masked;
    }

    private object MaskResponse(TransactionResponse response)
    {
        var masked = new
        {
            response.TransactionId,
            response.TerminalId,
            response.Success,
            response.ResultCode,
            response.Message,
            response.ApprovalCode,
            response.ReferenceNumber,
            response.Amount,
            // Enmascarar datos sensibles en ResponseData (ej. CardNumber)
            ResponseData = MaskDictionary(response.ResponseData)
        };

        return masked;
    }

    private Dictionary<string, string>? MaskDictionary(Dictionary<string, string>? data)
    {
        if (data == null) return null;

        var masked = new Dictionary<string, string>(data);

        // Claves sensibles comunes
        var sensitiveKeys = new[] { "CardNumber", "Account", "Track1", "Track2", "CVV", "PinBlock" };

        foreach (var key in masked.Keys.ToList())
        {
            if (sensitiveKeys.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                masked[key] = MaskValue(masked[key]);
            }
        }

        return masked;
    }

    private string MaskValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length <= 4) return "****";
        
        // Mostrar últimos 4 dígitos
        return new string('*', value.Length - 4) + value.Substring(value.Length - 4);
    }

    public void Dispose()
    {
        _transactionLogger.Dispose();
    }
}
