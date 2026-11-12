// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OriginRedesignTest
{
    private const string RegionPrelude = "contract Source\n    associate Item\ncontract Marker\ncontract Shown\nstruct Cursor {at}\n    Self is Source\n" +
        "    associate Item is ref/i32 during at\n    public let item: ref/i32 during at\n    public init(item: ref/i32 during at) => self.item = item\n" +
        "struct View<T> {source}\n    let data: ref/T during source\n    public init(data: ref/T during source) => self.data = data\n" +
        "public func main() => ()\n";

    // PLAN G457 (R1) d_decl_ok: a method called on another value of its Type in its own body.
    private const string MethodOnAnotherValue = "struct W {x}\n    public let r: ref/i32 during x\n    public init(r: ref/i32 during x) => self.r = r\n    public func m(self: ref/Self, y: ref/i32 during k, z: ref/i32 during j) -> ref/i32 during x\n        let w: W during k = W.init(y)\n        let p: ref/i32 during k = w.m(z, z)\n        return self.r";

    [Theory]
    [InlineData("struct V<T> {source}\n    struct Inner\n        let value: ref/T during source")]
    [InlineData("func identity<T>(x: ref/T during s) -> ref/T during s => x")]
    [InlineData("func identity(x: V<i32>) -> V<i32>{r}\n    origin r.source == x.source\n    return x@move")]
    [InlineData("struct W {s}\n    let value: V<i32>{v}\n        origin v.source == s")]
    [InlineData("func identity(x: V<i32>{v}) -> V<i32>{r}\n    origin r.source == v.source\n    return x@move")]
    [InlineData("var pending: V<i32>{p}\n    origin p.source == static")]
    [InlineData("func identity(x: V<i32>) -> V<i32>{r}\n    origin r.source == x.source\n    let value: V<i32> = x@move\n    return value@move")]
    [InlineData("func identity(x: ref/V<i32>) -> ref/i32 during x.source => x.value")]
    [InlineData("func identity(x: V<i32>{v}) -> V<i32>{r}\n    origin v.source == r.source\n    return x@move")]
    [InlineData("func shorten(x: V<i32>) -> V<i32>{r}\n    origin x.source outlives r.source\n    return x@move\nfunc use(x: V<i32>)\n    let y = shorten(x@move)")]
    [InlineData("func shorten(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b\n    origin a outlives b\n    return x")]
    [InlineData("func same(x: V<i32>, y: V<i32>)\n    origin x.source == y.source\n    ()\nfunc use(x: V<i32>, y: V<i32>) => same(x@move, y@move)")]
    [InlineData("func bounded(x: ref/i32 during a, y: ref/i32 during b)\n    origin a outlives b\n    ()\nfunc use(x: ref/i32, y: ref/i32) => bounded(x, y)")]
    [InlineData("func forward(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b\n    origin a outlives b\n    let result: ref/i32 during b = x\n    return result")]
    [InlineData("func composite(x: ref/i32 during a, y: ref/i32 during b, z: ref/i32 during c) -> ref/i32 during (b and c)\n    origin a outlives b and c\n    return x")]
    [InlineData("func call(callback: (ref/i32) -> V<i32>{v}) => ()")]
    [InlineData("func call(callback: (V<i32>{v}) -> (), value: V<i32>)\n    origin v.source == value.source\n    ()")]
    [InlineData("func f(x: ref/i32)\n    var pending: V<i32>\n        origin pending.source == x")]
    [InlineData("struct Bound {a, b}\n    origin a outlives b\n    let source: ref/i32 during a\n    let view: V<i32>{v}\n        origin v.source == a\n        origin v.source outlives b")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b)\n    origin a outlives b\n    origin b outlives a\n    ()")]
    [InlineData("func f<F>(x: V<i32>, callback: F)\n    F is Callable<(V<i32>{v}) -> ()>\n    origin v.source == x.source\n    ()")]
    [InlineData("func f(x: uniq/(ref/i32 during a), y: ref/i32 during b) -> uniq/(ref/i32 during b)\n    origin a outlives b\n    origin b outlives a\n    return x")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> ref/i32 during b\n    origin (a) outlives b\n    return x")]
    [InlineData("struct S {source}\n    let value: ref/i32 during source\n    let source: i32\n    func f(x: V<i32>) -> ref/i32 during source => $abort(\"unused\")")]
    [InlineData("func identity<T>(x: T) -> T => x@move\nfunc f(x: V<i32>)\n    let y = identity<V<i32>{v}>(x@move)\n        origin v.source == x.source")]
    [InlineData("func f(x: ref/V<i32>)\n    let y = x@follow@ref/V<i32>{v}\n        origin v.source == x.source")]
    public void CompletesDeclarationOriginContracts(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var prefix = source.StartsWith("struct V", StringComparison.Ordinal) ? string.Empty : "struct V<T> {source}\n    public let value: ref/T during source\n";
        c.Kotonoha.AddSource(new SourceDocument("redesign.kimi", prefix + source));
        Assert.Empty(TestDiagnostics.Of(c));
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Node.CodeContext.SourceDocument?.Path}: {x.Node}: {x.Node.FailureOf()}")));
        }
    }

    [Theory]
    [InlineData("struct Wrong\n    let a: ref/i32 during source\n    let b: ref/i32 during souce")]
    [InlineData("struct Wrong\n    let a: ref/i32 during source")]
    [InlineData("struct Wrong {source}\n    let a: ref/i32 during souce")]
    [InlineData("func f(x: V<i32>{v}, y: V<i32>{v}) => ()")]
    [InlineData("func f(x: V<i32>{x}) => ()")]
    [InlineData("func f(x: V<i32>{s}, y: ref/i32 during s) => ()")]
    [InlineData("func f(x: V<i32>{v}) -> ref/i32 during v => $abort(\"bad\")")]
    [InlineData("func f(callback: (ref/i32 during fresh) -> ()) => ()")]
    [InlineData("func f(x: ref/i32)\n    var pending: V<i32>{p}\n        origin p.source outlives x")]
    [InlineData("struct Wrong {a, b}\n    let item: V<i32>{v}\n        origin v.source == a\n        origin a outlives b")]
    [InlineData("func f(callback: (ref/i32) -> V<i32>{v}) -> ref/i32 during v.source => $abort(\"bad\")")]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b, z: ref/i32 during c) -> ref/i32 during b\n    origin a outlives b and c\n    return x")]
    [InlineData("func f(x: ref/i32)\n    let a: V<i32>{v} = $abort(\"a\")\n        origin v.source == x\n    let b: V<i32>{v} = $abort(\"b\")")]
    [InlineData("struct Marker {a}\nfunc shorten(x: Marker) -> Marker{r}\n    origin x.a outlives r.a\n    return x")]
    [InlineData("func f(x: V<i32>, y: V<i32>)\n    var pending: V<i32>{p}\n        origin x.source outlives p.source")]
    [InlineData("func f(x: ref/i32 during a)\n    let a = 1\n    var pending: ref/i32 during a")]
    [InlineData("func f<F>(x: ref/i32 during a, callback: F)\n    F is Callable<(ref/i32 during a) -> ()>\n    ()")]
    public void RejectsInvalidOriginDeclarations(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("invalid.kimi", "struct V<T> {source}\n    let value: ref/T during source\n" + source));
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("struct Marker")]
    [InlineData("struct V<T> {source}\n    let item: ref/T during source")]
    [InlineData("func f<T>(x: ref/T during s) -> ref/T during s\n    origin s outlives s\n    return x")]
    [InlineData("func f<T>(x: View<T>) -> View<T>{r}\n    origin r.source == x.source\n    return x")]
    [InlineData("struct W<T> {s}\n    let item: View<T>{v}\n        origin v.source == s")]
    [InlineData("var pending: View<i32>{p}\n    origin p.source == static")]
    [InlineData("let view: View<i32> = make()\n    origin view.source == static")]
    [InlineData("enum E<T> {s}\n    Some(View<T>{v})\n        origin v.source == s\n    None")]
    [InlineData("struct S<T> {s}\n    associate C.Element is View<T>{v}\n        origin v.source == s")]
    [InlineData("alias Helpers => Family.Helpers{h}\n    origin h.source == static")]
    [InlineData("contract C\n    func f<T>(self, x: View<T>) -> View<T>{r}\n        origin r.source == x.source")]
    [InlineData("struct S {s}\n    computed value: ref/i32 during s\n        get(self: ref/Self) -> ref/i32 during s\n            origin s outlives self\n            return getValue()")]
    public void DeclarationRelationsRoundTrip(string source)
    {
        var tree = ParseTestHelper.Parse(source);
        Assert.Empty(TestDiagnostics.Of(tree));
        var builder = default(IndentedStringBuilder);
        try
        {
            tree.RootKoto.UnparseAll(ref builder);
            var written = builder.ToString();
            var reloaded = ParseTestHelper.Parse(written);
            Assert.True(TestDiagnostics.Of(reloaded).Length == 0, written + "\n" + string.Join("\n", TestDiagnostics.Of(reloaded).Select(x => x.Message)));
            if (source.Contains("origin ", StringComparison.Ordinal))
            {
                Assert.Contains("origin ", written, StringComparison.Ordinal);
            }

            if (source.Contains("{}", StringComparison.Ordinal))
            {
                Assert.Contains("{}", written, StringComparison.Ordinal);
            }
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData("func f {s}(x: ref/i32 during s) => ()")]
    [InlineData("struct S\n    init {s}(x: ref/i32 during s) => ()")]
    [InlineData("struct S {a : b, b}")]
    [InlineData("func f(x: View<i32>{source => static}) => ()")]
    [InlineData("func f(x: View<i32>{static}) => ()")]
    [InlineData("func f(x: View<i32>{a.source}) => ()")]
    [InlineData("func f(x: View<i32>{}) => ()")]
    [InlineData("func f(x: ref/i32)\n    ()\n    origin x == x")]
    public void RejectsRemovedOrMisplacedSyntax(string source)
        => Assert.NotEmpty(TestDiagnostics.Of(ParseTestHelper.Parse(source)));

    [Theory]
    [InlineData("Implicit", "func read(x: ref/Cell during s) -> i32 => x.value\nlet value = Cell.init()\nif read(value@ref) != 21 => $abort(\"origin\")")]
    [InlineData("Equality", "func read(x: ref/Cell during a, y: ref/Cell during b) -> i32\n    origin a == b\n    return x.value + y.value\nlet a = Cell.init()\nlet b = Cell.init()\nif read(a@ref, b@ref) != 42 => $abort(\"origin\")")]
    [InlineData("Relation", "func read(x: ref/Cell during a, y: ref/Cell during b) -> i32\n    origin a outlives b\n    return x.value + y.value\nlet a = Cell.init()\nlet b = Cell.init()\nif read(a@ref, b@ref) != 42 => $abort(\"origin\")")]
    public void OriginContractsReachCheckedEmission(string name, string source)
    {
        source = "struct Cell\n    public let value: i32 = 21\n" + source;
        var compilation = MinimalEmissionTest.Analyze(source);
        Assert.True(compilation.Binding.Result.IsComplete, string.Join("\n", compilation.Binding.Issues));
        ScalarEmissionTest.EmitFixture("OriginRedesign" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("first@uniq, second@uniq", true)]
    [InlineData("first@uniq, first@uniq", false)]
    public void EqualOriginsRetainDistinctExclusiveLoans(string arguments, bool valid)
    {
        var source = "struct Cell\n    public var value: i32 = 0\n" +
            "func update(x: uniq/Cell during a, y: uniq/Cell during b)\n    origin a == b\n    x.value = 1\n    y.value = 2\n" +
            $"var first = Cell.init()\nvar second = Cell.init()\nupdate({arguments})";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(valid == c.Analysis.IsVerified, string.Join('\n', c.Problems));
        Assert.Equal(valid, c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRelationsAndCallsKeepAllocationsBounded()
    {
        var c = MinimalEmissionTest.Analyze("struct Cell\n    public var value: i32 = 0\n" +
            "func update(x: uniq/Cell during a, y: uniq/Cell during b)\n    origin a == b\n    x.value = 1\n    y.value = 2\n" +
            "var first = Cell.init()\nvar second = Cell.init()\nupdate(first@uniq, second@uniq)");
        Assert.True(c.Generate(TextWriter.Null));
        var bindingBytes = AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Origin relation Binding failed.");
            }
        });
        c.Binding.CheckStartup(OutputKind.Application);
        var ownershipBytes = AllocationMeasurement.Measure(() => c.Analyze());
        var emissionBytes = AllocationMeasurement.Measure(() => c.Generate(TextWriter.Null));
        // Binding retains a bounded 288 bytes per measured warm pass.
        Assert.True(
            bindingBytes <= AllocationMeasurement.MeasuredOperations * 288 && ownershipBytes == 0 && emissionBytes == 0,
            $"Binding: {bindingBytes}; ownership: {ownershipBytes}; emission: {emissionBytes}");
    }

    // SPEC 15.3.4 (G417 (3) U1): a set named in a Constraint is its declaration's, in any order: a named function's binds inside its
    // header's Origin declaration, which its relations complete, whether a Constraint, the signature or the body names it first
    // (a use before the occurrence included, and a set inside a nested Callable). A struct's set is seen by every fragment's members
    // and nested Types, and a related one is valid in a Field Type or a payload; a conditional conformance owns its `when` list's set
    // (its block sees it, the Type's other members do not), and a Contract-wide clause owns its own. A set no relation equates
    // quantifies every choice of its Origins (G417 (3) U2): a function's, a struct's, a requirement's, a `when` list's and a
    // Contract-wide clause's are valid.
    [Theory]
    [InlineData("func f<T>(value: ref/T, y: ref/i32) -> i32\n    origin k.at == y\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0", "")]
    [InlineData("func f<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    origin k.at == y\n    return 0", "")]
    [InlineData("func f<T>(value: ref/T, y: ref/i32) -> i32\n    origin k.at == y\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    let z: ref/i32 during k.at = y\n    return 0", "")]
    [InlineData("func f<T>(x: ref/i32 during k.at, y: ref/i32) -> i32\n    origin k.at == y\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0", "")]
    [InlineData("func f<T>(x: ref/i32, y: ref/i32) -> ref/i32 during k.at\n    origin k.at == y\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return y", "")]
    [InlineData("func f<T, U>(v: ref/T, u: ref/U, y: ref/i32) -> i32\n    origin k.at == y\n    T is Source\n    U is Source\n    U.(Source).Item is ref/i32 during k.at\n    T.(Source).Item is (Cursor{k}).Item\n    return 0", "")]
    [InlineData("func f<T, U>(v: ref/T, u: ref/U, y: ref/i32) -> i32\n    origin k.at == y\n    T is Source\n    U is Source\n    T.(Source).Item is (Cursor{k}).Item\n    U.(Source).Item is ref/i32 during k.at\n    return 0", "")]
    [InlineData("func f<F, T>(g: ref/F, t: ref/T, x: View<i32>{xs}) -> i32\n    T is ref/i32 during a.source\n    F is Callable<(View<i32>{a}) -> ()>\n    origin a.source == xs.source\n    return 0", "")]
    [InlineData("func f<F, T>(g: ref/F, t: ref/T, x: View<i32>{xs}) -> i32\n    T is Source\n    T.(Source).Item is ref/i32 during a.source\n    F is Callable<(Option<(View<i32>{a}) -> ()>) -> ()>\n    origin a.source == xs.source\n    return 0", "")]
    [InlineData("struct Holder<T> {h}\n    public let v: i32\n    public init(v: i32, y: ref/i32)\n        origin k.at == y\n        T is Source\n        T.(Source).Item is (Cursor{k}).Item\n        self.v = v", "")]
    [InlineData("struct Holder<T> {h}\n    public let v: i32\n    public func get(self, y: ref/i32) -> i32\n        origin k.at == y\n        T is Source\n        T.(Source).Item is (Cursor{k}).Item\n        return self.v", "")]
    [InlineData("contract Picks\n    func pick<T>(value: ref/T, y: ref/i32) -> i32\n        origin k.at == y\n        T is Source\n        T.(Source).Item is (Cursor{k}).Item", "")]
    [InlineData("struct Holder<T> {h}\n    origin s.at == h\n    T is Source\n    T.(Source).Item is (Cursor{s}).Item\n    public let w: ref/i32 during s.at\n    public func item(self: ref/Self) -> ref/i32 during s.at => self.w", "")]
    [InlineData("struct Holder<T> {h}\n    origin s.at == h\n    T is Source\n    T.(Source).Item is (Cursor{s}).Item\n    public let w: ref/i32 during h\nstruct Holder<T> {h}\n    public func item(self: ref/Self) -> ref/i32 during s.at => self.w", "")]
    [InlineData("struct Holder<T> {h}\n    origin s.at == h\n    T is Source\n    T.(Source).Item is (Cursor{s}).Item\n    public let w: ref/i32 during h\n    struct Inner\n        public let v: ref/i32 during s.at", "")]
    [InlineData("enum Choice<T> {h}\n    origin s.at == h\n    T is Source\n    T.(Source).Item is (Cursor{s}).Item\n    One(ref/i32 during s.at)\n    Two", "")]
    [InlineData("func f<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0", "")]
    [InlineData("struct Holder<T> {h}\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    public let v: i32", "")]
    [InlineData("contract Picks\n    func pick<T>(value: ref/T, y: ref/i32) -> i32\n        T is Source\n        T.(Source).Item is (Cursor{k}).Item", "")]
    [InlineData("struct W<T, U> {w}\n    public let v: i32\n    Self is Marker when T is (Cursor{m}).Item\n    Self is Shown when U is (Cursor{m}).Item", "")]
    [InlineData("contract Lens\n    associate Item\n    Self.Item is (Cursor{n}).Item", "")]
    [InlineData("open struct Box<T>\n    public let v: i32\nstruct D<T> {h} : Box<ref/i32 during s.at>\n    origin s.at == h\n    T is Source\n    T.(Source).Item is (Cursor{s}).Item", "")]
    [InlineData("struct Holder<T, U> {h}\n    T is Source\n    U is Source\n    U.(Source).Item is (Cursor{j}).Item\n    public let v: ref/i32 during j.at", "InvalidOriginBinding_Kd")]
    [InlineData("enum Choice<T, U> {h}\n    T is Source\n    U is Source\n    U.(Source).Item is (Cursor{j}).Item\n    One(ref/i32 during j.at)\n    Two", "InvalidOriginBinding_Kd")]
    [InlineData("open struct Box<T>\n    public let v: i32\nstruct D<T, U> {h} : Box<ref/i32 during j.at>\n    T is Source\n    U is Source\n    U.(Source).Item is (Cursor{j}).Item", "InvalidOriginBinding_Kd")]
    public void ConstraintSetsAreTheirDeclarations(string declaration, string codes)
        => Assert.Equal(codes, string.Join(' ', ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Select(static x => x.Code)));

    // SPEC 15.3.4 (DECISIONS BU-100): an unrelated set's slot is a fixed Origin that no caller substitutes (OriginKind.ConstraintSet).
    // A call under another set, two calls and a recursion prove nothing of it (a recursion keeps its own record, as without sets). A
    // body row on it is judged, never the located limit: a `when` block's member, a local from it and an annotated local; a finite
    // Origin never flows into it, and an `outlives` clause is its premise, while storage still hides it. A uniq parameter over it is
    // valid; a raw layer stating it is the located Unsupported_Kd. Pins: a set inside a Constraint's Callable input stays unbound
    // (Nested signatures), and a call proving a Constraint over it, one that every choice satisfies with a static argument included,
    // stays UnprovenConstraint_Kd (the call-site gap, STATUS).
    [Theory]
    [InlineData("func f<T>(v: ref/T) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0\nfunc g<U>(u: ref/U) -> i32\n    U is Source\n    U.(Source).Item is (Cursor{m}).Item\n    return f(u)", "UnprovenConstraint_Kd")]
    [InlineData("func f<T>(v: ref/T) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0\nfunc g(a: ref/i32) -> i32\n    let c = Cursor.init(a)\n    let x = f(c@ref)\n    return f(c@ref)", "UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData("func f<T>(v: ref/T) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return f(v)", "UnboundTypeArgument_Kd")]
    [InlineData("struct W<V> {w}\n    public let v: i32\n    Self is Marker when V is (Cursor{m}).Item\n        public func get(self: ref/Self, y: ref/i32 during m.at) -> ref/i32 during m.at => y", "")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    let z: ref/i32 during k.at = y\n    return 0", "UnprovenOriginRelation_Kd")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    var a = 1\n    let z: ref/i32 during k.at = a@ref\n    return 0", "UnsatisfiedOriginRelation_Kd")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32 during k.at) -> ref/i32 during k.at\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    let z: ref/i32 during k.at = y\n    return z", "")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32 during k.at) -> ref/i32 during k.at\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    let z = y\n    return z", "")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32) -> i32\n    origin k.at outlives y\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0", "")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32) -> ref/i32 during y\n    origin k.at outlives y\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    let z: ref/i32 during k.at = y\n    return z", "UnprovenOriginRelation_Kd")]
    [InlineData("struct H<T> {h}\n    origin j.at outlives h\n    T is Source\n    T.(Source).Item is (Cursor{j}).Item\n    public let v: ref/i32 during j.at", "InvalidOriginBinding_Kd")]
    [InlineData("func f<T>(v: ref/T, y: uniq/i32 during k.at) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    y@follow += 1\n    return 0", "")]
    [InlineData("func f<T>(v: ref/T, p: raw/i32 during k.at) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0", "Unsupported_Kd")]
    [InlineData("func f<F>(g: ref/F) -> i32\n    F is Callable<(View<i32>{a}) -> ()>\n    return 0", "InvalidConstraint_Kd InvalidConstraint_Kd MissingOriginBinding_Kd")]
    [InlineData("struct Plain\n    Self is Source\n    associate Item is i32\nfunc f<T>(v: ref/T, y: ref/i32 during k.at) -> i32\n    T is Source\n    T.(Source).Item is not (Cursor{k}).Item\n    return 0\nfunc g(p: ref/Plain, s: ref/i32 during static) -> i32 => f(p, s)", "UnprovenConstraint_Kd")]
    public void AnUnrelatedSetIsAFixedOrigin(string declaration, string codes)
        => Assert.Equal(codes, string.Join(' ', ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Select(static x => x.Code).Order(StringComparer.Ordinal)));

    // SPEC 6.2.2, 15.3.1, 9.7.2 (PLAN G425): a Type occurrence's Constraints hold under its whole substitution, its Origin arguments
    // included: a base clause (single-slot, set and concrete), a Field, a parameter, an enum Type, a method's own clause over its
    // declaring Type's slot and a conditional conformance's condition. A premise naming another Origin, an elided slot (`during` on
    // `ref`, SPEC 3.3.2) and the declaration's own slot standing for another occurrence's prove nothing. The substitution is simultaneous:
    // a Type argument naming the declaration's own slots (`Box<Cursor during h> during x` inside `Box<T>{h}`, swapped slots) keeps them,
    // in a clause, a nested Type, an inherited conformance, a Callable's effect bound and a Field's Type alike.
    [Theory]
    [InlineData("open struct Base<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\nstruct Derived<T> {d} : Base<T> during d\n    T is Source\n    T.(Source).Item is ref/i32 during d\n    public func again(self: ref/Self) -> ref/i32 during d => self.w", "")]
    [InlineData("open struct Base<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\nstruct Derived<T> {d} : Base<T>{b}\n    origin b.h == d\n    T is Source\n    T.(Source).Item is ref/i32 during d", "")]
    [InlineData("open struct Base<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\nstruct Derived {d} : Base<Cursor during d> during d", "")]
    [InlineData("struct Box<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\nstruct Holder<T> {d}\n    T is Source\n    T.(Source).Item is ref/i32 during d\n    public let b: Box<T> during d", "")]
    [InlineData("open struct Base<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\nfunc f<T>(b: ref/(Base<T> during d)) -> i32\n    T is Source\n    T.(Source).Item is ref/i32 during d\n    return 0", "")]
    [InlineData("enum E<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    One(ref/i32 during h)\nfunc g<V>(e: ref/(E<V> during m)) -> i32\n    V is Source\n    V.(Source).Item is ref/i32 during m\n    return 0", "")]
    [InlineData("struct Box<T> {h}\n    public let w: ref/i32 during h\n    public func get<U>(self: ref/Self, u: ref/U) -> i32\n        U is Source\n        U.(Source).Item is ref/i32 during h\n        return 0\nfunc g<V>(b: ref/(Box<i32> during m), v: ref/V) -> i32\n    V is Source\n    V.(Source).Item is ref/i32 during m\n    return b.get(v)", "")]
    [InlineData("struct Box<T> {h}\n    public let w: ref/i32 during h\n    Self is Marker when T is Source, T.(Source).Item is ref/i32 during h\nfunc need<M>(m: ref/M) -> i32\n    M is Marker\n    return 0\nfunc g<V>(b: ref/(Box<V> during m)) -> i32\n    V is Source\n    V.(Source).Item is ref/i32 during m\n    return need(b)", "")]
    [InlineData("open struct Base<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\nstruct Derived<T> {d} : Base<T> during d\n    T is Source", "InvalidConstraint_Kd InvalidTypeFormation_Kd UnprovenConstraint_Kd")]
    [InlineData("open struct Base<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\nstruct Derived<T> {d, e} : Base<T> during d\n    T is Source\n    T.(Source).Item is ref/i32 during e", "InvalidConstraint_Kd InvalidConstraint_Kd InvalidTypeFormation_Kd UnprovenConstraint_Kd")]
    [InlineData("struct Box<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\nstruct Holder<T> {d, e}\n    T is Source\n    T.(Source).Item is ref/i32 during d\n    public let b: Box<T> during e", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData("open struct Base<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\nfunc f<T>(b: ref/Base<T> during d) -> i32\n    T is Source\n    T.(Source).Item is ref/i32 during d\n    return 0", "UnprovenConstraint_Kd")]
    [InlineData("struct Box<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\n    public func other(self: ref/Self, b: ref/(Box<T> during x)) -> i32 => 0", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData("struct Box<T> {h}\n    public let w: ref/i32 during h\n    public func get<U>(self: ref/Self, u: ref/U) -> i32\n        U is Source\n        U.(Source).Item is ref/i32 during h\n        return 0\nfunc g<V>(b: ref/(Box<i32> during m), v: ref/V, z: ref/i32 during n) -> i32\n    V is Source\n    V.(Source).Item is ref/i32 during n\n    return b.get(v)", "UnprovenConstraint_Kd")]
    [InlineData("struct Box<T> {h}\n    public let w: ref/i32 during h\n    Self is Marker when T is Source, T.(Source).Item is ref/i32 during h\nfunc need<M>(m: ref/M) -> i32\n    M is Marker\n    return 0\nfunc g<V>(b: ref/(Box<V> during m), z: ref/i32 during n) -> i32\n    V is Source\n    V.(Source).Item is ref/i32 during n\n    return need(b)", "UnprovenConstraint_Kd")]
    [InlineData("struct P2<T> {h, k}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\n    public let v: ref/i32 during k\n    public func other(self: ref/Self, b: ref/(P2<Cursor during k>{q})) -> i32\n        origin q.h == k\n        origin q.k == h\n        return 0", "")]
    [InlineData("struct Box2<T> {h}\n    public let t: ref/T during h\n    public func get(self: ref/Self, b: ref/(Box2<Cursor during h> during x)) -> ref/(Cursor during h) during x => b.t", "")]
    [InlineData("open struct Base<T> {h}\n    public let w: ref/i32 during h\n    Self is Marker when T is Source, T.(Source).Item is ref/i32 during h\nstruct Derived<T> {d} : Base<T> during d\nfunc need<M>(m: ref/M) -> i32\n    M is Marker\n    return 0\nfunc g<V>(b: ref/(Derived<V> during m)) -> i32\n    V is Source\n    V.(Source).Item is ref/i32 during m\n    return need(b)", "")]
    [InlineData("struct Outer<T> {o}\n    T is Source\n    T.(Source).Item is ref/i32 during o\n    public let w: ref/i32 during o\n    public struct Inner\n        public let v: ref/i32 during o\nfunc g<V>(x: ref/((Outer<V> during m).Inner)) -> i32\n    V is Source\n    V.(Source).Item is ref/i32 during m\n    return 0", "")]
    [InlineData("struct Box<T> {h}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\n    public func other(self: ref/Self, b: ref/(Box<Cursor during h> during x)) -> i32 => 0", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData("struct P2<T> {h, k}\n    T is Source\n    T.(Source).Item is ref/i32 during h\n    public let w: ref/i32 during h\n    public let v: ref/i32 during k\n    public func other(self: ref/Self, b: ref/(P2<Cursor during h>{q})) -> i32\n        origin q.h == k\n        origin q.k == h\n        return 0", "UnprovenConstraint_Kd UnprovenConstraint_Kd UnprovenConstraint_Kd")]
    [InlineData("struct Box2<T> {h}\n    public let t: ref/T during h\n    public func get(self: ref/Self, b: ref/(Box2<Cursor during h> during x)) -> ref/(Cursor during x) during x => b.t", "UnprovenOriginRelation_Kd")]
    [InlineData("struct Holder<F> {h}\n    origin s.at == h\n    F is Callable<(Cursor{s}) -> i32>\n        effect confined\n    public let w: ref/i32 during h\nfunc g<G>(x: ref/(Holder<G> during m)) -> i32\n    origin t.at == m\n    G is Callable<(Cursor{t}) -> i32>\n        effect confined\n    return 0", "")]
    [InlineData("struct Holder<F> {h}\n    origin s.at == h\n    F is Callable<(Cursor{s}) -> i32>\n        effect confined\n    public let w: ref/i32 during h\nfunc g<G>(x: ref/(Holder<G> during m)) -> i32\n    origin t.at == m\n    G is Callable<(Cursor{t}) -> i32>\n    return 0", "UnsatisfiedEffectBound_Kd")]
    public void ATypeOccurrenceProvesItsConstraintsUnderItsOriginArguments(string declaration, string codes)
        => Assert.Equal(codes, string.Join(' ', ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Select(static x => x.Code).Order(StringComparer.Ordinal)));

    // SPEC 10.1 step 5, 15.6.1, 15.6.4 (PLAN G707, G417): a call's Constraints hold under its whole substitution, its solved Origin
    // images included, as its signature does: a function's, a method's, a requirement's and a constructor's clause, two Origins, a
    // projection and a conformance clause, a clause beside a waiting anonymous argument and a bounded Callable clause's effect use. An
    // image the clause's other side does not equal proves nothing, the callee's own Origin in its own body included.
    [Theory]
    [InlineData("func f<U>(u: U, r: ref/i32 during b)\n    U is ref/i32 during b\n    return\nfunc use(x: ref/i32) => f(x, x)", "")]
    [InlineData("struct S\n    public func put<U>(self: ref/Self, u: U, r: ref/i32 during b)\n        U is ref/i32 during b\n        return\nfunc use(s: ref/S, x: ref/i32) => s.put(x, x)", "")]
    [InlineData("contract Put<T>\n    func put<U>(self: ref/Self, u: U, r: ref/T during a)\n        U is ref/T during a\nfunc use<C>(c: ref/C, x: ref/i32)\n    C is Put<i32>\n    c.put(x, x)", "")]
    [InlineData("func f<U, W>(u: U, w: W, r: ref/i32 during b, q: ref/i32 during c)\n    U is ref/i32 during b\n    W is ref/i32 during c\n    return\nfunc use(x: ref/i32, y: ref/i32) => f(x, y, x, y)", "")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32 during k)\n    T is Source\n    T.(Source).Item is ref/i32 during k\n    return\nfunc use<U>(u: ref/U, z: ref/i32 during m)\n    U is Source\n    U.(Source).Item is ref/i32 during m\n    f(u, z)", "")]
    [InlineData("contract Keep<X>\nfunc f<U>(u: U, r: ref/i32 during b)\n    U is Keep<ref/i32 during b>\n    return\nfunc use<W>(w: W, x: ref/i32 during p)\n    W is Keep<ref/i32 during p>\n    f(w@move, x)", "")]
    [InlineData("struct Box<U>\n    public var n: i32 = 0\n    public init(u: U, r: ref/i32 during b)\n        U is ref/i32 during b\n        return\nfunc use(x: ref/i32 during p)\n    let b = Box<ref/i32 during p>.init(x, x)", "")]
    [InlineData("func f<U, F>(u: U, r: ref/i32 during b, action: F)\n    U is ref/i32 during b\n    F is Callable<(i32) -> i32>\n    return\nfunc use(x: ref/i32) => f(x, x, func (v) => v)", "")]
    [InlineData("func f<F>(action: ref/F, y: ref/i32 during k) -> i32\n    origin s.at == k\n    F is Callable<(Cursor{s}) -> i32>\n        effect confined\n    return 0\nfunc g<G>(a: ref/G, x: ref/i32 during m) -> i32\n    origin t.at == m\n    G is Callable<(Cursor{t}) -> i32>\n        effect confined\n    return f(a, x)", "")]
    [InlineData("func f<U>(u: U, r: ref/i32 during b)\n    U is ref/i32 during b\n    f<U>(u@move, r)", "")]
    [InlineData("func f<T>(x: ref/i32 during a, y: T) -> i32\n    T is ref/i32 during a\n    return 1\nfunc use() -> i32\n    let v = 4\n    return f(v@ref, v@ref)", "")]
    [InlineData("func f<U>(u: U, r: ref/i32 during b)\n    U is ref/i32 during b\n    return\nfunc use(x: ref/i32, y: ref/i32) => f(x, y)", "UnprovenConstraint_Kd")]
    [InlineData("func f<U>(u: U, r: ref/i32 during b)\n    U is ref/i32 during static\n    return\nfunc use(x: ref/i32 during p) => f(x, x)", "UnprovenConstraint_Kd")]
    [InlineData("struct S\n    public func put<U>(self: ref/Self, u: U, r: ref/i32 during b)\n        U is ref/i32 during b\n        return\nfunc use(s: ref/S, x: ref/i32, y: ref/i32) => s.put(x, y)", "UnprovenConstraint_Kd")]
    [InlineData("contract Put<T>\n    func put<U>(self: ref/Self, u: U, r: ref/T during a)\n        U is ref/T during a\nfunc use<C>(c: ref/C, x: ref/i32, y: ref/i32)\n    C is Put<i32>\n    c.put(x, y)", "UnprovenConstraint_Kd")]
    [InlineData("func f<U, W>(u: U, w: W, r: ref/i32 during b, q: ref/i32 during c)\n    U is ref/i32 during b\n    W is ref/i32 during c\n    return\nfunc use(x: ref/i32, y: ref/i32) => f(x, y, y, x)", "UnprovenConstraint_Kd")]
    [InlineData("func f<T>(v: ref/T, y: ref/i32 during k)\n    T is Source\n    T.(Source).Item is ref/i32 during k\n    return\nfunc use<U>(u: ref/U, z: ref/i32 during m, w: ref/i32 during n)\n    U is Source\n    U.(Source).Item is ref/i32 during m\n    f(u, w)", "UnprovenConstraint_Kd")]
    [InlineData("contract Keep<X>\nfunc f<U>(u: U, r: ref/i32 during b)\n    U is Keep<ref/i32 during b>\n    return\nfunc use<W>(w: W, x: ref/i32 during p, y: ref/i32 during q)\n    W is Keep<ref/i32 during p>\n    f(w@move, y)", "UnprovenConstraint_Kd")]
    [InlineData("struct Box<U>\n    public var n: i32 = 0\n    public init(u: U, r: ref/i32 during b)\n        U is ref/i32 during b\n        return\nfunc use(x: ref/i32 during p, y: ref/i32 during q)\n    let b = Box<ref/i32 during q>.init(y, x)", "UnprovenConstraint_Kd")]
    [InlineData("func f<U, F>(u: U, r: ref/i32 during b, action: F)\n    U is ref/i32 during b\n    F is Callable<(i32) -> i32>\n    return\nfunc use(x: ref/i32, y: ref/i32) => f(x, y, func (v) => v)", "UnprovenConstraint_Kd")]
    [InlineData("func f<F>(action: ref/F, y: ref/i32 during k) -> i32\n    origin s.at == k\n    F is Callable<(Cursor{s}) -> i32>\n        effect confined\n    return 0\nfunc g<G>(a: ref/G, x: ref/i32 during m) -> i32\n    origin t.at == m\n    G is Callable<(Cursor{t}) -> i32>\n    return f(a, x)", "UnsatisfiedEffectBound_Kd")]
    [InlineData("func f<U>(u: U, r: ref/i32 during b, q: ref/i32 during c)\n    U is ref/i32 during b\n    f<U>(u@move, q, r)", "UnprovenConstraint_Kd")]
    public void ACallProvesItsConstraintsUnderItsOriginImages(string declaration, string codes)
        => Assert.Equal(codes, string.Join(' ', ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Select(static x => x.Code).Order(StringComparer.Ordinal)));

    // SPEC 15.6.4 steps 1 to 3, 10.1 step 5, 15.3.7 (PLAN G707, G700, G457 (R1)): a call's substitution is one value over both binders,
    // the Type declaring the callee and the callee, read by its signature, its Constraints, its declared Origin relations and a witness
    // comparison alike: a clause naming a parameter's elided Origin (In), the declaring Type's slot on another value of that Type (Sd, So),
    // a method called on another value in its own body (d_decl, Oq), an implementation's clause under the comparison's solved Origins (O2)
    // and a constructor's clause over its structure's slots (D). No image is read again as the callee's or the declaring Type's: a
    // clause over the declaring Type's Type slot (Gp, Gt), over both its Type and Origin slots (Hb) and over a parameter's elided Origin
    // beside them (Hi) takes each slot's image once.
    [Theory]
    [InlineData("func f<U>(u: U, r: ref/i32)\n    U is ref/i32 during r\n    return\nfunc use(x: ref/i32) => f(x, x)", "")] // In1 V
    [InlineData("struct S\n    public func put<U>(self: ref/Self, u: U, r: ref/i32)\n        U is ref/i32 during r\n        return\nfunc use(s: ref/S, x: ref/i32) => s.put(x, x)", "")] // In1m V
    [InlineData("func f<U>(u: U, r: ref/i32)\n    U is ref/i32 during r\n    return\nfunc use(x: ref/i32, y: ref/i32) => f(x, y)", "UnprovenConstraint_Kd")] // In1i I
    [InlineData("func f<U>(u: U, r: ref/i32, q: ref/i32)\n    U is ref/i32 during r\n    f<U>(u@move, q, r)", "UnprovenConstraint_Kd")] // In1r I
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, q: ref/i32 during b)\n        U is ref/i32 during b\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        other.put(y, y)", "")] // Sd1v V
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, q: ref/i32 during b)\n        U is ref/i32 during b\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        other.put(x, y)", "UnprovenConstraint_Kd")] // Sd1i I
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, q: ref/i32 during b)\n        U is ref/i32 during b\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        other.put(y, x)", "UnprovenConstraint_Kd")] // Sd2i I
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put2<U>(self: ref/Self, u: U)\n        U is ref/i32 during a\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        other.put2(x)", "UnprovenConstraint_Kd")] // Sdp I
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put2<U>(self: ref/Self, u: U)\n        U is ref/i32 during a\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        other.put2(y)", "")] // Sdpv V
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, q: ref/i32 during b)\n        U is ref/i32 during b\n        return\nfunc use(other: ref/H{o}, y: ref/i32 during o.a) => other.put(y, y)", "")] // So1v V
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, q: ref/i32 during b)\n        U is ref/i32 during b\n        return\nfunc use(other: ref/H{o}, x: ref/i32 during p, y: ref/i32 during o.a) => other.put(x, y)", "UnprovenConstraint_Kd")] // So1i I
    [InlineData(MethodOnAnotherValue, "")] // d_decl_ok V
    [InlineData("struct W {x}\n    public let r: ref/i32 during x\n    public init(r: ref/i32 during x) => self.r = r\n    public func m(self: ref/Self, y: ref/i32 during k, z: ref/i32 during j) -> ref/i32 during x\n        let w: W during k = W.init(y)\n        let p: ref/i32 during j = w.m(z, z)\n        return self.r", "UnprovenOriginRelation_Kd")] // d_decl_bad I
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32 during a)\n        U is ref/i32 during a\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32 during b)\n        U is ref/i32 during b\n        return", "")] // O2 V
    [InlineData("contract Keep<X>\ncontract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32 during a, q: ref/i32 during c)\n        U is Keep<ref/i32 during a>\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32 during b, q: ref/i32 during d)\n        U is Keep<ref/i32 during d>\n        return", "UnprovenConstraint_Kd")] // O2ki I
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32)\n        U is ref/i32 during r\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32)\n        U is ref/i32 during r\n        return", "")] // O2in V
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32, q: ref/i32)\n        U is ref/i32 during r\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32, q: ref/i32)\n        U is ref/i32 during q\n        return", "UnprovenConstraint_Kd")] // O2ini I
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32 during a)\n        U is ref/i32 during a\nstruct S {x}\n    public let k: ref/i32 during x\n    public init(k: ref/i32 during x) => self.k = k\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32 during b)\n        U is ref/i32 during b\n        return", "DeclarationOrderWarning_Kd")] // O2d V
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32 during a)\n        U is ref/i32 during a\nstruct S {x}\n    public let k: ref/i32 during x\n    public init(k: ref/i32 during x) => self.k = k\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32 during b)\n        U is ref/i32 during x\n        return", "DeclarationOrderWarning_Kd UnprovenConstraint_Kd")] // O2di I
    [InlineData("struct W {x}\n    public let r: ref/i32 during x\n    public init(r: ref/i32 during x) => self.r = r\n    public func m(self: ref/Self, y: ref/i32 during k, c: uniq/(ref/i32 during q), z: ref/i32 during q)\n        origin x outlives q\n        let w: W during k = W.init(y)\n        w.m(z, c, z)", "UnprovenOriginRelation_Kd")] // Oq1i I
    [InlineData("struct W {x}\n    public let r: ref/i32 during x\n    public init(r: ref/i32 during x) => self.r = r\n    public func m(self: ref/Self, y: ref/i32 during k, c: uniq/(ref/i32 during q), z: ref/i32 during q)\n        origin x outlives q\n        origin k outlives q\n        let w: W during k = W.init(y)\n        w.m(z, c, z)", "")] // Oq1v V
    [InlineData("struct Box<U>\n    public var n: i32 = 0\n    public init(u: U)\n        U is i32\n        return\nfunc use(x: ref/i32 during p)\n    let b = Box<i32>.init(1)", "")] // D1 V
    [InlineData("struct Box<U>\n    public var n: i32 = 0\n    public init(u: U)\n        U is i32\n        return\nfunc use(x: ref/i32 during p)\n    let b = Box.init(1)", "")] // D8 V
    [InlineData("struct Box<X>\n    public var n: i32 = 0\nstruct G<T>\n    public var n: i32 = 0\n    public func put2<U>(self: ref/Self, u: ref/U)\n        U is Box<T>\n        return\n    public func go(self: ref/Self, other: ref/G<i32>, x: ref/Box<T>, y: ref/Box<i32>)\n        other.put2(x)", "UnprovenConstraint_Kd")] // Gp1i I
    [InlineData("struct Box<X>\n    public var n: i32 = 0\nstruct G<T>\n    public var n: i32 = 0\n    public func put2<U>(self: ref/Self, u: ref/U)\n        U is Box<T>\n        return\n    public func go(self: ref/Self, other: ref/G<i32>, x: ref/Box<T>, y: ref/Box<i32>)\n        other.put2(y)", "")] // Gp1v V
    [InlineData("struct Box<X>\n    public var n: i32 = 0\nstruct G<T>\n    public var n: i32 = 0\n    public func put2<U>(self: ref/Self, u: ref/U)\n        U is Box<T>\n        return\n    public func go(self: ref/Self)\n        let g = G<i32>.put2<Box<T>>", "UnprovenConstraint_Kd")] // Fg1i I (a function reference, C5)
    [InlineData("struct Box<X>\n    public var n: i32 = 0\nstruct G<T>\n    public var n: i32 = 0\n    public func put2<U>(self: ref/Self, u: ref/U)\n        U is Box<T>\n        return\n    public func go(self: ref/Self)\n        let g = G<i32>.put2<Box<i32>>", "")] // Fg1v V
    [InlineData("struct G<T>\n    public var n: i32 = 0\n    public func put<U>(self: ref/Self, u: U, q: ref/T during b)\n        U is ref/T during b\n        return\n    public func go(self: ref/Self, other: ref/G<i32>, x: ref/T, y: ref/i32)\n        other.put(x, y)", "UnprovenConstraint_Kd")] // Gt1i I
    [InlineData("struct G<T>\n    public var n: i32 = 0\n    public func put<U>(self: ref/Self, u: U, q: ref/T during b)\n        U is ref/T during b\n        return\n    public func go(self: ref/Self, other: ref/G<i32>, x: ref/T, y: ref/i32)\n        other.put(y, y)", "")] // Gt1v V
    [InlineData("struct G<T>\n    public var n: i32 = 0\n    public func put<U>(self: ref/Self, u: U, q: ref/T during b)\n        U is ref/T during b\n        return\n    public func go(self: ref/Self, other: ref/G<i32>, x: ref/T, y: ref/i32)\n        self.put(x, x)", "")] // Gt2v V
    [InlineData("struct Two<X, Y>\n    public var n: i32 = 0\nstruct H<T> {a}\n    public let r: ref/T during a\n    public init(r: ref/T during a) => self.r = r\n    public func m<U>(self: ref/Self, u: ref/U, q: ref/i32 during b)\n        U is Two<ref/T during a, ref/i32 during b>\n        return\nfunc use(h: ref/H<i32>{o}, t: ref/Two<ref/i32 during o.a, ref/i32 during p>, q: ref/i32 during p) => h.m(t, q)", "")] // Hb1v V
    [InlineData("struct Two<X, Y>\n    public var n: i32 = 0\nstruct H<T> {a}\n    public let r: ref/T during a\n    public init(r: ref/T during a) => self.r = r\n    public func m<U>(self: ref/Self, u: ref/U, q: ref/i32 during b)\n        U is Two<ref/T during a, ref/i32 during b>\n        return\nfunc use(h: ref/H<i32>{o}, t: ref/Two<ref/i32 during o.a, ref/i32 during p>, q: ref/i32 during p, q2: ref/i32 during p2) => h.m(t, q2)", "UnprovenConstraint_Kd")] // Hb1i I
    [InlineData("struct Two<X, Y>\n    public var n: i32 = 0\nstruct H<T> {a}\n    public let r: ref/T during a\n    public init(r: ref/T during a) => self.r = r\n    public func m<U>(self: ref/Self, u: ref/U, q: ref/i32 during b)\n        U is Two<ref/T during a, ref/i32 during b>\n        return\n    public func go(self: ref/Self, other: ref/H<i32>{o}, t: ref/Two<ref/T during a, ref/i32 during p>, s: ref/Two<ref/i32 during o.a, ref/i32 during p>, q: ref/i32 during p)\n        other.m(t, q)", "UnprovenConstraint_Kd")] // Hb3i I
    [InlineData("struct Two<X, Y>\n    public var n: i32 = 0\nstruct H<T> {a}\n    public let r: ref/T during a\n    public init(r: ref/T during a) => self.r = r\n    public func m<U>(self: ref/Self, u: ref/U, q: ref/i32 during b)\n        U is Two<ref/T during a, ref/i32 during b>\n        return\n    public func go(self: ref/Self, other: ref/H<i32>{o}, t: ref/Two<ref/T during a, ref/i32 during p>, s: ref/Two<ref/i32 during o.a, ref/i32 during p>, q: ref/i32 during p)\n        self.m(t, q)", "")] // Hb3v V
    [InlineData("struct Two<X, Y>\n    public var n: i32 = 0\nstruct H<T> {a}\n    public let r: ref/T during a\n    public init(r: ref/T during a) => self.r = r\n    public func m<U>(self: ref/Self, u: ref/U, q: ref/i32 during b)\n        U is Two<ref/T during a, ref/i32 during b>\n        return\n    public func go(self: ref/Self, other: ref/H<i32>{o}, t: ref/Two<ref/T during a, ref/i32 during p>, s: ref/Two<ref/i32 during o.a, ref/i32 during p>, q: ref/i32 during p)\n        other.m(s, q)", "")] // Hb4v V
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, w: ref/i32)\n        U is ref/i32 during w\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        other.put(x, y)", "UnprovenConstraint_Kd")] // Hi1i I
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, w: ref/i32)\n        U is ref/i32 during w\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        other.put(y, y)", "")] // Hi1v V
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(r: ref/i32 during a) => self.r = r\n    public func put<U>(self: ref/Self, u: U, w: ref/i32)\n        U is ref/i32 during w\n        return\n    public func go(self: ref/Self, other: ref/H{o}, x: ref/i32 during a, y: ref/i32 during o.a)\n        self.put(x, x)", "")] // Hi2v V
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32 during a)\n        U is ref/i32 during a\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32)\n        U is ref/i32 during r\n        return", "")] // O2inm V
    [InlineData("contract Put\n    func put<U>(self: uniq/Self, u: U, r: ref/i32 during a, q: ref/i32 during c)\n        U is ref/i32 during a\nstruct S\n    Self is Put\n    public func put<U>(self: uniq/Self, u: U, r: ref/i32, q: ref/i32)\n        U is ref/i32 during q\n        return", "UnprovenConstraint_Kd")] // O2inmi I
    public void ACallIsJudgedUnderItsOneSubstitution(string declaration, string codes)
        => Assert.Equal(codes, string.Join(' ', ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Select(static x => x.Code).Order(StringComparer.Ordinal)));

    // The same call through MIR (PLAN G707): MIR reads the call's interned substitution (Subst), column for column the value Binding
    // judged it under, and builds the body without a finding.
    [Fact]
    public void ACallUnderItsOneSubstitutionBuildsMir()
    {
        var c = MirBuildTest.Compile(RegionPrelude + MethodOnAnotherValue + "\n");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Empty(c.BuildMir().Findings);
    }

    // SPEC 15.3.4: the same set name repeats only across declarations that cannot see each other (two `when` lists); within one
    // declaration, or where an enclosing declaration's set is visible, it is a duplicate.
    [Theory]
    [InlineData("func f<T, U>(value: ref/T, other: ref/U, y: ref/i32) -> i32\n    origin k.at == y\n    T is Source\n    U is Source\n    T.(Source).Item is (Cursor{k}).Item\n    U.(Source).Item is (Cursor{k}).Item\n    return 0", true)]
    [InlineData("struct W<T> {w}\n    public let v: i32\n    Self is Source when T is (Cursor{k}).Item, T is (Cursor{k}).Item\n        associate Item is i32", true)]
    [InlineData("struct W<T, U> {w}\n    public let v: i32\n    Self is Marker when T is (Cursor{m}).Item\n    Self is Shown when U is (Cursor{m}).Item", false)]
    public void ASetRepeatsOnlyAcrossInvisibleDeclarations(string declaration, bool duplicate)
        => Assert.Equal(duplicate, ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Any(static x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd)));

    // SPEC 15.3.4, 15.3.2: a `when` list's set is seen by its implementation block only; a set no relation relates is no public Origin in
    // storage, whatever its kind (a Field, a payload, a base: a hidden Origin); a Contract-wide clause's set is seen by no requirement.
    [Theory]
    [InlineData("struct W<T> {w}\n    public let v: i32\n    Self is Marker when T is (Cursor{m}).Item\n        public func get(self: ref/Self, y: ref/i32 during m.at) -> i32 => self.v", "    public func get", false)]
    [InlineData("struct W<T> {w}\n    public let v: i32\n    Self is Marker when T is (Cursor{m}).Item\n    public func get(self: ref/Self, y: ref/i32 during m.at) -> i32 => self.v", "    public func get", true)]
    [InlineData("struct Holder<T, U> {h}\n    T is Source\n    U is Source\n    U.(Source).Item is (Cursor{j}).Item\n    public let v: ref/i32 during j.at", "    public let v", true)]
    [InlineData("enum Choice<T, U> {h}\n    T is Source\n    U is Source\n    U.(Source).Item is (Cursor{j}).Item\n    One(ref/i32 during j.at)\n    Two", "    One(", true)]
    [InlineData("contract Lens\n    associate Item\n    Self.Item is (Cursor{n}).Item\n    func read(self: ref/Self) -> ref/i32 during n.at", "    func read", true)]
    public void ASetIsSeenOnlyWhereItsDeclarationIs(string declaration, string line, bool invalid)
    {
        var source = RegionPrelude + declaration + "\n";
        var at = source[..source.IndexOf(line, StringComparison.Ordinal)].Count(static c => c == '\n');
        Assert.Equal(invalid, ContextualCaptureTest.PublishedRecords(source).Any(x => x.Code == nameof(DiagnosticCode.InvalidOriginBinding_Kd) &&
            source[..x.Span.Start].Count(static c => c == '\n') == at));
    }

    // SPEC 23.1.2: binding a header for its sets keeps the header's records its own, so an invalid parameter Type and an independent
    // failure of the Constraint that triggered it each keep their record.
    [Fact]
    public void AHeaderBoundForItsSetsKeepsItsOwnRecords()
    {
        var source = RegionPrelude + "func f<T>(value: ref/T, y: ref/i32, w: Missing) -> i32\n    origin k.at == y\n    T is Source\n" +
            "    T.(Source).Item is (Cursor{k}).Nope\n    return 0\n";
        var lines = ContextualCaptureTest.PublishedRecords(source).Select(x => source[..x.Span.Start].Count(static c => c == '\n')).ToArray();
        var header = source[..source.IndexOf("w: Missing", StringComparison.Ordinal)].Count(static c => c == '\n');
        var constraint = source[..source.IndexOf(".Nope", StringComparison.Ordinal)].Count(static c => c == '\n');
        Assert.Contains(header, lines);
        Assert.Contains(constraint, lines);
    }

    // SPEC 5.7, 15.3.2: a raw layer's `self` names the holding value on an inline layer of storage (a Field's or payload's Type, or a
    // Tuple or fixed-array element of one), read from the one storage predicate (StorageType), and nothing elsewhere in storage: a
    // Field's Type argument or Function Type, a payload's Function Type and a base clause all get InvalidOriginBinding_Kd.
    [Theory]
    [InlineData("struct S\n    public let p: raw/i32 during self", "")]
    [InlineData("struct S\n    public let p: (raw/i32 during self, i32)", "")]
    [InlineData("enum E\n    One(raw/i32 during self)\n    Two", "")]
    [InlineData("open struct Box<T>\n    public let v: i32\nstruct S\n    public let p: Box<raw/i32 during self>", "InvalidOriginBinding_Kd")]
    [InlineData("enum E\n    One((raw/i32 during self) -> ())\n    Two", "InvalidOriginBinding_Kd")]
    [InlineData("struct S\n    public let f: (raw/i32 during self) -> ()", "InvalidOriginBinding_Kd")]
    [InlineData("open struct Box<T>\n    public let v: i32\nstruct D : Box<raw/i32 during self>\n    public let w: i32", "InvalidOriginBinding_Kd")]
    public void RawSelfNamesOnlyInlineStorage(string declaration, string codes)
    {
        var records = ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Select(static x => x.Code).ToArray();
        Assert.Equal(codes, string.Join(' ', records.Where(static x => x is nameof(DiagnosticCode.InvalidOriginBinding_Kd) or nameof(DiagnosticCode.MissingOriginBinding_Kd))));
    }
}
