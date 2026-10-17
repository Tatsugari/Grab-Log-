namespace GrabLog.Models;

public class KioskTransactionRequest
{
    public string CardUID { get; set; } = "";
    public string UserName { get; set; } = "";
    public string ActionType { get; set; } = ""; // BORROW | RETURN
    public string? ToolId { get; set; }
}
