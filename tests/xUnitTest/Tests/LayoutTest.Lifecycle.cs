// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// Layout memo validity, reuse and lifetime across edits and compilation disposal.
public partial class LayoutTest
{
    [Fact]
    public void AnEditReachesTheLayoutOfAStructuralType()
    {
        // R5-M37: the memo serves one program version. An edit of S's Field keeps the interned Tuple (S, u8) but changes its layout.
        const string Source = "struct S\n    var a: u8\nfunc probe(t: (S, u8)) => ()\n";
        var c = CompilationTestHelper.BindSuccess(Source);
        var tuple = Parameters(c)[0];
        Assert.Equal(2, c.Layout.Of(tuple).Value!.Layout.Size);
        EditFieldType(c, Source, "var a: u8", "var a: i64");
        Assert.Same(tuple, Parameters(c)[0]);
        Assert.Equal(16, c.Layout.Of(tuple).Value!.Layout.Size);
    }

    [Fact]
    public void AWarmRebindKeepsTheVerdicts()
    {
        // R4-M51's premise, on which the memo rests: a pass without an edit gives every Type the verdict a fresh Layout gives.
        var c = CompilationTestHelper.BindSuccess(
            CLayoutGeneric + "struct S\n    var a: u8\n    var b: i64\nenum E\n    A(S)\n    B\nlet x: u8 = 4\nlet f = func [x] () -> u8 => x\n" +
            "func probe(s: S, e: E, p: P<u8>) => ()");
        BoundType[] types = [.. Parameters(c), Closure(c), P(c, BoundType.Unit)];
        foreach (var type in types)
        {
            c.Layout.Of(type);
        }

        Assert.True(c.Bind().IsComplete);
        var fresh = new Layout();
        foreach (var type in types)
        {
            Assert.Equal(Verdict(fresh.Of(type)), Verdict(c.Layout.Of(type)));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLayoutsAllocateNothing()
    {
        // R5-M37: after a warm rebind, rows, placements of every mode and every outcome, complete and exhausted memo hits allocate nothing
        // and give the same records. Rejected comes from a failed Binding, which is not rebound.
        var c = CompilationTestHelper.BindSuccess(
            CLayoutGeneric + "open struct B\n    var a: i64\n    var b: u8\nstruct D: B\n    var c: u16\n" +
            "func probe(d: D, p: P<u8>, o: Option<(i32, [3 of bool])>, l: [2147483648 of u8], m: [1073741824 of i32], v: (" + M + ", u8), " +
            "h: [4611686018427387904 of u8]) => ()");
        var failed = CompilationTestHelper.Parse("struct Node\n    let value: i32\n    let next: Node\nlet y = 1");
        Assert.False(failed.Bind().IsComplete);
        var node = Declared(failed, "Node");
        BoundType[] types =
        [
            BoundType.I32, BoundType.Never, BoundType.String, BoundType.WrappingOf(BoundType.ISize),
            new("p", BoundTypeKind.Semantics, semantics: SemanticsKind.Ref, components: [BoundType.I32]),
            new("h", BoundTypeKind.Semantics, semantics: SemanticsKind.Rc, components: [BoundType.I32]),
            new("fn", BoundTypeKind.Function, components: [BoundType.I32]),
            new("s", BoundTypeKind.Dictionary, components: [BoundType.String, BoundType.I32]),
            .. Parameters(c), P(c, BoundType.Unit), Wrap(c, BoundType.I32, 65),
        ];
        types = [.. types, c.Types.Tuple([Parameters(c)[6], Parameters(c)[6], types[^1]])];
        var outcomes = new HashSet<Layout.Outcome> { failed.Layout.Of(node).Outcome };
        foreach (var type in types)
        {
            outcomes.Add(c.Layout.Of(type).Outcome);
        }

        Assert.Equal(7, outcomes.Count);
        var record = c.Layout.Of(Parameters(c)[0]).Value;
        var size = 0L;
        var allocated = AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            size += (int)failed.Layout.Of(node).Outcome;
            foreach (var type in types)
            {
                size += c.Layout.Of(type).Value?.Layout.Size ?? 0;
            }
        });
        Assert.Equal(0, allocated);
        Assert.True(size > 0);
        Assert.Same(record, c.Layout.Of(Parameters(c)[0]).Value);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AnEditReleasesThePreviousRecords()
    {
        // R5-M37: an edit clears the records with the memo, so the records Layout retains are the current program's.
        const string Source = "struct S\n    var a: u8\n    var b: [77 of u8]\nfunc probe(s: S) => ()\n";
        var c = CompilationTestHelper.BindSuccess(Source);
        var record = PlaceRecord(c);
        EditFieldType(c, Source, "var a: u8", "var a: i64");
        Assert.Equal(88, c.Layout.Of(Parameters(c)[0]).Value!.Layout.Size);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(record.IsAlive);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ADroppedCompilationKeepsNoType()
    {
        // R5-M37: Layout keeps no static state, so a compilation it laid out is collected with its Types.
        var compilation = LayOutAndDrop();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(compilation.IsAlive);
    }

    // Replaces the Type syntax of the Field a by the donor's, an edit (as RetentionTest.AnEditRecomputesRetentions does), and rebinds.
    private static void EditFieldType(Compilation c, string source, string field, string edited)
    {
        var donor = CompilationTestHelper.BindSuccess(source.Replace(field, edited, StringComparison.Ordinal));
        var syntax = FieldTypeSyntax(c);
        Assert.True(KotoHelper.Replace(syntax.Parent!, syntax, FieldTypeSyntax(donor)));
        Assert.True(c.Bind().IsComplete);
    }

    private static Koto FieldTypeSyntax(Compilation c)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>().Single(static x => x.NameKoto.IdentifierName == "a").TypeKoto!;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PlaceRecord(Compilation c) => new(c.Layout.Of(Parameters(c)[0]).Value!);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LayOutAndDrop()
    {
        var c = CompilationTestHelper.BindSuccess("struct S\n    var a: u8\nenum E\n    A(S)\n    B\nfunc probe(s: S, e: E, t: (S, E)) => ()");
        foreach (var type in Parameters(c))
        {
            c.Layout.Of(type);
        }

        return new(c);
    }
}
