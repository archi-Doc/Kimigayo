// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The Places of the MIR build: selections lending their receivers, positions, stored references, user indexes, Property targets,
/// compound updates and guard candidates.</summary>
public sealed partial class MirBuildTest
{
    // SPEC 4.6.3, 4.8.1, 5.6 (DECISIONS R4-M40, R4-M55): a range selection's RawSlice takes what the Slice retains as its backing, the
    // third operand: a shared borrow of an array's whole Place, or a Slice receiver's handle, its value and never borrowed, read where it
    // is located (SPEC 4.7.4: copied first where a boundary builds code); nothing backs a written Raw.slice, so its backing is Unit.
    [Fact]
    public void RangeSelectionsPassTheirBacking()
    {
        const string Source = "func middle(values: ref/[5 of i32]) -> Slice<i32> during values => values[1..^1]\n" +
            "func reslice(view: Slice<i32>) -> Slice<i32> during view.source => view[1..]\nfunc size(view: Slice<i32>) -> isize => 1\n" +
            "func skip(view: Slice<i32>) -> Slice<i32> during view.source => view[size(view)..]\n" +
            "func empty() -> Slice<i32>\n    unsafe => return Raw.slice(null@raw/i32, 0)\n";
        var middle = Body(Source, "middle");
        var backing = middle.Operands[Slice(middle)];
        var borrow = Assert.Single(
            middle.Statements.ToArray(),
            x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref } && x.Place == backing.Place);
        Assert.Equal((MirRefKind.Shared, 1), ((MirRefKind)borrow.Rvalue.Op, borrow.Rvalue.Place.Root));
        var reslice = Body(Source, "reslice");
        Assert.Equal((MirOperandKind.Copy, "_1"), (reslice.Operands[Slice(reslice)].Kind, Steps(reslice, reslice.Operands[Slice(reslice)].Place)));
        var skip = Body(Source, "skip");
        var handle = skip.Operands[Slice(skip)];
        var copy = Assert.Single(skip.Statements.ToArray(), x => x.Kind == MirStatementKind.Assign && x.Place == handle.Place);
        Assert.Equal((MirOperandKind.Copy, MirRvalueKind.Use), (handle.Kind, copy.Rvalue.Kind));
        Assert.Equal((MirOperandKind.Copy, 1), (skip.Operands[copy.Rvalue.Start].Kind, skip.Operands[copy.Rvalue.Start].Place.Root));
        Assert.All(
            new[] { reslice, skip },
            static body => Assert.DoesNotContain(
                body.Statements.ToArray(),
                static x => x is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Ref, Place.Root: 1 } } && x.Rvalue.Op != (byte)MirRefKind.Raw));
        var empty = Body(Source, "empty");
        Assert.Equal(MirConstKind.Unit, empty.Consts[empty.Operands[Slice(empty)].Arg].Kind);

        // The third operand of the body's one RawSlice.
        static int Slice(MirBody body)
        {
            var call = body.Blocks.ToArray().Single(static x => x.Terminator is
            {
                Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.RawSlice },
            });
            Assert.Equal(3, call.Terminator.Count);
            return call.Terminator.Start + 2;
        }
    }

    // SPEC 4.7.4, 4.8.1 (DECISIONS R4-M56): a built-in selection lends its receiver's storage shared where it locates it, before the index:
    // an element's guard, ended once the index is evaluated and before the element's own access (a read, or an index update's write), and
    // a range's RawSlice backing, formed before its boundaries.
    [Theory]
    [InlineData("element", "    let x = values[reset(values@uniq)]\n")]
    [InlineData("update", "    values[reset(values@uniq)] = 9\n")]
    [InlineData("range", "    let s = values[reset(values@uniq)..]\n")]
    public void SelectionsLendTheirReceiverWhereLocated(string name, string statement)
    {
        var body = Body($"func reset(v: uniq/[3 of i32]) -> isize => 0\nfunc {name}()\n    var values: [3 of i32] = [1, 2, 3]\n{statement}", name);
        var values = Enumerable.Range(0, body.Locals.Length).Single(x => body.Locals[x].Name == "values");
        var path = RuntimePath(body).ToArray();
        var lent = Array.FindIndex(path, at => Statement(at) is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Ref, Place.Projection: 0 } } x &&
            x.Rvalue.Place.Root == values && x.Rvalue.Op == (byte)MirRefKind.Shared);
        var reset = Array.FindIndex(path, at => at.Index == body.Blocks[at.Block].Count && body.Blocks[at.Block].Terminator.Callee.Symbol?.Name == "reset");
        Assert.InRange(lent, 0, reset - 1);
        if (name != "range")
        {
            var guard = Statement(path[lent])!.Value.Place;
            var end = Array.FindIndex(path, at => Statement(at) is { Kind: MirStatementKind.EndBorrow } x && x.Place == guard);
            var access = Array.FindIndex(path, at => Statement(at) is { Kind: MirStatementKind.Assign } x && (Indexes(x.Place) ||
                (x.Rvalue.Kind == MirRvalueKind.Use && Indexes(body.Operands[x.Rvalue.Start].Place))));
            Assert.InRange(end, reset + 1, access - 1);
        }

        MirStatement? Statement(MirLocation at) => at.Index < body.Blocks[at.Block].Count ? body.Statements[body.Blocks[at.Block].Start + at.Index] : null;

        bool Indexes(MirPlace place) => place.Root == values && body.Projections[place.Projection].Kind == MirProjectionKind.Index;
    }

    // SPEC 15.1.3: a fixed-array element at a literal position is projected at the position its index keeps on its own plan row, so the
    // literal's own row, here an Error, is never read.
    [Fact]
    public void AConstIndexReadsItsOwnPosition()
    {
        Assert.Equal(Built(rejected: false), Built(rejected: true));

        static string Built(bool rejected)
        {
            var c = Compile("func second(values: [3 of i32]) -> i32 => values[(1)]\n");
            var literal = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<NumberLiteralKoto>().Single(static x => x.ToString() == "1");
            if (rejected)
            {
                HirTables.Of(literal).Failures[literal.SyntaxId] = BindingFailure.TypeMismatch;
            }

            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records);
            Assert.Equal(HirPlaceKind.ConstIndex, HirTables.PlaceOf(literal.Parent!.Parent!).Kind);
            var dump = new StringBuilder();
            MirDump.Write(program, 0, dump);
            return dump.ToString();
        }
    }

    // SPEC 4.6.1, 4.7.1, 22.5.2 (DECISIONS R5-M84): a position is the isize its selection reads, its value where isize holds it, else -1,
    // which only the selection's one bounds check rejects. A Type whose range lies within isize's converts proven; another is compared
    // with isize's extremes in its own Type first, and the Index's local is -1 on the other arm. No position fails by itself (fail-before:
    // a u64 or usize position wrapped, an i128 one converted checked).
    [Theory]
    [InlineData("i8", "")]
    [InlineData("u32", "")]
    [InlineData("u64", "Le(copy _2, const 9223372036854775807_u64)")]
    [InlineData("usize", "Le(copy _2, const 9223372036854775807_usize)")]
    [InlineData("i128", "Le(const -9223372036854775808_i128, copy _2) Le(copy _2, const 9223372036854775807_i128)")]
    public void APositionIsItsValueWhereIsizeHoldsItElseMinusOne(string type, string tests)
    {
        var program = Compile($"func f(values: [3 of i32], p: {type}) -> i32 => values[p]\n").BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        Assert.Empty(program.Problems.All.ToArray());
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        Assert.Equal(tests, string.Join(' ', Regex.Matches(text.ToString(), @"Le\([^)]*\)").Select(static x => x.Value)));
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var index = body.Projections[1..].ToArray().Single(static x => x.Kind == MirProjectionKind.Index).Arg;
        var writes = body.Statements.ToArray().Where(x => x.Kind == MirStatementKind.Assign && x.Place == new MirPlace(index)).Select(x => x.Rvalue switch
        {
            { Kind: MirRvalueKind.Cast, Op: (byte)MirCastKind.Integer, Proven: true } cast => $"cast _{body.Operands[cast.Start].Place.Root}",
            { Kind: MirRvalueKind.Use } use when body.Operands[use.Start] is { Kind: MirOperandKind.Const } value => $"{body.Consts[value.Arg].Bits}",
            var other => other.Kind.ToString(),
        });
        Assert.Equal(tests.Length == 0 ? "cast _2" : "cast _2 -1", string.Join(' ', writes));
        Assert.DoesNotContain(body.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Intrinsic });
    }

    // SPEC 4.6.1, 4.6.3, 4.7.1 (DECISIONS R5-M154): a position of a Type parameter, whose range its instance decides, is tested in its own
    // Type (0 <= p) and, widened by a Proven Cast to u128, which holds every nonnegative value of every PrimitiveInteger Type, against
    // isize's maximum; a failing test gives -1, which the selection's one check rejects as it rejects any negative position (an element;
    // a written ^x, 0 <= x; a range, 0 <= start <= end). The tests read the key's value, which only the conversion moves (fail-before: a
    // located Unsupported_Kd at p).
    [Theory]
    [InlineData("return values[p]", "")]
    [InlineData("return values[conv<I>(b@move)]", "")]
    [InlineData("return values[^conv<I>(b@move)]", " Le(const 0_isize, copy _)")]
    [InlineData("var copy = values\n    copy[p] = 4\n    return copy[0]", "")]
    [InlineData("let rest = values[p..]\n    return rest[0]", " Le(const 0_isize, copy _) Le(copy _, copy _) Le(copy _, copy _)")]
    public void APositionOfATypeParameterIsTestedInItsOwnTypeAndU128(string statements, string selection)
    {
        var c = Compile("struct Box\n    public var n: i32 = 1\n    public init() => ()\nfunc conv<I>(b: Box) -> I\n    I is PrimitiveInteger\n" +
            $"    return 1\nfunc f<I>(values: [3 of i32], p: I) -> i32\n    I is PrimitiveInteger\n    let b = Box.init()\n    {statements}\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Subject.Module == 0);
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        var body = Regex.Replace(text.ToString()[text.ToString().IndexOf("fn f @", StringComparison.Ordinal)..], @"_\d+\b", "_");
        var tests = string.Join(' ', Regex.Matches(body, @"Le\([^)]*\)").Select(static x => x.Value));
        Assert.Equal("Le(const 0_I, copy _) Le(copy _, const 9223372036854775807_u128)" + selection, tests);
        Assert.Contains(" = copy _ as u128 (Integer) proven", body, StringComparison.Ordinal);
    }

    // SPEC 8.2.3, 23.1.2 (DECISIONS R5-M63, R5-M84; PLAN G168): a Field of a Place whose Type an identity premise makes a struct (`T is
    // Pair`) is a located Unsupported_Kd whose Opaque site names only the receiver's root, built already, so a root the receiver's index
    // moved is not used again there and A1 reports no MovedPlace_Kd at the member, while the independent ones stay: a later use of the
    // moved root, a use of a moved receiver (fail-before: the site named the member's syntax roots, `_1 _2`, and A1 added MovedPlace_Kd at
    // the member in c1 and c1Used). Each row: the parameters, the statements, the module-0 problems.
    [Theory]
    [InlineData("xs: ref/Array<T>, t: string", "return xs[pos(t@move)].a", "")]
    [InlineData("xs: ref/Array<T>, t: string", "let v = xs[pos(t@move)].a\n    use(t@move)\n    return v", "MovedPlace_Kd t")]
    [InlineData("s: T", "let m = s@move\n    return s.a", "MovedPlace_Kd s.a")]
    [InlineData("s: ref/T", "return s.a", "")]
    [InlineData("s: T", "return s.a", "")]
    public void AnUnsupportedFieldsSiteNamesItsReceiversRoot(string parameters, string statements, string problems)
    {
        var c = Compile("struct Pair\n    public var a: i32 = 1\n    public var b: i32 = 2\n    public init() => ()\n" +
            $"func pos(y: string) -> isize => 0\nfunc use(y: string) => ()\nfunc f<T>({parameters}) -> i32\n    T is Pair\n    {statements}\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var member = Assert.Single(program.Records, static x => x.Subject.Module == 0);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, ".a"), (member.Code, c.SyntaxOf(member.Subject).ToString()![^2..]));
        Assert.Equal(
            problems.Split('|', StringSplitOptions.RemoveEmptyEntries),
            program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0).Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}"));
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        var dump = text.ToString();
        var sites = Regex.Matches(dump[dump.IndexOf("fn f @", StringComparison.Ordinal)..], @"opaque \d+ @\d+: ([^\r\n]*)");
        Assert.Equal(["_1"], sites.Select(static x => x.Groups[1].Value));
    }

    // SPEC 8.2.3 (DECISIONS BU-72; PLAN G168b): a selection Binding resolved through an identity premise whose subject is the Type
    // parameter of its receiver's Place (an Array, a fixed array or a Tuple, also through a chain, at a position, ^x or a key) is one
    // located Unsupported_Kd at the selection on every pipeline, its Opaque site naming only the receiver's root, decided before anything
    // reads the receiver, with no projection or length the MIR validator rejects and no InternalInvariant_Kd (fail-before: a typeless
    // projection or Len, a validator finding and InternalInvariant_Kd beside Unsupported_Kd at the member, for ^x and a key at the
    // selection); a struct Field of such a Place stays Unsupported_Kd (chainTU). Each row: the Type parameters, the
    // premises, the parameters, the statement and the selection.
    [Theory]
    [InlineData("T", "T is Array<Pair>", "s: ref/T, t: string", "return s[pos(t@move)].a", "s[pos(t@move)]")]
    [InlineData("T", "T is (Pair, i32)", "s: ref/T", "return s.0.a", "s.0")]
    [InlineData("T", "T is [2 of Pair]", "s: ref/T", "return s[0].a", "s[0]")]
    [InlineData("T", "T is Array<Pair>", "s: ref/T", "return s[^1].a", "s[^1]")]
    [InlineData("T", "T is [2 of Pair]", "s: ref/T", "return s[^1].a", "s[^1]")]
    [InlineData("T", "T is Array<Pair>", "s: ref/T, k: FromEnd<isize>", "return s[k].a", "s[k]")]
    [InlineData("T, U", "T is U\n    U is Array<Pair>", "s: ref/T, t: string", "return s[pos(t@move)].a", "s[pos(t@move)]")]
    [InlineData("T, U", "T is U\n    U is Pair", "s: ref/T", "return s.a", "s.a")]
    public void AnIdentityPremisesSelectionIsOneUnsupported(string types, string premises, string parameters, string statement, string selection)
    {
        var source = "struct Pair\n    public var a: i32 = 1\n    public var b: i32 = 2\n    public init() => ()\nfunc pos(y: string) -> isize => 0\n" +
            $"func f<{types}>({parameters}) -> i32\n    {premises}\n    {statement}\npublic func main() => ()\n";
        foreach (var pipeline in (CompilerPipeline[])[CompilerPipeline.Legacy, CompilerPipeline.Mir])
        {
            var c = Compilation.CreateForTest();
            c.Pipeline = pipeline;
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
            c.Analyze();
            c.Binding.ReportDiagnostics();
            c.ReportAnalysis();
            var errors = TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error).Select(static x => (x.Code, x.Text));
            Assert.Equal([("Unsupported_Kd", selection)], errors);
        }

        var program = Compile(source).BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Subject.Module == 0);
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        var dump = text.ToString();
        var sites = Regex.Matches(dump[dump.IndexOf("fn f @", StringComparison.Ordinal)..], @"opaque \d+ @\d+: ([^\r\n]*)");
        Assert.Equal(["_1"], sites.Select(static x => x.Groups[1].Value));
    }

    // SPEC 4.6.3, 4.7.4, 22.5.1 (DECISIONS R5-M84): a written ^x that does not resolve is -1, so it fails only in its selection's one
    // check, after every boundary is evaluated, at the selection: the range's one Fail follows the later boundary's call (fail-before: ^x
    // failed where it was evaluated, at ^x, before next() was called).
    [Fact]
    public void AnUnresolvedFromEndOffsetFailsAtItsSelection()
    {
        var c = Compile("func next() -> isize => 2\nfunc f(values: ref/Array<i32>, x: i32) -> Slice<i32> during values => values[^x..next()]\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var fail = Assert.Single(body.Blocks.ToArray(), static x => x.Terminator.Callee is { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.Fail });
        Assert.Equal("values[^x..next()]", c.SyntaxOf(fail.Terminator.Source.Syntax).ToString());
        var statements = body.Statements.ToArray();
        var start = statements.Single(x => x.Rvalue is { Kind: MirRvalueKind.BinaryOp, Op: (byte)MirBinaryOp.Sub } &&
            c.SyntaxOf(x.Source.Syntax).ToString() == "^x").Place;
        Assert.Contains(statements, x => x.Place == start && x.Rvalue.Kind == MirRvalueKind.Use);
    }

    // SPEC 3.4.3, 4.7.1, 7.3, 13.4 (PLAN G147 (2b)): a receiver or a compared operand below an element storing an exclusive reference
    // is borrowed in place, through the selected call's reference and the stored one, never through a copy, so a call's write reaches
    // the element's referent and a compared string is neither copied out nor dropped.
    [Theory]
    [InlineData("Point", "entries[1].inner.mutate()", @"= &uniq two-phase \(\*\(\*_\d+\)\)\.0: uniq/Inner")]
    [InlineData("Point", "_ = entries[1].name == \"a\"", @"= &\(\*\(\*_\d+\)\)\.1: ref/string")]
    [InlineData("Point", "_ = entries[1].inner.name == \"a\"", @"= &\(\*\(\*_\d+\)\)\.0\.1: ref/string")]
    [InlineData("(string, i32)", "_ = entries[1].0 == \"a\"", @"= &\(\*\(\*_\d+\)\)\.0: ref/string")]
    public void APartBelowAStoredReferenceIsBorrowedInPlace(string element, string statement, string borrow)
    {
        var program = Compile("struct Inner\n    public var x: i32 = 0\n    public var name: string = \"\"\n    public init() => ()\n" +
            "    public func mutate(self: uniq/Self) => self.x = 7\nstruct Point\n    public var inner: Inner = Inner.init()\n" +
            "    public var name: string = \"\"\n    public init() => ()\n" + $"func f(value: uniq/{element} during a)\n" +
            $"    var entries: Dictionary<i32, uniq/{element} during a> = [1: value@move]\n    " + statement + "\n").BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        var body = text.ToString()[text.ToString().IndexOf("fn f @", StringComparison.Ordinal)..];
        Assert.Matches(borrow, body);
        Assert.DoesNotContain("copy (*(*", body, StringComparison.Ordinal);
        Assert.DoesNotContain("move (*(*", body, StringComparison.Ordinal);
    }

    // SPEC 4.7.1, 15.1.5 (DECISIONS BU-13): a user index below an inline part of a var root (a Tuple element, an Array element) selects
    // indexUniq, and the write goes through the exclusive reference it returns: no write or exclusive borrow follows a shared Deref.
    [Theory]
    [InlineData("var t = ([1: value@move], 0)\n    t.0[1].n = 5")]
    [InlineData("var vals: Array<Dictionary<i32, uniq/Point during a>> = [[1: value@move]]\n    vals[0][1].n = 5")]
    public void AUserIndexBelowAnInlinePartOfAVarRootSelectsIndexUniq(string statements)
    {
        var program = Compile("struct Point\n    public var n: i32 = 0\n    public init() => ()\nfunc f(value: uniq/Point during a)\n    " + statements + "\n").BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        var dump = text.ToString()[text.ToString().IndexOf("fn f @", StringComparison.Ordinal)..];
        Assert.Contains("= indexUniq(", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("= index(", dump, StringComparison.Ordinal);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        foreach (var statement in body.Statements)
        {
            Assert.False(statement.Kind == MirStatementKind.Assign && BelowSharedDeref(body, statement.Place), dump);
            var exclusive = statement.Rvalue is { Kind: MirRvalueKind.Ref, Op: (byte)MirRefKind.Uniq or (byte)MirRefKind.TwoPhaseUniq };
            Assert.False(exclusive && BelowSharedDeref(body, statement.Rvalue.Place), dump);
        }
    }

    // SPEC 3.4, 13.7: Binding designates a Property target through its parentheses, so an update or assignment of a parenthesized
    // Property builds as the unparenthesized one does (its syntax ids aside), whichever of its accessors is custom.
    [Theory]
    [InlineData("        set(self: uniq/Self, value: i32) -> () => storage = value", "s.item += 1")]
    [InlineData("        get(self: ref/Self) -> i32 => storage\n        set(self: uniq/Self, value: i32) -> () => storage = value", "s.item += 1")]
    [InlineData("        set(self: uniq/Self, value: i32) -> () => storage = value", "s.item = 2")]
    [InlineData("        get(self: ref/Self) -> i32 => storage + 1", "s.item++")]
    public void AParenthesizedPropertyTargetIsItsProperty(string accessors, string statement)
    {
        Assert.Equal(Built(statement), Built(statement.Replace("s.item", "(s.item)")));

        string Built(string written)
        {
            var program = Compile($"struct S\n    public var item: i32 = 0\n{accessors}\n\nfunc f(s: uniq/S)\n    {written}\n").BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records);
            var text = new StringBuilder();
            MirDump.Write(program, 0, text);
            return string.Join('\n', text.ToString().Split('\n').Select(static x => x.Split(" @")[0]));
        }
    }

    // SPEC 13.7.2: an in-place compound update reserves its located destination (TwoPhaseUniq) and reads and writes it through that
    // reservation, so the write activates it, also where its right side does not complete, in the code only the checks reach (SPEC
    // 14.10.3; DECISIONS R4-M62).
    [Fact]
    public void CompoundUpdatesWriteThroughTheirReservation()
    {
        var body = Body("func f() -> i32\n    var x: i32 = 1\n    x += 2\n    var t: (i32, i32) = (1, 2)\n    t.0 *= 3\n    x += (return 1)\n", "f");
        var statements = body.Statements.ToArray();
        var reservations = statements.Where(static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref })
            .Select(static x => ((MirRefKind)x.Rvalue.Op, x.Place.Root)).ToArray();
        Assert.Equal(3, reservations.Length);
        foreach (var (kind, root) in reservations)
        {
            Assert.Equal(MirRefKind.TwoPhaseUniq, kind);
            Assert.Contains(statements, x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.BinaryOp } && x.Place.Root == root &&
                body.Projections[x.Place.Projection].Kind == MirProjectionKind.Deref);
        }
    }

    // SPEC 14.8.3: a guard candidate's guard-local Origin ends at the guard's cleanup on every path, a transfer out of the guard
    // included: each StorageDead of a candidate follows its EndBorrow.
    [Fact]
    public void GuardCandidatesEndAtEveryCleanup()
    {
        var body = Body(
            "func f(o: Option<i32>) -> i32\n    match o\n        .Some(let v) if v@follow > 3 or (return 9) => return v\n        _ => return 0\n",
            "f");
        var candidates = Enumerable.Range(0, body.Locals.Length).Where(x => body.Locals[x].Role == MirLocalRole.GuardCandidate).ToArray();
        Assert.NotEmpty(candidates);
        var (ends, deaths) = (0, 0);
        foreach (var block in body.Blocks.ToArray())
        {
            var statements = body.Statements.Slice(block.Start, block.Count).ToArray();
            for (var i = 0; i < statements.Length; i++)
            {
                if (statements[i] is { Kind: MirStatementKind.StorageDead } death && candidates.Contains(death.Place.Root))
                {
                    deaths++;
                    Assert.True(i > 0 && statements[i - 1] is { Kind: MirStatementKind.EndBorrow } end && end.Place == death.Place);
                }

                ends += statements[i].Kind == MirStatementKind.EndBorrow && candidates.Contains(statements[i].Place.Root) ? 1 : 0;
            }
        }

        Assert.True(deaths >= 2 && ends == deaths, $"{ends} EndBorrows, {deaths} StorageDeads");
    }

    // SPEC 15.1.5, 15.6.3, 14.9.2 (DECISIONS BU-27): a stored exclusive reference Reborrowed in its own mode, as an argument, a receiver, a
    // Subject or the source of an `if`, `do` or `match` result, is a Ref of the Type its consumer expects, Binding's name for the Reborrow:
    // the meet of the stored layer and the parameter layer that reaches it, never the stored Type alone (a result's common Type is the
    // named Type its sources supply).
    [Theory]
    [InlineData("func f(holder: uniq/(Holder during a) during h)\n    change(holder.link)\n    holder.counter.bump()\n    match holder.o\n        .Some(let v) => ()\n        .None => ()\n", 3)]
    [InlineData("func f(holder: uniq/(Holder during a) during h, c: bool) -> i32\n    let r = if c => holder.link else => holder.link\n    return r@follow\n", 2)]
    [InlineData("func f(holder: uniq/(Holder during a) during h) -> i32\n    let r = label l: do\n        exit to l holder.link\n    return r@follow\n", 1)]
    [InlineData("func f(holder: uniq/(Holder during a) during h, o: Option<i32>) -> i32\n    let r = match o\n        .Some(_) => holder.link\n        .None => holder.link\n    return r@follow\n", 2)]
    public void ABareReborrowIsARefOfItsNamedType(string body, int reborrows)
    {
        const string Declarations = "func change(value: uniq/i32) => value@follow = 1\nstruct Counter\n    var count: i32\n    public init() => self.count = 0\n" +
            "    public func bump(self: uniq/Self) => self.count = self.count + 1\nstruct Holder {a}\n    public var link: uniq/i32 during a\n" +
            "    public var counter: uniq/Counter during a\n    public var o: uniq/Option<i32> during a\n";
        var program = Compile(Declarations + body + "public func main() => ()\n").BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        var f = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var refs = f.Statements.ToArray().Where(static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref }).Select(static x => x.Rvalue.Type!)
            .Where(static x => x.Semantics == SemanticsKind.Uniq).ToArray();
        Assert.True(refs.Length >= reborrows);
        Assert.All(refs, static x => Assert.Equal(OriginKind.Intersection, x.Origin?.Kind));
    }

    // SPEC 13.6.1, 15.1.5 (DECISIONS BU-27): an object upcast of a stored exclusive reference to a view of its own Semantics Reborrows it,
    // a Ref of its payload in that Semantics of the Origin Binding names for the upcast's borrow (a path's meet through an exclusive
    // parameter), which the Upcast then changes to the view; a view of another Semantics borrows it in the view's mode (fail-before: an
    // InternalInvariant_Kd at the Reborrowed operand).
    [Theory]
    [InlineData("func f()\n    var owner = make()\n    let child = owner@objuniq/Leaf\n    exclusive(child@objuniq/Base)\n", SemanticsKind.ObjUniq, false)]
    [InlineData("func f(holder: uniq/(Holder during a) during h)\n    exclusive(holder.leaf@objuniq/Base)\n", SemanticsKind.ObjUniq, true)]
    [InlineData("func f()\n    var owner = make()\n    let child = owner@objuniq/Leaf\n    shared(child@objref/Base)\n", SemanticsKind.ObjRef, false)]
    public void AnUpcastsOperandIsARefOfItsPayloadInTheView(string body, SemanticsKind view, bool met)
    {
        const string Declarations = "open struct Base\n    public let value: i32\n    protected init(value: i32) => self.value = value\nstruct Leaf: Base\n" +
            "    public init(): base(10) => ()\nstruct Holder {a}\n    public var leaf: objuniq/Leaf during a\nfunc shared(view: objref/Base) => ()\n" +
            "func exclusive(view: objuniq/Base) => ()\nfunc make() -> obj/Leaf => Kimi.Intrinsics.makeObj(Leaf.init())\n";
        var program = Compile(Declarations + body + "public func main() => ()\n").BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var f = program.Bodies.ToArray().Single(x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var statements = f.Statements.ToArray();
        var cast = Assert.Single(statements, static x => x is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Cast, Op: (byte)MirCastKind.Upcast } }).Rvalue;
        var operand = f.Operands[cast.Start];
        var reference = Assert.Single(statements, x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Ref } && x.Place == operand.Place).Rvalue.Type!;
        Assert.Equal((view, "Leaf"), (reference.Semantics, reference.Components[0].Symbol?.Name));
        Assert.Equal((view, "Base"), (cast.Type!.Semantics, cast.Type.Components[0].Symbol?.Name));
        Assert.True(!met || reference.Origin?.Kind == OriginKind.Intersection, $"{reference.Origin?.Kind}");
    }

    // SPEC 23.1.2 (DECISIONS BU-27): a jump Binding rejects (no target, an unknown label, a yield outside a construct, an exit across a
    // Closure) is derived; no consumer takes its operand, so a stored exclusive reference there is designated, never Reborrowed, and the
    // build adds no record of its own to Binding's.
    [Theory]
    [InlineData("func f(r: uniq/i32)\n    exit r\n")]
    [InlineData("func f(r: uniq/i32) -> i32\n    while true\n        exit to nowhere r\n    return 0\n")]
    [InlineData("func f(r: uniq/i32)\n    yield r\n")]
    [InlineData("struct H {a}\n    public var link: uniq/i32 during a\nfunc f(h: uniq/(H during a) during b)\n    exit h.link\n")]
    [InlineData("func f(r: uniq/i32) -> i32\n    let g = func () -> i32\n        exit to outer r\n    label outer: while true\n        return g()\n    return 0\n")]
    [InlineData("func f(r: uniq/i32) -> uniq/i32 during r\n    exit r\n")]
    public void ARejectedJumpsOperandIsNotAcquired(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source + "public func main() => ()\n"));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        var codes = TestDiagnostics.Of(c).Select(static x => x.Code).ToArray();
        Assert.Contains(nameof(DiagnosticCode.InvalidJumpTarget_Kd), codes);
        Assert.DoesNotContain(nameof(DiagnosticCode.InternalInvariant_Kd), codes);
    }

    // SPEC 15.6.2: whether a Place is reached through a Deref of a shared reference or view, which grants Read only.
    private static bool BelowSharedDeref(MirBody body, MirPlace place)
    {
        for (var id = place.Projection; id != 0; id = body.Projections[id].Prefix)
        {
            if (body.Projections[id].Kind == MirProjectionKind.Deref &&
                body.TypeOf(place with { Projection = body.Projections[id].Prefix }) is { Semantics: SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc })
            {
                return true;
            }
        }

        return false;
    }
}
