// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class ComparisonCompositionTest
{
    [Theory]
    [InlineData("T, f64", "UnprovenConstraint_Kd", "T")]
    [InlineData("f64, T", "UnprovenConstraint_Kd", "T")]
    [InlineData("T, bool", "UnsatisfiedConstraint_Kd", "bool")]
    [InlineData("bool, T", "UnsatisfiedConstraint_Kd", "bool")]
    public void CompositeFailureKeepsTheDecisiveQueriedLeaf(string elements, string code, string subject)
    {
        var source = $"func before<T>(left: ({elements}), right: ({elements})) -> bool => left < right\npublic func main() => ()";
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.False(compilation.Bind().IsComplete);
        compilation.Binding.ReportDiagnostics();
        var error = Assert.Single(compilation.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(code == nameof(DiagnosticCode.UnprovenConstraint_Kd) ? DiagnosticCategory.Proof : DiagnosticCategory.Language, error.Category);
        Assert.Equal(["subject", "member", "constraint"], error.Reason!.Select(static x => x.Name));
        Assert.Equal([subject, "<", $"{subject} is Comparable"], error.Reason!.Select(static x => x.Value));
        var verdict = code == nameof(DiagnosticCode.UnprovenConstraint_Kd) ? "not proven" : "not satisfied";
        Assert.Equal($"Required condition is {verdict}: {subject} is Comparable", error.Note);
        Assert.Equal("left < right", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Theory]
    [InlineData("U, bool, Broken<i32>", 0, 1, 2)]
    [InlineData("Broken<i32>, U, bool", 1, 2, 0)]
    [InlineData("U, Broken<i32>, bool", 0, 2, 1)]
    public void AnInvalidQueriedLeafDominatesUnknownAndRefuted(string elements, int unknown, int refuted, int invalid)
    {
        var source = $$"""
            struct Broken<T>
                Self is Comparable when T is Missing
                public func equals(self: ref/Self, other: ref/Self) -> bool => true
                public func compare(self: ref/Self, other: ref/Self) -> i32 => 0
            func before<U>(left: ({{elements}}), right: ({{elements}})) -> bool => left < right
            public func main() => ()
            """;
        var compilation = CompilationTestHelper.ParseSuccess(source);
        Assert.False(compilation.Bind().IsComplete);
        var function = KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "before");
        var tuple = function.Parameters[0].Type.TypeOf()!;
        var comparable = compilation.Library.GetSymbol(KimiDeclarationId.Comparable)!;
        Assert.Equal(ConstraintProof.Unknown, compilation.Binding.ResolveConformance(tuple.Components[unknown], comparable, function, out _));
        Assert.Equal(ConstraintProof.Refuted, compilation.Binding.ResolveConformance(tuple.Components[refuted], comparable, function, out _));
        Assert.Equal(ConstraintProof.Error, compilation.Binding.ResolveConformance(tuple.Components[invalid], comparable, function, out _));
        Assert.Equal(ConstraintProof.Error, compilation.Binding.ResolveConformance(tuple, comparable, function, out _));
        var issue = Assert.Single(compilation.Binding.Issues, static x => x.Node is BinaryKoto { Akind: KotoKind.LessThan });
        Assert.Equal(DiagnosticCode.InvalidConstraint_Kd, issue.Code);
        compilation.Binding.ReportDiagnostics();
        var errors = compilation.Diagnostics.Finalize(rejected: true).Diagnostics;
        var error = Assert.Single(errors, x => x.Span is { } span && source.Substring(span.Start, span.Length) == "left < right");
        Assert.Equal(nameof(DiagnosticCode.InvalidConstraint_Kd), error.Code);
        Assert.Null(error.Reason);
    }

    // G308's native forms: say prints a bool; K's witnesses print their operands, e (equals) or c (compare) and the two values; pair builds
    // the DAG ((K, K), (K, K)).
    private const string Say = "func say(value: bool)\n    match value\n        true => Console.writeLine(\"true\")\n" +
        "        false => Console.writeLine(\"false\")\n";

    private const string Printing = """
        struct K
            Self is Comparable
            public let v: i32
            public init(v: i32) => self.v = v
            public func equals(self: ref/Self, other: ref/Self) -> bool
                Console.writeLine("e\(self.v)\(other.v)")
                return self.v == other.v
            public func compare(self: ref/Self, other: ref/Self) -> i32
                Console.writeLine("c\(self.v)\(other.v)")
                return self.v - other.v
        func pair(a: i32, b: i32, c: i32, d: i32) -> ((K, K), (K, K)) => ((K.init(a), K.init(b)), (K.init(c), K.init(d)))

        """ + Say;

    // G308 N2: K values with the ids their drops print, in two DAG Tuples compared, then used.
    private const string KeptSource = """
        func make(v: i32, id: i32) -> K
            var k = K.init(v)
            k.id = id
            return k@move
        func run()
            let a = ((make(1, 1), 2, "x"), (make(3, 2), 4, "y"))
            let b = ((make(1, 3), 2, "x"), (make(3, 4), 4, "z"))
            say(a == b)
            say(a < b)
            say(a.1.2 == "y" and b.1.2 == "z" and a.0.0.id == 1 and b.1.0.id == 4)
            Console.writeLine("end")
        run()
        """;

    // G308 N3: a generic equality and inequality over (T, f64) and the written (f64, f64), with a NaN at either leaf.
    private const string GenericFloatingSource = """
        func same<T>(a: (T, f64), b: (T, f64)) -> bool
            T is Equatable
            return a == b
        func differ<T>(a: (T, f64), b: (T, f64)) -> bool
            T is Equatable
            return a != b
        let nan: f64 = 0.0 / 0.0
        say(same((nan, 1.0), (nan, 1.0)))
        say(differ((nan, 1.0), (nan, 1.0)))
        say(same((1.0, nan), (1.0, nan)))
        say(differ((1.0, nan), (1.0, nan)))
        let p = (nan, 1.0)
        let q = (1.0, nan)
        say(p == p)
        say(p != p)
        say(q == q)
        say(q != q)
        """;

    // G308 N4: the four generic orderings over (T, f64), and a written ordering of a signed zero.
    private const string GenericOrderingSource = """
        func lt<T>(a: (T, f64), b: (T, f64)) -> bool
            T is Comparable
            return a < b
        func le<T>(a: (T, f64), b: (T, f64)) -> bool
            T is Comparable
            return a <= b
        func gt<T>(a: (T, f64), b: (T, f64)) -> bool
            T is Comparable
            return a > b
        func ge<T>(a: (T, f64), b: (T, f64)) -> bool
            T is Comparable
            return a >= b
        let nan: f64 = 0.0 / 0.0
        say(lt((1, nan), (1, 2.0)))
        say(le((1, nan), (1, 2.0)))
        say(gt((1, nan), (1, 2.0)))
        say(ge((1, nan), (1, 2.0)))
        say((-0.0, 1.0) < (0.0, 2.0))
        """;

    // A generic struct whose witness compares a tuple of its own borrowed instances.
    private const string RecursiveSource = """
        struct Key<T>
            Self is Equatable
            T is Copy
            public let value: T
            public let remaining: i32
            public init(value: T, remaining: i32)
                self.value = value
                self.remaining = remaining
            public func equals(self: ref/Self, other: ref/Self) -> bool
                if self.remaining == 0 => return other.remaining == 0
                if other.remaining == 0 => return false
                let next = Key<T>.init(self.value, self.remaining - 1)
                let peer = Key<T>.init(other.value, other.remaining - 1)
                return (next@ref, 0) == (peer@ref, 0)
        let first = Key<i32>.init(1, 3)
        let last = Key<i32>.init(2, 3)
        require (first@ref, 0) == (last@ref, 0) else => $abort("recursive witness")
        """;

    private const string FloatingSource = """
        let nan: f64 = 0.0 / 0.0
        let a = (nan, 1)
        let b = (nan, 2)
        require not (a == b) and a != b else => $abort("equality")
        require not (a < b) and not (a <= b) and not (a > b) and not (a >= b) else => $abort("unordered")
        require (1, nan) < (2, nan) and (2, nan) >= (1, nan) else => $abort("short circuit")
        require (0.0, 1) == (-0.0, 1) and (0.0, 1) <= (-0.0, 1) else => $abort("zero")
        """;

    private const string ReferenceSource = """
        struct Key
            Self is Equatable
            public let number: i32
            public init(number: i32) => self.number = number
            public func equals(self: ref/Self, other: ref/Self) -> bool => self.number == other.number
        func equal<T>(left: T, right: T) -> bool
            T is Copy and Equatable
            return left == right
        let first = Key.init(1)
        let last = Key.init(1)
        require equal(first@ref, last@ref) else => $abort("reference witness")
        """;

    // Tuple comparisons over a closed witness and a nested one, as operators and through the Contract, over witnesses of conditional
    // conformances (one of a Type with an Origin slot, one nested in itself) and in generic functions over a Type parameter.
    private const string ViewSource = """
        struct Key
            Self is Comparable
            public let value: i32
            public init(value: i32) => self.value = value
            public func equals(self: ref/Self, other: ref/Self) -> bool => self.value == other.value
            public func compare(self: ref/Self, other: ref/Self) -> i32 => self.value - other.value
        struct Wrap<V>
            Self is Equatable
            public init() => ()
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
        struct Held<T> {a}
            Self is Equatable when T is Copy
            public var value: ref/T during a
            public init(value: ref/T during a) => self.value = value
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
        struct Box<T>
            Self is Comparable when T is Comparable
            public let value: T
            public init(value: T) => self.value = value
            public func equals(self: ref/Self, other: ref/Self) -> bool => true
            public func compare(self: ref/Self, other: ref/Self) -> i32 => 0
        func same<T>(left: T, right: T) -> bool
            T is Copy and Equatable
            return (left, 1.0) == (right, 1.0)
        func before<T>(left: T, right: T) -> bool
            T is Copy and Comparable
            return (left, 1.0) < (right, 2.0)
        let a = Key.init(1)
        let b = Key.init(2)
        let w = Wrap<i32>.init()
        let pair = (2.5, a@ref)
        let seven = 7
        let held = Held<i32>.init(seven@ref)
        let box = Box<Box<Key>>.init(Box<Key>.init(Key.init(3)))
        require (a@ref, 0.5) < (b@ref, 0.5) and (1, (2.0, a@ref)) != (1, (2.0, b@ref)) and pair.equals(pair@ref) else => $abort("closed")
        require (held@ref, 1) == (held@ref, 1) and (box@ref, 0.5) <= (box@ref, 1.5) else => $abort("conditional")
        require same(3, 3) and before(4, 5) else => $abort("generic")
        """;

    // SPEC 13.4, 13.4.1, 12.4.3 (DECISIONS R5-M126, plan C8f's NaN and ±0 tuple fixture): the Contract's mapping on Tuples through a
    // generic comparison, which MIR code generation runs as a Builtin shim: an equality NaN-reflexive whatever the NaN's payload and sign,
    // both zeros equal, a later part deciding, nested Tuples, a ref layer and f32, while the built-in operators on the same Tuples stay IEEE
    // (FloatingSource); a part that is not equal decides, so a later part's witness is not called (its Abort would end the run); a compare
    // over integer, string and char parts; a Dictionary's key equality, the same mapping (SPEC 12.4.3), keeping one entry for NaNs of either
    // sign and one for both zeros.
    private const string ContractFloatingSource = """
        struct Probe
            Self is Equatable
            public let number: i32
            public init(number: i32) => self.number = number
            public func equals(self: ref/Self, other: ref/Self) -> bool
                require self.number >= 0 else => $abort("a decided comparison called a later witness")
                return self.number == other.number
        func equal<T>(left: ref/T, right: ref/T) -> bool
            T is Equatable
            return left == right
        func same<T>(left: ref/T, right: ref/T) -> bool
            T is Equatable
            return left.equals(right)
        func order<T>(left: ref/T, right: ref/T) -> i32
            T is Comparable
            return left.compare(right)
        let nan: f64 = 0.0 / 0.0
        let other: f64 = -nan
        let zero: f64 = 0.0
        let negativeZero: f64 = -0.0
        let a = (nan, 1)
        let b = (other, 1)
        let c = (nan, 2)
        require equal(a@ref, b@ref) and same(a@ref, b@ref) and not equal(a@ref, c@ref) else => $abort("NaN tuple")
        require not (a == b) and a != b else => $abort("IEEE tuple")
        let z = (zero, 1)
        let n = (negativeZero, 1)
        let d = (1.0, 1)
        require equal(z@ref, n@ref) and z == n and not equal(z@ref, d@ref) and not equal(a@ref, z@ref) else => $abort("zero tuple")
        let nested = ((nan, 1), zero)
        let alike = ((other, 1), negativeZero)
        require equal(nested@ref, alike@ref) and not (nested == alike) else => $abort("nested tuple")
        let small: f32 = 0.0 / 0.0
        let pair = (small, -small)
        let flipped = (-small, small)
        require equal(pair@ref, flipped@ref) else => $abort("f32 tuple")
        let x = (nan@ref, 3)
        let y = (other@ref, 3)
        require equal(x@ref, y@ref) else => $abort("ref layer")
        let first = (Probe.init(1), Probe.init(-1))
        let second = (Probe.init(2), Probe.init(-1))
        require not equal(first@ref, second@ref) else => $abort("early exit")
        let low = (1, "b", 'c')
        let high = (1, "b", 'd')
        require order(low@ref, high@ref) < 0 and order(high@ref, low@ref) > 0 and order(low@ref, low@ref) == 0 else => $abort("order")
        var keys: Dictionary<(f64, i32), i32> = [:]
        var added = 0
        match keys.tryInsert((nan, 1), 10)
            .Ok(_) => added += 1
            .Err(_) => ()
        match keys.tryInsert((other, 1), 20)
            .Ok(_) => added += 1
            .Err(_) => ()
        match keys.tryInsert((negativeZero, 2), 30)
            .Ok(_) => added += 1
            .Err(_) => ()
        let hit = (zero, 2)
        let found = (other, 1)
        require added == 2 and keys.length == 2 and keys[hit] == 30 and keys[found] == 10 else => $abort("Dictionary keys")
        Console.writeLine("Contract mapping on tuples kept.")
        """;

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RecursiveGenericWitnessCompositionReusesThePreparedHelper()
    {
        var c = MinimalEmissionTest.Analyze(RecursiveSource);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        NativeAllocationAudit.WriteFixture("ComparisonCompositionRecursive", RecursiveSource, 0, 0, 0);
    }

    [Fact]
    public void MilestoneComposesBorrowedAndTupleWitnesses()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/milestones/Milestone25.kimi"));
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        const string expected = "User comparisons keep their operands.\nTuple comparisons compose witnesses.\nFloating Contract equality is NaN-reflexive.\nComparison contracts finished.\n";
        ScalarEmissionTest.EmitFixture("ComparisonCompositionMilestone25", source, expected);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void FloatingTupleOperatorsPreservePartialOrderingAndShortCircuit()
        => NativeAllocationAudit.WriteFixture("ComparisonCompositionFloating", FloatingSource, 0, 0, 0);

    [Fact]
    public void ContractMappingOnFloatingTuplesIsNaNReflexive()
        => ScalarEmissionTest.EmitFixture("ComparisonCompositionContractFloating", ContractFloatingSource, "Contract mapping on tuples kept.\n");

    [Theory]
    [InlineData("let x = (1, true) < (1, false)")]
    [InlineData("let x = (1, 2) == (true, 2)")]
    [InlineData("let a = (0.0, 1)\nlet b = (1.0, 2)\nlet invalid = a.compare(b@ref)")]
    public void CompositionDoesNotInventMissingCapabilitiesOrCoreConversions(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.WriteIr(TextWriter.Null, out _));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceValuesComposeThroughByValueGenericParameters(bool match)
    {
        var source = match
            ? ReferenceSource.Replace("return left == right", "return match true\n        true => left == right\n        false => false", StringComparison.Ordinal)
            : ReferenceSource;
        NativeAllocationAudit.WriteFixture("ComparisonCompositionReferenceValues" + match, source, 0, 0, 0);
    }

    // G308 N1, SPEC 13.4, 13.4.1: over the DAG ((K, K), (K, K)), ==, !=, < and >= deciding at the first, the third and the last leaf run
    // K's witnesses, which print their operands, left to right up to the deciding leaf, and give SPEC 13.4's results.
    [Fact]
    public void ADagComparisonRunsItsWitnessesUpToTheDecidingLeaf()
    {
        var source = new System.Text.StringBuilder(Printing + "func run()\n    let a = pair(1, 2, 3, 4)\n    let first = pair(9, 2, 3, 4)\n" +
            "    let third = pair(1, 2, 9, 4)\n    let last = pair(1, 2, 3, 9)\n");
        var expected = new System.Text.StringBuilder();
        foreach (var (op, witness, decided) in new[] { ("==", 'e', "false"), ("!=", 'e', "true"), ("<", 'c', "true"), (">=", 'c', "false") })
        {
            foreach (var (name, calls) in new[] { ("first", "19"), ("third", "11 22 39"), ("last", "11 22 33 49") })
            {
                source.Append($"    say(a {op} {name})\n");
                expected.Append(string.Concat(calls.Split(' ').Select(x => $"{witness}{x}\n"))).Append(decided).Append('\n');
            }
        }

        ScalarEmissionTest.EmitFixture("ComparisonDagWitnessOrder", source.Append("run()\n").ToString(), expected.ToString());
    }

    // G308 N2, SPEC 13.4: comparing DAG Tuples with a K leaf that has drop, a Copy and a Non-Copy leaf neither moves nor destroys either
    // operand: both are used after the comparisons and each K is dropped once at the end of their scope.
    [Fact]
    public void ADagComparisonKeepsItsOperands()
    {
        const string Init = "    public init(v: i32) => self.v = v\n";
        const string Dropped = "    public var id: i32 = 0\n" + Init + "    drop => Console.writeLine(\"d\\(self.id)\")\n";
        var source = Printing.Replace(Init, Dropped, StringComparison.Ordinal) + KeptSource;
        ScalarEmissionTest.EmitFixture("ComparisonDagOperandsKept", source, "e11\ne33\nfalse\nc11\nc33\ntrue\ntrue\nend\nd4\nd3\nd2\nd1\n");
    }

    // G308 N3, SPEC 13.4: in a generic (T, f64) at T = f64, a NaN at the T leaf compares by T's NaN-reflexive Contract mapping (equal), one at
    // the f64 leaf by IEEE (unequal); the written (f64, f64) of the same values compares IEEE at both leaves.
    [Fact]
    public void AGenericLeafKeepsItsContractMappingBesideAWrittenTuple()
        => ScalarEmissionTest.EmitFixture("ComparisonDagGenericFloating", Say + GenericFloatingSource, "true\nfalse\nfalse\ntrue\nfalse\ntrue\nfalse\ntrue\n");

    // G308 N4, SPEC 13.4: a generic ordering of (T, f64) at T = i32 whose f64 pair is unordered is false under <, <=, > and >=;
    // (-0.0, 1.0) < (0.0, 2.0) is true, -0.0 == 0.0.
    [Fact]
    public void AGenericOrderingOfAnUnorderedPairIsFalse()
        => ScalarEmissionTest.EmitFixture("ComparisonDagGenericOrdering", Say + GenericOrderingSource, "false\nfalse\nfalse\nfalse\ntrue\n");

    // G308 N5, SPEC 13.4: S1's equality and S2's orderings over P_6's Tuple (MirBuildTest.Dag, 64 leaves) with a distinct value at the first,
    // a middle (the 33rd) and the last leaf.
    [Fact]
    public void ADagComparisonDecidesAtAnyLeaf()
    {
        var operators = string.Concat(new[] { ("eq", "=="), ("lt", "<"), ("ge", ">=") }.Select(static x =>
            MirBuildTest.DagOperator.Replace("f(", x.Item1 + "(", StringComparison.Ordinal).Replace("==", x.Item2, StringComparison.Ordinal)));
        var source = MirBuildTest.Dag(6, operators) + Say + """
            func check(a: ref/D1<i32>, b: ref/D1<i32>)
                say(eq(a, b))
                say(lt(a, b))
                say(ge(a, b))
            func run()
                let a = D1<i32>.init(0)
                var first = D1<i32>.init(0)
                first.x.x.x.x.x.x.0.0.0.0.0.0 = 1
                var middle = D1<i32>.init(0)
                middle.x.x.x.x.x.x.1.0.0.0.0.0 = 1
                var last = D1<i32>.init(0)
                last.x.x.x.x.x.x.1.1.1.1.1.1 = 1
                check(a, a)
                check(a, first)
                check(a, middle)
                check(a, last)
                check(last, a)
            run()
            """;
        var expected = "true\nfalse\ntrue\n" + string.Concat(Enumerable.Repeat("false\ntrue\nfalse\n", 3)) + "false\nfalse\ntrue\n";
        ScalarEmissionTest.EmitFixture("ComparisonDagLeaves", source, expected);
    }

    // DECISIONS R5-M68 (F package 4.6, 4.7): every comparison of the library, the milestones and this class's programs has, under its
    // Contract, Self and mode, the legacy plan's leaves for a closed Self (each has a plan: an inherited witness, which the plan refuses,
    // implements no comparison requirement, whose `other` is `ref/Self`); for a Self with an abstract part, which has no plan, the view's
    // leaves answer: the requirement on each leaf with an abstract part. The views leave Binding's records and the HIR version unchanged.
    [Fact]
    public void TheViewHasTheLegacyPlansLeaves()
    {
        var problems = new List<string>();
        var corpus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            Twin(c, module, name, corpus, problems);
        }

        var direct = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in new[] { RecursiveSource, FloatingSource, ReferenceSource, ViewSource })
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
            Twin(c, c.Kotonoha, source[..source.IndexOf('\n')], direct, problems);
        }

        var counts = $"corpus {Counts(corpus)}; direct {Counts(direct)}";
        Assert.True(
            problems.Count == 0 && Has(corpus, "Builtin", "Composite", "Witness") && Has(direct, "Builtin", "Composite", "Requirement", "Witness"),
            $"{counts}; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        static string Counts(SortedDictionary<string, int> kinds) => string.Join(", ", kinds.Select(static x => $"{x.Key} {x.Value}"));

        static bool Has(SortedDictionary<string, int> kinds, params string[] names) => names.All(kinds.ContainsKey);
    }

    // SPEC 13.4, 13.4.1: tuples and `ref` layers compose elementwise, built-in Types natively, a closed leaf by its witness under its declaring
    // instance (a conditional conformance's too), a leaf with an abstract part by the Contract's requirement on it; each mode and key has its own
    // node, asked again the same one.
    // A leaf whose storage nests past Binding's depth bound makes the comparison a Limit (null), its shallow form not.
    [Fact]
    public void AComparisonComposesItsLeaves()
    {
        var c = MinimalEmissionTest.Analyze(ViewSource);
        var shown = Comparisons(c.Kotonoha).Where(static x => x.ConformingType!.Kind == BoundTypeKind.Tuple).Select(x => Show(View(c, x)!));
        string[] expected = [
            "(T.equals, f64)", "(T.compare, f64)", "((Key.compare @Key), f64)", "(i32, (f64, (Key.equals @Key)))", "(f64, (Key.equals @Key))",
            "((Held.equals @Held<i32>), i32)", "((Box.compare @Box<Box<Key>>), f64)"];
        Assert.Equal(expected, shown);

        var equatable = c.Library.GetSymbol(KimiDeclarationId.Equatable)!;
        var pair = c.Types.Tuple([BoundType.F64, BoundType.I32]);
        Assert.NotSame(c.Types.Comparison(equatable, pair, operators: true), c.Types.Comparison(equatable, pair, operators: false));
        Assert.Same(c.Types.Comparison(equatable, pair, operators: false), c.Types.Comparison(equatable, pair, operators: false));

        var wrap = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Symbol.Name: "Wrap" })!;
        var deep = BoundType.I32;
        for (var i = 0; i < 80; i++)
        {
            deep = c.Types.Intern(wrap.Kind, wrap.Symbol, wrap.Semantics, [deep]);
        }

        Assert.Null(c.Types.Comparison(equatable, c.Types.Tuple([deep, BoundType.I32]), operators: true));
        var shallow = c.Types.Intern(wrap.Kind, wrap.Symbol, wrap.Semantics, [BoundType.I32]);
        Assert.Equal("(Wrap.equals @Wrap<i32>, i32)", Show(c.Types.Comparison(equatable, c.Types.Tuple([shallow, BoundType.I32]), operators: true)!));
    }

    // An edit discards the view's nodes with the other views' memos (TyCtxt.ResetViews): replacing Key's witness between passes gives the same
    // comparison key, whose leaf then names the new witness.
    [Fact]
    public void AnEditRecomputesTheComparison()
    {
        var c = MinimalEmissionTest.Analyze(ViewSource);
        var call = Comparisons(c.Kotonoha).First(static x => x.ConformingType is { Kind: BoundTypeKind.Tuple, HasAbstractPart: false });
        var before = View(c, call)!.Parts[0].Parts[0];
        var self = call.ConformingType;
        var donor = MinimalEmissionTest.Analyze(ViewSource.Replace("self.value - other.value", "other.value - self.value", StringComparison.Ordinal));
        var key = Container(c, "Key");
        Assert.True(KotoHelper.Replace(key, Compare(key), ParseTestHelper.Detach(Compare(Container(donor, "Key")), new CodeBlockKoto(key.CodeContext))));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        call = Comparisons(c.Kotonoha).First(x => ReferenceEquals(x.ConformingType, self));
        var after = View(c, call)!.Parts[0].Parts[0];
        Assert.Equal("Key.compare @Key", Show(after));
        Assert.NotSame(before.Target, after.Target);
        Assert.Same(Compare(key).SymbolOf(), after.Target);

        static DeclarationContainerKoto Container(Compilation c, string name) => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);

        static FunctionKoto Compare(DeclarationContainerKoto key) => key.Members.OfType<FunctionKoto>().Single(static x => x.Name == "compare");
    }

    // A warm rebind gives every comparison of ViewSource the same node, and asking again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmComparisonIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(ViewSource);
        var calls = Comparisons(c.Kotonoha).ToArray();
        var nodes = calls.Select(x => View(c, x)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += ReferenceEquals(View(c, calls[i]), nodes[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(nodes, static x => x is { Kind: ComparisonKind.Composite });
    }

    // Each call of a module that compares through Equatable or Comparable, with the Self it compares.
    private static IEnumerable<CallPlan> Comparisons(Kotonoha module)
        => KotoTree.Walk(module.RootKoto)
            .SelectMany(static x => Enumerable.Range(0, 8).Select(i => CallPlan.Of(x, i)).TakeWhile(static y => y is not null).OfType<CallPlan>())
            .Where(static x => x is { Target.CompilerFunction: CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare } and
                { ConformingType: not null });

    // The view of a comparison call: its Contract (the built-in target's), Self and mode.
    private static ComparisonNode? View(Compilation c, CallPlan call)
    {
        var equality = call.Target.CompilerFunction == CompilerFunctionKind.BuiltinEquals;
        var contract = c.Library.GetSymbol(equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable)!;
        return c.Types.Comparison(contract, call.ConformingType!, call.TupleOperator);
    }

    // A node: parts in parentheses, a built-in Type, a witness and its declaring instance, the requirement on a Self, or none.
    private static string Show(ComparisonNode node) => node.Kind switch
    {
        ComparisonKind.Composite => "(" + string.Join(", ", node.Parts.Select(Show)) + ")",
        ComparisonKind.Builtin => TyCtxt.Name(node.Type),
        ComparisonKind.Witness => $"{((DeclarationContainerKoto)node.Target!.Scope.Owner).Name}.{node.Target.Name} @{TyCtxt.Name(node.Subst!.Declaring!)}",
        ComparisonKind.Requirement => $"{TyCtxt.Name(node.Subst!.Self!)}.{node.Target!.Name}",
        _ => "none",
    };

    // Each comparison's view against the legacy plan: for a closed Self the plan's leaves; for a Self with an abstract part the Contract's
    // requirement at each leaf with one. Counts the nodes by kind.
    private static void Twin(Compilation c, Kotonoha module, string label, IDictionary<string, int> kinds, List<string> problems)
    {
        var version = HirTables.Publish(c);
        var issues = c.Binding.Issues.Count;
        var views = Comparisons(module).Select(x => (Call: x, Node: View(c, x))).ToArray();
        if (HirTables.Publish(c) != version || c.Binding.Issues.Count != issues)
        {
            problems.Add($"{label}: a view changed Binding's state");
        }

        foreach (var (call, node) in views)
        {
            var self = call.ConformingType!;
            var plan = self.HasAbstractPart ? null : c.Binding.ComparisonPlan(call);
            var where = $"{label} {TyCtxt.Name(self)} {call.Target.Name} {call.TupleOperator}: {(node is null ? "null" : Show(node))}";
            if (node is null || !(self.HasAbstractPart ? Answers(node) : plan is not null && Same(node, plan)))
            {
                problems.Add(where);
                continue;
            }

            Count(node);
        }

        void Count(ComparisonNode node)
        {
            kinds[node.Kind.ToString()] = kinds.TryGetValue(node.Kind.ToString(), out var count) ? count + 1 : 1;
            foreach (var part in node.Parts)
            {
                Count(part);
            }
        }

        // The same Types and kinds and, for a witness, the same function under the same declaring instance.
        static bool Same(ComparisonNode node, BoundComparison plan) => ReferenceEquals(node.Type, plan.Type) && node.Kind switch
        {
            ComparisonKind.Composite => plan.Implementation is null && node.Parts.Length == plan.Parts.Length &&
                node.Parts.Zip(plan.Parts).All(static x => Same(x.First, x.Second)),
            ComparisonKind.Builtin => plan is { Implementation: null, Parts.Length: 0 },
            ComparisonKind.Witness => plan.Implementation is { } leaf && ReferenceEquals(node.Target, leaf.Target) &&
                ReferenceEquals(node.Subst!.Declaring, leaf.DeclaringType),
            _ => false,
        };

        // Every leaf answers: a closed one by its witness, one with an abstract part by the requirement on it.
        static bool Answers(ComparisonNode node) => node.Kind switch
        {
            ComparisonKind.Composite => node.Parts.All(Answers),
            ComparisonKind.Builtin => true,
            ComparisonKind.Witness => !node.Type.HasAbstractPart,
            ComparisonKind.Requirement => node.Type.HasAbstractPart && ReferenceEquals(node.Subst!.Self, node.Type) &&
                ReferenceEquals(node.Subst.Target, node.Target),
            _ => false,
        };
    }
}
