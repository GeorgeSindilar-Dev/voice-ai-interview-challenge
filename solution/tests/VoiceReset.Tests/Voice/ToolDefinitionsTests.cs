using System.Text.Json;
using Azure.AI.VoiceLive;
using VoiceReset.Voice;

namespace VoiceReset.Tests.Voice;

public sealed class ToolDefinitionsTests
{
    [Fact]
    public void All_Always_AreTheSevenContractTools()
    {
        var names = ToolDefinitions.All.Select(tool => tool.Name);

        Assert.Equal(
            ["start_recovery", "submit_code", "send_reset_link", "check_reset_status", "request_human", "cancel_reset", "end_call"],
            names);
    }

    [Fact]
    public void All_Parameters_AreOnlyUsernameAndCode()
    {
        // Every "tool.parameter" pair the model can fill in: no IDs, receipts, destinations or free text.
        var parameters = ToolDefinitions.All.SelectMany(tool =>
            Schema(tool).GetProperty("properties").EnumerateObject().Select(p => $"{tool.Name}.{p.Name}"));

        Assert.Equal(["start_recovery.username", "submit_code.code"], parameters);
    }

    [Fact]
    public void All_Schemas_RejectExtraProperties()
    {
        Assert.All(ToolDefinitions.All, tool =>
            Assert.False(Schema(tool).GetProperty("additionalProperties").GetBoolean()));
    }

    private static JsonElement Schema(VoiceLiveFunctionDefinition tool) =>
        JsonDocument.Parse(tool.Parameters.ToString()).RootElement;
}
