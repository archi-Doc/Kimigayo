// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 7.6.1, 14.9: anonymous blocks share result-source inference, including unreachable returns and structural end Unit.
public sealed class BlockClosureInferenceTest
{
    [Theory]
    [InlineData("Returns", "let f = func [] (flag: bool)\n    if flag => return 1\n    return 2\nrequire f(true) == 1 and f(false) == 2 else => $abort(\"returns\")")]
    [InlineData("WideFirst", "let f = func [] (flag: bool, n: i64)\n    if flag => return n\n    return 1\nrequire f(false, 5000000000) == 1 else => $abort(\"wide\")")]
    [InlineData("WideLast", "let f = func [] (flag: bool, n: i64)\n    if flag => return 1\n    return n\nrequire f(false, 5000000000) == 5000000000 else => $abort(\"wide\")")]
    [InlineData("LocalEvidence", "let f = func [] (flag: bool)\n    if flag => return 1\n    let n: i64 = 5000000000\n    return n\nrequire f(false) == 5000000000 else => $abort(\"local\")")]
    [InlineData("InferredLocalEvidence", "func wide() -> i64 => 5000000000\nlet f = func [] (flag: bool)\n    if flag => return 1\n    let n = wide()\n    return n\nrequire f(false) == 5000000000 else => $abort(\"local\")")]
    [InlineData("DiscardedTail", "let f = func [] ()\n    7\nf()")]
    [InlineData("NestedBoundary", "let f = func [] ()\n    func inner() -> string => \"s\"\n    let g = func [] ()\n        return \"nested\"\n    _ = inner()\n    _ = g()\n    return 3\nrequire f() == 3 else => $abort(\"boundary\")")]
    [InlineData("Owned", "let text = \"owned\"\nlet f = func [text@move] ()\n    return text@move\nlet result = f@move()\nrequire result == \"owned\" else => $abort(\"owned\")")]
    [InlineData("InputBorrow", "let f = func [] (value: ref/i32)\n    return value\nlet n: i32 = 7\nrequire f(n@ref)@follow == 7 else => $abort(\"borrow\")")]
    [InlineData("InputCallBorrow", "func first(value: ref/i32) -> ref/i32 => value\nlet f = func [] (value: ref/i32)\n    return first(value)\nlet n: i32 = 7\nrequire f(n@ref)@follow == 7 else => $abort(\"borrow call\")")]
    [InlineData("CallReadEvidence", "func first(value: ref/i64) -> ref/i64 => value\nlet f = func [] (flag: bool, value: ref/i64)\n    if flag => return 1\n    return first(value)\nlet n: i64 = 5000000000\nrequire f(false, n@ref) == 5000000000 else => $abort(\"read evidence\")")]
    [InlineData("MeetBorrow", "let f = func [] (a: ref/i32, b: ref/i32, flag: bool)\n    if flag => return a\n    return b\nlet a: i32 = 4\nlet b: i32 = 7\nrequire f(a@ref, b@ref, false)@follow == 7 else => $abort(\"meet\")")]
    [InlineData("FixedBorrow", "let n: i32 = 7\nlet r = n@ref\nlet f = func [r] ()\n    return r\nrequire f()@follow == 7 else => $abort(\"fixed\")")]
    [InlineData("Erased", "let f = func [] (n: i32)\n    return n + 1\nlet g: (i32) -> i32 = f\nrequire g(4) == 5 else => $abort(\"erased\")")]
    [InlineData("UnreachableConsistent", "let f = func [] ()\n    return 1\n    return 2\nrequire f() == 1 else => $abort(\"unreachable\")")]
    [InlineData("GenericContext", "func apply<F, T>(f: ref/F) -> T\n    F is Callable<() -> T>\n    return f()\nlet f = func [] ()\n    return 7\nrequire apply(f) == 7 else => $abort(\"generic\")")]
    [InlineData("Selection", "let f = func [] (flag: bool, n: i64)\n    return if flag => 1 else => n\nrequire f(false, 5000000000) == 5000000000 else => $abort(\"selection\")")]
    [InlineData("Try", "func wrap(n: i32) -> i32? => .Some(n)\nlet f = func [] (value: i32?)\n    let n = try value\n    return wrap(n + 1)\nmatch f(.Some(4))\n    .Some(let n) => require n == 5 else => $abort(\"try\")\n    .None => $abort(\"none\")\nmatch f(.None)\n    .None => ()\n    .Some(let n) => $abort(\"failure\")")]
    [InlineData("TryQualifiedResult", "let f = func [] (value: i32?)\n    let n = try value\n    return Option<i32>.Some(n + 1)\nmatch f(.Some(4))\n    .Some(let n) => require n == 5 else => $abort(\"try\")\n    .None => $abort(\"none\")\nmatch f(.None)\n    .None => ()\n    .Some(let n) => $abort(\"failure\")")]
    [InlineData("NestedTransfers", "let f = func [] (flag: bool)\n    let n = label done: do\n        if flag => exit to done 2\n        exit to done 3\n    defer => ()\n    return n\nrequire f(true) == 2 else => $abort(\"transfers\")")]
    [InlineData("LengthItemResult", "func keep<length N>(a: [N of i32]) -> [N of i32] => a\nlet factory = func [] ()\n    return keep<2>\nlet f = factory()\nrequire f([4, 7])[1] == 7 else => $abort(\"item\")")]
    [InlineData("EmptyArray", "let f = func [] (flag: bool, a: [0 of i32])\n    if flag => return []\n    return a\nlet empty: [0 of i32] = []\nrequire f(true, empty).length == 0 else => $abort(\"array\")")]
    public void AnonymousBlocksInferAndExecuteTheirResult(string name, string source)
        => ScalarEmissionTest.EmitFixture("BlockClosureInference" + name, source, string.Empty);

