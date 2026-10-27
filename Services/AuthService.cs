using GrabLog.Models;

namespace GrabLog.Services;

public class AuthService
{
    private readonly SupabaseClient _db;
    public Operator? Current { get; private set; }

    public AuthService(SupabaseClient db) => _db = db;

    public async Task<bool> RegisterAsync(string name, string email, string pin, string role = "member")
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pin))
            return false;

        role = role == "admin" ? "admin" : "member";

        var existing = await _db.GetAsync<Operator>("operators",
            $"select=*&email=eq.{Uri.EscapeDataString(email.Trim().ToLowerInvariant())}");
        if (existing.Count > 0) return false;

        var created = await _db.PostAsync<Operator>("operators", new
        {
            name = name.Trim(),
            email = email.Trim().ToLowerInvariant(),
            pin,
            role
        });
        Current = created.FirstOrDefault();
        return Current != null;
    }

    public async Task<bool> LoginAsync(string email, string pin, string? requiredRole = null)
    {
        // Role is always taken from the database (admin vs member auto-detected)
        var list = await _db.GetAsync<Operator>("operators",
            $"select=*&email=eq.{Uri.EscapeDataString(email.Trim().ToLowerInvariant())}&pin=eq.{Uri.EscapeDataString(pin)}");

        if (list.Count == 0)
        {
            // Optional: auto-create as member on first login
            var part = email.Contains('@') ? email.Split('@')[0] : email;
            part = part.Replace(".", " ").Replace("_", " ");
            var name = string.Join(" ", part.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLower() : "")));
            if (string.IsNullOrWhiteSpace(name)) name = "Operator";
            return await RegisterAsync(name, email, pin, "member");
        }

        Current = list[0];
        return true;
    }

    public string Initials
    {
        get
        {
            if (Current == null) return "—";
            var parts = Current.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Take(2).Select(p => char.ToUpper(p[0])));
        }
    }
}
