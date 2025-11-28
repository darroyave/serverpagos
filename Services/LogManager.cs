using Serilog;
using Serilog.Events;
using Serilog.Enrichers;
using PAXTransactionServer.Models;

namespace PAXTransactionServer.Services;

/// <summary>
/// Gestor de logs del sistema
/// </summary>
public static class LogManager
{
    /// <summary>
    /// Configura el sistema de logging
    /// </summary>
    public static void ConfigureLogging(LogSettings settings)
    {
        // Crear directorio de logs si no existe
        if (!Directory.Exists(settings.LogPath))
        {
            Directory.CreateDirectory(settings.LogPath);
        }

        var logLevel = ParseLogLevel(settings.LogLevel);
        var logFile = Path.Combine(settings.LogPath, $"{settings.LogFileName}_.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: settings.RetentionDays
            )
            .CreateLogger();

        Log.Information("═══════════════════════════════════════════════════════");
        Log.Information("  PAX Transaction Server - Sistema de Logging Iniciado");
        Log.Information("═══════════════════════════════════════════════════════");
        Log.Information($"📁 Ruta de logs: {settings.LogPath}");
        Log.Information($"📄 Nombre de archivo: {settings.LogFileName}");
        Log.Information($"📊 Nivel de log: {settings.LogLevel}");
        Log.Information($"🗓️  Retención: {settings.RetentionDays} días");
    }

    private static LogEventLevel ParseLogLevel(string level)
    {
        return level.ToUpper() switch
        {
            "VERBOSE" => LogEventLevel.Verbose,
            "DEBUG" => LogEventLevel.Debug,
            "INFORMATION" or "INFO" => LogEventLevel.Information,
            "WARNING" or "WARN" => LogEventLevel.Warning,
            "ERROR" => LogEventLevel.Error,
            "FATAL" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    /// <summary>
    /// Cierra el logger
    /// </summary>
    public static void CloseAndFlush()
    {
        Log.Information("═══════════════════════════════════════════════════════");
        Log.Information("  Cerrando sistema de logging");
        Log.Information("═══════════════════════════════════════════════════════");
        Log.CloseAndFlush();
    }
}