    [Theory]
    [InlineData("let f = func [] ()\n    $abort(\"unused\")", true)]
    [InlineData("let f = func [] ()\n    loop => continue", true)]
    [InlineData("let f = func [] ()\n    return", false)]
    [InlineData("let f = func [] ()\n    ()", false)]
    public void NeverRequiresNoValueSourceAndNoEndArrival(string source, bool never)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous);
        Assert.Same(never ? BoundType.Never : BoundType.Unit, closure.SymbolOf()!.Type);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("let f = func [] ()\n    return 1\n    return \"bad\"")]
    [InlineData("let f = func [] (flag: bool)\n    if flag => return 1")]
    [InlineData("let f = func [] ()\n    if true => return 1")]
    [InlineData("let f = func [] (flag: bool)\n    if flag => return func [] () => 1\n    return func [] () => 2")]
    [InlineData("func named()\n    return 1")]
    public void AllWrittenResultsAndStructuralFallthroughConstrainTheType(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.Unsupported_Kd));
    }

    [Fact]
    public void AnUnreachableNonNeverSourceStillFixesTheResult()
    {
        var c = MinimalEmissionTest.Analyze("let f = func [] ()\n    loop => continue\n    return 1");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous);
        Assert.Same(BoundType.I32, closure.SymbolOf()!.Type);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("let f = func [] ()\n    let n: i32 = 1\n    return n@ref")]
    [InlineData("let f = func [] (n: i32)\n    return n@ref")]
    [InlineData("let f = func [] (flag: bool, value: ref/i32)\n    if flag => return value\n    let n: i32 = 1\n    return n@ref")]
    public void AnInferredResultCannotBorrowTheCallsOwnStorage(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), error.Code);
        Assert.Equal("n@ref", error.Text);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public void AResultMeetRetainsBothInputLoans(string owner)
    {
        var c = MinimalEmissionTest.Analyze("let f = func [] (a: ref/i32, b: ref/i32, flag: bool)\n    if flag => return a\n    return b\nvar a: i32 = 4\nvar b: i32 = 7\nlet r = f(a@ref, b@ref, false)\n" + owner + " = 9\n_ = r@follow");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void LocalResultFailureExplainsTheBorrowInCliAndLsp()
    {
        const string Source = "let f = func [] ()\n    let n: i32 = 1\n    return n@ref";
        var path = Path.GetFullPath("BlockResult.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), record.Code);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("n@ref", Source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.Contains(record.Related!, static r => r.Role == "origin");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Fact]
    public void EditingTheBodyInvalidatesItsInferredContract()
    {
        var c = MinimalEmissionTest.Analyze("let f = func [] ()\n    return 1\nlet n: i32 = f()");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out _), MinimalEmissionTest.Describe(c, null));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous);
        var donor = MinimalEmissionTest.Analyze("let f = func [] ()\n    return \"s\"");
        var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous);
        Assert.True(KotoHelper.Replace(closure, closure.Body!, replacement.Body!));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Bind().IsComplete);
        Assert.Same(BoundType.String, closure.SymbolOf()!.Type);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [Trait("Purpose", "Allocation")]
    [InlineData("let f = func [] (flag: bool, n: i64)\n    if flag => return 1\n    return n\n_ = f(false, 5000000000)")]
    [InlineData("func wide() -> i64 => 5000000000\nlet f = func [] (flag: bool)\n    if flag => return 1\n    let n = wide()\n    let m = n\n    return m\n_ = f(false)")]
    public void WarmBlockInferenceReusesItsResultContexts(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
