// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The published HIR columns (docs/dev/COMPILER_ARCHITECTURE.md, R2b) over the Kimi library and every milestone program:
/// every node of the tree has an Op, every call Op its call, every selected symbol a Res, a parameter read from a later default the
/// prepared slot, and every jump its target; publishing changes no other result and allocates nothing again.</summary>
public sealed class HirColumnsTest
{
    // Virtual bodies over a generic base whose applicability has premises, and over a closed base.
    private const string VirtualSource =
        "contract Marker\nstruct NonCopy\n    public init() => ()\n    drop => ()\nopen struct Base<T>\n    T is Owned\n    public init() => ()\n" +
        "    public virtual func first(self: objref/Self) -> i32 => 1\n    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return value@follow\n" +
        "    public virtual func last(self: objref/Self) -> i32 => 3\nstruct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n" +
        "    override func copy(self: objref/Self, value: ref/T) -> T => value@follow\n    override func last(self: objref/Self) -> i32 => 30\n" +
        "open struct Plain\n    public init() => ()\n    public virtual func size(self: objref/Self) -> i32 => 1\nstruct Sub : Plain\n    public init() => ()\n" +
        "    override func size(self: objref/Self) -> i32 => 2\nlet a = Leaf<i32>.init()@obj\nlet b = Leaf<NonCopy>.init()@obj\nlet p = Sub.init()@obj\n" +
        "require a.copy(7) == 7 and a.last() == 30 and b.last() == 30 and b.first() == 1 and p.size() == 2 else => $abort(\"virtual\")\n";

    public static TheoryData<string> Corpus
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in SemanticSnapshotTest.CaseNames())
            {
                if (name == "library" || name.StartsWith("milestone/", StringComparison.Ordinal))
                {
                    data.Add(name);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryNodeIsPublished(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        HirTables.Publish(c);
        var problems = Problems(module);
        Assert.True(problems.Count == 0, $"{problems.Count} unpublished facts:\n{string.Join('\n', problems.Take(40))}");
    }

    // SPEC 7.2.3: a later default reads the slot its call prepared for an earlier parameter; a closure there reads its capture.
    [Fact]
    public void ParametersReadInLaterDefaultsAreThePreparedSlots()
    {
        var c = MinimalEmissionTest.Analyze("func run(k: i32, m: i32 = k + 1, action: (i32) -> i32 = func [k] (v) => v + k, other: (i32) -> i32 = func (v) => v + m) -> i32 => action(m) + other(k)\n" +
            "require run(1) == 6 else => $abort(\"run\")\n");
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var function = Reachable(c.Kotonoha).OfType<FunctionKoto>().Single(static x => x.Name == "run");
        var reads = Reachable(c.Kotonoha).Where(x => x is IdentifierNameKoto && HirTables.Op(x) == HirOp.Name && IsWithin(x, function)).Select(static x => HirTables.ResOf(x));

        // k in the default of m; v, the capture k; v, the capture m; then action, m, other and k in the body.
        Assert.Equal([HirRes.PreparedParam, HirRes.Param, HirRes.Capture, HirRes.Param, HirRes.Capture, HirRes.Param, HirRes.Param, HirRes.Param, HirRes.Param], reads);
    }

    // A virtual or override body Binding bound is built, the conditional one too; the corpus has none.
    [Fact]
    public void VirtualBodiesArePublished()
    {
        var c = MinimalEmissionTest.Analyze(VirtualSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        HirTables.Publish(c);
        Assert.Empty(Problems(c.Kotonoha));
        var functions = Reachable(c.Kotonoha).OfType<FunctionKoto>().Where(static x => x.IsVirtual || x.IsOverride).ToArray();
        Assert.Equal(7, functions.Length);
        Assert.All(functions, static x => Assert.Equal(HirOp.Body, HirTables.Op(x)));
    }

    // Publishing before the ownership analysis leaves every Binding, control-flow and ownership fact and diagnostic unchanged.
    [Theory]
    [MemberData(nameof(Corpus))]
    public void PublishIsInvisible(string name)
        => Assert.Equal(SemanticSnapshotTest.Take(name, false).Text, SemanticSnapshotTest.Take(name, true).Text);

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmPublishAllocatesNothing(bool virtualBodies)
    {
        var c = virtualBodies ? MinimalEmissionTest.Analyze(VirtualSource) : SemanticSnapshotTest.Bind("milestone/Milestone26").Compilation;
        HirTables.Publish(c);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            HirTables.Publish(c);
        }));
    }

    // Every parsed node in the tree has an Op, a call Op its call, a selected symbol its Res (the prepared slot exactly where the
    // parameter is read in a later default of its function), and a jump the target it transfers to.
    private static List<string> Problems(Kotonoha module)
    {
        var table = module.Syntax;
        var problems = new List<string>();
        foreach (var node in Reachable(module))
        {
            // A synthesized node in the tree belongs to the expansion that made it.
            if (!ReferenceEquals(node.SyntaxOwner, table) || node.SyntaxId > table.ParsedCount)
            {
                continue;
            }

            var op = HirTables.Op(node);
            var where = $"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} {node.StateOf()} {op}";
            if (op == HirOp.Unset || (op == HirOp.Call && CallPlan.Of(node) is null))
            {
                problems.Add(where);
            }

            if (node.SymbolOf() is { } symbol &&
                (HirTables.ResOf(node) == HirRes.None || (HirTables.ResOf(node) == HirRes.PreparedParam) != DefaultParameters.InLaterDefault(node, symbol)))
            {
                problems.Add($"{where} {symbol.Kind} {HirTables.ResOf(node)}");
            }

            if (op is HirOp.Jump or HirOp.Continue && (HirTables.PlanOf(node).Plan is not Koto target || !ReferenceEquals(target, KotoHelper.ResolveTransferTarget((JumpKoto)node))))
            {
                problems.Add($"{where} without its target");
            }
        }

        return problems;
    }

    private static bool IsWithin(Koto node, Koto ancestor)
    {
        for (Koto? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static List<Koto> Reachable(Kotonoha module)
    {
        var nodes = new List<Koto>();
        var collector = new Collector(nodes);
        collector.Visit(module.RootKoto);
        return nodes;
    }

    private sealed class Collector(List<Koto> nodes) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            nodes.Add(node);
            base.Visit(node);
        }
    }
}
