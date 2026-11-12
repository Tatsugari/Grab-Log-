using System.Text.Json.Serialization;

namespace GrabLog.Models;

public class AppNotification
{
    public Guid? Id { get; set; }
    public string Type { get; set; } = ""; // borrow, return, overdue, dispose
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    [JsonPropertyName("tool_id")]
    public string? ToolId { get; set; }
    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }
    [JsonPropertyName("for_role")]
    public string ForRole { get; set; } = "all"; // all, admin, member
    [JsonPropertyName("is_read")]
    public bool IsRead { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}
