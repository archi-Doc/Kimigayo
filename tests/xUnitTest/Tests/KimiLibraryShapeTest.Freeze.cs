// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// PLAN G949 S1-1 (the library pass once per compilation; G949\s11\plan.md, section 3): what the library pass makes for S1-1's freeze. From
// S1-1 the library pass runs once and every later Bind is a program pass, so a fact a library object holds is made by the library pass
// itself, never on demand by a program pass; these rows read the library's facts after the empty program's Final Bind.
public partial class KimiLibraryShapeTest
{
    // f1's exemptions (s11-design 2.2, n1-n10): a field the walk selects by its width or name that is no stamp, by its declaring Type's
    // name and its name, with the reason no reader compares it with a per-compilation counter after the freeze; and the library's
    // stamped columns the library pass never writes, or writes as its working state and the freeze releases (NodeColumns.Freeze's
    // Release rows), by their family's name (stamp 0 and no row: a column without a library row reads absent, as before the freeze). An
    // entry naming no field or family fails the row.
    private static readonly Dictionary<string, string> NotStamps = new(StringComparer.Ordinal)
    {
        ["OriginRequirementWork.Consumer"] = "n1: read only behind Pass == the requirement call, never for a frozen work",
        ["BindingScope.LinkVersion"] = "n2: a change counter of the scope's link, which no pass changes on a library scope",
        ["ConstraintEnvironment.Version"] = "n2: a change counter of the environment, which no pass changes on a library scope",
        ["BindingScope.ViewSerial"] = "n3: a change counter SerialOf compares for equality; the library pass settles it (B1)",
        ["BindingScope.ViewState"] = "n3: the chain state the view serial names",
        ["ViewIndex.Serial"] = "a Closure(view) entry's view serial at its forming, compared for equality with its scope's (n3's counter, settled by B1)",
        ["BinderSet.low"] = "a word of a binder bit set, selected by its width",
        ["BindingObligation.Condition"] = "an obligation's condition mask, selected by its width",
        ["SyntaxTable.Version"] = "n4: the module's syntax change counter, compared only with its numbering memo",
        ["SyntaxTable.numberedVersion"] = "n4: the numbering memo",
        ["BoundContract.State"] = "n5: a completion state, not a counter copy (complete in the layer's part: e3)",
        ["BoundContract.EffectState"] = "n5: the effect bounds' completion state (complete in the layer's part: k4)",
        ["ComponentSummary.Epoch"] = "n6: a per-epoch memo of Binding's, the program's, not a library object",
        ["Binding.comparisonPlans"] = "n7: Binding's per-pass memo keyed by library symbols, refilled by a program pass",
        ["Binding.requirementCalls"] = "n7: Binding's per-pass memo keyed by library symbols, refilled by a program pass",
        ["Binding.hirVersion"] = "n8: a per-compilation counter of Binding's, on no library object",
        ["Binding.originRowsVersion"] = "n8",
        ["Binding.recursiveVersion"] = "n8",
        ["Binding.frameEpoch"] = "n8",
        ["Binding.retentionStamp"] = "n8",
        ["Binding.summaryEpoch"] = "n8",
        ["InstanceStorage.Version"] = "n9: the run whose substitution formed an instance (Binding.instances, a Type-keyed memo), never on a library object",
        ["CheckedSet.Version"] = "n10: MIR's, outside Binding (no MIR freeze, O-381 (b))",
        ["MirProgram.Version"] = "n10",
        ["NodeColumns.VirtualOverrides"] = "never written: the library declares no virtual function",
        ["NodeColumns.VirtualEffectBounds"] = "never written: the library declares no virtual function",
        ["NodeColumns.ImplicitConstructorDecisions"] = "never written: the library declares no derived structure",
        ["NodeColumns.ObjectErasures"] = "never written: the library erases no object view",
        ["NodeColumns.ShapeContractHeads"] = "never written: the library declares no shape contract (TheLibraryDeclaresNoShapeContract)",
        ["NodeColumns.AliasTargets"] = "never written: the library declares no alias",
        ["NodeColumns.ImportedContainers"] = "never written: the library declares no alias, so no qualifier selects an alias import",
        ["NodeColumns.QualificationFailures"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.PerCallOrigins"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.PairFollows"] = "never written: the library follows no pair layer",
        ["NodeColumns.ImplicitPairFollows"] = "never written: the library follows no pair layer",
        ["NodeColumns.PhantomRawOrigins"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.InvalidStaticOriginSlots"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.CallablePremiseFailures"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.EffectBoundRejections"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.InferredArrayLengths"] = "never written: the library writes no length hole",
        ["NodeColumns.ArrayInferenceShapes"] = "never written: the library writes no fixed-array annotation with a hole",
        ["NodeColumns.CallInferenceFailures"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.CallableSelectionFailures"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.BaseCallFailures"] = "never written: the library pass records no failure (one refuses it: FreezeLibrary)",
        ["NodeColumns.ObjectErasureFailures"] = "never written: the library erases no object view",
        ["NodeColumns.ReceiverOperations"] = "released at the freeze: its library rows are read only by the HIR publisher at the library's publication, before the freeze, and TryGetReceiverOperation refuses a library node (R7-N5 B2a-2; TheFreezeReleasesTheLibrarysWorkingState)",
        ["NodeColumns.ResultCompletions"] = "released at the freeze: the library pass's result checks' query memo, its working state, no reader reads after it (R7-N5 B6b)",
        ["NodeColumns.GuessedCompletions"] = "released at the freeze: the guess rule's query memo, its working state; the guess rule answers none at a frozen node (R7-N5 B6b)",
        ["NodeColumns.PublishedCompletions"] = "released at the freeze: the HIR publisher's query memo, read only at the library's publication, before the freeze (R7-N5 B6b)",
        ["NodeColumns.LintCompletions"] = "released at the freeze: the lint walk's query memo; the library is not linted (R7-N5 B6b)",
        ["NodeColumns.ResultContexts"] = "released at the freeze: the library pass's result contexts, its working state, no reader reads after it (R7-N5 B2a-2; TheFreezeReleasesTheLibrarysWorkingState)",
    };

    // w1's counters: an instance's Version (n9), the run whose substitution formed a memo row (Binding.instances), its currency, not its
    // content, printed as #.
    private static readonly HashSet<string> MemoStamps = ["InstanceStorage.Version"];

    // The empty program's layer with w1's counters (NoLaterStepWritesTheLibraryLayer).
    private static readonly Lazy<string[]> EmptyProgramGuardedLayer = new(() => LibraryLayer.Dump(Bound(string.Empty), MemoStamps));

    // The empty program's L (EveryLibraryTypeIsInTheLibrarysPrefix).
    private static readonly Lazy<int> EmptyProgramTypeCount = new(() => Bound(string.Empty).Library.Layer.TypeCount);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo[]> FreezeFieldsOf = new();

    // The objects the walk does not enter: the compilation-wide roots and syntax (a library node by its own symbol), Binding and its
    // memos, TyCtxt's interners and the library's registrar; and a NodeColumns, whose library rows the walk reaches through the
    // library's families (LibraryLayer.Families) and whose part copy (PassPart.ForeignFacts) holds rows only at the part's foreign
    // entries, which the library part never has (R7-N5 B4, C1: AFactALibraryNodesFailureCarriesIsRecordedWithIt).
    private static readonly HashSet<Type> WalkEnds = [typeof(Compilation), typeof(Binding), typeof(TyCtxt), typeof(KimiLibrary), typeof(Kotonoha), typeof(CodeContext), typeof(SyntaxTable), typeof(HirTables), typeof(NodeColumns)];

    /// <summary>Gets c2's programs (the design's coverage row c2: R8-M32's ten), as the layer rows name them.</summary>
    public static TheoryData<string> ReferencePrograms
        => [string.Empty, "example/Hello", "milestone/Milestone26", "milestone/Milestone27", "milestone/Milestone30", "milestone/Milestone31",
            "milestone/Milestone32", "milestone/Milestone35", "example/Ranges", "example/Utf8Formatting"];

