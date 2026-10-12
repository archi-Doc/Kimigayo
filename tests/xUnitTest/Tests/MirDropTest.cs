// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>Drop elaboration on MIR (A2, SPEC 16.2, 16.3.2, 15.1.3, 14.10.3; docs/dev/DECISIONS.md R4-M6a): each verdict and each
/// destructor rule with a valid program and an invalid one, through the dump of the named body (<c>MirDump</c>: its flags, each Drop's
/// verdict groups and each location's flag points, without source suffixes) and the problems <c>Compilation.BuildMir</c> finds.</summary>
public sealed partial class MirDropTest(ITestOutputHelper output)
{
    private const string Resource = "struct Resource\n    var name: string\n    public init(name: string) => self.name = name@move\n" +
        "func make() -> Resource => Resource.init(\"r\")\nfunc consume(r: Resource) => ()\n";

    private const string Box = Resource + "struct Box\n    public var value: Resource\n    public init(value: Resource) => self.value = value@move\n" +
        "    drop => ()\n";

    // SPEC 16.2.1: a value initialized on every path is destroyed (Keep); one moved on every path is not (Elide), and needs no flag.
    [Fact]
    public void KeepWhereInitializedElideWhereMoved()
    {
        Assert.Equal(["drop _1 -> bb2 [keep]"], Lines(Resource + "func f()\n    let r = make()\n", "f"));
        Assert.Equal(["drop _3 -> bb3 [elide]", "drop _1 -> bb4 [elide]"], Lines(Resource + "func f()\n    let r = make()\n    consume(r@move)\n", "f"));
    }

    // SPEC 15.1.4, 16.2.1: a value moved on some paths only is destroyed under a flag, which mirrors its initialization: set where it is
    // defined (an argument at the entry), cleared where it is moved, dropped or its storage begins or ends. A drop tree node shared by two
    // exits, one after the Move, has one verdict; reinitializing on the moving path makes it Keep again.
    [Fact]
    public void FlagWhereInitializedOnSomePaths()
    {
        Assert.Equal(
            ["flags 1", "StorageLive(_2) [clear f0]", "_2 = make() -> bb1 [set f0]", "drop _2 -> bb7 [flag f0] [clear f0]", "_6 = move _2 [clear f0]",
                "drop _6 -> bb6 [elide]", "StorageDead(_2) [clear f0]"],
            Lines(Resource + "func f(flag: bool)\n    let r = make()\n    if flag => consume(r@move)\n", "f"));
        Assert.Equal(
            ["flags 1 [set f0]", "drop _2 -> bb6 [flag f0] [clear f0]", "_6 = move _2 [clear f0]", "drop _6 -> bb5 [elide]"],
            Lines(Resource + "func f(flag: bool, r: Resource)\n    if flag => consume(r@move)\n", "f"));
        const string Exits = Resource + "func log() => ()\nfunc f(flag: bool) -> i32\n    let r = make()\n    defer => log()\n    if flag\n" +
            "        consume(r@move)\n        return 1\n    return 2\n";
        Assert.Contains("drop _2 -> bb11 [flag f0] [clear f0]", Lines(Exits, "f"));
        Assert.Equal(
            ["drop _2 -> bb10 [keep]", "drop _6 -> bb6 [elide]", "drop _2 -> bb8 [elide]", "drop _8 -> bb9 [elide]"],
            Lines(Resource + "func f(flag: bool)\n    var r = make()\n    if flag\n        consume(r@move)\n        r = make()\n", "f"));
    }

    // SPEC 16.2.2, 16.3.2: after a Partial Move, the remaining parts are destroyed (Open): each child path by its own verdict, the components
    // without one by the own bit; restoring the part makes the value whole again, and a part moved on some paths only is flagged. An owned
    // decomposition leaves the enum Subject's other payloads and Cases with the own bit.
    [Fact]
    public void OpenAfterAPartialMove()
    {
        const string Pair = "func see(p: (string, i32)) => ()\nfunc f(flag: bool)\n    var pair: (string, i32) = (\"Alice\", 30)\n";
        Assert.Contains("drop _2 -> bb2 [open (.0 elide)]", Lines(Pair + "    let name = pair.0@move\n", "f"));
        Assert.Equal(
            ["drop _2.0 -> bb1 [elide]", "drop _5 -> bb2 [elide]", "drop _3 -> bb3 [keep]", "drop _2 -> bb4 [keep]"],
            Lines(Pair + "    let name = pair.0@move\n    pair.0 = \"Bob\"\n", "f"));
        Assert.Contains("drop _2 -> bb7 [open (.0 flag f0)] [clear f0]", Lines(Pair + "    if flag => see((pair.0@move, 1))\n", "f"));
        const string Decomposed = Resource + "enum Packet\n    Data(Resource)\n    Empty\nfunc f(packet: Packet)\n    match packet@move\n" +
            "        .Data(let r) => consume(r@move)\n        _ => ()\n";
        Assert.Contains("drop _3 -> bb9 [open ((_ as case 0) open (.0 flag f0))] [clear f0]", Lines(Decomposed, "f"));
    }

