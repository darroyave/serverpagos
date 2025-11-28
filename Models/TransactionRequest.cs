namespace PAXTransactionServer.Models;

/// <summary>
/// Solicitud de transacción
/// </summary>
public class TransactionRequest
{
    public string TransactionId { get; set; } = Guid.NewGuid().ToString();
    public string TerminalId { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string? Invoice { get; set; }
    public string? ReferenceNumber { get; set; }
    public Dictionary<string, string>? AdditionalData { get; set; }
    public DateTime RequestTime { get; set; } = DateTime.Now;
}

/// <summary>
/// Tipo de transacción.
/// Códigos de PAX POSLink:
/// <list type="table">
/// <item><term>Sale</term><description>Venta</description></item>
/// <item><term>Return</term><description>Devolución</description></item>
/// <item><term>Authorization</term><description>Autorización</description></item>
/// <item><term>PostAuthorization</term><description>Post-Autorización</description></item>
/// <item><term>ForceAuthorization</term><description>Autorización Forzada</description></item>
/// <item><term>Adjust</term><description>Ajuste</description></item>
/// <item><term>Withdrawal</term><description>Retiro</description></item>
/// <item><term>Activate</term><description>Activar</description></item>
/// <item><term>Issue</term><description>Emitir</description></item>
/// <item><term>Reload</term><description>Recarga</description></item>
/// <item><term>Cashout</term><description>Cashout</description></item>
/// <item><term>Deactivate</term><description>Desactivar</description></item>
/// <item><term>Replace</term><description>Reemplazar</description></item>
/// <item><term>Merge</term><description>Fusionar</description></item>
/// <item><term>ReportLost</term><description>Reportar Perdida</description></item>
/// <item><term>Void</term><description>Anular</description></item>
/// <item><term>VoidSale</term><description>Anular Venta</description></item>
/// <item><term>VoidReturn</term><description>Anular Devolución</description></item>
/// <item><term>VoidAuthorization</term><description>Anular Autorización</description></item>
/// <item><term>VoidPostAuthorization</term><description>Anular Post-Autorización</description></item>
/// <item><term>VoidForceAuthorization</term><description>Anular Autorización Forzada</description></item>
/// <item><term>VoidWithdrawal</term><description>Anular Retiro</description></item>
/// <item><term>Inquiry</term><description>Consulta</description></item>
/// <item><term>Verify</term><description>Verificar</description></item>
/// <item><term>Reactivate</term><description>Reactivar</description></item>
/// <item><term>ForcedIssue</term><description>Emisión Forzada</description></item>
/// <item><term>ForcedAdd</term><description>Adición Forzada</description></item>
/// <item><term>Unload</term><description>Descargar</description></item>
/// <item><term>Renew</term><description>Renovar</description></item>
/// <item><term>GetConvertDetail</term><description>Obtener Detalle Conversión</description></item>
/// <item><term>Convert</term><description>Convertir</description></item>
/// <item><term>Tokenize</term><description>Tokenizar</description></item>
/// <item><term>IncrementalAuthorization</term><description>Autorización Incremental</description></item>
/// <item><term>BalanceWithLock</term><description>Balance con Bloqueo</description></item>
/// <item><term>RedemptionWithUnlock</term><description>Redención con Desbloqueo</description></item>
/// <item><term>Rewards</term><description>Recompensas</description></item>
/// <item><term>Reenter</term><description>Re-ingresar</description></item>
/// <item><term>TransactionAdjustment</term><description>Ajuste de Transacción</description></item>
/// <item><term>Transfer</term><description>Transferencia</description></item>
/// <item><term>Finalize</term><description>Finalizar</description></item>
/// <item><term>Deposit</term><description>Depósito</description></item>
/// <item><term>AccountPayment</term><description>Pago de Cuenta</description></item>
/// <item><term>Payment</term><description>Pago</description></item>
/// <item><term>VoidPayment</term><description>Anular Pago</description></item>
/// <item><term>Reversal</term><description>Reversión</description></item>
/// </list>
/// </summary>
public enum TransactionType
{
    Sale,
    Void,
    Refund,
    Auth,
    Capture,
    Balance,
    BatchClose
}

