// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OriginRedesignTest
{
    [Theory]
    [InlineData("struct V<T>\n    let value: ref{source}/T")]
    [InlineData("func identity<T>(x: ref{s}/T) -> ref{s}/T => x")]
    [InlineData("func identity(x: V<i32>) -> V<i32>{r}\n    origin r.source == x.source\n    return x")]
    [InlineData("struct W {s}\n    let value: V<i32>{v}\n        origin v.source == s")]
    [InlineData("func identity(x: V<i32>{v}) -> V<i32>{r}\n    origin r.source == v.source\n    return x")]
    [InlineData("var pending: V<i32>{p}\n    origin p.source == static")]
    [InlineData("func identity(x: V<i32>) -> V<i32>{r}\n    origin r.source == x.source\n    let value: V<i32> = x\n    return value")]
    [InlineData("func identity(x: ref/V<i32>) -> ref{x.source}/i32 => x.value")]
    [InlineData("func identity(x: V<i32>{v}) -> V<i32>{r}\n    origin v.source == r.source\n    return x")]
    [InlineData("func shorten(x: V<i32>) -> V<i32>{r}\n    origin x.source outlives r.source\n    return x@move\nfunc use(x: V<i32>)\n    let y = shorten(x@move)")]
    [InlineData("func shorten(x: ref{a}/i32, y: ref{b}/i32) -> ref{b}/i32\n    origin a outlives b\n    return x")]
    [InlineData("func same(x: V<i32>, y: V<i32>)\n    origin x.source == y.source\n    ()\nfunc use(x: V<i32>, y: V<i32>) => same(x@move, y@move)")]
    [InlineData("func bounded(x: ref{a}/i32, y: ref{b}/i32)\n    origin a outlives b\n    ()\nfunc use(x: ref/i32, y: ref/i32) => bounded(x, y)")]
    [InlineData("func forward(x: ref{a}/i32, y: ref{b}/i32) -> ref{b}/i32\n    origin a outlives b\n    let result: ref{b}/i32 = x\n    return result")]
    [InlineData("func composite(x: ref{a}/i32, y: ref{b}/i32, z: ref{c}/i32) -> ref{b and c}/i32\n    origin a outlives b and c\n    return x")]
    [InlineData("func call(callback: (ref/i32) -> V<i32>{v}) => ()")]
    [InlineData("func call(callback: (V<i32>{v}) -> (), value: V<i32>)\n    origin v.source == value.source\n    ()")]
    [InlineData("func f(x: ref/i32)\n    var pending: V<i32>\n        origin pending.source == x")]
    [InlineData("struct Bound {a, b}\n    origin a outlives b\n    let source: ref{a}/i32\n    let view: V<i32>{v}\n        origin v.source == a\n        origin v.source outlives b")]
    [InlineData("func f(x: ref{a}/i32, y: ref{b}/i32)\n    origin a outlives b\n    origin b outlives a\n    ()")]
    [InlineData("func f<F>(x: V<i32>, callback: F)\n    F is Callable<(V<i32>{v}) -> ()>\n    origin v.source == x.source\n    ()")]
    [InlineData("func f(x: uniq/(ref{a}/i32), y: ref{b}/i32) -> uniq/(ref{b}/i32)\n    origin a outlives b\n    origin b outlives a\n    return x")]
    [InlineData("func f(x: ref{a}/i32, y: ref{b}/i32) -> ref{b}/i32\n    origin (a) outlives b\n    return x")]
    [InlineData("struct S {source}\n    let value: ref{source}/i32\n    let source: i32\n    func f(x: V<i32>) -> ref{source}/i32 => $abort(\"unused\")")]
    [InlineData("func identity<T>(x: T) -> T => x@move\nfunc f(x: V<i32>)\n    let y = identity<V<i32>{v}>(x@move)\n        origin v.source == x.source")]
    [InlineData("func f(x: ref/V<i32>)\n    let y = x@ref/V<i32>{v}\n        origin v.source == x.source")]
    public void CompletesDeclarationOriginContracts(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var prefix = source.StartsWith("struct V", StringComparison.Ordinal) ? string.Empty : "struct V<T> {source}\n    public let value: ref{source}/T\n";
        c.Kotonoha.AddSource(new SourceDocument("redesign.kimi", prefix + source));
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        for (var pass = 0; pass < 2; pass++)
        {
            Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Node.CodeContext.SourceDocument?.Path}: {x.Node}: {x.Node.BindingFailure}")));
        }
    }

    [Theory]
    [InlineData("struct Wrong\n    let a: ref{source}/i32\n    let b: ref{souce}/i32")]
    [InlineData("struct Wrong {}\n    let a: ref{source}/i32")]
    [InlineData("struct Wrong {source}\n    let a: ref{souce}/i32")]
    [InlineData("func f(x: V<i32>{v}, y: V<i32>{v}) => ()")]
    [InlineData("func f(x: V<i32>{x}) => ()")]
    [InlineData("func f(x: V<i32>{s}, y: ref{s}/i32) => ()")]
    [InlineData("func f(x: V<i32>{v}) -> ref{v}/i32 => $abort(\"bad\")")]
    [InlineData("func f(callback: (ref{fresh}/i32) -> ()) => ()")]
    [InlineData("func f(x: ref/i32)\n    var pending: V<i32>{p}\n        origin p.source outlives x")]
    [InlineData("struct Wrong {a, b}\n    let item: V<i32>{v}\n        origin v.source == a\n        origin a outlives b")]
    [InlineData("func f(callback: (ref/i32) -> V<i32>{v}) -> ref{v.source}/i32 => $abort(\"bad\")")]
    [InlineData("func f(x: ref{a}/i32, y: ref{b}/i32, z: ref{c}/i32) -> ref{b}/i32\n    origin a outlives b and c\n    return x")]
    [InlineData("func f(x: ref/i32)\n    let a: V<i32>{v} = $abort(\"a\")\n        origin v.source == x\n    let b: V<i32>{v} = $abort(\"b\")")]
    [InlineData("struct Marker {a}\nfunc shorten(x: Marker) -> Marker{r}\n    origin x.a outlives r.a\n    return x")]
    [InlineData("func f(x: V<i32>, y: V<i32>)\n    var pending: V<i32>{p}\n        origin x.source outlives p.source")]
    [InlineData("func f(x: ref{a}/i32)\n    let a = 1\n    var pending: ref{a}/i32")]
    [InlineData("func f<F>(x: ref{a}/i32, callback: F)\n    F is Callable<(ref{a}/i32) -> ()>\n    ()")]
    public void RejectsInvalidOriginDeclarations(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("invalid.kimi", "struct V<T> {source}\n    let value: ref{source}/T\n" + source));
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("struct Marker {}")]
    [InlineData("struct V<T> {source}\n    let item: ref{source}/T")]
    [InlineData("func f<T>(x: ref{s}/T) -> ref{s}/T\n    origin s outlives s\n    return x")]
    [InlineData("func f<T>(x: View<T>) -> View<T>{r}\n    origin r.source == x.source\n    return x")]
    [InlineData("struct W<T> {s}\n    let item: View<T>{v}\n        origin v.source == s")]
    [InlineData("var pending: View<i32>{p}\n    origin p.source == static")]
    [InlineData("let view: View<i32> = make()\n    origin view.source == static")]
    [InlineData("enum E<T> {s}\n    Some(View<T>{v})\n        origin v.source == s\n    None")]
    [InlineData("struct S<T> {s}\n    associate C.Element is View<T>{v}\n        origin v.source == s")]
    [InlineData("alias Helpers => Family.Helpers{h}\n    origin h.source == static")]
    [InlineData("contract C\n    func f<T>(self, x: View<T>) -> View<T>{r}\n        origin r.source == x.source")]
    [InlineData("struct S {s}\n    computed value: ref{s}/i32\n        get(self: ref/Self) -> ref{s}/i32\n            origin s outlives self\n            return getValue()")]
    public void DeclarationRelationsRoundTrip(string source)
    {
        var tree = ParseTestHelper.Parse(source);
        Assert.Empty(tree.DiagnosticCollection.GetArray());
        var builder = default(IndentedStringBuilder);
        try
        {
            tree.RootKoto.UnparseAll(ref builder);
            var written = builder.ToString();
            var reloaded = ParseTestHelper.Parse(written);
            Assert.True(reloaded.DiagnosticCollection.GetArray().Length == 0, written + "\n" + string.Join("\n", reloaded.DiagnosticCollection.GetArray().Select(x => x.Message)));
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
    [InlineData("func f {s}(x: ref{s}/i32) => ()")]
    [InlineData("struct S\n    init {s}(x: ref{s}/i32) => ()")]
    [InlineData("struct S {a : b, b}")]
    [InlineData("func f(x: View<i32>{source => static}) => ()")]
    [InlineData("func f(x: View<i32>{static}) => ()")]
    [InlineData("func f(x: View<i32>{a.source}) => ()")]
    [InlineData("func f(x: View<i32>{}) => ()")]
    [InlineData("func f(x: ref/i32)\n    ()\n    origin x == x")]
    public void RejectsRemovedOrMisplacedSyntax(string source)
        => Assert.NotEmpty(ParseTestHelper.Parse(source).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("Implicit", "func read(x: ref{s}/Cell) -> i32 => x.value\nlet value = Cell.init()\nif read(value@ref) != 21 => $abort(\"origin\")")]
    [InlineData("Equality", "func read(x: ref{a}/Cell, y: ref{b}/Cell) -> i32\n    origin a == b\n    return x.value + y.value\nlet a = Cell.init()\nlet b = Cell.init()\nif read(a@ref, b@ref) != 42 => $abort(\"origin\")")]
    [InlineData("Relation", "func read(x: ref{a}/Cell, y: ref{b}/Cell) -> i32\n    origin a outlives b\n    return x.value + y.value\nlet a = Cell.init()\nlet b = Cell.init()\nif read(a@ref, b@ref) != 42 => $abort(\"origin\")")]
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
            "func update(x: uniq{a}/Cell, y: uniq{b}/Cell)\n    origin a == b\n    x.value = 1\n    y.value = 2\n" +
            $"var first = Cell.init()\nvar second = Cell.init()\nupdate({arguments})";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(valid == c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        Assert.Equal(valid, c.Emission.Validate(out _));
    }

    [Fact]
    public void WarmRelationsAndCallsKeepAllocationsBounded()
    {
        var c = MinimalEmissionTest.Analyze("struct Cell\n    public var value: i32 = 0\n" +
            "func update(x: uniq{a}/Cell, y: uniq{b}/Cell)\n    origin a == b\n    x.value = 1\n    y.value = 2\n" +
            "var first = Cell.init()\nvar second = Cell.init()\nupdate(first@uniq, second@uniq)");
        Assert.True(c.Emission.Validate(out _));
        var bindingBytes = AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Origin relation Binding failed.");
            }
        });
        c.Binding.CheckStartup(OutputKind.Application);
        var ownershipBytes = AllocationMeasurement.Measure(() => c.Ownership.Analyze());
        var emissionBytes = AllocationMeasurement.Measure(() => c.Emission.WriteIr(TextWriter.Null, out _));
        // Eight measured warm passes: Binding retains a bounded 288 bytes per pass.
        Assert.True(
            bindingBytes <= 8 * 288 && ownershipBytes == 0 && emissionBytes == 0,
            $"Binding: {bindingBytes}; ownership: {ownershipBytes}; emission: {emissionBytes}");
    }
}
