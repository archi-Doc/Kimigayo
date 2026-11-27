// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using Printer = XunitTest.SemanticSnapshotTest.SemanticPrinter;

namespace XunitTest;

// PLAN G949 S1-0b (design R1.4, coverage row c16): after the pass split the library pass never sees the program, so a program dependence
// hidden in today's library computation would vanish silently. Each row dumps the library layer after a Final Bind and compares it with
// the empty program's, beyond R8-M32's ten programs and its "Not compared" list:
// - Binding's private tables, ImplTable's, TyCtxt's and KimiLibrary's through their fields (Invalidate's five stores among them): a
//   table's library rows, those whose key or element names library declarations only (one naming a program declaration is the
//   program's, R1.3; one naming none is a builtin row); a table keyed by a number (TyCtxt's interned Types) lists its elements as rows;
//   the per-query answer tables (TypeMemo) hold no fact after a pass and are not listed;
// - every library declaration's symbol and scope, field by field: the Contract, Property, Schema and Adt contents, the slots R8-M32 does
//   not print, and the declaration query states;
// - the library's nodes, parsed and synthesized, as R8-M32's printer prints them: each node's line by location and its HIR row by node
//   id (S1-0b r2 and r3, the dispositions review's C5; a parsed id is fixed by the library's table, a synthesized one is equal across
//   programs from G949 S1-e);
// - the aggregates' library parts: Issues, Obligations, LibraryImports (Binding's lists) and the effect obligations.
// Values print through declarations and nodes (by location), Types (by printed form and dense id: d6, review-n52 N2, from G949
// S1-r1b's split the library pass interns the library's Types first, so after a cold Final Bind each library row's Type has the empty
// program's id; one pass interleaved them, Option<isize> 508 in the empty program and 511 in Milestone10) and pass stamps (by their
// distance from the latest pass), so synthesized ids (R8-M32's Temporary 1) do not enter a table's row; rows compare as a sorted
// multiset. How a store compares
// is its disposition (S1-0b r2: the dispositions' §2 with the review's C1, C2 and C5), named in one table (LibraryLayer.Disposition);
// a store not in it is a fact:
// - fact: each program's rows equal the empty program's. Call scratch (suppliedExpansions, activeOriginRequirements and an Origin
//   requirement work's Dependents) is a fact once its call empties it at its end (G949 S1-e), so a leftover is a difference;
//   storedCases, storedFields and the Origin summaries' forming lists likewise (G949 S1-r1b, ACallsScratchIsEmptyAfterABind);
// - subset: a content interner or Type-keyed memo keeps every row of the empty program's unchanged, and a program's extra row is the
//   program's level (D2b-d §1.1, §1.5). The table classes each subset store (G949 ruling 167 Q4): a Type interner's extra row is
//   keyed by a Type the program made, its key naming a Type id above L (LibraryLayer.TypeCount; the subset Fact, over the stores keyed
//   by Type: a row keyed by the library's Types alone is a library fact a later pass made); a memo's extra rows, and those of an
//   interner keyed by no Type (constraints), are the program's whatever their key;
// - epoch: a memo of one capability epoch is the program's (D2b-d §1.1, census O16) and is not printed; its invariant is
//   TheCapabilityMemosEndWithTheirEpoch's;
// - number: a number naming a walk's or memo's order prints as #, the rest of its row compared; ArchitectureRulesTest's rows confine
//   its readers to the walk or memo that assigns it;
// - program: a per-pass scratch, pool or cache both passes use (r1b plan O9, F's rulings 152 and 178), each named with its reason,
//   holds after a Bind what the program pass, the last, left (G949 S1-r1b's split; Re-check 3's RC-e: the result walk's and the
//   publisher's Completion scratch follows the publication order, M4): the program's, no library-layer fact, not printed. Before the
//   split the one pass ran the library's work last, so they held the library's leftovers, equal for every program. A pool's
//   live-prefix rule stays a row: inlineSummaryPool's live prefix is inlineSummaries' values after every Bind (AssertLivePrefix).
// Any other difference is a program fact in the library layer, never filtered: it moves to the program overlay by design (F decides).
// The layer is clean since G949 S1-e (S1-r1 waited for it): before it the rows differed in the lazy library facts S1-e makes eager (the
// dispositions' rows 1, 1b and 2), in the call scratch it empties (rows 8 and 17, and a library Origin requirement work's Dependents,
// which held the program's consumers) and in the synthesized nodes' HIR rows, which Milestones 27 and 30 numbered by their uses (R8-M32's
// Temporary 1). The fast row runs in every Unit, about 0.4 s per program; the corpus row (593 programs, a few minutes) is Explicit,
// run by each G949 unit and window.
public partial class KimiLibraryShapeTest
{
    private static readonly Lazy<string[]> EmptyProgramLayer = new(() => LibraryLayer.Dump(Bound(string.Empty)));

    /// <summary>Gets the fast row's programs: R8-M32's ten and every milestone.</summary>
    public static TheoryData<string> LayerPrograms
        => [string.Empty, "example/Hello", "example/Ranges", "example/Utf8Formatting", .. Names(Directory.GetFiles(
            DiagnosticCorpus.RepositoryPath("tests", "milestones"), "Milestone*.kimi").Select(static x => "milestone/" + System.IO.Path.GetFileNameWithoutExtension(x)))];

    /// <summary>Gets the corpus row's programs: every other example and the diagnostic snapshot's mutations and syntax cases.</summary>
    public static TheoryData<string> LayerCorpus
        => [.. Names(Directory.GetDirectories(DiagnosticCorpus.RepositoryPath("docs", "examples")).Select(static x => "example/" + System.IO.Path.GetFileName(x))
            .Except(["example/Hello", "example/Ranges", "example/Utf8Formatting"])
            .Concat(DiagnosticCorpus.Mutations.Select(static x => "mutation/" + x.Name))
            .Concat(DiagnosticCorpus.SyntaxCases.Select(static x => "syntax/" + x.Name)))];

    // The dump itself: the empty program's layer twice, in two compilations, is the same (no hash order, identity or counter in a row);
    // and each disposition names a store or field that exists, so none widens a rule silently; every node family (NodeColumns) with
    // library rows has rows in it, so a family the walk misses fails here.
    [Fact]
    public void TheLibraryLayerDumpIsDeterministic()
    {
        Assert.Empty(LibraryLayer.Unresolved());
        SameLayerAsTheEmptyPrograms(string.Empty);
        var c = Bound(string.Empty);
        var dump = LibraryLayer.Dump(c);
        Assert.All(
            typeof(NodeColumns).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(x => LibraryLayer.RowObjects(x.GetValue(c.Library.Kotonoha.Syntax.Hir.Families)!, c.Binding).Any()),
            x => Assert.Contains(dump, row => row.StartsWith(x.Name + " ", StringComparison.Ordinal)));
    }

