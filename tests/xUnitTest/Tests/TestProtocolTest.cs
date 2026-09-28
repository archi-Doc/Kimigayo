// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers.Binary;
using System.Text;
using Kimi.Testing;
using Xunit;

namespace XunitTest;

public class TestProtocolTest
{
    [Theory]
    [InlineData("artifact")]
    [InlineData("case")]
    [InlineData("version")]
    [InlineData("size")]
    [InlineData("site")]
    [InlineData("missingLatch")]
    [InlineData("duplicateCompletion")]
    [InlineData("messageWithoutFailure")]
    public async Task RejectsMalformedAndStaleRecords(string error)
    {
        var outcome = Create();
        using var input = new MemoryStream();
        Frame(input, 0, payload: error == "artifact" ? new string('0', 67) : outcome.Catalog.ArtifactId);
        Frame(input, 1, payload: error == "case" ? new string('0', 67) : outcome.Test.CaseId);
        switch (error)
        {
            case "version":
                Frame(input, 6, version: 2);
                break;
            case "size":
                input.Write([1, 0, 1, 0]);
                break;
            case "site":
                Frame(input, 2, 1, 99, 1);
                break;
            case "missingLatch":
                Frame(input, 6, count: 1);
                break;
            case "duplicateCompletion":
                Frame(input, 6);
                Frame(input, 6);
                break;
            case "messageWithoutFailure":
                Frame(input, 3, 1, 0, 0, "missing");
                break;
        }

        input.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => TestWorker.Receive(input, outcome, new(), new(TestOptions.Parse([])), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KeepsFailureStateWithZeroRetentionAndOmittedDetails()
    {
        var outcome = Create();
        using var input = new MemoryStream();
        Frame(input, 0, payload: outcome.Catalog.ArtifactId);
        Frame(input, 1, payload: outcome.Test.CaseId);
        Frame(input, 2, 1, 0, 1);
        Frame(input, 6, count: 1000);
        input.Position = 0;
        await TestWorker.Receive(input, outcome, new() { DiagnosticCount = 0, DiagnosticBytes = 0 }, new(TestOptions.Parse([])), TestContext.Current.CancellationToken);
        Assert.True(outcome.Completed);
        Assert.Equal(1000UL, outcome.FailureCount);
        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public async Task CorrelatesOuterMessageAfterNestedFailure()
    {
        var outcome = Create();
        using var input = new MemoryStream();
        Frame(input, 0, payload: outcome.Catalog.ArtifactId);
        Frame(input, 1, payload: outcome.Test.CaseId);
        Frame(input, 2, 1, 0, 1);
        Frame(input, 2, 2, 0, 2);
        Frame(input, 3, 2, 0, 2, "inner");
        Frame(input, 3, 1, 0, 2, "outer");
        Frame(input, 4, 1, 0, 2);
        input.Position = 0;
        await TestWorker.Receive(input, outcome, new(), new(TestOptions.Parse([])), TestContext.Current.CancellationToken);
        Assert.False(outcome.Completed);
        Assert.Equal("requireAbort", outcome.Termination);
        Assert.Equal("outer", outcome.Failures[0].Message);
        Assert.Equal("inner", outcome.Failures[1].Message);
    }

    [Fact]
    public async Task TruncatedFrameCannotCompleteACase()
    {
        var outcome = Create();
        using var input = new MemoryStream([32, 0, 0]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => TestWorker.Receive(input, outcome, new(), new(TestOptions.Parse([])), TestContext.Current.CancellationToken));
        Assert.False(outcome.Completed);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--jobs", "0")]
    [InlineData("--jobs", "1", "--no-parallel")]
    [InlineData("--case", "id", "--filter", "name")]
    [InlineData("--timeout", "0s")]
    [InlineData("--timeout", "1441m")]
    [InlineData("--case-log-bytes", "-1")]
    [InlineData("--run-diagnostic-bytes", "2147483648")]
    [InlineData("--format", "json", "--format", "text")]
    public void RejectsInvalidOptions(params string[] args)
        => Assert.Throws<InvalidDataException>(() => TestOptions.Parse(args));

    [Theory]
    [InlineData("Test = { Unknown = 1 }")]
    [InlineData("Test = { Timeout = \"1s\", Timeout = \"2s\" }")]
    [InlineData("Test = {}, Test = {}")]
    [InlineData("Test = { Environment = { A = \"1\", A = \"2\" } }")]
    [InlineData("Test = null")]
    public void RejectsAmbiguousProjectSettings(string text)
        => Assert.ThrowsAny<Exception>(() => Kimi.ProjectFile.Load(Encoding.UTF8.GetBytes(text)));

    private static TestOutcome Create()
    {
        var compilation = TestExecutionAnalysisTest.Analyze("#Test\nfunc sample() => $expect(false, message: \"message\")");
        Assert.True(compilation.Ownership.Result.IsVerified);
        compilation.Tests.Discover(compilation);
        return new(compilation.Tests.Cases[0], compilation.Tests);
    }

    private static void Frame(Stream stream, ushort kind, ulong issue = 0, int site = -1, ulong count = 0, string payload = "", ushort version = 1)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        Span<byte> header = stackalloc byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(header, 32 + bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], kind);
        BinaryPrimitives.WriteUInt64LittleEndian(header[8..], issue);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], site);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteUInt64LittleEndian(header[24..], count);
        stream.Write(header);
        stream.Write(bytes);
    }
}
