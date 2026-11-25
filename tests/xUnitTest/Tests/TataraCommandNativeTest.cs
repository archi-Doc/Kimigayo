// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json.Nodes;
using Kimi;
using Kimi.Build;
using Kimi.Command;
using Kimi.Compiler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace XunitTest;

// kimi tatara's native rows (Tatara design A3 §3), on the compiler's Tatara (one instance per process, Tatara B6b1); each skips off
// Windows, and on Windows a missing tatara.dll fails it. Single-shot runs each line in a unit of its own; the session runs every line in one unit. The fatal
// row leaves Tatara refusing every later call for the rest of its process, so it runs in a child test process of its own (Fatal).
[Collection<ConsoleIsolationCollection>]
public sealed class TataraCommandNativeTest : IDisposable
{
    private const string Windows = "kimi tatara's native builds run on Windows.";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-session-" + Guid.NewGuid().ToString("N"));

    public TataraCommandNativeTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => TestFolder.Delete(this.directory);

    // In one process: build A, run B, run B --no-build, an edit that makes A fail, its fix and the rebuild. Each line's exit code and
    // products equal single-shot's (the files equal, the records equal but for tools, the text equal with the folder replaced), and the
    // session's records name Tatara in tools.
    [Fact]
    public async Task EachLineEqualsSingleShot()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
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
            var product = TataraCommandTest.Unit(console);
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

