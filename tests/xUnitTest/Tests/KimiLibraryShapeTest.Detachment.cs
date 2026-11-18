// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;
using Kimi.Compiler;
using Kimi.Compiler.Target;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U2b-2 (design u2-design 2.7, 5.2; rows s7, s9b, s20b, R2-N2b, s15b, N1; P-d's list; labels
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

    // N1 (B's ruling 45; P-d's walk as a row): every object the frozen library's fields reach (an array element by element; a string, a
    // primitive, a Type or another reflection object ends a path) is none of a compilation's stores: no Compilation, Binding, TyCtxt,
    // DiagnosticOwner or DiagnosticCollection, so the compilations that read the library (U3) share no mutable store through it.
    [Fact]
    public void AFrozenLibraryReachesNoCompilationStore()
    {
        var library = KimiLibrary.Freeze(Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false));
        Type[] stores = [typeof(Compilation), typeof(Binding), typeof(TyCtxt), typeof(Kimi.Diagnostics.DiagnosticOwner), typeof(Kimi.Diagnostics.DiagnosticCollection)];
        var fields = new Dictionary<Type, FieldInfo[]>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        var reached = new List<string>();
        Push(library);
        while (pending.TryPop(out var item))
        {
            var type = item.GetType();
            if (Array.Exists(stores, x => x.IsAssignableFrom(type)))
            {
                reached.Add(type.Name);
            }
            else if (item is Array array)
            {
                foreach (var element in array)
                {
                    Push(element);
                }
            }
            else
            {
                foreach (var field in Fields(type))
                {
                    Push(field.GetValue(item));
                }
            }
        }

        Assert.Contains(library.Kotonoha.RootKoto, seen);
        Assert.Empty(reached);

        void Push(object? value)
        {
            if (value is not (null or string or MemberInfo or Pointer) && value.GetType() is { IsPrimitive: false, IsEnum: false } && seen.Add(value))
            {
                pending.Push(value);
            }
        }

        FieldInfo[] Fields(Type type)
        {
            if (!fields.TryGetValue(type, out var known))
            {
                var list = new List<FieldInfo>();
                for (var level = type; level is not null; level = level.BaseType)
                {
                    list.AddRange(level.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                        .Where(static x => x.FieldType is { IsPointer: false, IsFunctionPointer: false, IsPrimitive: false, IsEnum: false }));
                }

                known = [.. list];
                fields.Add(type, known);
            }

            return known;
        }
    }

    // A frozen library whose freeze compilation only a weak reference names (the compilation made and dropped in a frame of its own).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (KimiLibrary Library, WeakReference Freeze) FrozenAndForgotten()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        return (KimiLibrary.Freeze(freeze), new WeakReference(freeze));
    }
}
