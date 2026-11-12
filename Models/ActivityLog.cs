using System.Text.Json.Serialization;

namespace GrabLog.Models;

public class ActivityLog
{
    public Guid? Id { get; set; }
    public string Action { get; set; } = "";       // borrow, return, dispose, restore, overdue
    [JsonPropertyName("tool_id")]
    public string ToolId { get; set; } = "";
    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = "";
    [JsonPropertyName("user_name")]
    public string UserName { get; set; } = "";
    [JsonPropertyName("user_email")]
    public string? UserEmail { get; set; }
    public string? Details { get; set; }
    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }
}
