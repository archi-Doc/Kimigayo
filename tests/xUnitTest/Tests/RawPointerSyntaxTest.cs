// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC F.3, 3.3, 5: the raw pointer Semantics is spelled raw; unsafe marks only the places where a promise is made. A raw layer may
// state an Origin (SPEC 5.7).
public class RawPointerSyntaxTest
{
    // A view whose slot a shared Loan keeps (requirement `ref`), and a struct whose slot keeps no Loan (Phantom).
    internal const string KeptView = "struct View {source}\n    internal let r: ref/i32 during source\n    public init(r: ref/i32 during source) => self.r = r\n";

    private const string PhantomW = "struct W {s}\n    public let n: i32\n    public init() => self.n = 0\n";

    [Fact]
    public void UnsafeIsNoSemanticsName()
    {
        Assert.True(CompilerHelper.TryParse("raw", out var raw));
        Assert.Equal(SemanticsKind.Raw, raw);
        Assert.False(CompilerHelper.TryParse("unsafe", out _));
        Assert.Equal("raw", SemanticsKind.Raw.ToText());
    }

    // raw is reserved only in Semantics positions and after '@'; elsewhere it is an ordinary Name.
    [Fact]
    public void RawIsAnOrdinaryNameElsewhere()
    {
        var c = MinimalEmissionTest.Analyze("struct Sample\n    public let raw: i32\n    public init(raw: i32) => self.raw = raw\n" +
            "func read(pointer: raw/i32) -> raw/i32 => pointer\npublic func main()\n    let raw = Sample.init(raw: 1)\n    let total = raw.raw + 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheOldSpellingIsNoSemantics()
    {
        var c = MinimalEmissionTest.Analyze("func read(pointer: unsafe/i32) -> () => ()\npublic func main() => ()\n");
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 5.7, 3.3.2, 13.5.1 (DECISIONS BU-24): a raw layer states an Origin wherever a Type is written, also inside a Type argument or
    // a pointee, and a grouped adaptation target states its outer raw layer's (the claim, valid in an unsafe context).
    [Theory]
    [InlineData("func f(b: ref/i32, p: raw/i32 during b) => ()")]
    [InlineData("func f(b: ref/i32, p: raw/i32 during b) -> raw/i32 during b => p")]
    [InlineData("struct View {source}\n    let value: ref/i32 during source\n    let at: raw/i32 during source")]
    [InlineData("struct Buffer\n    let data: raw/i32 during self")]
    [InlineData("enum Cell\n    Some(raw/i32 during self)\n    None")]
    [InlineData("func f(b: ref/i32, p: Option<raw/i32 during b>) => ()")]
    [InlineData("func f(b: ref/i32, p: raw/(raw/i32 during b)) => ()")]
    [InlineData("func f(p: raw/i32) -> raw/i32 during static\n    unsafe => return p@(raw/i32 during static)")]
    [InlineData("func f<T>(x: T)\n    T is raw/i32 during static\n    ()")]
    public void AWrittenRawOriginIsStated(string declarations)
        => Assert.Empty(Records(declarations));

    // SPEC 5.7 (D17, D18): `during` on a raw layer writes one atom of a borrow: never a meet, a name no borrow, slot or static carries,
    // or `self` outside a raw layer of inline storage; a signature's name is never introduced by a raw layer (bu19).
    [Theory]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during (a and b)) => ()", "InvalidOriginBinding_Kd: raw/i32 during (a and b)")]
    [InlineData("func f(x: i32, p: raw/i32 during x) => ()", "InvalidOriginBinding_Kd: x")]
    [InlineData("func f<T>(p: raw/T during d) => ()", "MissingOriginBinding_Kd: d")]
    [InlineData("struct S\n    let p: Option<raw/i32 during self>", "InvalidOriginBinding_Kd: raw/i32 during self")]
    [InlineData("struct S\n    let p: raw/(raw/i32 during self)", "InvalidOriginBinding_Kd: raw/i32 during self")]
    public void ARawOriginIsOneAtomOfABorrow(string declarations, string record)
        => Assert.Equal([record], Records(declarations));

    // SPEC 5.7 (design-final §7.3): a declaration's raw Origin that has no step once the slots' requirements exist is rejected at its
    // annotation and published unstated, so holding the Type inline, a dereference and a call substituting it keep that one record: a
    // Phantom slot, U1's case-dependent pair input, and an atom no borrow holds (bound only by a slot that keeps no Loan).
    [Theory]
    [InlineData(
        "struct W<T> {s}\n    internal let at: raw/T during s\nstruct X<T> {a}\n    internal let w: W<T> during a\nfunc use(x: ref/X<i32>) -> raw/i32 => x.w.at",
        "InvalidOriginBinding_Kd: raw/T during s")]
    [InlineData(
        "func f<s/T>(x: s/T during a, p: raw/T during a) -> T\n    s is ref or uniq\n    T is Copy\n    unsafe => return *p\nfunc g(r: ref/i32, q: raw/i32 during r) -> i32 => f(r, q)",
        "Unsupported_Kd: raw/T during a")]
    [InlineData(
        "struct V {s}\n    internal let n: i32\nfunc h(v: V during a, p: raw/i32 during a) -> raw/i32 during a => p\nfunc k(v: V during b, q: raw/i32 during b) -> raw/i32 => h(v, q)",
        "InvalidOriginBinding_Kd: raw/i32 during a | InvalidOriginBinding_Kd: raw/i32 during a | InvalidOriginBinding_Kd: raw/i32 during b")]
    public void ARejectedRawOriginIsNeverPublished(string declarations, string records)
        => Assert.Equal(records, string.Join(" | ", Records(declarations)));

    // SPEC 5.7 Origins, Authority (DECISIONS BU-24, the delta review's D1 and D2): every written raw Origin is formed with the step its
    // written form gives and judged where it is formed, a body's at formation: a slot projection steps by the projected slot's requirement
    // whatever atom binds it, so a closure parameter's or result's projection of a `ref`-kept slot is Shared, as in a named function; a
    // Phantom projection, in a body annotation, a body Type argument or a closure parameter, is one record, published unstated.
    [Theory]
    [InlineData(PhantomW + "func f(v: ref/W)\n    let q: raw/i32 during v.s = null\n    unsafe => *q = 1", "InvalidOriginBinding_Kd: raw/i32 during v.s")]
    [InlineData(PhantomW + "func f(v: ref/W)\n    let q: raw/i32 during v.s = null\n    unsafe => q[0] = 2", "InvalidOriginBinding_Kd: raw/i32 during v.s")]
    [InlineData(PhantomW + "func f(v: ref/W)\n    let q: raw/i32 during v.s = null\n    let r = (q, 1)\n    unsafe => *r.0 = 1", "InvalidOriginBinding_Kd: raw/i32 during v.s")]
    [InlineData(PhantomW + "struct Box<T>\n    public var v: T\n    public init(v: T) => self.v = v\nfunc f(w: ref/W)\n    var b = Box<raw/i32 during w.s>.init(null)\n    unsafe => *b.v = 1", "InvalidOriginBinding_Kd: raw/i32 during w.s")]
    [InlineData(PhantomW + "func f(w: ref/W)\n    let g = func (l: ref/W, p: raw/i32 during l.s)\n        unsafe => *p = 1", "InvalidOriginBinding_Kd: raw/i32 during l.s")]
    [InlineData(KeptView + "func h(l: ref/View, p: raw/i32 during l.source)\n    unsafe => *p = 1", "SharedPathAccess_Kd: *p")]
    [InlineData(KeptView + "func f()\n    let n = 1\n    let v = View.init(n@ref)\n    let g = func (l: ref/View, p: raw/i32 during l.source)\n        unsafe => *p = 1\n    g(v@ref, v.r@follow@raw)", "SharedPathAccess_Kd: *p")]
    [InlineData(KeptView + "func f()\n    let n = 1\n    let v = View.init(n@ref)\n    let g = func (l: ref/View) -> raw/i32 during l.source => l.r@follow@raw\n    unsafe => *g(v@ref) = 1", "SharedPathAccess_Kd: *g(v@ref)")]
    public void AWrittenRawOriginIsJudgedWhereItIsFormed(string declarations, string record)
        => Assert.Equal([record], Records(declarations));

    // SPEC 5.7, 8.3.4 (design-final §4; U1): a raw layer stating the Origin of a pair input whose admitted cases mix modes or admit a
    // non-borrow case has no one step: a located Unsupported_Kd at the annotation; one mode is the step of every case.
    [Theory]
    [InlineData("func f<s/T>(x: s/T during a, p: raw/T during a)\n    s is ref or uniq\n    ()", "Unsupported_Kd: raw/T during a")]
    [InlineData("func f<s/T>(x: s/T during a, p: raw/T during a)\n    s is value or ref\n    ()", "Unsupported_Kd: raw/T during a")]
    [InlineData("func f<s/T>(x: s/T during a, p: raw/T during a)\n    s is ref\n    ()", null)]
    public void APairOriginOfMixedCasesHasNoStep(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], Records(declarations));

    // SPEC 5.7, Fits and claims: a raw layer without `during` states an Origin every Origin outlives, so dropping or narrowing an Origin
    // is a fit and stating one is a claim, valid only in an unsafe context; elsewhere it is an Origin relation error (rulings S4), at the
    // value whose unstated Origin does not outlive the stated one. An invariant position relates both ways.
    [Theory]
    [InlineData("func adopt(pointer: raw/i32) -> raw/i32 during static\n    return pointer", "UnsatisfiedOriginRelation_Kd: pointer")]
    [InlineData("func adopt(pointer: raw/i32) -> raw/i32 during static => pointer@(raw/i32 during static)", "UnsatisfiedOriginRelation_Kd: pointer")]
    [InlineData("func f(r: ref/i32, p: raw/i32)\n    let q: raw/i32 during r = p", "UnsatisfiedOriginRelation_Kd: p")]
    [InlineData("func g(b: ref/i32, p: raw/i32 during b) => ()\nfunc f(b: ref/i32, q: raw/i32) => g(b, q)", "UnsatisfiedOriginRelation_Kd: q")]
    [InlineData("func f(r: ref/i32, a: uniq/Array<raw/i32 during r>)\n    let b: uniq/Array<raw/i32> = a", "UnsatisfiedOriginRelation_Kd: a")]
    [InlineData("func g(a: uniq/Array<raw/i32>) => ()\nfunc f(r: ref/i32, a: uniq/Array<raw/i32 during r>) => g(a)", "UnsatisfiedOriginRelation_Kd: a")]
    [InlineData("func f(r: ref/i32, a: raw/i32 during r)\n    var c = a\n    let u: uniq/(raw/i32) = c@uniq", "UnsatisfiedOriginRelation_Kd: c@uniq")]
    public void StatingARawOriginIsAClaim(string declarations, string record)
        => Assert.Equal([record], Records(declarations));

    [Theory]
    [InlineData("func adopt(pointer: raw/i32) -> raw/i32 during static\n    unsafe => return pointer@(raw/i32 during static)")]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/i32 => p")]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/i32 during r => p@(raw/i32 during r)")]
    [InlineData("func f(r: ref/i32) -> raw/i32 during r => null@(raw/i32 during r)")]
    [InlineData("func g(p: raw/i32) => ()\nfunc f(r: ref/i32, p: raw/i32 during r) => g(p)")]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/u8 during r => p@raw/u8")]
    public void DroppingOrKeepingARawOriginIsAFit(string declarations)
        => Assert.Empty(Records(declarations));

    // SPEC 5.7, 15.3.5 (B′ §8 keep-rebuilds): an instance, a closure's inferred result and a Semantics case view keep a raw Origin, so an
    // invariant position still compares it.
    [Theory]
    [InlineData("func id<T>(x: T) -> T => x@move\nfunc f(r: ref/i32, p: raw/i32 during r)\n    var q = id(p)\n    let u: uniq/(raw/i32) = q@uniq", "UnsatisfiedOriginRelation_Kd: q@uniq")]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r)\n    let g = func [p] () => p\n    var q = g()\n    let u: uniq/(raw/i32) = q@uniq", "UnsatisfiedOriginRelation_Kd: q@uniq")]
    [InlineData("func f<s/T>(x: s/T, r: ref/i32, p: raw/i32 during r)\n    s is ref or uniq\n    var q = p\n    let u: uniq/(raw/i32) = q@uniq", "UnsatisfiedOriginRelation_Kd: q@uniq")]
    [InlineData("func id<T>(x: T) -> T => x@move\nfunc f(r: ref/i32, p: raw/i32 during r)\n    var q = id(p)\n    let u: uniq/(raw/i32 during r) = q@uniq", null)]
    public void InstancesClosuresAndCaseViewsKeepARawOrigin(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], Records(declarations));

    // B′ §8 (design-final §14; DECISIONS BU-24): the census of published raw Origins. In every node's Type (signatures, Field and Case
    // declarations and bodies) and every function's result, a raw layer's Origin is `static`, a shared view or meet of allowed atoms,
    // or an atom whose declaration encloses the node; `self` (Holder) only in a stored shape (a Field or Case declaration, outside any
    // body), which every read replaces; never Unbound or Unstated. So no Inference, callee or local-region atom is published past its
    // body.
    [Theory]
    [InlineData(RawStorageTest.Buffer + "func use()\n    var b = Buffer<i32>.init(capacity: 4)\n    b.push(1)\n    let v = b.view()\n    let s = v.get(0)@follow + b.get(0)@follow")]
    [InlineData(RawStorageTest.Block + "func more(b: ref/Block, u: uniq/Block)\n    let c = Cell.Two((null, 1))\n    unsafe => *u.data = 1\n    let p: raw/u8 = b.pair.0")]
    [InlineData(RawPlaceAccessTest.Lenses + "func f(lens: ref/Lens<i32>) -> i32\n    unsafe => return *lens.at\nfunc g(y: uniq/B)\n    unsafe => *y.data = 1")]
    [InlineData("func choose(c: bool, a: ref/i32, b: ref/i32) -> ref/i32 => if c => a else => b\nfunc f(c: bool, x: ref/i32, y: ref/i32) -> raw/i32 => choose(c, x, y)@follow@raw\nfunc h<s/T>(x: s/i32) -> i32\n    s is ref or uniq\n    let p = x@follow@raw\n    unsafe => return *p")]
    [InlineData("func id<T>(x: T) -> T => x@move\nfunc k(r: ref/i32, p: raw/i32 during r) -> raw/i32\n    let q = id(p)\n    let n: i32 = 1\n    let local = n@raw\n    let g = func [q] () => q\n    return g()")]
    public void PublishedRawOriginsStayWithinTheirDeclarations(string declarations)
    {
        var source = declarations.Replace("{0}", "()", StringComparison.Ordinal).Replace("{1}", "()", StringComparison.Ordinal) + "\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var census = 0;
        var leaks = new List<string>();
        foreach (var node in KotoTree.Walk(c.Kotonoha.RootKoto))
        {
            Audit(node, node.TypeOf());
            Audit(node, (node as FunctionKoto)?.SymbolOf()?.Type);
        }

        Assert.True(census > 0);
        Assert.Empty(leaks);

        void Audit(Koto node, BoundType? type)
        {
            for (var i = 0; type is not null && i < type.Components.Count; i++)
            {
                Audit(node, type.Components[i]);
            }

            if (type?.RawOrigin is { } raw)
            {
                census++;
                if (!Allowed(raw, node))
                {
                    leaks.Add($"{node.GetType().Name} `{node.ToString()?.Split('\n')[0]}`: {raw.Kind} {raw.Name}");
                }
            }
        }

        static bool Allowed(BoundOrigin origin, Koto node) => origin.Kind switch
        {
            OriginKind.Static => true,
            OriginKind.Holder => Declaration(node) is not FunctionKoto,
            OriginKind.Shared or OriginKind.Intersection => origin.Operands.All(x => Allowed(x, node)),
            OriginKind.Unbound or OriginKind.Unstated => false,
            _ => origin.Binder is { } binder && Encloses(Declaration(binder), node),
        };

        static Koto Declaration(Koto binder)
        {
            for (Koto? at = binder; at is not null; at = at.Parent)
            {
                if (at is FunctionKoto or DeclarationContainerKoto)
                {
                    return at;
                }
            }

            return binder;
        }

        static bool Encloses(Koto outer, Koto node)
        {
            for (Koto? at = node; at is not null; at = at.Parent)
            {
                if (ReferenceEquals(at, outer))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // SPEC 5.7 (Q-O6): a var local keeps its initializer's raw Origin; raw Origins that differ have the unstated common Type, never a meet,
    // also where one branch is a call whose result states its argument's Origin.
    [Theory]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b)\n    var x = p\n    x = q", "UnprovenOriginRelation_Kd: q")]
    [InlineData("func f(a: ref/i32, b: ref/i32)\n    var x = a@follow@raw\n    x = b@follow@raw", "UnprovenOriginRelation_Kd: b@follow@raw")]
    public void AVarLocalKeepsItsRawOrigin(string declarations, string record)
        => Assert.Equal([record], Records(declarations));

    [Theory]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b)\n    var x = p\n    x = p")]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b)\n    let both = [p, q]\n    let x: raw/i32 = both[0]")]
    [InlineData("func f(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b) -> bool => p == q")]
    [InlineData("func f(a: ref/i32, n: i32)\n    var x = a@follow@raw\n    x = a@follow@raw\n    let both = [a@follow@raw, n@raw]")]
    [InlineData("func get(b: ref/i32, q: raw/i32 during b) -> raw/i32 during b => q\nfunc f(c: bool, a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b) -> raw/i32\n    let x = if c => get(b, q) else => p\n    return x")]
    public void DifferingRawOriginsJoinUnstated(string declarations)
        => Assert.Empty(Records(declarations));

    // SPEC 5.7 (rulings S4, design-final §13 A2): a relation with an unstated end forms no obligation, and no obligation names Shared,
    // Holder or Unstated.
    [Fact]
    public void NoObligationNamesARawOnlyOrigin()
    {
        const string Source = "struct Buffer\n    internal let data: raw/i32 during self\n    public init() => unsafe => self.data = Raw.allocate<i32>(1)@(raw/i32 during self)\n" +
            "func shared(b: ref/Buffer) -> raw/i32 => b.data\nfunc pass(r: ref/i32, p: raw/i32 during r) -> raw/i32 => p\n" +
            "func local(n: i32)\n    let r = n@ref\n    let p: raw/i32 during r = null\n    let q: raw/i32 = p\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Binding.Obligations, static x => RawOnly(x.Longer) || RawOnly(x.Shorter));

        static bool RawOnly(BoundOrigin? origin) => origin?.Kind is OriginKind.Shared or OriginKind.Holder or OriginKind.Unstated;
    }

    // SPEC 10.7, 15.3.7 (review 2 B-B1): a conversion's contract compares raw Origins too; a required unstated input the implementation
    // states an Origin for is the member that fails, with the unstated end first.
    [Fact]
    public void AConversionComparesRawOrigins()
        => Assert.Equal(
            ["UnprovenOriginContract_Kd: f"],
            Records("func f(r: ref/i32, p: raw/i32 during r) => ()\nfunc g()\n    let h: (ref/i32, raw/i32) -> () = f"));

    // SPEC 15.3.6, 5.7 (review 2 §6 item 7): a closure's inferred result names no body-local region: a raw Origin over a var local is
    // resolved to that local's sources, as a borrow's is.
    [Fact]
    public void AClosureResultNamesNoLocalRawRegion()
    {
        const string Source = "func f(x: ref/i32) -> raw/i32\n    let g = func [x] ()\n        var v = x\n        let p: raw/i32 during v = null\n        return p\n" +
            "    return g()\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var result = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.IsAnonymous).SymbolOf()!.Type!;
        Assert.NotNull(result.RawOrigin);
        Assert.False(Binding.IsLocalRegion(Binding.RegionOf(result.RawOrigin)!));
    }

    // SPEC 5.4, 5.7: a provable claim needs no unsafe context, so an unsafe block holding only it is unnecessary; an unprovable one needs it.
    [Theory]
    [InlineData("func f(r: ref/i32, p: raw/i32 during r) -> raw/i32 during r\n    unsafe => return p@(raw/i32 during r)", "UnnecessaryUnsafeBlock_Kd: unsafe")]
    [InlineData("func f(r: ref/i32, p: raw/i32) -> raw/i32 during r\n    unsafe => return p@(raw/i32 during r)", null)]
    public void AClaimNeedsAnUnsafeContextOnlyWhenItIsNoFit(string declarations, string? record)
        => Assert.Equal(record is null ? [] : [record], Records(declarations));

    // The check's records of declarations completed by an empty main, each as `code: primary span text` (SPEC 23.1).
    internal static string[] Records(string declarations)
    {
        var source = declarations + "\npublic func main() => ()\n";
        return [.. DiagnosticCorpus.Check(source).Diagnostics.Select(x => x.Span is { } span && x.Source == 0 ? $"{x.Code}: {source.Substring(span.Start, span.Length)}" : x.Code)];
    }
}
