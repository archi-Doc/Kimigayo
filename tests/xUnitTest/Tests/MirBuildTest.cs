// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>The MIR build (docs/dev/COMPILER_ARCHITECTURE.md, R3): every body of the Kimi library and the milestone programs builds and
/// validates without a finding, a form the builder does not build yet is an Opaque value at its one located record, the build is
/// deterministic and a warm rebuild allocates nothing. The census measures what the builder covers (V9). This file holds the checks of the
/// build as a whole (the corpus, determinism, scaling and allocation) and the helpers the other files share; they hold the tests by what the
/// builder builds: Values, Defaults, Cases (Semantics case views), Calls (reservations and evaluation order) and Places.</summary>
public sealed partial class MirBuildTest(ITestOutputHelper output)
{
    // PLAN G308's forms over the compared Tuple of its DAG family P_n, each a format of the paths {0} = a.x…x and {1} = b.x…x: the tuple
    // operator (S1), a generic equality whose T is the Tuple, its Contract mapping (S4), and a Dictionary keyed by the Tuple (S6).
    internal const string DagOperator = "func f(a: ref/D1<i32>, b: ref/D1<i32>) -> bool => {0} == {1}\n";

    internal const string DagContract = "func same<T>(a: ref/T, b: ref/T) -> bool\n    T is Equatable\n    return a == b\n" +
        "func f(a: ref/D1<i32>, b: ref/D1<i32>) -> bool => same({0}, {1})\n";

    internal const string DagKey = "func fill<K>(key: K)\n    K is Copy and Equatable\n    var table: Dictionary<K, i32> = [:]\n" +
        "    _ = table.tryInsert(key, 1)\n    _ = table.tryGet(key)\nfunc f(a: ref/D1<i32>, b: ref/D1<i32>) => fill({0})\n";

    // PLAN G951's X3: a library function erased at a declared Function Type and called through it (SPEC 10.5.3, 7.7.4; valid).
    internal const string StopwatchAction = "let action: (uniq/::Kimi.Time.Stopwatch) -> () = ::Kimi.Time.Stopwatch.start\n" +
        "var watch = ::Kimi.Time.Stopwatch.init()\naction(watch@uniq)\n";

    // A struct with an Array Field, an inspection of it and the start of a function f that binds a holder (SPEC 15.6.7).
    private const string Holder = "struct Holder\n    public var items: Array<i32>\n    public var count: i32\n    public init()\n" +
        "        self.items = []\n        self.count = 0\nfunc inspect(values: ref/Array<i32>) => ()\nfunc see(value: ref/i32) => ()\n" +
        "func f(x: i32)\n    var holder = Holder.init()\n";

    // A root module's main and its dependency Lib, whose definitions have the body kinds the library lacks (a Closure, a Setter; valid: SPEC
    // 22.4.2, 18.4; 11.8 and 22.4.3, a static let; 7.2.2, a default; 7.7, an anonymous function; 11.3, stored custom accessors over storage;
    // 6.3.6, Gauge's implicit constructor).
    private const string DependencyMain = "public func main() -> ()\n    var gauge = Lib.Gauge.init()\n    gauge.level = 3\n    _ = gauge.level\n" +
        "    _ = Lib.Api.twice(2)\n    _ = Lib.Api.take(1)\n    _ = Lib.Api.useCounter()\n    return\n";

    private const string DependencyLib = "public group Api\n    public let counter: i32 = seed()\n    func seed() -> i32 => 7\n" +
        "    public func useCounter() -> i32 => counter\n    public func take(x: i32, extra: i32 = 1) -> i32 => x + extra\n" +
        "    public func twice(x: i32) -> i32\n        let f = func (y: i32) => y * 2\n        return f(x)\n" +
        "public struct Gauge\n    public var level: i32 = 0\n        get() -> i32\n            return storage\n" +
        "        set(value: i32) -> ()\n            storage = if value > 100 => 100 else => value\n";

    // The callees of the operand order tests and the start of their function f, which binds x (SPEC 12.2).
    private const string Operations = "func take(a: i32, b: ()) -> i32 => a\nfunc pair(t: (i32, ())) => ()\nfunc reset(p: uniq/i32) -> i32 => 0\n" +
        "func consume(a: string, b: ()) => ()\nfunc touch(p: uniq/Slice<i32>) -> isize => 0\nfunc jump(p: uniq/raw/i32) -> isize => 0\n" +
        "func f(view: Slice<i32>, raw: raw/i32)\n    var x: i32 = 1\n";

    // The callees of the evaluation order tests and the start of their function f, whose first parameter is x (SPEC 10.1.1, 7.3.4, 13.7.1).
    private const string Order = "func give2(a: i32, b: i32) => ()\nstruct Box\n    public var v: i32 = 0\n    public init() => ()\n" +
        "    public func put(a: i32, self: ref/Self) => ()\nstruct Gauge\n    public var level: i32 = 0\n        set(value: i32) -> ()\n" +
        "            storage = value\n    public init() => ()\nfunc f(x: i32, box: ref/Box)\n    var y: i32 = 0\n    var gauge = Gauge.init()\n";

    // An unbuilt form of a valid program: a tuple operator over two values of Tuple, whose leaf's storage nests past Binding's bound, has
    // no comparison (a Limit of TyCtxt.Comparison, DECISIONS R5-M75), so the MIR records one located Unsupported_Kd at the operator.
    // Declarations declares the leaf's Type. Since PLAN G167 the other unbuilt form is the second limit past that bound, a default template
    // the call's substitution cannot instantiate (MirBuilder.Defaults.cs, R4-M127); both are PD30's (e) row until R7.
    internal static (string Declarations, string Tuple) Unbuilt { get; } = (
        "struct Wrap<V>\n    Self is Equatable\n    public init() => ()\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n",
        "(" + string.Concat(Enumerable.Repeat("Wrap<", 80)) + "i32" + new string('>', 80) + ", i32)");

