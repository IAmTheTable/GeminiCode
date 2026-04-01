// src/GeminiCode.Tests/ContextProcessorUploadTests.cs
namespace GeminiCode.Tests;

using GeminiCode.Agent;
using GeminiCode.Tools;

public class ContextProcessorUploadTests
{
    private ContextProcessor CreateProcessor(string workDir)
    {
        return new ContextProcessor(new PathSandbox(workDir));
    }

    [Fact]
    public void FileWithLSyntax_ParsesLineRange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.cs");
            File.WriteAllLines(testFile, Enumerable.Range(1, 500).Select(i => $"Line {i} content"));

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("check @file test.cs L200-L300");
            Assert.Equal(1, count);
            Assert.Contains("Line 200 content", expanded);
            Assert.Contains("Line 300 content", expanded);
            Assert.DoesNotContain("Line 100 content", expanded);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FileWithLSyntax_SingleLine()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.cs");
            File.WriteAllLines(testFile, Enumerable.Range(1, 500).Select(i => $"Line {i} content"));

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("check @file test.cs L200");
            Assert.Equal(1, count);
            Assert.Contains("Line 200 content", expanded);
            Assert.Contains("Line 500 content", expanded);
            Assert.DoesNotContain("Line 100 content", expanded);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FileWithoutLineRange_QueuedForUpload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(testFile, "hello world");

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("analyze @file test.txt");
            Assert.Equal(1, count);
            Assert.True(processor.PendingUploads.Count > 0);
            Assert.Contains(processor.PendingUploads, u => u.Contains("test.txt"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UploadTag_QueuedForUpload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ctx_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var testFile = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(testFile, "hello world");

            var processor = CreateProcessor(tempDir);
            var (expanded, count) = processor.Process("analyze @upload test.txt");
            Assert.Equal(1, count);
            Assert.True(processor.PendingUploads.Count > 0);
            Assert.Contains(processor.PendingUploads, u => u.Contains("test.txt"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
