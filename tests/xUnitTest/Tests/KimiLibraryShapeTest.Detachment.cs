// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;
using Kimi.Compiler;
using Kimi.Compiler.Target;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U2b-2 (design u2-design 2.7, 5.2; rows s7, s9b, s20b, R2-N2b, s15b; P-d's list; labels
// back\notes\l1-s2\u2b-impl\labels.md): Freeze detaches the library it returns, so no frozen object names its freeze compilation, and the
// frozen library keeps its documentation failures, hover anchors and syntax list as data. The identity rows read Freeze's result through
// the freeze compilation the test holds: S1-1's rules hold there unchanged (no fail-before: S1-1's B1 and freeze made them).
public partial class KimiLibraryShapeTest
{
    // s7: after Freeze no frozen object names the freeze compilation (it is collected); a library node's compilation and collection, and
    // the module's, are the internal-invariant form; no documentation source has an owner; the hover anchors and syntax list are data.
    [Fact]
    public void AFrozenLibraryNamesNoCompilation()
    {
        var (library, freeze) = FrozenAndForgotten();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(freeze.IsAlive);
        var node = library.Kotonoha.RootKoto;
        Refused(() => node.CodeContext.Compilation);
        Refused(() => node.CodeContext.DiagnosticCollection);
        Refused(() => library.Kotonoha.DiagnosticCollection);
        var owner = typeof(DocumentationSource).GetField("optionalOwner", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotEmpty(library.Kotonoha.DocumentationSources);
        Assert.All(library.Kotonoha.DocumentationSources, x => Assert.Null(owner.GetValue(x)));
        Assert.NotEmpty(library.HoverAnchors);
        Assert.All(library.HoverAnchors, x => Assert.Same(library.Kotonoha, x.Syntax.Kotonoha));
        Assert.Empty(library.DocumentationFailures);
        Assert.Empty(library.SyntaxRecords.Facts);

        static void Refused(Func<object> read)
            => Assert.Contains("(InternalInvariant; PLAN G949 Stage 2 U2b)", Assert.Throws<InvalidOperationException>(read).Message, StringComparison.Ordinal);
    }

    // s9b, s20b and R2-N2b over Freeze's result: every stamp a frozen object holds is Frozen, every library scope's view is settled, and
    // every library symbol's query states are Frozen (S1-1's rules, now of the process's library).
    [Fact]
    public void TheFrozenLibraryKeepsTheFreezesRules()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        _ = KimiLibrary.Freeze(freeze);
        var counters = Counters(freeze).ToArray();
        Assert.True(counters.Length == 0, string.Join('\n', counters));
        Assert.Empty(UnsettledLibraryScopes(freeze));
        var library = DeclaredSymbols(freeze).Where(x => ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, freeze.Library.Kotonoha)).ToArray();
        Assert.NotEmpty(library);
        Assert.Empty(library.Where(static x => !x.QueriesFrozen).Select(static x => x.Name));
    }

    // s15b: the eager list's objects are read after the freeze, never written: a library constraint's negation is the one the library pass
    // made, and the module's name memo holds a name for every document a library node names.
    [Fact]
    public void TheFrozenLibrarysEagerFactsAreOnlyRead()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        var library = KimiLibrary.Freeze(freeze);
        var interned = (System.Collections.IDictionary)typeof(Binding).GetField("constraints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(freeze.Binding)!;
        var constraints = interned.Values.Cast<BoundConstraint>().Where(static x => x.Kind != ConstraintKind.Not).ToArray();
        var negations = constraints.Select(static x => x.Negation).ToArray();
        Assert.All(negations, Assert.NotNull);
        var names = (System.Collections.IDictionary)typeof(Kotonoha).GetField("names", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(library.Kotonoha)!;
        var count = names.Count;
        foreach (var node in library.Kotonoha.Syntax.Nodes)
        {
            if (node?.CodeContext.SourceDocument is { } document)
            {
                _ = library.Kotonoha.LogicalName(document);
            }
        }

        Assert.Equal(count, names.Count);
        Assert.Equal(negations, constraints.Select(static x => x.Negation));
    }

    // A frozen library whose freeze compilation only a weak reference names (the compilation made and dropped in a frame of its own).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (KimiLibrary Library, WeakReference Freeze) FrozenAndForgotten()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        return (KimiLibrary.Freeze(freeze), new WeakReference(freeze));
    }
}
