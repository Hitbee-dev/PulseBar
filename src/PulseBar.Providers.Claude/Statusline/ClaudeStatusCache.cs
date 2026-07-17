using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseBar.Providers.Claude.Statusline;

/// <summary>Normalized cache written by the bridge and read by the provider (spec §10.1).</summary>
public sealed class ClaudeStatusCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int SchemaVersion { get; set; } = 1;
    public string Provider { get; set; } = "claude";
    public CachedModel? Model { get; set; }
    public CachedRateLimits? RateLimits { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ClaudeStatusCache? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ClaudeStatusCache>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class CachedModel
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
}

public sealed class CachedRateLimits
{
    public CachedWindow? FiveHour { get; set; }
    public CachedWindow? SevenDay { get; set; }
}

public sealed class CachedWindow
{
    public double? UsedPercent { get; set; }

    /// <summary>Unix seconds, matching the statusline payload.</summary>
    public long? ResetsAt { get; set; }
}
