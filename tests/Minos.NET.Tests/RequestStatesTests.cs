using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Protocols;
using Minos.Transport;

namespace Minos.Tests;

/// <summary>The protocol writes the definition's questions around each kind of state, so a set built at run time can use it too.</summary>
public sealed class RequestStatesTests
{
    [Fact]
    public void Write_CopiesTheDefinitionsQuestions()
    {
        var pool = new CountingPool();
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("q", "x"));

        using (var body = SystemOneProtocol.Instance.WriteRequest(definition, "text", "m", pool))
        {
            var sent = JsonNode.Parse(body.Span)!;
            Assert.Equal("""{"q":{"type":"noul","instructions":"x"}}""", sent["questions"]!.ToJsonString());
            Assert.Equal("text", (string?)sent["state"]);
            Assert.Equal("m", (string?)sent["model"]);
        }

        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void WriteJsonElement_CopiesTheDefinitionsQuestions()
    {
        var pool = new CountingPool();
        using var state = JsonDocument.Parse("""{"a":1}""");

        using (var body = SystemOneProtocol.Instance.WriteRequest(UrgencyCheck.Definition, state.RootElement, "m", pool))
        {
            var sent = JsonNode.Parse(body.Span)!;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition)), sent["questions"]));
            Assert.Equal(1, (int)sent["state"]!["a"]!);
            Assert.Equal("m", (string?)sent["model"]);
        }

        Assert.Equal(0, pool.Outstanding);
    }
}
