// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Testing;
using Xunit;

namespace XunitTest;

// The test rows of MIR code generation (R5 C10a; SPEC 17.6, 20.6; DECISIONS R5-M136): a verification's observation, message and Abort and
// the temporary directory through the test runtime's routines, a verification site's number from the test catalog, a compared value held
// by the test runtime until its observation, and a test build's entry, which calls each test in catalog order through the one call writer;
// the phases the test runtime reports (R5 C10b; PD22; DECISIONS R5-M137, R5-M140): a defer body and a destroying Drop in the cleanup phase,
// a static's initializer in the initialization phase, a product build without phases.
public sealed partial class FunctionCodegenTest
{
    // A test build: a defer with a verification beside a body's, a Scope Exit and an assignment's destruction and a moved value, a defer
    // whose body destroys and compares, a $require in a defer, and a first-access static beside a folded one.
    private const string Phases =
        "func tpMake() -> string => \"x\"\nfunc tpTake(text: string) => ()\n" +
        "group TpValues\n    public let limit: i32 = TpValues.seed() + 1\n    public let fixed: i32 = 3\n    func seed() -> i32 => 4\n" +
        "#Test\nfunc tpDefer()\n    defer => $expect(false, message: \"cleanup\")\n    $expect(true)\n" +
        "#Test\nfunc tpDrops()\n    var s = tpMake()\n    s = tpMake()\n    let moved = tpMake()\n    tpTake(moved@move)\n    Console.writeLine(s)\n" +
        "#Test\nfunc tpNested()\n    let t = tpMake()\n    defer\n        let u = tpMake()\n        $expect(u == t)\n" +
        "#Test\nfunc tpStops()\n    defer => $require(false)\n" +
        "#Test\nfunc tpStatics() => $expect(TpValues.limit + TpValues.fixed == 8)\n";

    // A test build: comparisons of an i32 variable and a constant, a call's result, an f64, a bool and an i128, and the temporary directory
    // called, called through its Item and erased to a Function Type.
    private const string Verifications =
        "func tvHelper(x: i32) -> i32 => x + 1\n" +
        "#Test\nfunc tvCompare()\n    var value = 7\n    $expect(value == 8, message: \"comparison\")\n    $require(tvHelper(1) > 0)\n" +
        "#Test\nfunc tvKinds()\n    let f = 1.5\n    let b = true\n    let w: i128 = 5\n    $expect(f == 2.5)\n    $expect(b == false)\n" +
        "    $expect(w != 5)\n" +
        "#Test\nfunc tvTemp()\n    Console.writeLine(Test.tempDirectory())\n" +
        "#Test\nfunc tvItem()\n    let make = Test.tempDirectory\n    Console.writeLine(make())\n    Console.writeLine(tvRun(Test.tempDirectory))\n" +
        "func tvRun(make: () -> string) -> string => make()\n";

