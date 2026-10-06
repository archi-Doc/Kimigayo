// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 7.6.2, 7.6.3, 8.9, 8.10: a bare capture entry of a pair binding whose every admitted case Copies or Reborrows binds as a
// bare entry, which each Semantics case acquires as the binding's case Type does; a case without a plan is the entry's own
// TransferRequired_Kd naming that case; a bare use that Reborrows in some case makes the closure's receiver Exclusive.
public sealed class ConditionalCaptureTest
{
    private const string Main = "public func main() -> ()\n    var n: i32 = 1\n    f(n)\n";

    [Fact]
    public void ABareEntryOfAPairBindingBindsAsBare()
    {
        var source = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    var h = func [value] () -> ()\n        _ = value@follow\n    h()\n    h()\n" + Main;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var closure = Closure(c);
        var capture = Assert.Single(closure.Captures);
        Assert.Equal(CaptureAcquisition.Bare, capture.Environment.CaptureAcquisition);
        Assert.Equal(SemanticsKind.Ref, closure.Receiver); // Reading through the capture is Shared in every case.
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ACaseWithoutAPlanIsTheEntrysTransferRequired()
    {
        var source = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    var h = func [value] () -> ()\n        _ = value@follow\n    h()\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("value", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains("in the Semantics case s = owner (SPEC 8.9)", error.Note, StringComparison.Ordinal);
        Assert.Contains(error.Repairs!, static x => x.Title.Contains("@move", StringComparison.Ordinal));
    }

    // A bare acquisition of the captured pair binding Reborrows in the uniq case, so the closure is Exclusive: a `var` closure
    // runs in every instance, and a `let` closure is rejected as its concrete uniq counterpart is.
    [Fact]
    public void ABareUseThatReborrowsInSomeCaseMakesTheClosureExclusive()
    {
        const string Body = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    {0} h = func [value] () -> ()\n        let local = value\n        _ = local\n    h()\n    h()\n" + Main;
        var c = MinimalEmissionTest.Analyze(Body.Replace("{0}", "var", StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(SemanticsKind.Uniq, Closure(c).Receiver);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));

        var errors = DiagnosticCorpus.Check(Body.Replace("{0}", "let", StringComparison.Ordinal)).Diagnostics;
        Assert.NotEmpty(errors);
        Assert.All(errors, static x => Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), DiagnosticSeverity.Error), (x.Code, x.Severity)));
    }

    private static BoundClosure Closure(Compilation c)
        => Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous).BoundClosure!;
}
