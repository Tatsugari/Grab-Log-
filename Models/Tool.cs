using System.Text.Json.Serialization;

namespace GrabLog.Models;
public class Tool {
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string Icon { get; set; } = "🔧";
  public string Status { get; set; } = "available";
  public string Location { get; set; } = "";
  [JsonPropertyName("logged_to")]
  public string? LoggedTo { get; set; }
  [JsonPropertyName("borrowed_at")]
  public DateTime? BorrowedAt { get; set; }
  [JsonPropertyName("borrowed_card_uid")]
  public string? BorrowedCardUid { get; set; }
}
