namespace GeminiCode.Tools;

public class SandboxViolationException : Exception
{
    public SandboxViolationException(string path, string workDir)
        : base($"Access denied: path '{path}' is outside the working directory '{workDir}'.") { }
}

public class PathSandbox
{
    private string _workingDirectory;

    public string WorkingDirectory => _workingDirectory;

    public PathSandbox(string workingDirectory)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
    }

    public string Resolve(string path)
    {
        string absolute;
        if (Path.IsPathRooted(path))
            absolute = Path.GetFullPath(path);
        else
            absolute = Path.GetFullPath(Path.Combine(_workingDirectory, path));

        var normalizedAbsolute = NormalizePath(absolute);
        var normalizedWorkDir = NormalizePath(_workingDirectory);

        if (!normalizedAbsolute.StartsWith(normalizedWorkDir, StringComparison.OrdinalIgnoreCase))
            throw new SandboxViolationException(path, _workingDirectory);

        return absolute;
    }

    public void UpdateWorkingDirectory(string newPath)
    {
        _workingDirectory = Path.GetFullPath(newPath);
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.EndsWith(Path.DirectorySeparatorChar))
            full += Path.DirectorySeparatorChar;
        return full;
    }
}
