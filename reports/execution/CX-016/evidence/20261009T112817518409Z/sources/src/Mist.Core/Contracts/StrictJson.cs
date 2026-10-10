using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mist.Core.Contracts;

public static class StrictJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) }
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string text)
    {
        using var document = JsonDocument.Parse(text);
        CheckDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(text, Options) ?? throw new JsonException("Null object is not accepted.");
    }
    public static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate field: " + property.Name);
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
}

public static class UnicodeOffsets
{
    public static int ToUtf16(string text, int scalarIndex)
    {
        if (scalarIndex < 0) throw new ArgumentOutOfRangeException(nameof(scalarIndex));
        int scalars = 0, units = 0;
        while (units < text.Length && scalars < scalarIndex)
        {
            if (Rune.DecodeFromUtf16(text.AsSpan(units), out _, out int consumed) != OperationStatus.Done) throw new InvalidDataException("Malformed Unicode text.");
            units += consumed;
            scalars++;
        }
        if (scalars != scalarIndex) throw new ArgumentOutOfRangeException(nameof(scalarIndex));
        return units;
    }
    public static string Slice(string text, int startInclusive, int endExclusive)
    {
        if (endExclusive < startInclusive) throw new ArgumentException("Invalid scalar range.");
        int start = ToUtf16(text, startInclusive), end = ToUtf16(text, endExclusive);
        return text[start..end];
    }
    public static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
