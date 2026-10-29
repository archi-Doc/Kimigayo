// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The C1-F4 view (2a0-D §2-§4; docs/dev/DECISIONS.md R4-M94, R4-M101): each Origin relation a body checks is one row of the body
/// Binding formed it in (its formation context, never its Use's syntax), with its ends resolved at the use and classed by SPEC 15.6.5's
/// precedence relative to that body; a declaration's rows are in no slice; a constructor's slice ends with its layer's Field-initializer
/// rows, judged with the layer's premises; the body's fixed-fixed oracle agrees with Binding's proof.</summary>
public sealed partial class OriginRowsTest
{
    private const string Main = "public func main() => ()\n";

    private const string Pick = "func pick(x: ref/i32 during s, y: ref/i32 during s) -> i32 => x@follow\n";

    // A default whose Type is well formed only when `b outlives a` (PLAN G277 (1)).
    private const string Put2 = "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a> = .None)\n" +
        "    slot@follow = v\n";

    // Pick in a group, for callers a top-level function does not reach (a group's or struct's member).
    private const string Util = "group Util\n    public func pick(x: ref/i32 during s, y: ref/i32 during s) -> i32 => x@follow\n";

    // A Closure storing a Borrow of an environment binding through a captured exclusive reference (2a0-D's rv/env-store), under an
    // Exclusive receiver and, consuming another capture, under `owner/Self`.
    private const string EnvStore = "func store(target: uniq/(ref/i32 during t), value: ref/i32 during t) -> ()\n    target@follow = value\n" +
        "func g(x: i32, target: uniq/(ref/i32 during t)) -> ()\n    var f = func [x, target] () => store(target, value: x@ref)\n    f()\n";

    private const string EnvStoreConsuming = "func store(target: uniq/(ref/i32 during t), value: ref/i32 during t) -> ()\n    target@follow = value\n" +
        "func consume(s: string) => ()\nfunc g(x: i32, target: uniq/(ref/i32 during t), s: string) -> ()\n    let f = func [x, target, s@move] ()\n" +
        "        consume(s@move)\n        store(target, value: x@ref)\n    f@move()\n";

    // A struct whose Field initializer forms rows, with two constructors (2a0-D F2; the review's f2-init-two-ctors).
    private const string FieldInit = Util + "struct S\n    public var n: i32 = Util.pick(3@ref, 4@ref)\n    public init() => ()\n    public init(k: i32) => self.n = k\n";

    // A Field initializer's local annotation needing `b outlives a`, which only the constructor's parameter implies (the review's probes).
    private const string InitPremise = "struct Pair {a, b}\n    public let p: ref/i32 during a\n    public var n: i32 = if true\n" +
        "        let t: Option<ref/(ref/i32 during b) during a> = Option.None\n        yield 0\n    else => 0\n" +
        "    public init(p: ref/i32 during a, w: ref/(ref/i32 during b) during a)\n        self.p = p\n";

    // A function iterating a Slice: its `for` is the function's.
    private const string Total = "func total(values: Slice<i32>) -> i32\n    var sum = 0\n    for v in values\n        sum = sum + v@follow\n    return sum\n";

    // A getter iterating a borrowed sequence (2a0-D's cf/p/accessor-for): its `for` is the accessor's.
    private const string Getter = "struct Wrap {s}\n    public let values: Slice<i32> during s\n    public init(values: Slice<i32> during s) => self.values = values\n" +
        "    public computed first: i32\n        get() -> i32\n            var sum = 0\n            for v in self.values\n                sum = sum + v@follow\n            return sum\n";

