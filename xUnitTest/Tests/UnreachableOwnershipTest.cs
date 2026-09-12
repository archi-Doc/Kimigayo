// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

[TestClass(DisableParallelization = true)]
public class UnreachableOwnershipTest
{
    [Theory]
    [InlineData("func f() -> i32\n    let x = 3\n    return 0\n    return x")]
    [InlineData("func f() -> i32\n    let x: i32\n    return 0\n    x = 3\n    return x")]
    [InlineData("func f() -> i32\n    return 0\n    let x = 3\n    return x")]
    [InlineData("func f() -> i32\n    let x = 3\n    return 0\n    return 1\n    return x")]
    [InlineData("func f()\n    let x = 3\n    return\n    return\n    let y = x")]
    [InlineData("func f()\n    let x = \"s\"\n    return\n    writeLine(x)")]
    [InlineData("func f()\n    var x = \"s\"\n    writeLine(x)\n    return\n    x = \"again\"\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    return\n    let x: i32\n    if c\n        x = 1\n    else\n        x = 2\n    let y = x")]
    [InlineData("func f(c: bool) -> i32\n    let x = \"s\"\n    return 0\n    require c else\n        writeLine(x)\n        return 1\n        let y = 2\n    writeLine(x)\n    return 2")]
    [InlineData("func f(c: bool) -> i32\n    let x = \"s\"\n    return 0\n    require c else\n        return 1\n        writeLine(x)\n    require c else => return 2\n    writeLine(x)\n    return 3")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    return\n    if c\n        writeLine(x)\n        return\n        let y = 1\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    while c\n        return\n        writeLine(x)\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    while c\n        let x = \"s\"\n        continue\n        writeLine(x)")]
    [InlineData("func f()\n    let x = \"s\"\n    loop\n        exit\n        writeLine(x)\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    return\n    while c\n        let x = \"s\"\n        writeLine(x)")]
    [InlineData("func f(c: bool)\n    return\n    let x: i32\n    loop\n        x = 1\n        exit\n    let y = x")]
    [InlineData("func f()\n    return\n    let x = Option<string>.Some(\"s\")\n    let y = x")]
    [InlineData("func f(c: bool)\n    return\n    let x = Option<string>.Some(\"s\")\n    match x\n        .Some(let text) => writeLine(text)\n        .None => ()")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    return\n    match c\n        true\n            writeLine(x)\n            return\n            let y = 1\n        false => ()\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    return\n    let value = if c => 1 else => 2\n    writeLine(x)")]
    [InlineData("func f(c: bool) -> string\n    let x = \"s\"\n    return \"first\"\n    require c else => return x\n    return x")]
    [InlineData("func f(c: bool)\n    return\n    let x = \"s\"\n    while c\n        continue\n        writeLine(x)\n    writeLine(x)")]
    [InlineData("func f()\n    return\n    let x = \"s\"\n    loop\n        exit\n        writeLine(x)\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    return\n    if c\n        return\n        writeLine(x)\n    else if c\n        return\n        writeLine(x)\n    writeLine(x)")]
    [InlineData("func f()\n    let x = \"s\"\n    defer => loop => ()\n    return\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    return\n    let value = match c\n        true\n            yield 1\n            writeLine(x)\n        false => 2\n    writeLine(x)")]
    [InlineData("func f(c: bool)\n    let x = \"s\"\n    return\n    choice: if c\n        yield to choice\n        writeLine(x)\n    writeLine(x)")]
    [InlineData("func f()\n    let x = \"s\"\n    return\n    work: do\n        exit to work\n        writeLine(x)\n    writeLine(x)")]
    [InlineData("func f()\n    return\n    let x = \"s\"\n    defer => writeLine(x)")]
    [InlineData("func f()\n    let x = \"s\"\n    defer => writeLine(x)\n    return\n    let y = 1")]
    public void SupportedCheckingContinuationsVerify(string source)
    {
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
    }

    [Theory]
    [InlineData("func f() -> i32\n    let x: i32\n    return 0\n    return x", OwnershipFailure.UninitializedUse)]
    [InlineData("func f(x: string) -> string\n    return x\n    return x", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    let x = \"s\"\n    return\n    writeLine(x)\n    writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    let x = \"s\"\n    return\n    writeLine(x)\n    return\n    writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    let x = 1\n    return\n    x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("func f()\n    return\n    let x: i32\n    x = 1\n    x = 2", OwnershipFailure.ReassignedLet)]
    [InlineData("func f()\n    let x = \"s\"\n    return\n    writeLine(x)\n    x = \"again\"", OwnershipFailure.ReassignedLet)]
    [InlineData("func f(c: bool)\n    return\n    let x: i32\n    if c\n        x = 1\n    let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("func f(c: bool)\n    return\n    let x = \"s\"\n    if c\n        writeLine(x)\n    writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    return\n    let x = \"s\"\n    while c\n        writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    return\n    let x: i32\n    while c\n        x = 1", OwnershipFailure.ReassignedLet)]
    [InlineData("func f()\n    return\n    let x = Option<string>.Some(\"s\")\n    let y = x\n    let z = x", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    let x = \"s\"\n    defer => writeLine(x)\n    return\n    writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    return\n    let x = \"s\"\n    if false => writeLine(x)\n    writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f(c: bool)\n    return\n    let x: i32\n    while c\n        x = 1\n        exit\n    let y = x", OwnershipFailure.UninitializedUse)]
    [InlineData("func f()\n    let x = \"s\"\n    let result = work: do\n        exit to work: x\n        writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    let x = \"s\"\n    return\n    let value = work: do\n        exit to work: x\n        writeLine(x)", OwnershipFailure.PossiblyMovedUse)]
    [InlineData("func f()\n    return\n    let x = Option<string>.Some(\"s\")\n    match x\n        .Some(let text)\n            yield\n            writeLine(text)\n            writeLine(text)\n        .None => ()", OwnershipFailure.PossiblyMovedUse)]
    public void CheckingUsesOrdinaryOwnershipDiagnostics(string source, OwnershipFailure failure)
    {
        var c = Parse(source);
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Theory]
    [InlineData("func stop() -> Never => stop()\nfunc f() -> i32 => stop()")]
    [InlineData("func stop() -> Never => stop()\nfunc f() -> i32\n    stop()")]
    [InlineData("func f() -> i32 => loop => ()")]
    [InlineData("func f() -> i32\n    loop => ()")]
    [InlineData("func f() -> i32\n    return 0\n    loop => ()")]
    public void NonCompletingBodiesDoNotRequireSyntheticResults(string source)
    {
        var c = Parse(source);
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
    }

    [Theory]
    [InlineData("func stop() -> Never => stop()\nfunc f(x: i32)\n    stop()\n    let y = x")]
    [InlineData("func f(x: i32)\n    loop => ()\n    let y = x")]
    [InlineData("func f(x: i32)\n    return\n    loop => ()\n    let y = x")]
    [InlineData("func f(x: i32)\n    work: do\n        defer => loop => ()\n        exit to work\n    let y = x")]
    [InlineData("func f(x: i32, c: bool)\n    if c => return else => return\n    let y = x")]
    public void UnseededRegionsRetainTheSafetyGate(string source)
    {
        var c = Parse(source);
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.Unsupported);
    }

    [Fact]
    public void CheckingStateDoesNotChangeRuntimeStateOrPlans()
    {
        var c = Parse("func f()\n    var x = \"s\"\n    return\n    x = \"new\"\n    writeLine(x)");
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var body = Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "f");
        var local = Assert.Single(body.Places, x => x.Kind == OwnershipPlaceKind.Local);
        var write = Enumerable.Range(0, body.Operations.Count).Single(i => body.Operations[i] is { Kind: OwnershipOperationKind.Write, Source: BinaryKoto });
        Assert.False(body.IsReachable(write));
        Assert.True(body.HasCheckingState(write));
        Assert.Equal(PlaceState.None, body.GetInputState(write, local.Id));
        Assert.True(body.GetCheckingInputState(write, local.Id).HasFlag(PlaceState.MustInit));
        Assert.Equal(PlacementKind.None, body.Operations[write].Placement);
        Assert.DoesNotContain(body.CleanupSteps, x => x.Operation == write);
        Assert.All(body.CleanupPlans.Where(x => x.Reason == CleanupReason.Replacement), plan => Assert.True(body.IsReachable(body.Edges[plan.Edge].To)));
        var cleanupCount = body.CleanupSteps.Count;
        c.Bind();
        Assert.False(body.IsVerified);
        Assert.False(body.HasCheckingState(write));
        Assert.Equal(PlaceState.None, body.GetCheckingInputState(write, local.Id));
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        Assert.Same(body, Assert.Single(c.Ownership.Bodies, x => x.Function.Name == "f"));
        Assert.Equal(cleanupCount, body.CleanupSteps.Count);
    }

    [Fact]
    public void DuplicateDeferredSourceDiagnosticsArePublishedOnce()
    {
        var c = Parse("func f()\n    let x = \"s\"\n    defer => writeLine(x)\n    writeLine(x)\n    return\n    let y = 1");
        Assert.False(c.Ownership.Analyze().IsVerified);
        c.Ownership.ReportDiagnostics();
        c.Ownership.ReportDiagnostics();
        Assert.Single(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(128)]
    public void WarmCheckingAndBindingAllocateNothing(int count)
    {
        var source = new StringBuilder("func f(c: bool)\n    return\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("    var s").Append(i).Append(" = \"s\"\n    if c\n        writeLine(s").Append(i).Append(")\n    return\n");
        }

        var c = Parse(source.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified, Describe(c));
        var bindingBytes = AllocationMeasurement.Measure(() => c.Bind());
        var analysisBytes = AllocationMeasurement.Measure(() => c.Ownership.Analyze());
        var combinedBytes = AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            c.Ownership.Analyze();
        });
        Assert.True(
            bindingBytes == 0 && analysisBytes == 0 && combinedBytes == 0,
            $"Binding: {bindingBytes}; analysis: {analysisBytes}; combined: {combinedBytes}");
        Assert.True(c.Ownership.Result.IsVerified, Describe(c));
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        return c;
    }

    private static string Describe(Compilation c)
        => string.Join("\n", c.Ownership.Issues.Select(x => $"{x.Failure}: {x.Source}")) +
            string.Join("\n", c.Ownership.ControlFlow!.Issues.Select(x => x.Message));
}
