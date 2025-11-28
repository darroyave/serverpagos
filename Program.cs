using System.Text.Json;
using PAXTransactionServer.Models;
using PAXTransactionServer.Services;
using Serilog;

namespace PAXTransactionServer;

class Program
{
    private static ServerConfig? _config;
    private static TCPServer? _server;
    private static TerminalManager? _terminalManager;
    private static bool _running = true;

    static async Task Main(string[] args)
    {
        try
        {
            // Cargar configuración
            _config = LoadConfiguration();

            // Configurar logging
            LogManager.ConfigureLogging(_config.LogSettings);

            // Mostrar banner
            ShowBanner();

            // Crear gestor de terminales
            _terminalManager = new TerminalManager(_config.TerminalSettings, _config.LogSettings);

            // Crear y configurar servidor TCP
            _server = new TCPServer(_config.ServerSettings, _terminalManager);

            // Configurar manejador de señales
            Console.CancelKeyPress += OnCancelKeyPress;

            // Iniciar servidor
            Log.Information("🚀 Iniciando servidor de transacciones PAX...");
            
            var serverTask = _server.StartAsync();
            var commandTask = ProcessCommandsAsync();

            await Task.WhenAny(serverTask, commandTask);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error fatal en el servidor");
        }
        finally
        {
            await Shutdown();
        }
    }

    /// <summary>
    /// Carga la configuración desde appsettings.json
    /// </summary>
    private static ServerConfig LoadConfiguration()
    {
        var configFile = "appsettings.json";
        
        if (!File.Exists(configFile))
        {
            Console.WriteLine($"⚠️  Archivo de configuración no encontrado: {configFile}");
            Console.WriteLine("📝 Creando configuración por defecto...");
            
            var defaultConfig = new ServerConfig();
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, json);
            
            return defaultConfig;
        }

