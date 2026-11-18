// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Reflection;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Compiler.Target;
using Xunit;
using Printer = XunitTest.SemanticSnapshotTest.SemanticPrinter;

namespace XunitTest;

// PLAN G949 Stage 2 U2a (design u2-design 2.1-2.3, 2.6; rows s2a, s2b, s6a, s6d, s16, s19, s21a, s21b, P-b; labels in
// back\notes\l1-s2\u2a-impl\labels.md): the process's library per target, KimiLibrary.Of, frozen once by Freeze over an unfrozen library
// whose freeze compilation Unfrozen makes (B's ruling 37); from U3 (rows c14, s2c, s21c, s22a, s22c, h3, h6) every compilation not given a
// library reads it, fixed when its Binding is made.
public partial class KimiLibraryShapeTest
{
    // s19: parallel readers of one target get one object, frozen, not refused (s16's windows-x64 row too).
    [Fact]
    public void ParallelReadersOfOneTargetGetOneLibrary()
    {
        var target = TargetTriple.Parse(WindowsProfile.Target);
        var found = new KimiLibrary[8];
        Parallel.For(0, found.Length, i => found[i] = KimiLibrary.Of(target));
        Assert.All(found, x => Assert.Same(found[0], x));
        Assert.True(found[0].Kotonoha.Syntax.Frozen);
        Assert.Null(found[0].Layer.Refusal);
    }

    // s16 (§9 item 14): the shipped library freezes, unrefused, for every target width the suite uses and without a target.
    [Theory]
    [InlineData("i686-unknown-linux-gnu")]
    [InlineData("avr-unknown-unknown")]
    [InlineData("")]
    public void TheLibraryFreezesForEveryKey(string target)
    {
        var library = KimiLibrary.Of(target.Length == 0 ? TargetTriple.Invalid : TargetTriple.Parse(target));
        Assert.True(library.Kotonoha.Syntax.Frozen);
        Assert.Null(library.Layer.Refusal);
    }