    // SPEC 15.1.3, 16.3.2: a layer whose Type declares a drop must be complete wherever that drop runs: a Partial Move not restored on every
    // path to its scope exit or a replacement is MovedPlace_Kd at the Drop, a base layer's own drop included; restored on every path, or
    // without an own drop, the parts are destroyed as they are.
    [Fact]
    public void ALayerWithItsOwnDropIsCompleteAtItsDrop()
    {
        Assert.Empty(Problems(Box + "func f()\n    var box = Box.init(make())\n    consume(box.value@move)\n    box.value = make()\n"));
        Assert.Empty(Problems(Resource + "struct Plain\n    public var value: Resource\n    public init(value: Resource) => self.value = value@move\n" +
            "func f()\n    var plain = Plain.init(make())\n    consume(plain.value@move)\n"));
        Assert.Equal(["MovedPlace_Kd var box = Box.init(make()) 1"], Problems(Box + "func f()\n    var box = Box.init(make())\n    consume(box.value@move)\n"));
        Assert.Equal(
            ["MovedPlace_Kd var box = Box.init(make()) 1"],
            Problems(Box + "func f(flag: bool)\n    var box = Box.init(make())\n    consume(box.value@move)\n    if flag => box.value = make()\n"));
        Assert.Equal(
            ["MovedPlace_Kd box = Box.init(make()) 1"],
            Problems(Box + "func f()\n    var box = Box.init(make())\n    consume(box.value@move)\n    box = Box.init(make())\n"));
        Assert.Equal(
            ["MovedPlace_Kd var leaf = Leaf.init(make()) 1"],
            Problems(Resource + "open struct Base\n    public var value: Resource\n    protected init(value: Resource)\n        self.value = value@move\n" +
                "    drop => ()\nstruct Leaf : Base\n    public var extra: i32 = 7\n    public init(value: Resource) : base(value@move)\n        ()\n" +
                "func f()\n    var leaf = Leaf.init(make())\n    consume(leaf.value@move)\n"));
    }

    // Plan 5.6.1: the Type rule is per view: a pair-generic value is destroyed in its owner case and owns nothing in a reference case, so
    // its Drop has a group per view class, a part moved out of it included (GenericCleanupCaseTest's select). A default copy keeps the Drops
    // its instance Type may need (an abstract U's, judged by their state; none of i32).
    [Fact]
    public void TheTypeRuleIsPerView()
    {
        Assert.Equal(["drop _1 -> bb1 [keep in v0; elide in v1]"], Lines("func hold<s/T>(value: s/T) -> ()\n    s is owner or ref\n    ()\n", "hold"));
        const string Select = "struct Payload\n    drop => ()\ngroup Helpers\n    public func select<s/T>(pair: (s/T, s/T)) -> s/T\n" +
            "        s is ref or obj\n        return pair.0@move\n";
        Assert.Equal(["drop _1 -> bb3 [elide in v0; open (.0 elide) in v1]", "drop _2 -> bb5 [elide]"], Lines(Select, "select"));
        const string Pick = "func pick<T>(a: T, b: T = a) -> T\n    T is Copy\n    return b\nfunc generic<U>(a: U) -> U\n    U is Copy\n    return pick(a)\n" +
            "func concrete() -> i32 => pick(1)\n";
        Assert.Equal(["drop _1 -> bb5 [keep]", "drop _4 -> bb7 [elide]", "drop _3 -> bb8 [elide]", "drop _2 -> bb9 [elide]"], Lines(Pick, "generic"));
        Assert.Empty(Lines(Pick, "concrete", all: true));
    }

