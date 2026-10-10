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
    [InlineData("[1]", "The response body is not a JSON object.")]
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
        Assert.Null(result.Error.Exception);
    }

    // Moved from TypedEvaluationTests with TypedEvaluation.ParseResponse: the envelope reader replaces it.
    [Theory]
    [InlineData("response-noul.json")]
    [InlineData("response-type-last.json")]
    [InlineData("response-openrouter.json")]
    public void Finds_the_answers_in_each_response_fixture(string fixture)
    {
        var result = SystemOneProtocol.Instance.ReadResponse(System.Text.Encoding.UTF8.GetBytes(Fixture.Text(fixture)), UrgencyCheck.Definition);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResponseAnswers.Parse<UrgencyCheck>(Fixture.Text(fixture)), UrgencyCheck.Create(result.Value.ToAnswerSlots()));
    }

    [Fact]
    public void Answers_before_the_envelope_fields_are_found()
    {
        var result = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"model":"jev-1.13.0","usage":{"input_tokens":1,"output_tokens":1}}"""u8, Urgency);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.3, result.Value.Answers[0].Value);
        Assert.Equal("jev-1.13.0", result.Value.Model);
    }

    [Fact]
    public void Nested_answers_properties_are_skipped()
    {
        var result = SystemOneProtocol.Instance.ReadResponse(
            """{"meta":{"answers":{}},"answers":{"is_urgent":{"type":"noul","noul":0.3}},"extra":{"answers":null}}"""u8, Urgency);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.3, result.Value.Answers[0].Value);
    }

    [Theory]
    [InlineData("""{"answers":{"department":{"type":"choice","choice":"billing","probabilities":{},"confidence":1}}}""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"usage":""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}}} trailing""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.3}},"model":tru}""")]
    [InlineData("")]
    public void Rejected_or_malformed_bodies_keep_the_json_exception(string json)
    {
        var result = SystemOneProtocol.Instance.ReadResponse(System.Text.Encoding.UTF8.GetBytes(json), Urgency);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        Assert.Equal(200, result.Error.StatusCode);
        Assert.IsAssignableFrom<System.Text.Json.JsonException>(result.Error.Exception);
    }

    // Moved from EvaluatedTests with Evaluated: the envelope reader keeps its lenient usage reads.
    [Fact]
    public void Token_counts_outside_the_int32_range_are_absent()
    {
        var response = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"usage":{"input_tokens":3000000000,"output_tokens":3000000000}}"""u8, Urgency).Value;

        Assert.Null(response.InputTokens);
        Assert.Null(response.OutputTokens);
        Assert.Null(response.Usage);
    }

    [Fact]
    public void String_or_null_token_counts_are_absent()
    {
        var response = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"usage":{"input_tokens":"7","output_tokens":null}}"""u8, Urgency).Value;

        Assert.Null(response.InputTokens);
        Assert.Null(response.OutputTokens);
        Assert.Null(response.Usage);
    }

    [Theory]
    [InlineData("""{"model":"m-1","usage":{"input_tokens":7,"output_tokens":3},"answers":{"is_urgent":{"type":"noul","noul":0.5}}}""")]
    [InlineData("""{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"model":"m-1","usage":{"input_tokens":7,"output_tokens":3}}""")]
    [InlineData("""{"model":"m-1","answers":{"is_urgent":{"type":"noul","noul":0.5}},"usage":{"output_tokens":3,"input_tokens":7}}""")]
    public void Model_and_usage_are_read_wherever_they_are(string json)
    {
        var response = SystemOneProtocol.Instance.ReadResponse(System.Text.Encoding.UTF8.GetBytes(json), Urgency).Value;

        Assert.Equal("m-1", response.Model);
        Assert.Equal(7, response.Usage!.InputTokens);
        Assert.Equal(3, response.Usage.OutputTokens);
    }

    [Fact]
    public void A_confidence_key_inside_an_answer_is_not_its_confidence_and_confidences_enumerate_twice()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Choice(
            "a",
            DecisionContent.FromString("Which?"),
            new OptionDefinition("confidence", null),
            new OptionDefinition("other", null)));
        var response = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"a":{"type":"choice","choice":"confidence","probabilities":{"confidence":0.3,"other":0.7},"confidence":0.6}}}"""u8,
            definition).Value;

        Assert.Equal([0.6], Confidences(response));
        Assert.Equal([0.6], Confidences(response));
    }

    [Fact]
    public void A_string_equal_to_the_cached_one_is_returned_as_that_string()
    {
        string? cache = null;

        var first = ReadCached("\"minos-1\""u8, ref cache);
        var again = ReadCached("\"minos-1\""u8, ref cache);
        var other = ReadCached("\"minos-2\""u8, ref cache);
        var notAString = ReadCached("5"u8, ref cache);

        Assert.Equal("minos-1", first);
        Assert.Same(first, again);
        Assert.Equal("minos-2", other);
        Assert.Same(other, cache);
        Assert.Null(notAString);
        Assert.Same(other, cache);
    }

    [Fact]
    public void Usage_is_built_once_on_first_access()
    {
        var response = SystemOneProtocol.Instance.ReadResponse(
            """{"answers":{"is_urgent":{"type":"noul","noul":0.5}},"usage":{"input_tokens":7,"output_tokens":3,"cost":0.5}}"""u8, Urgency).Value;

        var usage = response.Usage;

        Assert.Equal(new DecisionUsage { InputTokens = 7, OutputTokens = 3, Cost = 0.5 }, usage);
        Assert.Same(usage, response.Usage);
    }

    [Fact]
    public void Matches_the_protocols_answer_reader_for_every_wire_fixture()
    {
        foreach (var (definition, responseJson) in ResponseFixtureSets.Responses())
        {
            var neutral = SystemOneProtocol.Instance.ReadResponse(responseJson, definition);

            (AnswerSlot[] Slots, double[] Probabilities) direct;
            try
            {
                direct = ResponseAnswers.Read(responseJson, definition, static slots => (slots.Slots.ToArray(), slots.Probabilities));
            }
            catch (System.Text.Json.JsonException exception)
            {
                Assert.True(neutral.IsFailure);
                Assert.Equal(DecisionErrorKind.InvalidResponse, neutral.Error.Kind);
                Assert.Equal("The response could not be read as the question set's answers: " + exception.Message, neutral.Error.Message);
                continue;
            }

            Assert.True(neutral.IsSuccess);
            Assert.Equal(direct.Slots, neutral.Value.Slots);
            Assert.Equal(direct.Probabilities, neutral.Value.Probabilities);
        }
    }

    private static List<double> Confidences(DecisionResponse response)
    {
        var values = new List<double>();
        foreach (var confidence in response.Confidences)
        {
            values.Add(confidence);
        }

        return values;
    }

    private static string? ReadCached(ReadOnlySpan<byte> json, ref string? cache)
    {
        var reader = new System.Text.Json.Utf8JsonReader(json);
        reader.Read();
        return SystemOneResponseReader.CachedStringOrSkip(ref reader, ref cache);
    }
}
