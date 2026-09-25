using System.Text.Json;

namespace PrinterShareFixer.Core.Runtime;

/// <summary>把 PowerShell ConvertTo-Json 的结果转成易用结构。</summary>
internal static class JsonHelpers
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static JsonElement? ParseSingle(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json, Options);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<JsonElement> ParseObjects(string json)
    {
        var root = ParseSingle(json);
        if (root is null)
        {
            return [];
        }

        return root.Value.ValueKind switch
        {
            JsonValueKind.Array => root.Value.EnumerateArray().Select(e => e.Clone()).ToList(),
            JsonValueKind.Object => [root.Value],
            _ => [],
        };
    }

    public static string String(JsonElement element, string property, string fallback = "")
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Null => fallback,
            JsonValueKind.Undefined => fallback,
            _ => value.ToString(),
        };
    }

    public static int? Int(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    public static bool? Bool(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    public static IReadOnlyList<JsonElement> Array(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return [];
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Select(e => e.Clone()).ToList(),
            JsonValueKind.Object => [value.Clone()],
            _ => [],
        };
    }
}
