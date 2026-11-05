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

    [Theory]
    [InlineData("struct V<T> {source}\n    struct Inner\n        let value: ref/T during source")]
    [InlineData("func identity<T>(x: ref/T during s) -> ref/T during s => x")]
    [InlineData("func identity(x: V<i32>) -> V<i32>{r}\n    origin r.source == x.source\n    return x")]
    [InlineData("struct W {s}\n    let value: V<i32>{v}\n        origin v.source == s")]
    [InlineData("func identity(x: V<i32>{v}) -> V<i32>{r}\n    origin r.source == v.source\n    return x")]
    [InlineData("var pending: V<i32>{p}\n    origin p.source == static")]
    [InlineData("func identity(x: V<i32>) -> V<i32>{r}\n    origin r.source == x.source\n    let value: V<i32> = x\n    return value")]
    [InlineData("func identity(x: ref/V<i32>) -> ref/i32 during x.source => x.value")]
    [InlineData("func identity(x: V<i32>{v}) -> V<i32>{r}\n    origin v.source == r.source\n    return x")]
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
    // (its block sees it, the Type's other members do not), and a Contract-wide clause owns its own. An unrelated set keeps
    // MissingOriginBinding_Kd until G417 (3) U2.
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
    [InlineData("func f<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is (Cursor{k}).Item\n    return 0", "InvalidConstraint_Kd InvalidConstraint_Kd MissingOriginBinding_Kd")]
    public void ConstraintSetsAreTheirDeclarations(string declaration, string codes)
        => Assert.Equal(codes, string.Join(' ', ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Select(static x => x.Code)));

    // SPEC 15.3.4: the same set name repeats only across declarations that cannot see each other (two `when` lists); within one
    // declaration, or where an enclosing declaration's set is visible, it is a duplicate.
    [Theory]
    [InlineData("func f<T, U>(value: ref/T, other: ref/U, y: ref/i32) -> i32\n    origin k.at == y\n    T is Source\n    U is Source\n    T.(Source).Item is (Cursor{k}).Item\n    U.(Source).Item is (Cursor{k}).Item\n    return 0", true)]
    [InlineData("struct W<T> {w}\n    public let v: i32\n    Self is Source when T is (Cursor{k}).Item, T is (Cursor{k}).Item\n        associate Item is i32", true)]
    [InlineData("struct W<T, U> {w}\n    public let v: i32\n    Self is Marker when T is (Cursor{m}).Item\n    Self is Shown when U is (Cursor{m}).Item", false)]
    public void ASetRepeatsOnlyAcrossInvisibleDeclarations(string declaration, bool duplicate)
        => Assert.Equal(duplicate, ContextualCaptureTest.PublishedRecords(RegionPrelude + declaration + "\n").Any(static x => x.Code == nameof(DiagnosticCode.DuplicateBinding_Kd)));

    // SPEC 15.3.4, 15.3.2: a `when` list's set is seen by its implementation block only; a set no relation relates is no public Origin in
    // storage; a Contract-wide clause's set is seen by no requirement. A `when` list holds no relation, so until G417 (3) U2 its set's own
    // failure rejects the block's use as well (the first row); U2 makes that row valid.
    [Theory]
    [InlineData("struct W<T> {w}\n    public let v: i32\n    Self is Marker when T is (Cursor{m}).Item\n        public func get(self: ref/Self, y: ref/i32 during m.at) -> i32 => self.v", "    public func get", true)]
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
