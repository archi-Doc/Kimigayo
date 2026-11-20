// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Kimi;
using Kimi.Build;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// Tatara's executor (Tatara design A2 §4.5). Every test that loads tatara.dll is Explicit until unit A4: it needs a private toolchain
// root holding tatara.dll, named by KIMI_TOOLCHAIN_ROOT in the test process's own environment. KIMI_TATARA_CORPUS names a corpus
// directory that replaces the fixed rows: its pairs/<row>/ (a generated project) go through the executor comparison, its
// fixtures/*.ll (bare IR) through the tool control comparison at O0 and O2; each row's outcome is appended to <corpus>/results.tsv.
// tests/scripts/run-tatara-corpus.ps1 prepares the root and the corpus and runs this class. A fatal row (Fatal) leaves Tatara
// refusing every later call for the rest of its process, so each fatal row runs in a test process of its own, never beside another
// row of either class.
public sealed class TataraExecutorTest : IDisposable
{
    private const string Triple = WindowsProfile.Target;

    private static readonly string[] Levels = ["O0", "O2"];

    private static readonly string[] ToolNames = ["opt", "llc", "lld-link", "llvm-nm", "llvm-readobj"];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-" + Guid.NewGuid().ToString("N"));

    public TataraExecutorTest() => Directory.CreateDirectory(this.directory);

    private delegate IntPtr SetFilter(IntPtr filter);

    private delegate int ModuleFromAddress(uint flags, IntPtr address, out IntPtr module);

    private static string Root => Environment.GetEnvironmentVariable("KIMI_TOOLCHAIN_ROOT") ?? throw new InvalidOperationException("KIMI_TOOLCHAIN_ROOT is not set.");

    private static string? Corpus => Environment.GetEnvironmentVariable("KIMI_TATARA_CORPUS");

    private static string Hello => Path.Combine(Repository.Root, "docs", "examples", "Hello", "Hello.kimi");

    private static string Kernel32 => Path.Combine(Root, "windows_x64", "kernel32.lib");

    private static string Backend => Path.Combine(Root, "windows_x64", ToolchainIdentity.BackendFile);

    public void Dispose() => Directory.Delete(this.directory, true);

