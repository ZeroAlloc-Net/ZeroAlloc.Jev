using System.Globalization;
using System.Text.Json;

namespace Minos.Protocols;

/// <summary>How <c>/v1/systemone</c> and OpenRouter report errors: the status decides the kind, and a whole JSON body becomes <see cref="DecisionError.Detail"/>.</summary>
internal static class SystemOneErrors
{
    public static DecisionError Map(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter)
    {
        var kind = statusCode switch
        {
            401 or 403 => DecisionErrorKind.Unauthorized,
            400 or 422 => DecisionErrorKind.Validation,
            429 => DecisionErrorKind.RateLimited,
            503 or 529 => DecisionErrorKind.Overloaded,
            >= 500 => DecisionErrorKind.Server,
            _ => DecisionErrorKind.Http,
        };

        return new DecisionError(kind, "The API returned HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ".")
        {
            StatusCode = statusCode,
            RetryAfter = retryAfter,
            Detail = Detail(body, bodyTruncated, contentType),
        };
    }

    private static JsonElement? Detail(ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType)
    {
        if (body.IsEmpty || bodyTruncated || !IsJson(contentType))
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(body);
            using var document = JsonDocument.ParseValue(ref reader);

            // Reads past the root value, so trailing content after it is rejected as JsonDocument.Parse rejects it.
            reader.Read();
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsJson(string? contentType)
        => contentType is not null
            && (string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
