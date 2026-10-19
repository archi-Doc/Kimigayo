// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The frame of MIR code generation (R5 C0b1; package C 3.2, 3.3 and 3.9; DECISIONS R5-M65): <c>ModuleCodegen</c> writes every
/// item of the checked set and the output form, <c>FunctionCodegen</c> a body item's prologue, slots, runtime-reached blocks and the forms
/// the frame covers, and a form not covered yet is one located <c>Unsupported_Kd</c> with no module. The <c>Frame*</c> fixtures run under
/// the process's generators (the old one by default, MIR code generation under <c>-Pipeline mir -Codegen mir</c>); the other tests select
/// MIR code generation themselves.</summary>
public sealed class FunctionCodegenTest
{
    private const string Branches =
        "func choose(flag: bool, a: i64, b: i64) -> i64\n    if flag => return a\n    return b\nfunc nested(first: bool, second: bool) -> char\n" +
        "    if first\n        if second => return 'a'\n        return 'b'\n    return 'c'\nlet flag = false\nlet other = true\n" +
        "let x: i32 = if flag => 1 else => 2\nlet y: char = if flag => 'x' else if other => 'y' else => 'z'\n";

    private const string ForLoop =
        "func f(values: ref/Array<i32>) -> i32\n    var total: i32 = 0\n    for value in values\n        total = 1\n    return total\n()";

    private const string Strings =
        "func show(text: ref/string) => Console.writeLine(text)\nfunc greeting() -> string => \"Hi\"\nfunc nothing(flag: bool) -> string => \"\"\n()";

    // Two literals' temporaries, one on a conditional path, each borrowed and destroyed (Keep), and a string never initialized (Elide).
    private const string Drops =
        "Console.writeLine(\"a\")\nlet s: string\nlet flag = true\nif flag\n    Console.writeLine(\"b\")\n";

    // Programs of the frame's forms only: scalar locals and constants, branches, loops and labels, an explicit startup and unused functions
    // with scalar, string-borrow, zero-sized and Never signatures.
    public static TheoryData<string, string> Frames => new()
    {
        {
            "FrameConstants",
            "let flag = true\nlet off = false\nlet small: u8 = 255\nlet big: u64 = 18446744073709551615\nlet low: i64 = -7\nlet wide: i128 = -5\n" +
            "let letter = 'k'\nlet nothing = ()\nvar count: i32 = 1\ncount = 2\n"
        },
        { "FrameBranches", Branches },
        {
            "FrameLoops",
            "var running = true\nlabel outer: loop\n    if running\n        running = false\n        continue to outer\n    exit to outer\n" +
            "let found: i32 = loop => exit 5\nvar again = true\nwhile again\n    again = false\n"
        },
        {
            "FrameMain",
            "func identity(flag: bool, value: i32, text: ref/string, unit: ()) -> i32\n    if flag => return value\n    return 0\n" +
            "func stop() -> Never\n    loop\n        let x = 1\npublic func main()\n    var running = true\n    while running\n        running = false\n"
        },
    };

    // The first programs (R5 C0b2, C0c): console output through writeLine, one line, several lines, an empty line and non-ASCII text: the
    // literal, its borrow, the call and the temporary's destruction (DECISIONS R5-M73, R5-M78).
    public static TheoryData<string, string, string> Hellos => new()
    {
        { "HelloWorld", "Console.writeLine(\"Hello, world!\")\n", "Hello, world!\n" },
        { "HelloLines", "Console.writeLine(\"one\")\nConsole.writeLine(\"\")\nConsole.writeLine(\"日本語\")\n", "one\n\n日本語\n" },
    };

    [Theory]
    [MemberData(nameof(Frames))]
    public void FrameProgramsRun(string name, string source)
        => ScalarEmissionTest.EmitFixture(name, source, string.Empty);

    [Theory]
    [MemberData(nameof(Hellos))]
    public void HelloProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // The console writeLine is a compiler function's legacy runtime row (RuntimeAbi.WriteLine): the routine is called with the string borrow
    // loaded from its local and the call site's location text (SPEC 22.5.1), a header constant. A string constant is stored whole into its
    // local, its bytes a header constant and its release kind Static (IMPL 21.1.3); the empty string has no bytes.
    [Fact]
    public void WriteLineCallsTheConsoleRuntimeRow()
    {
        var text = Generate(Strings);
        var ir = GeneratedIr.Parse(text);
        var call = Assert.Single(ir.Single("internal void (ptr noundef nonnull align 8 dereferenceable(24))").Instructions, static x => x.Opcode == "call");
        Assert.Same(ir.Function("__kimi_write_line"), call.Callee);
        Assert.Equal("call void @(ptr noundef nonnull align 8 dereferenceable(24) %, ptr @, i64 15)", call.Shape);
        Assert.Equal("load ptr, ptr %, align 8", Assert.Single(call.Operands).Shape);
        Assert.Contains(Constant("Hello.kimi:1:32"), text);
        var greeting = ir.Single("internal void (ptr)");
        var store = Assert.Single(greeting.Instructions, static x => x.Opcode == "store");
        Assert.Equal(("store %kimi.string { ptr @, i64 2, i8 0 }, ptr %, align 8", greeting.Parameters[0]), (store.Shape, Assert.Single(store.Operands)));
        Assert.Contains(Constant("Hi"), text);
        Assert.Equal(
            "store %kimi.string { ptr null, i64 0, i8 0 }, ptr %, align 8",
            Assert.Single(ir.Single("internal void (ptr, i1)").Instructions, static x => x.Opcode == "store" && x.Shape.Contains("kimi.string")).Shape);
    }

