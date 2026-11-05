// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ClosedConformancePrerequisiteBindingTest
{
    public static TheoryData<string, bool> PendingReads()
    {
        var data = new TheoryData<string, bool>();
        foreach (var family in new[] { "cap6", "cap6n", "cap3b", "cap9", "cap10", "n3b", "n3c" })
        {
            data.Add(family, false);
            data.Add(family, true);
        }

        return data;
    }

    [Theory]
    [InlineData("struct", false)]
    [InlineData("struct", true)]
    [InlineData("enum", false)]
    [InlineData("enum", true)]
    public void ClosedDeclarationPrerequisitesNeedFiniteEvidence(string kind, bool cycle)
    {
        var extra = kind == "enum" ? "\n    Item" : string.Empty;
        var c = MinimalEmissionTest.Analyze("public contract C\npublic " + kind + " A\n    B is C\n    Self is C" + extra + "\npublic " + kind + " B\n    " + (cycle ? "A is C\n    " : string.Empty) + "Self is C" + extra);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(!cycle, c.Binding.Result.IsComplete);
        var contract = Container(c, "C");
        Assert.Equal(!cycle, c.Binding.GetConformanceDefinition(Container(c, "A").TypeOf()!, contract.SymbolOf()!)!.IsVerified);
        Assert.Equal(!cycle, c.Bind().IsComplete);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.Equal(!cycle, restored.Bind().IsComplete);
    }

    [Fact]
    public void NamedSelfPrerequisiteCannotCertifyItself()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\npublic struct A\n    A is C\n    Self is C");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(Container(c, "A").TypeOf()!, Container(c, "C").SymbolOf()!)!.IsVerified);
    }

    [Theory]
    [InlineData("or", true)]
    [InlineData("and", false)]
    public void IndependentFiniteEvidenceCanDischargeARecursiveQuery(string operation, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract C\npublic struct A\n    B is C\n    Self is C\npublic struct B\n    A is C " + operation + " A\n    Self is C");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    // SPEC 8.3.3 Finality (DECISIONS BU-59): a closed clause reading another declaration's own check, through a conditional Copy whose
    // condition is that declaration's conformance, a refinement's premise or a three-step chain, is proven in either declaration order;
    // each publication round clears the capability results, so no answer computed from a Pending state outlives its round.
    [Theory]
    [MemberData(nameof(PendingReads))]
    public void APendingReadDecidesNothingInEitherOrder(string family, bool reverse)
    {
        var c = Bind(Program(family, reverse));
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
    }

    // SPEC 8.3.3, 23.1.2: declarations whose own checks need each other, directly (two or three) or through a Contract premise, leave each
    // `Self is C` unproven in every order, two independent failures, not one InvalidConstraint_Kd chosen by the declaration order.
    [Theory]
    [InlineData("pr2", false, 2)]
    [InlineData("pr2", true, 2)]
    [InlineData("n3a", false, 3)]
    [InlineData("n3a", true, 3)]
    [InlineData("cyc1", false, 2)]
    [InlineData("cyc1", true, 2)]
    public void MutualPrerequisitesStayUnprovenInEveryOrder(string family, bool reverse, int count)
    {
        var records = Records(Program(family, reverse)).Where(static x => x.Text == "Self is C").ToArray();
        Assert.Equal(count, records.Length);
        Assert.All(records, static x => Assert.Equal("UnprovenConstraint_Kd", x.Code));
    }

    // SPEC 8.3.1, 8.3.2, BU-22: a closed clause resting on an Error is an Error in every order: a conformance limited by OCC-X, checked
    // outside conformance verification, is an Error on its site (occ1); a Contract's closed clause reading another Contract's Error as a
    // premise (n3d).
    [Theory]
    [InlineData("occ1", false, "Self is K", 1)]
    [InlineData("occ1", true, "Self is K", 1)]
    [InlineData("n3d", false, "Self is R", 3)]
    [InlineData("n3d", true, "Self is R", 3)]
    public void AClosedClauseRestingOnAnErrorIsInvalidInEveryOrder(string family, bool reverse, string clause, int count)
    {
        var records = Records(Program(family, reverse)).Where(x => x.Text!.StartsWith(clause, StringComparison.Ordinal)).ToArray();
        Assert.Equal(count, records.Length);
        Assert.All(records, static x => Assert.Equal("InvalidConstraint_Kd", x.Code));
    }

    // SPEC 8.3.3 Finality, 23.1.2, BU-22: a closed clause limited by a component summary's site (X) is an Unknown resting on that limit,
    // and so is each declaration whose closed clause reads it, directly (Y) or through another (Z), in every order and however the rounds
    // run (B's valid check makes a round change a state): each `Self is K` rests on the site, one record set in all orders.
    [Theory]
    [InlineData("sumy")]
    [InlineData("sumz")]
    [InlineData("sumw")]
    public void ALimitReachesEachReaderInEveryOrder(string family)
    {
        var shape = Shape(family);
        string[]? first = null;
        foreach (var order in Orders(shape.Declarations))
        {
            var source = shape.Prefix + string.Concat(order);
            var records = Records(source).Select(x => x.Code + " " + Owner(source, x.Span.Start) + ": " + x.Text)
                .Order(StringComparer.Ordinal).ToArray();
            Assert.DoesNotContain(records, static x => x.EndsWith(": Self is K", StringComparison.Ordinal));
            first ??= records;
            Assert.Equal(first, records);
        }
    }

    // The rounds' bound (BU-59): each round runs in the reverse of the previous round's order, so a chain of declarations whose closed
    // clauses each rest on the next one's conformance settles in at most three rounds in either order, and the checks grow linearly.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AChainOfClosedClausesSettlesInBoundedRounds(bool reverse)
    {
        var shallow = Work(16);
        var deep = Work(64);
        Assert.True(deep <= 5 * shallow, $"{shallow} -> {deep}");

        int Work(int count)
        {
            var declarations = Enumerable.Range(0, count).Select(i =>
                "public struct X" + i + "\n    " + (i == count - 1 ? "i32" : "Pair<X" + (i + 1) + ">") + " is Copy\n    Self is R\n");
            var c = Bind("public contract R: Copy\npublic struct Pair<T>\n    Self is Copy when T is R\n    var first: T\n" +
                string.Concat(reverse ? declarations.Reverse() : declarations));
            Assert.True(c.Binding.Result.IsComplete, string.Join('\n', c.Binding.Issues));
            return c.Binding.SourceCheckWork;
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFinitePrerequisiteVerificationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\npublic struct A\n    B is C\n    Self is C\npublic struct B\n    Self is C");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Finite conformance verification failed.");
            }
        }));
    }

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);

    // A family's declarations in their order or reversed, between a fixed prefix and a fixed tail of uses.
    private static string Program(string family, bool reverse)
    {
        var shape = Shape(family);
        return shape.Prefix + string.Concat(reverse ? Enumerable.Reverse(shape.Declarations) : shape.Declarations) + shape.Tail;
    }

    // Every order of a family's declarations.
    private static IEnumerable<string[]> Orders(string[] declarations)
        => declarations.Length <= 1 ? new[] { declarations }
            : declarations.SelectMany((first, i) => Orders([.. declarations[..i], .. declarations[(i + 1)..]]).Select(rest => rest.Prepend(first).ToArray()));

    // The header of the declaration a record's span starts in.
    private static string Owner(string source, int start)
        => source[(source.LastIndexOf("\npublic ", start, StringComparison.Ordinal) + 1)..].Split('\n')[0];

    private static (string Prefix, string[] Declarations, string Tail) Shape(string family)
    {
        const string k = "public contract K\n";
        const string r = "public contract R: Copy\n";
        const string pair = "public struct Pair<T>\n    Self is Copy when T is R\n    var first: T\n";
        const string x = "public struct X\n    Pair<Y> is Copy\n    Self is K\n";
        const string y = "public struct Y\n    Self is R\n";
        const string useK = "func useK<T>(x: T) -> i32\n    T is K\n    return 1\nfunc g(x: X) -> i32\n    return useK(x@move)\n";
        const string ranked = "public struct X\n    P1<Y1> is Copy\n    Self is K\n";
        const string ranks = "public struct P1<T>\n    Self is Copy when T is R1\n    var first: T\n" +
            "public struct P2<T>\n    Self is Copy when T is R2\n    var first: T\n" +
            "public struct P3<T>\n    Self is Copy when T is R3\n    var first: T\n" +
            "public struct Y1\n    Self is R1\npublic struct Y2\n    Self is R2\npublic struct Y3\n    Self is R3\n";
        const string r1 = "public contract R1: Copy\n    P2<Y2> is Copy\n";
        const string r2 = "public contract R2: Copy\n    P3<Y3> is Copy\n";
        const string c = "public contract C\n";
        const string a = "public struct A\n    B is C\n    Self is C\n";
        const string tree = "public contract Tree\n    associate Child is Tree\npublic contract OTree: Tree, Owned\n    Self.(Tree).Child is OTree\n" +
            "public struct TreeNode<T>\n    T is Tree\n    let v: T\n    let kids: Array<TreeNode<T.(Tree).Child>>\n" +
            "public struct Nd\n    Self is OTree\n    associate Tree.Child is Nd\n" + k;
        const string limited = "public struct X\n    TreeNode<Nd> is Owned\n    Self is K\n";
        const string reader = "public struct Y\n    X is K\n    Self is K\n";
        return family switch
        {
            "cap6" => (string.Empty, [x, k, r, pair, y], string.Empty),
            "cap6n" => (string.Empty, [x, k, r, pair, "public struct Y\n    i32 is Copy\n    Self is R\n"], useK),
            "cap3b" => (string.Empty, [x, k, r, "public struct Pair<T>\n    T is R\n    Self is Copy\n    var first: T\n", y], useK),
            "cap9" => (
                string.Empty,
                ["public struct X\n    Pair<Y> is Copy\n", "public contract K\n    func k(self) -> i32\n", r, pair, y,
                    "public contract Q: K\n    Pair<Y> is Copy\n"],
                "public func f<T>(x: T) -> i32\n    T is Q\n    return x.k()\n"),
            "cap10" => (string.Empty, [x, k, "public contract R: Copy\n    i32 is Copy\n", pair, y], useK),
            "n3b" => (string.Empty, [r1, r2, "public contract R3: Copy\n    i32 is Copy\n", ranked, k, ranks], useK),
            "n3c" => (
                string.Empty,
                [x, "public struct Y\n    Pair<Z> is Copy\n    Self is R\n", "public struct Z\n    i32 is Copy\n    Self is R\n", k, r, pair],
                useK),
            "n3d" => (string.Empty, [r1, r2, "public contract R3: Copy\n    P1<Y1> is Copy\n", ranked, k, ranks], useK),
            "pr2" => (c, [a, "public struct B\n    A is C\n    Self is C\n"], string.Empty),
            "n3a" => (c, [a, "public struct B\n    D is C\n    Self is C\n", "public struct D\n    A is C\n    Self is C\n"], string.Empty),
            "cyc1" => (
                c,
                [a, "public struct B\n    A is C\n    Self is C\n"],
                "public contract K\n    func k(self) -> i32\npublic contract R: K\n    A is C\n" +
                    "public func f<T>(x: T) -> i32\n    T is R\n    return x.k()\n"),
            "occ1" => (
                string.Empty,
                [
                    "public contract Bumps\n    func bump(self: uniq/Self) -> ()\npublic open struct Base\n    public var count: i32 = 1\n" +
                        "    public func bump(self: uniq/Self) -> () => self.count += 1\n" +
                        "public struct Leaf: Base\n    Self is Bumps\n    public init() => ()\n",
                    "public contract K\npublic struct X\n    Leaf is Bumps\n    Self is K\n",
                ],
                string.Empty),
            "sumy" => (tree, [limited, reader], string.Empty),
            "sumz" => (tree, [limited, reader, "public struct Z\n    Y is K\n    Self is K\n"], string.Empty),
            "sumw" => (
                tree,
                ["public struct Z\n    Y is K\n    Self is K\n", reader, limited, "public struct B\n    i32 is Copy\n    Self is K\n"],
                string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }

    private static Compilation Bind(string source)
    {
        var c = CompilationTestHelper.Parse(source, "Hello.kimi");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        c.Bind();
        return c;
    }

    private static TestDiagnostic[] Records(string source)
        => CompilationTestHelper.BindingErrors(Bind(source));
}