    // k3 (E4's tripwire; F's ruling 167, Q3), strengthened as t3 (PLAN G949 Stage 2 U3, design R3.7 C2): no library contract declares a
    // Property requirement, so no library conformance path has a Property witness, and so none a standard one (SPEC 11.9.2: a stored Field
    // witnessing a Property requirement, which a bridge accessor executes): no program makes or refreshes a witness bridge on a frozen
    // path (PropertyWitnessFunction makes one per path and witness) and S1-1 needs no eager bridge step. The unit that gives a library
    // contract the first Property requirement routes those writes by U4's two levels, adds the eager step over the base paths (s11-design
    // section 4, E4) and removes this Fact.
    [Fact]
    public void TheLibraryDeclaresNoStandardPropertyWitness()
    {
        var c = Bound(string.Empty);
        var requirements = KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<PropertyKoto>().Where(static x => x.DeclarationKind == PropertyDeclarationKind.Requirement).Select(static x => Line(x)).ToArray();
        var removal = "A library contract declares a Property requirement, so PropertyWitnessFunction (through InstantiatePropertyRequirementCall) " +
            "creates or refreshes in place a bridge on a frozen path (PropertyBridges; the bridge's receiver, Type, accessor and body) and " +
            "OperationOrigins extends its PropertyWitnessOrigins: route them by U4's two levels, as Binding.Hop's (PLAN G949 Stage 2 U3), with " +
            "k3's E4 eager step, then remove this row: ";
        Assert.True(requirements.Length == 0, removal + string.Join(", ", requirements));
        var paths = DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Type && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha))
            .SelectMany(x => c.Binding.Semantics.Conformances(x) ?? [])
            .SelectMany(static x => x.Paths)
            .ToArray();
        Assert.NotEmpty(paths);
        Assert.Empty(paths.SelectMany(static x => x.PropertyWitnesses.Where(static w => w.Kind != PropertyWitnessKind.AccessorCall).Select(w => $"{x.Type.Name} is {x.Contract.Name}: {w.Kind}")));
    }

    // DeclQuery's Frozen column (B3; s11-design section 5, D-721's line on BU-105): a declaration query state stamped Frozen is the library
    // pass's in every pass, and a program pass's demand writes none of it: Running and NotStarted leave it (a not-Done SignatureVerdict
    // is then formed at each use, PLAN G375), and a completion is a broken contract. The stamp and states are set here by reflection on
    // one library function (from B4 the freeze writes them; e2 reads the per-use path through a program). Before B3 the Running write
    // took the frozen state (Current reads a Frozen stamp as the pass's).
    [Fact]
    public void AFrozenQueryStateIsNeverWritten()
    {
        var c = BoundOverItsOwnLibrary(string.Empty);
        var function = DeclaredSymbols(c).First(x => x.Kind == BindingSymbolKind.Function && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha));
        var stamp = typeof(BindingSymbol).GetField("queryPass", BindingFlags.Instance | BindingFlags.NonPublic)!;
        stamp.SetValue(function, Binding.Frozen32);
        typeof(BindingSymbol).GetField("queries", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(function, (ushort)((int)DeclState.Done << ((int)DeclQueryKind.Signature * 2)));
        var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)! + 1;
        function.SetQueryState(DeclQueryKind.SignatureVerdict, pass, DeclState.Running);
        function.SetQueryState(DeclQueryKind.Signature, pass, DeclState.NotStarted);
        var refused = Assert.Throws<InvalidOperationException>(() => function.SetQueryState(DeclQueryKind.SignatureVerdict, pass, DeclState.Done));
        Assert.Contains("(InternalInvariant; PLAN G949 S1-1)", refused.Message, StringComparison.Ordinal);
        Assert.Equal(Binding.Frozen32, stamp.GetValue(function));
        Assert.Equal(DeclState.NotStarted, function.QueryState(DeclQueryKind.SignatureVerdict, pass));
        Assert.Equal(DeclState.Done, function.QueryState(DeclQueryKind.Signature, pass));
    }

    // e7 (s11 plan finding F2): the freeze stamps both counter families of the library's columns, so an edit, which advances the edit
    // counter, keeps the library's edit family (AssociatedOrigins: an associated Type declaration's Origin parameters): its rows are
    // the same objects right after the edit and after the next Bind. Before B4 the edit left the column stale (no rows) until the next
    // run's library pass rewrote it; a freeze that skipped the edit family would leave it stale for good.
    [Fact]
    public void AnEditKeepsTheLibrarysAssociatedOrigins()
    {
        var c = Bound(string.Empty);
        var rows = Rows(c);
        Assert.NotEmpty(rows);
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        Assert.Equal(rows, Rows(c), ReferenceEqualityComparer.Instance);
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(rows, Rows(c), ReferenceEqualityComparer.Instance);

        static object[] Rows(Compilation c)
            => [.. LibraryLayer.Families().Where(static x => x.Name == "AssociatedOrigins").SelectMany(x => LibraryLayer.RowObjects(x.GetValue(c.Library.Kotonoha.Syntax.Hir.Families)!, c.Binding))];
    }

    // Ruling 203 (4) (B6): an Item of a library requirement member (`P.tryResolve`, in two bodies) gets its call context at MIR time
    // (RequirementItemContext), which interns the member's Input atoms; from the freeze such an atom is a row of the program's
    // (programAtoms), never one of the library's OriginAtoms column, which gained the member's row at Analyze before. The store lives
    // as the item contexts whose plans hold its atoms (ruling 205 N4): a second Bind and Analyze keep the same atom objects, and an edit
    // empties both (before N4 it was emptied per pass, so after a second Bind the memoized plans held atoms the store no longer had).
    // SPEC 8.11.3, 7.7.4: the program is valid (MirEffectsTest's atItem row), without records.
    [Fact]
    public void ARequirementItemsAtomsAreTheProgramPasss()
    {
        var c = CompilationTestHelper.Parse("group H\n    public func atItem<P>(p: P, length: isize) -> Option<isize>\n        P is Position\n" +
            "        let resolve = P.tryResolve\n        return resolve(p, length)\n    public func atItem2<P>(p: P, length: isize) -> Option<isize>\n" +
            "        P is Position\n        let resolve = P.tryResolve\n        return resolve(p, length)\npublic func main()\n    _ = H.atItem(3, 10)\n" +
            "    _ = H.atItem2(5@i64, 10)\n");
        Assert.True(c.Bind().IsComplete);
        var atoms = LibraryAtoms(c);
        c.Analyze();
        Assert.Empty(TestDiagnostics.Of(c));
        Assert.Equal(atoms, LibraryAtoms(c));
        var program = ProgramAtoms(c);
        Assert.Equal(2, program.Count); // tryResolve's two inputs, interned once for both bodies.
        Assert.All(program, x => Assert.Same(c.Library.Kotonoha.Syntax, x.Binder!.SyntaxOwner));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal<object>(program, ProgramAtoms(c), ReferenceEqualityComparer.Instance);
        c.Analyze();
        Assert.Equal(atoms, LibraryAtoms(c));
        Assert.Equal<object>(program, ProgramAtoms(c), ReferenceEqualityComparer.Instance);
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        Assert.Empty(ProgramAtoms(c));

        static List<BoundOrigin> LibraryAtoms(Compilation c) => Atoms(c, c.Library.Kotonoha.Syntax.Hir.Families.OriginAtoms);

        static List<BoundOrigin> ProgramAtoms(Compilation c)
            => Atoms(c, (PagedColumn<BoundOrigin?[]?>)typeof(Binding).GetField("programAtoms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!);

        static List<BoundOrigin> Atoms(Compilation c, PagedColumn<BoundOrigin?[]?> column)
        {
            var atoms = new List<BoundOrigin>();
            for (var id = 1; id <= c.Library.Kotonoha.Syntax.Count; id++)
            {
                atoms.AddRange((column.Get(id) ?? []).OfType<BoundOrigin>());
            }

            return atoms;
        }
    }

    // r1 (s11-design 3.1, 6.3; PLAN G949 S1-1 B6; ruling 203 (3b)): from the freeze no later step writes the library layer. After a
    // Final Bind each refusing writer throws the internal-invariant form on a library node, row or base store, which the compilation
    // reports as InternalInvariant_Kd: a new id (Allocate, so EnsureId), an edit (NoteSyntaxEdit, so no renumbering), a HIR row (Of, so
    // every column and node-family write), an existing plan row (PlanFor), a row's reset, the publisher's entry, the base ImplTable's
    // Begin and Register, a query state of a library declaration whether or not the freeze stamped it (its table decides), a new
    // parent (RecordRegionBound) or a new solution (RecordOpening) on a library region row, and a region row of frozen library atoms only
    // (contract C's create-time assertion, n55 rule 6; G949 ruling 205 N3); an identical re-record writes nothing and is not refused. The
    // program table's writers still work.
    [Fact]
    public void EveryWriteToTheFrozenLibraryIsRefused()
    {
        var c = BoundOverItsOwnLibrary(string.Empty);
        var table = c.Library.Kotonoha.Syntax;
        var node = c.Library.Kotonoha.RootKoto;
        var planned = table.Nodes.ToArray().First(x => x is not null && table.Hir.PlanIndexes[x.SyntaxId] != 0)!;
        _ = HirTables.Of(c.Kotonoha.RootKoto);
        var count = table.Count;
        Refused(() => table.Allocate(node));
        Refused(() => c.NoteSyntaxEdit(node));
        Refused(() => { _ = HirTables.Of(node); });
        Refused(() => { _ = HirTables.PlanFor(planned, written: false); });
        Refused(() => HirTables.Reset(node));
        var publisher = typeof(Binding).GetNestedType("HirPublisher", BindingFlags.NonPublic)!;
        Refused(() => publisher.GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Activator.CreateInstance(publisher, c.Binding), [table, node]));
        Assert.Equal(count, table.Count);

        var function = DeclaredSymbols(c).First(x => x.Kind == BindingSymbolKind.Function && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha));
        var stamp = typeof(BindingSymbol).GetField("queryPass", BindingFlags.Instance | BindingFlags.NonPublic)!;
        stamp.SetValue(function, 0u); // As if the freeze's stamp had missed it: its table still refuses.
        var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)! + 1;
        function.SetQueryState(DeclQueryKind.Signature, pass, DeclState.Running);
        Refused(() => function.SetQueryState(DeclQueryKind.Signature, pass, DeclState.Done));
        Assert.Equal(0u, stamp.GetValue(function));

        var (target, row) = c.Library.Layer.Part.Regions.First(static x => x.Value.Parents.Count != 0);
        var source = c.Library.Layer.Part.Regions.Keys.First(x => !ReferenceEquals(x, target) && !row.Parents.Contains(x));
        var record = typeof(Binding).GetMethod("RecordRegionBound", BindingFlags.Instance | BindingFlags.NonPublic)!;
        _ = record.Invoke(c.Binding, [row.Parents[0], target, row.Values[0]]);
        Refused(() => record.Invoke(c.Binding, [source, target, node]));
        Assert.DoesNotContain(source, row.Parents);

        // A new solution: RecordOpening on a library row and one of its parents, so its bound is an identical re-record and only the
        // solution is written. The library opens no region (no row is keyed by the meet of a row and a parent), so the solution's row is
        // made here as the library pass would make it, frozen, and removed after the refusal.
        var regions = c.Library.Layer.Part.Regions;
        var meet = typeof(Binding).GetMethod("Meet", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var (region, bound, solution) = regions.SelectMany(static x => x.Value.Parents.Select(p => (Region: x.Key, Bound: p)))
            .Select(x => (x.Region, x.Bound, Solution: (BoundOrigin)meet.Invoke(c.Binding, [x.Bound, x.Region])!))
            .First(x => !ReferenceEquals(x.Solution, x.Region) && !regions.ContainsKey(x.Solution));
        var solved = new Binding.LocalRegion { Version = Binding.FrozenRegion };
        regions.Add(solution, solved);
        var opening = typeof(Binding).GetMethod("RecordOpening", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Refused(() => opening.Invoke(c.Binding, [region, bound, node]));
        Assert.Null(solved.Solved);
        Assert.True(regions.Remove(solution));

        // A row of frozen library atoms only: a bound on a library atom that has no row makes none, in either part.
        var atom = LibraryLayer.Families().Where(static x => x.Name == "OriginAtoms")
            .SelectMany(x => LibraryLayer.RowObjects(x.GetValue(table.Hir.Families)!, c.Binding)).OfType<BoundOrigin>().First(x => x.Binder is not null && !regions.ContainsKey(x));
        Refused(() => record.Invoke(c.Binding, [source, atom, node]));
        var program = (Binding.PassPart)typeof(Binding).GetField("programPart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        Assert.False(regions.ContainsKey(atom) || program.Regions.ContainsKey(atom));

        var impls = (ImplTable)typeof(Binding).GetField("impls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var path = impls.Paths.ToArray().First(static x => x.Type.Declaration.Kotonoha.Syntax.IsLibrary);
        Refused(() => impls.Register(path.Type, path.Contract, path.RootContract, (IsKoto)path.Use, path.Scope.Parent!, path.Premises));
        Refused(() => impls.Begin(Binding.Frozen, new([], c.Binding.Library.Kotonoha, new())));

        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        Assert.True(c.Bind().IsComplete);

        static void Refused(Action write)
        {
            var thrown = Xunit.Record.Exception(write);
            var refused = thrown is TargetInvocationException { InnerException: { } inner } ? inner : thrown;
            Assert.Contains("(InternalInvariant; PLAN G949 S1-1)", Assert.IsType<InvalidOperationException>(refused).Message, StringComparison.Ordinal);
        }
    }

    // s23 (PLAN G949 Stage 2, U0b-C2): a reader of a per-pass table, which the pass reset empties, given a node of the frozen library
    // refuses it as the internal-invariant form: the node's row was the library pass's and is gone, so its "none" would be silent. Each
    // row reads one such reader after the empty program's Bind with a library node found through the frozen library module's syntax: c1
    // IsResolvedSlice a slice selection (a range-value key first), c2 PairAdmitted an @follow (a pair follow first), c3
    // HasExclusiveIndexer a user index (the first that selects an index call), c3b ImplicitPairAdmitted a receiver operand and c3c
    // TryGetReceiverOperation a member access; the refusal is decided by the node's table, not by a row. c4: the same readers given a
    // program's nodes after its Bind answer (SPEC 4.7.1, 13.5.5; DynamicArraySliceTest's exclusive slice program). Before U0b-C2 every
    // library read answered "none".
    [Fact]
    public void EveryPassReadOfAFrozenLibraryNodeIsRefused()
    {
        var c = Bound(string.Empty);
        var table = c.Library.Kotonoha.Syntax;
        Assert.True(table.Frozen);
        var nodes = table.Nodes.ToArray().Where(static x => x is not null).Select(static x => x!).ToArray();
        var indexes = nodes.OfType<IndexKoto>().ToArray();
        var slice = indexes.Where(x => x.TypeOf(c)?.Kind == BoundTypeKind.Slice).OrderBy(static x => x.Right is RangeKoto).First();
        var follows = nodes.OfType<ConversionKoto>().Where(x => x.ConversionOf(c).Kind is ConversionBinding.PairFollow or ConversionBinding.Follow).ToArray();
        var follow = follows.FirstOrDefault(x => x.ConversionOf(c).Kind == ConversionBinding.PairFollow) ?? follows.First();
        var index = indexes.FirstOrDefault(x => c.Binding.SelectedIndexCall(x, exclusive: false) is not null) ?? indexes.First();
        var member = nodes.OfType<MemberAccessKoto>().First();
        Refused(nameof(Binding.IsResolvedSlice), () => c.Binding.IsResolvedSlice(slice));
        Refused(nameof(Binding.PairAdmitted), () => c.Binding.PairAdmitted(follow));
        Refused(nameof(Binding.HasExclusiveIndexer), () => c.Binding.HasExclusiveIndexer(index));
        Refused(nameof(Binding.ImplicitPairAdmitted), () => c.Binding.ImplicitPairAdmitted(member.Left));
        Refused(nameof(Binding.TryGetReceiverOperation), () => c.Binding.TryGetReceiverOperation(member, out _));

        var p = CompilationTestHelper.BindSuccess("var values: Array<i32> = [20, 22]\nlet handle = values@uniq\nlet view = handle[..]\nrequire view[0] + view[1] == 42 else => $abort(\"exclusive\")\nhandle@follow@uniq.clear()");
        var walked = KotoTree.Walk(p.Kotonoha.RootKoto).ToArray();
        Assert.Contains(walked, static x => x is IndexKoto);
        foreach (var node in walked)
        {
            _ = (p.Binding.IsResolvedSlice(node), p.Binding.PairAdmitted(node), p.Binding.HasExclusiveIndexer(node), p.Binding.ImplicitPairAdmitted(node));
            _ = p.Binding.TryGetReceiverOperation(node, out _);
        }

        static void Refused(string reader, Action read)
        {
            var message = Assert.IsType<InvalidOperationException>(Xunit.Record.Exception(read)).Message;
            Assert.Contains(reader, message, StringComparison.Ordinal);
            Assert.Contains("(InternalInvariant; PLAN G949 U0b-C2)", message, StringComparison.Ordinal);
        }
    }

    // e2 (s11-design 5, 6.3; G375; G949 ruling 187 (c)): a frozen library verdict is read, never written, so a program's per-use check
    // over a library function leaves the frozen verdict and the records as the library pass made them; one the freeze left not Done is
    // formed at each use in the user's scope. None is today (EveryLibrarySignatureVerdictIsProven), so the row clears every library
    // function's SignatureVerdict by reflection after the first Bind (the stamps stay Frozen): a second Bind of Hello then forms the
    // verdicts its uses demand (more signature walks than a second Bind without the clearing), keeps its Result and records, and leaves
    // every cleared state NotStarted and Frozen. Before B4 nothing was Frozen; with the freeze but without the per-use rule, the use's
    // formation proved the verdict and its completion threw the frozen state's internal invariant.
    [Fact]
    public void AFrozenVerdictThatIsNotDoneIsFormedAtEachUse()
    {
        var (walks, result, records) = Second(clear: false);
        var (cleared, clearedResult, clearedRecords) = Second(clear: true);
        Assert.True(cleared > walks, $"{cleared} signature walks with the verdicts cleared, {walks} without");
        Assert.Equal(result, clearedResult);
        Assert.Equal(records, clearedRecords);

        static (int Walks, BindingResult Result, string[] Records) Second(bool clear)
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
            CompilationTestHelper.UseEditedLibrary(c, static _ => { }); // A library of its own: the row writes its query states (x1).
            Assert.True(c.Bind().IsComplete);
            var queries = typeof(BindingSymbol).GetField("queries", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var functions = DeclaredSymbols(c).Where(x => x.Kind == BindingSymbolKind.Function && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha)).ToArray();
            Assert.All(functions, static x => Assert.True(x.QueriesFrozen));
            foreach (var function in clear ? functions : [])
            {
                queries.SetValue(function, (ushort)((ushort)queries.GetValue(function)! & ~(3 << ((int)DeclQueryKind.SignatureVerdict * 2))));
            }

            var result = c.Bind();
            var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
            Assert.All(functions, x => Assert.True(x.QueriesFrozen && (x.QueryState(DeclQueryKind.SignatureVerdict, pass) == DeclState.NotStarted) == clear));
            var walks = (int)typeof(Binding).GetField("signatureWalks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
            return (walks, result, [.. TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Span}")]);
        }
    }

    // F-1 and F-3 (s11-design 2.3 and 6.3, RC4, RC7; G949 rulings 167, 205 C1): the library's freeze verifies its query states and
    // refuses a library with records of any kind (issues, derived issues, non-failing records: lints and warnings), so on each target
    // class the library rows cover (probe 1: pointer widths 64 with the default x86_64-pc-windows-msvc, 32 and 16; eight triples gave
    // no record and identical library MIR) the empty program's Final Bind completes with no record of any kind, warnings included, and
    // every library header DeclQuery binds and every library struct's inherited conformances are Done and Frozen.
    [Theory]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("i686-unknown-linux-gnu")]
    [InlineData("avr-unknown-unknown")]
    public void TheLibraryPassCompletesEveryQuery(string target)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", "public func main() => ()\n"));
        Assert.True(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        c.Binding.ReportStartupDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c).Select(static x => $"{x.Severity} {x.Code} {x.Path} {x.Span}"));
        var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var library = DeclaredSymbols(c).Where(x => ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha)).ToArray();
        Assert.Empty(library.Where(x => !x.QueriesFrozen).Select(static x => x.Name));
        Assert.Empty(library.Where(x => x.Kind is BindingSymbolKind.Function or BindingSymbolKind.Property && x.Declaration is FunctionKoto or PropertyKoto
            && x.QueryState(DeclQueryKind.Signature, pass) != DeclState.Done).Select(static x => x.Name));
        Assert.Empty(library.Where(x => x.Declaration is StructKoto && x.QueryState(DeclQueryKind.InheritedConformances, pass) != DeclState.Done).Select(static x => x.Name));
    }

    // k4 (B1: E3's effect bounds, E7, R): the library pass completes the facts a later pass would otherwise form lazily on a library
    // object, so a program pass reads them and writes none: every Contract the library declares and every bound Contract reference of the
    // layer's part has its effect bounds (EffectState 2), every library scope, and every scope of a library path (its own, its
    // requirements' premises, its overlaps'), denotes its current view (SerialOf would write none), and every local region of the layer's
    // part has its sources formed. Before B1, after the empty program's Bind, 4 of 5 bound references had no effect bounds, 595 of 1,187
    // library scopes and 123 of 171 path scopes a view SerialOf would write, and all 19 regions had no sources (Version -1); before w1 (i)
    // the declared Contracts no node of the library pass holds (the `#Lang` markers Copy, Owned, Sealed, ObjectPayload and
    // PrimitiveInteger) had none, which Analyze then formed.
    [Fact]
    public void TheLibraryPassLeavesNoLazyFactOnTheLibrary()
    {
        var c = Bound(string.Empty);
        var layer = c.Library.Layer.Part;
        Assert.NotEmpty(layer.BoundContracts);
        Assert.Empty(layer.BoundContracts.Values.Where(static x => x.Contract!.EffectState != 2).Select(static x => x.Type!.ToString()));
        var declared = DeclaredSymbols(c).Where(x => x.Contract is not null && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha)).ToArray();
        Assert.NotEmpty(declared);
        Assert.Empty(declared.Where(static x => x.Contract!.EffectState != 2).Select(static x => x.Name));
        Assert.Empty(UnsettledLibraryScopes(c));
        Assert.NotEmpty(layer.Regions);
        Assert.Empty(layer.Regions.Where(static x => x.Value.Version == -1).Select(static x => x.Key.ToString()));
    }

    // f1 (C-380 F1; s11-design 2.2 and 6.3): the freeze leaves no counter value on the library. After the first Bind of each of c2's
    // programs, a second Bind, a Provisional then Final Bind and an edit then a Bind, every object reachable from the library layer's
    // roots (LibraryObjects) holds Frozen in every field of a stamp's width (ulong, uint) whatever its name, and in every int field named
    // as a stamp (*Stamp, *Pass, *Version, *Epoch, *Serial), unless NotStamps names it with its reason; so is each library stamped
    // column's stamp, both counter families' (s4, F2). Before S1-1's freeze every stamp held its run's counter value.
    [Theory]
    [MemberData(nameof(ReferencePrograms))]
    public void TheFreezeLeavesNoCounterOnTheLibrary(string program)
    {
        Assert.Empty(NotStamps.Keys.Where(static x => !Declared(x)).ToArray());
        var c = Bound(program);
        NoCounter(c, "the first Bind");
        c.Bind();
        NoCounter(c, "a second Bind");
        c.Binding.Bind(BindingMode.Provisional);
        c.Bind();
        NoCounter(c, "a Provisional then Final Bind");
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        c.Bind();
        NoCounter(c, "an edit then a Bind");

        static void NoCounter(Compilation c, string step)
        {
            var found = Counters(c).ToArray();
            Assert.True(found.Length == 0, $"After {step}:\n{string.Join('\n', found)}");
        }

        // A NotStamps entry's Type (by name, nested ones included) declares the field, an auto-property's backing field by its property.
        static bool Declared(string entry)
        {
            var (type, member) = (entry[..entry.IndexOf('.', StringComparison.Ordinal)], entry[(entry.IndexOf('.', StringComparison.Ordinal) + 1)..]);
            return typeof(Binding).Assembly.GetTypes().Where(x => x.Name == type).SelectMany(FreezeFields).Any(x => FieldName(x) == member);
        }
    }

    // R7-N5 B2a-2 (B's rulings 1 and 6 on C-403, the Front's R1 and R4; DECISIONS BU-220 as amended): a working-state family has no
    // library base. The library pass writes its rows in the freeze compilation's Binding (whose pool keeps the result contexts it
    // rented; with either Release row skipped, NoLaterStepWritesTheLibraryLayer and TheLibraryLayerAfterARestartIsTheEmptyPrograms
    // find the library pass's 125 receiver operations and 204 result contexts) and the freeze releases the library's ResultContexts and
    // ReceiverOperations columns: stamp 0, no row and no arena. Counters are per Binding (R8-M53): a reading compilation's first run
    // has the library pass's counter value, 1 (FreezeLibrary refuses a rerun), so a column the freeze left at that stamp would answer
    // the library pass's rows (and the freeze Binding's pooled contexts) there; released, no library node's row answers, while the
    // program's own `if` keeps its row.
    [Fact]
    public void TheFreezeReleasesTheLibrarysWorkingState()
    {
        var c = CompilationTestHelper.Parse("func f(b: bool) -> i32\n    let x = if b => 1 else => 2\n    return x\n", "Hello.kimi");
        var kimigayo = Compilation.CreateForTest().Kimigayo;
        var freeze = KimiLibrary.Unfrozen(c.TargetTriple, kimigayo, new Kimi.Project(kimigayo));
        var library = KimiLibrary.Freeze(freeze);
        c.UseLibrary(library);
        Assert.True(c.Bind().IsComplete);
        Assert.NotEmpty((IList)typeof(Binding).GetField("resultPool", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(freeze.Binding)!);
        Assert.Equal(1UL, Counter(c.Binding));
        var families = library.Kotonoha.Syntax.Hir.Families;
        Released(families.ResultContexts);
        Released(families.ReceiverOperations);
        var nodes = library.Kotonoha.Syntax.Nodes.ToArray().Where(static x => x is not null).Select(static x => x!).ToArray();
        Assert.Contains(nodes, static x => x is IfKoto or MatchKoto or DoKoto or LoopKoto);
        Assert.All(nodes, x => Assert.Null(families.ResultContexts.Get(x.SyntaxId, c.Binding)));
        Assert.All(nodes, x => Assert.Null(families.ReceiverOperations.Get(x.SyntaxId, c.Binding).Source));
        var target = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IfKoto>().Single();
        Assert.NotNull(c.Kotonoha.Syntax.Hir.Families.ResultContexts.Get(target.SyntaxId, c.Binding));

        static ulong Counter(Binding binding) => (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binding)!;

        static void Released<T>(StampedColumn<T, Binding.PassCounter> column)
        {
            object boxed = column;
            var rows = boxed.GetType().GetField("rows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boxed)!;
            Assert.Null(rows.GetType().GetField("arena", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rows));
            Assert.Equal(0UL, (ulong)boxed.GetType().GetField("stamp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boxed)!);
        }
    }

    // d6 and its prefix clauses (d2b-d 1.4; s11-design 4, E1 and E2; G949 ruling 167 Q4): L, the last Type id the library pass gave
    // (LibraryLayer.TypeCount, set by the freeze), is the empty program's in every compilation, and every Type a library object holds
    // (each reachable from the layer's roots, the library's ErasedTypes and Meanings columns too) has an id at most L, after the first
    // and a second Bind of each of c2's programs and after a Provisional first Bind: no pass after the freeze makes a library fact. No
    // late Contract Self threatens it (ruling 171): the six marker Contracts that have no Self (Copy, Owned, Callable, Sealed,
    // ObjectPayload and PrimitiveInteger, `#Lang` Contracts without a member) are in no pass's nodes, and every ContractSelfType caller
    // acts inside a Contract's own declaration or members, so no pass, the program's or the library's, makes theirs.
    [Theory]
    [MemberData(nameof(ReferencePrograms))]
    public void EveryLibraryTypeIsInTheLibrarysPrefix(string program)
    {
        var c = Bound(program);
        InThePrefix(c);
        c.Bind();
        InThePrefix(c);
        var provisional = Prepared(program);
        Assert.Equal(BindingMode.Provisional, provisional.Binding.Bind(BindingMode.Provisional).Mode);
        InThePrefix(provisional);

        static void InThePrefix(Compilation c)
        {
            var (library, hir) = (c.Library.Layer.TypeCount, c.Library.Kotonoha.Syntax.Hir);
            Assert.Equal(EmptyProgramTypeCount.Value, library);
            Assert.Empty(LibraryObjects(c, hir.ErasedTypes, hir.Meanings).OfType<BoundType>().Where(x => x.Id > library).Select(static x => $"{x}#{x.Id}").Distinct());
        }
    }

    // w1 (the write guard; design R1.4, c2's new rows; G949 ruling 199): the library layer's dump after the empty program's first Bind is
    // the dump after each later step of a compilation of each of c2's programs: Analyze, a second Bind, a Provisional Bind and an edit then
    // a Bind (the first Bind's is TheLibraryLayerIsIndependentOfTheProgram's), each store by its disposition: a fact equal, a memo a
    // superset with every earlier row unchanged (MemoStamps), an epoch or program store not compared, and a library scope's view serial
    // and state and a library Origin requirement work's Consumer by value (ruling 205 C2, the row below). Before S1-1 every run's
    // library pass rewrote the layer; before w1 (i) Analyze formed the effect bounds of the library's `#Lang` marker Contracts (Copy's
    // EffectState 0 -> 2), and before ruling 199's dispositions the rows MIR's reads add to Binding's memos and scratch read as facts.
    [Theory]
    [MemberData(nameof(ReferencePrograms))]
    public void NoLaterStepWritesTheLibraryLayer(string program) => SameLayerAfterEachStep(program);

    [Theory(Explicit = true)] // Its length (PLAN G949 S1-0b): run with the library-layer corpus row.
    [MemberData(nameof(LayerCorpus))]
    public void NoLaterStepWritesTheLibraryLayerOfTheCorpus(string program) => SameLayerAfterEachStep(program);

    // w1's reach (G949 ruling 205 C2; s11-design 3.2 O28, 4 E7, 6.3 w1; plan F6): a library scope's view serial and the chain state it
    // names, and a library Origin requirement work's Consumer mark, compare by value, so a later step's write to one is a difference:
    // each written here after the empty program's Bind changes w1's dump. Before ruling 205 the dump printed the three as # (the number
    // rule), and such a write passed w1.
    [Theory]
    [InlineData("ViewSerial")]
    [InlineData("ViewState")]
    [InlineData("Consumer")]
    public void W1ComparesTheLibrarysViewSerialsAndConsumersByValue(string field)
    {
        var c = BoundOverItsOwnLibrary(string.Empty); // It writes frozen library objects: on a library of its own (x1, X11).
        var families = c.Library.Kotonoha.Syntax.Hir.Families;
        var dump = LibraryLayer.Dump(c, MemoStamps);
        var scope = First(families.Scopes.Enumerate());
        var work = First(families.OriginRequirementNodes.Enumerate());
        switch (field)
        {
            case "ViewSerial":
                scope.ViewSerial++;
                break;
            case "ViewState":
                var (environment, link, parent) = scope.ViewState;
                scope.ViewState = (environment + 1, link, parent);
                break;
            default:
                work.Consumer++;
                break;
        }

        Assert.NotEqual(dump, LibraryLayer.Dump(c, MemoStamps));

        static T First<T>(PagedColumn<T?>.Enumerator rows)
            where T : class
        {
            foreach (var row in rows)
            {
                if (row is not null)
                {
                    return row;
                }
            }

            throw new InvalidOperationException("The library has no row of the family.");
        }
    }

    // e3 (O15; s11-design E3 and 6.3): the library's bound Contract references are the layer part's, complete when the freeze stamps the
    // library: every entry's State 2 with its effect bounds built (EffectState 2), and every reference an entry's Contract holds (its
    // ancestors, its requirements' and associated Types' declaring Contracts) a declaration's own symbol or an entry of the part. After
    // a second Bind, Analyze and an edit then a Bind of Hello they are the same objects with the same storage, never rewritten (each
    // list's mutation counter unchanged). Before S1-1 each run's library pass reset its entries (ResetContracts) and built them again.
    [Fact]
    public void TheLibrarysBoundContractsAreKept()
    {
        var c = Bound("example/Hello");
        var declared = new HashSet<BindingSymbol>(DeclaredSymbols(c), ReferenceEqualityComparer.Instance);
        var layer = c.Library.Layer.Part.BoundContracts;
        Assert.NotEmpty(layer);
        Assert.Empty(layer.Values.Where(static x => x.Contract is not { State: 2, EffectState: 2 }).Select(static x => x.Type!.ToString()));
        var entries = new HashSet<BindingSymbol>(layer.Values, ReferenceEqualityComparer.Instance);
        Assert.Empty(layer.Values.SelectMany(static x => x.Contract!.Ancestors.Concat(x.Contract.Requirements.Concat(x.Contract.AssociatedTypes).Select(static r => r.Contract)))
            .Where(x => !declared.Contains(x) && !entries.Contains(x)).Select(static x => x.Type?.ToString() ?? x.Name));
        var kept = Kept(c);
        c.Bind();
        Assert.Equal(kept, Kept(c));
        c.Analyze();
        Assert.Equal(kept, Kept(c));
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        c.Bind();
        Assert.Equal(kept, Kept(c));

        static object[] Kept(Compilation c) => [.. c.Library.Layer.Part.BoundContracts.SelectMany(static x => Storage(x.Key, x.Value))];

        static IEnumerable<object> Storage(BoundType key, BindingSymbol reference)
        {
            var contract = reference.Contract!;
            yield return key;
            yield return reference;
            yield return contract;
            yield return (contract.State, contract.EffectState, contract.HasEffectBounds, contract.HasUnresolvedParents);
            // Each view is its storage List.
            foreach (var list in new[] { (IList)contract.Ancestors, (IList)contract.Requirements, (IList)contract.AssociatedTypes, contract.ClauseStorage, (IList)contract.EffectBounds })
            {
                yield return list;
                yield return (int)list.GetType().GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(list)!;
                foreach (var item in list)
                {
                    yield return item!;
                }
            }
        }
    }

    // e5 (s11-design annex m6, K; s10): a program reaching a library slot's keepers reads the layer part's lists, frozen. SPEC 22.2.1's
    // example, valid by it and by SPEC 15.3.5 (a Type's UniqIterable entry returns Kimi.Iteration.BorrowingIterator<Self> during its
    // source, whose `source` slot the library keeps): its records and MIR are the same after the first and a second Bind and Analyze,
    // and the layer's keeper lists, the RequirementCall they follow included, are Frozen32, the same objects with the same keepers.
    // Before S1-1 they held the library pass's latest requirement call, rewritten in every run.
    [Fact]
    public void AProgramReadsTheLibrarysFrozenKeepers()
    {
        var c = CompilationTestHelper.Parse("struct Countdown\n    Self is Iterator\n    Self is IntoIterable\n    Self is UniqIterable\n    associate Iterator.Item is i32\n" +
            "    associate IntoIterable.IteratorType is Self\n    associate UniqIterable.IteratorType(a) is Kimi.Iteration.BorrowingIterator<Self> during a\n    var remaining: i32 = 3\n    public init() => ()\n" +
            "    public func next(self: uniq/Self) -> Option<i32>\n        if self.remaining == 0 => return .None\n        self.remaining -= 1\n        return .Some(self.remaining)\n" +
            "    public func intoIterator(self: Self) -> Self\n        return self@move\n" +
            "    public func iterateUniq(self: uniq/Self during source) -> Kimi.Iteration.BorrowingIterator<Self> during source\n        return Kimi.Iteration.borrowing(self)\n" +
            "var total: i32 = 0\nvar countdown = Countdown.init()\nfor number in countdown@uniq\n    total += number\n    exit\nfor number in countdown@move\n    total += number\n" +
            "require total == 3 else => $abort(\"total\")\nConsole.writeLine(\"countdown\")");
        var layer = c.Library.Layer.Part;
        (string Mir, string[] Records, object[] Keepers)? first = null;
        for (var bind = 0; bind < 2; bind++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Analyze();
            var mir = new StringBuilder();
            MirDump.Write(c.ConstructMir(), 0, mir);
            var now = (mir.ToString(), TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Span}").ToArray(), layer.Keepers.SelectMany(static x => new object[] { x.Key, x.Value, x.Value.Pass, string.Join(',', x.Value.Items.ToArray()) }).ToArray());
            Assert.NotEmpty(now.Item3);
            Assert.Equal(Binding.Frozen32, layer.RequirementCall);
            Assert.All(layer.Keepers.Values, static x => Assert.Equal(Binding.Frozen32, x.Pass));
            first ??= now;
            Assert.Equal(first.Value.Mir, now.Item1);
            Assert.Equal(first.Value.Records, now.Item2);
            Assert.Equal(first.Value.Keepers, now.Item3);
        }
    }

    // e6 (s11-design R and 6.3; contract C): from the freeze the layer's regions hold their sources for every pass (FrozenRegion), while a
    // program region row is the program pass's: one created by a Bind, and one the next Bind's run resets, reads its sources for the run
    // (its Version the run's region version after the read, never FrozenRegion), and a read of a library region writes nothing. SPEC
    // 15.3.6, 15.4.4: a mutable local's region is bounded by the values fitted to it, so the result is valid during (a and b) and its
    // twin during a is UnprovenOriginRelation_Kd, its record kept by the second Bind.
    [Theory]
    [InlineData("(a and b)", null)]
    [InlineData("a", "UnprovenOriginRelation_Kd")]
    public void AProgramRegionIsRecomputedAfterTheFreeze(string result, string? code)
    {
        var c = CompilationTestHelper.Parse("func f(c: bool, a: ref/i32, b: ref/i32) -> ref/i32 during " + result +
            "\n    var p = a\n    let pair: (ref/i32 during p, ref/i32) = if c => (p, p) else => (p, b)\n    return pair.1\npublic func main() => ()\n");
        var layer = c.Library.Layer.Part.Regions;
        var own = (Dictionary<BoundOrigin, Binding.LocalRegion>)typeof(Binding).GetField("localRegions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        for (var check = 0; check < 2; check++)
        {
            c.Check(); // The Final Bind, then Analyze (the result's relation is MIR's, SPEC 15.3.6).
            Assert.Equal(code is null ? [] : [code], TestDiagnostics.Of(c).Select(static x => x.Code));
            Assert.NotEmpty(own);
            foreach (var origin in own.Keys)
            {
                _ = c.Binding.LocalRegionSources(origin);
            }

            Assert.All(own.Values, x => Assert.Equal(c.Binding.RegionVersion, x.Version));
            Assert.DoesNotContain(own.Values, static x => x.Version == Binding.FrozenRegion);
            var (library, region) = layer.First();
            var (sources, version) = (region.Sources.ToArray(), Mutations(region.Sources));
            Assert.Equal(sources, c.Binding.LocalRegionSources(library).ToArray());
            Assert.Equal((Binding.FrozenRegion, version), (region.Version, Mutations(region.Sources)));
        }

        static int Mutations(List<BoundOrigin> list) => (int)typeof(List<BoundOrigin>).GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(list)!;
    }

    // F-2 (s11-design 3.2 N3 (1); review N4): a tripwire, true today. A library path that a program struct inherits inference from would
    // gain the program's consumers (AddInferenceConsumer), a per-pass fact list on a frozen base path, cleared with the library's by each
    // pass. After the empty program's Final Bind no library path is an inference source. The unit that adds the first one designs the
    // overlay list (owned by ImplTable) with the amending line on BU-220 beside D-726, and removes this Fact.
    [Fact]
    public void NoLibraryPathIsAnInferenceSource()
    {
        var c = Bound(string.Empty);
        var paths = DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Type && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha))
            .SelectMany(x => c.Binding.Semantics.Conformances(x) ?? []).SelectMany(static x => x.Paths).ToArray();
        Assert.NotEmpty(paths);
        Assert.Empty(paths.Where(static x => x.InferenceConsumers is { Count: not 0 }).Select(static x => $"{x.Type.Name} is {x.Contract.Name}"));
    }

    // t2 (PLAN G949 Stage 2 U3, design R3.7 C2; F-2's program side): a tripwire, true today. No library struct is `open`, so no program
    // struct inherits a library base (SPEC 6: a sealed structure cannot be a base) and no frozen path is an inherited path's source.
    [Fact]
    public void TheLibraryDeclaresNoOpenStruct()
    {
        var open = KotoTree.Walk(Bound(string.Empty).Library.Kotonoha.RootKoto).OfType<StructKoto>().Where(static x => (x.Modifier & ModifierKind.Open) != 0).Select(static x => Line(x)).ToArray();
        var removal = "A library struct is open, so a library base makes a frozen path an inherited path's source, which a program pass " +
            "writes (PublishInheritedInference's AddInferenceConsumer; VerifyConformance of an inherited source): route those writes by U4's " +
            "two levels, as Binding.Hop's (PLAN G949 Stage 2 U3), then remove this row and F-2: ";
        Assert.True(open.Length == 0, removal + string.Join(", ", open));
    }

    // c1 (PLAN G949 Stage 2 U3, design R3.7 C1): the freeze leaves every base conformance path verified and none at its OCC-X limit, for
    // every key, so verification stops at every frozen path (VerifyConformance's IsVerified return) and no reading compilation verifies
    // one on the shared object. A path whose verification is open is the obligation table's (R7-C2e2 e2-3b; no path field): it is not
    // verified, and the freeze refuses an open one (FreezeLibrary's ObligationDepth).
    [Theory]
    [InlineData("x86_64-pc-windows-msvc")]
    [InlineData("i686-pc-windows-msvc")]
    [InlineData("avr-unknown-unknown")]
    [InlineData("")]
    public void EveryBaseConformancePathIsVerified(string target)
    {
        var library = KimiLibrary.Of(target.Length == 0 ? Kimi.Compiler.Target.TargetTriple.Invalid : Kimi.Compiler.Target.TargetTriple.Parse(target));
        var paths = ((IDictionary)typeof(ImplTable).GetField("paths", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(library.Layer.Impls)!)
            .Values.Cast<BoundConformancePath>().Where(static x => x.Current).ToArray();
        Assert.NotEmpty(paths);
        Assert.Empty(paths.Where(static x => !x.IsVerified || x.Unsupported).Select(static x => $"{x.Type.Name} is {x.Contract.Name}"));
    }

    // c1's twin (invalid use: an internal contract; SPEC 12.7, OCC-X): a library whose inherited conformance (an exclusive witness reached
    // through its base) no check needs is left unverified at its limit with no record, so its freeze is the internal-invariant form and
    // the library stays unfrozen. Before c1 it froze with that path unverified, which every reading compilation would verify again.
    [Fact]
    public void AFreezeRefusesAnUnverifiedConformancePath()
    {
        var freeze = Unfrozen(Kimi.Compiler.Target.TargetTriple.Parse(WindowsProfile.Target), debugAndSetting: false);
        freeze.Library.Kotonoha.CreateCodeContext().Parse(
            freeze.Library.Kotonoha.RootKoto,
            "contract ZBumps\n    func bump(self: uniq/Self) -> ()\nopen struct ZBase\n    Self is ZBumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct ZLeaf: ZBase\n    public init() => ()\n");
        var refused = Assert.Throws<InvalidOperationException>(() => KimiLibrary.Freeze(freeze));
        Assert.Contains("unverified (InternalInvariant; PLAN G949 Stage 2 U3)", refused.Message, StringComparison.Ordinal);
        Assert.False(freeze.Library.Kotonoha.Syntax.Frozen);
    }

    /// <summary>Gets every field of every object reachable from a reading compilation's frozen library (LibraryObjects) and of every
    /// library node, by value (the implementation review's N1; its probe's form): a primitive or string by value, a reference by identity,
    /// a collection by identity, count and its elements' values two levels deep, a struct two levels deep.</summary>
    /// <param name="reader">A compilation reading the library.</param>
    /// <returns>Each object's field values, by the object's identity.</returns>
    internal static Dictionary<object, string[]> FieldSnapshot(Compilation reader)
    {
        var map = new Dictionary<object, string[]>(ReferenceEqualityComparer.Instance);
        foreach (var value in LibraryObjects(reader))
        {
            if (!value.GetType().IsValueType)
            {
                map[value] = Fields(value);
            }
        }

        foreach (var node in reader.Library.Kotonoha.Syntax.Nodes)
        {
            if (node is not null)
            {
                map[node] = Fields(node);
            }
        }

        return map;

        static string[] Fields(object value) => [.. FreezeFields(value.GetType()).Select(f => $"{f.DeclaringType!.Name}.{FieldName(f)}={Shallow(f.GetValue(value), 2)}")];

        static string Shallow(object? x, int depth)
        {
            switch (x)
            {
                case null:
                    return "-";
                case string s:
                    return "\"" + s + "\"";
                case Enum or bool or char:
                    return x.ToString()!;
                case Delegate:
                    return "delegate#" + RuntimeHelpers.GetHashCode(x);
            }

            var type = x.GetType();
            if (type.IsPrimitive)
            {
                return Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture)!;
            }

            if (x is ICollection collection)
            {
                var hash = default(HashCode);
                if (depth > 0)
                {
                    foreach (var item in collection)
                    {
                        hash.Add(Shallow(item, depth - 1));
                    }
                }

                return $"#{RuntimeHelpers.GetHashCode(x)}[{collection.Count}:{hash.ToHashCode()}]";
            }

            return type.IsValueType ? (depth == 0 ? type.Name : type.Name + "(" + string.Join(",", FreezeFields(type).Select(f => Shallow(f.GetValue(x), depth - 1))) + ")")
                : "#" + RuntimeHelpers.GetHashCode(x);
        }
    }

    /// <summary>Gets the changes between two field snapshots (FieldSnapshot): an object gained or lost, and each field whose value moved,
    /// with its holder.</summary>
    /// <param name="before">The earlier snapshot.</param>
    /// <param name="after">The later snapshot.</param>
    /// <returns>The changes; empty when no field of any object changed.</returns>
    internal static string[] FieldChanges(Dictionary<object, string[]> before, Dictionary<object, string[]> after)
    {
        var changes = new List<string>();
        foreach (var (key, fields) in after)
        {
            if (!before.TryGetValue(key, out var old))
            {
                changes.Add($"new {key.GetType().Name} {key}");
                continue;
            }

            for (var i = 0; i < fields.Length; i++)
            {
                if (fields[i] != old[i])
                {
                    changes.Add($"{Holder(key)}: {old[i]} -> {fields[i]}");
                }
            }
        }

        changes.AddRange(before.Keys.Where(x => !after.ContainsKey(x)).Select(static x => $"gone {x.GetType().Name} {x}"));
        return [.. changes];

        static string Holder(object key) => key switch
        {
            BindingSymbol s => $"{s.Kind}:{s.Name}@{CompilationTestHelper.Location(s.Declaration)}",
            Koto k => $"{k.GetType().Name}@{CompilationTestHelper.Location(k)}",
            _ => key.GetType().Name,
        };
    }

    // w1's comparison after each later step.
    private static void SameLayerAfterEachStep(string program)
    {
        var c = Bound(program);
        c.Analyze();
        Same("Analyze");
        c.Bind();
        Same("a second Bind");
        c.Binding.Bind(BindingMode.Provisional);
        Same("a Provisional Bind");
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        c.Bind();
        Same("an edit then a Bind");

        void Same(string step) => SameLayer($"{program} after {step}", EmptyProgramGuardedLayer.Value, LibraryLayer.Dump(c, MemoStamps), c.Library.Layer.TypeCount);
    }

    // The library's scopes, and every scope of a library path (its own, its requirements' premises, its overlaps'), whose view SerialOf
    // would still write: the owners of those whose chain state is not the view state they hold (k4; PLAN G949 Stage 2 U2b's s20b).
    private static string[] UnsettledLibraryScopes(Compilation c)
    {
        var scopes = new List<BindingScope>();
        foreach (var scope in c.Library.Kotonoha.Syntax.Hir.Families.Scopes.Enumerate())
        {
            if (scope is not null)
            {
                scopes.Add(scope);
            }
        }

        foreach (var path in DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Type && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha))
            .SelectMany(x => c.Binding.Semantics.Conformances(x) ?? []).SelectMany(static x => x.Paths))
        {
            scopes.Add(path.Scope);
            scopes.AddRange((path.WitnessScopes ?? []).Select(static x => x.Premises));
            scopes.AddRange((path.OverlapScopes ?? []).Select(static x => x.Scope));
        }

        Assert.NotEmpty(scopes);
        return [.. scopes.Where(static x => State(x) != x.ViewState).Select(static x => x.Owner?.ToString() ?? "<no owner>")];

        // The chain state SerialOf compares with a scope's ViewState, read without writing (ContractSolver.SerialOf).
        static (int, int, int) State(BindingScope scope) => (scope.Constraints?.Version ?? 0, scope.LinkVersion, scope.Parent is { } parent ? Serial(parent) : 0);

        static int Serial(BindingScope scope) => State(scope) != scope.ViewState ? scope.ViewSerial + 1 : scope.ViewSerial;
    }

    // f1's findings: each selected field off Frozen, by its declaring Type and name, with the first holder and the count.
    private static IEnumerable<string> Counters(Compilation c)
    {
        var found = new List<(string Field, string Holder)>();
        foreach (var family in LibraryLayer.Families().Where(static x => x.FieldType.Name.StartsWith("StampedColumn", StringComparison.Ordinal)))
        {
            var column = family.GetValue(c.Library.Kotonoha.Syntax.Hir.Families)!;
            var stamp = (ulong)column.GetType().GetField("stamp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(column)!;
            var never = NotStamps.ContainsKey("NodeColumns." + FieldName(family));
            if (never ? stamp != 0 || (int)column.GetType().GetProperty("Used", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(column)! != 0 : stamp != Binding.Frozen)
            {
                found.Add(($"NodeColumns.{FieldName(family)}.stamp", stamp.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        foreach (var value in LibraryObjects(c))
        {
            foreach (var field in FreezeFields(value.GetType()))
            {
                var (key, x) = ($"{field.DeclaringType!.Name}.{FieldName(field)}", field.GetValue(value));
                var stamp = x is ulong or uint || (x is int && FieldName(field) is var name && (name.EndsWith("Stamp", StringComparison.Ordinal) || name.EndsWith("Pass", StringComparison.Ordinal)
                    || name.EndsWith("Version", StringComparison.Ordinal) || name.EndsWith("Epoch", StringComparison.Ordinal) || name.EndsWith("Serial", StringComparison.Ordinal)));
                if (stamp && !NotStamps.ContainsKey(key) && x is not (Binding.Frozen or Binding.Frozen32 or Binding.FrozenRegion))
                {
                    found.Add(($"{key} = {x}", value is BindingSymbol s ? $"{s.Kind}:{s.Name}" : value.GetType().Name));
                }
            }
        }

        return found.GroupBy(static x => x.Field).Select(static x => $"{x.Key} ({x.Count()}): {string.Join(", ", x.GroupBy(static h => h.Holder).Select(static h => h.Count() == 1 ? h.Key : $"{h.Key} x{h.Count()}"))}");
    }

    // The library layer's objects by reachability from its roots (f1, d6; s11-design 6.3): every present row of the library's node
    // families (the `symbols` family's library store, the scopes, the library's Origin requirement works among them), the layer (Impls
    // with its identities, paths and satellites; Part with every part collection) and $abort, and the given roots; from each object its
    // fields, a collection's elements, and from a library node its own symbol (an accessor's execution function's). A program node and
    // WalkEnds end the walk.
    private static List<object> LibraryObjects(Compilation c, params object[] roots)
    {
        var (seen, work, found) = (new HashSet<object>(ReferenceEqualityComparer.Instance), new Stack<object>(roots.Where(static x => x is not null)), new List<object>());
        work.Push(c.Library.Layer);
        work.Push(c.Library.Abort);
        foreach (var family in LibraryLayer.Families())
        {
            foreach (var row in LibraryLayer.RowObjects(family.GetValue(c.Library.Kotonoha.Syntax.Hir.Families)!, c.Binding))
            {
                work.Push(row);
            }
        }

        while (work.TryPop(out var value))
        {
            var type = value.GetType();
            if (value is string or Delegate or Enum || type.IsPrimitive || WalkEnds.Contains(type) || (!type.IsValueType && !seen.Add(value)))
            {
                continue;
            }

            if (value is Koto node)
            {
                if (ReferenceEquals(node.CodeContext?.Kotonoha, c.Library.Kotonoha) && HirTables.Symbol(node) is { } own && ReferenceEquals(own.Declaration, node))
                {
                    work.Push(own);
                }
            }
            else if (type.Assembly == typeof(Binding).Assembly || value is ITuple || type.Name.StartsWith("KeyValuePair", StringComparison.Ordinal))
            {
                found.Add(value);
                foreach (var field in FreezeFields(type))
                {
                    if (field.GetValue(value) is { } x)
                    {
                        work.Push(x);
                    }
                }
            }
            else if (value is IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item is not null)
                    {
                        work.Push(item);
                    }
                }
            }
        }

        return found;
    }

    private static FieldInfo[] FreezeFields(Type type)
        => FreezeFieldsOf.GetOrAdd(type, static t =>
        {
            var fields = new List<FieldInfo>();
            for (var x = t; x is not null && x != typeof(object) && x != typeof(ValueType); x = x.BaseType)
            {
                fields.AddRange(x.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            }

            return [.. fields];
        });

    // A field's name, an auto-property's backing field by its property.
    private static string FieldName(FieldInfo field) => field.Name.StartsWith('<') ? field.Name[1..field.Name.IndexOf('>', StringComparison.Ordinal)] : field.Name;
}
