// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G647: a walk of one Type's parts (a layer walk, or a recursive walk of its components) ends where the Type ends and has
/// no count of its own, so a program is judged alike at any number of layers up to IMPL A.4's Type height H = 8,192. This file owns the
/// layered forms the acceptance rows of the owning classes share (<see cref="LayeredForm"/>), the walks' scaling rows and the stack row.
/// A named Origin on each of n nested layers costs Origin-proof work that grows faster than the layers (PLAN G647's design section 7: the
/// premise environment and the proof's closure, not a layer walk), which keeps the written sizes small.</summary>
public partial class TypeDagScalingTest
{
    private const string LayerTypes =
        "struct Node\n    public var weight: i32\n    public init(weight: i32) => self.weight = weight\n    public func load(self: ref/Self) -> i32 => self.weight\n" +
        "struct Point\n    Self is Equatable\n    public var x: i32\n    public init(x: i32) => self.x = x\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.x == other.x\n" +
        "struct Q\n    public var v: i32\n    public init(v: i32) => self.v = v\n" +
        "func g(v: ref/i32) -> i32 => 0\n";

    private const string LayerMain = "public func main()\n    Console.writeLine(\"p\")\n";

    // SPEC 3.4.3, 13.5.5: a Field read, a receiver call, a shared-reference argument, arithmetic, a comparison and a `for` through n
    // layers. Each walk's steps grow with the layers: Binding's layer walks and the component walks of the fits (TermWalkWork), the
    // layers HIR publishes (HirPlace.Layers: one step of its loop each) and the projections the MIR builder forms (one per layer its
    // Follow takes). The sizes pass every count the walks had (8, 16, 32, 64) where the proof cost of the named Origins allows.
    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 10)]
    public void LayerWalksAreLinearInTheLayers(bool pair, int size)
    {
        var work = new[] { size, 2 * size, 4 * size }.Select(n => Work(pair, n)).ToArray();
        foreach (var measure in new Func<(long Terms, long Layers, long Projections), long>[] { static x => x.Terms, static x => x.Layers, static x => x.Projections })
        {
            var steps = work.Select(measure).ToArray();
            Assert.True(steps[1] > steps[0] && MirBorrowsTest.Grows(steps, 2), string.Join(' ', work));
        }

        static (long Terms, long Layers, long Projections) Work(bool pair, int layers)
        {
            var layered = pair ? (Func<int, string, string>)PairLayers : (n, terminal) => RefLayers(n, terminal);
            var c = MirBuildTest.Compile(LayerTypes +
                $"func f{(pair ? "<s/T>" : string.Empty)}(x: {layered(layers, "Node")}, y: {layered(layers, "Array<i32>")}, z: {layered(layers, "i32")}{(pair ? ", m: s/T" : string.Empty)}) -> i32\n" +
                (pair ? "    s is value or valueborrow\n" : string.Empty) +
                "    var total: i32 = x.weight + x.load() + g(z) + (z + 1)\n    for e in y\n        total += e\n    return if z < z => total else => total + 1\n" + LayerMain);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            var terms = c.Binding.TermWalkWork;
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records(MirRecordClass.Construction));
            var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
            return (terms, HirColumnsTest.Reachable(c.Kotonoha).Sum(static x => (long)HirTables.PlaceOf(x).Layers), body.Projections.Length);
        }
    }

    // PLAN G348, DECISIONS BU-134: the recursive walks of a Type's components recurse once per level, so at most H deep, inside the
    // compile stack's margin. D13<i32>.Item has height H = 8,192 (TypeHeightLimitTest's doubling declarations) and D12<i32>.Item half
    // of it, which a borrow or a Function Type of it stays under: an annotated local (AddTypeClauseObligations), calls that take one by
    // value and by reference (CollectCallRelations), a generic call that infers it and returns it (MentionsSlots, the fit's
    // FailedOriginRelation) and a function reference with it written (SameExceptOmittedOrigins, SolveOmittedOrigins, InputOrigins) are
    // judged with no stack failure, and the counted walks went the Type's height; one doubling higher is the height record alone.
    [Fact]
    public void TheComponentWalksReachTheHeightLimitWithoutAStackFailure()
    {
        const string Uses = "func id<T>(v: T) -> T => v@move\nfunc take(v: {0}) -> {0} => v@move\nfunc near(v: ref/{1}) -> i32 => 0\n" +
            "func p(x: {0}, w: {1}) -> {0}\n    let y: {0} = x@move\n    let n = near(w@ref)\n    let h = id<{1}>\n    return id(take(y@move))\n";
        var c = MinimalEmissionTest.Analyze(TypeHeightLimitTest.Doubling(13) + string.Format(Uses, "D13<i32>.Item", "D12<i32>.Item") + LayerMain);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Binding.TermWalkWork >= 2 * 8192, $"{c.Binding.TermWalkWork}");

        var over = DiagnosticCorpus.Check(TypeHeightLimitTest.Doubling(14) + string.Format(Uses, "D14<i32>.Item", "D13<i32>.Item") + LayerMain).Diagnostics;
        Assert.NotEmpty(over);
        Assert.All(over, static x => Assert.Equal(nameof(DiagnosticCode.TypeHeightLimit_Kd), x.Code));
    }

    // SPEC 15.4.2: a borrow layer nested in an input names its Origin, so each layer below the outermost one names a_k, the innermost one
    // `innermost` where given.
    internal static string RefLayers(int layers, string terminal, string? innermost = null)
    {
        var inner = terminal;
        for (var k = layers; k > 1; k--)
        {
            inner = $"ref/{Operand(inner)} during {(k == layers && innermost is not null ? innermost : "a" + k)}";
        }

        return "ref/" + Operand(inner);
    }

    // SPEC 13.5.5: n qualifying pair layers of one selector s (`s is value or valueborrow`).
    internal static string PairLayers(int layers, string terminal)
    {
        var inner = terminal;
        for (var k = layers; k > 1; k--)
        {
            inner = $"s/{Operand(inner)} during a{k}";
        }

        return "s/" + Operand(inner);
    }

    // One operation on x through `layers` ref or qualifying pair layers, valid or its invalid twin, whose terminal lacks the operation.
    // The forms and their SPEC rules: a Field read and a receiver below the layers (3.4.3, 7.3; Q has neither); a `for` Subject (14.6.2,
    // 15.1.6; i32 is not iterable); built-in arithmetic and comparison of the terminals (3.5.3, 13.1, 13.4; bool is not numeric or
    // ordered, Q not Equatable); a Case Pattern (14.8.1; i32 has no Case); the shared-reference row at an argument (10.2: layers ending
    // in i32 give one ref/i32; bool does not); a string comparison through safe reference layers (13.4; i32 is no string).
    internal static string LayeredForm(string form, bool pair, int layers, bool valid)
    {
        var (good, bad, result, body) = form switch
        {
            "field" => ("Node", "Q", "i32", "    return x.weight\n"),
            "recv" => ("Node", "Q", "i32", "    return x.load()\n"),
            "for" => ("Array<i32>", "i32", "i32", "    var total: i32 = 0\n    for v in x\n        total += v\n    return total\n"),
            "arith" => ("i32", "bool", "i32", "    return x + 1\n"),
            "match" => ("Option<i32>", "i32", "i32", "    match x\n        .Some(let v) => return v\n        .None => return 0\n"),
            "eq" => ("Point", "Q", "bool", "    return x == x\n"),
            "lt" => ("i32", "bool", "bool", "    return x < x\n"),
            "argref" => ("i32", "bool", "i32", "    return g(x)\n"),
            "streq" => ("string", "i32", "bool", "    return x == \"a\"\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
        var terminal = valid ? good : bad;
        return LayerTypes + (pair ? $"func f<s/T>(x: {PairLayers(layers, terminal)}, m: s/T) -> {result}\n    s is value or valueborrow\n"
            : $"func f(x: {RefLayers(layers, terminal)}) -> {result}\n") + body + LayerMain;
    }

    // The acceptance rows' one assertion: the valid form has no record, and its invalid twin the records it has through 2 layers.
    internal static void AssertLayeredTwins(string form, bool pair, int layers)
    {
        Assert.Equal("clean", DiagnosticCorpus.RecordProfile(LayeredForm(form, pair, layers, true), "Application"));
        var shallow = DiagnosticCorpus.RecordProfile(LayeredForm(form, pair, 2, false), "Application");
        Assert.NotEqual("clean", shallow);
        Assert.Equal(shallow, DiagnosticCorpus.RecordProfile(LayeredForm(form, pair, layers, false), "Application"));
    }

    private static string Operand(string type) => type.Contains('/') || type.Contains(' ') ? $"({type})" : type;
}
