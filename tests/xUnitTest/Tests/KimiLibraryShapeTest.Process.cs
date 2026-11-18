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
// that Unfrozen makes in its own freeze compilation; read by no compilation before U3, so these rows read the frozen library alone.
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
        Assert.True(library.Kotonoha.Compilation.Binding.Result.IsComplete);
    }

    // s21a and s21b: a direct freeze adds no entry to Of's store, and the freeze's Binding holds the library it made (the freeze never
    // calls Of; its recursion half is U3's row, O-U2-5).
    [Fact]
    public void ADirectFreezeIsNotCachedAndHoldsItsOwnLibrary()
    {
        var target = TargetTriple.Parse("x86_64-unknown-linux-gnu");
        var library = Unfrozen(target, debug: false);
        Assert.Same(library, library.Kotonoha.Compilation.Binding.Library);
        Assert.Same(library, KimiLibrary.Freeze(library));
        Assert.True(library.Kotonoha.Syntax.Frozen);
        var store = (ConcurrentDictionary<TargetTriple, Lazy<KimiLibrary>>)typeof(KimiLibrary).GetField("Process", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.False(store.ContainsKey(target));
    }

    // s2a and s2b (R8-M32's Debug row, N10): Of's library equals an uncached freeze, with the default preparation and with Debug on.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OfEqualsAnUncachedFreeze(bool debug)
    {
        var target = TargetTriple.Parse(WindowsProfile.Target);
        Assert.Equal(FrozenDump(KimiLibrary.Of(target)), FrozenDump(KimiLibrary.Freeze(Unfrozen(target, debug))));
    }

    // s6a and s6d's seam half (C3): a library with records frozen through the seam is refused as a value, never thrown: its refusal
    // holds each record (two independent ones here) at its library location, and the table is not frozen.
    [Fact]
    public void AFreezeOfADefectiveLibraryIsARefusal()
    {
        var library = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debug: false);
        library.Kotonoha.CreateCodeContext().Parse(library.Kotonoha.RootKoto, ZUnsupportedProbe + "public group ZUses\n    public func zuse(v: ref/::ZProbe.Inner) => ()\n    public func zuse2(v: ref/::ZProbe) => ()\n");
        Assert.Same(library, KimiLibrary.Freeze(library));
        var refusal = Assert.IsType<Kimi.Compiler.LibraryLayer.Refused>(library.Layer.Refusal);
        Assert.Equal(2, refusal.Records.Length);
        Assert.All(refusal.Records, static x => Assert.NotNull(x.Document));
        Assert.Equal(2, refusal.Records.Select(static x => x.Span).Distinct().Count());
        Assert.False(refusal.Syntax);
        Assert.False(library.Kotonoha.Syntax.Frozen);
    }

    // P-b (rulings-u1 5; the probe of 2026-11-18 15:08): no library call's storage names a Binding-made built-in target, which each
    // compilation's Binding makes for itself, so a compilation over the frozen library (U3) never meets another Binding's shim.
    [Fact]
    public void ALibraryCallNamesNoBindingMadeShim()
    {
        var library = KimiLibrary.Of(TargetTriple.Parse(WindowsProfile.Target));
        var binding = library.Kotonoha.Compilation.Binding;
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
            if (node is InvocationKoto call && call.CallOf() is { } plan)
            {
                calls++;
                Assert.DoesNotContain(plan.Target, shims);
            }
        }

        Assert.NotEqual(0, calls);
    }

    // An unfrozen library over the seam's preparation: an empty project, with Debug on where the row asks.
    private static KimiLibrary Unfrozen(TargetTriple target, bool debug)
    {
        var kimigayo = Compilation.CreateForTest().Kimigayo; // A console-less compiler service, as Of's default preparation has.
        var project = new Project(kimigayo);
        project.KimiOptions.Debug = debug;
        return KimiLibrary.Unfrozen(target, kimigayo, project);
    }

    // The frozen library's printed syntax and published HIR.
    private static string FrozenDump(KimiLibrary library)
    {
        var printer = new Printer(true);
        printer.Visit(library.Kotonoha.RootKoto);
        return printer + "\n" + printer.Hir;
    }
}
