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
    // The empty program's L (EveryLibraryTypeIsInTheLibrarysPrefix).
    private static readonly Lazy<int> EmptyProgramTypeCount = new(() => Bound(string.Empty).Library.Layer.TypeCount);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo[]> FreezeFieldsOf = new();

    // The objects the walk does not enter: the compilation-wide roots and syntax (a library node by its own symbol), Binding and its
    // memos, TyCtxt's interners and the library's registrar.
    private static readonly HashSet<Type> WalkEnds = [typeof(Compilation), typeof(Binding), typeof(TyCtxt), typeof(KimiLibrary), typeof(Kotonoha), typeof(CodeContext), typeof(SyntaxTable), typeof(HirTables)];

    /// <summary>Gets c2's programs (the design's coverage row c2: R8-M32's ten), as the layer rows name them.</summary>
    public static TheoryData<string> ReferencePrograms
        => [string.Empty, "example/Hello", "milestone/Milestone26", "milestone/Milestone27", "milestone/Milestone30", "milestone/Milestone31",
            "milestone/Milestone32", "milestone/Milestone35", "example/Ranges", "example/Utf8Formatting"];

    // k3 (E4's tripwire; F's ruling 167, Q3): no library conformance path has a standard Property witness (SPEC 11.9.2: a stored Field
    // witnessing a Property requirement, which a bridge accessor executes), so no program can make a witness bridge on a library path
    // (PropertyWitnessFunction makes one per path and witness) and S1-1 needs no eager bridge step: every library path's Property
    // witnesses are accessor calls (95 verified paths today). The unit that gives a library Type the first standard witness removes this
    // Fact and adds the eager step over the base paths, returning a library bridge without its refresh (s11-design section 4, E4).
    [Fact]
    public void TheLibraryDeclaresNoStandardPropertyWitness()
    {
        var c = Bound(string.Empty);
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
        var c = Bound(string.Empty);
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

    // F-1 and F-3 (s11-design 2.3 and 6.3, RC4, RC7; G949 ruling 167): the library's freeze verifies its query states and refuses a
    // library with records, so on each target class the library rows cover (probe 1: pointer widths 64 with the default
    // x86_64-pc-windows-msvc, 32 and 16; eight triples gave no record and identical library MIR) the empty program's Final Bind
    // completes with no record of any kind, warnings included, and every library header DeclQuery binds and every library struct's
    // inherited conformances are Done and Frozen.
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
    // object, so a program pass reads them and writes none: every bound Contract reference of the layer's part has its effect bounds
    // (EffectState 2), every library scope, and every scope of a library path (its own, its requirements' premises, its overlaps'),
    // denotes its current view (SerialOf would write none), and every local region of the layer's part has its sources formed. Before B1,
    // after the empty program's Bind, 4 of 5 bound references had no effect bounds, 595 of 1,187 library scopes and 123 of 171 path
    // scopes a view SerialOf would write, and all 19 regions had no sources (Version -1).
    [Fact]
    public void TheLibraryPassLeavesNoLazyFactOnTheLibrary()
    {
        var c = Bound(string.Empty);
        var layer = c.Library.Layer.Part;
        Assert.NotEmpty(layer.BoundContracts);
        Assert.Empty(layer.BoundContracts.Values.Where(static x => x.Contract!.EffectState != 2).Select(static x => x.Type!.ToString()));
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

        Assert.Empty(scopes.Where(static x => State(x) != x.ViewState).Select(static x => x.Owner?.ToString()));
        Assert.NotEmpty(layer.Regions);
        Assert.Empty(layer.Regions.Where(static x => x.Value.Version == -1).Select(static x => x.Key.ToString()));

        // The chain state SerialOf compares with a scope's ViewState, read without writing (ContractSolver.SerialOf).
        static (int, int, int) State(BindingScope scope) => (scope.Constraints?.Version ?? 0, scope.LinkVersion, scope.Parent is { } parent ? Serial(parent) : 0);

        static int Serial(BindingScope scope) => State(scope) != scope.ViewState ? scope.ViewSerial + 1 : scope.ViewSerial;
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
            foreach (var list in new[] { (IList)contract.Ancestors, (IList)contract.Requirements, (IList)contract.AssociatedTypes, contract.ClauseStorage, (IList)contract.EffectBounds }) // Each a storage List.
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

    // The library layer's objects by reachability from its roots (d6, and the stamp walk f1 will read; s11-design 6.3): every present row of the library's node
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
                if (ReferenceEquals(node.CodeContext?.Kotonoha, c.Library.Kotonoha) && node.SymbolOf() is { } own && ReferenceEquals(own.Declaration, node))
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
}
