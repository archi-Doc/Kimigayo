// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class NormalizedConstraintProofBindingTest
{
    private const string Head = "public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\n";

    private const string Trees = "contract Tree\n    associate Child is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n" +
        "contract K\n    associate X\n    Self.(K).X is i32\n";

    // R7-C2e2-1's valid twins: `Moved`, a projection's body use, whose fill writes its scope's premises between requests (the view's
    // entry is rebuilt at the new serial); `Derived`, a view whose identity table a derived identity fact keeps Provisional (`T is K`
    // supplies `T.(K).X is i32`): its class keys are not final, so the entry stays the request's.
    private const string Moved = "func twice<T>(value: T.(Tree).Child) -> (T.(Tree).Child, T.(Tree).Child)\n    T is CopyTree\n" +
        "    let x: T.(Tree).Child = value\n    let y = (x, x)\n    return (x, x)\n";

    private const string Validated = "contract K<X>\n    associate E\nfunc f<T, U, V>(t: T, u: U, v: V)\n    T is K<U>\n    T is K<V>\n    T is K<i32>\n" +
        "    T.(K<U>).E is i32\n    T.(K<V>).E is i32\n    T.(K<i32>).E is i32\n    ()\n" +
        "contract P<E>\n    func read(self: ref/Self, value: ref/E) -> i32\ncontract Left: P<i32>\ncontract Right<U>: P<U>\n" +
        "struct S<U, T>\n    Self is Left when T is i32\n    Self is Right<U> when T is string\n" +
        "    public func read(self: ref/Self, value: ref/i32) -> i32 => 1\n    public func read(self: ref/Self, value: ref/U) -> i32 => 2\n";

    private const string Derived = "func both<T, U>(a: U.(Tree).Child, b: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child, U.(Tree).Child, " +
        "U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n    T is U\n    T is CopyTree\n    T is K\n    return (a, a, b, b)\n";

    // DECISIONS R7-C2e2-1: Closure(view) forms a view's index once per view value, not once per request. n Copy requests in one view of
    // n written premises read one entry, so each added premise adds a fact to each of a few views' entries; a per-request index walks the
    // n facts once per request (4,800 more facts from 8 to 16 premises). No projection: a fill moves the view at each use.
    [Fact]
    public void AViewsIndexIsFormedOncePerViewNotPerRequest()
    {
        var (small, large) = (Work(8), Work(16));
        Assert.True(large - small <= 16 * 8, $"{small} at 8, {large} at 16");

        static int Work(int n)
        {
            var range = Enumerable.Range(0, n);
            var parameters = string.Join(", ", range.Select(static i => $"T{i}"));
            var values = string.Join(", ", range.Select(static i => $"v{i}: T{i}"));
            var results = string.Join(", ", range.Select(static i => $"T{i}, T{i}"));
            var premises = string.Concat(range.Select(static i => $"    T{i} is CopyTree\n"));
            var copies = string.Join(", ", range.Select(static i => $"v{i}, v{i}"));
            var c = MinimalEmissionTest.Analyze(Trees + $"func f<{parameters}>({values}) -> ({results})\n{premises}    return ({copies})\n" +
                "public func main() -> ()\n    return\n");
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
            return c.Binding.ClosureIndexWork;
        }
    }

    // DECISIONS R7-C2e2-1: an entry is its view's at the serial it was formed at (Moved), and Kept only when its class keys are final
    // (Derived: a Provisional identity table; the readings' admission is x1's count row below). Each with its invalid twin: `T is Tree`
    // supplies no Copy.
    [Theory]
    [InlineData(Moved, "clean")]
    [InlineData(Derived, "clean")]
    [InlineData(Moved, "invalid")]
    [InlineData(Derived, "invalid")]
    public void AViewsIndexIsTheViewsAtItsSerialAndCompletion(string function, string records)
    {
        var twin = records == "clean" ? function : function.Replace("T is CopyTree", "T is Tree", StringComparison.Ordinal);
        Assert.Equal(records, DiagnosticCorpus.RecordProfile(Trees + twin + "public func main() => ()\n") == "clean" ? "clean" : "invalid");
    }

    // DECISIONS R7-C2e2-1: the pairs the entries index for Moved (the fill moves its view: the entry is formed again at the new serial)
    // and Derived (an identity view whose table the derived identity fact of `T is K` keeps Provisional: its class keys are not final,
    // so its entry stays the request's and each request forms it again, R7-C2c2 c2-1's lifetime), less the library's: exact. An entry
    // read across its serial indexes fewer (1,411); one Kept under a table that is not Final indexes 192. Every row counts (R7-C2e2-2).
    [Fact]
    public void AViewsIndexIsFormedAtEachSerialUntilComplete()
        => Assert.Equal(1416, ClosureWork(Trees + Moved + Derived) - ClosureWork(Trees));

    // DECISIONS R7-C2e2-1, R7-M20 (implementation review 2's twins): `(a, a)` is an in-region Copy request, which keeps the view's entry
    // with readings; `Box<U>.(K).X` then normalizes through `Box<T>: K when T is C`, whose condition is proven by premise inside that
    // associated normalization, an outermost request outside KeptRegion: it forms the entry again with its own readings. The condition
    // holds by `U is C` (x1) or through the identity by `T is C` (x2); without it (x1n) `Box<U>` is not K.
    [Theory]
    [InlineData("x1", "    U is C\n", "")]
    [InlineData("x2", "    T is C\n", "")]
    [InlineData("x1n", "", "UnprovenConstraint_Kd: let y: Box<U>.(K).X = b")]
    public void AKeptEntrysReadingsAreNotReadInsideANormalization(string form, string premise, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, Boxed(premise) + "public func main() => ()\n", records);

    // The pairs x1's entries index over the library's, exact (a fact of the identity view also under its class key; the table is Final,
    // so the entry is Kept): the request inside the normalization indexes the view again, so reading the Kept entry there, without
    // IndexOf's rebuild branch, indexes 68, as does an entry Kept with readings that were not kept.
    [Fact]
    public void ARequestInsideANormalizationIndexesItsViewAgain()
        => Assert.Equal(98, ClosureWork(Boxed("    U is C\n")) - ClosureWork(string.Empty));

    // DECISIONS R7-C2e2-2: a closure validator's read forms its view's entry when it has a fact to pair, and the entry goes back when
    // the read ends. The agreement reads f's view (three identities of one projection through K<U>, K<V> and K<i32>, SPEC 8.9.2);
    // disjointness reads the scopes of S's two conditional conformances (`T is i32` against `T is string`, SPEC 8.9.1). The pairs their
    // entries and the requests' index over the library's: exact.
    [Fact]
    public void AValidatorsReadIndexesItsViewOncePerRead()
        => Assert.Equal(89, ClosureWork(Validated) - ClosureWork(string.Empty));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindThatRebuildsAViewsIndexAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Trees + Moved + Derived + "public func main() => ()\n");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("The rebind failed.");
            }
        }));
    }

    [Theory]
    [InlineData("Source.(Origin).Item", "i32", ConstraintProof.Proven)]
    [InlineData("i32", "Source.(Origin).Item", ConstraintProof.Proven)]
    [InlineData("Source.(Origin).Item or string", "i32 or string", ConstraintProof.Proven)]
    [InlineData("i32 or string", "Source.(Origin).Item or string", ConstraintProof.Proven)]
    [InlineData("not Source.(Origin).Item", "i32", ConstraintProof.Refuted)]
    [InlineData("not i32", "Source.(Origin).Item", ConstraintProof.Refuted)]
    [InlineData("[2 of Source.(Origin).Item] or string", "[2 of i32] or string", ConstraintProof.Proven)]
    [InlineData("(Source.(Origin).Item, string) or bool", "(i32, string) or bool", ConstraintProof.Proven)]
    [InlineData("Source.(Origin).Item or string", "i32", ConstraintProof.Unknown)]
    [InlineData("Source.(Origin).Item or string\n    T is not i32", "string", ConstraintProof.Unknown)]
    [InlineData("Source.(Origin).Item\n    T is not i32", "bool", ConstraintProof.Error)]
    public void EquivalentRequirementSpellingsUseTheSameEvidence(string evidence, string requirement, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func context<T>(value: T)\n    T is " + evidence + "\n    ()\nfunc query<T>(value: T)\n    T is " + requirement + "\n    ()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        AssertProof(c, expected);
        c.Bind();
        AssertProof(c, expected);
        var restored = CompilationTestHelper.Reload(c);
        restored.Bind();
        AssertProof(restored, expected);
    }

    [Theory]
    [InlineData("Source.(Origin).Item or string", "i32 or string")]
    [InlineData("i32 or string", "Source.(Origin).Item or string")]
    [InlineData("not Source.(Origin).Item", "not i32")]
    [InlineData("not i32", "not Source.(Origin).Item")]
    public void DependentCallsUseEquivalentCompoundPremises(string evidence, string requirement)
    {
        var c = MinimalEmissionTest.Analyze(Head + "func take<T>()\n    T is " + requirement + "\n    ()\nfunc caller<U>()\n    U is " + evidence + "\n    take<U>()");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.NotNull(Assert.IsType<InvocationKoto>(Function(c, "caller").Body!.Items.Single()).CallOf());
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConcreteProofNormalizesTheAssociatedRequirement(string argument, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze(
            Head + "func query<T>(value: T)\n    T is Source.(Origin).Item\n    ()\nfunc context(value: " + argument + ") => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        AssertProof(c, expected);
    }

    // Pins Binding's proof value Unknown for the [3 of i32] and i32 rows, which SPEC 8.2.3 refutes, pending PLAN G170's C2-C4 (R4-M115).
    [Theory]
    [InlineData("Source.(Origin).Item and Copy", "i32 and Copy", ConstraintProof.Proven)]
    [InlineData("not (Source.(Origin).Item and string)", "not i32", ConstraintProof.Unknown)]
    [InlineData("not (Source.(Origin).Item and string)", "not (i32 and string)", ConstraintProof.Proven)]
    [InlineData("[2 of Source.(Origin).Item]", "[3 of i32]", ConstraintProof.Unknown)]
    [InlineData("ref/Source.(Origin).Item during static", "i32", ConstraintProof.Unknown)]
    public void NormalizationDoesNotAddLogicalRulesOrEraseTypeStructure(string evidence, string requirement, ConstraintProof expected)
        => this.EquivalentRequirementSpellingsUseTheSameEvidence(evidence, requirement, expected);

    [Fact]
    public void ReplacingAssociatedIdentityRebuildsProofEvidence()
    {
        const string functions = "func context<T>(value: T)\n    T is Source.(Origin).Item or string\n    ()\nfunc query<T>(value: T)\n    T is i32 or string\n    ()";
        var c = MinimalEmissionTest.Analyze(Head + functions);
        AssertProof(c, ConstraintProof.Proven);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint);
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(Head.Replace("is i32", "is bool", StringComparison.Ordinal) + functions);
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Source").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint).Right;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Unknown);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        AssertProof(c, ConstraintProof.Proven);
    }

    // DECISIONS R7-C2e1 (F1): `T is K` supplies `T.(K).X is Q` and Q's clause `T.(K).X.(Q).Y is i32` (SPEC 8.3.2), so the written premise
    // asks `i32 is Marker`, which a primitive's fixed conformances refute (SPEC 8.7.1): f's premises contradict, and each use of them is
    // InvalidConstraint_Kd. f's view's environment is written after its facts are first read in the pass, so a kept reading is valid only
    // under the serial naming the environment's state. Both orders.
    [Theory]
    [InlineData("x.q()\n    g<i32>()\n")]
    [InlineData("g<i32>()\n    x.q()\n")]
    public void AReadingIsKeptOnlyForTheEnvironmentItWasFormedIn(string uses)
        => Assert.Equal(
            "9*InvalidConstraint_Kd",
            DiagnosticCorpus.RecordProfile("contract Marker\ncontract Q\n    associate Y\n    Self.(Q).Y is i32\n    func q(self: ref/Self) -> ()\n" +
                "contract K\n    associate X is Q\nfunc g<Z>()\n    Z is Marker\n    ()\nfunc f<T>(x: ref/T.(K).X)\n    T is K\n    T.(K).X.(Q).Y is Marker\n" +
                "    g<T.(K).X.(Q).Y>()\n    " + uses));

    // DECISIONS R7-C2e1 (F5):a requirement's signature is formed in its declaration's view under the call's reference (SPEC 8.6.4), so
    // P's clause reads `A.(Src).Item is not Marker` under P<A> and `B.(Src).Item is not Marker` under P<B>, and each call's candidate is
    // refuted under its own reference (SPEC 8.3.2): a reading is one per requirement Contract. Both orders.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReadingIsOnePerRequirementReference(bool reversed)
    {
        const string Declarations = "contract Marker\ncontract Src\n    associate Item\nstruct Box<T>\n    T is Marker\n    public init() => ()\n" +
            "contract P<E>\n    E is Src\n    E.(Src).Item is not Marker\n    func read(self: ref/Self) -> Box<E.(Src).Item>\n";
        var (first, second) = (Use("via", "A"), Use("via2", "B"));
        var source = Declarations + (reversed ? second + first : first + second);
        Assert.Equal("2*NoApplicableOverload_Kd 3*UnsatisfiedConstraint_Kd", DiagnosticCorpus.RecordProfile(source));

        static string Use(string name, string slot)
            => $"func {name}<T, {slot}>(t: ref/T)\n    T is P<{slot}>\n    {slot} is Src\n    {slot}.(Src).Item is not Marker\n    t.read()\n";
    }

    // DECISIONS R7-C2e1 (F6): the builtin specification of `T.(Addable<T>).Output` proves T's arithmetic conditions in f's view, where
    // both premises naming the projection meet the open normalization and stay unreduced. Those readings are not the view's: `g<V>()`
    // reads the second premise as `V is Marker` (SPEC 8.2.3). Both orders; without that premise `V is Marker` is unproven.
    [Theory]
    [InlineData("T.(Addable<T>).Output is V\n    T.(Addable<T>).Output is Marker\n", "clean")]
    [InlineData("T.(Addable<T>).Output is Marker\n    T.(Addable<T>).Output is V\n", "clean")]
    [InlineData("T.(Addable<T>).Output is V\n", "1*UnprovenConstraint_Kd")]
    public void AReadingFormedUnderANormalizationIsNotKept(string premises, string records)
        => Assert.Equal(
            records,
            DiagnosticCorpus.RecordProfile("contract Marker\nfunc g<X>()\n    X is Marker\n    ()\nfunc f<T, V>()\n    T is Addable<T>\n    " + premises + "    g<V>()\n"));

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmNormalizedProofsAndCallsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Head + "func take<T>()\n    T is [2 of i32] or string\n    ()\nfunc caller<U>()\n    U is [2 of Source.(Origin).Item] or string\n    take<U>()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Normalized constraint proof failed.");
            }
        }));
    }

    private static string Boxed(string premise)
        => "contract Tree\n    associate Child is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ncontract C\n" +
            "contract K\n    associate X\nstruct Box<T>\n    public let v: i32\n    Self is K when T is C\n        associate K.X is i32\n" +
            "func f<T, U>(a: U.(Tree).Child, b: i32) -> (U.(Tree).Child, U.(Tree).Child)\n    T is U\n    T is CopyTree\n" + premise +
            "    let p = (a, a)\n    let y: Box<U>.(K).X = b\n    return p\n";

    // Closure(view)'s pairs indexed by a compilation of `source` (the class's scaling measure).
    private static int ClosureWork(string source)
    {
        var c = MinimalEmissionTest.Analyze(source + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        return c.Binding.ClosureIndexWork;
    }

    private static void AssertProof(Compilation c, ConstraintProof expected)
    {
        var context = Function(c, "context");
        var query = Function(c, "query");
        var proposition = ((IsKoto)query.TypeConstraints[0]).ConstraintOf()!;
        Assert.Equal(expected, c.Binding.Prove(proposition, query, [context.Parameters[0].Type.TypeOf()], context));
    }

    private static FunctionKoto Function(Compilation c, string name)
        => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);
}
