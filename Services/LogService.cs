using GrabLog.Models;

namespace GrabLog.Services;

public class LogService
{
    private readonly SupabaseClient _db;

    public LogService(SupabaseClient db) => _db = db;

    public async Task WriteAsync(string action, string toolId, string toolName, string userName, string? userEmail = null, string? details = null)
    {
        try
        {
            await _db.PostAsync<ActivityLog>("logs", new
            {
                action,
                tool_id = toolId,
                tool_name = toolName,
                user_name = userName,
                user_email = userEmail,
                details,
                created_at = DateTime.UtcNow.ToString("o")
            });
        }
        catch
        {
            // Don't break tool actions if logging fails
        }
    }

    public async Task<List<ActivityLog>> GetRecentAsync(int limit = 100)
    {
        return await _db.GetAsync<ActivityLog>("logs", $"select=*&order=created_at.desc&limit={limit}");
    }

    public async Task<List<ActivityLog>> GetForToolAsync(string toolId)
    {
        return await _db.GetAsync<ActivityLog>("logs",
            $"select=*&tool_id=eq.{Uri.EscapeDataString(toolId)}&order=created_at.desc");
    }
}
