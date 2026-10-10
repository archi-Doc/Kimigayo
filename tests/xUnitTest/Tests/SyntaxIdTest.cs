// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SyntaxIds: each module numbers the nodes reachable from its root densely in pre-order at the start of
/// a pass that follows a parse or an edit, and a node outside the tree takes the next id at its first semantic write.</summary>
public class SyntaxIdTest
{
    // A struct with a synthesized constructor, a try over an Option (selected while binding) and formatting, so Binding synthesizes nodes.
    private const string Source =
        "struct Point\n    public var x: i32 = 1\n    public let y: i32 = 2\n" +
        "func source() -> i32? => .Some(1)\nfunc run() -> bool?\n    let n = try source()\n    return .Some(n == 1)\n" +
        "let p = Point.init()\nConsole.writeLine(\"\\(p.x) \\(p.y)\")\n_ = run()\n";

    [Fact]
    public void ReachableNodesAreNumberedDenselyInPreOrderAndDeterministically()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var again = MinimalEmissionTest.Analyze(Source);
        foreach (var (module, other) in new[] { (c.Kotonoha, again.Kotonoha), (c.Library.Kotonoha, again.Library.Kotonoha) })
        {
            var table = module.Syntax;
            AssertConsistent(table);
            Assert.Same(module.RootKoto, table.Nodes[1]);
            Assert.True(table.Count > table.ParsedCount, "Binding synthesized nodes outside the tree.");

            // Binding's own normalization may attach or detach nodes after numbering; every numbered node it leaves in place keeps its
            // pre-order position, and every id names a distinct node.
            var previous = 0;
            foreach (var node in PreOrder(module.RootKoto))
            {
                if (ReferenceEquals(node.SyntaxOwner, table) && node.SyntaxId <= table.ParsedCount)
                {
                    Assert.True(node.SyntaxId > previous);
                    previous = node.SyntaxId;
                }
            }

            Assert.Equal(table.Count, table.Nodes[1..].ToArray().Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.Equal(Shape(table), Shape(other.Syntax));
        }
    }

