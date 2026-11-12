using GrabLog.Models;

namespace GrabLog.Services;

public class NotificationService
{
    private readonly SupabaseClient _db;

    public NotificationService(SupabaseClient db) => _db = db;

    public async Task NotifyAsync(string type, string title, string message, string? toolId = null, string? userName = null, string forRole = "all")
    {
        try
        {
            await _db.PostAsync<AppNotification>("notifications", new
            {
                type,
                title,
                message,
                tool_id = toolId,
                user_name = userName,
                for_role = forRole,
                is_read = false,
                created_at = DateTime.UtcNow.ToString("o")
            });
        }
        catch
        {
            // never break tool actions if notify fails
        }
    }

    public async Task<List<AppNotification>> GetRecentAsync(int limit = 30)
    {
        return await _db.GetAsync<AppNotification>("notifications",
            $"select=*&order=created_at.desc&limit={limit}");
    }

    public async Task<List<AppNotification>> GetUnreadAsync(string role)
    {
        var all = await GetRecentAsync(50);
        return all.Where(n => !n.IsRead && (n.ForRole == "all" || n.ForRole == role)).ToList();
    }

    public async Task MarkReadAsync(string id)
    {
        await _db.PatchAsync("notifications", $"id=eq.{Uri.EscapeDataString(id)}", new { is_read = true });
    }

    public async Task MarkAllReadAsync()
    {
        var unread = (await GetRecentAsync(100)).Where(n => !n.IsRead).ToList();
        foreach (var n in unread)
        {
            if (n.Id.HasValue)
                await MarkReadAsync(n.Id.Value.ToString());
        }
    }
}
