// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
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

    // s15b: the eager list's objects are read after the freeze, never written: a library constraint's negation (of the layer's level, PLAN
    // G949 Stage 2 U4) is the one the library pass made, read when that pass ends (the freeze compilation's first Bind) and again after
    // Freeze (its later pass and the detachment), and the module's name memo holds a name for every document a library node names.
    [Fact]
    public void TheFrozenLibrarysEagerFactsAreOnlyRead()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        freeze.Bind();
        var constraints = freeze.Library.Layer.Constraints.Values.Where(static x => x.Kind != ConstraintKind.Not).ToArray();
        Assert.NotEmpty(constraints);
        var negations = constraints.Select(static x => x.Negation).ToArray();
        Assert.All(negations, Assert.NotNull);
        var library = KimiLibrary.Freeze(freeze);
        Assert.Same(freeze.Library, library);
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

    // PLAN G949 Stage 2 U3's step 0 (the Front's U2b C3; B's U2 ruling 49): a detached syntax record is about one document, its own, so the
    // fact's one renumbering (WithSource) moves every source index it holds to that document's index in a reading owner, once. The seam's
    // syntax-only library defect after a program document (the Front's P5: every index the freeze owner gave is the library document's)
    // and a fact holding each kind of index, prerequisites included; -1 and the unresolved prerequisite stay. A subject primary key keeps
    // its index (B's U3 ruling 21: it names the subject's document, as a node's report located at another node keys it).
    [Fact]
    public void ADetachedSyntaxRecordMovesWholeToAnotherSourceTable()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        freeze.Kotonoha.CreateCodeContext().Parse(freeze.Kotonoha.RootKoto, "func f(v: ref/i32) => ()\n");
        freeze.Library.Kotonoha.CreateCodeContext().Parse(freeze.Library.Kotonoha.RootKoto, "public group ZSyntax\n    public func f(x: i32 -> i32 => x\n");
        var library = KimiLibrary.Freeze(freeze);
        Assert.NotNull(library.Layer.Refusal);
        var facts = library.SyntaxRecords.Facts;
        Assert.Equal(3, facts.Length);
        Assert.Contains(facts, static x => x.Related is { Length: > 0 } && x.Repairs is { Length: > 0 });
        for (var i = 0; i < facts.Length; i++)
        {
            Assert.NotNull(library.SyntaxRecords.Documents[i]);
            Assert.All(Indices(facts[i]), x => Assert.Equal(facts[i].Source, x));
            var moved = facts[i].WithSource(facts[i].Source + 7);
            Assert.Equal(Indices(facts[i]).Length, Indices(moved).Length);
            Assert.All(Indices(moved), x => Assert.Equal(facts[i].Source + 7, x));
        }

        var requirement = new Kimi.Diagnostics.DiagnosticRequirement(Kimi.Diagnostics.DiagnosticPartition.Syntax, 0);
        var fact = new Kimi.Diagnostics.DiagnosticFact(Kimi.DiagnosticCode.PrerequisiteUnavailable_Kd, new(null, 3, 1, 2, requirement), 3, 1, 2, null, null, null, null) with
        {
            DerivedFrom = [new(null, 3, 4, 1, requirement), new(freeze.Library.Kotonoha.RootKoto, 3, 0, 9, requirement), Kimi.Diagnostics.DiagnosticKey.Unresolved],
            Related = [new("r", 3, 5, 1, null), new("s", -1, 0, -1, null)],
            Repairs = [new(Kimi.Diagnostics.RepairKind.InsertToken, null, [new(3, 1, 0, ")"), new(3, 6, 0, ";")], Kimi.Diagnostics.RepairConditionSet.None)],
        };
        Assert.True(fact.IsAboutOneDocument);
        var renumbered = fact.WithSource(0);
        Assert.Equal(Enumerable.Repeat(0, 7), Indices(renumbered));
        Assert.Equal(-1, renumbered.DerivedFrom![2].Source);
        Assert.True(renumbered.DerivedFrom[2].IsUnresolved);
        Assert.Equal(-1, renumbered.Related![1].Source);
        Assert.Equal(fact.DerivedFrom![1], renumbered.DerivedFrom[1]); // A subject key is its node: equal, its index moved.

        var located = new Kimi.Diagnostics.DiagnosticFact(Kimi.DiagnosticCode.MissingSyntax_Kd, new(freeze.Library.Kotonoha.RootKoto, 5, 0, 9, requirement), 3, 1, 2, ")", null, null, null);
        Assert.True(located.IsAboutOneDocument);
        var kept = located.WithSource(0);
        Assert.Equal(0, kept.Source);
        Assert.Equal(5, kept.Key.Source);
        Assert.Same(located.Key.Subject, kept.Key.Subject);
    }

    // Step 0's check (the Front's U2b C3 (i)): TakeSyntax refuses a record that holds another document's index in any of its fields, as the
    // internal-invariant form, and leaves the list where it was.
    [Theory]
    [InlineData("key")]
    [InlineData("prerequisite")]
    [InlineData("subject prerequisite")]
    [InlineData("related")]
    [InlineData("edit")]
    public void TakeSyntaxRefusesARecordAboutTwoDocuments(string field)
    {
        var owner = new Kimi.Diagnostics.DiagnosticOwner();
        var module = Compilation.CreateForTest().Kotonoha;
        var index = owner.ModuleIndex(module);
        var own = owner.DocumentSource(new SourceDocument("own.kimi", "func f() => ()\n"), index);
        var other = owner.DocumentSource(new SourceDocument("other.kimi", "func g() => ()\n"), index);
        var requirement = new Kimi.Diagnostics.DiagnosticRequirement(Kimi.Diagnostics.DiagnosticPartition.Syntax, 0);
        var fact = new Kimi.Diagnostics.DiagnosticFact(Kimi.DiagnosticCode.MissingSyntax_Kd, new(null, own, 1, 1, requirement), own, 1, 1, ")", null, null, null);
        fact = field switch
        {
            "key" => fact with { Key = new(null, other, 1, 1, requirement) },
            "prerequisite" => fact with { Code = Kimi.DiagnosticCode.PrerequisiteUnavailable_Kd, DerivedFrom = [new(null, other, 2, 1, requirement)] },
            "subject prerequisite" => fact with { Code = Kimi.DiagnosticCode.PrerequisiteUnavailable_Kd, DerivedFrom = [new(module.RootKoto, other, 2, 1, requirement)] },
            "related" => fact with { Related = [new("r", other, 2, 1, null)] },
            _ => fact with { Repairs = [new(Kimi.Diagnostics.RepairKind.InsertToken, null, [new(other, 1, 0, ")")], Kimi.Diagnostics.RepairConditionSet.None)] },
        };
        owner.Record(Kimi.Diagnostics.DiagnosticPartition.Syntax, index, fact, isError: true);
        Assert.False(fact.IsAboutOneDocument);
        var refusal = Assert.Throws<InvalidOperationException>(() => owner.TakeSyntax(module)).Message;
        Assert.Contains("names a document other than its own (InternalInvariant; PLAN G949 Stage 2 U3)", refusal, StringComparison.Ordinal);
        Assert.True(owner.HasSyntaxRecords(module));
    }

    // The boundary of U3's registration (B's U3 ruling 25; SPEC 19.4, docs/dev/DIAGNOSTICS.md's excludedBy): the detached syntax list
    // carries no excluded range, so a reading compilation would publish a library record in excluded syntax without its excludedBy
    // location. No library file holds a compile-time directive: a text check of every embedded library source (Kimi.csproj's Library *.kimi,
    // the files the library parses), with no freeze (B's U3 ruling 28; the library module's SourceDocuments lists none of them, since the
    // library parses its sources itself). When one gains one, U3's registration carries the freeze owner's excluded ranges with the list,
    // and this row goes.
    [Fact]
    public void TheShippedLibraryHoldsNoCompileTimeDirective()
    {
        var directive = new Regex(@"^[ \t]*#(if|switch|case)\b", RegexOptions.Multiline);
        Assert.Matches(directive, "func f() => ()\n    #if feature\n"); // The pattern finds a directive line.
        var assembly = typeof(KimiLibrary).Assembly;
        var names = assembly.GetManifestResourceNames().Where(static x => x.StartsWith("Kimi.Library.", StringComparison.Ordinal) && x.EndsWith(".kimi", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(names);
        var holding = names.Where(x => directive.IsMatch(Text(x))).ToArray();
        var removal = $"{string.Join(", ", holding)} hold a compile-time directive (#if, #switch or #case): a reading compilation (PLAN G949 Stage 2 U3) " +
            "would publish such a document's syntax records without their excludedBy location (SPEC 19.4), since the detached list carries no " +
            "excluded range. Carry the freeze owner's excluded ranges with the detached syntax list in U3's registration (B's U3 ruling 25), then " +
            "remove this row.";
        Assert.True(holding.Length == 0, removal);

        string Text(string name)
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            return reader.ReadToEnd();
        }
    }

    // n2 (PLAN G949 Stage 2 U3, design R2.2; the Front's U2b N2, form (b)): a library's detached data exist only once its freeze detached
    // it; a read before Freeze is the internal-invariant form, never an empty default (before U3 each read empty), and after Freeze they read
    // as AFrozenLibraryNamesNoCompilation reads them.
    [Fact]
    public void ALibrarysDetachedDataAreReadOnlyAfterItsFreeze()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        var library = freeze.Library;
        Refused(() => library.DocumentationFailures);
        Refused(() => library.HoverAnchors);
        Refused(() => library.SyntaxRecords);
        Assert.Same(library, KimiLibrary.Freeze(freeze));
        Assert.Empty(library.DocumentationFailures);
        Assert.NotEmpty(library.HoverAnchors);
        Assert.Empty(library.SyntaxRecords.Facts);

        static void Refused(Func<object> read)
            => Assert.Contains("is not detached", Assert.Throws<InvalidOperationException>(read).Message, StringComparison.Ordinal);
    }

    // The registration's order (PLAN G949 Stage 2 U3, the delta review's N8 and its re-check's row; B's U3 ruling 28): AddLibrary registers
    // the library's documents in the order of their indices in the freeze owner, not the list's order, so the source table keeps the order
    // of a compilation that parsed the library. A hand-made list holds document B's record before document A's (freeze indices A < B): A
    // is published first, each record located in its own document, and the registered list is read-only.
    [Fact]
    public void AddLibraryRegistersTheLibrarysDocumentsInTheirFreezeOrder()
    {
        var module = Compilation.CreateForTest().Kotonoha;
        var (a, b) = (new SourceDocument("a.kimi", "func f( => ()\n"), new SourceDocument("b.kimi", "func g( => ()\n"));
        var requirement = new Kimi.Diagnostics.DiagnosticRequirement(Kimi.Diagnostics.DiagnosticPartition.Syntax, 0);
        Kimi.Diagnostics.DiagnosticFact Fact(int source) => new(Kimi.DiagnosticCode.MissingSyntax_Kd, new(null, source, 7, 1, requirement), source, 7, 1, ")", null, null, null);
        var owner = new Kimi.Diagnostics.DiagnosticOwner();
        owner.DocumentSource(new SourceDocument("program.kimi", "func main() => ()\n"), owner.ModuleIndex(Compilation.CreateForTest().Kotonoha));
        owner.AddLibrary(module, new([Fact(5), Fact(3)], [b, a]));
        Assert.True(owner.HasSyntaxErrors(a));
        Assert.True(owner.HasSyntaxErrors(b));
        var result = owner.Finalize(Kimi.Diagnostics.DiagnosticPartition.Syntax, Kimi.Diagnostics.DiagnosticPartition.Syntax);
        Assert.Equal(["a.kimi", "b.kimi"], result.Sources.Select(static x => x.Path).Where(static x => x != "program.kimi"));
        Assert.Equal(["a.kimi", "b.kimi"], result.Diagnostics.Select(x => result.Sources[x.Source].Path));
        Assert.Contains("registered read-only", Assert.Throws<InvalidOperationException>(() => owner.InvalidateSyntax(module)).Message, StringComparison.Ordinal);
    }

    // PLAN G1072 (G949 Stage 2 U3c; B's U3 ruling 48; DECISIONS R8-M55: after the seal no compilation writes a frozen library object): the
    // library pass's end fills every lazily filled field of each library node and of each document a node names
    // (SyntaxTable.CompleteLazyFields), so a reader after the freeze writes none: every library node's ToString and hover header, a
    // position in its document, a number literal's value, and every property of every node. A library of its own (a direct freeze), so no
    // other class has read it first and the row does not rest on the suite's order. Before the fix: 357 integer literals' Literal (the
    // printers), 5 documents' line maps, 2 float literals' parsed values and 2,545 fields of the parser's mutable views (the getters).
    [Fact]
    public void AReaderAfterTheFreezeFillsNoLazyField()
    {
        var library = KimiLibrary.Freeze(Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false));
        var reader = Compilation.CreateForTest();
        Assert.True(reader.Prepare(WindowsProfile.Target));
        reader.UseLibrary(library);
        Assert.True(reader.Bind().IsComplete);
        var nodes = library.Kotonoha.Syntax.Nodes.ToArray().OfType<Koto>().ToArray();
        var lines = typeof(SourceDocument).GetField("lineStarts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var documents = nodes.Select(static x => x.CodeContext.SourceDocument).OfType<SourceDocument>().Distinct().ToArray();
        var maps = documents.Select(lines.GetValue).ToArray();
        var fields = FieldSnapshot(reader);
        foreach (var node in nodes)
        {
            _ = node.ToString();
            if (node is FunctionKoto or PropertyKoto or DeclarationContainerKoto)
            {
                _ = Kimi.Checking.HoverBuilder.Header(node);
            }

            _ = node.CodeContext.SourceDocument?.GetPosition(node.Span.Start);
            if (node is NumberLiteralKoto number)
            {
                _ = number.TryGetBasicValue(out _);
            }

            foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property is { GetMethod: not null, PropertyType.IsByRefLike: false } && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        _ = property.GetValue(node);
                    }
                    catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException)
                    {
                        // A detached library node's compilation or collection is the internal-invariant form (s7): read, nothing written.
                    }
                }
            }
        }

        Assert.Empty(FieldChanges(fields, FieldSnapshot(reader)));
        Assert.DoesNotContain(null, maps);
        Assert.Equal(maps, documents.Select(lines.GetValue));
    }

    // Every source index a fact holds, in the order WithSource names them (a subject primary key's excepted: its node is its identity).
    private static int[] Indices(in Kimi.Diagnostics.DiagnosticFact fact)
        => [.. new[] { fact.Source, fact.Key.Subject is null ? fact.Key.Source : -1 }
            .Concat((fact.DerivedFrom ?? []).Select(static x => x.Source))
            .Concat((fact.Related ?? []).Select(static x => x.Source))
            .Concat((fact.Repairs ?? []).SelectMany(static x => x.Edits).Select(static x => x.Source))
            .Where(static x => x >= 0)];

    // A frozen library whose freeze compilation only a weak reference names (the compilation made and dropped in a frame of its own).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (KimiLibrary Library, WeakReference Freeze) FrozenAndForgotten()
    {
        var freeze = Unfrozen(TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        return (KimiLibrary.Freeze(freeze), new WeakReference(freeze));
    }
}