    // SPEC 15.6.1: in the library, the milestones, the goldens and the accepting SPEC variants, each Origin row lies once in the slice of the
    // body Binding formed it in; Binding's declaration judge consumes a declaration's rows (DECISIONS BU-26), so none remains; no broken
    // contract is found; no row names another declaration's universal atom (+cen: I-1's shape, PLAN G195, is the only one, and it occurs in
    // none of them).
    [Fact]
    public void EachCensusRowLiesOnceInTheSliceOfItsBody()
    {
        foreach (var (name, c, library) in Census())
        {
            var program = c.BuildMir();
            foreach (var body in program.Bodies)
            {
                var slice = c.Types.OriginRows(body.Key.Body);
                foreach (var atom in slice.Atoms)
                {
                    var foreign = atom is { Kind: OriginAtomKind.Unclassed, Atom.Kind: OriginKind.Parameter or OriginKind.Input };
                    Assert.False(foreign, $"{name}: {atom.Atom.Name} of another declaration");
                }
            }

            foreach (var o in Rows(c, library))
            {
                Assert.True(o.Body.IsBody, $"{name}: the declaration row at {o.Use} remains");
                var slice = c.Types.OriginRows(o.Body);
                var longer = Atom(c, o.Body, o.Longer!, o.Use);
                var shorter = Atom(c, o.Body, o.Shorter!, o.Use);
                var count = slice.Rows.Take(slice.LayerStart).Count(x => x.Site == SourceRef.Of(o.Use) && slice.Atoms[x.Longer] == longer &&
                    slice.Atoms[x.Shorter] == shorter && x.Equality == o.Equality && ReferenceEquals(x.Destination, o.Type));
                Assert.True(count == (ReferenceEquals(longer.Atom, shorter.Atom) ? 0 : 1), $"{name}: {count} rows at {o.Use}");
            }

            Assert.Empty(c.Types.OriginFindings);

            // PLAN G215: every layer with rows has a constructor whose slice includes them.
            foreach (var layer in Rows(c, library).Select(static x => x.Body).Where(static x => x.Definition is { Kind: BindingSymbolKind.Type }).Distinct())
            {
                Assert.Contains(program.Bodies.ToArray(), x => AdtDef.IsConstructor(x.Key.Decl) && ReferenceEquals(x.Key.Decl!.Scope.Owner.SymbolOf(), layer.Definition));
            }
        }
    }

    // DECISIONS R4-M101 E1, as an oracle independent of the formation context: each body row's owner and each formed atom's body is the body
    // containing its Use or binder, but for the formation exceptions (a Closure's header is its evaluating body's, a Property's initializer and
    // its fit are a static's own body or the layer's, a default is its template, a signature is no body).
    [Fact]
    public void FormationAgreesWithContainmentOverTheCensus()
    {
        foreach (var (name, c, library) in Census())
        {
            foreach (var o in Rows(c, library))
            {
                Assert.True(Contains(o.Body, o.Use), $"{name}: the row at {o.Use} is {Show(o.Body)}");
                for (var i = 0; o.Body.IsBody && i < 2; i++)
                {
                    var end = i == 0 ? o.Longer! : o.Shorter!;
                    foreach (var atom in end.Kind == OriginKind.Intersection ? end.Operands : [end])
                    {
                        if (atom is { Kind: OriginKind.Projection or OriginKind.Inference or OriginKind.Anchor, Binder: { } binder } &&
                            binder is not (FunctionKoto or PropertyAccessorKoto or PropertyKoto))
                        {
                            Assert.True(Contains(atom.Body, binder), $"{name}: {atom.Name} at {o.Use} is {Show(atom.Body)}");
                        }
                    }
                }
            }
        }
    }

