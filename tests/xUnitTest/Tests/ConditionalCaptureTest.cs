// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 7.7.2, 7.7.3, 8.14, 8.15: a bare capture entry of a pair binding whose every admitted case Copies or Reborrows binds as a
// bare entry, which each Semantics case acquires as the binding's case Type does; a case without a plan is the entry's own
// TransferRequired_Kd naming that case; a bare use that Reborrows in some case makes the closure's receiver Exclusive.
public sealed class ConditionalCaptureTest
{
    // A struct whose method takes an exclusive receiver (ClosureEnvironmentOriginTest reads it too).
    internal const string NodeMe = "struct Node\n    public var n: i32\n    public init(n: i32) => self.n = n\n" +
        "    public func me(self: uniq/Self) -> uniq/i32 during self => self.n@uniq\n";

    private const string Main = "public func main() -> ()\n    var n: i32 = 1\n    f(n)\n";
    private const string Tag = "contract Iter<T>\n    associate Tag\n";
    private const string Pair = "func f<s/T>(x: s/i32 during a, p: s/(i32, i32), q: uniq/(s/i32 during a), m: s/T) -> i32\n";
    private const string NodeHead = "struct Node\n    public var n: i32\n    public init(n: i32) => self.n = n\nfunc f<s/T>(m: s/T, o: obj/Node) -> i32\n";
    private const string ObjectView = NodeHead + "    s is objref or objuniq\n    var p = o@move\n    let y = p@s\n"; // `s/Node` (G759).
    private const string UniqFamily = NodeHead + "    s is uniq or objuniq\n    var p = o@move\n    let y = p@s\n"; // A family.

    // SPEC 7.7.2, 8.14, 3.5.2 (DECISIONS BU-184): an entry of a pair target or a projection acquires as its initialization would in each
    // case: Copy where Copy is proven, otherwise the entry's TransferRequired_Kd; `@move` transfers it, and a later Move is MovedPlace_Kd.
    [Theory]
    [InlineData("func f<s/T>(m: s/T, t: T) -> i32\n    s is owner or ref\n    T is Copy\n    let g = func [t] () => ()\n    g()\n    return 0\n", "")]
    [InlineData("func f<s/T>(m: s/T, t: T) -> i32\n    s is owner or ref\n    let g = func [t] () => ()\n    g()\n    return 0\n", "TransferRequired_Kd")]
    [InlineData("func f<s/T>(m: s/T, t: T) -> i32\n    s is owner or ref\n    let g = func [t@move] () => ()\n    g()\n    return 0\n", "")]
    [InlineData("func f<s/T>(m: s/T, t: T) -> i32\n    s is owner or ref\n    let g = func [t@move] () => ()\n    let u = t@move\n    g()\n    return 0\n", "MovedPlace_Kd")]
    [InlineData(Tag + "func f<T>(t: T.Tag) -> i32\n    T is Iter<i32>\n    T.Tag is Copy\n    let g = func [t] () => ()\n    g()\n    return 0\n", "")]
    [InlineData(Tag + "func f<T>(t: T.Tag) -> i32\n    T is Iter<i32>\n    let g = func [t] () => ()\n    g()\n    return 0\n", "TransferRequired_Kd")]
    [InlineData(Tag + "func f<T>(t: T.Tag) -> i32\n    T is Iter<i32>\n    let g = func [t@move] () => ()\n    g()\n    return 0\n", "")]
    public void AnEntryOfAPairTargetOrAProjectionAcquiresAsItsInitialization(string function, string codes)
        => Assert.Equal(codes, Codes(function));

