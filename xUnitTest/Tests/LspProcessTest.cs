// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 23.4.1 at the process boundary: `kimi lsp` writes only protocol frames to standard output and owns the exit code.
public sealed class LspProcessTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-lsp-process-" + Guid.NewGuid().ToString("N"));

    public LspProcessTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public async Task ServerProcessPublishesDiagnosticsAndExitsCleanly()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var token = timeout.Token;
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Kimi.exe" : "Kimi"), "lsp")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = this.directory,
        };
        using var process = Process.Start(start)!;
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(token);
            var output = new MemoryStream();
            var pipe = new Pipe();
            var copy = Tee(process.StandardOutput.BaseStream, output, pipe.Writer, token);
            var reader = new LspFrameReader(pipe.Reader.AsStream());
            var input = process.StandardInput.BaseStream;

            var path = Path.Combine(this.directory, "Hello.kimi");
            await Send(input, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"capabilities\":{},\"initializationOptions\":{\"checkQuietPeriodMs\":0}}}", token);
            await Receive(reader, static x => x.TryGetProperty("id", out _), token);
            await Send(input, "{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}", token);
            await Send(input, $"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{{\"textDocument\":{{\"uri\":\"{LspTestClient.Uri(path)}\",\"languageId\":\"kimi\",\"version\":1,\"text\":\"::Kimi.Console.writeLine(1 +\"}}}}}}", token);
            var publish = await Receive(reader, x => LspTestClient.IsPublish(x, LspTestClient.Uri(path)), token);
            Assert.NotEqual(0, publish.GetProperty("params").GetProperty("diagnostics").GetArrayLength());

            await Send(input, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"shutdown\"}", token);
            var response = await Receive(reader, static x => x.TryGetProperty("id", out var id) && id.GetInt32() == 2, token);
            Assert.Equal(JsonValueKind.Null, response.GetProperty("result").ValueKind);
            await Send(input, "{\"jsonrpc\":\"2.0\",\"method\":\"exit\"}", token);
            await process.WaitForExitAsync(token);
            await copy;
            await stderr;

            Assert.Equal(0, process.ExitCode);
            AssertOnlyFrames(output.ToArray());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
    }

    private static async Task Tee(Stream source, MemoryStream copy, PipeWriter writer, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copy.Write(buffer, 0, read);
            await writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await writer.CompleteAsync();
    }

    private static async Task Send(Stream input, string json, CancellationToken cancellationToken)
    {
        await input.WriteAsync(LspTestClient.Frame(json), cancellationToken);
        await input.FlushAsync(cancellationToken);
    }

    private static async Task<JsonElement> Receive(LspFrameReader reader, Func<JsonElement, bool> predicate, CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await reader.ReadAsync(cancellationToken) ?? throw new EndOfStreamException("The server output ended.");
            try
            {
                using var document = JsonDocument.Parse(frame.Buffer.AsMemory(0, frame.Length));
                if (predicate(document.RootElement))
                {
                    return document.RootElement.Clone();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frame.Buffer);
            }
        }
    }

    // Standard output must be exactly a sequence of "Content-Length: n\r\n\r\n" headers, each followed by n bytes of JSON.
    private static void AssertOnlyFrames(ReadOnlySpan<byte> output)
    {
        var header = "Content-Length: "u8;
        var frames = 0;
        while (!output.IsEmpty)
        {
            Assert.True(output.StartsWith(header), "Unexpected output: " + Encoding.UTF8.GetString(output[..Math.Min(80, output.Length)]));
            output = output[header.Length..];
            Assert.True(Utf8Parser.TryParse(output, out int length, out var consumed));
            output = output[consumed..];
            Assert.True(output.StartsWith("\r\n\r\n"u8));
            output = output[4..];
            using var document = JsonDocument.Parse(output[..length].ToArray());
            output = output[length..];
            frames++;
        }

        Assert.True(frames >= 3);
    }
}