    // DECISIONS R4-M101 E2, E3 and BU-26: rows formed while Binding binds a declaration (an instance Field's or computed Property's declared
    // Type, a requirement's or an override's signature) are formed with no body and consumed by Binding's declaration judge, so no row whose
    // Use lies in a declaration part remains, and none is in a slice. The second form is PLAN G214's: its header and getter result records.
    [Theory]
    [InlineData("struct Pair {a, b}\n    public let x: ref/(ref/i32 during b) during a\n    public init(x: ref/(ref/i32 during b) during a) => self.x = x\n", false)]
    [InlineData("struct Pair {a, b}\n    public let p: ref/i32 during a\n    public init(p: ref/i32 during a) => self.p = p\n    public computed y: Option<ref/(ref/i32 during b) during a>\n        get() -> Option<ref/(ref/i32 during b) during a> => Option.None\n", false)]
    [InlineData("contract C\n    func f(x: ref/(ref/i32 during b) during a) -> i32\n", true)]
    [InlineData("open struct Base\n    public init() => ()\n    public virtual func f(self: objref/Self, x: ref/(ref/i32 during b) during a) -> i32 => 0\nstruct Derived : Base\n    public init() : base() => ()\n    override func f(self: objref/Self, x: ref/(ref/i32 during b) during a) -> i32 => 1\n", true)]
    public void ADeclarationsRowsAreConsumedByBinding(string source, bool complete)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.Equal(complete, c.Binding.Result.IsComplete);
        Assert.DoesNotContain(Rows(c, false), static x => Contains(default, x.Use));
        Assert.Empty(c.Types.OriginFindings);
    }

    // DECISIONS R4-M101 E3 for a specialization: its signature's well-formedness rows are a declaration's, formed with no body, so Binding's
    // declaration judge proves and consumes them (BU-26).
    [Fact]
    public void ASpecializationsSignatureRowsAreADeclarations()
    {
        var c = MirBuildTest.Compile("func f<T>(x: ref/(ref/T during b) during a) -> i32 => 0\n" +
            "specialize func f<i32>(x: ref/(ref/i32 during b) during a) -> i32 => 1\n" + Main);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.DoesNotContain(Rows(c, false), static x => Within(x.Use, static y => y is FunctionKoto { IsSpecialization: true }));
        Assert.Empty(c.Types.OriginFindings);
    }

    // DECISIONS R4-M101 E1 (2a0-D D3): a row is the body's Binding binds it in: a function's, a Closure's body, a Closure literal's fit in
    // its evaluating body, a getter's, a default as its template, a static initializer, a Field initializer as its layer, a module's
    // top-level statements, a default's well-formedness at its call in the calling body (DECISIONS BU-51); a parentless Use's below.
    [Theory]
    [InlineData(EnvStoreConsuming, "x@ref", "Closure")]
    [InlineData(EnvStore, "func [x, target]() => store(target, value: x@ref)", "Fn:g")]
    [InlineData(Pick + "func g(a: i32 = pick(3@ref, 4@ref)) -> i32 => a\n", "3@ref", "Default:g:0")]
    [InlineData(Pick + "func h(n: i32, f: () -> i32 = func [] () => pick(7@ref, 8@ref)) -> i32 => f()\n", "7@ref", "Closure")]
    [InlineData(Util + "group G\n    public let n: i32 = Util.pick(3@ref, 4@ref)\n", "3@ref", "Static:n")]
    [InlineData(FieldInit, "3@ref", "Layer:S")]
    [InlineData(Pick + "let v = pick(3@ref, 4@ref)\n", "3@ref", "TopLevel")]
    [InlineData(Put2 + "func outer() -> i32\n    let long = 1\n    var keep = long@ref\n    if true\n        let short = 2\n        put2(keep@uniq, short@ref)\n    return keep@follow\n", "put2(keep@uniq, short@ref)", "Fn:outer")]
    public void ARowIsTheBodyBindingFormsItIn(string source, string site, string expected)
    {
        var c = CompilationTestHelper.Parse(source + (source.Contains("\nlet v", StringComparison.Ordinal) ? string.Empty : Main), "Program.kimi");
        c.Bind();
        var rows = Rows(c, false).Where(x => First(x.Use) == site).ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, x => Assert.Equal(expected, Describe(x.Body)));
    }

    // DECISIONS R4-M101 E1 for a parentless Use, a user loop's `$for.iterator`: in a getter it is the accessor's, in a function the
    // function's. No program forms a row there since G216 (a)'s one operand set (DECISIONS R4-M155; the planning session's decision of
    // 2026-10-30): Iterator.next's receiver fit relates the iterator's Borrow to itself, which the exact solution entails. So the rule is
    // read on a constructed input at that Use: the Borrow is a Finite atom of the body that forms it and of no other program body (a MIR
    // body: the accessor's is its get function's).
    [Theory]
    [InlineData(Getter, "Fn:first.Get")]
    [InlineData(Total, "Fn:total")]
    public void AParentlessForUseIsTheBodyBindingFormsItIn(string source, string expected)
    {
        var c = MirBuildTest.Compile(source + Main);
        var (borrow, use) = IteratorBorrow(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>().Single());
        var owners = Bodies(c).Where(x => InProgram(x.Key.Decl?.Declaration ?? c.Kotonoha.RootKoto) && Atom(c, x.Key.Body, borrow, use).Kind == OriginAtomKind.Finite);
        Assert.Equal([expected], owners.Select(static x => Describe(x.Key.Body)));
    }

    // The library's `for` in Slice.contains has its parentless Use in contains' body, read as above on a constructed input (no row forms
    // there since R4-M155; the planning session's decision of 2026-10-30).
    [Fact]
    public void TheLibrarysForRowIsItsFunctions()
    {
        var c = SemanticSnapshotTest.Bind("library").Compilation;
        var contains = Bodies(c).First(static x => Describe(x.Key.Body) == "Fn:contains" && KotoTree.Walk(x.Key.Decl!.Declaration).OfType<ForKoto>().Any());
        var (borrow, use) = IteratorBorrow(KotoTree.Walk(contains.Key.Decl!.Declaration).OfType<ForKoto>().Single());
        Assert.Equal(OriginAtomKind.Finite, Atom(c, contains.Key.Body, borrow, use).Kind);
    }

    // DECISIONS R4-M101 E4 (Q1 (a)): a constructor's slice is its own rows, then its layer's, the same in each constructor; the layer's rows
    // are judged with the Type's public premises, as Binding judges them today, even where the constructor's premises would prove them.
    [Fact]
    public void EachConstructorEndsWithItsLayersRowsJudgedWithTheLayersPremises()
    {
        var c = MirBuildTest.Compile(FieldInit + Main);
        var constructors = Bodies(c).Where(IsProgramConstructor).Select(x => c.Types.OriginRows(x.Key.Body)).ToList();
        Assert.Equal(2, constructors.Count);
        Assert.All(constructors, x => Assert.Equal("Layer:S", Describe(x.Layer)));
        Assert.All(constructors, x => Assert.Equal(2, x.Rows.Count - x.LayerStart));
        Assert.All(constructors, x => Assert.All(x.Rows.Skip(x.LayerStart), y => Assert.Equal(OriginAtomKind.Finite, x.Atoms[y.Longer].Kind)));
        Assert.Empty(c.Types.OriginFindings);

        c = MirBuildTest.Compile(InitPremise + Main);
        var init = Bodies(c).Single(IsProgramConstructor);
        var slice = c.Types.OriginRows(init.Key.Body);
        var row = Assert.Single(slice.Rows.Skip(slice.LayerStart));
        var (longer, shorter) = (slice.Atoms[row.Longer], slice.Atoms[row.Shorter]);
        Assert.Equal((OriginAtomKind.Fixed, OriginAtomKind.Fixed), (longer.Kind, shorter.Kind));
        Assert.False(Entails(c, slice.Layer, longer.Atom, shorter.Atom, row.CaseMask));
        Assert.True(Entails(c, slice.Body, longer.Atom, shorter.Atom, row.CaseMask));
    }

    // DECISIONS R4-M101 E4 (BU's B2): the body key decides the composition: the template of a constructor's default holds only its own rows.
    [Fact]
    public void AConstructorsDefaultTemplateHoldsOnlyItsOwnRows()
    {
        var c = MirBuildTest.Compile(FieldInit[..FieldInit.IndexOf("    public init()", StringComparison.Ordinal)] +
            "    public init(k: i32 = Util.pick(5@ref, 6@ref)) => self.n = k\n" + Main);
        var slices = Bodies(c).Where(IsProgramConstructor).Select(x => c.Types.OriginRows(x.Key.Body)).ToList();
        var template = Assert.Single(slices, static x => x.Body.Index == 0);
        Assert.Equal((2, 2, false), (template.Rows.Count, template.LayerStart, template.Layer.IsBody));
        var init = Assert.Single(slices, static x => x.Body.Index < 0);
        Assert.Equal(("Layer:S", 2), (Describe(init.Layer), init.Rows.Count - init.LayerStart));
        Assert.Empty(c.Types.OriginFindings);
    }

    // The recorded limit of DECISIONS R4-M101 E4 (PLAN, STATUS): a layer whose struct has no constructor (its omitted base clause
    // selects none) keeps its Field-initializer rows, which no body's slice includes, so no MIR analysis judges them.
    [Fact]
    public void AConstructorlessLayersRowsLieInNoBodysSlice()
    {
        var c = MirBuildTest.Compile(Util + "open struct Base\n    protected init(a: i32 = 1) => ()\n    protected init(b: string = \"x\") => ()\n" +
            "struct Leaf: Base\n    public var n: i32 = Util.pick(3@ref, 4@ref)\n" + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        var leaf = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(static x => x.Name == "Leaf").SymbolOf()!;
        Assert.Equal(2, c.Types.OriginRows(new ExecutableBody(leaf)).Rows.Count);
        Assert.DoesNotContain(Bodies(c), x => AdtDef.IsConstructor(x.Key.Decl) && ReferenceEquals(x.Key.Decl!.Scope.Owner.SymbolOf(), leaf));
    }

    // 2a0-D D2: a row's CaseMask numbers the body's pair binders (MirBody.Binders), renumbered from those in scope of its Use.
    [Fact]
    public void ACaseMaskNumbersTheBodysPairBinders()
    {
        var c = MirDumpTest.Compile("adaptations");
        var masked = 0;
        foreach (var body in c.BuildMir().Bodies)
        {
            var slice = c.Types.OriginRows(body.Key.Body);
            foreach (var o in Rows(c, false).Where(x => x.Body == slice.Body && x.Condition != 0))
            {
                var useBinders = new List<PairBinder>();
                c.Binding.PairBinders(o.Use, useBinders);
                var expected = 0UL;
                for (var bit = 0; bit < useBinders.Count; bit++)
                {
                    if ((o.Condition & (1UL << bit)) != 0)
                    {
                        expected |= 1UL << body.Binders.FindIndex(x => ReferenceEquals(x.Target, useBinders[bit].Target));
                    }
                }

                Assert.Contains(slice.Rows, x => x.Site == SourceRef.Of(o.Use) && x.CaseMask == expected);
                masked++;
            }
        }

        Assert.NotEqual(0, masked);
    }

    // 2a0-D D4: an Origin a local's annotation omits is pending while its initializer is bound; it reads as its resolution at the uses
    // in that initializer, so the bound the initializer recorded becomes a self-loop, which forms no row, and resolves to the borrowed
    // local's atom. Each local's pending Origin is its own (two locals of one Type keep two keys, each resolved to its own initializer's).
    [Fact]
    public void AnInitializerAliasResolvesToTheBorrowedLocalsAtom()
    {
        var c = MirBuildTest.Compile("struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n" +
            "func g(p: ref/i32) -> i32\n    let local = 3\n    let h: H = H.init(local@ref)\n    let k: H = H.init(p)\n    return h.item@follow + k.item@follow\n" + Main);
        var aliases = Rows(c, false).Where(static x => First(x.Use).StartsWith("H.init(", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, aliases.Count);
        Assert.NotSame(aliases[0].Shorter, aliases[1].Shorter);
        foreach (var alias in aliases)
        {
            var longer = Atom(c, alias.Body, alias.Longer!, alias.Use);
            var shorter = Atom(c, alias.Body, alias.Shorter!, alias.Use);
            Assert.Equal(OriginKind.Inference, alias.Shorter!.Kind);
            Assert.Same(longer.Atom, shorter.Atom);
            Assert.DoesNotContain(c.Types.OriginRows(alias.Body).Rows, x => x.Site == SourceRef.Of(alias.Use));
        }

        Assert.Equal(OriginAtomKind.Finite, Atom(c, aliases[0].Body, aliases[0].Shorter!, aliases[0].Use).Kind);
        Assert.Equal(OriginAtomKind.Fixed, Atom(c, aliases[1].Body, aliases[1].Shorter!, aliases[1].Use).Kind);
    }

    // 2a0-D D5 (T1): no row comes from a Constraint proof: a Type's own clause forms rows only at a local annotation of that Type, never at
    // a use its Constraint check proves (CheckTypeOriginRelations).
    [Fact]
    public void ATypesClauseFormsRowsOnlyAtLocalAnnotations()
    {
        var c = MirBuildTest.Compile("struct S {a, b}\n    origin a outlives b\n    public let x: ref/i32 during a\n    public let y: ref/i32 during b\n" +
            "    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\n" +
            "func keep<T>(value: T) -> i32 => 0\nfunc f(p: ref/i32, q: ref/i32) -> i32\n    origin p outlives q\n    let s: S{w} = S.init(p, q)\n" +
            "        origin w.a == p\n    return keep(s@move)\n" + Main);
        var clauses = Rows(c, false).Where(static x => x.Body.IsBody && x.Clause is { } clause && Within(clause, static y => y is StructKoto)).ToList();
        Assert.NotEmpty(clauses);
        Assert.All(clauses, x => Assert.True(Within(x.Use, static y => y.Parent is FieldKoto local && ReferenceEquals(local.TypeKoto, y)), $"{x.Use}"));
    }

    // 2a0-D §4 (D8): the body's oracle, extracted once at its root, agrees with Binding's per-request proof at the use on every fixed-fixed
    // row of the census, per Semantics case; a layer's rows with the layer's oracle.
    [Fact]
    public void TheOracleAgreesWithBindingOnEveryFixedRow()
    {
        var compared = 0;
        foreach (var (name, c, library) in Census().Append(("layer", MirBuildTest.Compile(InitPremise + Main), false)))
        {
            foreach (var o in Rows(c, library).Where(static x => x.Body.IsBody))
            {
                var slice = c.Types.OriginRows(o.Body);
                var (longer, shorter) = (Atom(c, o.Body, o.Longer!, o.Use), Atom(c, o.Body, o.Shorter!, o.Use));
                if (longer.Kind != OriginAtomKind.Fixed || shorter.Kind != OriginAtomKind.Fixed || ReferenceEquals(longer.Atom, shorter.Atom))
                {
                    continue;
                }

                var row = slice.Rows.First(x => x.Site == SourceRef.Of(o.Use) && slice.Atoms[x.Longer] == longer && slice.Atoms[x.Shorter] == shorter);
                var oracle = Entails(c, o.Body, longer.Atom, shorter.Atom, row.CaseMask) &&
                    (!o.Equality || Entails(c, o.Body, shorter.Atom, longer.Atom, row.CaseMask));
                Assert.True(oracle == c.Binding.IsVerifiedOriginObligation(o), $"{name}: {o.Use} {longer.Atom.Name} >= {shorter.Atom.Name}");
                compared++;
            }
        }

        Assert.NotEqual(0, compared);
    }

    // SPEC 7.2.3: a default sees only the parameters before it, also from a Closure inside it (the formation context, DECISIONS R4-M101 E1).
    [Theory]
    [InlineData("func f(a: i32 = b, b: i32 = 1) -> i32 => a\n", false)]
    [InlineData("func f(g: () -> i32 = func [] () => b, b: i32 = 1) -> i32 => g()\n", false)]
    [InlineData("func f(a: i32, b: i32 = a) -> i32 => b\n", true)]
    [InlineData("func f(a: i32, g: () -> i32 = func [a] () => a) -> i32 => g()\n", true)]
    public void ADefaultSeesOnlyTheParametersBeforeIt(string source, bool complete)
    {
        var c = MirBuildTest.Compile(source + Main);
        Assert.Equal(complete, c.Binding.Result.IsComplete);
    }

    // Immutable after Bind and pooled: a warm rebind and the view's reads (slices, atoms, an oracle) allocate nothing, and the rebind
    // stamps each row and atom with the same body.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindAndTheViewsReadsAllocateNothing()
    {
        var c = MirBuildTest.Compile(EnvStore + FieldInit + Pick + "func h(a: i32 = pick(3@ref, 4@ref)) -> i32 => a\n" + Main);
        var bodies = c.BuildMir().Bodies.ToArray().Select(static x => x.Key.Body).ToArray();
        var (counts, rowStamps, atomStamps) = (Read(), RowStamps(), AtomStamps());
        Assert.Equal(counts, Read());
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            _ = Read();
        }));
        Assert.Equal(counts, Read());
        Assert.Equal(rowStamps, RowStamps());

        // The baseline's tables hold the atoms MirOrigins appended during that pass's BuildMir (the view's contract: appended atoms are
        // per pass), so the new pass is read by the same MIR reader before the whole tables are compared.
        _ = c.BuildMir();
        Assert.Equal(atomStamps, AtomStamps());

        ExecutableBody[] RowStamps() => [.. Rows(c, false).Select(static x => x.Body)];

        (BoundOrigin Atom, ExecutableBody Body, OriginAtomKind Kind)[] AtomStamps()
            => [.. bodies.SelectMany(x => c.Types.OriginRows(x).Atoms).Select(static x => (x.Atom, x.Atom.Body, x.Kind))];

        int Read()
        {
            var rows = 0;
            foreach (var body in bodies)
            {
                var slice = c.Types.OriginRows(body);
                rows += slice.Rows.Count;
                for (var i = 0; i < slice.Rows.Count; i++)
                {
                    var row = slice.Rows[i];
                    using var oracle = c.Types.UniversalRelations(i < slice.LayerStart ? body : slice.Layer);
                    oracle.Close(slice.Atoms[row.Shorter].Atom, row.CaseMask);
                    rows += oracle.Outlives(slice.Atoms[row.Longer].Atom) ? 1 : 0;
                }
            }

            return rows;
        }
    }

    // A warm rebind of nested signatures' headers (PLAN G216's forms and a Function Type annotation, each called or not) allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindOfNestedSignatureHeadersAllocatesNothing()
    {
        var c = MirBuildTest.Compile(HeaderOnly + HeaderCalled + FunctionTypeCalled + Main);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // The census sets as MirCensusTest loads them (R4-M12): the library, the milestones, the goldens and the accepting SPEC variants.
    private static IEnumerable<(string Name, Compilation Compilation, bool Library)> Census()
    {
        foreach (var name in SemanticSnapshotTest.CaseNames().Where(static x => x == "library" || x.StartsWith("milestone/", StringComparison.Ordinal)))
        {
            yield return (name, SemanticSnapshotTest.Bind(name).Compilation, name == "library");
        }

        foreach (var program in MirDumpTest.Programs)
        {
            yield return ("golden/" + program.Data, MirDumpTest.Compile(program.Data), false);
        }

        foreach (var example in DiagnosticCorpus.SpecCases)
        {
            for (var i = 0; i < example.Variants.Length; i++)
            {
                if (example.Variants[i].Expected.Length == 0)
                {
                    yield return ($"spec/{example.Name}/{i}", VerifiedSpecExampleTest.Bind(DiagnosticCorpus.Source(example, example.Variants[i])), false);
                }
            }
        }
    }

    // The Origin obligations of the body-origin deadline whose Use is in the program, or in the library for the library.
    private static IEnumerable<BindingObligation> Rows(Compilation c, bool library)
        => c.Binding.Obligations.Where(x => x is { Kind: BindingObligationKind.OriginOutlives, Deadline: BindingDeadline.BodyOrigins } &&
            x is { Longer: not null, Shorter: not null } &&
            x.Use.CodeContext.SourceDocument?.Path?.StartsWith("compiler://", StringComparison.Ordinal) == library);

    // A MIR body's key in the view's form: a default template's index, or a module's ordinal for top-level statements.
    // An atom's entry in a body's table, read through the view at a syntax site (the view takes MIR's SourceRef and returns the index).
    private static OriginAtomEntry Atom(Compilation c, ExecutableBody body, BoundOrigin origin, Koto site)
        => c.Types.OriginRows(body).Atoms[c.Types.OriginAtom(body, origin, SourceRef.Of(site))];

    // A row's Site as its syntax's first line (the view publishes a SourceRef).
    private static string Site(Compilation c, OriginRow row) => First(c.SyntaxOf(row.Site));

    private static string First(Koto node) => (node.ToString() ?? string.Empty).Split('\n')[0].Trim();

    // A `for` loop's Borrow of its iterator for `next` (the receiver's adapted Origin) and the parentless Use it is formed at.
    private static (BoundOrigin Borrow, Koto Use) IteratorBorrow(ForKoto loop)
        => (loop.Iteration!.Next.CallOf()!.ReceiverOperation.AdaptedType!.Origin!, loop.Iteration.Receiver);

    private static bool InProgram(Koto node) => node.CodeContext.SourceDocument?.Path == "Program.kimi";

    private static MirBody[] Bodies(Compilation c) => c.BuildMir().Bodies.ToArray();

    private static bool IsProgramConstructor(MirBody body) => body.Key.Decl is { Name: "init" } constructor && InProgram(constructor.Declaration);

    private static bool IsProgramClosure(MirBody body) => body.Key is { Kind: MirBodyKind.Closure, Decl: { } literal } && InProgram(literal.Declaration);

    private static bool IsBodyOf(FunctionKoto function, Koto part) => ReferenceEquals(part, function.Body) || ReferenceEquals(part, function.ExpressionBody);

    private static string Describe(ExecutableBody body) => body.Definition switch
    {
        null => body.IsBody ? "TopLevel" : "None",
        { Declaration: FunctionKoto { IsAnonymous: true } } => "Closure",
        { Kind: BindingSymbolKind.PropertyAccessor } => "Accessor",
        { Kind: BindingSymbolKind.Property } symbol => $"Static:{symbol.Name}",
        { Kind: BindingSymbolKind.Function } symbol => body.Index >= 0 ? $"Default:{symbol.Name}:{body.Index}" : $"Fn:{symbol.Name}",
        { } symbol => $"Layer:{symbol.Name}",
    };

    private static string Show(ExecutableBody body) => $"{Describe(body)} {body.Definition?.Declaration.ToString()?.Split('\n')[0]}";

    // Whether a node has an ancestor (or is one) that the predicate accepts.
    private static bool Within(Koto node, Func<Koto, bool> predicate)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (predicate(current))
            {
                return true;
            }
        }

        return false;
    }

    // The census oracle: whether a body is the one syntactic containment gives a node (true for a node without parents, which only the
    // formation context can place), with the formation exceptions.
    private static bool Contains(ExecutableBody body, Koto node)
    {
        Koto? previous = null;
        for (var current = node; current is not null; previous = current, current = current.Parent)
        {
            switch (current)
            {
                case FunctionKoto { IsAnonymous: true } closure when previous is not null && IsBodyOf(closure, previous):
                    return ReferenceEquals(body.Definition?.Declaration, closure) && body.Index < 0;
                case FunctionKoto { IsAnonymous: true }:
                    continue; // A Closure's header and its literal are its evaluating body's.
                case FunctionKoto function:
                    return FunctionPart(body, function, previous);
                case PropertyAccessorKoto accessor:
                    return previous is not null && ReferenceEquals(previous, accessor.Body)
                        ? ReferenceEquals(body.Definition?.Declaration, accessor) : !body.IsBody;
                case PropertyKoto property:
                    return previous is null && property.InitializerKoto is null ? !body.IsBody : InitializerPart(body, property, previous);
                case DeclarationContainerKoto or AliasKoto:
                    return !body.IsBody;
            }
        }

        return true;
    }

    // A function's body, base initializer and `=> e` are its own body (a module's top-level statements by the module's ordinal); default i
    // is its template i; its signature is no body.
    private static bool FunctionPart(ExecutableBody body, FunctionKoto function, Koto? part)
    {
        if (part is not null && (IsBodyOf(function, part) || ReferenceEquals(part, function.BaseInitializer)))
        {
            return function.IsGenerated ? body.Definition is null && body.Index == function.SyntaxOwner!.Module
                : ReferenceEquals(body.Definition?.Declaration, function) && body.Index < 0;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (part is not null && ReferenceEquals(part, function.Parameters[i].DefaultValue))
            {
                return ReferenceEquals(body.Definition?.Declaration, function) && body.Index == i;
            }
        }

        return !body.IsBody;
    }

    // A Property's initializer and its fit (at the Property itself) are a static's own body or the layer's, its struct; its declared Type
    // is no body.
    private static bool InitializerPart(ExecutableBody body, PropertyKoto property, Koto? part)
    {
        if (part is not null && !ReferenceEquals(part, property.InitializerKoto))
        {
            return !body.IsBody;
        }

        return ReferenceEquals(body.Definition?.Declaration, property.Parent is GroupKoto ? property : property.Parent);
    }

    // Whether a body's oracle entails `longer outlives shorter` in a Semantics case.
    private static bool Entails(Compilation c, ExecutableBody body, BoundOrigin longer, BoundOrigin shorter, ulong caseMask)
    {
        using var oracle = c.Types.UniversalRelations(body);
        oracle.Close(shorter, caseMask);
        return oracle.Outlives(longer);
    }
}
