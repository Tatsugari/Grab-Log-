using GrabLog.Models;
using GrabLog.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GrabLog.Pages;

public class LogsModel : PageModel
{
    private readonly LogService _logs;

    public LogsModel(LogService logs) => _logs = logs;

    public List<ActivityLog> Entries { get; set; } = new();
    public bool IsAdmin { get; set; } = true;
    public string UserName { get; set; } = "";
    public int OpId { get; set; }
    public string Initials { get; set; } = "—";
    public string? ToolFilter { get; set; }
    public string? Error { get; set; }

    public async Task<IActionResult> OnGetAsync(string? tool)
    {
        var email = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(email))
            return RedirectToPage("/Index");

        IsAdmin = HttpContext.Session.GetString("UserRole") == "admin";
        if (!IsAdmin)
            return RedirectToPage("/Inventory");

        UserName = HttpContext.Session.GetString("UserName") ?? "User";
        OpId = Math.Abs(email.GetHashCode()) % 100000;
        Initials = string.Concat(UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpper(p[0])));
        ToolFilter = tool;

        try
        {
            Entries = string.IsNullOrEmpty(tool)
                ? await _logs.GetRecentAsync(150)
                : await _logs.GetForToolAsync(tool);
        }
        catch (Exception ex)
        {
            Error = ex.ToString();
            Entries = new();
        }
        return Page();
    }
}
