using System.Text;
using System.Text.Json;

namespace Minos.Tests;

/// <summary>Covers the internal <see cref="TypedEvaluation"/> parsing helpers the typed paths share.</summary>
public sealed class TypedEvaluationTests
{
    [Theory]
    [InlineData("response-noul.json")]
    [InlineData("response-type-last.json")]
    [InlineData("response-openrouter.json")]
    public void ParseResponse_FindsTheAnswers(string fixture)
    {
        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(Fixture.Text(fixture)));

        Assert.True(result.IsSuccess);
        Assert.Equal(ResponseAnswers.Parse<UrgencyCheck>(Fixture.Text(fixture)), result.Value);
    }

    [Fact]
    public void ParseResponse_AnswersFirst_IsFound()
    {
        var json = """{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"model":"jev-1.13.0","usage":{"input_tokens":1,"output_tokens":1}}""";

        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.Equal(0.3, result.Value.IsUrgent.Probability);
    }

    [Fact]
    public void ParseResponse_NestedAnswersBeforeTheTopLevelOnes_IsSkipped()
    {
        var json = """{"meta":{"answers":{}},"answers":{"is_urgent":{"type":"noul","noul":0.3}},"usage":{"answers":null}}""";

        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsSuccess);
        Assert.Equal(0.3, result.Value.IsUrgent.Probability);
    }

    [Theory]
    [InlineData("""{"model":"jev-1.13.0"}""", "no answers")]
    [InlineData("""{"answers":null}""", "null")]
    [InlineData("""[1]""", "not a JSON object")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"answers":{}}""", "more than one")]
    public void ParseResponse_UnusableBody_IsInvalidResponse(string json, string messagePart)
    {
        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json), statusCode: 200);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.Contains(messagePart, result.Error.Message, StringComparison.Ordinal);
        Assert.Null(result.Error.Exception);
    }

    [Theory]
    [InlineData("""{"answers":{"department":{"type":"choice","choice":"billing","probabilities":{},"confidence":1}}}""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"usage":""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}}} trailing""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"model":tru}""")]
    [InlineData("")]
    public void ParseResponse_RejectedOrMalformed_KeepsTheJsonException(string json)
    {
        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(Encoding.UTF8.GetBytes(json));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Null(result.Error.StatusCode);
        Assert.IsAssignableFrom<JsonException>(result.Error.Exception);
    }

    [Fact]
    public void ParseResponse_WithLeadingUtf8Bom_IsParsed()
    {
        var json = Fixture.Text("response-noul.json");
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();

        var result = TypedEvaluation.ParseResponse<UrgencyCheck>(withBom);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
    }

    [Fact]
    public void ParseResponse_WithAParser_RunsIt()
    {
        var result = TypedEvaluation.ParseResponse(
            Encoding.UTF8.GetBytes(Fixture.Text("response-noul.json")), GeneratedAnswerParser<UrgencyCheck>.Instance);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);
    }

    [Fact]
    public void GeneratedAnswerParser_IsCreatedOnce()
        => Assert.Same(GeneratedAnswerParser<UrgencyCheck>.Instance, GeneratedAnswerParser<UrgencyCheck>.Instance);
}