    // PLAN G949 S1-r1b (review-s1r1 Re-check 3, RB1; F's rulings 153, 156 and 158): the library's bodies built after a Provisional Bind
    // are the ones built after a Final Bind: the MIR builder reads the library's published HIR whatever the Bind's mode, and the HIR is
    // published from the library's facts as its binding left them. From the split the program pass empties the per-pass tables the
    // publisher reads for library nodes (receiverOperations among them), so the library's module is published at the end of its own
    // pass in every mode; a head that publishes it after the program pass (the lazy PublishHir) fails here on the trial split
    // (25,859 lines against 26,127). SPEC 18: a library body names library bodies only, so the library segment is the same for the
    // empty program and for Hello. The bodies are built over the library check's root range (every library body, PLAN G951 U2-T), since
    // the compilation's own build (ConstructMir) holds the source modules' range from G951 U2 and builds a library body only on demand.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLibrarysBodiesAfterAProvisionalBindAreTheFinalBinds(bool hello)
    {
        var final = LibraryMir(BindingMode.Final);
        var lines = final.Count(static x => x == '\n');
        Assert.True(lines > 10_000, $"the library segment is near empty: {lines} lines"); // Every library body (review-s1r1b Part 2, N4).
        Assert.Equal(final, LibraryMir(BindingMode.Provisional));

        string LibraryMir(BindingMode mode)
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            if (hello)
            {
                c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
            }

            c.Binding.Bind(mode);
            Assert.NotEqual(0ul, HirTables.LibraryVersion(c)); // Its publication's version: the frozen library's in every mode (n4).
            var mir = new StringBuilder();
            MirDump.Write(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), SyntaxTable.LibraryModule, mir);
            return mir.ToString();
        }
    }

    // n4 (PLAN G949 Stage 2 U3; C-378 N4): the library's publication version is Binding.Frozen in every compilation over a frozen library,
    // from its Binding's construction, before its first Bind and after it, so G950-K's stamp holds (0 is a broken contract) and a later
    // MIR version keeps the library segment. Before the switch a compilation's version was its own run counter, 0 before its first Bind.
    [Fact]
    public void EveryCompilationPublishesTheFrozenLibraryVersion()
    {
        foreach (var program in new[] { "docs/examples/Hello/Hello.kimi", "tests/milestones/Milestone26.kimi" })
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(TargetProfile.Windows.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText(program.Split('/'))));
            Assert.Equal(Binding.Frozen, HirTables.LibraryVersion(c));
            Assert.True(c.Bind().IsComplete, program);
            Assert.Equal(Binding.Frozen, HirTables.LibraryVersion(c));
            var segment = c.BuildMir().LibrarySegment;
            Assert.Equal(Binding.Frozen, c.BuildMir().LibraryVersion);
            c.NoteSyntaxEdit(c.Kotonoha.RootKoto);
            Assert.True(c.Bind().IsComplete, program);
            Assert.Equal((Binding.Frozen, segment), (c.BuildMir().LibraryVersion, c.BuildMir().LibrarySegment));
        }
    }

    // V9: builds every body of the corpus (the library check's root range: every library body, PLAN G951 U2-T) and counts the bodies, those with an Opaque value, the records per HirOp and the bodies per number
    // of Semantics case views; with KIMI_MIR_CENSUS naming a file, writes the census there as TSV. Every built body is valid and within the
    // case bound (plan 5.6.1); the bodies one body names, each staticEnsure's initializer, called default template and created Closure, are
    // the validator's program pass's (plan 5.6.2, DECISIONS R4-M121).
    [Fact]
    public void CorpusBuildsValidBodies()
    {
        var rows = new List<string> { "case\tbodies\topaque\tunsupported\tderived" };
        var sites = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var views = new SortedDictionary<long, int>();
        var totals = new int[4];
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            var program = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1);
            Assert.True(program.Findings.Count == 0, $"{name}: {string.Join(", ", program.Findings.Take(20))}");
            int[] counts = [0, 0, 0, 0];
            foreach (var body in program.Bodies)
            {
                if (body.Source.Module == module.Syntax.Module)
                {
                    counts[0]++;
                    counts[1] += body.HasOpaque ? 1 : 0;
                    views[MirCases.Count(body)] = views.GetValueOrDefault(MirCases.Count(body)) + 1;
                    foreach (var site in body.OpaqueSites)
                    {
                        counts[3] += HirTables.Op(c.SyntaxOf(site.Source)) == HirOp.Error ? 1 : 0;
                    }
                }
            }

            foreach (var record in program.Records(MirRecordClass.Construction).Where(x => x.Subject.Module == module.Syntax.Module))
            {
                counts[2]++;
                var key = Key(c.Binding, c.SyntaxOf(record.Subject));
                sites[key] = sites.GetValueOrDefault(key) + 1;
            }

            rows.Add($"{name}\t{string.Join('\t', counts)}");
            for (var i = 0; i < counts.Length; i++)
            {
                totals[i] += counts[i];
            }
        }

        rows.Add($"total\t{string.Join('\t', totals)}");
        rows.Add("site\tcount");
        rows.AddRange(sites.OrderByDescending(static x => x.Value).ThenBy(static x => x.Key, StringComparer.Ordinal).Select(static x => $"{x.Key}\t{x.Value}"));
        rows.Add("views\tbodies");
        rows.AddRange(views.Select(static x => $"{x.Key}\t{x.Value}"));
        output.WriteLine(string.Join('\n', rows));
        if (Environment.GetEnvironmentVariable("KIMI_MIR_CENSUS") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllLines(path, rows, new UTF8Encoding(false));
        }

        Assert.True(totals[0] > 500, $"{totals[0]} bodies");
        Assert.True(views.Keys.Max() <= 64, $"{views.Keys.Max()} views");
    }

    // SPEC 3.6, 16.2.1 (DECISIONS R4-M129): every temp with storage of its own, of a Type that may need drop and written whole, is made, so
    // its body drops it (a held temp that no Made registered has no Drop, and its value would leak), over the goldens, the library and the
    // milestone programs, each built with the library check's root range (every library body, PLAN G951 U2-T). A transfer's result temp
    // has no storage of its own (DECISIONS R5-M50).
    [Fact]
    public void EveryWrittenTempIsMade()
    {
        var programs = MirDumpTest.Programs.Select(static x => ("golden/" + x.Data, MirDumpTest.Compile(x.Data)))
            .Concat(HirColumnsTest.Corpus.Select(static x => (x.Data, SemanticSnapshotTest.Bind(x.Data).Compilation)));
        foreach (var (name, c) in programs)
        {
            foreach (var body in new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1).Bodies)
            {
                var unmade = Unmade(c.Types, body);
                Assert.True(unmade is null, $"{name}: {body.Key.Decl?.Name}'s _{unmade} is never dropped");
            }
        }
    }

    // SPEC 14.8.1, 22.10.1 (R8-W2 N3): a case test, a Case position's or a synthesized call's Result's, is MirBuilder.CaseTest's: its
    // discriminant's StorageLive, its one write and the SwitchInt that reads it lie in one block, over the goldens' programs, as each
    // compilation builds them, and the library (LibraryBuild, PLAN G951 U2). A guard, not a fail-before row (N3 changed MIR text only), so
    // that a re-blessed golden cannot bring back a discriminant held across its call's operands or shared by two tests.
    [Fact]
    public void ACaseTestsDiscriminantIsWrittenOnceInTheBlockThatReadsIt()
    {
        var programs = MirDumpTest.Programs.Select(static x => (Name: x.Data, Program: MirDumpTest.Compile(x.Data).BuildMir())).Append(("library", LibraryBuild()));
        foreach (var (name, program) in programs)
        {
            foreach (var body in program.Bodies)
            {
                var statements = body.Statements.ToArray();
                foreach (var block in body.Blocks)
                {
                    for (var i = block.Start; i < block.Start + block.Count; i++)
                    {
                        if (statements[i] is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Discriminant, Place: var place })
                        {
                            var live = Array.FindIndex(
                                statements, block.Start, i - block.Start, x => x.Kind == MirStatementKind.StorageLive && x.Place == place);
                            Assert.True(
                                block.Terminator is { Kind: MirTerminatorKind.SwitchInt, Operand.Place: var read } && read == place && live >= 0 &&
                                statements.Count(x => x.Kind == MirStatementKind.Assign && x.Place.Root == place.Root) == 1,
                                $"{name}: {body.Key.Decl?.Name}'s _{place.Root} is not one write in the block that reads it");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void OneIntegerValueIsOneConstant()
    {
        // G620: a Literal Pattern's constant is the bits Binding published, the encoding of a literal and a folded conversion, so one u8 value
        // spelled three ways is one interned constant (fail-before: 255 and -1, two).
        var c = Compile("func f(x: u8) -> i32\n    let a: u8 = 255\n    let b = (-1)@wrap<u8>\n    return match x\n        255 => 1\n        _ => 0\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var f = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Int128[] expected = [-1];
        Assert.Equal(expected, f.Consts.ToArray().Where(static x => x.Kind == MirConstKind.Integer && x.Type.Underlying.Name == "u8").Select(static x => x.Bits));
    }

    // G658 (SPEC 13.5.4, DECISIONS R7-M14): a direct literal wrapped to a Type whose instance decides its width is a Proven Wrap Cast of
    // its i128 payload, the exact value modulo 2^128, so no constant of the abstract Type leaves 0 through 127 (fail-before: rejected).
    [Theory]
    [InlineData("T", "200", 200)]
    [InlineData("T", "-1", -1)]
    [InlineData("Wrapping<T>", "340282366920938463463374607431768211455", -1)]
    public void AGenericWrapIsACastOfItsI128Payload(string target, string literal, int bits)
    {
        var c = Compile($"func f<T>(x: T) -> {target}\n    T is PrimitiveInteger\n    return {literal}@wrap<{target}>\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var f = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var cast = Assert.Single(f.Statements.ToArray(), static x => x is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Cast, Op: (byte)MirCastKind.Wrap } }).Rvalue;
        var operand = f.Operands[cast.Start];
        Assert.True(cast.Proven);
        Assert.Equal(target, TyCtxt.Name(cast.Type!));
        Assert.Equal(MirOperandKind.Const, operand.Kind);
        Assert.Equal((MirConstKind.Integer, "i128", (Int128)bits), (f.Consts[operand.Arg].Kind, TyCtxt.Name(f.Consts[operand.Arg].Type), f.Consts[operand.Arg].Bits));
    }

    // SPEC 16.2.2 (DECISIONS R5-M50, R4-M132): the temp a transfer delivers whole into _0 after the Scope Exit is the body's result in
    // transit (MirLocalRole.Result): a return leaving a scope with a cleanup has one, a body's tail value writes _0 and has none, a
    // constructor's return none (its target is a Unit temp), a default template whose tail construct a transfer leaves one, at the
    // construct, and that template's copy in a caller none (it delivers into the caller's slot). Each is valid MIR.
    [Fact]
    public void TheResultInTransitIsTheTempDeliveredIntoTheResult()
    {
        var c = Compile("""
            func leave(x: ref/i32, c: bool) -> ref/i32 during x
                if c
                    let local = 1
                    var r = local@ref
                    return r
                return x
            func plain(x: i32) -> i32 => x
            struct Res
                public var v: i32 = 0
                public init(c: bool)
                    if c
                        let t = 1
                        return
                    self.v = 2
            func evaluate<T>(sample: T, marker: i32 = label result: do
                let pending: Option<T> = .None
                match pending@move
                    .Some(let value) => exit to result 1
                    .None => exit to result 0
            ) -> i32 => marker
            func caller() -> i32 => evaluate("held")
            """);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(
            "DefaultTemplate evaluate 1, Fn caller 0, Fn evaluate 0, Fn init 0, Fn leave 1, Fn plain 0",
            string.Join(", ", program.Bodies.ToArray().Where(static x => x.Source.Module == 0 && x.Key.Decl is not null)
                .Select(static x => $"{x.Key.Kind} {x.Key.Decl!.Name} {x.Locals.ToArray().Count(static y => y.Role == MirLocalRole.Result)}")
                .Order(StringComparer.Ordinal)));
    }

    // The same compilation builds the same MIR, fresh or after a warm rebind, and a fresh compilation of the same source does too; Milestone43
    // keeps Opaque values where Binding rejected it.
    [Fact]
    public void BuildIsDeterministic()
    {
        var first = Dump(SemanticSnapshotTest.Bind("milestone/Milestone43").Compilation);
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone43");
        Assert.Equal(first, Dump(c));
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(first, Dump(c));
        Assert.Contains("opaque", first, StringComparison.Ordinal);
    }

    // PLAN G796: a location's scope is its index in the body's scope table, an int like the table's own (MirScope.Outer), so no count of
    // scopes changes it. 65,540 sibling `do` Bodies in one function (SPEC 14.3; no limit of IMPL 21.3.2 counts them) each hold their
    // assignment in a scope of their own. Fails before G796: the index was narrowed to 16 bits, so the Bodies past the 65,536th joined
    // the body's first scopes, an Unsafe Block's (A5's storing rule skipped) or an inline default copy's included.
    [Fact]
    public void ALocationPastThe65536thScopeKeepsItsScope()
    {
        const int Count = 65_540;
        var body = Body("func f()\n    var n: i32 = 0\n" + string.Concat(Enumerable.Repeat("    do\n        n = 1\n", Count)), "f");
        var used = new HashSet<int>();
        foreach (ref readonly var statement in body.Statements)
        {
            used.Add(statement.Source.Scope);
        }

        Assert.True(used.Count >= Count && body.Scopes.Length > Count, $"{used.Count} scopes hold a statement of {body.Scopes.Length}");
    }

    // R8-L1 M-B1: the library's bodies hold the ordinals 0 to LibraryCount - 1 and the program's follow, a default template and a Closure
    // included, each the body its ordinal reads, in the first build and after a rebind, in an eager build (the library check's root range;
    // a build with the program's range is N3-b's lazy twin, PLAN G951 U2).
    [Fact]
    public void TheLibrarysBodiesAreTheOrdinalsPrefix()
    {
        var c = Compile("func scale(x: i32, by: i32 = 2) -> i32 => x * by\nfunc main()\n    let g = func [] (y: i32) -> i32 => y\n" +
            "    _ = scale(g(1))\n");
        var builder = new MirBuilder(c);
        for (var build = 0; build < 2; build++)
        {
            var program = builder.Build(SyntaxTable.LibraryModule + 1);
            var bodies = program.Bodies.ToArray();
            Assert.InRange(program.LibraryCount, 1, bodies.Length - 4);
            Assert.All(bodies, x => Assert.Equal(x.Ordinal < program.LibraryCount, x.Source.IsLibrary));
            Assert.All(bodies, x => Assert.Same(x, program.Body(x.Ordinal)));
            Assert.Contains(bodies[program.LibraryCount..], static x => x.Key.Kind == MirBodyKind.DefaultTemplate);
            Assert.Contains(bodies[program.LibraryCount..], static x => x.Key.Kind == MirBodyKind.Closure);
            c.Binding.Bind(BindingMode.Final);
        }
    }

    // PLAN G951 U2-K (row SL-1, N3-b's lazy twin; DECISIONS R9-M9's fixed library ordinals, G950 R-4): Compilation builds with the
    // source modules' root range, and each library body its lookups demand takes the ordinal the library check's build gives it, below
    // LibraryCount, which is the library check's; the program's bodies take the ordinals from LibraryCount; the library list holds the
    // library's templates only (none: KimiLibraryShapeTest.TheLibraryDeclaresNoParameterDefault) and the build-order list the
    // program's bodies before the demanded ones; a read by ordinal gives each built body and fails for an empty slot, which A2's table
    // does not hold (row PE-2 through Compilation); in the first build and after a rebind. After the rebind the version keeps its library
    // segment (N6 of U2-K-b's design review; DECISIONS R9-M9, PLAN G951 U2-K-b with G950-K): the first build's demanded library bodies are
    // the library list, the same bodies in build order at their slots, and the version's own bodies are the program's, at least one
    // (in the first build they precede the demanded ones: the third design review's N-a). X3 as below. The
    // row replaces U2's ACompilationsLibraryBodiesFollowItsProgramsBodies, whose expectation (a demanded library body takes the next
    // program ordinal) was R9-M9's Staged state before the fixed ordinals. Fail-before: the demanded bodies followed the program's,
    // LibraryCount 0; before G950-K the rebuilt segment's (the second build's layout was the first's).
    [Fact]
    public void ACompilationsLibraryBodiesTakeTheLibraryChecksOrdinals()
    {
        var c = Compile(StopwatchAction);
        MirBody[] demanded = [];
        for (var build = 0; build < 2; build++)
        {
            _ = c.Analyze();
            var (program, eager) = (c.BuildMir(), new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1));
            var bodies = program.Bodies.ToArray();
            MirBody[] library = [.. bodies.Where(static x => x.Source.IsLibrary)];
            Assert.NotEmpty(library);
            Assert.All(library, x => Assert.Equal(eager[x.Key]!.Ordinal, x.Ordinal));
            Assert.Equal(demanded, program.LibraryBodies.ToArray());
            Assert.Equal(eager.LibraryCount, program.LibraryCount);
            var own = program.VersionBodies.ToArray()[..(bodies.Length - library.Length)];
            Assert.NotEmpty(own);
            Assert.All(own, static x => Assert.False(x.Source.IsLibrary));
            Assert.All(bodies, x => Assert.Equal(x.Source.IsLibrary, x.Ordinal < program.LibraryCount));
            Assert.All(bodies, x => Assert.Equal((x, true), (program.Body(x.Ordinal), program.Drops.Holds(x.Ordinal))));
            var empty = eager.LibraryBodies.ToArray().Where(x => program[x.Key] is null).Select(static x => x.Ordinal).ToArray();
            Assert.Equal(eager.LibraryCount - library.Length, empty.Length);
            Assert.All(empty, x => Assert.False(program.Drops.Holds(x) || program.Drops.RowsOf(x).Length > 0));
            Assert.All(empty, x => Assert.Throws<InvalidOperationException>(() => program.Body(x)));
            demanded = library;
            c.Binding.Bind(BindingMode.Final);
        }
    }

    // PLAN G951 U0 (C-g191, M1): in a build whose root range is the program's, a lookup of a library definition the HIR publishes with a
    // value builds its body once, validated, and runs its program pass where it is built, which builds the initializer its staticEnsure
    // names (Time.readCounterFrequency, which Stopwatch.start calls, reads Time.counterFrequency, SPEC 22.4.3); the key then finds the
    // same body. X3 is valid (SPEC 10.5.3: an erasure at a declared Function Type; 7.7.4).
    [Fact]
    public void ADemandedBodyIsBuiltWithItsProgramPass()
    {
        var c = Compile(StopwatchAction);
        var eager = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1);
        var (reader, frequency) = (Library(eager, MirBodyKind.Fn, "readCounterFrequency"), Library(eager, MirBodyKind.StaticInit, "counterFrequency"));
        var program = new MirBuilder(c).Build(c.SourceModules.Length);
        var count = program.Bodies.Length;
        Assert.DoesNotContain(program.Bodies.ToArray(), static x => x.Source.IsLibrary);
        var body = program[reader];
        Assert.Equal([reader, frequency], program.Bodies[count..].ToArray().Select(static x => x.Key));
        Assert.Equal([eager[reader]!.Ordinal, eager[frequency]!.Ordinal], program.Bodies[count..].ToArray().Select(static x => x.Ordinal));
        Assert.Same(body, program[reader]);
        Assert.Same(body, program.Body(body!.Ordinal));
        Assert.Same(program.Bodies[count + 1], program[frequency]);
        Assert.Equal(count + 2, program.Bodies.Length);
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
    }

    // PLAN G951 U0 (M1, the design's R1.2): every definition of a module outside the root range is demanded as the module walk builds it,
    // with the bodies its program pass demands, a Closure's and a Setter's included; the program pass runs on each body once, so after a
    // nested drain (twice's pass demands its Closure) the next demand's pass runs (useCounter's demands counter's initializer).
    [Fact]
    public void ADependencyModuleIsDemandedAsBuilt()
    {
        var c = ModuleBindingTest.Create(DependencyMain, DependencyLib);
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var eager = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1);
        Assert.Empty(eager.Findings);
        Assert.Empty(eager.Records(MirRecordClass.Construction));
        static string Name(MirBody x) => $"{x.Key.Kind} {x.Key.Decl?.Name}".TrimEnd();
        var demands = new List<string>();
        foreach (var key in eager.Bodies.ToArray().Where(static x => x.Source.Module == 1 && x.Key.Kind != MirBodyKind.DefaultTemplate).Select(static x => x.Key))
        {
            var lazy = new MirBuilder(c).Build(1);
            var count = lazy.Bodies.Length;
            Assert.NotNull(lazy[key]);
            var built = lazy.Bodies[count..].ToArray();
            Assert.All(built, x => Assert.Equal(MirDumpTest.Unsited(eager, eager[x.Key]!), MirDumpTest.Unsited(lazy, x)));
            Assert.All(built, x => Assert.InRange(x.Ordinal, lazy.LibraryCount, int.MaxValue)); // No library body: the next ordinal (row SL-3).
            demands.Add(string.Join(", ", built.Select(Name)));
        }

        Assert.Equal(
            [
                "StaticInit counter", "Fn seed", "Fn useCounter, StaticInit counter", "Fn take", "Fn twice, Closure", "Closure", "Getter level.Get",
                "Setter level.Set", "Fn init",
            ],
            demands);
        var program = new MirBuilder(c).Build(1);
        Assert.NotNull(program[eager.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "twice" }).Key]);
        var before = program.Bodies.Length;
        Assert.NotNull(program[eager.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "useCounter" }).Key]);
        Assert.Equal(["Fn useCounter", "StaticInit counter"], program.Bodies[before..].ToArray().Select(Name));
        Assert.Empty(program.Findings);
    }

    // PLAN G951 U0 (N6): null means only "no body": a key the HIR publishes no value for (a library function without a body), a key that is
    // not its definition's body (an accessor's executing function as Fn, whose body is its Getter's) and any lookup of a sealed version
    // build nothing; the root range's walk built every body below it, so an eager build never demands. X3 as above.
    [Fact]
    public void ALookupWithoutABodyBuildsNothing()
    {
        var c = Compile(StopwatchAction);
        var eager = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1);
        var getter = eager.Bodies.ToArray().First(static x => x.Source.IsLibrary && x.Key.Kind == MirBodyKind.Getter).Key;
        var program = new MirBuilder(c).Build(c.SourceModules.Length);
        var count = program.Bodies.Length;
        Assert.Null(program.Callee(Bodyless(eager)));
        Assert.Null(program[getter with { Kind = MirBodyKind.Fn }]);
        Assert.Equal(count, program.Bodies.Length);
        Assert.Equal(MirBodyKind.Getter, program[getter]!.Key.Kind);
        count = program.Bodies.Length;
        program.Enter(MirProgram.Phase.Sealed);
        Assert.Null(program[Library(eager, MirBodyKind.Fn, "start")]);
        Assert.Equal(count, program.Bodies.Length);
        Assert.Equal(MirProgram.Phase.Sealed, c.BuildMir().Demands);
    }

    // PLAN G951 U1 (R2-N1, R3-N1): while the analyses judge (step 4 and MirOrigins.Findings), a lookup that builds a body is a broken
    // contract and throws, and a definition without a body is null; the version is Open again afterwards, a return to Open without
    // Judging is a broken contract (so a Run that skipped Judging fails), and a sealed version stays sealed. X3 as above.
    [Fact]
    public void ALookupThatBuildsWhileJudgingThrows()
    {
        var c = Compile(StopwatchAction);
        var eager = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1);
        var program = new MirBuilder(c).Build(c.SourceModules.Length);
        program.Enter(MirProgram.Phase.Judging);
        Assert.Null(program.Callee(Bodyless(eager)));
        Assert.Throws<InvalidOperationException>(() => program[Library(eager, MirBodyKind.Fn, "start")]);
        program.Enter(MirProgram.Phase.Open);
        Assert.NotNull(program[Library(eager, MirBodyKind.Fn, "reset")]);
        Assert.Throws<InvalidOperationException>(() => program.Enter(MirProgram.Phase.Open));
        program.Enter(MirProgram.Phase.Sealed);
        program.Enter(MirProgram.Phase.Open);
        Assert.Equal(MirProgram.Phase.Sealed, program.Demands);
    }

    // PLAN G951 U0 (N4, R3-N2): a version is built with one root range: the same range finds the version's program as it is, demanded bodies
    // included, and another range for the same version is a broken contract, since the analyses and the collector keep a version's facts by
    // its version. Version 0 is no version: a compilation no Binding pass published builds the empty program in any range.
    // PLAN G951 U2-D (D1 per builder; G950-R's range note): every version of a builder keeps the library on the side of the range its first
    // version had, since a kept library segment's shape is that side's and its stamp holds no range: a later version with the library on
    // the other side is a broken contract either way (D1a: source range, then library range; library, then source), and a range that grows
    // with the library outside builds (D1b: Lib's module joins the root range). X3 as above; Lib as in ADependencyModuleIsDemandedAsBuilt.
    [Fact]
    public void AVersionIsBuiltWithOneRootRange()
    {
        Assert.Empty(new MirBuilder(Compilation.CreateForTest()).Build(SyntaxTable.LibraryModule + 1).Bodies.ToArray());
        var c = Compile(StopwatchAction);
        var start = Library(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), MirBodyKind.Fn, "start");
        var builder = new MirBuilder(c);
        var program = builder.Build(c.SourceModules.Length);
        Assert.NotNull(program[start]);
        var count = program.Bodies.Length;
        Assert.Same(program, builder.Build(c.SourceModules.Length));
        Assert.Equal(count, program.Bodies.Length);
        Assert.Throws<InvalidOperationException>(() => builder.Build(SyntaxTable.LibraryModule + 1));
        foreach (var (first, later) in new[] { (c.SourceModules.Length, SyntaxTable.LibraryModule + 1), (SyntaxTable.LibraryModule + 1, c.SourceModules.Length) })
        {
            var sided = new MirBuilder(c);
            Assert.Equal(first, sided.Build(first).RootModules);
            c.Binding.Bind(BindingMode.Final);
            Assert.Throws<InvalidOperationException>(() => sided.Build(later));
        }

        var dependency = ModuleBindingTest.Create(DependencyMain, DependencyLib);
        Assert.True(dependency.Bind().IsComplete);
        var growing = new MirBuilder(dependency);
        var one = growing.Build(1).Bodies.Length;
        Assert.True(dependency.Bind().IsComplete);
        var two = growing.Build(2);
        Assert.Equal(2, two.RootModules);
        Assert.InRange(one, 1, two.Bodies.Length - 1);
        Assert.Empty(two.Findings);
    }

    // V11: publishing, building and validating an unchanged compilation again, and selecting every Semantics case view of its bodies with
    // their locals' case Types, allocates nothing, owning interpolations (Milestone30) and pair-generic bodies (Milestone28) included.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("milestone/Milestone34")]
    [InlineData("milestone/Milestone28")]
    [InlineData("milestone/Milestone30")]
    public void WarmRebuildAllocatesNothing(string name)
    {
        var (c, _) = SemanticSnapshotTest.Bind(name);
        var (bodies, version) = (c.BuildMir().Bodies.Length, c.BuildMir().Version);
        var cases = new MirCases(c.Types);
        var rebuilds = 0UL;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            rebuilds++;
            c.Binding.Bind(BindingMode.Final);
            foreach (var body in c.BuildMir().Bodies)
            {
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    cases.Select(body, view);
                    foreach (var local in body.Locals)
                    {
                        _ = cases.KnownType(local.Type);
                    }
                }
            }
        }));
        Assert.Equal((bodies, version + rebuilds), (c.BuildMir().Bodies.Length, c.BuildMir().Version)); // Warm-up and measured rebuilds.
    }

    // DECISIONS R5-M186 (review B3): a warm rebind and rebuild of a program whose failure bodies rest on guesses allocates nothing: the
    // builder copies Binding's guessed nodes into the body's pooled sites at once. A guard: before R6-1 the builder asked no guess.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebuildOfGuessedFailureBodiesAllocatesNothing()
    {
        var c = Compile("func f(flag: bool)\n    require flag else => missing()\n    require flag else => missing()\n");
        Assert.Equal(2, c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f").GuessedSites.Length);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            c.BuildMir();
        }));
    }

    // DECISIONS R5-M186 (review N6): the builder asks the guess rule only at a failure body that can complete, so N nested requires whose
    // outer failure bodies return after the inner require, in a pass that left the innermost call unresolved, cost the guess walk work
    // linear in N, and only the innermost row names a site. A guard: the guess walk never runs where the structural walk finds no
    // completion, which the condition also saves and GuessCompletionWork does not count.
    [Fact]
    public void GuessedSitesOfNestedRequiresScaleLinearly()
    {
        long[] work = [Work(8), Work(16), Work(32)];
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"guess walk {string.Join(", ", work)}");

        static long Work(int depth)
        {
            var text = new StringBuilder("func nest(flag: bool)\n");
            for (var i = 1; i <= depth; i++)
            {
                text.Append(' ', 4 * i).Append("require flag else\n");
            }

            text.Append(' ', 4 * (depth + 1)).Append("missing()\n");
            for (var i = depth; i > 1; i--)
            {
                text.Append(' ', 4 * i).Append("return\n");
            }

            var c = Compile(text.ToString());
            var before = c.Binding.GuessCompletionWork;
            var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "nest");
            Assert.Equal(1, body.GuessedSites.Length);
            return c.Binding.GuessCompletionWork - before;
        }
    }

    // G350: the process's one-time initialization on the check path stays small. A fresh process's first cold build allocates at most
    // 64 KB more than a second cold build of the same text on a fresh Compilation: the class constructors of the rows and ABIs it reads
    // and the runtime's first instantiations, measured about 21.5 KB on .NET 10, three times that being the margin. Before G350 the
    // collector's IntrinsicRows built code generation's runtime texts there, 8.2 MB more. This test owns that one-time cost:
    // AllocationMeasurement.MeasureSeries keeps it out of every cold scaling series.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public Task AFreshProcessPaysLittleOnceForItsFirstBuild()
        => FreshProcess.RunExplicit(typeof(MirBuildTest), nameof(MirBuildTest.TheFirstBuildOfAFreshProcess));

    // AFreshProcessPaysLittleOnceForItsFirstBuild's child: the first and a second cold build, each of a fresh Compilation of one text.
    [Trait("Purpose", "Allocation")]
    [Fact(Explicit = true)]
    public void TheFirstBuildOfAFreshProcess()
    {
        var allocated = new long[2];
        for (var i = 0; i < allocated.Length; i++)
        {
            var c = Compile("func work() -> i32 => 1\n");
            allocated[i] = AllocationMeasurement.MeasureOnce(() => c.BuildMir());
        }

        output.WriteLine($"cold build allocation {allocated[0]}, then {allocated[1]}");
        Assert.True(allocated[0] - allocated[1] <= 64 * 1024, $"cold build allocation {allocated[0]}, then {allocated[1]}");
    }

    // G308 S1 (==), S2 (<, >=; V16): the program of a comparison over P_n's Tuple at n = 8, 12, 16 has a MIR (Blocks + Statements of every
    // body BuildMir returns) linear in n, the distinct Types (DropTreesScaleLinearly's ratio rule). Fails before G308: the comparison builds
    // each of its 2^n leaves, so the second increment is about 16 times the first.
    [Theory]
    [InlineData("==")]
    [InlineData("<")]
    [InlineData(">=")]
    public void ADagComparisonsMirGrowsWithItsDistinctTypes(string op)
    {
        var sizes = new[] { 8, 12, 16 };
        var work = sizes.Select(n =>
        {
            var program = Compile(Dag(n, DagOperator.Replace("==", op, StringComparison.Ordinal))).BuildMir();
            Assert.Empty(program.Findings);
            return program.Bodies.ToArray().Sum(static x => (long)x.Blocks.Length + x.Statements.Length);
        }).ToArray();
        output.WriteLine($"{op} N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}");
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"{op}: MIR size {string.Join(", ", work)}");
    }

    // G136 N2: a malformed fixture must fail before Binding can silently change the program under test.
    [Theory]
    [InlineData("func loop() => ()\n")]
    [InlineData("func f() => \"unterminated\n")]
    public void CompileRejectsSyntaxErrors(string source)
        => Assert.ThrowsAny<XunitException>(() => Compile(source));

    [Fact]
    public void CompileAllowsBindingErrors()
        => Assert.NotEmpty(Compile("func f() => missing\n").Binding.Issues);

    // R7 plan Z23 (SPEC 8.14, 4.7.1 step 3, 4.7.4, 7.2.3): a take by case owns one case branch where its decision splits the views: a1's
    // `v[0]` and n5's `w[0][0]` (the outer index takes the side) one, r2's key of the same decision none of its own, r1's key of another
    // binder one on each side; t1's take of another binder in the opened index's own key closes its branch before v's (the key is
    // located before the branch), and t1s's of the same binder too, two branches of one decision in sequence; d2's default copy in
    // `main` keeps its branch on the case-free decider `ref/i32`.
    [Theory]
    [InlineData("    let x = v[0]\n", "f", 1, "s/T")]
    [InlineData("    let x = w[0][0]\n", "f", 1, "s/T")]
    [InlineData("    let x = w[0][pick(u[0])]\n", "f", 1, "s/T")]
    [InlineData("    let x = w[0][pick(r[0])]\n", "f", 3, "s/T, r/U, r/U")]
    [InlineData("    let x = v[pick(r[0])]\n", "f", 2, "r/U, s/T")]
    [InlineData("    let x = v[pick(u[0])]\n", "f", 2, "s/T, s/T")]
    [InlineData("", "", 1, "ref/i32")]
    public void ATakeByCaseBranchesWhereItsDecisionSplitsTheViews(string take, string function, int branches, string deciders)
    {
        const string Two = "struct Two<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n" +
            "    public init(first: T) => self.first = first@move\n    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n" +
            "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq\n" +
            "func pick<X>(value: X) -> isize => 0\n";
        var source = take.Length == 0
            ? Two + "func g<s/T>(v: s/T, k: isize = pick(Two.init(5@s)[0])) -> isize\n    s is ref or uniq\n    return k\nvar n: i32 = 1\n_ = g(n@ref)\n"
            : Two + "func f<s/T, r/U>(v: uniq/Two<s/T>, w: uniq/Two<Two<s/T>>, u: uniq/Two<s/T>, r: uniq/Two<r/U>) -> ()\n    s is ref or uniq\n" +
              "    r is ref or uniq\n" + take + "    _ = x\n";
        var c = Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records(MirRecordClass.Construction));
        var body = program.Bodies.ToArray().Single(x => function.Length == 0 ? x.Key.Decl is null && x.Source.Module == 0 : x.Key.Decl?.Name == function);
        var found = body.Blocks.ToArray().Where(static x => x.Terminator.Kind == MirTerminatorKind.CaseSwitch).ToArray();
        Assert.Equal(branches, body.CaseSwitches);
        Assert.Equal(deciders, string.Join(", ", found.Select(x => body.Consts[x.Terminator.Operand.Arg].Type.ToString())));
    }

    // A body's TestRecordValue calls, in block order.
    internal static MirTerminator[] Records(MirBody body) => [.. body.Blocks.ToArray().Select(static x => x.Terminator)
        .Where(static x => x is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.TestRecordValue } })];

    // PLAN G308's DAG family P_n with a form: D1 to Dn, D_k<T>'s Field x a D_{k+1}<(T, T)> and Dn's the pair (T, T), so a.x…x (n steps)
    // of a D1<i32> is a Tuple of 2^n leaves but n distinct composite Types; each init builds its value from one leaf.
    internal static string Dag(int n, string form)
    {
        var text = new StringBuilder();
        for (var k = 1; k <= n; k++)
        {
            var (field, value) = k < n ? ($"D{k + 1}<(T, T)>", $"D{k + 1}<(T, T)>.init((value, value))") : ("(T, T)", "(value, value)");
            text.Append($"struct D{k}<T>\n    T is Copy\n    public var x: {field}\n    public init(value: T) => self.x = {value}\n");
        }

        var steps = string.Concat(Enumerable.Repeat(".x", n));
        return text.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, form, "a" + steps, "b" + steps)).ToString();
    }

    internal static Compilation Compile(string source, bool test = false)
    {
        var c = Compilation.CreateForTest();
        c.IsTestBuild = test;
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        Assert.DoesNotContain(TestDiagnostics.Of(c), static x => x.Severity == DiagnosticSeverity.Error);
        c.Bind();
        _ = test ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(OutputKind.Application);
        return c;
    }

    // PLAN G951 U2-T: every body of a compilation, the library's included, built by a builder of its own with the library check's root
    // range and analyzed with the source modules as roots, as Compilation built and analyzed its own before U2's switch: the program a test
    // of the library's bodies or their A2 and A4 facts reads, since Compilation's own build holds only the library bodies its program reaches.
    internal static MirProgram LibraryRange(Compilation c)
        => new MirAnalysis(c.Types).Run(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), c.SourceModules.Length);

    // PLAN G951 U2-F: every library body, built with the library check's root range from the library case (an empty program, so the
    // library's bodies alone): the library's row of a rule over every MIR body, whose programs' compilations build only the library bodies
    // they reach.
    internal static MirProgram LibraryBuild() => new MirBuilder(SemanticSnapshotTest.Bind("library").Compilation).Build(SyntaxTable.LibraryModule + 1);

    // The key of a library body an eager build built, by its kind and definition's name.
    internal static MirDefKey Library(MirProgram program, MirBodyKind kind, string name)
        => program.Bodies.ToArray().Single(x => x.Source.IsLibrary && x.Key.Kind == kind && x.Key.Decl?.Name == name).Key;

    // A Fn callee of a library body an eager build built whose function has no body (an intrinsic, an import).
    private static MirCallee Bodyless(MirProgram eager) => eager.Bodies.ToArray().Where(static x => x.Source.IsLibrary).SelectMany(static x => x.Blocks.ToArray())
        .Select(static x => x.Terminator.Callee).First(x => x is { Kind: MirCalleeKind.Fn, Arg: 0, Symbol: not null } && eager.Callee(x) is null);

    // The body of a function of a source, built without validator findings.
    private static MirBody Body(string source, string name)
    {
        var program = Compile(source).BuildMir();
        Assert.Empty(program.Findings);
        return program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name);
    }

    // A function's view statements as its dump spells them (a Storage; an Add), without their sites, scopes and local numbers.
    private static string Views(MirProgram program, string name)
    {
        var body = Array.Find(program.Bodies.ToArray(), x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == name && x.Source.Module == 0)!;
        return string.Join("|", MirDumpTest.Unsited(program, body).Split('\n')
            .Where(static x => x.Contains("storage(", StringComparison.Ordinal) || x.Contains("= Add(", StringComparison.Ordinal))
            .Select(static x => Regex.Replace(Regex.Replace(x, @"^[ *]+| s\d+$", string.Empty), @"_\d+", "_")));
    }

    // The locations of a body's runtime path from its entry, as far as each block has one runtime successor: its statements, then its
    // terminator.
    private static IEnumerable<MirLocation> RuntimePath(MirBody body)
    {
        for (var block = 0; block >= 0; block = MirUses.Successors(body.Blocks[block].Terminator) == 1 ? body.Blocks[block].Terminator.Target : -1)
        {
            for (var i = 0; i <= body.Blocks[block].Count; i++)
            {
                yield return new(block, i);
            }
        }
    }

    // A Place as its root and its projections in order: a Deref as *, a Field as its position, so (*_1).0 is _1*.0.
    private static string Steps(MirBody body, MirPlace place) => place.Projection == 0 ? $"_{place.Root}"
        : Steps(body, place with { Projection = body.Projections[place.Projection].Prefix }) + body.Projections[place.Projection] switch
        {
            { Kind: MirProjectionKind.Deref } => "*",
            { Kind: MirProjectionKind.Field } row => $".{row.Arg}",
            var row => $"?{row.Kind}{row.Arg}",
        };

    // The census key of a record: its Op, a Name's Res, a call's callee kind or the part of the call not built yet, and an Adjust the
    // builder does not take yet.
    private static string Key(Binding binding, Koto node)
    {
        var op = HirTables.Op(node);
        var key = op.ToString();
        if (op == HirOp.Name)
        {
            key += "." + HirTables.ResOf(node);
        }
        else if (op == HirOp.Call && CallPlan.Of(binding, node) is { } call)
        {
            var disposition = call.Kind == CalleeKind.Value ? default : call.Target.Definition;
            key += "." + (call.Kind != CalleeKind.Function ? call.Kind.ToString() : disposition.Kind != DispositionKind.Body ? disposition.Kind.ToString()
                : CallPlan.Of(binding, node, 1) is not null ? "Expansion" : !call.DefaultArguments.IsEmpty ? "Default" : CallPlan.Parameter(binding, call.Target, -1) is not null ? "Constructor" : "Receiver");
        }

        var adjust = HirTables.AdjustOf(node);
        var plain = adjust.Acquire switch // Every member named, so a new one fails here (G422's review N-1).
        {
            HirAcquire.Unset or HirAcquire.None or HirAcquire.Copy or HirAcquire.Move => true,
            HirAcquire.Temporary or HirAcquire.Failed => true,
            HirAcquire.Reborrow or HirAcquire.ByCase => false,
            _ => throw new UnreachableException(),
        };
        return adjust.Adaptation == HirAdaptation.None && plain ? key : $"{key} {adjust.Acquire}/{adjust.Adaptation}";
    }

    // The first temp of a body with storage of its own, of a Type that may need drop, written whole and never dropped, or null.
    private static int? Unmade(TyCtxt types, MirBody body)
    {
        var (live, written, dropped) = (new HashSet<int>(), new HashSet<int>(), new HashSet<int>());
        foreach (var statement in body.Statements)
        {
            if (statement.Kind == MirStatementKind.StorageLive)
            {
                live.Add(statement.Place.Root);
            }
            else if (statement is { Kind: MirStatementKind.Assign, Place.Projection: 0 })
            {
                written.Add(statement.Place.Root);
            }
        }

        foreach (var block in body.Blocks)
        {
            if (block.Terminator is { Kind: MirTerminatorKind.Call or MirTerminatorKind.Drop, Place.Projection: 0 } terminator)
            {
                (terminator.Kind == MirTerminatorKind.Call ? written : dropped).Add(terminator.Place.Root);
            }
        }

        for (var local = 0; local < body.Locals.Length; local++)
        {
            if (body.Locals[local].Kind == MirLocalKind.Temp && live.Contains(local) && written.Contains(local) && !dropped.Contains(local) &&
                MirTypes.MayNeedDrop(types, body.Locals[local].Type, []))
            {
                return local;
            }
        }

        return null;
    }

    // A compilation's MIR dump: its program's, as Compilation builds it, and every library body's, as the library check builds them (PLAN
    // G951 U2-T: Compilation's own build holds only the library bodies its program reaches).
    private static string Dump(Compilation c)
    {
        var text = new StringBuilder();
        MirDump.Write(c.BuildMir(), 0, text);
        MirDump.Write(LibraryRange(c), SyntaxTable.LibraryModule, text);
        return text.ToString();
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public partial class AllocationTests(ITestOutputHelper output)
    {
        // V11, plan 5.6.4: N defers, then N returns that each leave all of them, build each defer once per target (one drop tree shared by
        // the returns, and the normal end), so the MIR and its cold build's allocation grow linearly with N, and a warm rebuild allocates nothing.
        [Fact]
        public void DropTreesScaleLinearly()
        {
            var (sizes, work, compilations, bodies) = (new[] { 16, 32, 64 }, new long[3], new Compilation[3], new MirBody[3]);
            var allocated = AllocationMeasurement.MeasureSeries(sizes.Length, i =>
            {
                var text = new StringBuilder("func touch(n: i32) => ()\nfunc work(flag: bool) -> i32\n");
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.Append("    defer => touch(").Append(n).Append(")\n");
                }

                for (var n = 0; n < sizes[i]; n++)
                {
                    text.Append("    if flag => return ").Append(n).Append('\n');
                }

                var c = compilations[i] = Compile(text.Append("    return -1\n").ToString());
                return () => bodies[i] = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "work");
            });
            for (var i = 0; i < sizes.Length; i++)
            {
                work[i] = bodies[i].Blocks.Length + bodies[i].Statements.Length;
                Assert.Equal(
                    sizes[i] * 2, bodies[i].Blocks.ToArray().Count(static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "touch" }));
            }

            output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold build allocation {string.Join(", ", allocated)}");
            Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
            Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");
            var last = compilations[^1];
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                last.Binding.Bind(BindingMode.Final);
                last.BuildMir();
            }));
        }

        // V11: a body of N statement groups of the common forms (a local from a call, its case adaptation, a condition with an update, a match
        // with a binding, a loop with a transfer, an interpolation) grows its MIR and its cold build's allocation linearly with N, and a warm
        // rebuild, whose validation resolves each adaptation under both case views, allocates nothing. The build analyzes (R4-M17); the output
        // gives A1's dense arena words for the body, which grow with heads × flow paths (R4-M20, V16's reading).
        [Fact]
        public void BodiesScaleLinearly()
        {
            var (sizes, work, arena) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
            var (compilations, programs, bodies) = (new Compilation[3], new MirProgram[3], new MirBody[3]);
            var allocated = AllocationMeasurement.MeasureSeries(sizes.Length, i =>
            {
                var text = new StringBuilder("enum Shape\n    Dot\n    Line(i32)\nfunc touch(n: i32) -> i32 => n\nfunc work<s/T>(flag: bool, shape: Shape) -> i32\n");
                text.Append("    s is owner or ref\n    var total = 0\n");
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.Append($"    let v{n} = touch({n})\n    _ = v{n}@s\n    if flag and v{n} > 0 => total += v{n}\n    match shape\n        .Line(let x) => total += x\n        _ => ()\n");
                    text.Append("    while total > 1000\n        total -= 1\n        if flag => exit\n    Console.writeLine(\"\\(total)\")\n");
                }

                var c = compilations[i] = Compile(text.Append("    return total\n").ToString());
                return () =>
                {
                    programs[i] = c.BuildMir();
                    bodies[i] = programs[i].Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "work" });
                };
            });
            for (var i = 0; i < sizes.Length; i++)
            {
                var (program, body) = (programs[i], bodies[i]);
                work[i] = body.Blocks.Length + body.Statements.Length;
                Assert.True(!body.HasOpaque && program.Records(MirRecordClass.Construction).Count() == 0 && program.Findings.Count == 0, $"{sizes[i]} groups");
                var (flow, moves) = (new MirDataflow(new MirReach(), new MirCases(compilations[i].Types)), new MirMoveData(compilations[i].Types));
                flow.Begin(body);
                moves.Build(body, flow);
                arena[i] = MirInitTest.ArenaWords(body, flow, moves);
            }

            output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold build allocation {string.Join(", ", allocated)}, " +
                $"A1 arena words {string.Join(", ", arena)}");
            Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
            Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");
            var last = compilations[^1];
            Assert.Equal(0, AllocationMeasurement.Measure(() =>
            {
                last.Binding.Bind(BindingMode.Final);
                last.BuildMir();
            }));
        }
    }
}