    // SPEC 6.2.3, 13.7.1: a constructor's first placement of a Field destroys nothing (Elide), and a placement after a conditional one
    // destroys the old value under a flag.
    [Fact]
    public void AConstructorFieldIsDestroyedOnlyWherePlaced()
        => Assert.Equal(
            ["flags 1", "drop _0.0 -> bb6 [flag f0] [clear f0]", "drop _0.0 -> bb4 [elide] [clear f0]", "_0.0 = move _6 [set f0]", "_0.0 = move _8 [set f0]"],
            Lines("struct S\n    var name: string\n    public init(flag: bool)\n        if flag => self.name = \"a\"\n        self.name = \"b\"\n", "init")
                .Where(static x => x.Contains("_0.0", StringComparison.Ordinal) || x.StartsWith("flags", StringComparison.Ordinal)));

    // SPEC 14.10.3, plan 5.6.3: a Drop only the checks reach adds no runtime path and has no verdict, so a layer with its own drop left
    // incomplete in check-only code is no problem (plan Appendix B C3); an owning interpolation's buffer keeps its statement-end Drop.
    [Fact]
    public void OnlyRuntimeDropsHaveVerdicts()
    {
        Assert.Equal(
            ["drop _1 -> bb4 [keep]", "drop _2 -> bb9", "drop _2 -> bb12", "drop _1 -> bb13"],
            Lines(Resource + "func f() -> i32\n    let r = make()\n    return 1\n    let s = make()\n    return 2\n", "f", all: true));
        Assert.Empty(Problems(Box + "func f() -> i32\n    return 1\n    var box = Box.init(make())\n    consume(box.value@move)\n"));
        Assert.Equal(["drop _10 -> bb11 [keep]", "drop _2 -> bb12 [keep]"], Lines("func label(n: i32) -> string => \"n=\\(n)\"\n", "label"));
    }

    // V16 (plan "New V-checks"): A2 is linear in the body: N 16/32/64 locals each initialized on one branch need N flags, and the cold analysis
    // of the body (fresh components, as MirInitTest's) allocates linearly. MirBuildTest.WarmRebuildAllocatesNothing covers the warm
    // re-analysis.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void DropFlagsScaleLinearly()
    {
        var (sizes, flags, allocated) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("func text(n: i32) -> string => \"\\(n)\"\nfunc work(flag: bool)\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.Append("    var v").Append(n).Append(": string\n    if flag => v").Append(n).Append(" = text(").Append(n).Append(")\n");
            }

            var c = MirBuildTest.Compile(text.ToString());
            var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "work");
            var (flow, moves, cases, sink) = (new MirDataflow(new MirReach()), new MirMoveData(), new MirCases(c.Types), new MirProgram());
            var drops = new MirDrops(moves, cases);
            var init = new MirInit(flow, moves, cases, drops);
            var before = GC.GetAllocatedBytesForCurrentThread();
            flow.Begin(body);
            moves.Build(body, flow);
            drops.Begin(body, sink);
            cases.Select(body, 0);
            Assert.True(init.Run(body, 0, sink.Problems));
            drops.Judge(0);
            drops.End();
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, sink.Problems.All.Length);
            flags[i] = sink.Drops.FlagsOf(0);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: flags {string.Join(", ", flags)}, cold analysis allocation {string.Join(", ", allocated)}");
        Assert.Equal([16L, 32L, 64L], flags);
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold analysis allocation {string.Join(", ", allocated)}");
    }

    // The dump lines of a source's body named name that carry A2's output (its flags, each Drop with a verdict and each location with flag
    // points; with all, every Drop), without their statement marks and source suffixes; the source has no Binding issue.
    private static string[] Lines(string source, string name, bool all = false)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray();
        var ordinal = Array.FindIndex(bodies, x => x.Key.Decl?.Name == name && x.Key.Kind == MirBodyKind.Fn);
        var text = new StringBuilder();
        MirDump.Write(bodies[ordinal], text, program.Drops, ordinal);
        return [.. text.ToString().Split('\n').Select(static x => Suffix().Replace(x.TrimStart(' ', '*'), string.Empty))
            .Where(x => x.StartsWith("flags", StringComparison.Ordinal) || Annotation().IsMatch(x) || (all && x.StartsWith("drop", StringComparison.Ordinal)))];
    }

    private static string[] Problems(string source)
    {
        Assert.Empty(MirBuildTest.Compile(source).Binding.Issues);
        return MirInitTest.Problems(source);
    }

    [GeneratedRegex(@" @[0-9:]+( s[0-9]+)?$")]
    private static partial Regex Suffix();

    [GeneratedRegex(@" \[(set|clear|keep|elide|flag|open)")]
    private static partial Regex Annotation();
}
