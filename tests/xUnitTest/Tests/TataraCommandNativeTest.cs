// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json.Nodes;
using Kimi;
using Kimi.Command;
using Kimi.Compiler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace XunitTest;

// kimi tatara's native rows (Tatara design A3 §3). Explicit until unit A4: they need a private toolchain root holding tatara.dll, named
// by KIMI_TOOLCHAIN_ROOT in the test process's own environment (tests/scripts/run-tatara-corpus.ps1 prepares it). Single-shot runs each
// line in a unit of its own on the external tools; the session runs every line in one unit on Tatara. The fatal row leaves Tatara
// refusing every later call for the rest of its process, so it runs in a test process of its own.
[Collection<ConsoleIsolationCollection>]
public sealed class TataraCommandNativeTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-session-" + Guid.NewGuid().ToString("N"));

    public TataraCommandNativeTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    // In one process: build A, run B, run B --no-build, an edit that makes A fail, its fix and the rebuild. Each line's exit code and
    // products equal single-shot's on the external tools (the files equal, the records equal but for tools, the text equal with the
    // folder replaced), and the session's records name Tatara in tools.
    [Fact(Explicit = true)]
    public async Task EachLineEqualsSingleShot()
    {
        string[] lines = ["build A/A.kimiproj", "run B/B.kimiproj", "run B/B.kimiproj --no-build", "build A/A.kimiproj", "build A/A.kimiproj"];
        var single = Path.Combine(this.directory, "single");
        var session = Path.Combine(this.directory, "session");
        var codes = new List<int>();
        var singleText = new StringBuilder();
        Write(single);
        for (var i = 0; i < lines.Length; i++)
        {
            Edit(single, i);
            var console = new TataraCommandTest.SessionConsole();
            var product = TataraCommandTest.Unit(console, null);
            codes.Add(await InFolder(single, async () =>
            {
                await product.Execute(lines[i], product.Context.ServiceProvider, TestContext.Current.CancellationToken);
                return Environment.ExitCode;
            }));
            Environment.ExitCode = 0;
            singleText.Append(console.Text);
            if (codes[^1] != 0)
            {
                singleText.Append($"Exit code: {codes[^1]}\n");
            }
        }

        Assert.Equal([0, 0, 0, 1, 0], codes);
        Write(session);
        var (code, text) = await Session(session, lines.Select<string, (string Line, Action? Before)>((x, i) => (x, () => Edit(session, i))).ToArray());
        Assert.Equal(1, code);
        Assert.Equal(singleText.ToString().Replace(single, "<W>", StringComparison.Ordinal), text.Replace(session, "<W>", StringComparison.Ordinal));
        Assert.Equal(Products(single), Products(session));
        foreach (var record in Directory.GetFiles(session, "*.link.build.json", SearchOption.AllDirectories))
        {
            Assert.NotNull(JsonNode.Parse(File.ReadAllText(record))!["tools"]!["tatara"]);
        }
    }

    // A press during a line cancels it (130, a failed line) and the next line builds.
    [Fact(Explicit = true)]
    public async Task ALineAfterACancelledLineSucceeds()
    {
        Write(this.directory);
        var pressed = 0;
        var (code, text) = await Session(this.directory, [("build A/A.kimiproj", null), ("build A/A.kimiproj", null)], () =>
        {
            if (pressed++ == 0)
            {
                Program.CancelKey();
            }
        });
        Assert.Equal(1, code);
        Assert.Contains("Command cancelled.", text, StringComparison.Ordinal);
        Assert.Contains("Exit code: 130", text, StringComparison.Ordinal);
        Assert.Equal(1, TataraCommandTest.Count(text, "Built: "));
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(Path.Combine(this.directory, "A", "bin", WindowsProfile.Target, "A.link.build.json")))!["tools"]!["tatara"]);
    }

    private static void Write(string root)
    {
        foreach (var (name, source) in new[] { ("A", "Hello.kimi"), ("B", "Hello.kimi") })
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(Repository.Root, "docs", "examples", "Hello", source), Path.Combine(folder, name + ".kimi"));
            File.WriteAllText(Path.Combine(folder, name + ".kimiproj"), $"Targets={{\"{WindowsProfile.Target}\"}} OutputKind=\"Application\" Optimization=\"O0\"");
        }
    }

    // Before the fourth line A fails (an unresolved name); before the fifth it is fixed.
    private static void Edit(string root, int line)
    {
        var path = Path.Combine(root, "A", "A.kimi");
        if (line == 3)
        {
            File.WriteAllText(path, "undefinedCall()\n");
        }
        else if (line == 4)
        {
            File.Copy(Path.Combine(Repository.Root, "docs", "examples", "Hello", "Hello.kimi"), path, true);
        }
    }

    private static async Task<int> InFolder(string folder, Func<Task<int>> action)
    {
        var current = Environment.CurrentDirectory;
        Environment.CurrentDirectory = folder;
        try
        {
            Environment.ExitCode = 0;
            return await action();
        }
        finally
        {
            Environment.CurrentDirectory = current;
        }
    }

    // A session in the folder over the lines, each line's action run when the reader reaches it (after the previous line ran).
    private static async Task<(int Code, string Text)> Session(string folder, (string Line, Action? Before)[] lines, Action? onProject = null)
    {
        var console = new TataraCommandTest.SessionConsole { OnProject = onProject };
        var product = TataraCommandTest.Unit(console, null);
        var command = product.Context.ServiceProvider.GetRequiredService<TataraCommand>();
        var code = await InFolder(folder, () => command.Run(new LineStream(lines), TestContext.Current.CancellationToken));
        return (code, console.Text);
    }

    // The products under a folder: every file by its relative path and text or hash, a record without its tools (the folder replaced).
    private static string Products(string root)
        => string.Join('\n', Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(x => !x.EndsWith(".kimi", StringComparison.Ordinal)).Order(StringComparer.Ordinal).Select(x =>
        {
            var name = Path.GetRelativePath(root, x);
            if (!name.EndsWith(".build.json", StringComparison.Ordinal))
            {
                return name + " " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(x)));
            }

            var record = JsonNode.Parse(File.ReadAllText(x))!.AsObject();
            record.Remove("tools");

            return name + " " + record.ToJsonString().Replace(root.Replace("\\", "\\\\", StringComparison.Ordinal), "<W>", StringComparison.Ordinal);
        }));

    // The fatal row, alone in its own test process (run-tatara-corpus.ps1 runs it by its method): a line whose link hits LLD's fatal()
    // (a malformed static library) ends the session with 1 and its one sentence; the next line does not run.
    public sealed class Fatal
    {
        [Fact(Explicit = true)]
        public async Task AFatalErrorEndsTheSession()
        {
            var directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-session-fatal-" + Guid.NewGuid().ToString("N"));
            Write(directory);
            var folder = Path.Combine(directory, "F");
            Directory.CreateDirectory(Path.Combine(folder, "native"));
            File.WriteAllText(Path.Combine(folder, "native", "malformed.lib"), "!<arch>\nnot a library member\n");
            File.WriteAllText(Path.Combine(folder, "F.kimi"), "group Native\n    #LibraryImport(\"codec\", \"f\")\n    public unsafe func f() -> i32\npublic func main()\n    var x: i32 = 0\n    unsafe => x = Native.f()\n");
            File.WriteAllText(Path.Combine(folder, "F.kimiproj"), $"Targets={{\"{WindowsProfile.Target}\"}} OutputKind=\"Application\" Optimization=\"O0\"\nNativeLibraries=\n  \"{WindowsProfile.Target}\"=\n    codec={{ Kind=\"static\" Input=\"native/malformed.lib\" }}");
            var (code, text) = await Session(directory, [("build F/F.kimiproj", null), ("build A/A.kimiproj", null)]);
            Assert.True(code == 1, text);
            Assert.Contains("Tatara cannot run again in this process (", text, StringComparison.Ordinal);
            Assert.Contains("truncated or malformed archive", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Project: A", text, StringComparison.Ordinal);
            Directory.Delete(directory, true);
        }
    }

    // The session's input: each line's bytes and a line feed; a line's action runs when its first byte is read.
    private sealed class LineStream((string Line, Action? Before)[] lines) : Stream
    {
        private int line = -1;
        private byte[] current = [];
        private int position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.position == this.current.Length)
            {
                if (this.line + 1 == lines.Length)
                {
                    return 0;
                }

                this.line++;
                lines[this.line].Before?.Invoke();
                this.current = Encoding.UTF8.GetBytes(lines[this.line].Line + "\n");
                this.position = 0;
            }

            buffer[offset] = this.current[this.position++];
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