    // s21a, s21b and s21c (U3's recursion half, O-U2-5): a direct freeze adds no entry to Of's store, and the freeze compilation holds the
    // library Unfrozen gave it, before its Freeze too (its Binding is made over it, never over Of's), so no freeze reads Of.
    [Fact]
    public void ADirectFreezeIsNotCachedAndHoldsItsOwnLibrary()
    {
        var target = TargetTriple.Parse("x86_64-unknown-linux-gnu");
        var freeze = Unfrozen(target, debugAndSetting: false);
        var made = freeze.Library;
        Assert.False(made.Kotonoha.Syntax.Frozen);
        var library = KimiLibrary.Freeze(freeze);
        Assert.Same(made, library);
        Assert.Same(freeze.Library, library);
        Assert.Same(library, freeze.Binding.Library);
        Assert.True(library.Kotonoha.Syntax.Frozen);
        var store = (ConcurrentDictionary<TargetTriple, Lazy<KimiLibrary>>)typeof(KimiLibrary).GetField("Process", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.False(store.ContainsKey(target));
    }

    // s2a and s2b (R8-M32's Debug row, N10): Of's library equals an uncached freeze, with the default preparation for windows-x64 and
    // without a target, and with Debug on and a compile-time setting.
    [Theory]
    [InlineData("x86_64-pc-windows-msvc", false)]
    [InlineData("", false)]
    [InlineData("x86_64-pc-windows-msvc", true)]
    public void OfEqualsAnUncachedFreeze(string target, bool debugAndSetting)
    {
        var key = target.Length == 0 ? TargetTriple.Invalid : TargetTriple.Parse(target);
        Assert.Equal(FrozenDump(KimiLibrary.Of(key)), FrozenDump(KimiLibrary.Freeze(Unfrozen(key, debugAndSetting))));
    }

    // s6a and s6d's seam half (C3): a library with records frozen through the seam is refused as a value, never thrown: its refusal
    // holds each record (two independent ones here) at its library location, and the table is not frozen.
    [Fact]
    public void AFreezeOfADefectiveLibraryIsARefusal()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        freeze.Library.Kotonoha.CreateCodeContext().Parse(freeze.Library.Kotonoha.RootKoto, ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n    public func zuse2(v: ref/::ZProbe) => ()\n");
        var library = KimiLibrary.Freeze(freeze);
        var refusal = Assert.IsType<Kimi.Compiler.LibraryLayer.Refused>(library.Layer.Refusal);
        Assert.Equal(2, refusal.Records.Length);
        Assert.All(refusal.Records, static x => Assert.NotNull(x.Document));
        Assert.Equal(2, refusal.Records.Select(static x => x.Span).Distinct().Count());
        Assert.Equal(refusal.Records[0], refusal.First); // No syntax record: the first counted record is the first Binding-side one.
        Assert.False(library.Kotonoha.Syntax.Frozen);
    }

    // P-b (rulings-u1 5; the probe of 2026-11-18 15:08): no library call's storage names a Binding-made built-in target, which each
    // compilation's Binding makes for itself, so a compilation over the frozen library (U3) never meets another Binding's shim.
    [Fact]
    public void ALibraryCallNamesNoBindingMadeShim()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        var library = KimiLibrary.Freeze(freeze);
        var binding = freeze.Binding;
        var shims = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var name in new[] { "builtinArithmetic", "builtinEquals", "builtinCompare" })
        {
            switch (typeof(Binding).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding))
            {
                case BindingSymbol?[] slots:
                    shims.UnionWith(slots.OfType<BindingSymbol>());
                    break;
                case BindingSymbol single:
                    shims.Add(single);
                    break;
            }
        }

        Assert.NotEmpty(shims);
        var calls = 0;
        foreach (var node in library.Kotonoha.Syntax.Nodes)
        {
            // The call's plan row: a frozen node names no compilation (the Koto currency getters route through it until U0b-P3).
            if (node is InvocationKoto && HirTables.PlanOf(node).Plan is CallPlan { Kind: not CalleeKind.Value } plan)
            {
                calls++;
                Assert.DoesNotContain(plan.Target, shims);
            }
        }

        Assert.NotEqual(0, calls);
    }

    // c14 (PLAN G949 Stage 2 U3, the switch): two fresh compilations of one target read one library, Of's, frozen before either Binds:
    // the same object and layer, so every library fact both read is one object. Before U3 each compilation made and bound its own.
    [Fact]
    public void TwoCompilationsOfOneTargetReadOneFrozenLibrary()
    {
        var (hello, milestone) = (Prepared("example/Hello"), Prepared("milestone/Milestone26"));
        Assert.Same(KimiLibrary.Of(hello.TargetTriple), hello.Library);
        Assert.Same(hello.Library, milestone.Library);
        Assert.Same(hello.Library.Layer, milestone.Library.Layer);
        Assert.True(hello.Library.Kotonoha.Syntax.Frozen);
        Assert.True(hello.Bind().IsComplete);
        Assert.True(milestone.Bind().IsComplete);
        Assert.Same(hello.Binding.Semantics.Conformances(hello.Library.Option), milestone.Binding.Semantics.Conformances(milestone.Library.Option));
    }

    // s22a (X5, X7): a compilation without a target reads its own key's library (TargetTriple.Invalid), so a source binds alike in every
    // target mode, and Of's store then holds the four keys.
    [Fact]
    public void EveryTargetModeReadsItsKeysLibrary()
    {
        Assert.Empty(CompilationTestHelper.BindInEveryTargetMode("let x: isize = 1\n"));
        var store = (ConcurrentDictionary<TargetTriple, Lazy<KimiLibrary>>)typeof(KimiLibrary).GetField("Process", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.All(new[] { WindowsProfile.Target, "i686-pc-windows-msvc", "avr-unknown-unknown" }, x => Assert.True(store.ContainsKey(TargetTriple.Parse(x)), x));
        Assert.True(store.ContainsKey(TargetTriple.Invalid));
    }

    // s22c (invalid use: an internal contract): a compilation given a library of another target refuses it where its Binding is made.
    [Fact]
    public void ALibraryOfAnotherTargetIsRefusedAtTheBinding()
    {
        var c = Prepared(string.Empty);
        c.UseLibrary(KimiLibrary.Freeze(Unfrozen(TargetTriple.Parse("i686-pc-windows-msvc"), debugAndSetting: false)));
        var refused = Assert.Throws<InvalidOperationException>(() => c.Binding);
        Assert.Contains("(InternalInvariant; PLAN G949 Stage 2 U3)", refused.Message, StringComparison.Ordinal);
    }

    // s2c (SPEC 22.1, the compiling half): compiling a program reads the frozen library and writes none of it: Of's frozen dump is the same
    // after each program's Check, on windows-x64 and on i686.
    [Theory]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("i686-pc-windows-msvc")]
    public void CompilingAProgramLeavesTheFrozenLibraryUnchanged(string target)
    {
        var library = KimiLibrary.Of(TargetTriple.Parse(target));
        var before = FrozenDump(library);
        foreach (var program in new[] { "docs/examples/Hello/Hello.kimi", "tests/milestones/Milestone26.kimi" })
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText(program.Split('/'))));
            Assert.True(c.Check(), program);
            Assert.Same(library, c.Library);
            Assert.Equal(before, FrozenDump(library));
        }
    }

    // h3 and h6 (the registration, form (i); the module set): a reading compilation's diagnostic owner holds the library module, with the
    // frozen library's syntax list (empty for the shipped library) registered read-only: no library document has a syntax error, nothing
    // is published for it, and invalidating it is the internal-invariant form; the compilation holds the library module, not another
    // compilation's source module.
    [Fact]
    public void AReadingCompilationRegistersTheLibrarysSyntaxListReadOnly()
    {
        var c = Prepared("example/Hello");
        Assert.True(c.Check());
        var module = c.Library.Kotonoha;
        Assert.False(c.Diagnostics.HasSyntaxRecords(module));
        var documents = new HashSet<SourceDocument>(ReferenceEqualityComparer.Instance);
        foreach (var node in module.Syntax.Nodes)
        {
            if (node?.CodeContext.SourceDocument is { } document)
            {
                documents.Add(document);
            }
        }

        Assert.NotEmpty(documents);
        Assert.All(documents, x => Assert.False(c.Diagnostics.HasSyntaxErrors(x)));
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Path is { } path && path.StartsWith("compiler://Kimi/", StringComparison.Ordinal));
        var refused = Assert.Throws<InvalidOperationException>(() => c.Diagnostics.InvalidateSyntax(module));
        Assert.Contains("(InternalInvariant; PLAN G949 Stage 2 U3)", refused.Message, StringComparison.Ordinal);
        Assert.True(c.HoldsModule(module));
        Assert.False(c.HoldsModule(Prepared(string.Empty).Kotonoha));
    }

    // The frozen library's syntax and every published HIR column, read from its table alone (B's ruling 45, C1: a frozen library names no
    // compilation, U2b, and the semantic printer reads each node through its compilation until U0b-P3): each node's parent, location, span
    // and kind; its State, Failure, Meaning, Symbol, Lookup and erased Type; its Op, Res, Completion, Adjust and Place; and every field of
    // its plan row, a plan object's fields by value (Types and Origins as the printer prints them, a node by its id).
    internal static string FrozenDump(KimiLibrary library)
    {
        var printer = new Printer(false);
        var text = new System.Text.StringBuilder();
        foreach (var node in library.Kotonoha.Syntax.Nodes)
        {
            if (node is null)
            {
                continue;
            }

            ref readonly var row = ref HirTables.PlanOf(node);
            text.Append(node.SyntaxId).Append(" ^").Append(node.Parent?.SyntaxId).Append(' ').Append(CompilationTestHelper.Location(node)).Append('+')
                .Append(node.Span.Length).Append(' ').Append(node.Akind).Append(' ').Append(HirTables.State(node)).Append('/').Append(HirTables.Failure(node))
                .Append(" m=").Append(Value(HirTables.Meaning(node), 2)).Append(" s=").Append(Value(HirTables.Symbol(node), 2)).Append(" l=")
                .Append(HirTables.LookupOf(node)).Append(" e=").Append(Value(HirTables.ErasedType(node), 2)).Append(" op=").Append(HirTables.Op(node))
                .Append(' ').Append(HirTables.ResOf(node)).Append(' ').Append(HirTables.CompletionOf(node)).Append(" a=").Append(Value(HirTables.AdjustOf(node), 2))
                .Append(" p=").Append(Value(HirTables.PlaceOf(node), 2)).Append(" plan=").Append(Value(row.Plan, 2)).Append(" second=").Append(Value(row.Second, 2))
                .Append(" format=").Append(Value(row.Formatting, 2)).Append(" mode=").Append(row.Mode).Append(" flag=").Append(row.Flag)
                .Append(" adjust=").Append(Value(row.Adjust, 2)).Append(" discarded=").Append(Value(row.DiscardedType, 2)).Append(" folded=")
                .Append(Value(row.Folded, 2)).Append('\n');
        }

        return text.ToString();

        // A value by its contents, to a bounded depth: a published object (a plan, a row field) has no printer of its own.
        string Value(object? value, int depth) => value switch
        {
            null => "-",
            Koto koto => "#" + koto.SyntaxId,
            BoundType type => printer.Type(type),
            BoundOrigin origin => Printer.Origin(origin),
            BindingSymbol symbol => $"{symbol.Kind}:{symbol.Name}@{CompilationTestHelper.Location(symbol.Declaration)}",
            string or bool => value.ToString()!,
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            System.Collections.IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(x => Value(x, depth))) + "]",
            _ when depth == 0 => value.GetType().Name,
            _ => value.GetType().Name + "{" + string.Join(" ", value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(x => x.Name + "=" + Value(x.GetValue(value), depth - 1))) + "}",
        };
    }

    // A freeze compilation over the seam's preparation: an empty project, with Debug on and a compile-time setting where the row asks.
    private static Compilation Unfrozen(TargetTriple target, bool debugAndSetting)
    {
        var kimigayo = Compilation.CreateForTest().Kimigayo; // A console-less compiler service, as Of's default preparation has.
        var project = new Project(kimigayo);
        if (debugAndSetting)
        {
            project.KimiOptions.Debug = true;
            project.ProjectFile.CompileTimeSettings["feature"] = new() { Bool = true };
        }

        return KimiLibrary.Unfrozen(target, kimigayo, project);
    }
}
