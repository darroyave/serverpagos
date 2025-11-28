namespace PAXTransactionServer.Models;

/// <summary>
/// Respuesta de transacción
/// </summary>
public class TransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string TerminalId { get; set; } = string.Empty;
    public bool Success { get; set; }
    /// <summary>
    /// Código de resultado (ResponseCode).
    /// Códigos de Éxito:
    /// <list type="table">
    /// <item><term>000000</term><description>Ok - Aprobado online</description></item>
    /// <item><term>000001</term><description>ApprovedOffline - Aprobado offline/localmente</description></item>
    /// <item><term>000002</term><description>PartiallyApproved - Parcialmente aprobado</description></item>
    /// <item><term>000003</term><description>ApprovedSurchargeNotApplied - Aprobado sin cargo adicional</description></item>
    /// <item><term>000029</term><description>PartialBatchClosed - Batch parcialmente cerrado</description></item>
    /// </list>
    /// Códigos de Rechazo/Error:
    /// <list type="table">
    /// <item><term>000100</term><description>Decline - Rechazado por el host</description></item>
    /// <item><term>000200</term><description>CardReadOk - Solo lectura de tarjeta (sin pago)</description></item>
    /// <item><term>000300</term><description>OutOfBalance - Batch cerrado pero desbalanceado</description></item>
    /// <item><term>000301</term><description>SafFailedPleaseSettle - SAF fallido, necesita liquidar</description></item>
    /// </list>
    /// Códigos de Error Terminal (100xxx):
    /// <list type="table">
    /// <item><term>100005</term><description>EDC no soportado</description></item>
    /// <item><term>100006</term><description>Batch fallido</description></item>
    /// <item><term>100007</term><description>Mensaje de respuesta inválido</description></item>
    /// <item><term>100008</term><description>Error al enviar mensaje al host</description></item>
    /// <item><term>100009</term><description>Error al recibir respuesta del host</description></item>
    /// <item><term>100011</term><description>Transacción duplicada</description></item>
    /// <item><term>100015</term><description>Error CVV</description></item>
    /// <item><term>100016</term><description>Error AVS (dirección/código postal)</description></item>
    /// <item><term>100017</term><description>HALO excedido</description></item>
    /// <item><term>100018</term><description>Solo swipe permitido</description></item>
    /// <item><term>100019</term><description>Track inválido</description></item>
    /// <item><term>100022</term><description>Error PINPAD</description></item>
    /// <item><term>100024</term><description>No hay aplicación de host</description></item>
    /// <item><term>100025</term><description>Por favor liquidar</description></item>
    /// <item><term>100027</term><description>Comando no soportado</description></item>
    /// <item><term>100028</term><description>Impuesto excede el monto</description></item>
    /// <item><term>100030</term><description>Impresora no soportada</description></item>
    /// <item><term>100031</term><description>Impresora deshabilitada</description></item>
    /// <item><term>100032</term><description>Sin papel</description></item>
    /// <item><term>100040</term><description>Fondos insuficientes - usuario rechazó</description></item>
    /// <item><term>100511</term><description>Servicio ocupado</description></item>
    /// <item><term>100512</term><description>Tarjeta no aceptada</description></item>
    /// <item><term>199999</term><description>Error interno de terminal</description></item>
    /// </list>
    /// </summary>
    public string ResultCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ApprovalCode { get; set; }
    public string? ReferenceNumber { get; set; }
    public decimal Amount { get; set; }
    public DateTime ResponseTime { get; set; } = DateTime.Now;
    public Dictionary<string, string>? ResponseData { get; set; }
}

