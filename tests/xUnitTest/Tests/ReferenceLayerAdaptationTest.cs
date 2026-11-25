// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 10.2: at a fixed expected ref/U, safe reference layers ending in U yield one shared reference; the
/// innermost ref layer is Copied with its own Origin and uniq layers below it are shared-Reborrowed. The row's exclusive mode (SPEC
/// 7.3.3): a Receiver Expression below several layers whose path and layers grant it is one exclusive reference through them.</summary>
public class ReferenceLayerAdaptationTest
{
    private const string Node = "struct Node\n    public var id: i32\n    public init(id: i32) => self.id = id\nfunc validate(node: ref/Node) -> i32 => node.id\n";
    private const string Meter = "struct Meter\n    public var n: i32 = 0\n    public init() => ()\n    public func bump(self: uniq/Self) => self.n += 1\n";
    private const string Two = "struct Two<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    public var uniqCalls: i32 = 0\n    public init(first: T) => self.first = first@move\n    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self\n        self.uniqCalls += 1\n        return self.first@uniq\n";
    private const string One = "struct One<T>\n    Self is Indexable<isize>\n    associate Element is T\n    var first: T\n    public init(first: T) => self.first = first@move\n    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n";
    private const string Gauge = "struct Gauge\n    public var hits: i32 = 0\n    public computed level: i32\n        get() -> i32 => self.hits\n        set(value: i32) -> () => self.hits = value\n    public init() => ()\n";
    private const string Pot = "struct Pot\n    public var n: i32 = 0\n    public init() => ()\n    public func absorb(self: uniq/Self, other: ref/Pot) => self.n += other.n\n";
    private const string Lent = "struct Lent\n    public var n: i32 = 0\n    public init() => ()\n    public func bump(self: uniq/Self) => self.n += 1\n    public func peek(self) -> i32 => self.n\n    public func me(self: uniq/Self) -> uniq/Lent during self => self\n";
    private const string Bump = "contract Bump\n    func bump(self: uniq/Self)\n";
    private const string Bumped = Bump + "struct Meter\n    Self is Bump\n    public var n: i32 = 0\n    public init() => ()\n    public func bump(self: uniq/Self) => self.n += 1\n";
    private const string PairBump = Bumped + "func f<s/T, U>(g: uniq/(s/U during b), m: s/T) -> ()\n    s is owner or uniq\n    U is Bump\n    g.bump()\n";
    private const string Bound = "func f(values: uniq/Array<uniq/(uniq/Meter during a) during b>)";
    private const string LentBound = Lent + "func f(values: uniq/Array<uniq/(uniq/Lent during a) during b>)";
    private const string LentOne = Lent + "func f(values: uniq/Array<uniq/Lent during a>)";

