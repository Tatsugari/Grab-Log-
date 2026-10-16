using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GrabLog.Services;

public class SupabaseClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public SupabaseClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri(SupabaseConfig.Url.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("apikey", SupabaseConfig.Key);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SupabaseConfig.Key);
        _http.DefaultRequestHeaders.Add("Prefer", "return=representation");
    }

    public async Task<List<T>> GetAsync<T>(string table, string? query = null)
    {
        var path = $"rest/v1/{table}?{query ?? "select=*"}";
        var res = await _http.GetAsync(path);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new Exception($"Supabase GET {table} failed: {(int)res.StatusCode} {body}");
        return JsonSerializer.Deserialize<List<T>>(body, JsonOpts) ?? new();
    }

    public async Task<T?> GetOneAsync<T>(string table, string query)
    {
        var list = await GetAsync<T>(table, query);
        return list.FirstOrDefault();
    }

    public async Task<List<T>> PostAsync<T>(string table, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await _http.PostAsync($"rest/v1/{table}", content);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new Exception($"Supabase POST {table} failed: {(int)res.StatusCode} {body}");
        return JsonSerializer.Deserialize<List<T>>(body, JsonOpts) ?? new();
    }

    public async Task PatchAsync(string table, string filter, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Patch, $"rest/v1/{table}?{filter}") { Content = content };
        var res = await _http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new Exception($"Supabase PATCH {table} failed: {(int)res.StatusCode} {body}");
    }

    public async Task DeleteAsync(string table, string filter)
    {
        var res = await _http.DeleteAsync($"rest/v1/{table}?{filter}");
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            throw new Exception($"Supabase DELETE {table} failed: {(int)res.StatusCode} {body}");
        }
    }
}