    // The Tatara plan's B7b8 (design row 20): one session builds Hello for Windows, for Linux and for Windows again; the last Windows
    // executable equals one built alone, and the Linux one is a static ELF (libc-free, entry __kimi_start). Fail-before: the Linux line
    // fails with "Artifact production currently requires exactly one configured Windows x64 target.".
    [Fact]
    public async Task ASessionBuildsEachTargetInTurn()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (windows, linux) = ("build A/A.kimiproj --Target " + TargetProfile.Windows.Target, "build A/A.kimiproj --Target " + TargetProfile.Linux.Target);
        var (alone, session) = (Path.Combine(this.directory, "alone"), Path.Combine(this.directory, "session"));
        foreach (var root in new[] { alone, session })
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "A")).FullName;
            File.Copy(Path.Combine(Repository.Root, "docs", "examples", "Hello", "Hello.kimi"), Path.Combine(folder, "A.kimi"));
            File.WriteAllText(Path.Combine(folder, "A.kimiproj"), $"Targets={{\"{TargetProfile.Windows.Target}\", \"{TargetProfile.Linux.Target}\"}} OutputKind=\"Application\" Optimization=\"O0\"");
        }

        var (code, text) = await Session(alone, [(windows, null)]);
        Assert.True(code == 0, text);
        (code, text) = await Session(session, [(windows, null), (linux, null), (windows, null)]);
        Assert.True(code == 0, text);
        Assert.Equal(3, TataraCommandTest.Count(text, "Built: "));
        var executable = Path.Combine("A", "bin", TargetProfile.Windows.Target, "A.O0.exe");
        Assert.Equal(File.ReadAllBytes(Path.Combine(alone, executable)), File.ReadAllBytes(Path.Combine(session, executable)));
        ElfFile.Read(Path.Combine(session, "A", "bin", TargetProfile.Linux.Target, "A.O0")).AssertStatic(TargetProfile.Linux.EntrySymbol);
    }

    // A press during a line cancels it (130, a failed line) and the next line builds.
    [Fact]
    public async Task ALineAfterACancelledLineSucceeds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
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
        Assert.NotNull(JsonNode.Parse(File.ReadAllText(Path.Combine(this.directory, "A", "bin", TargetProfile.Windows.Target, "A.link.build.json")))!["tools"]!["tatara"]);
    }

    // A malformed program library fails its line, in the dependency step's read (Tatara's inspect returns 1, LinkAmbiguity design §5.1),
    // and Tatara runs again: the session's next line builds. Fail-before (this row alone in its process): LLD's fatal() ended the session
    // and A was not built.
    [Fact]
    public async Task AMalformedLibraryFailsOnlyItsLine()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        Write(this.directory);
        WriteF(this.directory, "malformed.lib", "!<arch>\nnot a library member\n"u8.ToArray());
        var (code, text) = await Session(this.directory, [("build F/F.kimiproj", null), ("build A/A.kimiproj", null)]);
        Assert.True(code == 1, text);
        Assert.Contains("Tatara failed: inspect", text, StringComparison.Ordinal);
        Assert.Contains("truncated or malformed archive", text, StringComparison.Ordinal);
        Assert.Contains("Exit code: 1", text, StringComparison.Ordinal);
        Assert.Equal(1, TataraCommandTest.Count(text, "Built: "));
        Assert.DoesNotContain("Tatara cannot run again", text, StringComparison.Ordinal);
    }

    // The fatal row in a child test process of its own, its folder this row's (Fatal); the child loads the tatara.dll beside its
    // Kimi.dll, as this process does.
    [Fact]
    public async Task AFatalErrorEndsTheSessionOfItsProcess()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        await FreshProcess.RunExplicit(typeof(Fatal), nameof(Fatal.AFatalErrorEndsTheSession), (Fatal.Folder, this.directory));
    }

    private static void Write(string root)
    {
        foreach (var (name, source) in new[] { ("A", "Hello.kimi"), ("B", "Hello.kimi") })
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(Repository.Root, "docs", "examples", "Hello", source), Path.Combine(folder, name + ".kimi"));
            File.WriteAllText(Path.Combine(folder, name + ".kimiproj"), $"Targets={{\"{TargetProfile.Windows.Target}\"}} OutputKind=\"Application\" Optimization=\"O0\"");
        }
    }

    // F: a program calling its import f of codec, a static library whose input is native/<library> with these bytes.
    private static void WriteF(string root, string library, byte[] bytes)
    {
        var folder = Path.Combine(root, "F");
        Directory.CreateDirectory(Path.Combine(folder, "native"));
        File.WriteAllBytes(Path.Combine(folder, "native", library), bytes);
        File.WriteAllText(Path.Combine(folder, "F.kimi"), "group Native\n    #LibraryImport(\"codec\", \"f\")\n    public unsafe func f() -> i32\npublic func main()\n    var x: i32 = 0\n    unsafe => x = Native.f()\n");
        File.WriteAllText(Path.Combine(folder, "F.kimiproj"), $"Targets={{\"{TargetProfile.Windows.Target}\"}} OutputKind=\"Application\" Optimization=\"O0\"\nNativeLibraries=\n  \"{TargetProfile.Windows.Target}\"=\n    codec={{ Kind=\"static\" Input=\"native/{library}\" }}");
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
        var product = TataraCommandTest.Unit(console);
        var command = product.Context.ServiceProvider.GetRequiredService<TataraCommand>();
        var code = await InFolder(folder, () => command.Run(new TataraCommandTest.LineStream(lines), TestContext.Current.CancellationToken));
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

    // The fatal row, run by AFatalErrorEndsTheSessionOfItsProcess in a child test process of its own: a line whose link hits LLD's
    // fatal() ends the session with 1 and its one sentence; the next line does not run. Its static library is an object the dependency
    // step reads (a whole file defining f) and LLD fatals on: its .drectve's /merge:bad (LinkAmbiguity design §5.1 (a), P-F1), emitted
    // here by Tatara from IR with the Windows row's values, no tool (a malformed archive now fails its line in the read:
    // AMalformedLibraryFailsOnlyItsLine). Its folder is its parent's (named by Folder, removed by the parent).
    public sealed class Fatal
    {
        internal const string Folder = "KIMI_TATARA_SESSION_FATAL_FOLDER";

        [Fact(Explicit = true)]
        public async Task AFatalErrorEndsTheSession()
        {
            var directory = Environment.GetEnvironmentVariable(Folder)!;
            Write(directory);
            var ir = Path.Combine(directory, "fatal.ll");
            File.WriteAllText(ir, $"target triple = \"{TargetProfile.Windows.Target}\"\n\ndefine i32 @f() {{\n  ret i32 0\n}}\n\n!llvm.linker.options = !{{!0}}\n!0 = !{{!\"/merge:bad\"}}\n");
            var row = TargetProfile.Windows;
            var context = new TataraContext((_, _) => { }, TestContext.Current.CancellationToken, row.Target, row.Cpu, row.Features, row.RelocationModel, row.CodeModel);
            WriteF(directory, "fatal.lib", Tatara.Instance.Emit(context, ir, Path.Combine(directory, "fatal.obj"), 0));
            var (code, text) = await Session(directory, [("build F/F.kimiproj", null), ("build A/A.kimiproj", null)]);
            Assert.True(code == 1, text);
            Assert.Contains("Tatara cannot run again in this process (", text, StringComparison.Ordinal);
            Assert.Contains("/merge: invalid argument", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Project: A", text, StringComparison.Ordinal);
        }
    }
}
