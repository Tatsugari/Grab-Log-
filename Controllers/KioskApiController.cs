using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;

namespace GrabLog.Controllers;

[Route("api/kiosk")]
[ApiController]
public class KioskApiController : ControllerBase
{
    private readonly ToolService _tools;

    public KioskApiController(ToolService tools) => _tools = tools;

    [HttpPost("transaction")]
    public async Task<IActionResult> ProcessTransaction([FromBody] KioskTransactionRequest request)
    {
        if (string.IsNullOrEmpty(request.ToolId) || string.IsNullOrEmpty(request.ActionType) || string.IsNullOrEmpty(request.CardUID))
        {
            return BadRequest(new { success = false, message = "Missing required fields." });
        }

        string userName = string.IsNullOrEmpty(request.UserName) ? "Member1" : request.UserName;
        string userEmail = "Member1@gmail.com"; // System fallback or kiosk default operator email

        if (request.ActionType.Equals("BORROW", StringComparison.OrdinalIgnoreCase))
        {
            await _tools.BorrowAsync(request.ToolId, userName, userEmail);
            return Ok(new { success = true, message = $"Tool {request.ToolId} borrowed successfully." });
        }
        else if (request.ActionType.Equals("RETURN", StringComparison.OrdinalIgnoreCase))
        {
            await _tools.ReturnAsync(request.ToolId, userName, userEmail);
            return Ok(new { success = true, message = $"Tool {request.ToolId} returned successfully." });
        }

        return BadRequest(new { success = false, message = "Invalid action type." });
    }
}
