using System.Text.Json;
using GeminiCode.Permissions;
using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class PermissionGateTests
{
    private static Dictionary<string, JsonElement> PathParams(string path)
    {
        var json = JsonSerializer.Serialize(new { path });
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private class FakeTool : ITool
    {
        public string Name { get; init; } = "ReadFile";
        public RiskLevel Risk { get; init; } = RiskLevel.Low;
        public Task<ToolResult> ExecuteAsync(Dictionary<string, JsonElement> parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult(Name, true, "ok"));
        public string DescribeAction(Dictionary<string, JsonElement> parameters) => "test action";
    }

    [Fact]
    public void RequestPermission_UserTypesY_ReturnsApproved()
    {
        var allowlist = new SessionAllowlist();
        var input = new StringReader("y\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);
        var result = gate.RequestPermission(new FakeTool(), PathParams("foo.cs"));
        Assert.Equal(PermissionResult.Approved, result);
    }

    [Fact]
    public void RequestPermission_UserTypesN_ReturnsDenied()
    {
        var allowlist = new SessionAllowlist();
        var input = new StringReader("n\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);
        var result = gate.RequestPermission(new FakeTool(), PathParams("foo.cs"));
        Assert.Equal(PermissionResult.Denied, result);
    }

    [Fact]
    public void RequestPermission_UserTypesA_ReturnsAlwaysAndAddsToAllowlist()
    {
        var allowlist = new SessionAllowlist();
        var input = new StringReader("a\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);
        var result = gate.RequestPermission(new FakeTool { Name = "WriteFile", Risk = RiskLevel.Medium }, PathParams("foo.cs"));
        Assert.Equal(PermissionResult.AlwaysApproved, result);
        Assert.True(allowlist.IsAllowed("WriteFile"));
    }

    [Fact]
    public void RequestPermission_AllowlistedTool_SkipsPrompt()
    {
        var allowlist = new SessionAllowlist();
        allowlist.Add("ReadFile");
        var gate = new PermissionGate(allowlist, new StringReader(""), new StringWriter());
        var result = gate.RequestPermission(new FakeTool(), PathParams("foo.cs"));
        Assert.Equal(PermissionResult.Approved, result);
    }

    [Fact]
    public void RequestPermission_RunCommand_AlwaysRejected_FallsBackToYes()
    {
        var allowlist = new SessionAllowlist();
        var input = new StringReader("a\ny\n");
        var output = new StringWriter();
        var gate = new PermissionGate(allowlist, input, output);
        var tool = new FakeTool { Name = "RunCommand", Risk = RiskLevel.High };
        var cmdParams = JsonSerializer.Serialize(new { command = "ls" });
        using var doc = JsonDocument.Parse(cmdParams);
        var p = doc.RootElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone());
        var result = gate.RequestPermission(tool, p);
        Assert.Equal(PermissionResult.Approved, result);
        Assert.False(allowlist.IsAllowed("RunCommand"));
    }
}