    [Fact]
    public void AWarmRebindKeepsEveryIdAndBindingsOwnEditsAreNoEdits()
    {
        var c = CompilationTestHelper.Parse(Source, "Program.kimi");
        var version = c.Kotonoha.Syntax.Version;
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));

        // The try is selected for an Option while binding, which edits its arms; the none Pattern joined the tree after numbering.
        var table = c.Kotonoha.Syntax;
        Assert.Equal(version, table.Version);
        var attempt = PreOrder(c.Kotonoha.RootKoto).OfType<TryKoto>().Single();
        Assert.Same(table, attempt.Arms[1].Pattern.SyntaxOwner);
        Assert.True(attempt.Arms[1].Pattern.SyntaxId > table.ParsedCount);

        var ids = table.Nodes.ToArray();
        var library = c.Library.Kotonoha.Syntax.Nodes.ToArray();
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(version, table.Version);
        Assert.Equal(ids, table.Nodes.ToArray());
        Assert.Equal(library, c.Library.Kotonoha.Syntax.Nodes.ToArray());
        AssertConsistent(table);
    }

    [Fact]
    public void AnEditRenumbersItsModuleAndKeepsTheOrderOfNodesOutsideTheTree()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var table = c.Kotonoha.Syntax;
        var (version, libraryVersion) = (table.Version, c.Library.Kotonoha.Syntax.Version);
        var synthetic = table.Nodes[(table.ParsedCount + 1)..].ToArray();
        var statement = Statement(c, "p");
        var removed = PreOrder(statement);
        var replacement = Statement(CompilationTestHelper.Parse(Source, "Program.kimi"), "p");
        Assert.All(PreOrder(replacement), x => Assert.True(x.SyntaxOwner is null && x.SyntaxId == 0));
        Assert.True(KotoHelper.Replace(statement.Parent!, statement, replacement));
        Assert.NotEqual(version, table.Version);
        Assert.Equal(libraryVersion, c.Library.Kotonoha.Syntax.Version);

        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        AssertConsistent(table);
        Assert.Equal(Enumerable.Range(1, table.ParsedCount), PreOrder(c.Kotonoha.RootKoto).Select(x => x.SyntaxId));
        Assert.All(removed, x => Assert.True(x.SyntaxOwner is null && x.SyntaxId == 0));

        // A node outside the tree keeps its id after the reachable range, in its earlier order; a derived node the walk now reaches,
        // such as the synthesized constructor, is numbered with the tree.
        var kept = synthetic.Where(x => x!.SyntaxId > table.ParsedCount).ToArray();
        Assert.NotEmpty(kept);
        Assert.True(kept.Length < synthetic.Length);
        Assert.All(synthetic, x => Assert.Same(table, x!.SyntaxOwner));
        Assert.Equal(Enumerable.Range(table.ParsedCount + 1, kept.Length), kept.Select(x => x!.SyntaxId));
    }

    [Fact]
    public void RemovingAnAttributeOrClearingAContainerIsAnEdit()
    {
        var c = MinimalEmissionTest.Analyze("#Layout(\"C\")\nstruct S\n    var a: i32 = 1\n");
        var table = c.Kotonoha.Syntax;
        var version = table.Version;
        var structure = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        var attribute = Assert.IsType<AttributeKoto>(structure.AttributeChain);
        Assert.Same(table, attribute.SyntaxOwner);
        Assert.True(structure.RemoveAttribute(attribute));
        Assert.Equal(version + 1, table.Version);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(attribute.SyntaxOwner is null && attribute.SyntaxId == 0);
        Assert.Equal(Enumerable.Range(1, table.ParsedCount), PreOrder(c.Kotonoha.RootKoto).Select(x => x.SyntaxId));

        c.Kotonoha.RootKoto.Clear();
        Assert.Equal(version + 2, table.Version);
        c.Bind();
        Assert.True(structure.SyntaxOwner is null && structure.SyntaxId == 0);
        Assert.Equal(Enumerable.Range(1, table.ParsedCount), PreOrder(c.Kotonoha.RootKoto).Select(x => x.SyntaxId));
    }

    [Fact]
    public void ATransplantedSubtreeIsRehomedToTheModuleThatHoldsIt()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var donorCompilation = MinimalEmissionTest.Analyze(Source);
        var donorTable = donorCompilation.Kotonoha.Syntax;
        var donor = Statement(donorCompilation, "p");
        var donorIds = PreOrder(donor).Select(x => x.SyntaxId).ToArray();
        Assert.All(PreOrder(donor), x => Assert.Same(donorTable, x.SyntaxOwner));

        var statement = Statement(c, "p");
        Assert.True(KotoHelper.Replace(statement.Parent!, statement, donor));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));

        // The tree that holds the subtree numbers it as one pre-order range; the donor's table keeps an entry that no longer names
        // its owner.
        var table = c.Kotonoha.Syntax;
        AssertConsistent(table);
        var subtree = PreOrder(donor);
        Assert.All(subtree, x => Assert.Same(table, x.SyntaxOwner));
        Assert.Equal(Enumerable.Range(donor.SyntaxId, subtree.Count), subtree.Select(x => x.SyntaxId));
        Assert.True(donor.SyntaxId + subtree.Count - 1 <= table.ParsedCount);
        Assert.Same(donor, donorTable.Nodes[donorIds[0]]);
        AssertConsistent(donorTable);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var (count, libraryCount) = (c.Kotonoha.Syntax.Count, c.Library.Kotonoha.Syntax.Count);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Equal((count, libraryCount), (c.Kotonoha.Syntax.Count, c.Library.Kotonoha.Syntax.Count));
    }

    // Every id of the table names a node the table owns under that id; a node another table took over keeps a stale entry.
    private static void AssertConsistent(SyntaxTable table)
    {
        Assert.True(table.ParsedCount <= table.Count);
        Assert.Null(table.Nodes[0]);
        for (var id = 1; id <= table.Count; id++)
        {
            var node = table.Nodes[id];
            Assert.NotNull(node);
            Assert.True(!ReferenceEquals(node.SyntaxOwner, table) || node.SyntaxId == id, $"Id {id} names a node numbered {node.SyntaxId}.");
        }
    }

    private static string[] Shape(SyntaxTable table)
    {
        var shape = new string[table.Count + 1];
        for (var id = 1; id <= table.Count; id++)
        {
            var node = table.Nodes[id]!;
            shape[id] = $"{node.Akind} {node.Span.Start}+{node.Span.Length} {node.Parent?.SyntaxId}";
        }

        return shape;
    }

    private static FieldKoto Statement(Compilation c, string name)
        => c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FieldKoto>().Single(x => x.NameKoto.IdentifierName == name);

    private static List<Koto> PreOrder(Koto root)
    {
        var walk = new Walk();
        walk.Visit(root);
        return walk.Nodes;
    }

    private sealed class Walk : KotoVisitor
    {
        public List<Koto> Nodes { get; } = new();

        public override void Visit(Koto node)
        {
            this.Nodes.Add(node);
            base.Visit(node);
        }
    }
}
