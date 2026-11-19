// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics;
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
// src/Tatara/run-corpus.ps1 prepares the root and the corpus and runs this class; the fatal row (Fatal) runs in its own process.
public sealed class TataraExecutorTest : IDisposable
{
    private const string Triple = WindowsProfile.Target;

    private static readonly string[] Levels = ["O0", "O2"];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-" + Guid.NewGuid().ToString("N"));

    public TataraExecutorTest() => Directory.CreateDirectory(this.directory);

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
        Assert.Contains("src/Tatara/build.ps1", abi.Message, StringComparison.Ordinal);
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
            : (await this.FixedRows()).Select(row => Manifest(row)).Select(m => Path.ChangeExtension(Path.ChangeExtension(m, null), ".ll") + "|" + Level(m)).ToArray();
        await Run(rows, row => this.CompareWithTools(row));
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

    // A request that fails with return 1 is the toolchain's failure, and the next request succeeds: an invalid IR, then a link with
    // an undefined symbol.
    [Fact(Explicit = true)]
    public async Task FailedRequestsLeaveTataraUsable()
    {
        var row = await this.Generate("hello", Hello, "O0");
        var ir = Path.ChangeExtension(Path.ChangeExtension(Manifest(row), null), ".ll");
        var executor = this.Executor(Path.GetDirectoryName(ir)!);
        var invalid = Path.Combine(this.directory, "invalid.ll");
        File.WriteAllText(invalid, "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @f( {\n");
        var error = await Assert.ThrowsAsync<IOException>(() => executor.Emit(invalid, Path.Combine(this.directory, "invalid.obj"), "O0"));
        Assert.StartsWith("Tatara failed: emit\n", error.Message, StringComparison.Ordinal);
        Assert.True(NativeToolchain.IsToolchainFailure(error));
        var obj = Path.Combine(this.directory, "hello.obj");
        await executor.Emit(ir, obj, "O0");
        var link = await Assert.ThrowsAsync<IOException>(() => executor.Link([obj, Kernel32, "/entry:__kimi_start", "/subsystem:console", "/nodefaultlib", "/out:" + Path.Combine(this.directory, "a.exe")]));
        Assert.StartsWith("Tatara failed: link\n", link.Message, StringComparison.Ordinal);
        Assert.Contains("undefined symbol", link.Message, StringComparison.Ordinal);
        await executor.Link(LinkArguments(obj, Path.Combine(this.directory, "b.exe")));
        Assert.True(File.Exists(Path.Combine(this.directory, "b.exe")));
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

    // One pair through both executors: "equal", "equal failure" (both failed with the same report but for the failure's text, which
    // names the tool or the operation, design A2 §5), or what differs.
    private static async Task<string> CompareExecutors(string row)
    {
        var work = row + ".compare";
        var sides = new (string Name, Func<string, Action<DiagnosticSeverity, string>, Task> Build)[]
        {
            ("tools", (manifest, report) => NativeToolchain.BuildManifest(manifest, null, null, report, CancellationToken.None)),
            ("tatara", (manifest, report) => NativeToolchain.BuildManifest(manifest, null, null, NativeToolchain.TataraExecutor.Create, report, CancellationToken.None)),
        };
        var reports = new string[2];
        for (var i = 0; i < 2; i++)
        {
            var copy = Path.Combine(work, sides[i].Name);
            CopyDirectory(row, copy);
            var report = new List<string>();
            var manifest = Path.Combine(copy, Path.GetRelativePath(row, Manifest(row)));
            Action<DiagnosticSeverity, string> add = (severity, text) => report.Add(severity + " " + text.Replace(copy, "<dir>", StringComparison.OrdinalIgnoreCase));
            try
            {
                await sides[i].Build(manifest, add);
            }
            catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
            {
                report.Add("failure");
            }

            reports[i] = string.Join("\n", report);
        }

        var differences = CompareFiles(Path.Combine(work, "tools"), Path.Combine(work, "tatara"));
        var failed = reports[0].EndsWith("failure", StringComparison.Ordinal) || reports[1].EndsWith("failure", StringComparison.Ordinal);
        if (reports[0] != reports[1])
        {
            differences.Add("report: " + reports[0].ReplaceLineEndings(" ") + " | " + reports[1].ReplaceLineEndings(" "));
        }

        Directory.Delete(work, true);
        return differences.Count != 0 ? "differ\t" + string.Join("; ", differences) : failed ? "equal failure" : "equal";
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

    private static async Task<string> Tool(string name, string directory, params string[] arguments)
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
        return process.ExitCode == 0 ? string.Empty : $"{name} exit={process.ExitCode} {await output}{errors}";
    }

    // The steps, each run by the tools and by Tatara; a later step's input is that side's own product.
    private static IEnumerable<(string Step, Func<Task<string>> Tools, Func<Task> Tatara)> Steps(string ir, string level, string tools, string tatara, NativeExecutor executor)
    {
        var (toolIr, tataraIr) = (ir, ir);
        if (level == "O2")
        {
            (toolIr, tataraIr) = (Path.Combine(tools, "input.O2.ll"), Path.Combine(tatara, "input.O2.ll"));
            yield return ("optimize", () => Tool("opt", tools, "-S", "-passes=default<O2>", "-mtriple=" + Triple, ir, "-o", toolIr), () => executor.Optimize(ir, tataraIr));
        }

        yield return ("emit", () => Tool("llc", tools, "-" + level, "-filetype=obj", "-mtriple=" + Triple, "-mcpu=" + WindowsProfile.Cpu, "-mattr=" + WindowsProfile.Features, "-relocation-model=" + WindowsProfile.RelocationModel, "-code-model=" + WindowsProfile.CodeModel, toolIr, "-o", "input.obj"), () => executor.Emit(tataraIr, Path.Combine(tatara, "input.obj"), level));
        yield return ("link", () => Tool("lld-link", tools, LinkArguments(Path.Combine(tools, "input.obj"), Path.Combine(tools, "input.exe"))), () => executor.Link(LinkArguments(Path.Combine(tatara, "input.obj"), Path.Combine(tatara, "input.exe"))));
    }

    // One IR at one level: the tools (opt -S default<O2>, llc, lld-link with P13's arguments) in one folder, Tatara's optimize, emit,
    // inspect and link in another, the same input file read by both; equal O2 text, object and executable, or equal failing step.
    private async Task<string> CompareWithTools(string row)
    {
        var (ir, level) = (row[..row.LastIndexOf('|')], row[(row.LastIndexOf('|') + 1)..]);
        var work = Path.Combine(this.directory, Guid.NewGuid().ToString("N"));
        var (tools, tatara) = (Path.Combine(work, "tools"), Path.Combine(work, "tatara"));
        Directory.CreateDirectory(tools);
        Directory.CreateDirectory(tatara);
        var executor = this.Executor(tatara);
        string toolStep = string.Empty, tataraStep = string.Empty;
        foreach (var (step, run, call) in Steps(ir, level, tools, tatara, executor))
        {
            toolStep = toolStep.Length == 0 && await run() is { Length: > 0 } failed ? step + ": " + failed : toolStep;
            try
            {
                if (tataraStep.Length == 0)
                {
                    await call();
                }
            }
            catch (IOException ex)
            {
                tataraStep = step + ": " + ex.Message;
            }
        }

        var differences = CompareFiles(tools, tatara);
        Directory.Delete(work, true);
        var toolFailed = toolStep.Split(':')[0];
        var tataraFailed = tataraStep.Split(':')[0];
        if (toolFailed != tataraFailed)
        {
            return "differ\tfailing step: tools " + (toolStep.Length == 0 ? "none" : toolStep) + " | tatara " + (tataraStep.Length == 0 ? "none" : tataraStep);
        }

        return differences.Count != 0 ? "differ\t" + string.Join("; ", differences) : toolFailed.Length != 0 ? "equal failure\t" + toolFailed : "equal";
    }

    private NativeExecutor Executor(string output)
        => NativeToolchain.TataraExecutor.Create(
            new NativeExecutorContext(Root, Root, output, output, (_, _) => { }, CancellationToken.None, Triple, WindowsProfile.Cpu, WindowsProfile.Features, WindowsProfile.RelocationModel, WindowsProfile.CodeModel),
            new JsonObject());

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

    // The fatal row, alone in its own test process (run-corpus.ps1 runs this class separately): an LLVM fatal error returns 2, and the
    // failing request and every later one are the toolchain's failure naming the first cause.
    public sealed class Fatal
    {
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
            var context = new NativeExecutorContext(Root, Root, directory, directory, (_, _) => { }, CancellationToken.None, Triple, WindowsProfile.Cpu, WindowsProfile.Features, WindowsProfile.RelocationModel, WindowsProfile.CodeModel);
            var executor = NativeToolchain.TataraExecutor.Create(context, new JsonObject());
            var error = await Assert.ThrowsAsync<IOException>(() => executor.Emit(fatal, Path.Combine(directory, "fatal.obj"), "O2"));
            Assert.StartsWith("Tatara failed: emit\n", error.Message, StringComparison.Ordinal);
            Assert.Contains("Cannot select", error.Message, StringComparison.Ordinal);
            var later = await Assert.ThrowsAsync<IOException>(() => executor.Inspect(Path.Combine(directory, "fatal.obj")));
            Assert.StartsWith("Tatara failed: inspect\n", later.Message, StringComparison.Ordinal);
            Assert.Contains("Cannot select", later.Message, StringComparison.Ordinal);
            Assert.True(NativeToolchain.IsToolchainFailure(later));
            Directory.Delete(directory, true);
        }
    }
}
