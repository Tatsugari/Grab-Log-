using GrabLog.Models;

namespace GrabLog.Services;

public class ToolService
{
    private readonly SupabaseClient _db;
    private readonly LogService _logs;
    private readonly NotificationService _notifs;
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromHours(9);

    public ToolService(SupabaseClient db, LogService logs, NotificationService notifs)
    {
        _db = db;
        _logs = logs;
        _notifs = notifs;
    }

    public async Task<List<Tool>> GetAllAsync()
    {
        var tools = await _db.GetAsync<Tool>("tools", "select=*&order=id.asc");
        foreach (var t in tools)
        {
            if (t.Status != "inuse" || !t.BorrowedAt.HasValue) continue;
            var elapsed = DateTime.UtcNow - t.BorrowedAt.Value.ToUniversalTime();
            if (elapsed < OverdueAfter) continue;

            t.Status = "overdue";
            try
            {
                await _db.PatchAsync("tools", $"id=eq.{Uri.EscapeDataString(t.Id)}", new { status = "overdue" });
                await _logs.WriteAsync("overdue", t.Id, t.Name, t.LoggedTo ?? "system", null,
                    $"Auto-marked overdue after {OverdueAfter.TotalHours:0}h");
                await _notifs.NotifyAsync("overdue",
                    $"⚠️ Overdue: {t.Name}",
                    $"{t.Name} ({t.Id}) is overdue — logged to {t.LoggedTo ?? "unknown"}. Past 9h shift (8AM–5PM).",
                    t.Id, t.LoggedTo, "all");
            }
            catch { }
        }
        return tools;
    }

    public async Task<List<Tool>> FilterAsync(string f)
    {
        var all = await GetAllAsync();
        return f switch
        {
            "available" => all.Where(t => t.Status == "available").ToList(),
            "inuse" => all.Where(t => t.Status == "inuse").ToList(),
            "overdue" => all.Where(t => t.Status == "overdue").ToList(),
            "disposed" => all.Where(t => t.Status == "disposed").ToList(),
            _ => all
        };
    }

    public async Task BorrowAsync(string id, string op, string? email = null, string? cardUid = null)
    {
        var tools = await _db.GetAsync<Tool>("tools", $"select=*&id=eq.{Uri.EscapeDataString(id)}");
        var t = tools.FirstOrDefault();
        // Full name from RFID (not first word only)
        var who = string.IsNullOrWhiteSpace(op) ? "Unknown" : op.Trim();
        var now = DateTime.UtcNow;
        await _db.PatchAsync("tools", $"id=eq.{Uri.EscapeDataString(id)}", new
        {
            status = "inuse",
            logged_to = who,
            borrowed_at = now.ToString("o"),
            borrowed_card_uid = string.IsNullOrWhiteSpace(cardUid) ? null : cardUid.Trim()
        });
        var name = t?.Name ?? id;
        var detail = string.IsNullOrEmpty(cardUid)
            ? $"Borrowed at {now.ToLocalTime():MMM d, yyyy h:mm tt}"
            : $"RFID {cardUid} · {who} · Borrowed at {now.ToLocalTime():MMM d, yyyy h:mm tt}";
        await _logs.WriteAsync("borrow", id, name, who, email, detail);
        await _notifs.NotifyAsync("borrow",
            $"📦 Borrowed: {name}",
            $"{who} borrowed {name} ({id}) at {now.ToLocalTime():h:mm tt}. Due within 9 hours.",
            id, who, "all");
    }

    public async Task ReturnAsync(string id, string? userName = null, string? email = null, string? cardUid = null)
    {
        var tools = await _db.GetAsync<Tool>("tools", $"select=*&id=eq.{Uri.EscapeDataString(id)}");
        var t = tools.FirstOrDefault();
        await _db.PatchAsync("tools", $"id=eq.{Uri.EscapeDataString(id)}", new
        {
            status = "available",
            logged_to = (string?)null,
            borrowed_at = (string?)null,
            borrowed_card_uid = (string?)null
        });
        var who = string.IsNullOrWhiteSpace(userName) ? (t?.LoggedTo ?? "unknown") : userName.Trim();
        var name = t?.Name ?? id;
        var detail = string.IsNullOrEmpty(cardUid)
            ? $"Returned at {DateTime.Now:MMM d, yyyy h:mm tt}"
            : $"RFID {cardUid} · {who} · Returned at {DateTime.Now:MMM d, yyyy h:mm tt}";
        await _logs.WriteAsync("return", id, name, who, email, detail);
        await _notifs.NotifyAsync("return",
            $"✅ Returned: {name}",
            $"{who} returned {name} ({id}) at {DateTime.Now:h:mm tt}.",
            id, who, "all");
    }

    public async Task DisposeAsync(string id, string? userName = null, string? email = null)
    {
        var tools = await _db.GetAsync<Tool>("tools", $"select=*&id=eq.{Uri.EscapeDataString(id)}");
        var t = tools.FirstOrDefault();
        await _db.PatchAsync("tools", $"id=eq.{Uri.EscapeDataString(id)}", new
        {
            status = "disposed",
            location = "Disposal Bin",
            logged_to = (string?)null,
            borrowed_at = (string?)null
        });
        var name = t?.Name ?? id;
        await _logs.WriteAsync("dispose", id, name, userName ?? "admin", email,
            $"Disposed at {DateTime.Now:MMM d, yyyy h:mm tt}");
        await _notifs.NotifyAsync("dispose",
            $"🗑️ Disposed: {name}",
            $"{userName ?? "admin"} disposed {name} ({id}).",
            id, userName, "admin");
    }

    public async Task RestoreAsync(string id, string location = "Locker A-00", string? userName = null, string? email = null)
    {
        var tools = await _db.GetAsync<Tool>("tools", $"select=*&id=eq.{Uri.EscapeDataString(id)}");
        var t = tools.FirstOrDefault();
        await _db.PatchAsync("tools", $"id=eq.{Uri.EscapeDataString(id)}", new
        {
            status = "available",
            location,
            logged_to = (string?)null,
            borrowed_at = (string?)null
        });
        var name = t?.Name ?? id;
        await _logs.WriteAsync("restore", id, name, userName ?? "admin", email,
            $"Restored at {DateTime.Now:MMM d, yyyy h:mm tt}");
        await _notifs.NotifyAsync("restore",
            $"♻️ Restored: {name}",
            $"{userName ?? "admin"} restored {name} ({id}) to available.",
            id, userName, "admin");
    }

    public static double HoursUntilOverdue(Tool t)
    {
        if (t.BorrowedAt == null) return 9;
        var elapsed = DateTime.UtcNow - t.BorrowedAt.Value.ToUniversalTime();
        return (OverdueAfter - elapsed).TotalHours;
    }
}