    // DECISIONS R5-M78, R4-M6a: a Ref stores its local's address; a Keep Drop calls the dropped Type's glue once, on the Place's address, and
    // an Elide Drop nothing, so each string a path initializes is destroyed exactly once and the one never initialized is not; the string's
    // glue item calls the runtime release with its own parameters (PD18).
    [Fact]
    public void EachInitializedStringIsDestroyedOnce()
    {
        var ir = GeneratedIr.Parse(Generate(Drops));
        var body = ir.Startup;
        var strings = body.Instructions.Where(static x => x.Shape == "alloca %kimi.string, align 8").ToArray();
        var literals = body.Instructions.Where(static x => x.Opcode == "store" && x.Shape.StartsWith("store %kimi.string", StringComparison.Ordinal))
            .Select(static x => Assert.Single(x.Operands)).ToArray();
        var borrows = body.Instructions.Where(static x => x.Shape == "store ptr %, ptr %, align 8").Select(static x => x.Operands[0]).ToArray();
        var writes = body.Instructions.Where(x => x.Opcode == "call" && x.Callee == ir.Function("__kimi_write_line")).ToArray();
        var drops = body.Instructions.Where(x => x.Opcode == "call" && x.Callee != ir.Function("__kimi_write_line")).ToArray();
        Assert.Equal(3, strings.Length);
        Assert.Equal(2, literals.Distinct().Count());
        Assert.Equal(literals, borrows);
        Assert.Equal(2, writes.Length);
        Assert.Equal(literals, drops.Select(static x => x.Operands[0]));
        Assert.All(drops, static x => Assert.Equal("call void @(ptr %, ptr @, i64 15)", x.Shape));
        var glue = Assert.Single(drops.Select(static x => x.Callee).Distinct())!;
        Assert.Equal(["call void @(ptr %, ptr %, i64 %)", "ret void"], glue.Instructions.Select(static x => x.Shape));
        Assert.Same(ir.Function("__kimi_destroy_string"), glue.Instructions[0].Callee);
        Assert.Equal(glue.Parameters, glue.Instructions[0].Operands);
    }