    // SPEC 7.7.2, 8.14 (DECISIONS BU-184 on G755's one plan): a pair layer whose bare acquisition Copies in every admitted case is captured
    // as its initialization would be: a bare entry and an omitted list Copy; a case with no plan is the entry's TransferRequired_Kd.
    [Theory]
    [InlineData(Pair + "    s is owner or ref\n    let g = func [p] () -> i32 => 0\n    return g()\n", "")]
    [InlineData(Pair + "    s is owner or ref\n    let g = func [x] () -> i32 => x + 0\n    let h = g\n    return h() + g()\n", "")]
    [InlineData(Pair + "    s is ref\n    let g = func () -> i32 => x + 0\n    return g()\n", "")]
    [InlineData(Pair + "    s is owner or ref\n    let g = func () -> i32 => x + 0\n    return g()\n", "")]
    [InlineData("func f<s/T>(x: s/string, m: s/T) -> i32\n    s is owner or ref\n    let g = func [x] () -> i32 => 0\n    return g()\n", "TransferRequired_Kd")]
    public void APairLayerThatNeverReborrowsIsCaptured(string function, string codes)
        => Assert.Equal(codes, Codes(function));

    // SPEC 7.7.2, 8.14, 15.8.2 (DECISIONS BU-184 amended by G254b, BU-27): a pair layer other than the original `s/T` whose bare
    // acquisition Reborrows in some admitted case is captured as its initialization would be, its body's uses named by the bare Reborrow
    // rule: valid uses are accepted, a Reborrow stored or returned past the call receiver is `UnprovenOriginRelation_Kd` as its concrete
    // twin's (r23, r24; rcv-pair, a receiver's), an omitted list's or bare entry's case without a plan is `TransferRequired_Kd` (SPEC 7.7.2:
    // an omitted list Copies; c3-str-bare), and `[x@move]` moves (c3-str). The object view `p@s` (an application since G759) is captured.
    [Theory]
    [InlineData(Pair + "    s is owner or uniq\n    var g = func [x] () -> i32 => x + 0\n    return g()\n", "")]
    [InlineData(Pair + "    s is owner or uniq\n    let g = func [x@move] () -> i32 => x + 0\n    return g()\n", "")]
    [InlineData(Pair + "    s is owner or uniq\n    let g = func [x@ref] () -> i32 => 0\n    return g()\n", "")]
    [InlineData(Pair + "    s is owner or uniq\n    var g = func [var x] () -> i32\n        x@follow += 1\n        return 0\n    return g()\n", "")]
    [InlineData(Pair + "    s is ref or uniq\n    var g = func [x] () -> s/i32 during a => x\n    return 0\n", "UnprovenOriginRelation_Kd")]
    [InlineData(Pair + "    s is ref or uniq\n    var g = func [x, q@move] () -> i32\n        q@follow = x\n        return 0\n    return g()\n", "UnprovenOriginRelation_Kd")]
    [InlineData(Pair + "    s is owner or uniq\n    let g = func () -> i32 => x + 0\n    return g()\n", "TransferRequired_Kd")]
    [InlineData(ObjectView + "    let g = func [y] () -> i32 => 1\n    return g()\n", "")]
    [InlineData(ObjectView + "    let g = func [y@move] () -> i32 => 1\n    return g()\n", "")]
    [InlineData("func f<s/T>(x: s/string during a, m: s/T) -> i32\n    s is owner or uniq\n    let g = func [x@move] () -> i32 => 0\n    return g()\n", "")]
    [InlineData("func f<s/T>(x: s/string during a, m: s/T) -> i32\n    s is owner or uniq\n    let g = func [x] () -> i32 => 0\n    return g()\n", "TransferRequired_Kd")]
    [InlineData(NodeMe + "func f<s/T>(m: s/T, y: s/Node during a, q: uniq/(uniq/i32 during a)) -> i32\n    s is uniq\n    var g = func [y, q@move] () -> i32\n        q@follow = y.me()\n        return 0\n    return g()\n", "UnprovenOriginRelation_Kd")]
    [InlineData(NodeMe + "func f<s/T>(m: s/T, y: s/Node during a) -> i32\n    s is uniq\n    var g = func [y] () -> i32\n        let k = y.me()\n        k@follow = 1\n        return 0\n    return g()\n", "")]
    public void ACapturedPairLayerIsAcquiredAsItsInitializationWouldBe(string function, string codes)
        => Assert.Equal(codes, Codes(function));

