// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Binding's node families (NodeColumns, DECISIONS BU-220) follow their module's ids: each is resized with the table, handed
/// over to a renumbering and moves its rows with their nodes.</summary>
public partial class SyntaxIdTest
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // G949 D2b-d's d7: by reflection over NodeColumns' fields, so a family added without its Resize, Renumbered, Move or Retire line fails
    // here. A kept family's row moves into the renumbered column's spare arena, which Retire clears; an edit family (one the store's first
    // edit write empties) is handed over unmoved with the store's stamp, every other field of the store.
    [Fact]
    public void EveryNodeFamilyIsResizedRenumberedAndMoved()
    {
        var all = typeof(NodeColumns).GetFields(Members);
        var fields = all.Where(static x => x.FieldType.IsGenericType && x.FieldType.GetGenericTypeDefinition() == typeof(PagedColumn<>)).ToArray();
        Assert.NotEmpty(fields);
        object columns = default(NodeColumns);
        typeof(NodeColumns).GetMethod("Resize", Members)!.Invoke(columns, [40]);
        var stamps = all.Except(fields).ToArray();
        foreach (var stamp in stamps)
        {
            stamp.SetValue(columns, 7UL);
        }

        var rows = new Dictionary<FieldInfo, object>();
        foreach (var field in fields)
        {
            var column = field.GetValue(columns)!;
            var pages = Read<int>(column, "pages");
            Assert.True(pages * PagedColumn<int>.PageSize >= 40, $"{field.Name} is not resized.");
            var directory = new int[pages]; // A column's directory comes with its first write.
            column.GetType().GetField("directory", Members)!.SetValue(column, directory);
            var arena = Array.CreateInstance(field.FieldType.GetGenericArguments()[0], PagedColumn<int>.PageSize);
            arena.SetValue(rows[field] = NonDefault(arena.GetType().GetElementType()!), 1);
            column.GetType().GetField("arena", Members)!.SetValue(column, arena);
            column.GetType().GetField("used", Members)!.SetValue(column, PagedColumn<int>.PageSize);
            directory[0] = 1;
            field.SetValue(columns, column);
        }

        var moved = new object[] { 1, typeof(NodeColumns).GetMethod("Renumbered", Members)!.Invoke(columns, [40])!, 33 };
        typeof(NodeColumns).GetMethod("Move", Members)!.Invoke(columns, moved);
        Assert.All(stamps, x => Assert.Equal(7UL, x.GetValue(moved[1])));
        var targets = fields.ToDictionary(static x => x, x => (Column: x.GetValue(moved[1])!, Arena: Read<Array>(x.GetValue(columns)!, "arena")));
        var read = targets.ToDictionary(static x => x.Key, static x => (Moved: Get(x.Value.Column, 33), Kept: Get(x.Value.Column, 1)));
        typeof(NodeColumns).GetMethod("BeginEdit", Members)!.Invoke(columns, [8UL]);
        foreach (var field in fields)
        {
            var (target, arena) = targets[field];
            if (Read<int>(field.GetValue(columns)!, "used") == 0)
            {
                Assert.Same(arena, Read<Array>(target, "arena"));
                Assert.Equal(rows[field], read[field].Kept);
            }
            else
            {
                Assert.Same(arena, Read<Array>(target, "spare"));
                Assert.Equal(rows[field], read[field].Moved);
            }
        }

        typeof(NodeColumns).GetMethod("Retire", Members)!.Invoke(moved[1], null);
        Assert.All(fields.Where(x => !ReferenceEquals(targets[x].Arena, Read<Array>(targets[x].Column, "arena"))), x => Assert.All(Read<Array>(targets[x].Column, "spare").Cast<object?>(), static row => Assert.True(row is null || row.Equals(Activator.CreateInstance(row.GetType())))));

        static object? Get(object column, int id) => column.GetType().GetMethod("Get", Members)!.Invoke(column, [id]);
    }

    // G949 N5-5's n14: an edit family's store, the lowest layer. Its rows hold while its stamp is the edit counter; once the counter
    // advances (Invalidate) the store reads stale, a renumbering hands the stale column over unmoved, and its first write empties the
    // edit columns, keeping their capacity, and stamps the store; a later write of the same edit empties nothing.
    [Fact]
    public void AnEditFamilyIsStaleAfterAnEditUntilItsFirstWrite()
    {
        var columns = default(NodeColumns);
        columns.Resize(40);
        var (first, second) = (new BoundOrigin[1], new BoundOrigin[2]);
        columns.BeginEdit(3);
        columns.AssociatedOrigins.Ref(5) = first;
        Assert.True(columns.HoldsEdit(3));
        Assert.False(columns.HoldsEdit(4));

        var renumbered = columns.Renumbered(48);
        Assert.False(renumbered.HoldsEdit(4));
        Assert.Same(first, renumbered.AssociatedOrigins.Get(5));
        Assert.Same(Read<Array>(columns.AssociatedOrigins, "arena"), Read<Array>(renumbered.AssociatedOrigins, "arena"));

        renumbered.BeginEdit(4);
        Assert.True(renumbered.HoldsEdit(4));
        Assert.Equal(0, renumbered.AssociatedOrigins.Used);
        Assert.Null(renumbered.AssociatedOrigins.Get(5));
        renumbered.AssociatedOrigins.Ref(44) = second;
        renumbered.BeginEdit(4);
        Assert.Same(second, renumbered.AssociatedOrigins.Get(44));
        Assert.Equal(PagedColumn<int>.PageSize, renumbered.AssociatedOrigins.Used);
    }

    // G949 D2b-d's d10, per kept family: an edit's renumbering moves a kept family's rows into the column's spare arena, so the second
    // of two edits allocates no arena: the column's arena is again the one it had before the first, and its first row is the same object
    // at its node's id, which a synthesized node's renumbering may change (Symbols: a declaration's symbol; Receivers: Point's implicit
    // constructor's `self`).
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Symbols")]
    [InlineData("Receivers")]
    public void AKeptFamilyAlternatesItsArenasAcrossEdits(string family)
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var field = typeof(NodeColumns).GetField(family, Members)!;
        var rows = Column().Enumerate();
        Assert.True(rows.MoveNext(), $"{family} holds no row of the program.");
        var (node, row) = (c.Kotonoha.Syntax.Nodes[rows.Id]!, rows.Current);
        var before = Read<Array>(Column(), "arena");
        for (var i = 0; i < 2; i++)
        {
            c.Kotonoha.Syntax.Edit();
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        }

        Assert.Same(before, Read<Array>(Column(), "arena"));
        Assert.Same(row, Column().Get(node.SyntaxId));

        PagedColumn<BindingSymbol?> Column() => (PagedColumn<BindingSymbol?>)field.GetValue(c.Kotonoha.Syntax.Hir.Families)!;
    }

    // G949 D2b-d's d10 for an edit family (N5-5's associatedOrigins): an edit's renumbering hands its column over, stale, so the Bind
    // after each of two edits empties it at its first write and rewrites the row in the same arena.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AnEditFamilyKeepsItsArenaAcrossEdits()
    {
        var c = CompilationTestHelper.Parse("contract C\n    associate Item(a) for ref/i32 during a\n", "Hello.kimi");
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var before = Read<Array>(c.Kotonoha.Syntax.Hir.Families.AssociatedOrigins, "arena");
        Assert.NotNull(before);
        for (var i = 0; i < 2; i++)
        {
            c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Same(before, Read<Array>(c.Kotonoha.Syntax.Hir.Families.AssociatedOrigins, "arena"));
        Assert.Equal(PagedColumn<int>.PageSize, c.Kotonoha.Syntax.Hir.Families.AssociatedOrigins.Used);
    }

    // G949 N5-5's n15: an owner-keyed Origin interner's row (originAtoms, openOrigins) keeps its owner's atoms in key order, so a search
    // probes about log N of its N atoms. A function of N reference parameters (N Input atoms on its row) and a call of it with N
    // reference arguments (N open regions on the call's): a warm Bind's probes as N doubles from 128 to 512 grow within factor 3, where a
    // scan of the rows (N squared) takes 4 and N log N about 2.2.
    [Fact]
    public void AnOriginInternerRowIsSearchedInLogarithmicProbes()
    {
        long[] probes = [.. new[] { 128, 256, 512 }.Select(Probes)];
        Assert.True(MirBorrowsTest.Grows(probes, 3), $"probes {string.Join(", ", probes)}");

        static long Probes(int count)
        {
            var c = CompilationTestHelper.Parse(
                $"func f({string.Join(", ", Enumerable.Range(0, count).Select(static i => $"p{i}: ref/i32"))}) -> i32 => p0@follow\n" +
                $"func g() -> i32\n    let n: i32 = 1\n    return f({string.Join(", ", Enumerable.Range(0, count).Select(static _ => "n@ref"))})\n",
                "Hello.kimi");
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
            var before = c.Binding.OriginRowWork;
            Assert.True(c.Bind().IsComplete);
            return c.Binding.OriginRowWork - before;
        }
    }

    private static object NonDefault(Type type)
    {
        if (type.IsArray)
        {
            return Array.CreateInstance(type.GetElementType()!, 1);
        }

        if (!type.IsValueType)
        {
            return RuntimeHelpers.GetUninitializedObject(type);
        }

        var row = Activator.CreateInstance(type)!;
        var first = type.GetFields(Members)[0];
        first.SetValue(row, NonDefault(first.FieldType));
        return row;
    }

    private static T Read<T>(object column, string name) => (T)column.GetType().GetField(name, Members)!.GetValue(column)!;
}
