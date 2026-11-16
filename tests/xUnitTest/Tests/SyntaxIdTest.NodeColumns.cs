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
            var directory = Read<int[]>(column, "directory");
            Assert.True(directory.Length * PagedColumn<int>.PageSize >= 40, $"{field.Name} is not resized.");
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
        var symbol = c.Kotonoha.Syntax.Hir.Families.Symbols.Get(statement.SyntaxId).Symbol;
        var before = Read<Array>(c.Kotonoha.Syntax.Hir.Families.Symbols, "arena");
        for (var i = 0; i < 2; i++)
        {
            c.Kotonoha.Syntax.Edit();
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        }

        Assert.Same(before, Read<Array>(c.Kotonoha.Syntax.Hir.Families.Symbols, "arena"));
        Assert.NotNull(symbol);
        Assert.Same(symbol, c.Kotonoha.Syntax.Hir.Families.Symbols.Get(statement.SyntaxId).Symbol);
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
