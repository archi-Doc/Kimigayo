// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi;
using Kimi.Compiler;
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

    [Fact]
    public void PhysicalModuleStorageHasNoTransitiveSemanticReferences()
    {
        var visited = new HashSet<Type>();
        var pending = new Stack<Type>();
        pending.Push(typeof(EmissionModule));
        while (pending.TryPop(out var type))
        {
            if (!visited.Add(type) || type.IsEnum)
            {
                continue;
            }

            Assert.False(typeof(Kimi.Compiler.Parsing.Koto).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(Delegate).IsAssignableFrom(type), type.FullName);
            Assert.False(type.Name.StartsWith("Bound", StringComparison.Ordinal) || type == typeof(Binding) || type == typeof(Compilation) || type == typeof(OwnershipBody), type.FullName);
            if (type.HasElementType)
            {
                pending.Push(type.GetElementType()!);
            }

            foreach (var argument in type.GenericTypeArguments)
            {
                pending.Push(argument);
            }

            if (type.Assembly == typeof(EmissionModule).Assembly)
            {
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    pending.Push(field.FieldType);
                }
            }
        }
    }

    [Theory]
    [InlineData("::Kimi.Console.writeLine(\"Hello, world!\")")]
    [InlineData("Console.writeLine(\"\")")]
    [InlineData("Console.writeLine(text: \"日本語\\0\")")]
    [InlineData("Console.writeLine(\"a\")\nConsole.writeLine(\"b\")")]
    [InlineData("Console.writeLine((\"a\"))\n()")]
    public void EmitsCheckedLiteralCall(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Validate(out var error), Describe(c, error));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.True(c.WriteIr(writer, out error), error);
        var ir = writer.ToString();
        Assert.Contains("%kimi.string = type { ptr, i64, i8 }", ir);
        Assert.Contains("define void @__kimi_start() noreturn #0", ir);
        var module = GeneratedIr.Parse(ir);
        Assert.Contains(module.Startup.Instructions, x => x.Callee == module.Function("__kimi_write_line") &&
            x.Shape.StartsWith("call void @(ptr noundef nonnull align 8 dereferenceable(24) %, ", StringComparison.Ordinal));
        Assert.Contains(module.Functions.SelectMany(static x => x.Instructions), x => x.Callee == module.Function("__kimi_destroy_string"));
        Assert.Contains("@_fltused = global i32 0, align 4", ir);
        Assert.Equal("internal void (ptr noundef nonnull align 8 dereferenceable(24), ptr, i64)", module.Function("__kimi_write_line").Signature);
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
        Assert.True(c.Validate(out var error), Describe(c, error));
        c.Bind();
        Assert.False(c.Validate(out _));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.False(c.Validate(out _));
        c.Analyze();
        Assert.True(c.Validate(out error), Describe(c, error));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AppendingSourceInvalidatesFinalAnalysisBeforeEmission(bool directContext, bool valid)
    {
        var c = Analyze("Console.writeLine(\"original\")");
        Assert.True(c.Validate(out var error), Describe(c, error));
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
        Assert.False(c.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.Startup.IsComplete);
        Assert.False(c.Analysis.IsVerified);
        Assert.Throws<InvalidOperationException>(() => c.Binding.CheckBound());
        Assert.Throws<InvalidOperationException>(() => c.Analyze());
        Assert.Equal(valid, c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.Equal(valid, c.Validate(out error));
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
        Assert.True(c.Validate(out _));
        var snapshot = empty ? Compilation.CreateForTest().Kotonoha : c.Kotonoha;
        var restored = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(snapshot), ref restored);
        Assert.NotNull(restored);
        Assert.Same(c.Kotonoha, restored);
        restored.OnDeserialized(c);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.False(c.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Analysis.IsVerified);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        Assert.Equal(!empty, c.Validate(out _));
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
        Assert.False(c.WriteIr(writer, out _));
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
        Assert.False(c.Validate(out _));
        c.Diagnostics.InvalidateSyntax(c.Kotonoha);
        Assert.True(c.Validate(out var error), Describe(c, error));
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
        Assert.True(c.Validate(out var error), Describe(c, error));
    }

    [Fact]
    public void RuntimeLocationEscapesUnicodeAndRetainsOriginalLineAndColumn()
    {
        // IMPL 20.1: the displayed logical name separates directories with `/`.
        var path = Path.Combine("日本", "file.kimi");
        var c = Analyze("\n::Kimi.Console.writeLine(\"x\")", path);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.True(c.WriteIr(writer, out var error), error);
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
        Assert.True(c.Validate(out var error), Describe(c, error));

        // The helper retires the thread's allocation context before measuring (see AllocationMeasurement).
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.Validate(out _));
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmIrWritingDoesNotAllocateIntermediateStrings()
    {
        var c = Analyze("Console.writeLine(\"a\")");
        var valid = true;
        var allocated = AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _));
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
            Assert.True(c.WriteIr(writer, out var error), Describe(c, error));
        }
    }

    // A program analyzed for an Application, by the process's code generator unless one is named; MIR code generation needs the mir
    // pipeline until G2 ends that pairing (R5 PD4), so this switch goes in G2's merge.
    internal static Compilation Analyze(string source, string path = "Hello.kimi", CompilerCodegen? codegen = null)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = codegen == CompilerCodegen.Mir ? CompilerPipeline.Mir : c.Pipeline;
        c.Codegen = codegen ?? c.Codegen;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        return c;
    }

    // SPEC 8.15.3, 23.1.1: a valid program past a generation resource limit is refused with GenerationResourceLimit_Kd and no output, checked
    // and then generated as a build runs them. MIR code generation reports the checked set's limit at check (K13b); the old generator's
    // failure text is checked on a compilation pinned to it until G2 (DECISIONS R5-M85).
    internal static void AssertGenerationResourceLimit(string source, string legacyFailure)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, Describe(c, null));
        Assert.True(c.Analysis.IsVerified == Codegen.Expect(false, true, CodegenDeviations.CheckedSetAtCheck), Describe(c, null));
        c.ReportAnalysis();
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(("GenerationResourceLimit_Kd", DiagnosticSeverity.Error), (error.Code, error.Severity));

        var old = Analyze(source, codegen: CompilerCodegen.Legacy);
        Assert.False(old.WriteIr(TextWriter.Null, out var failure));
        Assert.True(old.Emission.FailureIsResourceLimit, failure);
        Assert.Contains(legacyFailure, failure, StringComparison.Ordinal);
    }

    // The failure message of an analysis or generation assertion: the results, the old path's records (requirement, code and location) and,
    // under mir, the problems with their Op. It only reads, since a message argument is evaluated whether or not the assertion fails.
    internal static string Describe(Compilation c, string? error)
        => $"{error}; Binding={c.Binding.Result}; Startup={c.Binding.Startup}; Analysis={c.Analysis}; " +
            string.Join(", ", LegacyRecords.Of(c).Select(static x => $"{x.Requirement} {x.Code} {LegacyRecords.Location(x.Node)}")) +
            (c.Pipeline == CompilerPipeline.Mir ? "; " + string.Join(", ", c.Problems.Select(x => $"{x.Code} {HirTables.Op(c.SyntaxOf(x.Subject))}")) : string.Empty);

    // SPEC 8.6: an effect bound rejects its conformance at Binding for the Loans and calls, and after ownership analysis
    // for the destructions the implementation's bodies actually perform (their planned cleanups).
    internal static void AssertEffectBoundRejected(Compilation c)
        => Assert.True(
            c.Binding.Issues.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd) || c.Problems.Any(x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd),
            Describe(c, null));
}