        var configJson = File.ReadAllText(configFile);
        return JsonSerializer.Deserialize<ServerConfig>(configJson) ?? new ServerConfig();
    }

    /// <summary>
    /// Muestra el banner de inicio
    /// </summary>
    private static void ShowBanner()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(@"
╔══════════════════════════════════════════════════════════════╗
║                                                              ║
║        ██████╗  █████╗ ██╗  ██╗    ███████╗███████╗██████╗  ║
║        ██╔══██╗██╔══██╗╚██╗██╔╝    ██╔════╝██╔════╝██╔══██╗ ║
║        ██████╔╝███████║ ╚███╔╝     ███████╗█████╗  ██████╔╝ ║
║        ██╔═══╝ ██╔══██║ ██╔██╗     ╚════██║██╔══╝  ██╔══██╗ ║
║        ██║     ██║  ██║██╔╝ ██╗    ███████║███████╗██║  ██║ ║
║        ╚═╝     ╚═╝  ╚═╝╚═╝  ╚═╝    ╚══════╝╚══════╝╚═╝  ╚═╝ ║
║                                                              ║
║           Servidor de Transacciones TCP - Versión 1.0       ║
║              Soporta hasta 35 terminales PAX                 ║
║                                                              ║
╚══════════════════════════════════════════════════════════════╝
");
        Console.ResetColor();
        Console.WriteLine();
    }

    /// <summary>
    /// Procesa comandos de consola
    /// </summary>
    private static async Task ProcessCommandsAsync()
    {
        ShowHelp();

        while (_running)
        {
            Console.Write("\nPAX> ");
            var input = Console.ReadLine()?.Trim().ToLower();

            if (string.IsNullOrEmpty(input))
                continue;

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0];

            try
            {
                switch (command)
                {
                    case "help":
                    case "?":
                        ShowHelp();
                        break;

                    case "status":
                        ShowStatus();
                        break;

                    case "terminals":
                    case "list":
                        ShowTerminals();
                        break;

                    case "add":
                        await AddTerminalInteractiveAsync();
                        break;

                    case "remove":
                        if (parts.Length > 1)
                            await RemoveTerminalAsync(parts[1]);
                        else
                            Console.WriteLine("❌ Uso: remove <terminal_id>");
                        break;

                    case "test":
                        if (parts.Length > 1)
                            await TestTerminalAsync(parts[1]);
                        else
                            Console.WriteLine("❌ Uso: test <terminal_id>");
                        break;

                    case "clear":
                    case "cls":
                        Console.Clear();
                        ShowBanner();
                        break;

                    case "exit":
                    case "quit":
                        _running = false;
                        break;

                    default:
                        Console.WriteLine($"❌ Comando no reconocido: {command}");
                        Console.WriteLine("💡 Escribe 'help' para ver comandos disponibles");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }
    }

    private static void ShowHelp()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n┌─────────────────────── COMANDOS DISPONIBLES ───────────────────────┐");
        Console.ResetColor();
        
        Console.WriteLine("│ status              - Muestra el estado del servidor               │");
        Console.WriteLine("│ terminals / list    - Lista todas las terminales registradas       │");
        Console.WriteLine("│ add                 - Agrega una nueva terminal                    │");
        Console.WriteLine("│ remove <id>         - Elimina una terminal                         │");
        Console.WriteLine("│ test <id>           - Prueba conexión con una terminal             │");
        Console.WriteLine("│ clear / cls         - Limpia la pantalla                           │");
        Console.WriteLine("│ help / ?            - Muestra esta ayuda                           │");
        Console.WriteLine("│ exit / quit         - Detiene el servidor y sale                   │");
        
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("└─────────────────────────────────────────────────────────────────────┘");
        Console.ResetColor();
    }

    private static void ShowStatus()
    {
        if (_server == null || _terminalManager == null || _config == null)
            return;

        var terminals = _terminalManager.GetAllTerminals().ToList();
        var terminalsByStatus = terminals.GroupBy(t => t.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine("\n╔═══════════════════ ESTADO DEL SERVIDOR ═══════════════════╗");
        Console.ForegroundColor = _server.IsRunning ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  Estado: {(_server.IsRunning ? "🟢 EJECUTANDO" : "🔴 DETENIDO")}");
        Console.ResetColor();
        Console.WriteLine($"  Puerto: {_config.ServerSettings.ServerPort}");
        Console.WriteLine($"  Clientes conectados: {_server.ConnectedClients}/{_config.ServerSettings.MaxConnections}");
        Console.WriteLine($"  Terminales registradas: {terminals.Count}/35");
        Console.WriteLine("├────────────────────────────────────────────────────────────┤");
        Console.WriteLine("  Terminales por estado:");
        
        foreach (var status in Enum.GetValues<TerminalStatus>())
        {
            var count = terminalsByStatus.GetValueOrDefault(status, 0);
            if (count > 0)
            {
                var icon = status switch
                {
                    TerminalStatus.Connected => "🟢",
                    TerminalStatus.Processing => "🔵",
                    TerminalStatus.Disconnected => "⚫",
                    TerminalStatus.Error => "🔴",
                    TerminalStatus.Maintenance => "🟡",
                    _ => "⚪"
                };
                Console.WriteLine($"    {icon} {status}: {count}");
            }
        }
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
    }

    private static void ShowTerminals()
    {
        if (_terminalManager == null)
            return;

        var terminals = _terminalManager.GetAllTerminals().ToList();
        
        if (!terminals.Any())
        {
            Console.WriteLine("\n📭 No hay terminales registradas");
            return;
        }

        Console.WriteLine("\n╔═══════════════════════ TERMINALES REGISTRADAS ═══════════════════════╗");
        Console.WriteLine("┌──────────┬────────────────┬──────────────┬──────────────┬─────────────┐");
        Console.WriteLine("│ ID       │ Nombre         │ IP:Puerto    │ Estado       │ Transacc.   │");
        Console.WriteLine("├──────────┼────────────────┼──────────────┼──────────────┼─────────────┤");
        
        foreach (var terminal in terminals.OrderBy(t => t.TerminalId))
        {
            var statusIcon = terminal.Status switch
            {
                TerminalStatus.Connected => "🟢",
                TerminalStatus.Processing => "🔵",
                TerminalStatus.Disconnected => "⚫",
                TerminalStatus.Error => "🔴",
                TerminalStatus.Maintenance => "🟡",
                _ => "⚪"
            };

            Console.WriteLine($"│ {terminal.TerminalId,-8} │ {terminal.Name,-14} │ {terminal.IpAddress}:{terminal.Port,-5} │ {statusIcon} {terminal.Status,-10} │ {terminal.TransactionCount,11} │");
        }
        
        Console.WriteLine("└──────────┴────────────────┴──────────────┴──────────────┴─────────────┘");
        Console.WriteLine($"Total: {terminals.Count} terminales");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
    }

    private static async Task AddTerminalInteractiveAsync()
    {
        if (_terminalManager == null)
            return;

        Console.WriteLine("\n┌─── AGREGAR NUEVA TERMINAL ───┐");
        
        Console.Write("│ ID de terminal: ");
        var id = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            Console.WriteLine("│ ❌ ID inválido");
            return;
        }

        Console.Write("│ Nombre: ");
        var name = Console.ReadLine()?.Trim();

        Console.Write("│ Dirección IP: ");
        var ip = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            Console.WriteLine("│ ❌ IP inválida");
            return;
        }

        Console.Write("│ Puerto (10009): ");
        var portStr = Console.ReadLine()?.Trim();
        var port = string.IsNullOrEmpty(portStr) ? 10009 : int.Parse(portStr);

        var terminal = new TerminalInfo
        {
            TerminalId = id,
            Name = name ?? id,
            IpAddress = ip,
            Port = port,
            Status = TerminalStatus.Disconnected
        };

        var success = await _terminalManager.RegisterTerminalAsync(terminal);
        
        if (success)
            Console.WriteLine("│ ✅ Terminal agregada exitosamente");
        else
            Console.WriteLine("│ ❌ Error agregando terminal");
        
        Console.WriteLine("└──────────────────────────────┘");
    }

    private static async Task RemoveTerminalAsync(string terminalId)
    {
        if (_terminalManager == null)
            return;

        var success = await _terminalManager.UnregisterTerminalAsync(terminalId);
        
        if (success)
            Console.WriteLine($"✅ Terminal {terminalId} eliminada");
        else
            Console.WriteLine($"❌ Terminal {terminalId} no encontrada");
    }

    private static async Task TestTerminalAsync(string terminalId)
    {
        if (_terminalManager == null)
            return;

        var terminal = _terminalManager.GetTerminal(terminalId);
        
        if (terminal == null)
        {
            Console.WriteLine($"❌ Terminal {terminalId} no encontrada");
            return;
        }

        Console.WriteLine($"🔍 Probando conexión con terminal {terminalId}...");
        
        var connected = await terminal.ConnectAsync();
        
        if (connected)
            Console.WriteLine($"✅ Conexión exitosa con {terminal.TerminalInfo.IpAddress}:{terminal.TerminalInfo.Port}");
        else
            Console.WriteLine($"❌ No se pudo conectar con la terminal");
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _running = false;
        Console.WriteLine("\n\n🛑 Señal de interrupción recibida...");
    }

    private static async Task Shutdown()
    {
        Console.WriteLine("\n╔═══════════════════════════════════════╗");
        Console.WriteLine("║   Cerrando servidor...                ║");
        Console.WriteLine("╚═══════════════════════════════════════╝");

        if (_server != null)
        {
            await _server.StopAsync();
            _server.Dispose();
        }

        _terminalManager?.Dispose();

        LogManager.CloseAndFlush();
        
        Console.WriteLine("\n✅ Servidor cerrado correctamente");
        Console.WriteLine("👋 ¡Hasta luego!\n");
    }
}