    // SPEC 17.6.2-17.6.3 (DECISIONS R5-M136): each Scalar operand of a verification's comparison is held (its side, kind 2 and width 32 for
    // i32, its bits zero-extended) before the observation, which takes the condition and its site's number in the catalog; the message
    // takes the issue and the text's address; $require Aborts the case at its site, and nothing follows (fail-before: one located
    // Unsupported_Kd at each test intrinsic).
    [Fact]
    public void AVerificationCallsTheTestRuntime()
    {
        var c = Analyze(Verifications);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "tvCompare"));
        var calls = CallsOf(function);
        var values = calls.Where(x => x.Callee == ir.Function(RuntimeAbi.TestValue.Name)).ToArray();
        Assert.Equal(
            ["call void @(i32 0, i16 2, i16 32, i128 %)", "call void @(i32 0, i16 2, i16 32, i128 %)", "call void @(i32 1, i16 2, i16 32, i128 %)",
                "call void @(i32 1, i16 2, i16 32, i128 %)"],
            values.Select(static x => x.Shape).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["zext i32 % to i128", "zext i32 % to i128", "zext i32 0 to i128", "zext i32 8 to i128"],
            values.Select(static x => Assert.Single(x.Operands).Shape).Order(StringComparer.Ordinal));
        Assert.Equal(
            [$"call i64 @(i1 %, i32 {SiteOf(c, "value == 8")})", $"call i64 @(i1 %, i32 {SiteOf(c, "tvHelper(1) > 0")})"],
            calls.Where(x => x.Callee == ir.Function(RuntimeAbi.TestObserve.Name)).Select(static x => x.Shape));
        Assert.Equal("call void @(i64 %, ptr %)", Assert.Single(calls, x => x.Callee == ir.Function(RuntimeAbi.TestMessage.Name)).Shape);
        var abort = Assert.Single(calls, x => x.Callee == ir.Function(RuntimeAbi.TestRequireAbort.Name));
        Assert.Equal($"call void @(i64 %, i32 {SiteOf(c, "tvHelper(1) > 0")})", abort.Shape);
        Assert.Equal("unreachable", Assert.Single(function.Blocks, x => x.Instructions.Contains(abort)).Terminator.Shape);
        Assert.Equal(
            [2, 4],
            calls.Where(x => x.Callee == ir.Function(RuntimeAbi.TestObserve.Name)).Select(x => calls.TakeWhile(y => y != x).Count(values.Contains)));
    }

    // SPEC 17.6.3 (DECISIONS R5-M136): a held value's frame kind and width are its Type's (an f64's 4 and 64, its bits reinterpreted as an
    // integer before the zero extension; a bool's 1 and 1; an i128's 2 and 128, its bits as they are).
    [Fact]
    public void AHeldValueIsItsTypesKindWidthAndBits()
    {
        var c = Analyze(Verifications);
        var ir = GeneratedIr.Parse(Generate(c));
        var values = CallsOf(Assert.Single(FunctionsOf(ir, c, "tvKinds"))).Where(x => x.Callee == ir.Function(RuntimeAbi.TestValue.Name)).ToArray();
        Assert.Equal(
            ["call void @(i32 0, i16 1, i16 1, i128 %)", "call void @(i32 0, i16 2, i16 128, i128 %)", "call void @(i32 0, i16 4, i16 64, i128 %)",
                "call void @(i32 1, i16 1, i16 1, i128 %)", "call void @(i32 1, i16 2, i16 128, i128 5)", "call void @(i32 1, i16 4, i16 64, i128 %)"],
            values.Select(static x => x.Shape).Order(StringComparer.Ordinal));
        var floating = Bits(values, "i16 4,");
        Assert.All(floating, static x => Assert.Equal("zext i64 % to i128", x.Shape));
        Assert.Equal(
            ["bitcast double % to i64", "bitcast double 0x4004000000000000 to i64"],
            floating.Select(static x => Assert.Single(x.Operands).Shape).Order(StringComparer.Ordinal));
        Assert.All(Bits(values, "i16 1,"), static x => Assert.StartsWith("zext i1 ", x.Shape, StringComparison.Ordinal));
    }

    // The test profile's temporary directory (DECISIONS R5-M136): its call, its Item's Value call and its Item erased to a Function Type
    // (the adapter's entry) each call the test runtime's routine, the destination its result slot. The Item's function has no Type
    // parameter, and its row reads none (fail-before: the collector read the Item's substitution's Type argument, which is not there, and
    // the check ended with CheckFaulted_Kd).
    [Fact]
    public void TheTemporaryDirectoryIsItsRoutineWhereverItIsCalled()
    {
        var c = Analyze(Verifications);
        var ir = GeneratedIr.Parse(Generate(c));
        var routine = ir.Function(RuntimeAbi.TestTempDirectory.Name);
        Assert.Equal(["call void @(ptr %)"], CallsOf(Assert.Single(FunctionsOf(ir, c, "tvTemp"))).Where(x => x.Callee == routine).Select(static x => x.Shape));
        Assert.Equal(["call void @(ptr %)"], CallsOf(Assert.Single(FunctionsOf(ir, c, "tvItem"))).Where(x => x.Callee == routine).Select(static x => x.Shape));
        var adapter = Assert.Single(ir.Functions, x => CallsOf(x).Any(y => y.Callee == routine) && x.Signature != "internal void ()");
        Assert.Equal(["call void @(ptr %)"], CallsOf(adapter).Where(x => x.Callee == routine).Select(static x => x.Shape));
        Assert.Empty(Records(c));
    }

    // SPEC 20.6.2 (DECISIONS R5-M136): a site's number is the catalog's, by file, line and column, not the order the code holds the sites
    // in: the body's verification before the deferred one of the same test, and the tests of a later file before an earlier file's.
    [Fact]
    public void ASitesNumberIsTheCatalogs()
    {
        var c = TestBuild(("Zeta.kimi", "#Test\nfunc tvLate()\n    defer => $expect(1 == 2)\n    $expect(3 == 4)\n"), ("Alpha.kimi", "#Test\nfunc tvEarly() => $expect(5 == 6)\n"));
        c.Tests.Discover(c);
        var ir = GeneratedIr.Parse(Generate(c));
        var observe = ir.Function(RuntimeAbi.TestObserve.Name);
        Assert.Equal([0, 1, 2], new[] { "5 == 6", "1 == 2", "3 == 4" }.Select(x => SiteOf(c, x)));
        Assert.Equal(
            ["call i64 @(i1 %, i32 2)", "call i64 @(i1 %, i32 1)"],
            CallsOf(Assert.Single(FunctionsOf(ir, c, "tvLate"))).Where(x => x.Callee == observe).Select(static x => x.Shape));
        Assert.Equal(["call i64 @(i1 %, i32 0)"], CallsOf(Assert.Single(FunctionsOf(ir, c, "tvEarly"))).Where(x => x.Callee == observe).Select(static x => x.Shape));
    }

    // IMPL 21.4.2, SPEC 20.6 (DECISIONS R5-M136): a test build's entry is the test runtime's, which reports the case and runs the selected
    // test, each test's function called in catalog order through the one call writer, under the runtime's test variant (its Aborts report
    // the case's); the module has no Application entry body (fail-before: one located Unsupported_Kd at each test).
    [Fact]
    public void ATestBuildsEntryRunsEachTestInCatalogOrder()
    {
        var c = Analyze(Verifications);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var start = ir.Function("__kimi_start");
        Assert.Equal(ir.Function("__kimi_test_begin"), CallsOf(start)[0].Callee);
        Assert.Equal(
            c.Tests.Cases.Select(x => Assert.Single(FunctionsOf(ir, c, x.Name))),
            CallsOf(start).Select(static x => x.Callee!).Where(static x => x.Signature == "internal void ()"));
        Assert.Contains(ir.Function("__kimi_test_aborted"), CalleesOf(ir.Function(RuntimeAbi.Abort.Name)));
        Assert.DoesNotContain("@" + RuntimeAbi.Entry.Name, text, StringComparison.Ordinal);
        Assert.Empty(Records(c));
    }

    // PLAN G157 (15) (DECISIONS R5-M136): a test build's roots and site numbers are its catalog's, so generation requires the discovery of
    // the version it generates: none, or one before an edit, is a broken contract at the project, and the discovery of the version is
    // generated.
    [Fact]
    public void ATestBuildNeedsTheDiscoveryOfItsVersion()
    {
        var c = TestBuild(("Hello.kimi", Verifications));
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal(["InternalInvariant_Kd"], Records(c).Select(static x => x.Code));
        c.Tests.Discover(c);
        Assert.True(c.Generate(TextWriter.Null));
        c.Kotonoha.AddSource(new SourceDocument("More.kimi", "#Test\nfunc tvMore() => $expect(true)\n", isTestOnly: true));
        Assert.True(Checked(c));
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal(["InternalInvariant_Kd"], Records(c).Select(static x => x.Code));
        c.Tests.Discover(c);
        Assert.True(c.Generate(TextWriter.Null));
    }

    // DECISIONS R5-M136: the test runtime's routines that code generation calls are defined once in its text under their RuntimeAbi
    // signatures, so a call and its definition agree.
    [Fact]
    public void TheTestRuntimesRoutinesAreTheirSignatures()
    {
        foreach (var routine in new[] { RuntimeAbi.TestObserve, RuntimeAbi.TestMessage, RuntimeAbi.TestRequireAbort, RuntimeAbi.TestValue, RuntimeAbi.TestTempDirectory })
        {
            Assert.Single(Regex.Matches(TestRuntime.Text, Regex.Escape(routine.GetDefinition(exported: false))));
        }
    }

    // SPEC 16.1, 17.6.2 (PD22; DECISIONS R5-M137): a defer body runs in Scope Exit, so its verification's observation and message run in
    // the cleanup phase, saved and set before them and restored before the block's branch, and so do its destruction and comparison; a
    // verification of the body runs in the phase the test was called in (fail-before: no phase is written under MIR code generation).
    [Fact]
    public void ADeferBodyRunsInTheCleanupPhase()
    {
        var c = Analyze(Phases);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "tpDefer"));
        var observes = CallsOf(function).Where(x => x.Callee == ir.Function(RuntimeAbi.TestObserve.Name)).ToArray();
        Assert.Equal(
            [(int)TestRuntime.Phase.Body, (int)TestRuntime.Phase.Cleanup],
            observes.Select(x => PhaseOf(function, x)));
        var message = Assert.Single(CallsOf(function), x => x.Callee == ir.Function(RuntimeAbi.TestMessage.Name));
        Assert.Equal((int)TestRuntime.Phase.Cleanup, PhaseOf(function, message));
        Assert.All(Windows(function), x => Assert.Equal("br label %", NextOf(function, x.Restore!).Shape));
        var nested = Assert.Single(FunctionsOf(ir, c, "tpNested"));
        var calls = CallsOf(nested); // t's creation, then the defer body's creation, comparison and observation and both destructions.
        Assert.Equal((int)TestRuntime.Phase.Body, PhaseOf(nested, calls[0]));
        Assert.All(calls.Skip(1), x => Assert.Equal((int)TestRuntime.Phase.Cleanup, PhaseOf(nested, x)));
    }

    // SPEC 16.2.1, 13.7.1 (DECISIONS R5-M137, R5-M140): a Drop whose A2 row destroys runs in the cleanup phase, a Scope Exit's and an
    // assignment's old value's alike; a moved value's Drop (Elide) writes nothing, so no phase either.
    [Fact]
    public void ADestroyingDropRunsInTheCleanupPhase()
    {
        var c = Analyze(Phases);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "tpDrops"));
        var destroys = CallsOf(function).Where(static x => x.Callee!.Signature == "internal void (ptr, ptr, i64)").ToArray();
        Assert.Equal(2, destroys.Length); // s's old value and s; moved's Drop is elided.
        Assert.All(destroys, x => Assert.Equal((int)TestRuntime.Phase.Cleanup, PhaseOf(function, x)));
        Assert.Equal(2, Windows(function).Length);
        Assert.All(CallsOf(function).Except(destroys), x => Assert.Equal((int)TestRuntime.Phase.Body, PhaseOf(function, x)));
    }

    // SPEC 17.4.3, 17.6.2 (DECISIONS R5-M137): a $require in a defer Aborts the case in the cleanup phase; nothing follows the Abort, so the
    // phase is not restored.
    [Fact]
    public void ARequireInADeferAbortsInTheCleanupPhase()
    {
        var c = Analyze(Phases);
        var ir = GeneratedIr.Parse(Generate(c));
        var function = Assert.Single(FunctionsOf(ir, c, "tpStops"));
        var abort = Assert.Single(CallsOf(function), x => x.Callee == ir.Function(RuntimeAbi.TestRequireAbort.Name));
        Assert.Equal((int)TestRuntime.Phase.Cleanup, PhaseOf(function, abort));
        Assert.Equal("unreachable", NextOf(function, abort).Shape);
        var block = function.Blocks.Single(x => x.Instructions.Contains(abort));
        Assert.Null(Assert.Single(Windows(function), x => block.Instructions.Contains(x.Save)).Restore);
    }

    // SPEC 22.4.3 (DECISIONS R5-M137, R5-M140): a static's ensure function runs its initializer in the initialization phase, between its
    // Initializing and Initialized states; a folded static runs no initializer, so it writes none.
    [Fact]
    public void AStaticInitializerRunsInTheInitializationPhase()
    {
        var c = Analyze(Phases);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var ensure = ir.Function(Assert.Single(Regex.Matches(text, @"define internal void @(__kimi_static\d+_ensure)\(")).Groups[1].Value);
        Assert.Equal(
            ["load i8, ptr @, align 1", "icmp ne i8 %, 0", "br i1 %, label %, label %", "call void @(i32 17, ptr %, i64 %, i64 -2)", "unreachable",
                "store i8 1, ptr @, align 1", "load i32, ptr @", "store i32 3, ptr @", "call i32 @()", "store i32 %, ptr @, align 4",
                "store i32 %, ptr @", "store i8 2, ptr @, align 1", "ret void"],
            ensure.Instructions.Select(static x => x.Shape));
        Assert.Equal([ensure.Instructions[6]], ensure.Instructions[10].Operands);
    }

    // PD22 (DECISIONS R5-M137): only a test build has phases; a product build's defer, destruction and static initializer write none.
    [Fact]
    public void AProductBuildWritesNoPhase()
    {
        var text = Generate(
            "func tpMake() -> string => \"x\"\ngroup TpValues\n    public let limit: i32 = TpValues.seed() + 1\n    func seed() -> i32 => 4\n" +
            "var s = tpMake()\ns = tpMake()\ndefer => Console.writeLine(s)\nlet n = TpValues.limit + 1\nConsole.writeLine(s)\n");
        Assert.DoesNotContain("__kimi_test_phase", text, StringComparison.Ordinal);
        Assert.All(GeneratedIr.Parse(text).Functions, static x => Assert.Empty(Windows(x)));
    }

    // The phase a function's instruction runs in (TestRuntime.WritePhase, RestorePhase): the phase set by the window around it, from its
    // save to its restore in text order, or to the end of its block when none restores it; else the phase its caller is in, Body here.
    private static int PhaseOf(IrFunction function, IrValue instruction)
    {
        var at = function.Instructions.IndexOf(instruction);
        foreach (var (save, phase, restore) in Windows(function))
        {
            var end = restore ?? function.Blocks.Single(x => x.Instructions.Contains(save)).Terminator;
            if (function.Instructions.IndexOf(save) < at && at < function.Instructions.IndexOf(end))
            {
                return phase;
            }
        }

        return (int)TestRuntime.Phase.Body;
    }

    // A function's phase windows in text order: the phase's save (an unaligned i32 load), the phase set right after it, and the restore of the
    // saved value (an unaligned store of it), or null.
    private static (IrValue Save, int Phase, IrValue? Restore)[] Windows(IrFunction function)
    {
        var instructions = function.Instructions;
        return [.. Enumerable.Range(0, Math.Max(instructions.Count - 1, 0))
            .Where(x => instructions[x].Shape == "load i32, ptr @" && Regex.IsMatch(instructions[x + 1].Shape, @"^store i32 \d, ptr @$"))
            .Select(x => (
                instructions[x],
                int.Parse(instructions[x + 1].Shape["store i32 ".Length..^", ptr @".Length], System.Globalization.CultureInfo.InvariantCulture),
                instructions.Skip(x + 2).FirstOrDefault(y => y.Shape == "store i32 %, ptr @" && y.Operands.SequenceEqual([instructions[x]]))))];
    }

    // The instruction after one in its function's text.
    private static IrValue NextOf(IrFunction function, IrValue instruction) => function.Instructions[function.Instructions.IndexOf(instruction) + 1];

    // A test build of test-only sources analyzed and accepted, its catalog not discovered yet.
    private static Compilation TestBuild(params (string File, string Source)[] sources)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = true;
        Assert.True(c.Prepare(WindowsProfile.Target));
        foreach (var (file, source) in sources)
        {
            c.Kotonoha.AddSource(new SourceDocument(file, source, isTestOnly: true));
        }

        Assert.True(Checked(c), MinimalEmissionTest.Describe(c, null));
        return c;
    }

    // Whether a test build's check accepts it.
    private static bool Checked(Compilation c) => c.Bind().IsComplete && c.Binding.CheckTestStartup().IsComplete && c.Analyze().IsVerified;

    // A verification site's number in the catalog, by its condition's text.
    private static int SiteOf(Compilation c, string condition) => Assert.Single(c.Tests.Sites, x => x.Expression == condition).Id;

    // The bits operands of the held values of a kind and width (a shape's part, such as "i16 4,").
    private static IrValue[] Bits(IrValue[] values, string kind)
        => [.. values.Where(x => x.Shape.Contains(kind, StringComparison.Ordinal)).Select(static x => Assert.Single(x.Operands))];
}
