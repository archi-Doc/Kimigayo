// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class MinimalEmissionTest
{
    [Theory]
    [InlineData("::Core.writeLine(\"Hello, world!\")")]
    [InlineData("writeLine(\"\")")]
    [InlineData("writeLine(text: \"日本語\\0\")")]
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
    [InlineData("let x = \"a\"\nwriteLine(x)")]
    [InlineData("func unused() => 1\nwriteLine(\"a\")")]
    [InlineData("func unused<T>() => ()\nwriteLine(\"a\")")]
    [InlineData("struct Empty\n    func unused() => ()\nwriteLine(\"a\")")]
    [InlineData("writeLine(\"a\")\nwriteLine(\"b\")")]
    [InlineData("public func main() => writeLine(\"a\")")]
    [InlineData("if false => writeLine(\"a\")")]
    [InlineData("let x: string\nwriteLine(x)")]
    [InlineData("let x = \"a\"\nwriteLine(x)\nwriteLine(x)")]
    [InlineData("func writeLine(x: string) => ()\nwriteLine(\"a\")")]
    [InlineData("()")]
    [InlineData("")]
    public void UnsupportedOrInvalidInputNeverWritesIr(string source)
    {
        var c = Analyze(source);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.False(c.Emission.WriteIr(writer, out var error));
        Assert.NotNull(error);
        Assert.Equal(string.Empty, writer.ToString());
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
        Assert.Equal("import", read.NativeLibraries[WindowsProfile.Target]["kernel32"].Kind);
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

    private static string Describe(Compilation c, string? error)
        => $"{error}; Binding={c.Binding.Result}; Startup={c.Binding.Startup}; Ownership={c.Ownership.Result}; " +
            string.Join(", ", c.Ownership.Bodies.SelectMany(x => x.Operations).Select(x => $"{x.Kind}:{x.Place}:{x.Source.Akind}"));
}