    // PLAN G949 S1-r1b (r1b plan section 6, the layer dump's two Facts): the dump walks its roots' fields by reflection, and from the
    // split the library's rows of every part collection are the layer's part's (LibraryLayer.Part, a root of the dump at its path from Binding),
    // while Binding's swapped fields and its own part hold the program pass's between Binds. So after a Final Bind of the empty program
    // the dump lists the layer's part's rows, and Binding's own part holds no library row: a dump that missed the layer's part would
    // compare two empty parts in the fast and corpus rows and pass them (it did until this row: the walk met the part past its nesting
    // limit).
    [Fact]
    public void TheLayerDumpListsTheLayersPartAndBindingsOwnPartHoldsNoLibraryRow()
    {
        var dump = LibraryLayer.Dump(Bound(string.Empty));
        Assert.Contains(dump, static x => x.StartsWith("Binding.Library.Layer.Part.Nodes ", StringComparison.Ordinal));
        Assert.Contains(dump, static x => x.StartsWith("Binding.Library.Layer.Part.Keepers ", StringComparison.Ordinal));
        Assert.DoesNotContain(dump, static x => x.StartsWith("Binding.programPart.", StringComparison.Ordinal) || x.StartsWith("Binding.nodes ", StringComparison.Ordinal));
    }