    // Interim (DECISIONS BU-184 amended by G254b): (1) until the bare Reborrow rule names an adaptation family (PLAN G254's family limit),
    // a family `p@s` whose bare acquisition Reborrows in some admitted case is not captured: a located Unsupported_Kd at a written entry,
    // since a body's bare use of it would Reborrow uncontained in the call receiver. (2) Until PLAN G170's identity query, a pair target or a
    // projection is not captured where an identity premise is available in scope (t14: `T is r/i32 during a`), an unrelated one included
    // (y14, y15: `V is i32`), so G170's removal turns both kinds of row.
    [Theory]
    [InlineData(UniqFamily + "    let g = func [y] () -> i32 => 1\n    return g()\n", "Unsupported_Kd")]
    [InlineData("func f<s/T, r/U>(m: s/T, n: r/U, t: T, w: ref/i32 during a) -> i32\n    s is owner or ref\n    r is owner or ref\n    T is r/i32 during a\n    let g = func [t] () -> i32 => t + 0\n    return g()\n", "Unsupported_Kd")]
    [InlineData("func f<s/T, V>(m: s/T, t: T, v: V) -> i32\n    s is owner or ref\n    T is Copy\n    V is i32\n    let g = func [t] () => ()\n    g()\n    return 0\n", "Unsupported_Kd")]
    [InlineData(Tag + "func f<T, V>(t: T.Tag, v: V) -> i32\n    T is Iter<i32>\n    T.Tag is Copy\n    V is i32\n    let g = func [t] () => ()\n    g()\n    return 0\n", "Unsupported_Kd")]
    public void AReborrowingFamilyOrPremiseMadePairLayerIsUnsupportedUntilTheFamilyRowOrG170(string function, string codes)
        => Assert.Equal(codes, Codes(function));

    [Fact]
    public void ABareEntryOfAPairBindingBindsAsBare()
    {
        var source = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    var h = func [value] () -> ()\n        _ = value@follow\n    h()\n    h()\n" + Main;
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var closure = Closure(c);
        var capture = Assert.Single(closure.Captures);
        Assert.Equal(CaptureAcquisition.Bare, capture.Environment.CaptureAcquisition);
        Assert.Equal(SemanticsKind.Ref, closure.Receiver); // Reading through the capture is Shared in every case.
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void ACaseWithoutAPlanIsTheEntrysTransferRequired()
    {
        var source = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    var h = func [value] () -> ()\n        _ = value@follow\n    h()\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("value", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Contains("in the Semantics case s = owner (SPEC 8.14)", error.Note, StringComparison.Ordinal);
        Assert.Contains(error.Repairs!, static x => x.Title.Contains("@move", StringComparison.Ordinal));
    }

    // A bare acquisition of the captured pair binding Reborrows in the uniq case, so the closure is Exclusive: a `var` closure
    // runs in every instance, and a `let` closure is rejected as its concrete uniq counterpart is.
    [Fact]
    public void ABareUseThatReborrowsInSomeCaseMakesTheClosureExclusive()
    {
        const string Body = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    {0} h = func [value] () -> ()\n        let local = value\n        _ = local\n    h()\n    h()\n" + Main;
        var c = MinimalEmissionTest.Analyze(Body.Replace("{0}", "var", StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(SemanticsKind.Uniq, Closure(c).Receiver);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));

        var errors = DiagnosticCorpus.Check(Body.Replace("{0}", "let", StringComparison.Ordinal)).Diagnostics;
        Assert.NotEmpty(errors);
        Assert.All(errors, static x => Assert.Equal((nameof(DiagnosticCode.InvalidAssignment_Kd), DiagnosticSeverity.Error), (x.Code, x.Severity)));
    }

    // The distinct error codes of a program, in order.
    private static string Codes(string function)
        => string.Join(" ", DiagnosticCorpus.Check(function + "public func main() -> ()\n    Console.writeLine(\"p\")\n").Diagnostics
            .Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => x.Code).Distinct().Order(StringComparer.Ordinal));

    private static BoundClosure Closure(Compilation c)
        => Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!;
}
