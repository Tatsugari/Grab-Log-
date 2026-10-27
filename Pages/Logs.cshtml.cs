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
    public string? Error { get; set; }

    // Filters
    public string Q { get; set; } = "";
    public string Category { get; set; } = "all"; // all, name, id, date, week, time, tools
    public string? ToolFilter { get; set; }

    public async Task<IActionResult> OnGetAsync(string? tool, string? q, string? category)
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
        Q = q ?? "";
        Category = string.IsNullOrWhiteSpace(category) ? "all" : category.Trim().ToLower();

        try
        {
            var all = string.IsNullOrEmpty(tool)
                ? await _logs.GetRecentAsync(500)
                : await _logs.GetForToolAsync(tool);

            if (!string.IsNullOrWhiteSpace(Q))
            {
                var s = Q.Trim().ToLower();
                all = Category switch
                {
                    "name" => all.Where(e =>
                        (e.UserName ?? "").ToLower().Contains(s)).ToList(),
                    "id" => all.Where(e =>
                        (e.ToolId ?? "").ToLower().Contains(s) ||
                        (e.Details ?? "").ToLower().Contains(s) ||
                        (e.UserEmail ?? "").ToLower().Contains(s)).ToList(),
                    "date" => all.Where(e =>
                        e.CreatedAt.HasValue &&
                        e.CreatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd").Contains(s)).ToList(),
                    "week" => FilterByWeek(all, s),
                    "time" => all.Where(e =>
                        e.CreatedAt.HasValue &&
                        e.CreatedAt.Value.ToLocalTime().ToString("h:mm tt").ToLower().Contains(s)).ToList(),
                    "tools" => all.Where(e =>
                        (e.ToolName ?? "").ToLower().Contains(s) ||
                        (e.ToolId ?? "").ToLower().Contains(s)).ToList(),
                    _ => all.Where(e =>
                        (e.UserName ?? "").ToLower().Contains(s) ||
                        (e.ToolId ?? "").ToLower().Contains(s) ||
                        (e.ToolName ?? "").ToLower().Contains(s) ||
                        (e.Details ?? "").ToLower().Contains(s) ||
                        (e.Action ?? "").ToLower().Contains(s) ||
                        (e.CreatedAt.HasValue && e.CreatedAt.Value.ToLocalTime().ToString("g").ToLower().Contains(s))
                    ).ToList()
                };
            }

            Entries = all;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Entries = new();
        }
        return Page();
    }

    private static List<ActivityLog> FilterByWeek(List<ActivityLog> all, string s)
    {
        // Accept "this week", "last week", or a date inside that week
        var now = DateTime.Now.Date;
        int diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
        var thisMonday = now.AddDays(-diff);
        var nextMonday = thisMonday.AddDays(7);
        var lastMonday = thisMonday.AddDays(-7);

        if (s.Contains("this"))
            return all.Where(e => e.CreatedAt.HasValue &&
                e.CreatedAt.Value.ToLocalTime().Date >= thisMonday &&
                e.CreatedAt.Value.ToLocalTime().Date < nextMonday).ToList();

        if (s.Contains("last"))
            return all.Where(e => e.CreatedAt.HasValue &&
                e.CreatedAt.Value.ToLocalTime().Date >= lastMonday &&
                e.CreatedAt.Value.ToLocalTime().Date < thisMonday).ToList();

        // fallback: any date string match in created_at local
        return all.Where(e => e.CreatedAt.HasValue &&
            e.CreatedAt.Value.ToLocalTime().ToString("yyyy-MM-dd MMM d").ToLower().Contains(s)).ToList();
    }
}
