using Minos.Protocols;

namespace Minos.Tests;

public sealed class SystemOneResponseReaderTests
{
    private static readonly QuestionSetDefinition Urgency = QuestionSets.UrgencyDefinition();

    [Fact]
    public void Reads_the_envelope_and_the_answers()
    {
        var body = """
            {"id":"r-1","provider":"Groq","model":"minos-1.13.0",
             "answers":{"is_urgent":{"type":"noul","noul":0.95}},
             "usage":{"input_tokens":296,"output_tokens":20,"cost":0.0012}}
            """u8;

        var result = SystemOneProtocol.Instance.ReadResponse(body, Urgency);

        Assert.True(result.IsSuccess);
        var response = result.Value;
        Assert.Equal("minos-1.13.0", response.Model);
        Assert.Equal("r-1", response.Id);
        Assert.Equal("Groq", response.Provider);
        Assert.Equal(296, response.Usage!.InputTokens);
        Assert.Equal(20, response.Usage.OutputTokens);
        Assert.Equal(0.0012, response.Usage.Cost);
        Assert.Equal(0.95, response.Answers[0].Value);
    }

    [Fact]
    public void Missing_model_and_usage_are_not_errors()
    {
        var result = SystemOneProtocol.Instance.ReadResponse("""{"answers":{"is_urgent":{"type":"noul","noul":0.5}}}"""u8, Urgency);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Model);
        Assert.Null(result.Value.Usage);
        Assert.Null(result.Value.InputTokens);
    }

    [Fact]
    public void One_token_count_is_kept_without_a_usage()
    {
        var result = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"usage":{"input_tokens":7,"output_tokens":"x"}}"""u8, Urgency);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Usage);
        Assert.Equal(7, result.Value.InputTokens);
        Assert.Null(result.Value.OutputTokens);
    }

    [Fact]
    public void A_repeated_field_keeps_its_first_value_and_a_second_usage_is_ignored()
    {
        var body = """
            {"model":"first","model":"second","id":"a","id":"b","provider":"p1","provider":"p2",
             "answers":{"is_urgent":{"type":"noul","noul":0.5}},
             "usage":{"input_tokens":1,"input_tokens":2,"output_tokens":3,"output_tokens":4,"cost":0.5,"cost":0.9},
             "usage":{"input_tokens":8,"output_tokens":9,"cost":1.5}}
            """u8;

        var response = SystemOneProtocol.Instance.ReadResponse(body, Urgency).Value;

        Assert.Equal("first", response.Model);
        Assert.Equal("a", response.Id);
        Assert.Equal("p1", response.Provider);
        Assert.Equal(1, response.InputTokens);
        Assert.Equal(3, response.OutputTokens);
        Assert.Equal(0.5, response.Cost);
    }

    [Theory]
    [InlineData("""{"model":5,"model":"x","answers":{"is_urgent":{"type":"noul","noul":0.5}}}""")]
    [InlineData("""{"id":5,"id":"x","answers":{"is_urgent":{"type":"noul","noul":0.5}}}""")]
    [InlineData("""{"provider":5,"provider":"x","answers":{"is_urgent":{"type":"noul","noul":0.5}}}""")]
    public void A_wrong_typed_first_string_field_decides_null_over_a_later_valid_one(string json)
    {
        var response = SystemOneProtocol.Instance.ReadResponse(System.Text.Encoding.UTF8.GetBytes(json), Urgency).Value;

        Assert.Null(response.Model);
        Assert.Null(response.Id);
        Assert.Null(response.Provider);
    }

    [Fact]
    public void A_first_usage_without_input_tokens_is_not_completed_by_a_second_usage()
    {
        var response = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"usage":{"output_tokens":2},"usage":{"input_tokens":1,"output_tokens":9}}"""u8, Urgency).Value;

        Assert.Null(response.InputTokens);
        Assert.Equal(2, response.OutputTokens);
        Assert.Null(response.Usage);
    }

    [Fact]
    public void A_wrong_typed_first_usage_member_decides_null_over_a_later_valid_one()
    {
        var response = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"usage":{"input_tokens":"a","input_tokens":3,"output_tokens":4}}"""u8, Urgency).Value;

        Assert.Null(response.InputTokens);
        Assert.Equal(4, response.OutputTokens);
    }

    [Fact]
    public void Fields_of_the_wrong_type_are_treated_as_absent()
    {
        var result = SystemOneProtocol.Instance.ReadResponse(
            """{"model":5,"id":{},"provider":[1],"answers":{"is_urgent":{"type":"noul","noul":0.5}}}"""u8, Urgency);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Model);
        Assert.Null(result.Value.Id);
        Assert.Null(result.Value.Provider);
    }

    [Fact]
    public void A_leading_utf8_bom_is_skipped()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("""{"model":"m","answers":{"is_urgent":{"type":"noul","noul":0.5}}}""");
        byte[] body = [0xEF, 0xBB, 0xBF, .. json];

        var result = SystemOneProtocol.Instance.ReadResponse(body, Urgency);

        Assert.True(result.IsSuccess);
        Assert.Equal("m", result.Value.Model);
    }

    [Theory]
    [InlineData("[]", "The response body is not a JSON object.")]
    [InlineData("""{"model":"m"}""", "The response has no answers.")]
    [InlineData("""{"answers":null}""", "The response's answers are null.")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"answers":{}}""", "The response has more than one answers property.")]
    public void Malformed_envelopes_fail_as_the_typed_path_does(string json, string message)
    {
        var result = SystemOneProtocol.Instance.ReadResponse(System.Text.Encoding.UTF8.GetBytes(json), Urgency);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(message, result.Error.Message);
        Assert.Equal(200, result.Error.StatusCode);
    }

    [Fact]
    public void Matches_the_typed_parser_for_every_wire_fixture()
    {
        foreach (var (definition, responseJson) in ResponseFixtureSets.Responses())
        {
            var neutral = SystemOneProtocol.Instance.ReadResponse(responseJson, definition);
            var typed = TypedEvaluation.ParseResponse(responseJson, Capture(definition), statusCode: 200);

            Assert.Equal(typed.IsSuccess, neutral.IsSuccess);
            if (typed.IsSuccess)
            {
                Assert.Equal(typed.Value.Slots, neutral.Value.Slots);
                Assert.Equal(typed.Value.Probabilities, neutral.Value.Probabilities);
            }
            else
            {
                Assert.Equal(typed.Error.Kind, neutral.Error.Kind);
                Assert.Equal(typed.Error.Message, neutral.Error.Message);
            }
        }
    }

    private static AnswerParser<(AnswerSlot[] Slots, double[] Probabilities)> Capture(QuestionSetDefinition definition)
        => (ref System.Text.Json.Utf8JsonReader answers)
            => SystemOneProtocol.Instance.ReadAnswers(ref answers, definition, static slots => (slots.Slots.ToArray(), slots.Probabilities));
}
