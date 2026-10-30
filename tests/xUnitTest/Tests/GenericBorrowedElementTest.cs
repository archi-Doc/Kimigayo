// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericBorrowedElementTest
{
    private const string At = "func at<length N, T>(a: ref/[N of T], i: isize) -> T\n    T is Copy\n    return a[i]\n";

    [Theory]
    [InlineData("Integer", "i32", "6, 7", "require n == 7 else => $abort(\"value\")")]
    [InlineData("Wide", "u128", "0, 340282366920938463463374607431768211455", "require n == 340282366920938463463374607431768211455 else => $abort(\"wide\")")]
    [InlineData("Tuple", "(i32, bool)", "(6, false), (7, true)", "match n\n    (7, true) => ()\n    (_, _) => $abort(\"tuple\")")]
    [InlineData("Unit", "()", "(), ()", "let unit: () = n")]
    public void Executes(string name, string type, string values, string check)
        => ScalarEmissionTest.EmitFixture("GenericBorrowedElement" + name, At + $"let a: [2 of {type}] = [{values}]\nlet n = at<2, {type}>(a@ref, 1)\n{check}", string.Empty);

    [Theory]
    [InlineData("func at<length N, T>(a: ref/[N of T], i: isize) -> T => a[i]")]
    [InlineData("let a: [1 of string] = [\"owned\"]\nlet n = at<1, string>(a@ref, 0)")]
    public void RejectsUnprovedCopy(string source)
    {
        var c = MinimalEmissionTest.Analyze(source.StartsWith("func", StringComparison.Ordinal) ? source : At + source);
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }

    [Theory]
    [InlineData("Negative", 2, -1)]
    [InlineData("Length", 2, 2)]
    [InlineData("Empty", 0, 0)]
    public void BoundsChecksIncludeZeroSizedElements(string name, int length, int index)
        => ScalarEmissionTest.EmitFixture(
            "GenericBorrowedElementBounds" + name,
            At + $"let a: [{length} of ()] = [{(length == 0 ? string.Empty : "(), ()")}]\nlet n = at<{length}, ()>(a@ref, {index})",
            string.Empty,
            1,
            "Hello.kimi:3:12: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    private static string Program21Source => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/milestones/Milestone21.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void RejectsSafeDereference()
    {
        // SPEC 13.2 and 3.3: `*` dereferences raw pointers only; a safe reference is read through the Copy
        // read where its referent Type is expected, never by `*` (PLAN issue G12).
        var c = MinimalEmissionTest.Analyze("func f(x: ref/i32) -> i32 => *x");
        Assert.False(c.Binding.Result.IsComplete);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(DiagnosticCode.TypeMismatch_Kd, issue.Code);
        Assert.IsType<DereferenceKoto>(issue.Node);
    }

    [Fact]
    public void BindsProgram21()
    {
        // SPEC 3.3 and 13.4: each `inferred == 10` check reads the returned element borrow as its i32 referent;
        // the whole Milestone 21 source binds: inferred length/Type/Origin, ordinary defaults and dependent forwarding.
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", Program21Source));
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code} ({x.Node.GetType().Name}): {x.Node}")));
    }

    [Fact]
    public void ExecutesProgram21()
    {
        // The unchanged Milestone 21 source (inferred length/Type/Origin, ordinary defaults evaluated
        // once per omission, dependent forwarding) analyzes, lowers per instance and runs natively.
        var c = MinimalEmissionTest.Analyze(Program21Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.True(module.IsComplete);
        ScalarEmissionTest.EmitFixture(
            "GenericBorrowedElementProgram21",
            Program21Source,
            "Default index evaluated.\nInferred selection is 10.\nExplicit selection is 30.\nDefault index evaluated.\nForwarded selection is 10.\nDefault index evaluated.\nOrdinary selection is 4.\nDefault index evaluated.\nLocal selection is 7.\nInference and defaults finished.\n");
    }

    [Fact]
    public void MonomorphizesAggregateElementReads()
    {
        // SPEC 21.3.1: the (i32, bool) instance copies the proved-Copy element from the borrowed array's
        // element storage instead of keeping the shared entry.
        var c = MinimalEmissionTest.Analyze(At + "let a: [2 of (i32, bool)] = [(6, false), (7, true)]\nlet n = at<2, (i32, bool)>(a@ref, 1)");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.True(module.IsComplete);
        var instance = Assert.Single(GenericStorageEmissionTest.Instances(module));
        Assert.Contains(instance.Instructions, x => x.Opcode == EmissionOpcode.Sequence && x.ScalarOperator == "ArrayStorageRead");
        Assert.Contains(instance.Instructions, x => x.Opcode == EmissionOpcode.TransferAggregate);
    }
}
