// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 13.5.5.1, 15.6.2: a field or Tuple element of an explicitly selected referent (`p@follow.x`) is reached through the
// reference as `p.x` is, and a borrow of it depends on that reference (SPEC 13.5.5). It was UnsupportedOwnership_Kd, and
// Binding gave `p@follow.x@ref` an Origin unrelated to `p`.
public class FollowedReferentFieldTest(ITestOutputHelper output)
{
    private const string S = "struct S\n    public var x: i32\n    public var inner: (i32, i32)\n    public init(x: i32)\n        self.x = x\n        self.inner = (x + 1, x + 2)\n    drop => ()\n";

    [Theory]
    [InlineData("ReadWrite", "func read(p: ref/S) -> i32 => p@follow.x\nfunc deep(p: ref/S) -> i32 => p@follow.inner.1\nfunc write(p: uniq/S) => p@follow.x = 9\nfunc bump(p: uniq/S) => p@follow.inner.0 += 10\nvar s = S.init(4)\nrequire read(s@ref) == 4 and deep(s@ref) == 6 else => $abort(\"read\")\nwrite(s@uniq)\nbump(s@uniq)\nrequire s.x == 9 and s.inner.0 == 15 else => $abort(\"write\")")]
    [InlineData("ReturnedBorrow", "func look(p: ref/S) -> ref/i32 during p => p@follow.x@ref\nvar s = S.init(4)\nlet r = look(s@ref)\nrequire r == 4 else => $abort(\"look\")")]
    [InlineData("SharedReads", "func f(p: ref/S) -> i32\n    let view = p@follow.x@ref\n    let y = p@follow.x\n    return view + y\nvar s = S.init(4)\nrequire f(s@ref) == 8 else => $abort(\"read\")")]
    [InlineData("Uses", "func bump(v: uniq/i32) => v@follow += 1\nfunc same(a: ref/S, b: ref/S) -> bool => a@follow.x == b@follow.x\nfunc g(p: uniq/S) -> i32\n    bump(p@follow.x@uniq)\n    let t = p@follow.inner\n    let c = p@follow.inner.0 < p@follow.x\n    p@follow.inner = (1, 2)\n    return t.0 + p@follow.x\nvar s = S.init(4)\nvar s2 = S.init(5)\nrequire g(s@uniq) == 10 and same(s@ref, s2@ref) and s.inner.1 == 2 else => $abort(\"uses\")")]
    [InlineData("Match", "func h(p: uniq/S) -> i32\n    match p@follow.inner\n        (let a, let b) => return a + b\nvar s = S.init(4)\nrequire h(s@uniq) == 11 else => $abort(\"match\")")]
    [InlineData("DisjointParts", "func f(p: uniq/S) -> i32\n    let view = p@follow.inner.1@ref\n    p@follow.inner.0 += 1\n    return view\nfunc g(p: uniq/S) -> i32\n    let r = p@follow.inner@ref\n    let u = p@follow.x@uniq\n    u@follow = 1\n    return r.0\nvar s = S.init(4)\nrequire f(s@uniq) == 6 and s.inner.0 == 6 else => $abort(\"f\")\nrequire g(s@uniq) == 6 and s.x == 1 else => $abort(\"g\")")]
    [InlineData("Tuple", "func read(p: ref/(i32, i32)) -> i32 => p@follow.1\nlet t = (1, 2)\nrequire read(t@ref) == 2 else => $abort(\"tuple\")")]
    public void AFollowedReferentFieldIsReachedThroughItsReference(string name, string body)
        => ScalarEmissionTest.EmitFixture("FollowedReferentField" + name, S + body, string.Empty);

    [Theory]
    [InlineData("    let view = p@follow.x@ref\n    p@follow.x = 3\n    return view", "p@follow.x")]
    [InlineData("    let view = p@follow.x@ref\n    p.x = 3\n    return view", "p.x")]
    [InlineData("    let view = p.x@ref\n    p@follow.x = 3\n    return view", "p@follow.x")]
    [InlineData("    let view = p@follow.inner.0@ref\n    p@follow.inner.0 += 1\n    return view", "p@follow.inner.0")]
    [InlineData("    let view = p@follow.inner@ref\n    p@follow.inner.0 += 1\n    return view.0", "p@follow.inner.0")]
    [InlineData("    let u = p@follow.x@uniq\n    let r = p@follow.x@ref\n    u@follow = 1\n    return r", "p")]
    [InlineData("    let u = p@follow.x@uniq\n    let v = p.x\n    u@follow = 1\n    return v", "p.x")]
    public void AWriteWhileAFollowedFieldIsBorrowedConflicts(string body, string conflict)
    {
        var source = S + "func f(p: uniq/S) -> i32\n" + body + "\npublic func main() => ()";
        var path = Path.GetFullPath("followed-field-update.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() == conflict);
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(conflict, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        var identity = SourceIdentity.FromPath(path);
        var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, true)[identity]);
        Assert.Equal(error.Display!.Range, sent.Range);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
    }

    [Fact]
    public void AWriteThroughASharedReferenceStaysRejected()
    {
        var error = Assert.Single(DiagnosticCorpus.Check(S + "func write(p: ref/S) => p@follow.x = 9\npublic func main() => ()\n").Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.SharedPathAccess_Kd), error.Code);
    }
}