    // Gate step 2's stand-in (DECISIONS R5-M78): A2's verdicts are exact only for programs A1 accepts (R4-M6a), and under sel the old analysis
    // decides acceptance, so a program whose MIR analyses recorded an Ownership problem writes no Drop: each is one located Unsupported_Kd; a
    // control-flow problem (A3) does not gate. No program the old analysis accepts and the frame covers holds such a problem yet (ledger row
    // a1-whole-incomplete needs a fixed array, and the arena-skipped bodies are rejected first), so the problem is added to the version's
    // report.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AnOwnershipProblemOfTheProgramLeavesItsDropsUnwritten(bool ownership, bool written)
    {
        var c = Analyze("Console.writeLine(\"a\")\n");
        var program = c.BuildMir();
        var requirement = ownership ? DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse) : DiagnosticRequirement.ControlFlow;
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, requirement, program.Bodies[0].Source, 1));
        using var writer = new StringWriter();
        Assert.Equal(written, c.Generate(writer));
        Assert.Equal(written ? [] : [("Unsupported_Kd", (string?)"\"a\"")], Records(c));
    }

    // PD12: in a program the stand-in passes, A1 and A2 judged every runtime Drop, so one without a verdict group is a broken contract
    // (InternalInvariant_Kd at the Drop); the program's one drop row is moved to another block to make it so.
    [Fact]
    public void ARuntimeDropWithoutAVerdictIsABrokenContract()
    {
        var c = Analyze("Console.writeLine(\"a\")\n");
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl: null });
        var first = program.Bodies.ToArray().Take(body.Ordinal).Sum(x => program.Drops.RowsOf(x.Ordinal).Length);
        var row = Assert.Single(program.Drops.RowsOf(body.Ordinal).ToArray());
        program.Drops.Rows[first] = row with { Block = body.Blocks.Length };
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("InternalInvariant_Kd", (string?)"\"a\"")], Records(c));
    }

    // I2 (IMPL 21.1.3): one aligned slot per local of nonzero size, in local order, bool stored as i8 and never as i1, none for a zero-sized
    // local; an integer constant is its value's two's complement at its width, stored into its local's slot (GeneratedIrTest covers the
    // entry and the profile attributes for both generators).
    [Fact]
    public void EachLocalOfNonzeroSizeHasOneSlot()
    {
        var text = Generate(
            "let flag = true\nlet n: i32 = 7\nlet nothing = ()\nlet small: u8 = 255\nlet big: u64 = 18446744073709551615\nlet wide: i128 = -5\n");
        var ir = GeneratedIr.Parse(text);
        var body = ir.Startup;
        var slots = body.Instructions.Where(static x => x.Opcode == "alloca").ToArray();
        Assert.Equal(
            ["alloca i8, align 1", "alloca i32, align 4", "alloca i8, align 1", "alloca i64, align 8", "alloca i128, align 16"],
            slots.Select(static x => x.Shape));
        Assert.Equal(
            [
                ("store i8 1, ptr %, align 1", slots[0]), ("store i32 7, ptr %, align 4", slots[1]), ("store i8 -1, ptr %, align 1", slots[2]),
                ("store i64 -1, ptr %, align 8", slots[3]), ("store i128 -5, ptr %, align 16", slots[4]),
            ],
            body.Instructions.Where(static x => x.Opcode == "store").Select(static x => (x.Shape, x.Operands[^1])));
        Assert.DoesNotMatch(@"store i1\b", text);
    }

    // I2: a Value argument is stored into its local's slot (bool widened), a pointer-passed argument is its local's home, a zero-sized one has
    // no parameter; a bool result is narrowed to be returned. I1: values %t<n>, slots %l<local> and arguments %a<slot> are numbered per
    // function.
    [Fact]
    public void ArgumentsLiveInTheirLocalsHomes()
    {
        var text = Generate("func f(flag: bool, n: i64, text: ref/string, unit: ()) -> bool => flag\nfunc g(pair: (i32, i64)) => ()\n()");
        Assert.Contains("  %t0 = zext i1 %a0 to i8\n  store i8 %t0, ptr %l1, align 1\n  store i64 %a1, ptr %l2, align 8\n", text);
        var ir = GeneratedIr.Parse(text);
        var f = ir.Single("internal i1 (i1, i64, ptr noundef nonnull align 8 dereferenceable(24))");
        var entry = f.Blocks[0].Instructions;
        Assert.Equal(
            [
                "alloca i8, align 1", "alloca i8, align 1", "alloca i64, align 8", "alloca ptr, align 8", "zext i1 % to i8", "store i8 %, ptr %, align 1",
                "store i64 %, ptr %, align 8", "store ptr %, ptr %, align 8", "br label %",
            ],
            entry.Select(static x => x.Shape));
        Assert.Equal([f.Parameters[0]], entry[4].Operands);
        Assert.Equal([entry[4], entry[1]], entry[5].Operands);
        Assert.Equal([f.Parameters[1], entry[2]], entry[6].Operands);
        Assert.Equal([f.Parameters[2], entry[3]], entry[7].Operands);
        Assert.Same(f.Blocks[1], Assert.Single(f.Blocks[0].Successors));
        var result = Assert.Single(f.Instructions, static x => x.Opcode == "ret");
        Assert.Equal("ret i1 %", result.Shape);
        Assert.Equal("trunc i8 % to i1", Assert.Single(result.Operands).Shape);
        Assert.DoesNotContain(ir.Single("internal void (ptr)").Instructions, static x => x.Opcode == "alloca");
    }

    // I3 (SPEC 14.10.3): exactly the blocks a runtime path from the entry reaches are generated; a block only the checks reach is not.
    [Fact]
    public void OnlyRuntimeReachedBlocksAreGenerated()
    {
        var c = Analyze("func f(flag: bool) -> i32\n    if flag\n        return 1\n    return 2\n()");
        var ir = Generate(c);
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var body = c.BuildMir()[new(function.SymbolOf(), MirBodyKind.Fn)]!;
        var reach = new MirReach();
        reach.Clear(body.Blocks.Length);
        reach.Walk(body, 0, checks: false);
        var expected = Enumerable.Range(0, body.Blocks.Length).Where(x => reach[x]).Select(static x => "b" + x).ToArray();
        Assert.NotEqual(body.Blocks.Length, expected.Length);
        Assert.Equal(expected, GeneratedIr.Parse(ir).Single("internal i32 (i1)").Blocks.Skip(1).Select(static x => x.Label));
    }

    // PD11: a form the frame does not cover yet is one Unsupported_Kd in the Emission partition at its location, and no module is written;
    // its valid counterpart is. A string's Move into another local copies a slot Type (C2b's row), at the source; a callee's call is C3a's,
    // at that call, while a generic callee's instance whose Keep of a T instanced at i32 needs no drop records nothing (R5-M78); the
    // Utf8Slice overload of writeLine is another legacy row (C9a), at its call; the records of a for loop's statement and of the siblings
    // its expansion synthesizes with its span ($for.iterator, $for.some, $for.none) are one at that location (DECISIONS R5-M65), which the
    // diagnostics could not order when each was keyed by its node. A static's initializer is C7b's row, at the static.
    [Theory]
    [InlineData("func twice(a: i32) -> i32 => a + a\n()", new[] { "a + a" })]
    [InlineData("func twice(a: i32) -> i32 => a\nlet x = twice(1)\n", new[] { "twice(1)" })]
    [InlineData("let t = \"x\"\nlet u = t@move\n", new[] { "t" })]
    [InlineData("func keep<T>(value: T) => ()\nkeep(1)\n", new[] { "keep(1)" })]
    [InlineData("func name() -> string => \"x\"\nfunc show() => Console.writeLine(name())\n()", new[] { "name()" })]
    [InlineData("func show(text: Text.Utf8Slice) => Console.writeLine(text)\n()", new[] { "Console.writeLine(text)" })]
    [InlineData(ForLoop, new[] { "for value in values\n        total = 1", "value" })]
    [InlineData("group Registry\n    public let limit: i32 = 4\n()", new[] { "let limit: i32 = 4" })]
    [InlineData("func twice(a: i32) -> i32 => a\n()", new string[0])]
    public void AnUncoveredFormIsOneLocatedUnsupportedRecord(string source, string[] sites)
    {
        var c = Analyze(source);
        using var writer = new StringWriter();
        var written = sites.Length == 0;
        Assert.Equal(written, c.Generate(writer));
        Assert.Equal(written, writer.ToString().Length != 0);
        Assert.Equal(sites.Select(static x => ("Unsupported_Kd", (string?)x)), Records(c));
        Assert.All(TestDiagnostics.Of(c), static x => Assert.Equal("Hello.kimi", x.Path));
        Assert.Equal(!written, c.Diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        Assert.Equal(written, c.WriteIr(TextWriter.Null, out var failure));
        Assert.Equal(written, failure is null);
    }

    // I1: the same program gives the same text, in one compilation and in two.
    [Theory]
    [InlineData(Branches)]
    [InlineData(Strings)]
    [InlineData(Drops)]
    public void GenerationIsDeterministic(string source)
    {
        var first = Generate(Analyze(source));
        var c = Analyze(source);
        Assert.Equal(first, Generate(c));
        Assert.Equal(first, Generate(c));
    }

    // Every milestone generates under MIR code generation, or records only coverage rows: never a broken contract.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void TheMilestonesRecordOnlyCoverageRows(string name)
    {
        if (!name.StartsWith("milestone/", StringComparison.Ordinal))
        {
            return;
        }

        var c = Analyze(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "milestones", name["milestone/".Length..] + ".kimi")));
        if (c.Generate(TextWriter.Null))
        {
            Assert.Empty(Records(c));
            return;
        }

        Assert.All(Records(c), static x => Assert.Equal("Unsupported_Kd", x.Code));
    }

    // I11: generating a program again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(Branches)]
    [InlineData(Strings)]
    [InlineData(Drops)]
    public void WarmGenerationAllocatesNothing(string source)
    {
        var c = Analyze(source);
        var written = true;
        var allocated = AllocationMeasurement.Measure(() => written &= c.Generate(TextWriter.Null));
        Assert.True(written);
        Assert.Equal(0, allocated);
    }

    // A program analyzed under MIR code generation (sel).
    private static Compilation Analyze(string source)
    {
        var c = Compilation.CreateForTest();
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Mir;
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        return c;
    }

    private static string Generate(string source) => Generate(Analyze(source));

    private static string Generate(Compilation c)
    {
        using var writer = new StringWriter();
        Assert.True(c.Generate(writer), string.Join(", ", Records(c)));
        return writer.ToString();
    }

    // The end of the header's definition of a pooled constant: its UTF-8 bytes, each escaped (LlvmConstantPool).
    private static string Constant(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return $" = private unnamed_addr constant [{bytes.Length} x i8] c\"{string.Concat(bytes.Select(static x => $"\\{x:X2}"))}\", align 1\n";
    }

    private static (string Code, string? Text)[] Records(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => (x.Code, x.Text))];
}
