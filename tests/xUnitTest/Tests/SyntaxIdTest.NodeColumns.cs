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

    // G949 D2b-d's d7: by reflection over NodeColumns' fields, so a family added without its Resize, Renumbered or Move line fails here.
    [Fact]
    public void EveryNodeFamilyIsResizedRenumberedAndMoved()
    {
        var fields = typeof(NodeColumns).GetFields(Members);
        Assert.NotEmpty(fields);
        object columns = default(NodeColumns);
        typeof(NodeColumns).GetMethod("Resize", Members)!.Invoke(columns, [40]);
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
        foreach (var field in fields)
        {
            var target = field.GetValue(moved[1])!;
            Assert.Same(Read<Array>(field.GetValue(columns)!, "arena"), Read<Array>(target, "spare"));
            Assert.Equal(rows[field], target.GetType().GetMethod("Get", Members)!.Invoke(target, [33]));
        }
    }

    // G949 D2b-d's d10: an edit's renumbering moves a kept family's rows into the column's spare arena, so the second of two edits
    // allocates no arena: the column's arena is again the one it had before the first.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AKeptFamilyAlternatesItsArenasAcrossEdits()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        var statement = Statement(c, "p");
        var symbol = c.Kotonoha.Syntax.Hir.Families.Symbols.Get(statement.SyntaxId);
        var before = Read<Array>(c.Kotonoha.Syntax.Hir.Families.Symbols, "arena");
        for (var i = 0; i < 2; i++)
        {
            c.Kotonoha.Syntax.Edit();
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        }

        Assert.Same(before, Read<Array>(c.Kotonoha.Syntax.Hir.Families.Symbols, "arena"));
        Assert.NotNull(symbol);
        Assert.Same(symbol, c.Kotonoha.Syntax.Hir.Families.Symbols.Get(statement.SyntaxId));
    }

    // G949 D2b-d's d10 for `associatedInference` (PLAN G949 S1-b2 commit 4): a conforming Type's inference batch is kept at its Type's
    // declaration (SPEC 8.6.1), so an edit's renumbering moves it with its node: after two edits it is the same batch at the declaration
    // and the column's arena is again its first. S's `X` is inferred from `x`'s result (SPEC 8.3), so S's conformance has a batch.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AnInferenceBatchMovesWithItsTypeAcrossEdits()
    {
        var c = MinimalEmissionTest.Analyze("contract K\n    associate X\n    func x(self: ref/Self) -> Self.X\nstruct S\n    Self is K\n    public func x(self: ref/Self) -> i32 => 0\n()");
        Assert.True(c.Binding.Result.IsComplete);
        var s = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single();
        var batch = c.Kotonoha.Syntax.Hir.Families.AssociatedInference.Get(s.SyntaxId);
        Assert.NotNull(batch);
        var before = Read<Array>(c.Kotonoha.Syntax.Hir.Families.AssociatedInference, "arena");
        for (var i = 0; i < 2; i++)
        {
            c.Kotonoha.Syntax.Edit();
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        }

        Assert.Same(before, Read<Array>(c.Kotonoha.Syntax.Hir.Families.AssociatedInference, "arena"));
        Assert.Same(batch, c.Kotonoha.Syntax.Hir.Families.AssociatedInference.Get(s.SyntaxId));
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
