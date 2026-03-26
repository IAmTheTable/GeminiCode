using GeminiCode.Tools;

namespace GeminiCode.Tests;

public class PathSandboxTests
{
    private readonly string _workDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sandbox_test"));

    [Fact]
    public void Resolve_RelativePath_ReturnsAbsoluteWithinWorkDir()
    {
        var sandbox = new PathSandbox(_workDir);
        var result = sandbox.Resolve("src/foo.cs");
        Assert.StartsWith(_workDir, result);
        Assert.EndsWith("foo.cs", result);
    }

    [Fact]
    public void Resolve_TraversalAttack_ThrowsSandboxException()
    {
        var sandbox = new PathSandbox(_workDir);
        Assert.Throws<SandboxViolationException>(() => sandbox.Resolve("../../etc/passwd"));
    }

    [Fact]
    public void Resolve_AbsolutePathOutsideWorkDir_ThrowsSandboxException()
    {
        var sandbox = new PathSandbox(_workDir);
        Assert.Throws<SandboxViolationException>(() => sandbox.Resolve("C:\\Windows\\System32\\cmd.exe"));
    }

    [Fact]
    public void Resolve_AbsolutePathInsideWorkDir_Succeeds()
    {
        var sandbox = new PathSandbox(_workDir);
        var target = Path.Combine(_workDir, "src", "bar.cs");
        var result = sandbox.Resolve(target);
        Assert.Equal(Path.GetFullPath(target), result);
    }

    [Fact]
    public void Resolve_DotPath_ResolvesToWorkDir()
    {
        var sandbox = new PathSandbox(_workDir);
        var result = sandbox.Resolve(".");
        Assert.Equal(Path.GetFullPath(_workDir), result);
    }

    [Fact]
    public void UpdateWorkDir_ChangesRoot()
    {
        var sandbox = new PathSandbox(_workDir);
        var newDir = Path.Combine(Path.GetTempPath(), "sandbox_test2");
        sandbox.UpdateWorkingDirectory(newDir);
        var result = sandbox.Resolve("foo.cs");
        Assert.StartsWith(Path.GetFullPath(newDir), result);
    }
}
