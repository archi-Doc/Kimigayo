// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Testing;
using Xunit;

namespace XunitTest;

// The test rows of MIR code generation (R5 C10a; SPEC 17.6, 20.6; DECISIONS R5-M136): a verification's observation, message and Abort and
// the temporary directory through the test runtime's routines, a verification site's number from the test catalog, a compared value held
// by the test runtime until its observation, and a test build's entry, which calls each test in catalog order through the one call writer.
public sealed partial class FunctionCodegenTest
{
    // A test build: comparisons of an i32 variable and a constant, a call's result, an f64, a bool and an i128, and the temporary directory.
    private const string Verifications =
        "func tvHelper(x: i32) -> i32 => x + 1\n" +
        "#Test\nfunc tvCompare()\n    var value = 7\n    $expect(value == 8, message: \"comparison\")\n    $require(tvHelper(1) > 0)\n" +
        "#Test\nfunc tvKinds()\n    let f = 1.5\n    let b = true\n    let w: i128 = 5\n    $expect(f == 2.5)\n    $expect(b == false)\n" +
        "    $expect(w != 5)\n" +
        "#Test\nfunc tvTemp()\n    Console.writeLine(Test.tempDirectory())\n";

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
    // integer before the zero extension; a bool's 1 and 1; an i128's 2 and 128, its bits as they are), and the temporary directory is the
    // test runtime's routine with the destination as its result slot.
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
        var temp = CallsOf(Assert.Single(FunctionsOf(ir, c, "tvTemp")));
        var temporary = Assert.Single(temp, x => x.Callee == ir.Function(RuntimeAbi.TestTempDirectory.Name));
        Assert.Equal("call void @(ptr %)", temporary.Shape);
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
        var c = Compilation.CreateForTest();
        (c.Pipeline, c.Codegen, c.IsTestBuild) = (CompilerPipeline.Mir, CompilerCodegen.Mir, true);
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Verifications, isTestOnly: true));
        Assert.True(Accepted(c));
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal(["InternalInvariant_Kd"], Records(c).Select(static x => x.Code));
        c.Tests.Discover(c);
        Assert.True(c.Generate(TextWriter.Null));
        c.Kotonoha.AddSource(new SourceDocument("More.kimi", "#Test\nfunc tvMore() => $expect(true)\n", isTestOnly: true));
        Assert.True(Accepted(c));
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal(["InternalInvariant_Kd"], Records(c).Select(static x => x.Code));
        c.Tests.Discover(c);
        Assert.True(c.Generate(TextWriter.Null));

        static bool Accepted(Compilation c) => c.Bind().IsComplete && c.Binding.CheckTestStartup().IsComplete && c.Analyze().IsVerified;
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

    // A verification site's number in the catalog, by its condition's text.
    private static int SiteOf(Compilation c, string condition) => Assert.Single(c.Tests.Sites, x => x.Expression == condition).Id;

    // The bits operands of the held values of a kind and width (a shape's part, such as "i16 4,").
    private static IrValue[] Bits(IrValue[] values, string kind)
        => [.. values.Where(x => x.Shape.Contains(kind, StringComparison.Ordinal)).Select(static x => Assert.Single(x.Operands))];
}
