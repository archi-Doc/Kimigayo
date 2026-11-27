// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        var line = FunctionCodegenTest.LineOf(module, c, view: false);
        Assert.Contains(
            module.Startup.Instructions,
            x => x.Callee == line && x.Shape.StartsWith("call void @(ptr noundef nonnull align 8 dereferenceable(24) %, ", StringComparison.Ordinal));
        Assert.DoesNotContain("__kimi_write_line", ir);
        Assert.Contains(module.Functions.SelectMany(static x => x.Instructions), x => x.Callee == module.Function("__kimi_destroy_string"));
        Assert.Contains("@_fltused = global i32 0, align 4", ir);
    }

    // The Tatara plan's B7b2a: Hello's linux-x64 module in the row's frame: its header without a float marker, its footer (#0 with
    // "probe-stack"="inline-asm"; no PIC Level module flag, a static row's), the libc-free start (module asm defining __kimi_start, which
    // calls the internal start body, which sets the process up before the startup body), no dllimport, the memory part and the Linux
    // layer's own routines defined; Windows' module defines neither routine. B7b2b's twin of the C path: the module requires no library
    // (the Kimi library imports nothing on linux-x64), so it defines no main.
    [Fact]
    public void HelloOnTheLinuxRowIsLibcFree()
    {
        const string Hello = "::Kimi.Console.writeLine(\"Hello, world!\")";
        var hello = Analyze(TargetProfile.Linux, Hello, "Hello.kimi", OutputKind.Application);
        var ir = CompilationTestHelper.WriteIr(hello);
        Assert.Empty(hello.Generated.Libraries);
        Assert.Equal(TargetProfile.Linux.EntrySymbol, hello.Generated.Entry);
        Assert.StartsWith(
            "; Kimigayo checked pre-optimization IR (linux-x64-v1)\ntarget triple = \"x86_64-unknown-linux-musl\"\ntarget datalayout = \"" +
            TargetProfile.Linux.DataLayout + "\"\n%kimi.string = type { ptr, i64, i8 }\n@",
            ir,
            StringComparison.Ordinal);
        const string Profile = "uwtable(async) \"target-cpu\"=\"x86-64\" \"target-features\"=\"+sse2\" \"denormal-fp-math\"=\"ieee,ieee\" \"probe-stack\"=\"inline-asm\"";
        Assert.EndsWith($"}}\nattributes #0 = {{ {Profile} }}\nattributes #1 = {{ {Profile} \"no-builtins\" }}\n", ir, StringComparison.Ordinal);
        Assert.Contains(
            "module asm \".globl __kimi_start\"\nmodule asm \".type __kimi_start, @function\"\nmodule asm \"__kimi_start:\"\n" +
            "module asm \"  xorl %ebp, %ebp\"\nmodule asm \"  andq $-16, %rsp\"\nmodule asm \"  callq __kimi_start_body\"\nmodule asm \"  ud2\"\n" +
            "@llvm.used = appending global [1 x ptr] [ptr @__kimi_start_body], section \"llvm.metadata\"\n" +
            "define internal void @__kimi_start_body() noreturn #0 {\nentry:\n  call void @__kimi_platform_start()\n",
            ir,
            StringComparison.Ordinal);
        Assert.Contains("  call void @__kimi_exit(i32 0)\n  unreachable\n}\n", ir, StringComparison.Ordinal);
        Assert.Contains(LlvmModuleText.MemoryRuntime, ir, StringComparison.Ordinal);
        Assert.Contains(RuntimeAbi.PlatformStart.GetDefinition(exported: false), ir, StringComparison.Ordinal);
        Assert.Contains(RuntimeAbi.Clock.GetDefinition(exported: false), ir, StringComparison.Ordinal);
        foreach (var absent in new[] { "define void @__kimi_start(", "@main(", "_fltused", "dllimport", "PIC Level", "llvm.module.flags" })
        {
            Assert.DoesNotContain(absent, ir, StringComparison.Ordinal);
        }

        var windows = CompilationTestHelper.WriteIr(Analyze(Hello));
        Assert.DoesNotContain("@__kimi_platform_start(", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("@__kimi_clock(", windows, StringComparison.Ordinal);
    }

    // The Tatara plan's B7b2a: a test build on the Linux row, whose layer runs no test build (B7b8 refuses a Linux test before any build),
    // is a broken contract at generation.
    [Fact]
    public void ATestBuildOnTheLinuxRowIsABrokenContract()
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = true;
        Assert.True(c.Prepare(TargetProfile.Linux.Target));
        c.Kotonoha.AddSource(new SourceDocument("Tests.kimi", "#Test\nfunc sample() => $expect(true)"));
        c.Bind();
        c.Binding.CheckTestStartup();
        Assert.True(c.Analyze().IsVerified, Describe(c));
        c.Tests.Discover(c);
        Assert.Throws<InvalidOperationException>(() => c.Generate(TextWriter.Null));
    }

    // IMPL 21.6: Kernel's module on the none row defines the platform storage of Board's layout and one adapter per Platform
    // requirement (PlatformRows' records), each calling its witness's function through the entry call: the storage as start's result
    // slot and as every receiver, the loader's boot passed through, write's text a Utf8Slice slot of the template's data and length,
    // terminate's call followed by unreachable. The start is the layer's libc-free form: the global module asm entry, and the start body
    // calling the platform start (the layer's StartCalls) before the startup root.
    [Fact]
    public void KernelOnTheNoneRowDefinesItsPlatformsAdapters()
    {
        var kernel = Analyze(TargetProfile.None, KernelSources.Hello + KernelSources.Board + KernelSources.BoardTerminate, "Kernel.kimi", OutputKind.Application);
        var ir = CompilationTestHelper.WriteIr(kernel);
        Assert.Empty(kernel.Generated.Libraries);
        Assert.Equal(TargetProfile.None.EntrySymbol, kernel.Generated.Entry);
        Assert.Contains("@__kimi_platform = internal global [40 x i8] zeroinitializer, align 8\n", ir, StringComparison.Ordinal);
        const string Call = @"call (\w+) @__kimi_f\d+\(";
        const string Storage = @"ptr[^,()]* @__kimi_platform";
        Assert.Matches(@"define internal void @__kimi_platform_member_start\(ptr %boot\) #0 \{\nentry:\n  " + Call.Replace(@"(\w+)", "void", StringComparison.Ordinal) + Storage + @", ptr[^,()]* %boot\)\n  ret void\n\}\n", ir);
        Assert.Matches(@"define internal ptr @__kimi_platform_member_mapPages\(i64 %bytes\) #0 \{\nentry:\n  %t0 = " + Call + Storage + @", i64[^,()]* %bytes\)\n  ret ptr %t0\n\}\n", ir);
        Assert.Matches(@"define internal i64 @__kimi_platform_member_releasePages\(ptr %pages, i64 %bytes\) #0 \{\nentry:\n  %t0 = " + Call + Storage + @", ptr[^,()]* %pages, i64[^,()]* %bytes\)\n  ret i64 %t0\n\}\n", ir);
        Assert.Matches(
            @"define internal i64 @__kimi_platform_member_write\(i32 %stream, ptr %data, i64 %length\) #0 \{\nentry:\n  %t0 = alloca [^\n]+, align 8\n" +
            @"  store ptr %data, ptr %t0[^\n]*\n  %t1 = getelementptr i8, ptr %t0, i64 8\n  store i64 %length, ptr %t1[^\n]*\n  %t2 = " + Call + Storage +
            @", i32[^,()]* %stream, ptr[^,()]* %t0\)\n  ret i64 %t2\n\}\n",
            ir);
        Assert.Matches(@"define internal i64 @__kimi_platform_member_clock\(\) #0 \{\nentry:\n  %t0 = " + Call + Storage + @"\)\n  ret i64 %t0\n\}\n", ir);
        Assert.Matches(@"define internal void @__kimi_platform_member_terminate\(i32 %status\) noreturn #0 \{\nentry:\n  " + Call.Replace(@"(\w+)", "void", StringComparison.Ordinal) + Storage + @", i32[^,()]* %status\)\n  unreachable\n\}\n", ir);
        Assert.Contains("module asm \".globl __kimi_start\"\n", ir, StringComparison.Ordinal);
        Assert.Matches(@"define internal void @__kimi_start_body\(\) noreturn #0 \{\nentry:\n  call void @__kimi_platform_start\(\)\n  call void @__kimi_f\d+\(\)\n  call void @__kimi_exit\(i32 0\)\n  unreachable\n\}\n", ir);
        Assert.Matches(@"attributes #0 = \{[^}\n]*noredzone", ir);
        foreach (var absent in new[] { "@main(", "_fltused", "dllimport", "PIC Level", "\"probe-stack\"", "declare ptr @__kimi_platform_member_" })
        {
            Assert.DoesNotContain(absent, ir, StringComparison.Ordinal);
        }
    }

    // IMPL 21.6.3: a field-less platform's storage is zero-sized, and its start passes no result slot.
    [Fact]
    public void AFieldlessPlatformsStorageIsZeroSized()
    {
        var ir = CompilationTestHelper.WriteIr(Analyze(TargetProfile.None, KernelSources.Hello + KernelSources.FieldlessBoard, "Kernel.kimi", OutputKind.Application));
        Assert.Contains("@__kimi_platform = internal global [0 x i8] zeroinitializer, align 1\n", ir, StringComparison.Ordinal);
        Assert.Matches(@"define internal void @__kimi_platform_member_start\(ptr %boot\) #0 \{\nentry:\n  call void @__kimi_f\d+\(ptr[^,()]* %boot\)\n  ret void\n\}\n", ir);
    }

    // IMPL 20.3.2, 21.6: a Library for the none row has no platform, so its module declares the six adapters the layer's routines call
    // and defines none, without the platform storage.
    [Fact]
    public void ALibraryOnTheNoneRowDeclaresThePlatformsAdapters()
    {
        var ir = CompilationTestHelper.WriteIr(Analyze(TargetProfile.None, "public group Twice\n    public func twice(x: i64) -> i64 => x + x\n", "Twice.kimi", OutputKind.Library));
        foreach (var row in PlatformRows.Rows)
        {
            using var declaration = new StringWriter();
            LlvmModuleText.WriteDeclaration(declaration, row.Adapter);
            Assert.Contains("\ndeclare " + declaration, ir, StringComparison.Ordinal);
            Assert.DoesNotContain("define internal " + row.Adapter.Result + " @" + row.Adapter.Name + "(", ir, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("@__kimi_platform =", ir, StringComparison.Ordinal);
    }

    // SPEC 22.6, IMPL 21.6: every platform witness is a body, so generation roots each one's Body item. An import has no receiver and
    // takes no struct by value (LibraryImportTargetBindingTest's `(value: S)` row is the by-value `start`'s record): Board's `clock` as a
    // receiver member import is refused at check. A witness body may call an import: a forwarding `start` over a scalar import from a
    // group calls it from Board.start's function, and the module requires its library.
    [Fact]
    public void APlatformWitnessIsABodyThatMayCallAnImport()
    {
        var board = KernelSources.Board + KernelSources.BoardTerminate;
        const string Clock = "    public func clock(self: uniq/Self) -> i64\n        self.ticks += 1\n        return self.ticks\n";
        const string Start = "        let window = boot.directMapOffset\n        return Board.init(window + 0x0100_0000, window + 0x0300_0000, (window + 0xB_8000)@raw/u16)\n";
        Assert.Contains(Clock, board, StringComparison.Ordinal);
        Assert.Contains(Start, board, StringComparison.Ordinal);
        var import = ForeignEmissionTest.AnalyzeAs(
            TargetProfile.None,
            KernelSources.Hello + board.Replace(Clock, "    #LibraryImport(\"codec\", \"codec_clock\")\n    public func clock(self: uniq/Board) -> i64\n", StringComparison.Ordinal),
            "static",
            OutputKind.Application);
        Assert.False(import.Accepts, Describe(import));
        Assert.Equal(["InvalidLibraryImport_Kd"], import.Binding.Issues.Select(static x => x.Code.ToString()));
        var seeded = board.Replace(Start, "        let window = boot.directMapOffset + Native.seed()@usize\n        return Board.init(window, window, window@raw/u16)\n", StringComparison.Ordinal);
        const string Seed = "group Native\n    #LibraryImport(\"codec\", \"codec_seed\")\n    public func seed() -> u64\n";
        var forwarding = ForeignEmissionTest.AnalyzeAs(TargetProfile.None, KernelSources.Hello + seeded + Seed, "static", OutputKind.Application);
        var ir = CompilationTestHelper.WriteIr(forwarding);
        Assert.Equal([("codec", "static")], forwarding.Generated.Libraries);
        var start = Regex.Match(ir, @"define internal void @__kimi_platform_member_start\(ptr %boot\) #0 \{\nentry:\n  call void @(__kimi_f\d+)\(").Groups[1].Value;
        Assert.Matches(@"\ndefine [^\n]*@" + start + @"\([^\n]*\n(?:(?!\})[^\n]*\n)*?[^\n]*call i64 @codec_seed\(\)", ir);
    }

    // SPEC 22.8: a Stopwatch's start and stop read the platform's clock: the program's clock call is the layer's __kimi_clock, whose
    // adapter calls Board.clock's function (the one adding 1 to `ticks`, Board's Field at offset 32).
    [Fact]
    public void AStopwatchReadsTheBoardsClock()
    {
        var source = "var watch = Time.Stopwatch.init()\nwatch.start()\nwatch.stop()\nlet elapsed = watch.elapsed()\n" + KernelSources.Board + KernelSources.BoardTerminate;
        var ir = CompilationTestHelper.WriteIr(Analyze(TargetProfile.None, source, "Kernel.kimi", OutputKind.Application));
        Assert.Matches(@"\ndefine internal [^\n]*@__kimi_f\d+\([^\n]*\n(?:(?!\})[^\n]*\n)*?[^\n]*call i64 @__kimi_clock\(\)", ir);
        var clock = Regex.Match(ir, @"define internal i64 @__kimi_platform_member_clock\(\) #0 \{\nentry:\n  %t0 = call i64 @(__kimi_f\d+)\(").Groups[1].Value;
        Assert.Matches(
            @"\ndefine [^\n]*@" + clock + @"\([^\n]*\n(?:(?!\})[^\n]*\n)*?[^\n]*getelementptr i8, ptr %\w+, i64 32\n(?:(?!\})[^\n]*\n)*?[^\n]*@llvm\.sadd\.with\.overflow\.i64\(i64 %\w+, i64 1\)\n",
            ir);
    }

    // IMPL 21.6.3 (PLAN G1455, O-538 (a)): every run of a platform member holds the busy flag, whoever calls it. Each witness's function,
    // the one every call of the member reaches, ends its entry block with the none layer's enter and passes the enter's value to the leave
    // directly above each of its returns, after the result is computed; terminate's, which never returns, has the enter alone. No other
    // function, the adapters and the start body included, calls the enter. The program's own call of clock calls that function directly.
    [Fact]
    public void EveryRunOfAPlatformMemberHoldsTheBusyFlag()
    {
        const string Own = "var board = Board.init(next: 0, end: 0, screen: (0@usize)@raw/u16)\nlet ticks = board.clock()\n";
        var board = KernelSources.Board.Replace("\n    init(", "\n    public init(", StringComparison.Ordinal);
        var ir = CompilationTestHelper.WriteIr(Analyze(TargetProfile.None, KernelSources.Hello + Own + board + KernelSources.BoardTerminate, "Kernel.kimi", OutputKind.Application));
        var clock = AssertEveryMembersRunIsBracketed(ir)[Array.FindIndex(PlatformRows.Rows, static x => x.Name == "clock")];
        var startup = Regex.Match(ir, @"\n  call void @__kimi_platform_start\(\)\n  call void @(__kimi_f\d+)\(\)\n").Groups[1].Value;
        Assert.Contains("call i64 @" + clock + "(", Body(ir, startup), StringComparison.Ordinal);
    }

    // EveryRunOfAPlatformMemberHoldsTheBusyFlag's twin: a field-less platform's one-line member bodies, over a zero-sized receiver.
    [Fact]
    public void EveryRunOfAFieldlessPlatformsMemberHoldsTheBusyFlag()
        => AssertEveryMembersRunIsBracketed(CompilationTestHelper.WriteIr(Analyze(TargetProfile.None, KernelSources.Hello + KernelSources.FieldlessBoard, "Kernel.kimi", OutputKind.Application)));

    // IMPL 21.6.3: a hosted row publishes no platform, so its generated code brackets no run.
    [Fact]
    public void AHostedProgramBracketsNoRun()
    {
        var ir = CompilationTestHelper.WriteIr(Analyze(KernelSources.Hello));
        Assert.DoesNotContain("call i8 @__kimi_platform_enter(", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("call void @__kimi_platform_leave(", ir, StringComparison.Ordinal);
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
            Targets = [TargetProfile.Windows.Target],
            OutputPath = "bin/Hello.ll",
            Optimization = "O0",
            NativeLibraries = new()
            {
                [TargetProfile.Windows.Target] = new()
                {
                    ["kernel32"] = new() { Name = "kernel32", Kind = "import", Input = "C:/App/clang+llvm-22.1.5-x86_64-pc-windows-msvc/bin/kernel32.lib" },
                },
            },
        };
        var read = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(TinyhandSerializer.SerializeToUtf8(file))!;
        Assert.Equal(file.OutputPath, read.OutputPath);
        Assert.Equal(file.Optimization, read.Optimization);
        Assert.Equal(file.NativeLibraries[TargetProfile.Windows.Target]["kernel32"], read.NativeLibraries[TargetProfile.Windows.Target]["kernel32"]);
    }

    [Fact]
    public void ExampleSettingsCanBeRead()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../docs/examples/Hello/Hello.kimiproj"));
        var read = TinyhandSerializer.DeserializeFromUtf8<ProjectFile>(File.ReadAllBytes(path))!;
        Assert.Equal(TargetProfile.Windows.Target, Assert.Single(read.Targets));
        Assert.Empty(read.NativeLibraries);
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

    // A program analyzed for `output`, an Application unless the caller names another, on the Windows row.
    internal static Compilation Analyze(string source, string path = "Hello.kimi", OutputKind output = OutputKind.Application)
        => Analyze(TargetProfile.Windows, source, path, output);

    // A program analyzed for `output` on a target row.
    internal static Compilation Analyze(TargetProfile row, string source, string path, OutputKind output)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(row.Target));
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        c.Bind();
        c.Binding.CheckStartup(output);
        c.Analyze();
        Assert.Equal(0, c.Binding.IdlessFactSites); // R7-E1: a fact site is a node the check read, so a semantic write numbered it.
        return c;
    }

    // A program checked for `output` as a build checks it (Compilation.Check): every phase run and its records published, on the Windows
    // row.
    internal static Compilation Check(string source, string path = "Hello.kimi", OutputKind output = OutputKind.Application)
        => Check(TargetProfile.Windows, source, path, output);

    // A program checked for `output` on a target row as a build checks it.
    internal static Compilation Check(TargetProfile row, string source, string path, OutputKind output)
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.OutputKind = output;
        Assert.True(c.Prepare(row.Target));
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

    // A module's text between a function's definition line and its closing brace, the brace's line excluded.
    private static string Body(string ir, string function)
    {
        var head = Regex.Match(ir, @"\ndefine [^\n]*@" + function + @"\([^\n]*\{\n");
        Assert.True(head.Success, function);
        var start = head.Index + head.Length;
        return ir[start..(ir.IndexOf("\n}\n", start, StringComparison.Ordinal) + 1)];
    }

    // IMPL 21.6.3: the function each platform adapter calls, in PlatformRows' order, each bracketed: the enter as its entry block's last
    // instruction and the leave of the enter's value as the line directly above each `ret` (none in terminate's); the module holds no other
    // enter call.
    private static string[] AssertEveryMembersRunIsBracketed(string ir)
    {
        var witnesses = new string[PlatformRows.Rows.Length];
        for (var i = 0; i < witnesses.Length; i++)
        {
            var adapter = PlatformRows.Rows[i].Adapter;
            witnesses[i] = Regex.Match(ir, @"\ndefine internal [^\n]* @" + adapter.Name + @"\([^\n]*\n(?:(?!\})[^\n]*\n)*?[^\n]*call \w+ @(__kimi_f\d+)\(").Groups[1].Value;
            var body = Body(ir, witnesses[i]);
            var enter = Regex.Match(body, @"\Aentry:\n(?:  [^\n]*\n)*?  %t(\d+) = call i8 @__kimi_platform_enter\(\)\n  br label %b0\n");
            Assert.True(enter.Success, adapter.Name);
            var leave = "  call void @__kimi_platform_leave(i8 %t" + enter.Groups[1].Value + ")";
            var lines = body.Split('\n');
            var returns = 0;
            for (var j = 1; j < lines.Length; j++)
            {
                if (lines[j].StartsWith("  ret ", StringComparison.Ordinal))
                {
                    Assert.Equal(leave, lines[j - 1]);
                    returns++;
                }
            }

            Assert.Equal(returns, body.Split("  call void @__kimi_platform_leave(").Length - 1);
            Assert.True(adapter.NoReturn == (returns == 0), adapter.Name);
        }

        Assert.Equal(witnesses.Length, Regex.Matches(ir, @"= call i8 @__kimi_platform_enter\(\)").Count);
        return witnesses;
    }
}
