using System.Text.Json.Serialization;

namespace GrabLog.Models;
public class Operator {
  public Guid? Id { get; set; }
  public string Name { get; set; } = "";
  public string Email { get; set; } = "";
  public string Pin { get; set; } = "";
  public string Role { get; set; } = "member"; // admin | member
}
