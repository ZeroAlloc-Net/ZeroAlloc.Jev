using System.Buffers;
using System.Text.Json;
using Minos.Transport;

namespace Minos.Protocols;

/// <summary>
/// Writes a <c>/v1/systemone</c> request body straight into a pooled <see cref="RawJson"/>: the state, the model and the
/// protocol's cached <c>questions</c> bytes for a definition, without building a <see cref="SystemOneRequest"/>. The body
/// is JSON-equal to a <see cref="SystemOneRequest"/> with the same questions, state and model, serialized.
/// </summary>
internal static class SystemOneRequestWriter
{
    // Room for the property names, the model and the braces around them, so a typical body needs no regrowth.
    private const int Overhead = 64;

    // Rents the body and writes {"state":, then the state through writeState, then ,"model":…,"questions":…}. The body
    // is disposed if anything throws, so the caller owns it only once it is returned.
    public static RawJson Write<TArg>(
        ReadOnlySpan<byte> questionsUtf8, TArg state, int stateSizeHint, StateWriter<TArg> writeState, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct
    {
        var body = RawJson.Create(pool, questionsUtf8.Length + model.Length + stateSizeHint + Overhead);
        try
        {
            using (var writer = new Utf8JsonWriter(body))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("state"u8);
                writeState(writer, body, state);
                writer.WriteString("model"u8, model);
                writer.WritePropertyName("questions"u8);
                writer.WriteRawValue(questionsUtf8, skipInputValidation: true);
                writer.WriteEndObject();
                writer.Flush();
            }

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }
}
