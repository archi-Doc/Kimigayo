// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>PagedColumn&lt;T&gt; (DECISIONS BU-220; G949 D2b-d §1.2, §4): the one store form of Binding's node families, a dense-id column in
/// pages of 16 slots carved from one arena at a page's first write.</summary>
public class PagedColumnTest
{
    // A page is carved at its first write, cleared, and only then: two ids of one page take one page, an id of another page a second.
    [Fact]
    public void APageIsCarvedAtItsFirstWrite()
    {
        var column = Column<int>(100);
        Assert.Equal(0, column.Get(37));
        Assert.Equal(0, Used(column));
        column.Ref(37) = 7;
        column.Ref(40) = 8;
        Assert.Equal(PagedColumn<int>.PageSize, Used(column));
        Assert.Equal((7, 8, 0, 0), (column.Get(37), column.Get(40), column.Get(36), column.Get(5)));
        column.Ref(5) = 9;
        Assert.Equal(2 * PagedColumn<int>.PageSize, Used(column));
        Assert.Equal(new[] { 9, 7, 8 }, Rows(column));
    }

    // The directory holds a page's offset plus 1: the first page carved sits at offset 0 and still reads as present, while an absent
    // page, whichever its directory slot, reads default.
    [Fact]
    public void TheFirstPageIsDistinctFromAnAbsentOne()
    {
        var column = Column<int>(100);
        column.Ref(40) = 4;
        Assert.Equal(1, Field<int[]>(column, "directory")[40 >> 4]);
        Assert.Equal((4, 0, 0), (column.Get(40), column.Get(0), column.Get(5)));
        column.Ref(5) = 5;
        Assert.Equal((4, 5), (column.Get(40), column.Get(5)));
        Assert.Equal(new[] { 5, 4 }, Rows(column));
    }

    // Empty makes every row absent, keeps the arena and clears its references, so no earlier row's object stays reachable; the next
    // write carves a cleared page from the same arena.
    [Fact]
    public void EmptyRetainsNothing()
    {
        var column = Column<object>(100);
        column.Ref(3) = new object();
        column.Ref(70) = new object();
        var arena = Field<object?[]>(column, "arena");
        column.Empty();
        Assert.Null(column.Get(3));
        Assert.Empty(Rows(column));
        Assert.Equal(0, Used(column));
        Assert.All(arena, static x => Assert.Null(x));
        column.Ref(70) = "kept";
        Assert.Same(arena, Field<object?[]>(column, "arena"));
        Assert.Null(column.Get(3));
        Assert.Equal("kept", column.Get(70));
    }

    // Enumerate gives the present rows by ascending id, whatever the order of the writes; Grow keeps every page.
    [Fact]
    public void RowsEnumerateInIdOrderAndSurviveGrow()
    {
        var column = Column<int>(40);
        foreach (var id in new[] { 33, 2, 17, 3 })
        {
            column.Ref(id) = id;
        }

        column.Grow(400);
        column.Ref(399) = 399;
        Assert.Equal(new[] { 2, 3, 17, 33, 399 }, Rows(column));
    }

    // A renumbering moves each present row into the spare arena, so two arenas alternate: the second renumbering reuses the first's
    // source arena, cleared, and allocates no arena.
    [Fact]
    public void ARenumberingMovesRowsIntoTheSpareArena()
    {
        var column = Column<string>(64);
        column.Ref(1) = "a";
        column.Ref(20) = "b";
        var first = Field<string?[]>(column, "arena");
        var renumbered = column.Renumbered(64);
        foreach (var (from, to) in new[] { (1, 21), (20, 2), (5, 6) })
        {
            column.MoveTo(ref renumbered, from, to);
        }

        Assert.Null(renumbered.Get(1));
        Assert.Equal(("b", "a"), (renumbered.Get(2), renumbered.Get(21)));
        Assert.Equal(new[] { "b", "a" }, Rows(renumbered));
        Assert.Same(first, Field<string?[]>(renumbered, "spare"));
        var second = renumbered.Renumbered(64);
        Assert.Same(first, Field<string?[]>(second, "arena"));
        Assert.All(first, static x => Assert.Null(x));
        renumbered.MoveTo(ref second, 21, 1);
        Assert.Equal("a", second.Get(1));
    }

    private static PagedColumn<T> Column<T>(int count)
    {
        var column = default(PagedColumn<T>);
        column.Grow(count);
        return column;
    }

    private static List<T> Rows<T>(PagedColumn<T> column)
    {
        var rows = new List<T>();
        foreach (var row in column.Enumerate())
        {
            rows.Add(row);
        }

        return rows;
    }

    private static int Used<T>(PagedColumn<T> column) => Field<int>(column, "used");

    private static TField Field<TField>(object column, string name)
        => (TField)column.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(column)!;
}