    // PLAN G949 S1-r1b (v2's r1b clause; s1r1-design 1.3, M6; F's ruling 178 (2)): a pass that asks for a rerun starts a new run of
    // every pass, the library pass's included, which runs as the run's second (StartRuns once per Bind). j4's cold Bind restarts (its
    // program pass reads a base before its clause is bound), and its library layer is the empty program's: the library pass's second
    // run binds the library as its first one does. The numbers the dump prints by value that are counters, not facts, are excluded by
    // name (RestartCounters, each with what it counts), since a second run of the library pass moves them: mutation counters (a List's,
    // an environment's, a scope's link), requirement calls and the run's reset stamps.
    [Fact]
    public void TheLibraryLayerAfterARestartIsTheEmptyPrograms()
    {
        var c = CompilationTestHelper.Parse(BaseInputFormationBindingTest.J4);
        Assert.True(c.Bind().IsComplete);
        Assert.True((bool)typeof(Binding).GetField("secondRun", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!);
        SameLayer("j4 after its restart", LibraryLayer.Dump(Bound(string.Empty), RestartCounters), LibraryLayer.Dump(c, RestartCounters), c.Library.Layer.TypeCount);
    }

    // G949 N5-5's n3 (C-380 F2): a library binder's atoms and a library source's open regions live in the library module's NodeColumns
    // (originAtoms, openOrigins): after a second Bind, and after an edit to the program's module and a Bind, every atom the library's
    // rows hold is the same object.
    [Theory]
    [MemberData(nameof(LayerPrograms))]
    public void TheLibraryAtomsAreKeptAcrossBindsAndEdits(string program)
    {
        var c = Bound(program);
        var atoms = LibraryAtoms(c);
        Assert.NotEmpty(atoms);
        c.Bind();
        Assert.Equal(atoms, LibraryAtoms(c), ReferenceEqualityComparer.Instance);
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        c.Bind();
        Assert.Equal(atoms, LibraryAtoms(c), ReferenceEqualityComparer.Instance);

        static object[] LibraryAtoms(Compilation c)
            => [.. LibraryLayer.Families().Where(static x => x.Name is "OriginAtoms" or "OpenOrigins").SelectMany(x => LibraryLayer.RowObjects(x.GetValue(c.Library.Kotonoha.Syntax.Hir.Families)!, c.Binding))];
    }

    // The epoch rule's invariant: ResetCapabilities, which begins every pass (its one call is ResetPass's, which the pass driver's RunPass
    // runs first in each pass: ArchitectureRulesTest's row), ends the capability epoch, so no entry of a per-epoch memo is current after
    // it. Each pass's start ends the epoch of the stores it reads: the shared ones and its own part's (the closure index, PassPart), so
    // the row starts each pass in turn, the library pass's and the program pass's (G949 S1-r1b). Every component summary is stale (its
    // Epoch is not summaryEpoch); every capability work holds CapabilityWork.Reset's state, its Fact included; every other epoch store is
    // empty, Enumerate's view index among them, whose entries hold its kept rows (ViewIndex.Rows, R7-C2e2 e2-3a). Reset leaves only a
    // work's Site (review N2): the limit site of the work's own Closure signature, its Use that signature's
    // binder and its Needed flag cleared by ResetCapabilities (ClearNeededSites); only the work's own evaluation reads it. Fact, the U fact
    // of its last Unknown, is payload no fixpoint compares (design-r2 Amendment 4), and a consumer that requests the work while it waits
    // in the queue reads it beside the reset Unknown (RequestCapability's read of the queued work), for record selection only; Reset
    // empties it since G949 S1-e, so that read never hands out a previous epoch's Fact (S1-0b r2's review, Finding 3).
    // PLAN G949 Stage 2 U3 (changed by decision): only the freeze compilation binds its library, so the row starts both passes on one,
    // with Hello as its program; a reading compilation runs no library pass, whose reset would find no work.
    [Fact]
    public void TheCapabilityMemosEndWithTheirEpoch()
    {
        var c = Unfrozen(Kimi.Compiler.Target.TargetTriple.Parse(TargetProfile.Windows.Target), debugAndSetting: false);
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs", "examples", "Hello", "Hello.kimi")));
        c.Bind();
        var binding = c.Binding;

        // Hello's works end with no U fact, so one work keeps one as an earlier epoch's Unknown leaves it: without Reset's clear the
        // Fact check below fails.
        var work = ((IDictionary)LibraryLayer.ValueAt(binding, "Binding.capabilityNodes")).Values.Cast<object>().First();
        var fact = RuntimeHelpers.GetUninitializedObject(typeof(Binding).GetNestedType("CallableUnknown", BindingFlags.NonPublic)!);
        work.GetType().GetProperty("Fact", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(work, fact);
        foreach (var pass in new[] { "LibraryPass", "ProgramPass" })
        {
            var part = typeof(Binding).GetProperty(pass, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding);
            typeof(Binding).GetMethod("EnterPass", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(binding, [part]);
            typeof(Binding).GetMethod("ResetCapabilities", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(binding, [binding.Result.Mode]);
        }

        var epoch = (int)LibraryLayer.ValueAt(binding, "Binding.summaryEpoch");
        string[] reset = ["Result", "Assumed", "Evaluated", "Queued", "Depends", "Cause", "OwnCause", "Fact", "Origin", "Parts", "Reads", "Dependents"];
        foreach (var store in LibraryLayer.StoresOf(LibraryLayer.Rule.Epoch))
        {
            var value = (IEnumerable)LibraryLayer.ValueAt(binding, store);
            switch (store)
            {
                case "Binding.summaries":
                    Assert.NotEmpty(value);
                    Assert.DoesNotContain(((IDictionary)value).Values.Cast<object>(), x => (int)Property(x, "Epoch")! == epoch);
                    break;
                case "Binding.capabilityNodes":
                    Assert.NotEmpty(value);
                    Assert.Empty(((IDictionary)value).Values.Cast<object>()
                        .SelectMany(x => reset.Where(name => Property(x, name) is not (null or false or ConstraintProof.Unknown or ICollection { Count: 0 }))));
                    break;
                default:
                    Assert.Empty(value);
                    break;
            }
        }

        static object? Property(object x, string name) => x.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(x);
    }

    [Theory]
    [MemberData(nameof(LayerPrograms))]
    public void TheLibraryLayerIsIndependentOfTheProgram(string program) => SameLayerAsTheEmptyPrograms(program);

    // ContractSolver's request tables (Rows.cs): the outermost request empties, capacity kept, the pooled link lists it lent (LeaveRows;
    // PLAN x41n, F's ruling 188 X1), so no row outlives its request and Enumerate's pool is empty after every Bind; the library-layer row
    // above then holds for the pool because it is empty, not because the requests that ran last left the same rows.
    [Theory]
    [MemberData(nameof(LayerPrograms))]
    public void ThePooledRowListsAreEmptyAfterABind(string program)
        => Assert.All(((IEnumerable)LibraryLayer.ValueAt(Bound(program).Binding, "Binding.enumeratePool")).Cast<ICollection>(), static rows => Assert.Empty(rows));

    [Theory(Explicit = true)] // Its length (PLAN G949 S1-0b): run by each G949 unit and window.
    [MemberData(nameof(LayerCorpus))]
    public void TheLibraryLayerIsIndependentOfTheCorpus(string program) => SameLayerAsTheEmptyPrograms(program);

    // PLAN G949 S1-r1b (S1-e's call-scratch rule; review-s1r1 Re-check 3, RC-e): the scratch of a call or a step is empty at its end,
    // so what the last container or summary left is no row of the layer, whichever pass ran last: the cases and Fields of the
    // container whose storage was prepared last, the nodes of the Origin summary formed last. Each held a leftover before (the
    // library's last, in one pass; the program's last after the split, a difference from the empty program's layer in 30, 182 and 6
    // of the corpus' 517 differing programs). A call's shape contracts keep no scratch: they read the call's own candidate span.
    [Theory]
    [InlineData("storedCases")]
    [InlineData("storedFields")]
    [InlineData("summaryNodes")]
    public void ACallsScratchIsEmptyAfterABind(string field)
        => Assert.Empty((System.Collections.ICollection)typeof(Binding).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Bound("example/Hello").Binding)!);

    private static IEnumerable<string> Names(IEnumerable<string> names) => names.Order(StringComparer.Ordinal);

    // The raw counters the restart row excludes by name (TheLibraryLayerAfterARestartIsTheEmptyPrograms).
    private static readonly HashSet<string> RestartCounters =
    [
        "List`1._version", // A List's mutation counter, in list-valued rows.
        "ConstraintEnvironment.Version", // An environment's change counter, which the contract solver's view serial reads.
        "BindingScope.LinkVersion", // A scope's link counter, read with it.
        "OriginRequirementWork.Pass", // The requirement call that last visited a work.
        "KeeperList.Pass", // The requirement call that kept a slot's list.
        "BoundConformance.Stamp", // A conformance identity's reset stamp, the run's counter.
        "BoundConformancePath.Stamp", // A conformance path's reset stamp, the run's counter.
        "InstanceStorage.Version", // The run whose substitution formed an instance (Binding.instances, a Type-keyed memo): from S1-1's freeze a restart reruns the program pass alone, so an instance the library pass formed keeps the first run's.
    ];

    private static void SameLayerAsTheEmptyPrograms(string program)
    {
        var c = Bound(program);
        LibraryLayer.AssertLivePrefix(c.Binding, "Binding.inlineSummaryPool", "Binding.inlineSummaries");
        LibraryLayer.AssertLivePrefix(c.Binding, "Binding.enumerateGroundsPool", "Binding.enumerateGrounds");
        SameLayer(program, EmptyProgramLayer.Value, LibraryLayer.Dump(c), c.Library.Layer.TypeCount);
    }

    // The comparison of a layer with the empty program's; `library` is the compilation's L (the subset Fact).
    private static void SameLayer(string program, string[] expected, string[] actual, int library)
    {
        var differences = new List<string>();
        var (e, a) = (0, 0);
        while (e < expected.Length || a < actual.Length)
        {
            var order = e == expected.Length ? 1 : a == actual.Length ? -1 : string.CompareOrdinal(expected[e], actual[a]);
            if (order == 0)
            {
                (e, a) = (e + 1, a + 1);
                continue;
            }

            if (order > 0 && LibraryLayer.RuleOf(actual[a]) is var rule && (rule == LibraryLayer.Rule.Memo || (rule == LibraryLayer.Rule.Interner && LibraryLayer.KeyIds(actual[a]).Any(x => x > library))))
            {
                a++; // The subset rule: the program's own row in a Type-keyed memo, or in a content interner under a Type the program made.
                continue;
            }

            differences.Add(order < 0 ? "- " + expected[e++] : "+ " + actual[a++]);
        }

        // The rows by store, then each store's first 12 rows, a long one from where it leaves the row before it (a "-" and its "+").
        var stores = differences.GroupBy(static x => x[..(x.IndexOf(' ', 2) is var i && i < 0 ? x.Length : i)]).Select(static x => $"{x.Key} {x.Count()}");
        var shown = differences.Select((x, i) => (Row: x, From: i == 0 ? 0 : Math.Max(0, CommonPrefix(x, differences[i - 1]) - 200)))
            .GroupBy(static x => x.Row[2..(x.Row.IndexOf(' ', 2) is var i && i < 0 ? x.Row.Length : i)]).SelectMany(static x => x.Take(12))
            .Select(static x => (x.From <= 2 ? x.Row[..2] : x.Row[..2] + "... ") + x.Row[Math.Max(2, x.From)..Math.Min(x.Row.Length, Math.Max(2, x.From) + 700)]);
        var message = $"{program}: {differences.Count} library-layer rows differ from the empty program's ({expected.Length} rows); by store: {string.Join(", ", stores)}";
        Assert.True(differences.Count == 0, message + "\n" + string.Join('\n', shown));
    }

    private static int CommonPrefix(string a, string b)
    {
        var n = 2;
        while (n < a.Length && n < b.Length && a[n] == b[n])
        {
            n++;
        }

        return n;
    }

    // A program read from the repository or the diagnostic corpus, after its Final Bind.
    private static Compilation Bound(string program)
    {
        var c = Prepared(program);
        c.Bind();
        return c;
    }

    // Bound's form over a library of the compilation's own (PLAN G949 Stage 2 U3, x1): for a row that writes a library object by reflection
    // or starts the library's pass itself, so no other compilation reads what it writes.
    private static Compilation BoundOverItsOwnLibrary(string program)
    {
        var c = Prepared(program);
        CompilationTestHelper.UseEditedLibrary(c, static _ => { });
        c.Bind();
        return c;
    }

    // A program read from the repository or the diagnostic corpus, prepared.
    private static Compilation Prepared(string program)
    {
        var c = Compilation.CreateForTest();
        var kind = program.Length == 0 ? string.Empty : program[..program.IndexOf('/', StringComparison.Ordinal)];
        var subject = program[(kind.Length == 0 ? 0 : kind.Length + 1)..];
        if (kind == "example" && Directory.GetFiles(DiagnosticCorpus.RepositoryPath("docs", "examples", subject), "*.kimi").Length == 0)
        {
            PrepareModules(c, DiagnosticCorpus.RepositoryPath("docs", "examples", subject));
        }
        else
        {
            var source = kind switch
            {
                "" => string.Empty,
                "milestone" => Repository.ReadText("tests", "milestones", subject + ".kimi"),
                "example" => File.ReadAllText(Directory.GetFiles(DiagnosticCorpus.RepositoryPath("docs", "examples", subject), "*.kimi").Single()),
                "syntax" => DiagnosticCorpus.Syntax(subject).Source,
                _ => DiagnosticCorpus.Apply(DiagnosticCorpus.Mutation(subject)),
            };
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        }

        return c;
    }

    // docs/examples/SourceDependencies: Application, its source dependency Geometry and Geometry's Math, linked as DependencyResolver links
    // them (ModuleBindingTest.Create's form).
    private static void PrepareModules(Compilation c, string folder)
    {
        c.Project.ProjectFile.Dependencies.Add("Geometry", new() { PackageId = "geometry", PackageVersion = "1", Project = "geometry.kimiproj" });
        var root = new DependencyNode("root", new("root.kimiproj", c.Project.ProjectFile, [], []), -1, string.Empty);
        var geometry = new DependencyNode("geometry@1", new("geometry.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 0, "Geometry");
        var math = new DependencyNode("math@1", new("math.kimiproj", new() { OutputKind = OutputKind.Library }, [], []), 1, "Math");
        root.Edges.Add("Geometry", geometry.Key);
        geometry.Edges.Add("Math", math.Key);
        Assert.True(c.Prepare(TargetProfile.Windows.Target, new([root, geometry, math])));
        string[] modules = ["Application", "Geometry", "Math"];
        for (var i = 0; i < modules.Length; i++)
        {
            var source = Directory.GetFiles(System.IO.Path.Combine(folder, modules[i]), "*.kimi").Single();
            c.SourceModules[i].AddSource(new SourceDocument(System.IO.Path.GetFileName(source), File.ReadAllText(source)));
        }
    }

    /// <summary>The library layer of a bound compilation as sorted rows (store, key, value).</summary>
    internal sealed class LibraryLayer
    {
        private const int Depth = 3; // Nesting printed inside a row.
        private const int Nesting = 3; // Objects whose fields are listed as stores below Binding (ImplTable, TyCtxt, their parts).

        private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldsOf = new();

        // The dispositions (PLAN G949 S1-0b r2; the dispositions' §2 table with the review's C1): a store by its path from Binding, a
        // number by its type and field. A store not named here is a fact.
        private static readonly Dictionary<string, Rule> Disposition = new(StringComparer.Ordinal)
        {
            // Content interners and Type-keyed memos (the subset rule; G949 ruling 167 Q4 classes each): the program's instantiations at
            // builtin Types are its own rows.
            ["Binding.types.types"] = Rule.Interner,
            ["Binding.boundContracts"] = Rule.Interner,
            ["Binding.programPart.BoundContracts"] = Rule.Interner, // the same entries, the program pass's part (PLAN G949 S1-1)
            ["Binding.constraints"] = Rule.Memo, // A content interner keyed by a constraint, no Type: a program's question over library Types (i32 is Position) is its own row.
            ["Binding.originSummaries"] = Rule.Memo,
            ["Binding.erasureOutcomes"] = Rule.Memo,
            ["Binding.pendingAssociatedTypes"] = Rule.Memo,
            ["Binding.storageMarks"] = Rule.Memo,

            // Memos the reads after a Bind (MIR's, at Analyze) fill too (PLAN G949 S1-1, ruling 199 w1 (ii)): a row is a function of its key,
            // whose Types are interned and whose declarations are frozen, so a later step adds rows and never changes or drops one (an edit's
            // ResetViews drops only TyCtxt's view memos, which a later read forms again, equal).
            ["Binding.instances"] = Rule.Memo, // An ADT's storage at a Type (n9): Fields and parts a function of the Type; Version the run's currency.
            ["Binding.types.instances"] = Rule.Memo, // Whether a definition instantiates: a function of its scope chain.
            ["Binding.types.copies"] = Rule.Memo, // A definition's Copy facts, each a function of the definition and the Type proven.
            ["Binding.types.topLevelCopies"] = Rule.Memo, // The top-level statements' Copy facts (no definition), each a function of the Type proven.
            ["Binding.types.retentions"] = Rule.Memo, // A callee's retention by callee, Subst, premise scope, region version and Closure receiver.
            ["Binding.types.handedCallables"] = Rule.Memo, // HandedCallables' answer per interned Type.
            ["Binding.types.comparisons"] = Rule.Memo, // A comparison's composition by Contract, Type and operator form.
            ["Binding.types.witnesses"] = Rule.Memo, // A requirement's target by requirement, Contract and Subst.
            ["Binding.types.erasedTypes"] = Rule.Memo, // A Type's erased form, structural.
            ["Binding.types.erasedSubsts"] = Rule.Memo, // A substitution's erased form, structural.
            ["Binding.types.substs"] = Rule.Memo, // The substitution interner: a row is its own key, keyed by no Type (constraints' class).
            ["Binding.programAtoms"] = Rule.Memo, // The program's atoms at library binders (ruling 203 (4)): a function of the binder, kind and slot, kept as the item contexts holding them are, until an edit (ruling 205 N4).

            // Memos of one capability epoch (ClearCapabilityResults): component summaries, capability works, Enumerate's view index, whose
            // entries hold its kept rows (ViewIndex.Rows, R7-C2e2 e2-3a), and a Contract's head depths.
            ["Binding.summaries"] = Rule.Epoch,
            ["Binding.capabilityNodes"] = Rule.Epoch,
            ["Binding.capabilityEdges"] = Rule.Epoch,
            ["Binding.capabilityQueue"] = Rule.Epoch,
            ["Binding.enumerateIndex"] = Rule.Epoch,
            ["Binding.programPart.EnumerateIndex"] = Rule.Epoch, // the same index, each pass's part's (PLAN G949 S1-r1b)
            ["Binding.Library.Layer.Part.EnumerateIndex"] = Rule.Epoch,
            ["Binding.enumerateGrounds"] = Rule.Epoch, // e2-7 (i): a view's T(G) entry, its closures' first round (GroundsEntry)
            ["Binding.programPart.GroundsIndex"] = Rule.Epoch,
            ["Binding.Library.Layer.Part.GroundsIndex"] = Rule.Epoch,
            ["Binding.enumerateHeads"] = Rule.Epoch,
            ["Binding.enumerateSupplies"] = Rule.Epoch, // The walk's Complete supply instances (SupplyOf, G1406-30), the head depths' lifetime.
            ["Binding.enumerateSupplied"] = Rule.Epoch, // their items
            ["Binding.readings"] = Rule.Epoch, // A view's readings (ResetCapabilities; G949 S1-1, w1 (ii)).
            ["Binding.identityClasses"] = Rule.Epoch, // A view's identity tables (ResetIdentityClasses, from ResetCapabilities).
            ["Binding.identityLent"] = Rule.Epoch, // The tables lent to them, returned to identityPool at the epoch's end.

            // e2-7's closure stores, which ResetIdentityClasses empties with them: program-pass memos over library keys (BU-220).
            ["Binding.identityGrounds"] = Rule.Epoch, // A view's T(G), a closure's first round (ExtendedClassesOf).
            ["Binding.identityRoots"] = Rule.Epoch, // The closed parts by view scope and part as asked (FileClosure).
            ["Binding.identityClosures"] = Rule.Epoch, // their closures
            ["Binding.identityClosed"] = Rule.Epoch, // and the closures' rows
            ["Binding.identityReadingHeads"] = Rule.Epoch, // A view scope's last reading (ReadingOf's chain).
            ["Binding.identityReadings"] = Rule.Epoch, // the readings' kept tables
            ["Binding.identityReadingClosures"] = Rule.Epoch, // and their closure sets

            // Numbers: Tarjan's visit order. A scope's view serial and the chain state it names, and an Origin requirement work's
            // Consumer mark, compare by value (PLAN G949 S1-1, ruling 205 C2): the library pass settles every library scope's serial
            // (SettleViewSerials) and a frozen work is never active, so a later step's write to a library one is a difference.
            ["InlineSummary.Index"] = Rule.Number,
            ["InlineSummary.Low"] = Rule.Number,

            // A per-pass scratch, pool or cache both passes use (G949 S1-r1b; O9, rulings 152 and 178): the program pass's after a Bind.
            ["Binding.enumeratePool"] = Rule.Program, // Enumerate's lists of one outermost request, lent by position, all free at its end.
            ["Binding.resultPool"] = Rule.Program, // The result contexts a pass rents by its cursor, which each pass's reset sets back to 0.
            ["Binding.identityPool"] = Rule.Program, // The free identity tables each capability epoch's end returns (ResetIdentityClasses).
            ["Binding.inlineSummaries"] = Rule.Program, // ValidateInlineLayouts' summaries, emptied at its start in each pass.
            ["Binding.inlineSummaryPool"] = Rule.Program, // Their pool: its live prefix is inlineSummaries' values (AssertLivePrefix).
            ["Binding.rawFits"] = Rule.Program, // The running pass's raw fits (G147 ruling 75), emptied at each pass's start; the publication reads them.
            ["Binding.resultStructure.outward"] = Rule.Program, // That walk's Outward target chains, emptied by its Clear for each body.
            ["Binding.hirPublisher.completion.outward"] = Rule.Program, // The publisher's Outward target chains for the body it published last (M4).
            ["Binding.memberSelections.rows"] = Rule.Program, // MemberSelections' rows its column's slots index (R7-N5 B1b-2), emptied by each pass's ResetContracts.

            // A run's view, a per-run cache and a walk's reused scratch the reads after a Bind (MIR's) use too (PLAN G949 S1-1, ruling 199 w1
            // (ii)): rebuilt at the run's first read, current in one run, or cleared at the walk's start, so after a step it holds what the
            // program's last read left.
            ["Binding.originUnitIndex"] = Rule.Program, // The C1-F4 view's units of the run's obligations, rebuilt at its first read (PrepareOriginRows).
            ["Binding.originUnits"] = Rule.Program, // Their rows, in the units' reused lists.
            ["Binding.enumerateIndexPool"] = Rule.Program, // The running part's free epoch index entries (EnumerateIndex's, returned at the epoch's end).
            ["Binding.programPart.EnumerateIndexPool"] = Rule.Program, // The same pool, the program pass's part's.
            ["Binding.enumerateGroundsPool"] = Rule.Program, // The running part's T(G) entries, lent by position: its live prefix is enumerateGrounds' values (AssertLivePrefix).
            ["Binding.programPart.GroundsPool"] = Rule.Program, // The same pool, the program pass's part's.
            ["Binding.retentionContracts"] = Rule.Program, // HandedSignatures' walk.
            ["Binding.retentionReached"] = Rule.Program,
            ["Binding.retentionWalk"] = Rule.Program,
            ["Binding.retentionScope"] = Rule.Program, // BeginRetention's walk and its erased signatures'.
            ["Binding.retentionErasedScope"] = Rule.Program,
            ["Binding.retentionPositions"] = Rule.Program,
            ["Binding.retentionStored"] = Rule.Program, // StoredRawSources' visits (D-933), cleared with retentionPositions.
            ["Binding.retentionSources"] = Rule.Program, // The retention sources' walk.
            ["Binding.retentionWalked"] = Rule.Program,
            ["Binding.retentionMet"] = Rule.Program, // The retention rows' walk.
            ["Binding.retentionArguments"] = Rule.Program, // Filled's instantiated inputs, per computation (PLAN G935).
            ["Binding.retentionFills"] = Rule.Program, // Filled's answers, per computation.
            ["Binding.retentionHidden"] = Rule.Program, // HiddenKinds' answers per kept Type, per Storages.
            ["Binding.retentionHiddenSeen"] = Rule.Program, // HiddenKind's walk, per input.
            ["Binding.retentionReaches"] = Rule.Program, // Writes' walk (WrittenThrough), per handed callable.
            ["Binding.retentionHandedTypes"] = Rule.Program, // HandedTypes' answer for the output written last.
            ["Binding.retentionStated"] = Rule.Program, // The stated raw layers one hold walk met (Hold).
            ["Binding.retentionWrites"] = Rule.Program, // The computation's OutputRetentions (a kept one names its library callee: PLAN G935).
            ["Binding.ownSeen"] = Rule.Program, // OwnLayers' walk.
            ["Binding.storageSeen"] = Rule.Program, // The storage walk (BeginStorages).
            ["Binding.storageHolds"] = Rule.Program,
            ["Binding.storageTargeted"] = Rule.Program,
            ["Binding.storagePositions"] = Rule.Program, // With the walked signature, and the Origin-free parts' (Node -1; PLAN G935 R1.2).
            ["Binding.storageTargets"] = Rule.Program, // With the walked signature (Admits; PLAN G935).
            ["Binding.storageBound"] = Rule.Program, // The kept Type's bindings' written roots (HiddenKinds).
            ["Binding.storageKept"] = Rule.Program, // The kept abstract Types the output's walk met (Handed).
            ["Binding.storageResultLeaves"] = Rule.Program, // ResultLeaves' walk and its answer, per computation (PLAN G935).
            ["Binding.storageResultSeen"] = Rule.Program,
            ["Binding.retentionOccurred"] = Rule.Program,
            ["Binding.types.handedFound"] = Rule.Program, // HandedCallables' walk.
            ["Binding.types.handedSeen"] = Rule.Program,
            ["Binding.types.handedPast"] = Rule.Program, // HandedPast's answers, per HandedCallables query (G408-B).
            ["Binding.types.retentionBounds"] = Rule.Program, // A retention computation's Callable bounds.
            ["Binding.types.composedTypes"] = Rule.Program, // Compose's bindings before interning.
            ["Binding.types.composedOrigins"] = Rule.Program,
            ["Binding.types.caseTypes"] = Rule.Program, // CaseSubst's case Types before interning.
            ["Binding.types.witnessOrigins"] = Rule.Program, // A witness instance's Origins before interning.
        };

        // The number rule's fields, by declaring type and name.
        private static readonly HashSet<(Type, string)> Numbers = [.. Disposition.Where(static x => x.Value == Rule.Number)
            .Select(static x => NumberField(x.Key)).Where(static x => x.HasValue).Select(static x => x!.Value)];

        // Compilation-wide roots and the source text: never listed (the library's nodes print through R8-M32's printer).
        private static readonly HashSet<Type> Roots = [typeof(Compilation), typeof(Kotonoha), typeof(CodeContext), typeof(SyntaxTable), typeof(HirTables)];

        // Per-query answer tables (Binding.TypeMemo, DECISIONS BU-19): an entry is read only by the query that wrote it, and the next query
        // begun evicts it, so after a pass no entry is a fact; never listed.
        private static readonly Type PerQuery = typeof(Binding).GetNestedType("TypeMemo`2", BindingFlags.NonPublic)!;

        // Objects listed as stores, never expanded inside a row.
        private static readonly HashSet<Type> Stores = [.. Roots, typeof(Binding), typeof(TyCtxt), typeof(ImplTable), typeof(KimiLibrary)];

        private readonly Kotonoha library;
        private readonly Printer printer = new(false);
        private readonly ulong pass;
        private readonly HashSet<object> listed = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> printing = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<BoundType, Side> typeOwners = new(ReferenceEqualityComparer.Instance);
        private readonly List<string> rows = [];
        private IReadOnlySet<string>? counters; // Fields printed as # by their declaring type's name and their name (the restart row).

        private LibraryLayer(Compilation c)
        {
            this.library = c.Library.Kotonoha;
            this.pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        }

        // How a store compares (the class header's list); Fact is a store's rule when the table does not name it.
        internal enum Rule
        {
            Fact,
            Interner, // The subset rule over a Type interner: an extra row is keyed by a Type above L (the subset Fact).
            Memo, // The subset rule over a memo, or an interner keyed by no Type: an extra row is the program's.
            Epoch,
            Number,
            Program,
        }

        private enum Side
        {
            None,
            Library,
            Program,
        }

        internal static string[] Dump(Compilation c, IReadOnlySet<string>? counters = null)
        {
            var layer = new LibraryLayer(c) { counters = counters };
            layer.Store("Binding", c.Binding, 0);

            // The library layer at its path from Binding (G949 S1-r1b): the walk meets it first through TyCtxt's library, where its
            // part (LibraryLayer.Part, the library pass's per-pass collections) lies past the nesting limit.
            layer.Store("Binding.Library.Layer", c.Library.Layer, 1);

            // Binding's node families (NodeColumns, DECISIONS BU-220) live in each module's HIR tables, which no store walks. A family's store
            // is its key's module, so its rows in the library's columns are the library layer's and a program's are its modules': by one
            // rule every family's library rows, each row's objects (a row's fields, an array's and a pair's elements; a Type or a scalar by
            // its printed form) by the family's name, whatever they name (a program fact there is a difference, never filtered).
            foreach (var family in Families())
            {
                foreach (var value in RowObjects(family.GetValue(c.Library.Kotonoha.Syntax.Hir.Families)!, c.Binding))
                {
                    layer.FamilyRow(Name(family), value);
                }
            }

            // The library's nodes as R8-M32's printer prints them (review C5), the parsed tree's, then each synthesized node's: a node's line
            // (its binding state, failure, Type or Origin, symbol and call facts, by location) and its HIR row (headed by its node's id).
            var (hir, table) = (new Printer(true), c.Library.Kotonoha.Syntax);
            hir.Attach(c);
            hir.Visit(c.Library.Kotonoha.RootKoto);
            for (var id = table.ParsedCount + 1; id <= table.Count; id++)
            {
                hir.Node(table.Nodes[id]!);
            }

            layer.rows.AddRange(hir.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static x => "node " + x));
            layer.rows.AddRange(hir.Hir.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static x => "hir " + x));
            var effects = new List<EffectObligation>(); // Computed on request (MIR's), after the tables: its lazy writes (census O5) stay out of them.
            c.Binding.EffectObligations(effects);
            layer.Table("EffectObligations", effects);
            return [.. layer.rows.Order(StringComparer.Ordinal)];
        }

        // A row's rule, by its store (a number-keyed table's rows are its elements, "[]") or the nearest store the walk listed it inside
        // (programAtoms' column lists its arena as Binding.programAtoms.arena).
        internal static Rule RuleOf(string row)
        {
            var store = row[..(row.IndexOf(' ', StringComparison.Ordinal) is var i && i < 0 ? row.Length : i)];
            for (store = store.EndsWith("[]", StringComparison.Ordinal) ? store[..^2] : store; !Disposition.ContainsKey(store); store = store[..store.LastIndexOf('.')])
            {
                if (!store.Contains('.', StringComparison.Ordinal))
                {
                    return Rule.Fact;
                }
            }

            return Disposition[store];
        }

        // The live-prefix rule of a pool both passes use (F's ruling 178): after a Bind its live prefix is its live store's values in
        // insertion order (the store is only cleared, never removed from), slot by slot; its free tail is not read.
        internal static void AssertLivePrefix(Binding binding, string pool, string live)
        {
            var (slots, values) = ((IList)ValueAt(binding, pool), (IDictionary)ValueAt(binding, live));
            var i = 0;
            foreach (var value in values.Values)
            {
                Assert.True(i < slots.Count && ReferenceEquals(slots[i], value), $"{pool}[{i}] is not its live store's value {i}");
                i++;
            }
        }

        internal static IEnumerable<string> StoresOf(Rule rule) => Disposition.Where(x => x.Value == rule).Select(static x => x.Key);

        // The Type ids a row's key names (a Type prints with its id, `#n`; a number-keyed table's element is its own key).
        internal static IEnumerable<int> KeyIds(string row)
        {
            var key = row[(row.IndexOf(' ', StringComparison.Ordinal) + 1)..];
            key = key.IndexOf(" = ", StringComparison.Ordinal) is var end and >= 0 ? key[..end] : key;
            return Regex.Matches(key, "#([0-9]+)").Select(static x => int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        // The dispositions naming no store or field: a store's path from Binding, a number's field of an integer or a tuple of integers.
        internal static IEnumerable<string> Unresolved()
            => Disposition.Where(static x => x.Value == Rule.Number ? NumberField(x.Key) is null : FieldAt(x.Key) is null).Select(static x => x.Key);

        // A store's value in a Binding, by its path.
        internal static object ValueAt(Binding binding, string store)
        {
            object value = binding;
            foreach (var name in store.Split('.').Skip(1))
            {
                value = Fields(value.GetType()).Single(x => Name(x) == name).GetValue(value)!;
            }

            return value;
        }

        // NodeColumns' families: every field, each a column (a PagedColumn or a StampedColumn, SyntaxIdTest's d7 row).
        internal static IEnumerable<FieldInfo> Families() => Fields(typeof(NodeColumns));

        // A family column's present rows (PagedColumn.Enumerate; a stamped column's current ones, StampedColumn.Enumerate), each flattened
        // to its objects: a composite column's list by its elements, a pair by its items.
        internal static IEnumerable<object> RowObjects(object column, Binding binding)
        {
            var enumerate = column.GetType().GetMethod("Enumerate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var rows = enumerate.Invoke(column, enumerate.GetParameters().Length == 0 ? null : [binding])!;
            var (moveNext, current) = (rows.GetType().GetMethod("MoveNext")!, rows.GetType().GetProperty("Current")!);
            while ((bool)moveNext.Invoke(rows, null)!)
            {
                var row = current.GetValue(rows)!;
                foreach (var part in row is Array items ? items.Cast<object?>() : [row])
                {
                    foreach (var value in part is ITuple pair ? Enumerable.Range(0, pair.Length).Select(i => pair[i]) : [part])
                    {
                        if (value is not null)
                        {
                            yield return value;
                        }
                    }
                }
            }
        }

        // A library node family's row (FamilyRows' library module): an object field by field, a symbol's with its query states; a Type or
        // a scalar by its printed form (G949 N5-2's InheritedOriginTypes and DeferredConstraints).
        internal void FamilyRow(string family, object value)
            => this.rows.Add($"{family} {(value is BoundType || Scalar(value) ? this.Print(value, 0) : this.Fields(value, 0))}{(value is BindingSymbol s ? this.Queries(s) : string.Empty)}");

        private static FieldInfo? FieldAt(string store)
        {
            var (type, field) = (typeof(Binding), (FieldInfo?)null);
            foreach (var name in store.StartsWith("Binding.", StringComparison.Ordinal) ? store.Split('.').Skip(1) : Enumerable.Empty<string>())
            {
                field = Fields(type).SingleOrDefault(x => Name(x) == name);
                if (field is null)
                {
                    return null;
                }

                type = field.FieldType;
            }

            return field;
        }

        private static (Type, string)? NumberField(string entry)
        {
            var (type, name) = (entry[..entry.IndexOf('.', StringComparison.Ordinal)], entry[(entry.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            return typeof(Binding).Assembly.GetTypes().Where(x => x.Name == type).ToArray() is [var owner] &&
                Fields(owner).SingleOrDefault(x => Name(x) == name) is { } field && Integral(field.FieldType) ? (field.DeclaringType!, name) : null;
        }

        private static bool Integral(Type type)
            => Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.UInt64 ||
                (type.IsValueType && typeof(ITuple).IsAssignableFrom(type) && type.GetGenericArguments().All(Integral));

        private static FieldInfo[] Fields(Type type)
            => FieldsOf.GetOrAdd(type, static t => [.. Hierarchy(t).SelectMany(static x => x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))]);

        private static IEnumerable<Type> Hierarchy(Type? type)
        {
            for (; type is not null && type != typeof(object) && type != typeof(ValueType); type = type.BaseType)
            {
                yield return type;
            }
        }

        private static string Name(FieldInfo field) => field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name;

        // A compiler object or a tuple, printed field by field inside a row; any other value prints as a leaf.
        private static bool Expandable(object value)
            => !Stores.Contains(value.GetType()) && value is not Delegate &&
                (value.GetType().Assembly == typeof(Binding).Assembly || value is ITuple || value.GetType().Name.StartsWith("KeyValuePair", StringComparison.Ordinal));

        // A table's elements; a dictionary's as DictionaryEntry (IEnumerable gives KeyValuePair).
        private static IEnumerable<object?> Items(object table)
        {
            if (table is IDictionary entries)
            {
                foreach (DictionaryEntry entry in entries)
                {
                    yield return entry;
                }

                yield break;
            }

            foreach (var item in (IEnumerable)table)
            {
                yield return item;
            }
        }

        private static bool Leaf(object value) => value is Koto or BindingSymbol or BindingScope or BoundType or SourceRef;

        private static bool Scalar(object? value) => value is null or string or Enum || value.GetType().IsPrimitive;

        // Every table of an object: Binding, then ImplTable, TyCtxt, KimiLibrary and the other compiler objects it holds.
        private void Store(string store, object owner, int nesting)
        {
            this.listed.Add(owner);
            if (owner.GetType().IsGenericType && owner.GetType().GetGenericTypeDefinition() == PerQuery)
            {
                return;
            }

            foreach (var field in Fields(owner.GetType()))
            {
                var (name, value) = ($"{store}.{Name(field)}", field.GetValue(owner));
                if (Disposition.GetValueOrDefault(name) is Rule.Epoch or Rule.Program)
                {
                    continue; // The program's memo or scratch, not the library layer.
                }

                if (value is IEnumerable and not string)
                {
                    this.Table(name, value);
                }
                else if (value is not null && nesting < Nesting && value.GetType().Assembly == typeof(Binding).Assembly && !Roots.Contains(value.GetType())
                    && !Leaf(value) && !this.listed.Contains(value))
                {
                    this.Store(name, value, nesting + 1);
                }
            }
        }

        // A table's library rows: keyed entries by key (a name-keyed entry by its value), a list's or set's elements. A table keyed by a
        // number (TyCtxt's Types by hash) lists its values as elements. Rows compare as a multiset: a list's order is not compared (c15
        // compares the aggregates' order at base and head).
        private void Table(string store, object table)
        {
            foreach (var item in Items(table))
            {
                if (item is DictionaryEntry entry && entry.Key.GetType().IsPrimitive)
                {
                    this.Element(store + "[]", entry.Value);
                }
                else if (item is DictionaryEntry keyed)
                {
                    if (this.Owner(keyed.Key is string ? keyed.Value : keyed.Key, 0) == Side.Library)
                    {
                        this.rows.Add($"{store} {this.Print(keyed.Key, 0)} = {this.Print(keyed.Value, 0)}");
                    }
                }
                else
                {
                    this.Element(store, item);
                }
            }
        }

        private void Element(string store, object? item)
        {
            if (item is IEnumerable and not string)
            {
                this.Table(store, item);
            }
            else if (!Scalar(item) && this.Owner(item, 0) == Side.Library)
            {
                this.rows.Add($"{store} {this.Print(item, 0)}");
            }
        }

        // Whether a value names library declarations only (Library), any program declaration (Program) or none (None).
        private Side Owner(object? value, int depth)
        {
            switch (value)
            {
                case null:
                    return Side.None;
                case Koto node:
                    return ReferenceEquals(node.CodeContext?.Kotonoha, this.library) ? Side.Library : Side.Program;
                case BindingSymbol symbol:
                    return this.Owner(symbol.Declaration, depth);
                case BindingScope scope:
                    return this.Owner(scope.Owner, depth);
                case SourceRef id:
                    return id.Module == SyntaxTable.LibraryModule ? Side.Library : Side.Program;
                case BoundType type:
                    return this.TypeOwner(type);
            }

            var side = Side.None;
            if (depth >= Depth || Scalar(value) || !(value is IEnumerable || Expandable(value)))
            {
                return side;
            }

            foreach (var part in this.Parts(value))
            {
                side = (Side)Math.Max((int)side, (int)this.Owner(part, depth + 1));
                if (side == Side.Program)
                {
                    break;
                }
            }

            return side;
        }

        // A Type's side at any depth (a program Type argument may sit deep inside a library Type), once per interned Type.
        private Side TypeOwner(BoundType type)
        {
            if (this.typeOwners.TryGetValue(type, out var side))
            {
                return side;
            }

            this.typeOwners[type] = Side.None; // A Type met again inside itself (a Closure's context) adds nothing.
            foreach (var part in this.Parts(type))
            {
                side = (Side)Math.Max((int)side, (int)this.Owner(part, 1));
            }

            return this.typeOwners[type] = side;
        }

        private IEnumerable<object?> Parts(object value)
        {
            if (value is IDictionary entries)
            {
                foreach (DictionaryEntry entry in entries)
                {
                    yield return entry.Key;
                    yield return entry.Value;
                }

                yield break;
            }

            if (value is IEnumerable items)
            {
                foreach (var item in items)
                {
                    yield return item;
                }

                yield break;
            }

            foreach (var field in Fields(value.GetType()))
            {
                yield return field.GetValue(value);
            }
        }

        private string Print(object? value, int depth)
        {
            switch (value)
            {
                case null:
                    return "-";
                case string text:
                    return text;
                case Koto node:
                    return Where(node);
                case BindingSymbol symbol:
                    return $"{symbol.Kind}:{symbol.Name}@{Where(symbol.Declaration)}";
                case BindingScope scope:
                    return "scope@" + Where(scope.Owner);
                case BoundType type:
                    return this.printer.Type(type) + "#" + type.Id.ToString(CultureInfo.InvariantCulture);
                case SourceRef id:
                    return id.Module == SyntaxTable.LibraryModule ? "@" + Where(this.library.Syntax.Nodes[id.Id]) : $"{id.Module}#{id.Id}";
                case not null when Scalar(value):
                    return Convert.ToString(value, CultureInfo.InvariantCulture)!;
                case IEnumerable items:
                    return depth >= Depth ? "[..]" : this.Items(items, depth);
            }

            if (depth >= Depth || !Expandable(value) || !this.printing.Add(value))
            {
                return value.GetType().Name;
            }

            var fields = this.Fields(value, depth);
            this.printing.Remove(value);
            return fields;
        }

        private string Items(IEnumerable items, int depth)
        {
            var printed = new List<string>();
            if (items is IDictionary entries)
            {
                foreach (DictionaryEntry entry in entries)
                {
                    // A number key (a hash) prints no key, as a table keyed by one lists its values.
                    printed.Add(entry.Key.GetType().IsPrimitive ? this.Print(entry.Value, depth + 1) : $"{this.Print(entry.Key, depth + 1)}:{this.Print(entry.Value, depth + 1)}");
                }
            }
            else
            {
                foreach (var item in items)
                {
                    printed.Add(this.Print(item, depth + 1));
                }
            }

            return "[" + string.Join(", ", items is IList ? printed : printed.Order(StringComparer.Ordinal)) + "]";
        }

        private string Fields(object value, int depth)
        {
            var text = new StringBuilder(value.GetType().Name).Append('{');
            foreach (var field in Fields(value.GetType()))
            {
                var (name, x) = (Name(field), field.GetValue(value));
                var stamp = x is ulong or uint && (name.Contains("pass", StringComparison.OrdinalIgnoreCase) || name.Contains("version", StringComparison.OrdinalIgnoreCase));
                var counter = this.counters?.Contains(field.DeclaringType!.Name + "." + name) == true;
                text.Append(name).Append('=').Append(counter || Numbers.Contains((field.DeclaringType!, name)) ? "#"
                    : stamp ? this.Stamp(Convert.ToUInt64(x, CultureInfo.InvariantCulture)) : this.Print(x, depth + 1)).Append(' ');
            }

            return text.Append('}').ToString();
        }

        // A pass stamp by its distance from the latest pass (Binding's storageVersion).
        private string Stamp(ulong stamp)
            => stamp == 0 ? "0" : stamp <= this.pass ? $"pass-{this.pass - stamp}" : stamp.ToString(CultureInfo.InvariantCulture);

        private string Queries(BindingSymbol symbol)
            => " queries=" + string.Join(',', Enum.GetValues<DeclQueryKind>().Select(x => $"{x}:{symbol.QueryState(x, this.pass)}"));
    }
}