    // The start check's comparison (no DLL): the expected ABI is the SHA-256 of src/Tatara/tatara.h, and a DLL of another ABI or LLVM
    // version is refused with both values and the remedy.
    [Fact]
    public void StartCheckComparesTheAbiAndTheLlvmVersion()
    {
        var header = Path.Combine(Repository.Root, "src", "Tatara", "tatara.h");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(header))), Tatara.ExpectedAbi);
        Tatara.CheckStart("tatara.dll", Tatara.ExpectedAbi, ToolchainIdentity.LlvmVersion);
        var abi = Assert.Throws<InvalidDataException>(() => Tatara.CheckStart("tatara.dll", "0123", ToolchainIdentity.LlvmVersion));
        Assert.Contains("0123", abi.Message, StringComparison.Ordinal);
        Assert.Contains(Tatara.ExpectedAbi, abi.Message, StringComparison.Ordinal);
        Assert.Contains(NativeToolchain.SetupCommand + " -ToolchainRoot <a new directory>", abi.Message, StringComparison.Ordinal);
        var version = Assert.Throws<InvalidDataException>(() => Tatara.CheckStart("tatara.dll", Tatara.ExpectedAbi, "1.0.0"));
        Assert.Contains("1.0.0", version.Message, StringComparison.Ordinal);
        Assert.Contains(ToolchainIdentity.LlvmVersion, version.Message, StringComparison.Ordinal);
        Assert.True(NativeToolchain.IsToolchainFailure(abi));
    }

    // The executor comparison (deleted in A4): the same pair through NativeBuild with each executor into two folders; equal files,
    // records equal but for tools, equal reports with the folder replaced.
    [Fact(Explicit = true)]
    public async Task ExecutorsBuildEqualProducts()
    {
        var rows = Corpus is { } corpus ? Directory.GetDirectories(Path.Combine(corpus, "pairs")) : await this.FixedRows();
        await Run(rows, CompareExecutors);
    }

    // The tool control comparison (permanent): the archive's opt, llc and lld-link with P13's arguments against Tatara's operations,
    // on the IR of the fixed rows or every corpus fixture, at O0 and O2.
    [Fact(Explicit = true)]
    public async Task TataraEqualsTheTools()
    {
        var rows = Corpus is { } corpus
            ? Directory.GetFiles(Path.Combine(corpus, "fixtures"), "*.ll").SelectMany(ir => Levels.Select(level => ir + "|" + level)).ToArray()
            : (await this.FixedRows()).Select(row => Manifest(row)).Select(m => Path.ChangeExtension(Path.ChangeExtension(m, null), ".ll") + "|" + Level(m))
                .Concat(Levels.Select(level => this.DebugIr() + "|" + level)).ToArray();
        await Run(rows, row => this.CompareWithTools(row));
    }

    // C1's deep input: a global of 20,000 nested array types, deeper than dotnet's 1.5 MiB reserve allows and within the tools' 10 MB;
    // Tatara's optimize and emit return 0 with the tools' products (the link fails alike on both sides: no entry).
    [Fact(Explicit = true)]
    public async Task DeepInputEqualsTheTools()
    {
        var deep = Path.Combine(this.directory, "deep.ll");
        File.WriteAllText(deep, "target triple = \"x86_64-pc-windows-msvc\"\n\n@g = global " + string.Concat(Enumerable.Repeat("[1 x ", 20000)) + "i32" + new string(']', 20000) + " zeroinitializer\n");
        foreach (var level in Levels)
        {
            var outcome = await this.CompareWithTools(deep + "|" + level);
            Assert.True(outcome is "equal" or "equal failure\tlink", level + ": " + outcome);
        }
    }

    // Twenty builds in one process alternating two programs give equal bytes each time.
    [Fact(Explicit = true)]
    public async Task RepeatedBuildsAreEqual()
    {
        string[] rows = [await this.Generate("hello", Hello, "O2"), await this.Generate("m1", Milestone(1), "O2")];
        var first = new string?[2];
        for (var i = 0; i < 20; i++)
        {
            var manifest = Manifest(rows[i % 2]);
            await NativeToolchain.BuildManifest(manifest, null, null, NativeToolchain.TataraExecutor.Create, (_, _) => { }, TestContext.Current.CancellationToken);
            var products = Hash(Path.ChangeExtension(manifest, null) + "." + Level(manifest) + ".obj") + Hash(Path.ChangeExtension(manifest, null) + "." + Level(manifest) + ".exe");
            first[i % 2] ??= products;
            Assert.Equal(first[i % 2], products);
        }
    }

    // A request that fails with return 1 is the toolchain's failure, and the next request succeeds: an invalid IR; an inline-asm
    // constraint error, which llc reports after opening the object and which leaves no object, as llc's; then a link with an
    // undefined symbol.
    [Fact(Explicit = true)]
    public async Task FailedRequestsLeaveTataraUsable()
    {
        var row = await this.Generate("hello", Hello, "O0");
        var ir = Path.ChangeExtension(Path.ChangeExtension(Manifest(row), null), ".ll");
        var executor = Executor(Path.GetDirectoryName(ir)!);
        var invalid = Path.Combine(this.directory, "invalid.ll");
        File.WriteAllText(invalid, "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @f( {\n");
        var error = await Assert.ThrowsAsync<IOException>(() => executor.Emit(invalid, Path.Combine(this.directory, "invalid.obj"), "O0"));
        Assert.StartsWith("Tatara failed: emit\n", error.Message, StringComparison.Ordinal);
        Assert.True(NativeToolchain.IsToolchainFailure(error));
        var asm = Path.Combine(this.directory, "asmerror.ll");
        File.WriteAllText(asm, "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @f() {\nentry:\n  %r = call i32 asm sideeffect \"mov $0, 1\", \"={notaregister}\"()\n  ret i32 %r\n}\n");
        var asmError = await Assert.ThrowsAsync<IOException>(() => executor.Emit(asm, Path.Combine(this.directory, "asmerror.obj"), "O0"));
        Assert.StartsWith("Tatara failed: emit\n", asmError.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(this.directory, "asmerror.obj")));
        var obj = Path.Combine(this.directory, "hello.obj");
        await executor.Emit(ir, obj, "O0");
        var link = await Assert.ThrowsAsync<IOException>(() => executor.Link([obj, Kernel32, "/entry:__kimi_start", "/subsystem:console", "/nodefaultlib", "/out:" + Path.Combine(this.directory, "a.exe")]));
        Assert.StartsWith("Tatara failed: link\n", link.Message, StringComparison.Ordinal);
        Assert.Contains("undefined symbol", link.Message, StringComparison.Ordinal);
        await executor.Link(LinkArguments(obj, Path.Combine(this.directory, "b.exe")));
        Assert.True(File.Exists(Path.Combine(this.directory, "b.exe")));
    }

    // Tatara takes no signal or exception-filter state (C-424 R1): after an optimize, an emit, a failed emit, an inspect and a link,
    // the process's unhandled-exception filter is still in the module that owned it before (the host's, never tatara.dll). LLVM's
    // Windows signal registration sets this filter and its console handler together, so an unchanged filter also means no console
    // handler. The filter is read by setting it and restoring it at once.
    [Fact(Explicit = true)]
    public async Task UnhandledExceptionFilterStaysTheHosts()
    {
        var row = await this.Generate("hello", Hello, "O0");
        var ir = Path.ChangeExtension(Path.ChangeExtension(Manifest(row), null), ".ll");
        var executor = Executor(Path.GetDirectoryName(ir)!);
        var before = FilterOwner();
        await executor.Optimize(ir, Path.Combine(this.directory, "hello.O2.ll"));
        var obj = Path.Combine(this.directory, "hello.obj");
        await executor.Emit(ir, obj, "O0");
        var asm = Path.Combine(this.directory, "asmerror.ll");
        File.WriteAllText(asm, "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @f() {\nentry:\n  %r = call i32 asm sideeffect \"mov $0, 1\", \"={notaregister}\"()\n  ret i32 %r\n}\n");
        await Assert.ThrowsAsync<IOException>(() => executor.Emit(asm, Path.Combine(this.directory, "asmerror.obj"), "O0"));
        await executor.Inspect(obj);
        await executor.Link(LinkArguments(obj, Path.Combine(this.directory, "hello.exe")));
        var tatara = NativeLibrary.Load(Path.Combine(Root, "tatara.dll"));
        NativeLibrary.Free(tatara);
        Assert.Equal(before, FilterOwner());
        Assert.NotEqual(tatara, before);
    }

    // The module that owns the process's unhandled-exception filter (IntPtr.Zero: none), through kernel32's exports.
    private static IntPtr FilterOwner()
    {
        var kernel32 = NativeLibrary.Load("kernel32.dll");
        var set = Marshal.GetDelegateForFunctionPointer<SetFilter>(NativeLibrary.GetExport(kernel32, "SetUnhandledExceptionFilter"));
        var owner = Marshal.GetDelegateForFunctionPointer<ModuleFromAddress>(NativeLibrary.GetExport(kernel32, "GetModuleHandleExW"));
        var filter = set(IntPtr.Zero);
        set(filter);
        NativeLibrary.Free(kernel32);

        // GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT
        return filter == IntPtr.Zero || owner(6, filter, out var module) == 0 ? filter : module;
    }

    private static string Milestone(int number) => Path.Combine(Repository.Root, "tests", "milestones", $"Milestone{number}.kimi");

    private static string Manifest(string row) => Directory.EnumerateFiles(row, "*.link.json", SearchOption.AllDirectories).Single();

    // The pair's optimization, as its file names carry it (Name.link.json beside Name.ll; the level from the manifest's text).
    private static string Level(string manifest) => File.ReadAllText(manifest).Contains("\"optimization\": \"O0\"", StringComparison.Ordinal) ? "O0" : "O2";

    private static string Hash(string path) => File.Exists(path) ? ArtifactFiles.Hash(path) : "(none)";

    private static string[] LinkArguments(string obj, string exe)
        => [obj, Kernel32, Backend, "/entry:" + WindowsProfile.EntrySymbol, "/subsystem:" + WindowsProfile.Subsystem, "/nodefaultlib", "/Brepro", "/out:" + exe];

    // Runs every row (in parallel for a corpus: the tools run concurrently, Tatara's calls are serialized by its lock), appends each
    // row's outcome to the corpus's results.tsv and fails with every row that differs.
    private static async Task Run(string[] rows, Func<string, Task<string>> compare)
    {
        var differences = new ConcurrentBag<string>();
        var results = Corpus is { } corpus ? Path.Combine(corpus, "results.tsv") : null;
        var gate = new object();
        await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = results is null ? 1 : 6, CancellationToken = TestContext.Current.CancellationToken }, async (row, _) =>
        {
            string outcome;
            try
            {
                outcome = await compare(row);
            }
            catch (Exception ex)
            {
                outcome = "error\t" + ex.GetType().Name + ": " + ex.Message.ReplaceLineEndings(" ");
            }

            if (!outcome.StartsWith("equal", StringComparison.Ordinal))
            {
                differences.Add(row + "\t" + outcome);
            }

            lock (gate)
            {
                if (results is not null)
                {
                    File.AppendAllText(results, row + "\t" + outcome.ReplaceLineEndings(" ") + "\n");
                }
            }
        });
        Assert.True(differences.IsEmpty, string.Join("\n", differences));
    }

    // One pair through both executors: "equal", "equal failure" (both failed alike: the same executor call or NativeBuild's own
    // verdict, the same exception type, and the same text but for the tool's name or path and the folder, design A2 §5), or what
    // differs.
    private static async Task<string> CompareExecutors(string row)
    {
        var work = row + ".compare";
        var sides = new (string Name, Func<NativeExecutorContext, JsonObject, NativeExecutor> Create)[]
        {
            ("tools", NativeToolchain.ExternalToolExecutor.Create), ("tatara", NativeToolchain.TataraExecutor.Create),
        };
        var reports = new string[2];
        for (var i = 0; i < 2; i++)
        {
            var copy = Path.Combine(work, sides[i].Name);
            CopyDirectory(row, copy);
            var report = new List<string>();
            var manifest = Path.Combine(copy, Path.GetRelativePath(row, Manifest(row)));
            Action<DiagnosticSeverity, string> add = (severity, text) => report.Add(severity + " " + text.Replace(copy, "<dir>", StringComparison.OrdinalIgnoreCase));
            var recording = new RecordingExecutor(sides[i].Create);
            try
            {
                await NativeToolchain.BuildManifest(manifest, null, null, recording.Create, add, CancellationToken.None);
            }
            catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
            {
                report.Add(recording.Failed is { } call
                    ? $"failure in {call}: {ex.GetType().Name}: {FailureText(ex.Message, copy)}"
                    : $"failure: {ex.GetType().Name}: {ex.Message.Replace(copy, "<dir>", StringComparison.OrdinalIgnoreCase)}");
            }

            reports[i] = string.Join("\n", report);
        }

        var differences = CompareFiles(Path.Combine(work, "tools"), Path.Combine(work, "tatara"));
        var failed = reports.Any(r => r.Split('\n')[^1].StartsWith("failure", StringComparison.Ordinal));
        if (reports[0] != reports[1])
        {
            differences.Add("report: " + reports[0].ReplaceLineEndings(" ") + " | " + reports[1].ReplaceLineEndings(" "));
        }

        return Clean(work, differences.Count != 0 ? "differ\t" + string.Join("; ", differences) : failed ? "equal failure" : "equal");
    }

    // A row's verdict is decided before its work folder is deleted; a folder that cannot be deleted (an executable still held, for
    // example) is reported as the cleanup's, beside the verdict.
    private static string Clean(string work, string verdict)
    {
        try
        {
            Directory.Delete(work, true);
            return verdict;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return verdict + "\tcleanup: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    // A failed call's text after its header line ("Tool failed: <path>; exit=<code>" or "Tatara failed: <operation>"), with the tools'
    // paths replaced by their names and the given folders removed: the tool's text and Tatara's compare equal when they say the same.
    private static string FailureText(string message, params string[] folders)
    {
        var text = message[(message.IndexOf('\n') + 1)..];
        foreach (var name in ToolNames)
        {
            text = text.Replace(Path.Combine(Root, name + ".exe"), name, StringComparison.OrdinalIgnoreCase).Replace(name + ".exe", name, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var folder in folders)
        {
            text = text.Replace(folder + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase).Replace(folder, "<dir>", StringComparison.OrdinalIgnoreCase);
        }

        return text.ReplaceLineEndings(" ").Trim();
    }

    // Every file of the two folders: equal names and bytes; a build record equal but for its tools.
    private static List<string> CompareFiles(string left, string right)
    {
        var differences = new List<string>();
        var names = Directory.EnumerateFiles(left, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(left, f))
            .Union(Directory.EnumerateFiles(right, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(right, f)));
        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            var (a, b) = (Path.Combine(left, name), Path.Combine(right, name));
            if (!File.Exists(a) || !File.Exists(b))
            {
                differences.Add(name + " only in " + (File.Exists(a) ? "tools" : "tatara"));
            }
            else if (name.EndsWith(".build.json", StringComparison.Ordinal) ? WithoutTools(a) != WithoutTools(b) : Hash(a) != Hash(b))
            {
                differences.Add(name);
            }
        }

        return differences;
    }

    private static string WithoutTools(string record)
    {
        var node = JsonNode.Parse(File.ReadAllText(record))!.AsObject();
        node.Remove("tools");
        return node.ToJsonString();
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private static async Task<string> Tool(string name, string directory, params string[] arguments) => (await Launch(name, directory, arguments)).Failure;

    // A tool of the root run in the directory: its failure ("<name> exit=<code>", a line end, then its output; empty on success) and
    // its standard output.
    private static async Task<(string Failure, string Output)> Launch(string name, string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Root, name + ".exe")) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode == 0 ? string.Empty : $"{name} exit={process.ExitCode}\n{await output}{errors}", await output);
    }

    // The tools' facts of an object, as ExternalToolExecutor reads them: the two llvm-nm listings and llvm-readobj's two predicates.
    private static async Task<(string Failure, string Facts)> ToolFacts(string directory, string obj)
    {
        var undefined = await Launch("llvm-nm", directory, "--undefined-only", "--format=posix", obj);
        var defined = await Launch("llvm-nm", directory, "--defined-only", "--extern-only", "--format=posix", obj);
        var inspection = await Launch("llvm-readobj", directory, "--unwind", "--coff-directives", obj);
        var failure = undefined.Failure + defined.Failure + inspection.Failure;
        return failure.Length != 0 ? (failure, string.Empty) : (string.Empty, Facts(new ObjectFacts(
            NativeToolchain.PosixSymbols(undefined.Output),
            NativeToolchain.PosixSymbols(defined.Output),
            inspection.Output.Contains("RuntimeFunction", StringComparison.Ordinal),
            inspection.Output.Contains("DEFAULTLIB", StringComparison.OrdinalIgnoreCase))));
    }

    private static string Facts(ObjectFacts facts)
        => string.Join(' ', facts.Undefined) + " | " + string.Join(' ', facts.DefinedExternal) + " | unwind " + facts.HasUnwind + ", default library " + facts.HasDefaultLibrary;

    // The steps, each run by the tools and by Tatara; a later step's input is that side's own product. The inspect step writes each
    // side's facts to inspected (tools, then Tatara).
    private static IEnumerable<(string Step, Func<Task<string>> Tools, Func<Task> Tatara)> Steps(string ir, string level, string tools, string tatara, NativeExecutor executor, string[] inspected)
    {
        var (toolIr, tataraIr) = (ir, ir);
        if (level == "O2")
        {
            (toolIr, tataraIr) = (Path.Combine(tools, "input.O2.ll"), Path.Combine(tatara, "input.O2.ll"));
            yield return ("optimize", () => Tool("opt", tools, "-S", "-passes=default<O2>", "-mtriple=" + Triple, ir, "-o", toolIr), () => executor.Optimize(ir, tataraIr));
        }

        yield return ("emit", () => Tool("llc", tools, "-" + level, "-filetype=obj", "-mtriple=" + Triple, "-mcpu=" + WindowsProfile.Cpu, "-mattr=" + WindowsProfile.Features, "-relocation-model=" + WindowsProfile.RelocationModel, "-code-model=" + WindowsProfile.CodeModel, toolIr, "-o", "input.obj"), () => executor.Emit(tataraIr, Path.Combine(tatara, "input.obj"), level));
        yield return ("inspect", async () =>
        {
            var (failure, facts) = await ToolFacts(tools, Path.Combine(tools, "input.obj"));
            inspected[0] = facts;
            return failure;
        }, async () => inspected[1] = Facts(await executor.Inspect(Path.Combine(tatara, "input.obj"))));
        yield return ("link", () => Tool("lld-link", tools, LinkArguments(Path.Combine(tools, "input.obj"), Path.Combine(tools, "input.exe"))), () => executor.Link(LinkArguments(Path.Combine(tatara, "input.obj"), Path.Combine(tatara, "input.exe"))));
    }

    private static NativeExecutor Executor(string output)
        => NativeToolchain.TataraExecutor.Create(
            new NativeExecutorContext(Root, Root, output, output, (_, _) => { }, CancellationToken.None, Triple, WindowsProfile.Cpu, WindowsProfile.Features, WindowsProfile.RelocationModel, WindowsProfile.CodeModel),
            new JsonObject());

    // One IR at one level: the tools (opt -S default<O2>, llc, llvm-nm and llvm-readobj, lld-link with P13's arguments) in one folder,
    // Tatara's optimize, emit, inspect and link in another, the same input file read by both; equal O2 text, object, object facts and
    // executable, or the same failing step with the same text (FailureText).
    private async Task<string> CompareWithTools(string row)
    {
        var (ir, level) = (row[..row.LastIndexOf('|')], row[(row.LastIndexOf('|') + 1)..]);
        var work = Path.Combine(this.directory, Guid.NewGuid().ToString("N"));
        var (tools, tatara) = (Path.Combine(work, "tools"), Path.Combine(work, "tatara"));
        Directory.CreateDirectory(tools);
        Directory.CreateDirectory(tatara);
        var executor = Executor(tatara);
        var inspected = new string[2];
        (string Step, string Text) toolFailure = (string.Empty, string.Empty), tataraFailure = (string.Empty, string.Empty);
        foreach (var (step, run, call) in Steps(ir, level, tools, tatara, executor, inspected))
        {
            toolFailure = toolFailure.Step.Length == 0 && await run() is { Length: > 0 } failed ? (step, FailureText(failed, tools)) : toolFailure;
            try
            {
                if (tataraFailure.Step.Length == 0)
                {
                    await call();
                }
            }
            catch (IOException ex)
            {
                tataraFailure = (step, FailureText(ex.Message, tatara));
            }
        }

        var differences = CompareFiles(tools, tatara);
        if (inspected[0] != inspected[1])
        {
            differences.Add("inspect: tools " + inspected[0] + " || tatara " + inspected[1]);
        }

        var verdict = toolFailure != tataraFailure
            ? "differ\tfailure: tools " + (toolFailure.Step.Length == 0 ? "none" : toolFailure) + " | tatara " + (tataraFailure.Step.Length == 0 ? "none" : tataraFailure)
            : differences.Count != 0 ? "differ\t" + string.Join("; ", differences) : toolFailure.Step.Length != 0 ? "equal failure\t" + toolFailure.Step : "equal";
        return Clean(work, verdict);
    }

    // C-424 N6: an IR with a CodeView compile unit (the debug information no fixed row's IR carries).
    private string DebugIr()
    {
        var path = Path.Combine(this.directory, "debug.ll");
        var ir = """
            target triple = "x86_64-pc-windows-msvc"

            @g = global i32 7, align 4

            define i32 @f(i32 %x) !dbg !8 {
            entry:
              %p = alloca i32, align 4
              store i32 %x, ptr %p, align 4, !dbg !12
              %v = load i32, ptr %p, align 4, !dbg !12
              %w = load i32, ptr @g, align 4, !dbg !13
              %y = add i32 %v, %w, !dbg !13
              ret i32 %y, !dbg !13
            }

            !llvm.dbg.cu = !{!0}
            !llvm.module.flags = !{!3, !4}

            !0 = distinct !DICompileUnit(language: DW_LANG_C99, file: !1, producer: "debug row", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug)
            !1 = !DIFile(filename: "debug.c", directory: "C:\\debug")
            !3 = !{i32 2, !"CodeView", i32 1}
            !4 = !{i32 2, !"Debug Info Version", i32 3}
            !8 = distinct !DISubprogram(name: "f", scope: !1, file: !1, line: 1, type: !9, scopeLine: 1, spFlags: DISPFlagDefinition, unit: !0)
            !9 = !DISubroutineType(types: !10)
            !10 = !{null}
            !12 = !DILocation(line: 2, column: 3, scope: !8)
            !13 = !DILocation(line: 3, column: 5, scope: !8)

            """;
        File.WriteAllText(path, ir);
        return path;
    }

    // The fixed rows, each a generated project: Hello and Milestones 1, 17 and 30 at O0 and O2, the FFI program with a user static
    // library (test-cli.ps1's foreign supply) at O2, and Hello's Debug build.
    private async Task<string[]> FixedRows()
    {
        var rows = new List<string>();
        foreach (var level in Levels)
        {
            rows.Add(await this.Generate("hello-" + level, Hello, level));
            foreach (var number in new[] { 1, 17, 30 })
            {
                rows.Add(await this.Generate($"m{number}-{level}", Milestone(number), level));
            }
        }

        rows.Add(await this.Generate("debug-O2", Hello, "O2", debug: true));
        rows.Add(await this.GenerateForeign());
        return [.. rows];
    }

    private async Task<string> Generate(string name, string source, string level, bool debug = false, string supply = "")
    {
        var row = Path.Combine(this.directory, name);
        Directory.CreateDirectory(row);
        var stem = Path.GetFileNameWithoutExtension(source);
        File.Copy(source, Path.Combine(row, stem + ".kimi"), true);
        var project = Path.Combine(row, stem + ".kimiproj");
        File.WriteAllText(project, $"Targets=\n  \"{Triple}\"\nOutputKind=\"Application\"\nOptimization=\"{level}\"\n{supply}\n");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded), console.Text);
        loaded.KimiOptions.Debug = debug;
        Assert.True(await loaded.Generate(TestContext.Current.CancellationToken), console.Text);
        return row;
    }

    private async Task<string> GenerateForeign()
    {
        var source = Path.Combine(this.directory, "Foreign.kimi");
        var program = """
            group Native
                #LibraryImport("codec", "mix")
                public unsafe func mix(a: i8, b: u16, c: i32, d: i64, e: f32, f: f64) -> f64
                #LibraryImport("codec", "notify")
                public unsafe func notify(value: u32) -> ()
                #LibraryImport("codec", "read_total")
                public unsafe func readTotal() -> i32
                #LibraryImport("codec", "?value@@YAHXZ")
                public unsafe func mangled() -> i32
            public func main()
                var total: f64 = 0.0
                unsafe => total = Native.mix(-1, 65535, 3, -4, 0.5, 2.25)
                require total == 65535.75 else => $abort("mix")
                unsafe => Native.notify(7)
                unsafe => Native.notify(5)
                var sum: i32 = 0
                unsafe => sum = Native.readTotal()
                require sum == 12 else => $abort("notify")
                var answer: i32 = 0
                unsafe => answer = Native.mangled()
                require answer == 42 else => $abort("mangled")
                Console.writeLine("foreign ok")
            """;
        File.WriteAllText(source, program);
        var native = Path.Combine(this.directory, "ffi-O2", "native");
        Directory.CreateDirectory(native);
        var codec = """
            static int total;
            double mix(signed char a, unsigned short b, int c, long long d, float e, double f) { return a + b + c + d + e + f; }
            void notify(unsigned int value) { total += (int)value; }
            int read_total(void) { return total; }
            int mangled(void) __asm__("?value@@YAHXZ");
            int mangled(void) { return 42; }
            """;
        File.WriteAllText(Path.Combine(native, "codec.c"), codec);
        Assert.Equal(string.Empty, await Tool("clang", native, "--target=" + Triple, "-O2", "-fno-autolink", "-fno-stack-protector", "-c", "codec.c", "-o", "codec.obj"));
        Assert.Equal(string.Empty, await Tool("llvm-lib", native, "/out:codec.lib", "codec.obj"));
        var supply = $"NativeLibraries=\n  \"{Triple}\"=\n    codec={{ Kind=\"static\" Input=\"native/codec.lib\" }}";
        return await this.Generate("ffi-O2", source, "O2", supply: supply);
    }

    // The fatal rows, each alone in its own test process (run-tatara-corpus.ps1 runs each by its method): a return of 2 makes the
    // failing request and every later one the toolchain's failure naming the first cause, for the rest of the process.
    public sealed class Fatal
    {
        // LLD's fatal(): a malformed static library; LLD's canRunAgain is false.
        [Fact(Explicit = true)]
        public async Task LinkerFatalRefusesEveryLaterCall()
        {
            var directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-fatal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "f.ll"), "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine void @f() {\n  ret void\n}\n");
            File.WriteAllText(Path.Combine(directory, "malformed.lib"), "!<arch>\nnot a library member\n");
            var executor = Executor(directory);
            var obj = Path.Combine(directory, "f.obj");
            await executor.Emit(Path.Combine(directory, "f.ll"), obj, "O0");
            var error = await Assert.ThrowsAsync<IOException>(() => executor.Link([obj, Path.Combine(directory, "malformed.lib"), "/entry:f", "/subsystem:console", "/nodefaultlib", "/out:" + Path.Combine(directory, "f.exe")]));
            Assert.StartsWith("Tatara failed: link\n", error.Message, StringComparison.Ordinal);
            Assert.Contains("lld-link: error:", error.Message, StringComparison.Ordinal);
            Assert.Contains("truncated or malformed archive", error.Message, StringComparison.Ordinal);
            var later = await Assert.ThrowsAsync<IOException>(() => executor.Inspect(obj));
            Assert.StartsWith("Tatara failed: inspect\n", later.Message, StringComparison.Ordinal);
            Assert.Contains("truncated or malformed archive", later.Message, StringComparison.Ordinal);
            Assert.True(NativeToolchain.IsToolchainFailure(later));
            Directory.Delete(directory, true);
        }

        // An LLVM fatal error after the object is opened; the object is removed, as llc leaves none (C-424 R2).
        [Fact(Explicit = true)]
        public async Task FatalErrorRefusesEveryLaterCall()
        {
            var directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-fatal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var fatal = Path.Combine(directory, "fatal.ll");
            var ir = """
                target triple = "x86_64-pc-windows-msvc"

                declare <4 x i32> @llvm.aarch64.neon.abs.v4i32(<4 x i32>)

                define <4 x i32> @f(<4 x i32> %x) {
                  %y = call <4 x i32> @llvm.aarch64.neon.abs.v4i32(<4 x i32> %x)
                  ret <4 x i32> %y
                }

                """;
            File.WriteAllText(fatal, ir);
            var executor = Executor(directory);
            var error = await Assert.ThrowsAsync<IOException>(() => executor.Emit(fatal, Path.Combine(directory, "fatal.obj"), "O2"));
            Assert.StartsWith("Tatara failed: emit\n", error.Message, StringComparison.Ordinal);
            Assert.Contains("Cannot select", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(directory, "fatal.obj")));
            var later = await Assert.ThrowsAsync<IOException>(() => executor.Inspect(Path.Combine(directory, "fatal.obj")));
            Assert.StartsWith("Tatara failed: inspect\n", later.Message, StringComparison.Ordinal);
            Assert.Contains("Cannot select", later.Message, StringComparison.Ordinal);
            Assert.True(NativeToolchain.IsToolchainFailure(later));
            Directory.Delete(directory, true);
        }
    }

    // An executor that records which of its calls failed (C-424 C3'): the executor comparison tells a failed call from NativeBuild's
    // own verdict.
    private sealed class RecordingExecutor(Func<NativeExecutorContext, JsonObject, NativeExecutor> create) : NativeExecutor
    {
        private NativeExecutor inner = null!;

        internal string? Failed { get; private set; }

        internal NativeExecutor Create(NativeExecutorContext context, JsonObject identities)
        {
            this.inner = create(context, identities);
            return this;
        }

        internal override Task Optimize(string ir, string optimizedIr) => this.Record("optimize", () => this.inner.Optimize(ir, optimizedIr));

        internal override Task Emit(string ir, string obj, string optimization) => this.Record("emit", () => this.inner.Emit(ir, obj, optimization));

        internal override async Task<ObjectFacts> Inspect(string obj)
        {
            var facts = default(ObjectFacts);
            await this.Record("inspect", async () => facts = await this.inner.Inspect(obj));
            return facts;
        }

        internal override Task Link(IReadOnlyList<string> arguments) => this.Record("link", () => this.inner.Link(arguments));

        private async Task Record(string call, Func<Task> run)
        {
            try
            {
                await run();
            }
            catch
            {
                this.Failed = call;
                throw;
            }
        }
    }
}
