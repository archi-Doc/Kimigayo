// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class MinimalEmissionTest
{
    // Supported floating arithmetic retained in the original execution-boundary inputs.
    internal const string FloatExpression = "1.0 + 2.0";

    [Theory]
    [InlineData("::Kimi.Console.writeLine(\"Hello, world!\")")]
    [InlineData("Console.writeLine(\"\")")]
    [InlineData("Console.writeLine(text: \"日本語\\0\")")]
    [InlineData("Console.writeLine(\"a\")\nConsole.writeLine(\"b\")")]
    [InlineData("Console.writeLine((\"a\"))\n()")]
    public void EmitsCheckedLiteralCall(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Generate(TextWriter.Null), Describe(c));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.True(c.Generate(writer), MinimalEmissionTest.Describe(c));
        var ir = writer.ToString();
        Assert.Contains("%kimi.string = type { ptr, i64, i8 }", ir);
        Assert.Contains("define void @__kimi_start() noreturn #0", ir);
        var module = GeneratedIr.Parse(ir);
        // SPEC 22.7 (R8-W2 d): the string overload is a Kimigayo body, called with the string borrow and the call's location; no module
        // holds the runtime routine it was (fail-before: __kimi_write_line).
        Assert.Contains(
            module.Startup.Instructions,
            x => x.Callee?.Signature == "internal void (ptr noundef nonnull align 8 dereferenceable(24), ptr, i64)" &&
                x.Shape.StartsWith("call void @(ptr noundef nonnull align 8 dereferenceable(24) %, ", StringComparison.Ordinal));
        Assert.DoesNotContain("__kimi_write_line", ir);
        Assert.Contains(module.Functions.SelectMany(static x => x.Instructions), x => x.Callee == module.Function("__kimi_destroy_string"));
        Assert.Contains("@_fltused = global i32 0, align 4", ir);
    }

    [Theory]
    [InlineData("func unused() -> ()\n    " + FloatExpression + "\nConsole.writeLine(\"a\")", true)]
    [InlineData("func unused<T>() => ()\nConsole.writeLine(\"a\")", true)]
    [InlineData("struct Empty\n    func unused() => ()\nConsole.writeLine(\"a\")", true)]
    [InlineData("public func main(x: i32) => Console.writeLine(\"a\")")]
    [InlineData("if false => " + FloatExpression, true)]
    [InlineData("let x: string\nConsole.writeLine(x)")]
    [InlineData("let x = \"a\"\nConsole.writeLine(x)\nConsole.writeLine(x)", true)]
    [InlineData("let x = \"a\"\nlet taken = x@move\nConsole.writeLine(x)")]
    [InlineData("func writeLine(x: string) => " + FloatExpression + "\nwriteLine(\"a\")", true)]
    [InlineData("let x = " + FloatExpression + "\nConsole.writeLine(\"a\")", true)]
    [InlineData("Console.writeLine(\"a\")\nlet flag = " + FloatExpression, true)]
    [InlineData("")]
    public void SelectedBodyEmissionMatchesImplementedFeatures(string source, bool emitted = false)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Fact]
    public void RebindingInvalidatesEmissionUntilAnalysisRunsAgain()
    {
        var c = Analyze("Console.writeLine(\"a\")");
        Assert.True(c.Generate(TextWriter.Null), Describe(c));
        c.Bind();
        Assert.False(c.Generate(TextWriter.Null));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.False(c.Generate(TextWriter.Null));
        c.Analyze();
        Assert.True(c.Generate(TextWriter.Null), Describe(c));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AppendingSourceInvalidatesFinalAnalysisBeforeEmission(bool directContext, bool valid)
    {
        var c = Analyze("Console.writeLine(\"original\")");
        Assert.True(c.Generate(TextWriter.Null), Describe(c));
        var source = new SourceDocument("Added.kimi", valid ? "func added() -> i32 => 7" : "func added() -> i32 => missing");
        if (directContext)
        {
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        }
        else
        {
            c.Kotonoha.AddSource(source);
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.False(c.Generate(writer));
        Assert.Equal(string.Empty, writer.ToString());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.Startup.IsComplete);
        Assert.False(c.Analysis.IsVerified);
        Assert.Throws<InvalidOperationException>(() => c.Binding.CheckBound());
        Assert.Throws<InvalidOperationException>(() => c.Analyze());
        Assert.Equal(valid, c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.Equal(valid, c.Generate(TextWriter.Null));
        if (valid)
        {
            var added = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "added");
            Assert.NotNull(c.BuildMir()[new(added.SymbolOf(), MirBodyKind.Fn)]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadingSourceInvalidatesFinalAnalysisEvenForAnEmptySnapshot(bool empty)
    {
        var c = Analyze("Console.writeLine(\"original\")");
        Assert.True(c.Generate(TextWriter.Null));
        var snapshot = empty ? Compilation.CreateForTest().Kotonoha : c.Kotonoha;
        var restored = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(snapshot), ref restored);
        Assert.NotNull(restored);
        Assert.Same(c.Kotonoha, restored);
        restored.OnDeserialized(c);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.False(c.Generate(writer));
        Assert.Equal(string.Empty, writer.ToString());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Analysis.IsVerified);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.Equal(!empty, c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DirectParseErrorsRemainFatal(bool customDestination, bool existingDiagnostic)
    {
        var c = Analyze("Console.writeLine(\"original\")");
        var diagnostics = customDestination ? c.Diagnostics.GetOrAddCollection("Added.kimi", c.Kotonoha) : c.Kotonoha.DiagnosticCollection;
        if (existingDiagnostic)
        {
            // The parser error has the same offset as an earlier error; the error state still records it.
            diagnostics.Add(new SourceSpan(0, 1), DiagnosticCode.TypeMismatch_Kd, sourceDocument: new SourceDocument("Earlier.kimi", "x"));
        }

        c.Kotonoha.CreateCodeContext(diagnostics).Parse(c.Kotonoha.RootKoto, new SourceDocument("Added.kimi", "virtual struct Invalid"));
        Assert.True(c.Diagnostics.HasSyntaxErrors(c.Kotonoha));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.False(c.Generate(writer));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectValidParseKeepsItsOwnSyntaxStateAndEveryErrorBlocksEmission(bool customDestination)
    {
        var c = Analyze("Console.writeLine(\"original\")");
        var diagnostics = customDestination ? c.Diagnostics.GetOrAddCollection("Added.kimi", c.Kotonoha) : c.Kotonoha.DiagnosticCollection;
        diagnostics.Add(new SourceSpan(0, 1), DiagnosticCode.TypeMismatch_Kd, sourceDocument: new SourceDocument("Earlier.kimi", "x"));
        var added = new SourceDocument("Added.kimi", "func added() => ()");
        c.Kotonoha.CreateCodeContext(diagnostics).Parse(c.Kotonoha.RootKoto, added);
        Assert.False(c.Diagnostics.HasSyntaxErrors(added));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.False(c.Generate(TextWriter.Null));
        c.Diagnostics.InvalidateSyntax(c.Kotonoha);
        Assert.True(c.Generate(TextWriter.Null), Describe(c));
    }

    [Fact]
    public void DirectParseWarningsDoNotPreventEmission()
    {
        var c = Analyze("Console.writeLine(\"original\")");
        var diagnostics = c.Diagnostics.GetOrAddCollection("Added.kimi", c.Kotonoha);
        c.Kotonoha.CreateCodeContext(diagnostics).Parse(c.Kotonoha.RootKoto, new SourceDocument("Added.kimi", "struct S\n    public func read(self: ref/Self) -> i32 => self.value\n    public let value: i32"));
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(TestDiagnostics.Of(c, "Added.kimi")).Severity);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.True(c.Generate(TextWriter.Null), Describe(c));
    }

    [Fact]
    public void RuntimeLocationEscapesUnicodeAndRetainsOriginalLineAndColumn()
    {
        // IMPL 20.1: the displayed logical name separates directories with `/`.
        var path = Path.Combine("日本", "file.kimi");
        var c = Analyze("\n::Kimi.Console.writeLine(\"x\")", path);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.True(c.Generate(writer), MinimalEmissionTest.Describe(c));
        const string Expected = "\\u{65E5}\\u{672C}/file.kimi:2:1";
        var escaped = string.Concat(Encoding.ASCII.GetBytes(Expected).Select(x => $"\\{x:X2}"));
        Assert.Contains($"@__kimi_location = private unnamed_addr constant [{Expected.Length} x i8] c\"{escaped}\"", writer.ToString());
        Assert.Equal(path, c.Kotonoha.SourceDocuments[0].Path);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmValidationDoesNotAllocate()
    {
        var c = Analyze("Console.writeLine(\"a\")");
        Assert.True(c.Generate(TextWriter.Null), Describe(c));

        // The helper retires the thread's allocation context before measuring (see AllocationMeasurement).
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null));
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmIrWritingDoesNotAllocateIntermediateStrings()
    {
        var c = Analyze("Console.writeLine(\"a\")");
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null));
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void SettingsRoundTripWithoutExecutingTools()
    {
        var file = new ProjectFile
        {
            Targets = [WindowsProfile.Target],
            LlvmBin = "C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin",
            OutputPath = "bin/Hello.ll",
            Optimization = "O0",
            NativeLibraries = new()
            {
                [WindowsProfile.Target] = new()
                {
                    ["kernel32"] = new() { Name = "kernel32", Kind = "import", Input = "C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin/kernel32.lib" },
                },
            },
        };
        var read = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(TinyhandSerializer.SerializeToUtf8(file))!;
        Assert.Equal(file.LlvmBin, read.LlvmBin);
        Assert.Equal(file.OutputPath, read.OutputPath);
        Assert.Equal(file.Optimization, read.Optimization);
        Assert.Equal(file.NativeLibraries[WindowsProfile.Target]["kernel32"], read.NativeLibraries[WindowsProfile.Target]["kernel32"]);
    }

    [Fact]
    public void ExampleSettingsCanBeRead()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/examples/Hello/Hello.kimiproj"));
        var read = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(File.ReadAllBytes(path))!;
        Assert.Equal(WindowsProfile.Target, Assert.Single(read.Targets));
        Assert.Empty(read.NativeLibraries);
        Assert.Null(read.LlvmBin);
    }

    [Fact]
    public void ExportNativeFixtures()
    {
        // These are inputs, not assertions of LLVM/link/runtime success. The separate native harness consumes them.
#if DEBUG
        const string Configuration = "Debug";
#else
        const string Configuration = "Release";
#endif
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../temp/emission-fixtures", Configuration));
        Directory.CreateDirectory(directory);
        foreach (var (name, source, path) in new[]
        {
            ("Hello", "::Kimi.Console.writeLine(\"Hello, world!\")", "Hello.kimi"),
            ("Empty", "::Kimi.Console.writeLine(\"\")", "Empty.kimi"),
            ("Unicode", "::Kimi.Console.writeLine(\"日本語\\0x\")", "日本語\\入力.kimi"),
        })
        {
            var c = Analyze(source, path);
            using var writer = new StreamWriter(Path.Combine(directory, name + ".ll"), false, new UTF8Encoding(false));
            Assert.True(c.Generate(writer), Describe(c));
        }
    }

    // A program analyzed for `output`, an Application unless the caller names another.
    internal static Compilation Analyze(string source, string path = "Hello.kimi", OutputKind output = OutputKind.Application)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        c.Bind();
        c.Binding.CheckStartup(output);
        c.Analyze();
        Assert.Equal(0, c.Binding.IdlessFactSites); // R7-E1: a fact site is a node the check read, so a semantic write numbered it.
        return c;
    }

    // A program checked for `output` as a build checks it (Compilation.Check): every phase run and its records published.
    internal static Compilation Check(string source, string path = "Hello.kimi", OutputKind output = OutputKind.Application)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = output;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        c.Check();
        Assert.Equal(0, c.Binding.IdlessFactSites);
        return c;
    }

    // The evidence of a verdict through the published pipeline (SPEC 23.2.2): the program analyzed for `output`, then reported and generated
    // as a build runs them, has the verdict `accepted`. An accepted program has no analysis problem and is generated, with a generated body
    // for each function it declares other than a Contract's requirement and for its top-level statements (null), of which it has at least
    // one; a requirement has no body (SPEC 8.4.1: no implementations; 8.6.1; a written one is a syntax error). A Library generates every root
    // (K11); an Application generates what its startup reaches, so its source is top-level statements that reach every declared function
    // (not an explicit `main`). A rejected program is not generated. The result is the declaration names of the generated bodies.
    internal static string?[] GeneratedBodies(string source, OutputKind output, bool accepted)
    {
        var c = Check(source, output: output);
        var generated = c.Generate(TextWriter.Null);
        Assert.True(c.Accepts == accepted && generated == accepted && (!accepted || c.Problems.Count == 0), Describe(c));
        if (!generated)
        {
            return [];
        }

        var set = c.Instances!;
        var bodies = Enumerable.Range(0, set.Items.Length).Where(x => set.Items[x].Key.Kind == MonoKind.Body && set.IsGenerated(x))
            .Select(x => set.Items[x].Key.Body.Def.Decl).ToArray();
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => !x.IsRequirement).ToArray();
        Assert.NotEmpty(functions);
        Assert.All(functions, f => Assert.Contains(bodies, x => x?.Declaration == (f == c.Kotonoha.GeneratedFunction ? null : f)));
        return [.. bodies.Select(static x => x?.Name)];
    }

    // SPEC 8.15.3, 23.1.1: a valid program past a generation resource limit is refused with GenerationResourceLimit_Kd and no output, checked
    // and then generated as a build runs them: check reports the checked set's limit at its location (K13b; DECISIONS R5-M156), and
    // generation adds nothing.
    internal static void AssertGenerationResourceLimit(string source, string at)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal("GenerationResourceLimit_Kd " + at, Problems(c));
        c.ReportAnalysis();
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(("GenerationResourceLimit_Kd", DiagnosticSeverity.Error), (error.Code, error.Severity));
    }

    // The problems of the latest analysis, each as its code and the first line of its subject, joined by " | ": the MIR's rows in the
    // publisher's order, which check publishes (DECISIONS R5-M185).
    internal static string Problems(Compilation c)
        => string.Join(" | ", c.Problems.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}"));

    // The failure message of an analysis or generation assertion: the results, Binding's issues (failure, code and location), the
    // analysis rows with their Op (DECISIONS R5-M185), and the generation records of the Emission partition (Compilation.Generate's). It
    // only reads, since a message argument is evaluated whether or not the assertion fails.
    internal static string Describe(Compilation c)
        => $"Binding={c.Binding.Result}; Startup={c.Binding.Startup}; Analysis={c.Analysis}; " +
            string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Failure} {x.Code} {CompilationTestHelper.Location(x.Node)}")) + "; " +
            string.Join(", ", c.Problems.Select(x => $"{x.Code} {HirTables.Op(c.SyntaxOf(x.Subject))}")) + "; Emission=" +
            string.Join(", ", c.Diagnostics.Finalize(DiagnosticPartition.Emission, DiagnosticPartition.Emission).Diagnostics
                .Select(static x => $"{x.Code} {x.Span}"));

    // SPEC 8.6: an effect bound rejects its conformance with A4's IncompatibleContractImplementation_Kd at the obligation subject, for the
    // Loans, the calls and the destructions the implementation's bodies perform (their planned cleanups): B-del deleted Binding's effect
    // checks (DECISIONS R5-M185). Binding keeps the code for a bounded Callable premise that a requirement does not declare (SPEC 8.11.4).
    internal static void AssertEffectBoundRejected(Compilation c)
        => Assert.True(
            c.Binding.Issues.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd) || c.Problems.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd),
            Describe(c));
}
