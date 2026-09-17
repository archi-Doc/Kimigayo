// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class MinimalEmissionTest
{
    // Supported floating arithmetic retained in the original execution-boundary inputs.
    internal const string FloatExpression = "1.0 + 2.0";

    [Theory]
    [InlineData("::Core.writeLine(\"Hello, world!\")")]
    [InlineData("writeLine(\"\")")]
    [InlineData("writeLine(text: \"日本語\\0\")")]
    [InlineData("writeLine(\"a\")\nwriteLine(\"b\")")]
    [InlineData("writeLine((\"a\"))\n()")]
    public void EmitsCheckedLiteralCall(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Emission.Validate(out var error), Describe(c, error));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.True(c.Emission.WriteIr(writer, out error), error);
        var ir = writer.ToString();
        Assert.Contains("%kimi.string = type { ptr, i64, i8 }", ir);
        Assert.Contains("define void @__kimi_start() noreturn #0", ir);
        Assert.Contains("call void @__kimi_write_line(ptr %p", ir);
        Assert.Contains("@_fltused = global i32 0, align 4", ir);
        Assert.DoesNotContain("byval", ir);
        Assert.DoesNotContain("sret", ir);
    }

    [Theory]
    [InlineData("func unused() -> ()\n    " + FloatExpression + "\nwriteLine(\"a\")", true)]
    [InlineData("func unused<T>() => ()\nwriteLine(\"a\")", true)]
    [InlineData("struct Empty\n    func unused() => ()\nwriteLine(\"a\")")]
    [InlineData("public func main(x: i32) => writeLine(\"a\")")]
    [InlineData("if false => " + FloatExpression, true)]
    [InlineData("let x: string\nwriteLine(x)")]
    [InlineData("let x = \"a\"\nwriteLine(x)\nwriteLine(x)")]
    [InlineData("func writeLine(x: string) => " + FloatExpression + "\nwriteLine(\"a\")", true)]
    [InlineData("let x = " + FloatExpression + "\nwriteLine(\"a\")", true)]
    [InlineData("writeLine(\"a\")\nlet flag = " + FloatExpression, true)]
    [InlineData("")]
    public void SelectedBodyEmissionMatchesImplementedFeatures(string source, bool emitted = false)
    {
        var c = MinimalEmissionTest.Analyze(source);
        FloatEmissionTest.AssertEmissionSupport(c, emitted);
    }

    [Fact]
    public void RebindingInvalidatesEmissionUntilAnalysisRunsAgain()
    {
        var c = Analyze("writeLine(\"a\")");
        Assert.True(c.Emission.Validate(out var error), Describe(c, error));
        c.Bind();
        Assert.False(c.Emission.Validate(out _));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out error), Describe(c, error));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AppendingSourceInvalidatesFinalAnalysisBeforeEmission(bool directContext, bool valid)
    {
        var c = Analyze("writeLine(\"original\")");
        Assert.True(c.Emission.Validate(out var error), Describe(c, error));
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
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.Startup.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Throws<InvalidOperationException>(() => c.Binding.CheckBound());
        Assert.Throws<InvalidOperationException>(() => c.Ownership.Analyze());
        Assert.Equal(valid, c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.Equal(valid, c.Emission.Validate(out error));
        if (valid)
        {
            Assert.Contains(c.Ownership.Bodies, x => x.Function.Name == "added");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadingSourceInvalidatesFinalAnalysisEvenForAnEmptySnapshot(bool empty)
    {
        var c = Analyze("writeLine(\"original\")");
        Assert.True(c.Emission.Validate(out _));
        var snapshot = empty ? Compilation.CreateForTest().Kotonoha : c.Kotonoha;
        var restored = c.Kotonoha;
        TinyhandSerializer.DeserializeObject(TinyhandSerializer.Serialize(snapshot), ref restored);
        Assert.NotNull(restored);
        Assert.Same(c.Kotonoha, restored);
        restored.OnDeserialized(c);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.Equal(!empty, c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DirectParseErrorsRemainFatalAfterDiagnosticClearing(bool customDestination, bool existingDiagnostic)
    {
        var c = Analyze("writeLine(\"original\")");
        var diagnostics = customDestination ? c.Kimigayo.GetOrAddDiagnosticCollection("Added.kimi") : c.Kotonoha.DiagnosticCollection;
        if (existingDiagnostic)
        {
            // The parser error will have the same offset as an already displayed error.
            diagnostics.Add(new SourceSpan(0, 1), DiagnosticCode.TypeMismatch_Kd);
        }

        c.Kotonoha.CreateCodeContext(diagnostics).Parse(c.Kotonoha.RootKoto, new SourceDocument("Added.kimi", "virtual func unavailable() => ()"));
        Assert.True(diagnostics.HasErrors);
        diagnostics.ClearDiagnostic();
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectValidParseDoesNotLatchPreexistingDiagnosticsAsSourceErrors(bool customDestination)
    {
        var c = Analyze("writeLine(\"original\")");
        var diagnostics = customDestination ? c.Kimigayo.GetOrAddDiagnosticCollection("Added.kimi") : c.Kotonoha.DiagnosticCollection;
        diagnostics.Add(new SourceSpan(0, 1), DiagnosticCode.TypeMismatch_Kd);
        c.Kotonoha.CreateCodeContext(diagnostics).Parse(c.Kotonoha.RootKoto, new SourceDocument("Added.kimi", "func added() => ()"));
        diagnostics.ClearDiagnostic();
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out var error), Describe(c, error));
    }

    [Fact]
    public void DirectParseWarningsDoNotPreventEmission()
    {
        var c = Analyze("writeLine(\"original\")");
        var diagnostics = c.Kimigayo.GetOrAddDiagnosticCollection("Added.kimi");
        c.Kotonoha.CreateCodeContext(diagnostics).Parse(c.Kotonoha.RootKoto, new SourceDocument("Added.kimi", "struct S\n    public func read(self: ref/Self) -> i32 => self.value\n    public let value: i32"));
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics.GetArray()).Entry.Severity);
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        Assert.True(c.Emission.Validate(out var error), Describe(c, error));
    }

    [Fact]
    public void RuntimeLocationEscapesUnicodeAndRetainsOriginalLineAndColumn()
    {
        var c = Analyze("\n::Core.writeLine(\"x\")", "日本\\file.kimi");
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.True(c.Emission.WriteIr(writer, out var error), error);
        const string Expected = "\\u{65E5}\\u{672C}\\\\file.kimi:2:1";
        var escaped = string.Concat(Encoding.ASCII.GetBytes(Expected).Select(x => $"\\{x:X2}"));
        Assert.Contains($"@__kimi_location = private unnamed_addr constant [{Expected.Length} x i8] c\"{escaped}\"", writer.ToString());
        Assert.Equal("日本\\file.kimi", c.Kotonoha.SourceDocuments[0].Path);
    }

    [Fact]
    public void WarmValidationDoesNotAllocate()
    {
        var c = Analyze("writeLine(\"a\")");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Emission.Validate(out var error), Describe(c, error));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Emission.Validate(out _);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WarmIrWritingDoesNotAllocateIntermediateStrings()
    {
        var c = Analyze("writeLine(\"a\")");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out _));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var valid = true;
        for (var i = 0; i < 128; i++)
        {
            valid &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
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
            NativeLibraries = new(StringComparer.Ordinal)
            {
                [WindowsProfile.Target] = new(StringComparer.Ordinal)
                {
                    ["kernel32"] = new() { Kind = "import", Input = "C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin/kernel32.lib" },
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
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../examples/Hello/Hello.kimiproj"));
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
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../bin/emission-fixtures", Configuration));
        Directory.CreateDirectory(directory);
        foreach (var (name, source, path) in new[]
        {
            ("Hello", "::Core.writeLine(\"Hello, world!\")", "Hello.kimi"),
            ("Empty", "::Core.writeLine(\"\")", "Empty.kimi"),
            ("Unicode", "::Core.writeLine(\"日本語\\0x\")", "日本語\\入力.kimi"),
        })
        {
            var c = Analyze(source, path);
            using var writer = new StreamWriter(Path.Combine(directory, name + ".ll"), false, new UTF8Encoding(false));
            Assert.True(c.Emission.WriteIr(writer, out var error), Describe(c, error));
        }
    }

    internal static Compilation Analyze(string source, string path = "Hello.kimi")
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        return c;
    }

    internal static string Describe(Compilation c, string? error)
        => $"{error}; Binding={c.Binding.Result}; Startup={c.Binding.Startup}; Ownership={c.Ownership.Result}; " +
            string.Join(", ", c.Ownership.Bodies.SelectMany(x => x.Operations).Select(x => $"{x.Kind}:{x.Place}:{x.Source.Akind}"));
}
