using System.Text;
using System.Text.Json;
using Minos.Protocols;

namespace Minos.Tests;

/// <summary>Positions a reader on a response's <c>answers</c> object and reads them through the protocol.</summary>
internal static class ResponseAnswers
{
    /// <summary>Reads the answers into the generated type.</summary>
    public static T Parse<T>(string responseJson)
        where T : IQuestionSet<T>
        => Read(Encoding.UTF8.GetBytes(responseJson), T.Definition, static answers => T.Create(answers));

    /// <summary>
    /// Reads the answers with <c>SystemOneProtocol.ReadAnswers</c> alone and every other top-level value with a plain
    /// <see cref="Utf8JsonReader"/>, to the end of the body, so malformed or trailing JSON anywhere throws
    /// <see cref="JsonException"/>, as the reader under test reports it.
    /// </summary>
    public static TResult Read<TResult>(byte[] responseJson, QuestionSetDefinition definition, AnswerFactory<TResult> create)
    {
        var reader = new Utf8JsonReader(responseJson);
        reader.Read();
        var found = false;
        TResult result = default!;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isAnswers = reader.ValueTextEquals("answers"u8);
            reader.Read();
            if (isAnswers && !found)
            {
                result = SystemOneProtocol.Instance.ReadAnswers(ref reader, definition, create);
                found = true;
                continue;
            }

            reader.Skip();
        }

        // Reads past the root object, so trailing content after it throws.
        reader.Read();
        return found ? result : throw new InvalidOperationException("The response has no 'answers' property.");
    }
}
