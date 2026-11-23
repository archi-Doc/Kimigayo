// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;
using Printer = XunitTest.SemanticSnapshotTest.SemanticPrinter;

namespace XunitTest;

// PLAN G949 Stage 1 (the design's coverage rows c13 and c17): the library pass binds the Kimi library once per compilation, and every
// later Bind is a program pass over the source modules. c13's node half passes from G949 S1-r1b's split (the library pass binds the
// library's nodes into the layer's part), its version half from S1-1's freeze; c17 passes since S1-e (S1-1's SyntaxTable refusal lands
// only after it), its corpus row Explicit as the library-layer corpus row is.
public partial class KimiLibraryShapeTest
{
    // c13 (bound once): after the first Bind, a second Bind's node table holds the program's nodes only, while its Result keeps the
    // library layer's counts (design R1.1, c15). PLAN G949 S1-1 (R1.3, s11-design 1.4): the library's publication version, set by the
    // first Bind's freeze, is nonzero and unchanged, with the library table's count and the layer's counts, after a second Bind,
    // Analyze, CheckBound, a Provisional then Final Bind and an edit then a Bind; and it is per compilation (G949 ruling 174, the
    // Middle's MR-3): a second compilation's comes from its own Binding's runs, whatever the first's. Before S1-1 each run's library
    // pass published it anew.
    [Fact]
    public void ASecondBindBindsNoLibraryNode()
    {
        var c = Hello();
        var first = c.Bind();
        Assert.True(first.IsComplete);
        var (version, count, counts) = (HirTables.LibraryVersion(c), c.Library.Kotonoha.Syntax.Count, c.Library.Layer.Part.Counts);
        Assert.NotEqual(0ul, version);
        var second = c.Bind();
        Assert.Equal(first, second);
        Assert.DoesNotContain(c.Binding.Semantics.BoundNodes, x => ReferenceEquals(x.Kotonoha, c.Library.Kotonoha));
        Unchanged();
        c.Analyze();
        Unchanged();
        Assert.True(c.Binding.CheckBound().IsComplete);
        Unchanged();
        c.Binding.Bind(BindingMode.Provisional);
        Assert.True(c.Bind().IsComplete);
        Unchanged();
        c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
        Assert.True(c.Bind().IsComplete);
        Unchanged();
        var other = Hello();
        Assert.True(other.Bind().IsComplete);
        Assert.Equal(version, HirTables.LibraryVersion(other));
        Unchanged();

        void Unchanged()
        {
            Assert.Equal(version, HirTables.LibraryVersion(c));
            Assert.Equal(count, c.Library.Kotonoha.Syntax.Count);
            Assert.Equal(counts, c.Library.Layer.Part.Counts);
        }

        static Compilation Hello()
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
            return c;
        }
    }

    // c4/c15 (PLAN G949 S1-1, B2; s11-design 1.2 "Mode", E2): the library pass runs Final whatever the Bind's mode, so no library fact S1-1
    // freezes is a Provisional pass's. After a Provisional first Bind of Hello the layer's part holds a Final first Bind's counts and
    // obligations, and the Bind's Result composes them with the program part's Provisional counts (c15's declared Provisional
    // difference: before B2 the layer's counts were the Provisional pass's). The library pass's publication is the library module's
    // (LibraryVersion), never the program's, so the program's modules are published after the Provisional Bind (PublishHir) and their
    // MIR is a Final Bind's.
    [Fact]
    public void AProvisionalBindComposesTheLibrarysFinalCounts()
    {
        var (final, provisional) = (Hello(BindingMode.Final), Hello(BindingMode.Provisional));
        var layer = provisional.Library.Layer.Part;
        Assert.Equal(final.Library.Layer.Part.Counts, layer.Counts);
        Assert.Equal(final.Library.Layer.Part.Obligations.Select(static x => (x.Kind, x.Use.SyntaxId, x.Deadline)), layer.Obligations.Select(static x => (x.Kind, x.Use.SyntaxId, x.Deadline)));
        var program = ((Binding.PassPart)typeof(Binding).GetField("programPart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(provisional.Binding)!).Counts;
        var (resolved, unresolved, invalid) = (layer.Counts.ResolvedCount + program.ResolvedCount, layer.Counts.UnresolvedCount + program.UnresolvedCount, layer.Counts.InvalidCount + program.InvalidCount);
        Assert.Equal(new BindingResult(BindingMode.Provisional, resolved, unresolved, invalid), provisional.Binding.Result);
        Assert.Equal(ProgramMir(final), ProgramMir(provisional));

        static Compilation Hello(BindingMode mode)
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
            Assert.Equal(mode, c.Binding.Bind(mode).Mode);
            return c;
        }

        static string ProgramMir(Compilation c)
        {
            var mir = new StringBuilder();
            MirDump.Write(c.ConstructMir(), 0, mir);
            Assert.NotEqual(0, mir.Length);
            return mir.ToString();
        }
    }

    // PLAN G949 S1-r1b (r1b-5; review-s1r1 Re-check 3, RC-a): the import declarations are a part of each pass (PassPart), read after a
    // Bind as one composed list, so the library's imports (Windows.kimi's two kernel32 functions), which the Backend lays out through
    // TyCtxt.Imports, are listed after a first and a second Bind, and from the split after a program pass that declares none. The Tatara
    // plan's B7b6: they are declared where `windows` holds, so windows-x64's library and the one without a target (its selection, F1 (a))
    // list them, and the Linux row's library has no import (every library import lies under `#if windows`, B3).
    [Theory]
    [InlineData("x86_64-pc-windows-msvc", "queryPerformanceCounter,queryPerformanceFrequency")]
    [InlineData("", "queryPerformanceCounter,queryPerformanceFrequency")]
    [InlineData("x86_64-unknown-linux-musl", "")]
    public void TheLibrarysImportsAreListedAfterEachBind(string target, string imports)
    {
        var c = Compilation.CreateForTest();
        Assert.True(target.Length == 0 || c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
        for (var bind = 0; bind < 2; bind++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.Equal(
                imports.Split(',', StringSplitOptions.RemoveEmptyEntries),
                c.Types.Imports.Where(x => ReferenceEquals(x.Declaration.Kotonoha, c.Library.Kotonoha)).Select(static x => x.Name));
        }
    }

    // The Tatara plan's F1 (a), its program half (C-496 C2): in a compilation without a target only the library's Kotonoha reads
    // windows-x64's Conditions; a program source keeps the compilation's own (none), so its Condition Name is unknown (SPEC 19.3) while
    // the library of the same compilation declares Kimi.Windows.
    [Fact]
    public void AProgramSourcesConditionStaysUnknownWithoutATarget()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", "#if windows\n    func a() -> i32 => 1\nfunc b() -> i32 => 2"));
        c.Bind();
        Assert.Equal(
            ["UnknownCompileTimeName_Kd Program.kimi 4+7"],
            TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Path} {x.Span.Start}+{x.Span.Length}"));
        Assert.Equal(
            ["queryPerformanceCounter", "queryPerformanceFrequency"],
            c.Types.Imports.Where(x => ReferenceEquals(x.Declaration.Kotonoha, c.Library.Kotonoha)).Select(static x => x.Name));
    }

    // PLAN G949 S1-r1b (s1r1-design 8.1, v9, at the layer below MIR's holding check: no existing row observes a library slot's keepers,
    // r1b/plan.md 11.3): a library slot's keepers are current after each Bind. A slot's list is found by the parts' one find (the
    // library layer's part first) and is current when its Pass is the RequirementCall of the part that holds it, the requirement
    // counter's value at that part's latest call. On the trial split without the parts the program pass's calls advanced the one
    // counter, and every read of a library slot's keepers after the Bind was stale (357,327 reads over 28 classes). The library keeps
    // 59 slots today (BorrowingIterator's `source` among them); the same slots are current after a second Bind.
    [Fact]
    public void ALibrarySlotsKeepersAreCurrentAfterEachBind()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
        var own = (Binding.PassPart)typeof(Binding).GetField("programPart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var layer = c.Library.Layer.Part;
        var first = -1;
        for (var bind = 0; bind < 2; bind++)
        {
            Assert.True(c.Bind().IsComplete);
            var kept = own.Keepers.Concat(layer.Keepers)
                .Where(x => x.Value.Count != 0 && x.Key.Origin.Binder is { } binder && ReferenceEquals(binder.Kotonoha, c.Library.Kotonoha)).ToArray();
            Assert.NotEmpty(kept);
            foreach (var (slot, list) in kept)
            {
                var holder = Binding.PassPart.Find(Binding.PassPart.Parts(layer.Keepers, own.Keepers), slot, out var found);
                Assert.Same(list, found);
                Assert.Equal(ReferenceEquals(holder, layer.Keepers) ? layer.RequirementCall : own.RequirementCall, list.Pass);
            }

            first = first < 0 ? kept.Length : first;
            Assert.Equal(first, kept.Length);

            // Disjointness (v9): a slot is kept only by its declaration's pass, so Binding's own part holds no library slot and the
            // layer's no program slot (Keep writes the running part without the find).
            Assert.DoesNotContain(own.Keepers.Keys, x => x.Origin.Binder is { } binder && ReferenceEquals(binder.Kotonoha, c.Library.Kotonoha));
            Assert.DoesNotContain(layer.Keepers.Keys, x => x.Origin.Binder is { } binder && !ReferenceEquals(binder.Kotonoha, c.Library.Kotonoha));
        }
    }

    // PLAN G949 S1-r1b (s1r1-design 8.2, v10): the Contract shapes are a part of each pass, and RecursiveContracts reads every pass's
    // shapes after a Bind. No library Contract is recursive today (SPEC 8.3.3: none of the library's 24 Contracts constrains an
    // associated Type of its own to itself), so no program can show a lost library shape: on the trial split without the part, 54
    // walks after the Bind saw no library shape and no answer moved. The unit that declares the first recursive library Contract
    // removes this Fact and adds the RecursiveBase row over it (RecursiveContractTest.AProjectionHasARecursiveBase).
    [Fact]
    public void TheLibraryDeclaresNoRecursiveContract()
    {
        var c = Bound(string.Empty);
        var recursive = (List<BindingSymbol>)typeof(Binding).GetMethod("RecursiveContracts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(c.Binding, null)!;
        Assert.Empty(recursive.Select(static x => x.Name));
    }

    // PLAN G949 S1-r1b (review-s1r1b Part 2, C1; F's ruling 176): rule (a)'s premise (ImplTable). The base keeps one earlier snapshot per
    // identity and per Type list, exact while every base row registers at point 1 (the Self-clause registrar) or 2 (the conditional
    // registrar). The inherited registrar registers after point 2 (point 3, and again after BindConstraints and at ConformancesOf's
    // demand, past every point), so a library struct with a base (SPEC 6.2.2) would give base rows that a pass without the library reads
    // wrongly at step 1 and never admits after its last point. No library struct names a base today: a tripwire, true today.
    [Fact]
    public void TheLibraryDeclaresNoStructWithABase()
    {
        var bases = KotoTree.Walk(Bound(string.Empty).Library.Kotonoha.RootKoto).OfType<StructKoto>().Where(static x => x.BaseClause is not null).Select(static x => Line(x)).ToArray();
        var build = "A library struct names a base, so rule (a)'s base rows of the inherited registrar register past point 2: before it " +
            "lands, ImplTable keeps per-point prefixes (an identity's paths, a Type's list and the composed lists' prefix at each point, " +
            "the inherited registrar's points admitted) in place of the one point-1 snapshot (PointOnePaths, pointOneLists): ";
        Assert.True(bases.Length == 0, build + string.Join(", ", bases));
    }

    // R7-N5 B6b's second premise (DECISIONS BU-220 as B6b amends it; the first is TheLibraryDeclaresNoParameterDefault): StructuralCompletion's
    // query memos are working-state families of the composed node's module, which the frozen library refuses, so no program query may
    // compose a library node. The guess rule, which MIR also asks after the Bind at a require's failure body, answers none at a frozen
    // library node (the library's pass is complete and recovered nothing), so a program whose pass is incomplete composes none.
    [Fact]
    public void TheGuessRuleAnswersNoneAtAFrozenNode()
    {
        var c = CompilationTestHelper.Parse("func f(flag: bool) -> i32\n    if flag => return 1\n    missing()\n", "Hello.kimi");
        Assert.False(c.Bind().IsComplete);
        var requires = KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<RequireKoto>().ToArray();
        Assert.NotEmpty(requires);
        Assert.All(requires, x => Assert.True(c.Binding.GuessedCompletionNodes(x.ElseBody, x.ElseBody).IsEmpty, Line(x)));
    }

    // PLAN G949 S1-r1b (commit 9's premise; review-s1r1b Part 3, F's ruling 179): the documents' unnamed aliases are a per-pass map
    // (PassPart.AliasesByDocument). The library's sources declare no unnamed alias; its one is its configured `Kimi` alias (F's ruling
    // N1-13 C2), an alias of the library pass in its configuration document, so the layer's map holds that document's entry alone and a
    // program pass, whose map is its own part's, neither reads nor prunes it (each pass keeps and prunes its own documents' entries).
    [Fact]
    public void TheLibrarysOnlyUnnamedAliasIsItsConfiguredOne()
    {
        var c = Bound(string.Empty);
        Assert.Empty(KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<AliasKoto>().Where(static x => x.Name is null).Select(static x => Line(x)));
        Assert.Equal("compiler://configuration#Alias[Kimi]", Assert.Single(c.Library.Layer.Part.AliasesByDocument).Key.Path);
    }

    // The empty program's synthesized library nodes after its first Analyze, by id (c17's reference).
    private static readonly Lazy<string[]> EmptyProgramSynthesized = new(() => SynthesizedLines(Analyzed(string.Empty)));

    // c17 (RC1): the library steps number every library node a later phase reads, so the library table after the first Analyze and a
    // build of every library body (PLAN G951 U2: Analyze builds the library bodies the program reaches) is the one after the first Bind,
    // and its synthesized nodes are the empty program's, id by id, whatever the program (R8-M32's Temporary 1 removed). Until G949 S1-e the MIR builder's CallPlan.Accessor created and numbered 7 library accessors' execution functions
    // (Binding.AccessorFunction) after Bind, 16,120 against 16,134, and a program's uses ordered the ids (Milestones 27 and 30). c2's
    // programs and every milestone here; the library-layer rows' whole corpus (c16's) in the corpus row, valid or not: a check analyzes
    // whatever Binding found (SPEC 23.1.2).
    [Theory]
    [MemberData(nameof(LayerPrograms))]
    public void TheLibraryPassNumbersEveryLibraryNode(string program) => NumberedByTheLibraryPass(program);

    [Theory(Explicit = true)] // Run with the library-layer corpus row (PLAN G949 S1-0b).
    [MemberData(nameof(LayerCorpus))]
    public void TheLibraryPassNumbersEveryLibraryNodeOfTheCorpus(string program) => NumberedByTheLibraryPass(program);

    // G949 S1-e: after a Final Bind every library function's SignatureVerdict is Proven, kept as Done (358 today), so a program's use
    // computes none and BU-105's Frozen line needs no clause for a library verdict that is not Proven. The domain is Binding's symbols,
    // not the step's walk.
    [Fact]
    public void EveryLibrarySignatureVerdictIsProven()
    {
        var c = Bound(string.Empty);
        Assert.Equal(BindingMode.Final, c.Binding.Result.Mode);
        var pass = (ulong)typeof(Binding).GetField("storageVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        var functions = DeclaredSymbols(c)
            .Where(x => x.Kind == BindingSymbolKind.Function && ReferenceEquals(x.Declaration.CodeContext?.Kotonoha, c.Library.Kotonoha)).ToArray();
        Assert.NotEmpty(functions);
        Assert.Empty(functions.Where(x => x.QueryState(DeclQueryKind.SignatureVerdict, pass) != DeclState.Done).Select(static x => Line(x.Declaration)));
    }

    // PLAN G949 S1-r1a (review-r1a B2): CollectCallableEffectUses tests every pass's bounded Callable clauses before it collects a pass's
    // uses, since a use may select a library callee's clause (SPEC 8.11.4). No program can show that read until the library declares
    // such a clause: today every clause a use selects is the program's, in the pass's own part. The unit whose library declares the
    // first one removes this Fact and adds the cross-part row: a program stating no bounded clause of its own calls that library
    // function, and its use gets its effect obligation (S1-r1b's c15 extension, ruling 65, once the parts split).
    [Fact]
    public void TheLibraryDeclaresNoBoundedCallableClause()
        => Assert.Empty(KotoTree.Walk(Bound(string.Empty).Library.Kotonoha.RootKoto).OfType<IsKoto>().Where(static x => x.EffectBounds.Count != 0).Select(static x => Line(x)));

    // PLAN G949 S1-r1b (v7; s1r1-design M6): the library binds in one run. The empty program's Final Bind leaves no known base and no
    // second run, so a restart's second run of the library pass (secondRun true) repeats the library's one run (v2's r1b clause:
    // TheLibraryLayerAfterARestartIsTheEmptyPrograms). A precondition, true today; a library base clause that names a projection fails
    // here first.
    [Fact]
    public void TheLibraryBindsInOneRun()
    {
        var c = Bound(string.Empty);
        Assert.False((bool)typeof(Binding).GetField("secondRun", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!);
        Assert.False(c.Library.Kotonoha.Syntax.Hir.Families.KnownBases.Enumerate().MoveNext()); // R7-N5 B5a: the kept family's rows.
        Assert.False(c.Kotonoha.Syntax.Hir.Families.KnownBases.Enumerate().MoveNext());
    }

    // PLAN G949 S1-r1b (r1b plan section 1, step 0; s1r1-design 8.10's first branch): the library declares no shape contract and no
    // library pair is a parameter-shape conflict, so the shape lists hold no library row a program pass's reset drops, and they stay per
    // pass (ResetParameterShapes). A tripwire, true today: the unit that gives a library function its first shape contract fails here
    // and makes 8.10's keyed PassPart store.
    [Fact]
    public void TheLibraryDeclaresNoShapeContract()
    {
        var c = Bound(string.Empty);
        var heads = 0;
        foreach (var head in c.Library.Kotonoha.Syntax.Hir.Families.ShapeContractHeads.Enumerate(c.Binding))
        {
            heads += head == 0 ? 0 : 1; // A row is its first shape contract's index + 1.
        }

        Assert.Equal(0, heads);
        Assert.Empty((System.Collections.ICollection)typeof(Binding).GetField("shapeContracts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!);
        // R7-E2 E2-3b: a conflict is a problem in the pass's report (its stores are gone), so the bound library holds none.
        var report = (BindingReport)typeof(Binding).GetField("report", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(c.Binding)!;
        Assert.DoesNotContain(Enumerable.Range(0, report.Count), i => report.Problem(i).Code == Kimi.DiagnosticCode.ParameterShapeMismatch_Kd);
    }

    // G949 D2b-d's d12 (N5-3): the library's explicit specializations (SPEC 8.13: tryResolve's, writer's and format's) are their
    // originals' after a second Bind of a program that runs one (an i128 formatted), as MIR's Implementation reads them through
    // Specializations. S1-1, which binds the library once, must keep them where a program pass reads them (else a program call of a
    // specialized library function would run the generic body: wrong code).
    [Fact]
    public void TheLibrarySpecializationsAreTheirOriginalsAfterASecondBind()
    {
        var c = CompilationTestHelper.Parse("let x: i128 = 5\nConsole.writeLine(\"\\(x)\")\n");
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        var specializations = KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.IsSpecialization).ToArray();
        Assert.Equal(7, specializations.Length);
        foreach (var specialization in specializations)
        {
            var original = c.Binding.GetSpecializationOriginal(specialization);
            Assert.NotNull(original);
            BindingSymbol? symbol = null;
            foreach (var declared in c.Binding.Semantics.Declarations)
            {
                symbol = ReferenceEquals(declared.Declaration, original) ? declared : symbol;
            }

            Assert.NotNull(symbol);
            Assert.Contains(specialization, c.Binding.Specializations(symbol) ?? []);
        }
    }

    // Binding's kept symbols (DECISIONS BU-220: the `symbols` family, read through SemanticQuery.Declarations).
    private static List<BindingSymbol> DeclaredSymbols(Compilation c)
    {
        var symbols = new List<BindingSymbol>();
        foreach (var symbol in c.Binding.Semantics.Declarations)
        {
            symbols.Add(symbol);
        }

        return symbols;
    }

    private static Compilation Analyzed(string program)
    {
        var c = Bound(program);
        c.Analyze();
        return c;
    }

    private static void NumberedByTheLibraryPass(string program)
    {
        var c = Bound(program);
        var bound = c.Library.Kotonoha.Syntax.Count;
        c.Analyze();
        _ = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1); // Every library body: Analyze builds those the program reaches (PLAN G951 U2).
        Assert.Equal(bound, c.Library.Kotonoha.Syntax.Count);
        Assert.Equal(EmptyProgramSynthesized.Value, SynthesizedLines(c));
    }

    // The library table's synthesized nodes in id order, each by its printer line.
    private static string[] SynthesizedLines(Compilation c)
    {
        var table = c.Library.Kotonoha.Syntax;
        var lines = new string[table.Count - table.ParsedCount];
        for (var i = 0; i < lines.Length; i++)
        {
            var printer = new Printer(false);
            printer.Attach(c);
            printer.Node(table.Nodes[table.ParsedCount + 1 + i]!);
            lines[i] = $"{table.ParsedCount + 1 + i} {printer.ToString().TrimEnd('\n')}";
        }

        return lines;
    }
}