    [Theory]
    [InlineData("SharedThroughExclusive", Node + "var first = Node.init(1)\nvar second = Node.init(2)\nlet nodes: [2 of uniq/Node] = [first@uniq, second@uniq]\nvar total: i32 = 0\nfor node in nodes\n    total += validate(node)\nrequire total == 3 else => $abort(\"total\")\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("InnerSharedCopied", "let x = \"a\"\nlet y = \"b\"\nlet names: [2 of ref/string] = [x@ref, y@ref]\nfor name in names\n    let view: ref/string = name\n    Console.writeLine(view)", "a\nb\n")]
    [InlineData("SharedBelowExclusive", "var n = 7\nlet r = n@ref\nvar holder: [1 of ref/i32] = [r]\nfor h in holder@uniq\n    let inner: ref/i32 = h\n    require inner == 7 else => $abort(\"inner\")\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("OptionPayload", Node + "func report(found: ref/Node? during a) -> i32\n    return match found\n        .Some(let hit) => validate(hit)\n        .None => 0\nvar total: i32 = 0\nlet third = Node.init(3)\nrequire validate(third@ref) == 3 else => $abort(\"direct\")\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("UniqTemporary", Node + "func pass(n: uniq/Node) -> uniq/Node => n\nvar a = Node.init(4)\nrequire validate(pass(a@uniq)) == 4 else => $abort(\"temporary\")\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("DictionaryIndex", "func get(m: ref/Dictionary<i32, i32>) -> i32 => m[1]\nlet m = [1: 42]\nrequire get(m) == 42 else => $abort(\"value\")", "")]
    [InlineData("DictionaryNestedIndex", "func get(m: ref/(ref/Dictionary<i32, i32> during b)) -> i32 => m[1]\nlet m = [1: 42]\nlet inner = m@ref\nlet outer = inner@ref\nrequire get(outer) == 42 else => $abort(\"value\")", "")]
    public void OneSharedReferenceThroughLayers(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ReferenceLayer" + name, source, stdout);

    // SPEC 7.3.3, 3.4.3, 15.6.2 (PLAN G147 (2) (c)): a uniq/Self receiver below several safe value-reference layers is one exclusive
    // reference through them, the exclusive mode of 10.2's row, where its path and layers grant it: an Array's, a user index's and a
    // Dictionary's element through indexUniq, a var root's element, a Type parameter's terminal; the bump reaches the referent. Two
    // calls in sequence each end their reservation (15.6.7); a setter and a compound update through the layers locate their receiver
    // once (11.3.1, 13.7.2); a generic body's `uniq/(s/U)` under `s is owner or uniq` runs in its owner and its uniq instance (8.1.3);
    // a call result is followed from its temporary (3.4.1).
    [Theory]
    [InlineData("Array", Meter + "func f(values: uniq/Array<uniq/(uniq/Meter during a) during b>) => values[0].bump()\nvar m = Meter.init()\nvar r = m@uniq\nvar rr = r@uniq\nvar vals = [rr@move]\nf(vals@uniq)\nrequire m.n == 1 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    [InlineData("UserIndex", Meter + Two + "func f(values: uniq/Two<uniq/(uniq/Meter during a) during b>) => values[0].bump()\nvar m = Meter.init()\nvar r = m@uniq\nvar rr = r@uniq\nvar two = Two.init(rr@move)\nf(two@uniq)\nrequire two.uniqCalls == 1 else => $abort(\"no indexUniq\")\nrequire m.n == 1 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    [InlineData("Dictionary", Meter + "func f(values: uniq/Dictionary<i32, uniq/(uniq/Meter during a) during b>) => values[1].bump()\nvar m = Meter.init()\nvar r = m@uniq\nvar rr = r@uniq\nvar d = [1: rr@move]\nf(d@uniq)\nrequire m.n == 1 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    [InlineData("VarRoot", Meter + "var m = Meter.init()\nvar r = m@uniq\nvar rr = r@uniq\nvar vals = [rr@move]\nvals[0].bump()\nrequire m.n == 1 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    [InlineData("TypeParameter", Bumped + "func f<U>(g: uniq/(uniq/U during b)) -> ()\n    U is Bump\n    g.bump()\nvar m = Meter.init()\nvar r = m@uniq\nf(r@uniq)\nrequire m.n == 1 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    [InlineData("TwoCalls", Meter + Bound + "\n    values[0].bump()\n    values[0].bump()\nvar m = Meter.init()\nvar r = m@uniq\nvar rr = r@uniq\nvar vals = [rr@move]\nf(vals@uniq)\nrequire m.n == 2 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    [InlineData("SetterCompound", Gauge + "func f(values: uniq/Array<uniq/(uniq/Gauge during a) during b>)\n    values[0].level = 5\n    values[0].level += 1\nvar g = Gauge.init()\nvar r = g@uniq\nvar rr = r@uniq\nvar vals = [rr@move]\nf(vals@uniq)\nrequire g.hits == 6 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    [InlineData("PairOwnerInstance", PairBump + "var m1 = Meter.init()\nlet one: i32 = 1\nf<i32, Meter>(m1@uniq, one)\nrequire m1.n == 1 else => $abort(\"owner\")\nConsole.writeLine(\"ok\")")]
    [InlineData("PairUniqInstance", PairBump + "func run(x: uniq/i32 during q) -> i32\n    var m2 = Meter.init()\n    var r2 = m2@uniq\n    f<uniq/i32 during q, Meter>(r2@uniq, x)\n    return m2.n\nvar n: i32 = 5\nrequire run(n@uniq) == 1 else => $abort(\"uniq\")\nConsole.writeLine(\"ok\")")]
    [InlineData("CallResult", Bumped + "func pass<U>(r: uniq/(uniq/U during a) during b) -> uniq/(uniq/U during a) during b => r\nfunc f<U>(r: uniq/(uniq/U during a) during b) -> ()\n    U is Bump\n    pass(r).bump()\nvar m = Meter.init()\nvar r1 = m@uniq\nf(r1@uniq)\nrequire m.n == 1 else => $abort(\"lost\")\nConsole.writeLine(\"ok\")")]
    public void AnExclusiveReceiverIsOneReferenceThroughLayers(string name, string source)
        => ScalarEmissionTest.EmitFixture("ReferenceLayerExclusiveReceiver" + name, source, "ok\n");

    // The exclusive mode reaches every terminal and layer count below a concrete top layer (three layers, a generic struct, an enum;
    // SPEC 3.4.3) and every Receiver Expression (a `let`-held reference, a Tuple element), and stays a Receiver Expression's: an argument
    // and an unbound method's `self` are ordinary inputs (10.2, 7.3.4). The invalid twins keep the record their one-layer forms have: a
    // shared layer or path (SPEC 15.6.2), a let root (15.1.5), a live shared Loan of the receiver's root or an argument reading it
    // (15.6.2, 15.6.7), an index without indexUniq (4.7.1 step 3). The reference carries 10.2's dependency and its Loan (15.6.3): a result
    // reached through it is contained in `values`, not in `a` alone, and access through the parent waits for the child's last use.
    [Theory]
    [InlineData("func f(values: uniq/Array<uniq/(uniq/(uniq/Meter during a) during b) during c>) => values[0].bump()", null)]
    [InlineData("func f(values: uniq/Array<uniq/(uniq/Array<i32> during a) during b>) => values[0].append(1)", null)]
    [InlineData("enum Mode\n    A\n    B\n    public func flip(self: uniq/Self) => self@follow = Mode.B\nfunc f(values: uniq/Array<uniq/(uniq/Mode during a) during b>) => values[0].flip()", null)]
    [InlineData("func take(m: uniq/Meter) => m.bump()\n" + Bound + " => take(values[0])", nameof(Kimi.DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData(Bound + " => Meter.bump(values[0])", nameof(Kimi.DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("func f(values: uniq/Array<ref/(uniq/Meter during a) during b>) => values[0].bump()", nameof(Kimi.DiagnosticCode.SharedPathAccess_Kd))]
    [InlineData("func f(values: ref/Array<uniq/(uniq/Meter during a) during b>) => values[0].bump()", nameof(Kimi.DiagnosticCode.SharedPathAccess_Kd))]
    [InlineData("func f(values: uniq/Array<uniq/(ref/(uniq/Meter during a) during b) during c>) => values[0].bump()", nameof(Kimi.DiagnosticCode.SharedPathAccess_Kd))]
    [InlineData("func f(values: ref/Dictionary<i32, uniq/(uniq/Meter during a) during b>) => values[1].bump()", nameof(Kimi.DiagnosticCode.SharedPathAccess_Kd))]
    [InlineData(Gauge + "func f(values: ref/Array<uniq/(uniq/Gauge during a) during b>) => values[0].level = 5", nameof(Kimi.DiagnosticCode.InvalidAssignment_Kd))]
    [InlineData("func g(rr: uniq/(uniq/Meter during a) during b)\n    let vals = [rr@move]\n    vals[0].bump()", nameof(Kimi.DiagnosticCode.InvalidAssignment_Kd))]
    [InlineData(Bound + "\n    let view: ref/(Array<uniq/(uniq/Meter during a) during b>) = values\n    values[0].bump()\n    _ = view", nameof(Kimi.DiagnosticCode.CallActivationConflict_Kd))]
    [InlineData(Pot + "func f(values: uniq/Array<uniq/(uniq/Pot during a) during b>) => values[0].absorb(values[1])", nameof(Kimi.DiagnosticCode.CallActivationConflict_Kd))]
    [InlineData(Pot + "func f(values: uniq/Array<uniq/Pot during a>) => values[0].absorb(values[1])", nameof(Kimi.DiagnosticCode.CallActivationConflict_Kd))]
    [InlineData(One + "func f(values: uniq/One<uniq/(uniq/Meter during a) during b>) => values[0].bump()", nameof(Kimi.DiagnosticCode.SharedPathAccess_Kd))]
    [InlineData(One + "func f(values: uniq/One<uniq/Meter during a>) => values[0].bump()", nameof(Kimi.DiagnosticCode.SharedPathAccess_Kd))]
    [InlineData(Bump + "func f<U>(g: uniq/(uniq/U during a) during b) -> ()\n    U is Bump\n    let h = g\n    h.bump()", null)]
    [InlineData(Bump + "func f<U>(t: uniq/(uniq/(uniq/U during a) during b, i32) during c) -> ()\n    U is Bump\n    t.0.bump()", null)]
    [InlineData(Bump + "func f<U>(t: ref/(uniq/(uniq/U during a) during b, i32) during c) -> ()\n    U is Bump\n    t.0.bump()", nameof(Kimi.DiagnosticCode.SharedPathAccess_Kd))]
    [InlineData(LentBound + " -> uniq/Lent during values\n    return values[0].me()", null)]
    [InlineData(LentBound + " -> uniq/Lent during a\n    return values[0].me()", nameof(Kimi.DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData(LentOne + " -> uniq/Lent during a\n    return values[0].me()", nameof(Kimi.DiagnosticCode.UnprovenOriginRelation_Kd))]
    [InlineData(LentBound + " -> i32\n    let k = values[0].me()\n    let n = values[1].peek()\n    k.bump()\n    return n", nameof(Kimi.DiagnosticCode.ComparisonLoanConflict_Kd))]
    [InlineData(LentOne + " -> i32\n    let k = values[0].me()\n    let n = values[1].peek()\n    k.bump()\n    return n", nameof(Kimi.DiagnosticCode.ComparisonLoanConflict_Kd))]
    [InlineData(LentBound + " -> i32\n    let k = values[0].me()\n    k.bump()\n    return values[1].peek()", null)]
    public void AnExclusiveReceiverThroughLayersHasItsOneLayerVerdict(string body, string? code)
        => Assert.Equal(code is null ? Array.Empty<string>() : [code], DiagnosticCorpus.Check(Meter + body + "\npublic func main() => ()\n").Diagnostics.Select(static x => x.Code).Distinct());

    // SPEC 10.2, 15.6.4: a bare exclusive reference at an identical fixed expected uniq Type is Reborrowed, never moved out
    // of its Place, so the original reference stays usable afterwards, for a local as for a stored field.
    [Theory]
    [InlineData("Local", "var n = 1\ndo\n    let r = n@uniq\n    let s: uniq/i32 = r\n    s@follow += 1\n    r@follow += 1\nrequire n == 3 else => $abort(\"local\")\nConsole.writeLine(\"ok\")")]
    [InlineData("Field", "struct Holder {a}\n    let value: uniq/i32 during a\n    public init(value: uniq/i32 during a) => self.value = value@move\n    public func bump(self: uniq/Self)\n        let alias: uniq/i32 = self.value\n        alias@follow += 1\n        self.value@follow += 1\nvar n = 1\ndo\n    var h = Holder.init(n@uniq)\n    h.bump()\nrequire n == 3 else => $abort(\"field\")\nConsole.writeLine(\"ok\")")]
    public void AnExactExclusiveReferenceIsReborrowed(string name, string source)
    {
        ScalarEmissionTest.EmitFixture("ReferenceLayerExactUniq" + name, source, "ok\n");
        var c = MinimalEmissionTest.Analyze(source);
        var initializer = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.FieldKoto>().First(x => x.NameKoto.IdentifierName is "s" or "alias").InitializerKoto!;
        Assert.True(c.Binding.TryGetAdaptation(initializer, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.Reborrow);
    }

    [Fact]
    public void TheInnerSharedReferenceKeepsOnlyItsOwnOrigin()
    {
        // The Copied inner reference no longer depends on the array slot; replacing the whole array afterwards is valid.
        var c = MinimalEmissionTest.Analyze("let x = \"a\"\nvar names: [1 of ref/string] = [x@ref]\nlet view: ref/string = names[0]\nnames = [x@ref]\nConsole.writeLine(view)");
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void AnExclusiveLayerKeepsItsLoan()
    {
        // Reborrowing through a uniq layer keeps that layer's Loan: the owner cannot be replaced while the view lives.
        var c = MinimalEmissionTest.Analyze(Node + "var first = Node.init(1)\nlet nodes: [1 of uniq/Node] = [first@uniq]\nlet view: ref/Node = nodes[0]\nfirst = Node.init(5)\nlet id = validate(view)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
    }

    [Theory]
    [InlineData("func f(x: ref/i32) => ()\nvar a: i32 = 1\nlet r = a@uniq\nf(r@move)")]
    [InlineData("var a: i32 = 1\nlet r = a@uniq\nlet v: ref/i32 = r@move")]
    [InlineData("func f(x: i32) => ()\nvar a: i32 = 1\nlet r = a@uniq\nf(r@move)")]
    public void ATransferredReferenceIsNotAdaptedAfterwards(string source)
    {
        // SPEC 10.2: an explicit @move runs first and is not corrected by a later Reborrow or Scalar read.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void ATransferredReferenceFitsItsOwnType()
    {
        var c = MinimalEmissionTest.Analyze("func f(x: uniq/i32) => ()\nvar a: i32 = 1\nlet r = a@uniq\nf(r@move)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void DictionaryIndexingCannotRecoverWriteThroughASharedLayer()
    {
        // SPEC 3.4.3, 4.7.1: Indexable follows reference layers but never recovers exclusive access through ref.
        var c = MinimalEmissionTest.Analyze("func set(m: ref/(uniq/Dictionary<i32, i32> during b)) => m[1] = 42\npublic func main() => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == Kimi.DiagnosticCode.SharedPathAccess_Kd);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == Kimi.DiagnosticCode.Unsupported_Kd);
        Assert.Equal(["SharedPathAccess_Kd m[1]"], CompilationTestHelper.PublishedErrors(c).Select(static x => $"{x.Code} {x.Text}"));
    }

    [Theory]
    [InlineData("func at(a: ref/(ref/[2 of i32] during b)) -> i32 => a[1]\npublic func main() => ()")]
    public void IndexingThroughSeveralLayersReportsOnlyItsBoundary(string source)
    {
        // STATUS boundary: indexing through several reference layers stops at Binding with one diagnostic.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.Unsupported_Kd);
        Assert.Equal([nameof(Kimi.DiagnosticCode.Unsupported_Kd)], CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code));
    }

    // SPEC 3.4.3: sequence metadata and indexing follow every safe reference layer and pair layer, so metadata past one reference
    // layer and indexing through a pair layer are one located Unsupported_Kd, not a missing Name or a receiver without an index.
    [Theory]
    [InlineData("func count(a: ref/(ref/Array<i32> during b)) -> isize => a.length", "a.length")]
    [InlineData("func at<s/T>(xs: s/Array<i32>) -> i32\n    s is owner or ref\n    return xs[0]", "xs[0]")]
    [InlineData("func at<s/T>(xs: s/[2 of i32]) -> i32\n    s is owner or ref\n    return xs[0]", "xs[0]")]
    public void SequenceAccessThroughFurtherLayersIsOneUnsupportedRecord(string body, string at)
    {
        var source = body + "\npublic func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(("Unsupported_Kd", source.IndexOf(at, StringComparison.Ordinal), at.Length), (error.Code, error.Span!.Value.Start, error.Span.Value.Length));
    }
}
