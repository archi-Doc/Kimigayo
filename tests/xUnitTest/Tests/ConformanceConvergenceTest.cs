// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ConformanceConvergenceTest(ITestOutputHelper output)
{
    private const string ImplementationNamesItsConformance = """
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct S
            Self is C
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S>) -> i32 => 1
        func use(s: ref/S, b: ref/Box<S>) -> i32 => s.m(b)
        """;

    private const string ImplementationsNameEachOthersConformance = """
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        contract K
            func k(self: ref/Self, other: ref/Pack<Self>) -> i32
        struct Box<T>
            T is K
            public init() => ()
        struct Pack<T>
            T is C
            public init() => ()
        struct S
            Self is C
            Self is K
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S>) -> i32 => 1
            public func k(self: ref/Self, other: ref/Pack<S>) -> i32 => 1
        func use(s: ref/S, b: ref/Box<S>, p: ref/Pack<S>) -> i32 => s.m(b) + s.k(p)
        """;

    private const string ImplementationDeclaredBeforeItsContract = """
        func use(s: ref/S, b: ref/Box<S>) -> i32 => s.m(b)
        struct S
            public func m(self: ref/Self, other: ref/Box<S>) -> i32 => 1
            public init() => ()
            Self is C
        struct Box<T>
            T is C
            public init() => ()
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        """;

    private const string ConcreteCycle = """
        contract C
            func m(self: ref/Self, other: ref/Box<S>) -> i32
        contract K
            func k(self: ref/Self, other: ref/Pack<S>) -> i32
        struct Box<T>
            T is K
            public init() => ()
        struct Pack<T>
            T is C
            public init() => ()
        struct S
            Self is C
            Self is K
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S>) -> i32 => 1
            public func k(self: ref/Self, other: ref/Pack<S>) -> i32 => 1
        func use(s: ref/S, b: ref/Box<S>, p: ref/Pack<S>) -> i32 => s.m(b) + s.k(p)
        """;

    private const string ConcreteCycleDeclaredBackwards = """
        func use(s: ref/S, b: ref/Box<S>, p: ref/Pack<S>) -> i32 => s.m(b) + s.k(p)
        struct S
            public func k(self: ref/Self, other: ref/Pack<S>) -> i32 => 1
            public func m(self: ref/Self, other: ref/Box<S>) -> i32 => 1
            public init() => ()
            Self is K
            Self is C
        struct Pack<T>
            T is C
            public init() => ()
        struct Box<T>
            T is K
            public init() => ()
        contract K
            func k(self: ref/Self, other: ref/Pack<S>) -> i32
        contract C
            func m(self: ref/Self, other: ref/Box<S>) -> i32
        """;

    private const string ImplementationNamesAContractItsTypeLacks = """
        contract K
        contract C
            func m(self: ref/Self, other: ref/Box<S>) -> i32
        struct Box<T>
            T is K
            public init() => ()
        struct S
            Self is C
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S>) -> i32 => 1
        """;

    private const string ResultMismatchBesideASignature = """
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct S
            Self is C
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S>) -> bool => true
        """;

    private const string ReceiverShapesKeepTheSignature = """
        contract C
            func m(self: ref/Self, x: i32) -> i32
        struct S
            Self is C
            public init() => ()
            public func m(self: ref/Self, x: i32) -> i32 => 1
            public func m(self: uniq/Self, x: string) -> i32 => 2
        func need<T>(t: ref/T) -> i32
            T is C
            return t.m(1)
        func use(s: ref/S) -> i32 => need(s)
        """;

    private const string CaseLimitKeepsTheSignature = """
        contract C
            func m<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(self: ref/Self, p: a/A, q: b/B, r: c/C, u: d/D, v: e/E, w: g/G, y: h/H) -> i32
                a is owner or uniq
                b is owner or uniq
                c is owner or uniq
                d is owner or uniq
                e is owner or uniq
                g is owner or uniq
                h is owner or uniq
        struct S
            Self is C
            public init() => ()
            public func m<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(self: ref/Self, p: a/A, q: b/B, r: c/C, u: d/D, v: e/E, w: g/G, y: h/H) -> i32
                a is owner or uniq
                b is owner or uniq
                c is owner or uniq
                d is owner or uniq
                e is owner or uniq
                g is owner or uniq
                h is owner or uniq
                return 1
        """;

    private const string BindingNamesItsConformance = """
        contract C
            associate Item
            func get(self: ref/Self, other: ref/Self.Item) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct S
            Self is C
            associate C.Item is Box<S>
            public init() => ()
            public func get(self: ref/Self, other: ref/Box<S>) -> i32 => 1
        """;

    private const string InferredBindingNamesItsConformance = """
        contract C
            associate Item
            func get(self: ref/Self) -> Self.Item
        struct Box<T>
            T is C
            public init() => ()
        struct S
            Self is C
            public init() => ()
            public func get(self: ref/Self) -> Box<S> => Box<S>.init()
        """;

    private const string SignatureSpellsItsBinding = """
        contract C
            associate Item
            func get(self: ref/Self, other: ref/Self.Item) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct S
            Self is C
            associate C.Item is Box<S>
            public init() => ()
            public func get(self: ref/Self, other: ref/Self.Item) -> i32 => 1
        """;

    private const string BindingsNameEachOthersConformance = """
        contract C
            associate Item
        contract K
            associate Item2
        struct Box<T>
            T is K
            public init() => ()
        struct Pack<T>
            T is C
            public init() => ()
        struct U
            Self is C
            associate C.Item is Box<V>
        struct V
            Self is K
            associate K.Item2 is Pack<U>
        """;

    private const string InlineCycleDirect = """
        contract C
            associate Item
        struct Box<T>
            T is C
            public var next: T.(C).Item
        struct S
            Self is C
            associate C.Item is Box<S>
        """;

    private const string InlineCycleThroughOption = """
        contract C
            associate Item
        struct Box<T>
            T is C
            public var next: Option<T.(C).Item>
        struct S
            Self is C
            associate C.Item is Box<S>
        func use(b: ref/Box<S>) -> i32 => 1
        """;

    private const string InlineCycleWithAValue = """
        contract C
            associate Item
        struct Box<T>
            T is C
            public var next: Option<T.(C).Item>
            public init() => self.next = .None
        struct S
            Self is C
            associate C.Item is Box<S>
        let b = Box<S>.init()
        Console.writeLine("ok")
        """;

    private const string InlineCycleInferred = """
        contract C
            associate Item
            func get(self: ref/Self) -> Self.Item
        struct Box<T>
            T is C
            public var next: Option<T.(C).Item>
        struct S
            Self is C
            public func get(self: ref/Self) -> Box<S> => $abort("x")
        """;

    // The inferred binding's cycle beside a Resolved body that makes a Box<S> value, and the written binding with S's method returning
    // Box<S> (the inferred row's shape): ruling 261's rows (PLAN G1071 gate 1 publishes S.get's body; F's C2 ruling 261 (2)).
    private const string InlineCycleInferredWithAValue = """
        contract C
            associate Item
            func get(self: ref/Self) -> Self.Item
        struct Box<T>
            T is C
            public var next: Option<T.(C).Item>
            public init() => self.next = .None
        struct S
            Self is C
            public func get(self: ref/Self) -> Box<S> => $abort("x")
        func h() -> i32
            let b = Box<S>.init()
            return 0
        """;

    private const string InlineCycleWrittenWithAMethod = """
        contract C
            associate Item
            func get(self: ref/Self) -> Self.Item
        struct Box<T>
            T is C
            public var next: Option<T.(C).Item>
        struct S
            Self is C
            associate C.Item is Box<S>
            public func get(self: ref/Self) -> Box<S> => $abort("x")
        """;

    private const string InlineCycleInTupleAndArray = """
        contract C
            associate Item
        struct Box<T>
            T is C
            public var next: [2 of Option<T.(C).Item>]
        struct S
            Self is C
            associate C.Item is (i32, Box<S>)
        """;

    private const string InlineCycleThroughTwoTypes = """
        contract C
            associate Item
        struct Box<T>
            T is C
            public var next: Option<T.(C).Item>
        struct S
            Self is C
            associate C.Item is Box<R>
        struct R
            Self is C
            associate C.Item is Box<S>
        """;

    private const string InlineCycleThroughAnotherContract = """
        contract K
            associate Y
        struct Box<T>
            T is K
            public var next: Option<T.(K).Y>
        struct S
            Self is K
            associate K.Y is Box<S>
        func use(b: ref/Box<S>) -> i32 => 1
        """;

    private const string InlineCycleGrowing = """
        contract Has2
            associate Out
        struct W<X>
            Self is Has2
            associate Has2.Out is PH<W<(X, X)>>
        struct PH<T>
            T is Has2
            let v: T.(Has2).Out
        """;

    private static readonly Dictionary<string, string> EditedSelfClauses = new()
    {
        ["e1"] = "contract Nominals\nstruct Numbers {source}\n    Self is Iterator\n    associate Iterator.Item is i32\n    let value: ref/i32 during source\n" +
            "    public init(value: ref/i32 during source) => self.value = value\n    public func next(self: uniq/Self) -> Option<i32> => .None\n" +
            "struct Bin<T>\n    Self is Iterable\n    associate Iterable.IteratorType(source) is Numbers during source\n    var value: i32 = 7\n" +
            "    public init() => ()\n    public func iterate(self: ref/Self during source) -> Numbers during source => Numbers.init(self.value@ref)\n" +
            "    public func walk(self: ref/Self) -> ()\n        for x in self\n            _ = x\nlet present = 1",
        ["e6"] = "contract HasItem\n    associate Item\nstruct SrcI\n    Self is HasItem\n    associate HasItem.Item is i32\n    public init() => ()\n" +
            "contract Iter<T>\nstruct B<S>\n    Self is Iter<i32>\n    S is HasItem\n    public init() => ()\nfunc need<T>(x: ref/T) -> i32\n    T is Iter<i32>\n" +
            "    return 1\nlet b = B<SrcI>.init()\nlet n = need(b@ref)",
        ["t1"] = "contract HasItem\n    associate Item\nstruct SrcI\n    Self is HasItem\n    associate HasItem.Item is i32\n    public init() => ()\n" +
            "struct SrcJ\n    Self is HasItem\n    associate HasItem.Item is i64\n    public init() => ()\n" +
            "contract Iter<T>\nstruct B<S>\n    Self is Iter<i32>\n    S is HasItem\n    public init() => ()\nfunc need<T>(x: ref/T) -> i32\n    T is Iter<i32>\n" +
            "    return 1\nstruct Holder<X>\n    X is Iter<i32>\n    public init() => ()\nlet h = Holder<B<SrcI>>.init()",
    };

    // The 24 orders of four declarations, each with its bound's two spellings and without Wrap's K
    // (ACapabilityReadUnderAnOpenObligationIsJudgedAgain).
    public static TheoryData<int, string, bool> DeclarationOrders
        => [.. Enumerable.Range(0, 24).SelectMany(static order => new[] { (order, "Copy or K", true), (order, "K or Copy", true), (order, "Copy or K", false) })];

    // The owner's O-44, SPEC 8.6.3, 8.3.2: a conformance forms no signature, so each Type is judged where it is written, after the conformance holds, in
    // any declaration order (8.3.3). The second program stays invalid at its own requirements: Box<Self> in C needs Self is K, which C does not supply
    // (7.5, 8.15.1). The last two are DECISIONS Q13's: the conformance's record went with the gate (k1) or names the true cause (m3).
    [Theory]
    [InlineData(ImplementationNamesItsConformance, "clean")]
    [InlineData(ImplementationsNameEachOthersConformance, "2*UnprovenConstraint_Kd")]
    [InlineData(ImplementationDeclaredBeforeItsContract, "1*DeclarationOrderWarning_Kd")]
    [InlineData(ConcreteCycle, "clean")]
    [InlineData(ConcreteCycleDeclaredBackwards, "1*DeclarationOrderWarning_Kd")]
    [InlineData(ImplementationNamesAContractItsTypeLacks, "2*UnsatisfiedConstraint_Kd")]
    [InlineData(ResultMismatchBesideASignature, "1*IncompatibleContractImplementation_Kd 1*InvalidConstraint_Kd")]
    public void AConformanceFormsNoSignature(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // SPEC 8.3.1, DECISIONS Q13: a declaration that failed but keeps its signature supplies evidence, and its own record stays (SuppliesNoEvidence).
    [Theory]
    [InlineData(ReceiverShapesKeepTheSignature, "2*ReceiverShapeMismatch_Kd")]
    [InlineData(CaseLimitKeepsTheSignature, "1*OwnershipCaseLimit_Kd")]
    public void AFailureThatKeepsTheSignatureFailsNoConformance(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // The owner's O-54 (a): a binding's Type is judged where it is written, after the conformance holds, so these are valid (R7-C2e2 e2-5a).
    [Theory]
    [InlineData(BindingNamesItsConformance, "clean")]
    [InlineData(InferredBindingNamesItsConformance, "clean")]
    [InlineData(SignatureSpellsItsBinding, "clean")]
    [InlineData(BindingsNameEachOthersConformance, "clean")]
    public void ABindingNamingItsConformanceIsValid(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // SPEC 8.6.1, 8.3.1, 8.3.3 (PLAN G629): a capability of an instance rests on the instance's formation, which the conformance being
    // verified cannot supply: P's Copy or Owned through its stored Box<S> needs S is C, the conformance whose bound asks for it, in either
    // declaration order; with no Constraint on Box nothing depends on S is C.
    [Theory]
    [InlineData("contract C\n    associate Item is Copy\nstruct Box<T>\n    T is C\n    Self is Copy\nstruct P\n    Self is Copy\n    public var b: Box<S>\nstruct S\n    Self is C\n    associate C.Item is P\n", false)]
    [InlineData("contract C\n    associate Item is Copy\nstruct S\n    Self is C\n    associate C.Item is P\nstruct P\n    Self is Copy\n    public var b: Box<S>\nstruct Box<T>\n    T is C\n    Self is Copy\n", false)]
    [InlineData("contract C\n    associate Item is Copy\nstruct Box<T>\n    Self is Copy\nstruct P\n    Self is Copy\n    public var b: Box<S>\nstruct S\n    Self is C\n    associate C.Item is P\n", true)]
    [InlineData("contract C\n    associate Item is Owned\nstruct Box<T>\n    T is C\nstruct P\n    public var b: Box<S>\nstruct S\n    Self is C\n    associate C.Item is P\n", false)]
    [InlineData("contract C\n    associate Item is Owned\nstruct S\n    Self is C\n    associate C.Item is P\nstruct P\n    public var b: Box<S>\nstruct Box<T>\n    T is C\n", false)]
    [InlineData("contract C\n    associate Item is Owned\nstruct Box<T>\n    public var n: i32 = 0\nstruct P\n    public var b: Box<S>\nstruct S\n    Self is C\n    associate C.Item is P\n", true)]
    public void ACapabilityOfAnInstanceRestsOnItsFormation(string source, bool valid)
        => Assert.Equal(valid, CompilationTestHelper.ParseSuccess(source).Bind().IsComplete);

    // SPEC 8.3.2, 8.3.3 Finality (R7-C2e2 e2-5a): `S is C`'s bound asks `Wrap<i32> is Copy or K`, and Wrap's Copy needs its stored
    // Box<S>, whose formation is `S is C`. Read while `S is C` is open the Copy supplies nothing, and it is judged again afterwards, so
    // no declaration order changes the result: with Wrap's own K the bound holds and `w` is copied at a call and a capture; without it
    // the bound rests on the conformance it proves, and the program is invalid in every order.
    [Theory]
    [MemberData(nameof(DeclarationOrders))]
    public void ACapabilityReadUnderAnOpenObligationIsJudgedAgain(int order, string bound, bool k)
    {
        var blocks = new List<string>
        {
            "struct S\n    Self is C\n    associate C.Item is Wrap<i32>\n",
            $"struct Wrap<T>\n    Self is Copy\n{(k ? "    Self is K\n" : string.Empty)}    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n",
            "struct Box<T>\n    T is C\n    Self is Copy\n    public init() => ()\n",
            $"contract K\ncontract C\n    associate Item is {bound}\n",
        };
        var source = string.Empty;
        for (var radix = blocks.Count; radix > 0; order /= radix--)
        {
            source += blocks[order % radix];
            blocks.RemoveAt(order % radix);
        }

        source += "func twice<V>(v: V) -> (V, V)\n    V is Copy\n    return (v, v)\nfunc count(w: Wrap<i32>) -> i32 => 2\n" +
            "func use() -> ()\n    let w = Wrap<i32>.init()\n    let t = twice(w)\n" +
            "func capture() -> ()\n    let w = Wrap<i32>.init()\n    let f = func () -> i32 => 1 + count(w)\n";
        Assert.Equal(k, DiagnosticCorpus.RecordProfile(source) == "clean");
    }

    // SPEC 8.3.3 Recursion, O-78 (a) (PLAN G629's F2, R7-C2e2 e2-5a's engine step): `S is C` needs `S is Owned` (C's Constraint), whose
    // storage writes Box<S>, formed only by `S is C`: the conformance being proven. A nominal proof never reads Owned's coinductive
    // assumption (it is read only between assumed Owned works, through no newly opened obligation), so each is invalid, whichever is
    // asked first (the Owned declaration, another Type storing Box<S>, an associated binding, a generic Self) and in the other declaration
    // order: the judgments on the cycle (`Self is Owned`, `Self is C`, the stored Box<S>) are unproven at their lines, and every other
    // record is a dependent InvalidConstraint_Kd.
    [Theory]
    [InlineData("h-own-first-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct S\n    Self is Owned\n    Self is C\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", "7 8 9")]
    [InlineData("k-a1o-own-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct S\n    Self is C\n    Self is Owned\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", "7 9")]
    [InlineData("h-other-first-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct T\n    Self is Owned\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\nstruct S\n    Self is C\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", "7 8 11 12")]
    [InlineData("h-other-last-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct S\n    Self is C\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\nstruct T\n    Self is Owned\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", "7 8 12")]
    [InlineData("h-own-first-assoc-n", "contract C\n    associate Item is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct S\n    Self is Owned\n    Self is C\n    associate C.Item is S\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", "7 8 10")]
    [InlineData("h-own-first-gen-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct G<T>\n    T is Owned\n    Self is Owned\n    Self is C\n    public var b: Box<G<T>>\n", "8 9 10")]
    public void ANominalProofReadsNoCoinductiveAssumption(string id, string source, string unproven)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete, id);
        c.Binding.ReportDiagnostics();
        var records = TestDiagnostics.Of(c);
        var lines = records.Where(static x => x.Code == "UnprovenConstraint_Kd").Select(x => source.AsSpan(0, x.Span.Start).Count('\n') + 1).Order();
        Assert.Equal(unproven, string.Join(' ', lines));
        Assert.All(records, static x => Assert.Contains(x.Code, (string[])["UnprovenConstraint_Kd", "InvalidConstraint_Kd"]));
    }

    // The coverage table's rows without a theory of their own (R7-C2e2 e2-5a, `coverage.md`, reused by name): SPEC 8.3.3 Recursion's
    // boundary (C states no Constraint, so S is C holds alone and Box<S> is formed), 15.3.5 Fixed points and 8.3.1 (a constrained
    // instance behind a storage hop is formed once in its declaration's formals, F1's six invalid, the controls valid; non-regular
    // recursive storage valid, R1's five, with two growing Fields or two declarations growing into each other), 8.3.3 Finality (a stored
    // instance judged under the declaration's premises; a conditional conformance on an Owned cycle, `C1<D> is K when D is Owned` with D
    // storing HK<C1<D>>, holds only with itself held pending, in either declaration order, and holds when unconditional), the boundary
    // of 15.3.5 (Owned of an abstract Type is judged by premises, never assumed: abstract-owned-*) and a growth-guard cut (PLAN G200).
    [Theory]
    [InlineData("f2-own-v", "contract C\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct S\n    Self is Owned\n    Self is C\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", true)]
    [InlineData("h-other-first-v", "contract C\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct T\n    Self is Owned\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\nstruct S\n    Self is C\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", true)]
    [InlineData("f2-assoc-v", "contract C\n    associate Item\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct S\n    Self is Owned\n    Self is C\n    associate C.Item is S\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\n", true)]
    [InlineData("f2-gen-v", "contract C\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct G<T>\n    T is Owned\n    Self is Owned\n    Self is C\n    public var b: Box<G<T>>\n", true)]
    [InlineData("a1o-hop2-n", "contract C\n    associate Item is Owned\nstruct Box<T>\n    T is C\nstruct P\n    public var b: Box<S>\nstruct Q\n    public var p: P\nstruct S\n    Self is C\n    associate C.Item is Q\n", false)]
    [InlineData("a1o-hop2-gen-n", "contract C\n    associate Item is Owned\nstruct Box<T>\n    T is C\nstruct P\n    public var b: Box<S>\nstruct Q<X>\n    public var p: P\nstruct S\n    Self is C\n    associate C.Item is Q<i32>\n", false)]
    [InlineData("a1o-hop2-opt-n", "contract C\n    associate Item is Owned\nstruct Box<T>\n    T is C\nstruct P\n    public var b: Box<S>\nstruct Q\n    public var p: Option<P>\nstruct S\n    Self is C\n    associate C.Item is Q\n", false)]
    [InlineData("w-wrap-owned-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct Wrap<T>\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\nstruct S\n    Self is C\n    public var w: Wrap<i32>\n    public init() => self.w = Wrap<i32>.init()\n", false)]
    [InlineData("w-wrap-owned-plain-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nstruct Wrap\n    public var b: Box<S>\n    public init() => self.b = Box<S>.init()\nstruct S\n    Self is C\n    public var w: Wrap\n    public init() => self.w = Wrap.init()\n", false)]
    [InlineData("w-enum-owned-n", "contract C\n    Self is Owned\nstruct Box<T>\n    T is C\n    public init() => ()\nenum E\n    A(Box<S>)\n    B\nstruct S\n    Self is C\n    public var e: E\n    public init() => self.e = .B\n", false)]
    [InlineData("f1-hop2-v", "contract C\n    associate Item is Owned\nstruct Box<T>\n    T is C\nstruct P\n    public var b: Box<S>\nstruct Q\n    public var p: P\nstruct S\n    Self is C\n    associate C.Item is i32\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(q: ref/Q) -> ()\n    needOwned(q)\n", true)]
    [InlineData("f1-hop2-rev-v", "func needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(q: ref/Q) -> ()\n    needOwned(q)\nstruct Q\n    public var p: P\nstruct P\n    public var b: Box<S>\nstruct S\n    Self is C\n    associate C.Item is i32\nstruct Box<T>\n    T is C\ncontract C\n    associate Item is Owned\n", true)]
    [InlineData("f1-ork-v-o00", "struct Box<T>\n    T is C\nstruct P\n    public var b: Box<S>\nstruct Wrap\n    Self is K\n    public var p: P\nstruct S\n    Self is C\n    associate C.Item is Wrap\ncontract K\ncontract C\n    associate Item is Owned or K\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(w: ref/Wrap) -> ()\n    needOwned(w)\n", true)]
    [InlineData("f1-ork-n-o00", "struct Box<T>\n    T is C\nstruct P\n    public var b: Box<S>\nstruct Wrap\n    public var p: P\nstruct S\n    Self is C\n    associate C.Item is Wrap\ncontract K\ncontract C\n    associate Item is Owned or K\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(w: ref/Wrap) -> ()\n    needOwned(w)\n", false)]
    [InlineData("g1-nouse", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct R<T>\n    T is Owned\n    let next: Array<S<R<Wrap<T>>>>\n", true)]
    [InlineData("g1-param", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct R<T>\n    T is Owned\n    let next: Array<S<R<Wrap<T>>>>\nfunc take(r: ref/R<i32>) -> () => ()\n", true)]
    [InlineData("g2-wrap2", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct R<T>\n    T is Owned\n    let next: Array<S<R<Wrap<Wrap<T>>>>>\n", true)]
    [InlineData("g3-deep", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct Hold2<A>\n    public var a: A\nstruct R<T>\n    T is Owned\n    let next: Array<Hold2<S<R<Wrap<T>>>>>\n", true)]
    [InlineData("g5-gendef", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct R<T>\n    T is Owned\n    let next: Array<S<R<Wrap<T>>>>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc f<X>(r: ref/R<X>) -> ()\n    X is Owned\n    needOwned(r)\n", true)]
    [InlineData("r2-prem-v", "contract K\nstruct S<U>\n    U is K\n    public var u: U\nstruct A\n    Self is K\nstruct R<T>\n    T is K\n    let next: Array<S<T>>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<A>) -> ()\n    needOwned(r)\n", true)]
    [InlineData("r2-prem-n", "contract K\nstruct S<U>\n    U is K\n    public var u: U\nstruct A\n    Self is K\nstruct R<T>\n    let next: Array<S<T>>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<A>) -> ()\n    needOwned(r)\n", false)]
    [InlineData("r2-closed-v", "contract K\nstruct S<U>\n    U is K\n    public var u: U\nstruct A\n    Self is K\nstruct R<T>\n    let t: T\n    let next: Array<S<A>>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<i32>) -> ()\n    needOwned(r)\n", true)]
    [InlineData("r2-closed-n", "contract K\nstruct S<U>\n    U is K\n    public var u: U\nstruct B\n    public var x: i32\nstruct R<T>\n    let t: T\n    let next: Array<S<B>>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<i32>) -> ()\n    needOwned(r)\n", false)]
    [InlineData("r2-enum-v", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nenum R<T>\n    T is Owned\n    A(Array<S<R<Wrap<T>>>>)\n    B\n", true)]
    [InlineData("abstract-owned-n", "contract C\n    associate Item\nstruct S<U>\n    U is Owned\n    public var u: U\nstruct R<T>\n    T is C\n    let next: Array<S<T.(C).Item>>\nstruct Q\n    Self is C\n    associate C.Item is R<Q>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<Q>) -> ()\n    needOwned(r)\n", false)]
    [InlineData("abstract-owned-bound-n", "contract C\n    associate Item is Owned\nstruct S<U>\n    U is Owned\n    public var u: U\nstruct R<T>\n    T is C\n    let next: Array<S<T.(C).Item>>\nstruct Q\n    Self is C\n    associate C.Item is R<Q>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<Q>) -> ()\n    needOwned(r)\n", false)]
    [InlineData("abstract-owned-v", "contract C\n    associate Item\nstruct S<U>\n    U is Owned\n    public var u: U\nstruct R<T>\n    T is C\n    T.(C).Item is Owned\n    let next: Array<S<T.(C).Item>>\nstruct Q\n    Self is C\n    associate C.Item is R<Q>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(r: ref/R<Q>) -> ()\n    needOwned(r)\n", true)]
    [InlineData("g200-copy-growth-v", "contract Has\n    associate Out\nstruct W<X>\n    Self is Has\n    associate Has.Out is G<W<(X, X)>>\nstruct G<T>\n    Self is Copy when T is Has, T.(Has).Out is Copy\nfunc inspect(x: G<W<i32>>) => ()\n", true)]
    [InlineData("g-rf4o-nouse", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct Pair<A>\n    public var a: A\nstruct R<T>\n    T is Owned\n    let next: Array<S<R<Wrap<T>>>>\n    let other: Array<S<R<Pair<T>>>>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\n", true)]
    [InlineData("g-two-decl", "struct S<U>\n    U is Owned\n    public var u: U\nstruct Wrap<A>\n    public var a: A\nstruct R1<T>\n    T is Owned\n    let next: Array<S<R2<Wrap<T>>>>\nstruct R2<T>\n    T is Owned\n    let next: Array<S<R1<Wrap<T>>>>\nfunc needOwned<V>(v: ref/V)\n    V is Owned\n    ()\n", true)]
    [InlineData("k8-condring-n", "contract K\nstruct HK<A>\n    A is K\n    public init() => ()\nstruct C1<X>\n    Self is K when X is Owned\n    public init() => ()\nstruct D\n    let h: HK<C1<D>>\n", false)]
    [InlineData("k8-condring-rev-n", "struct D\n    let h: HK<C1<D>>\ncontract K\nstruct HK<A>\n    A is K\n    public init() => ()\nstruct C1<X>\n    Self is K when X is Owned\n    public init() => ()\n", false)]
    [InlineData("k8-condring-free-v", "contract K\nstruct HK<A>\n    A is K\n    public init() => ()\nstruct C1<X>\n    Self is K\n    public init() => ()\nstruct D\n    let h: HK<C1<D>>\n", true)]
    public void AnEngineCoverageRowKeepsItsLabel(string id, string source, bool valid)
        => Assert.True(valid == (DiagnosticCorpus.RecordProfile(source) == "clean"), id);

    // SPEC 8.3.3 Finality ("a fact depends on a judgment when it does not hold with that judgment's result held pending"), 8.3.2
    // Disjunction (R7-C2e2 e2-3b): an answer that read the obligation being proven and is still Proven holds with it pending, so it is
    // evidence: a formation `T is C or D` with S both (either order), a second conditional path through `S is M`, a path beside a
    // dependent one, and a capability (Copy, Owned) of an instance whose formation reads `S is C` in a dead operand; with the other
    // operand refuted the answer is Unknown and the program invalid.
    [Theory]
    [InlineData("q2f", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C or D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n", true)]
    [InlineData("q2fr", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is D or C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n", true)]
    [InlineData("q2p", "contract M\ncontract D\ncontract D2: D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Wrap<T>\n    Self is D when T is C\n    Self is D2 when T is M\nstruct Box<T>\n    Self is K when T is D\n        associate K.X is R\nstruct S\n    Self is C\n    Self is M\n    associate C.Item is Box<Wrap<S>>.(K).X\n", true)]
    [InlineData("q3a", "contract M\ncontract K\n    associate X\ncontract K2: K\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K when T is C\n        associate K.X is R\n    Self is K2 when T is M\n        associate K.X is R\nstruct S\n    Self is C\n    Self is M\n    associate C.Item is Box<S>.(K).X\n", true)]
    [InlineData("cap1", "contract D\ncontract C\n    associate Item is Copy\nstruct Box<T>\n    T is C or D\n    Self is Copy\n    var value: i32\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>\n", true)]
    [InlineData("own1", "contract D\ncontract C\n    associate Item is Owned\nstruct Node<T>\n    T is C or D\n    var next: Option<rc/Node<T>>\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Node<S>\n", true)]
    [InlineData("own1r", "contract D\ncontract C\n    associate Item is Owned\nstruct Node<T>\n    T is D or C\n    var next: Option<rc/Node<T>>\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Node<S>\n", true)]
    [InlineData("cap1n", "contract D\ncontract E\ncontract C\n    associate Item is Copy\nstruct Box<T>\n    T is C or E\n    Self is Copy\n    var value: i32\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>\n", false)]
    [InlineData("own1n", "contract D\ncontract E\ncontract C\n    associate Item is Owned\nstruct Node<T>\n    T is C or E\n    var next: Option<rc/Node<T>>\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Node<S>\n", false)]
    public void ADecidedAnswerReadingTheObligationIsEvidence(string id, string source, bool valid)
        => Assert.True(valid == (DiagnosticCorpus.RecordProfile(source) == "clean"), id);

    // SPEC 8.6.1, 8.6.3 (R7-C2e2 e2-3b R2.5): evidence about an instance rests on its declaration's Constraints at its arguments, not
    // on the arguments' own formation, which is their written site's: Box<Box2<S>> is Copy (deepc), Box<Box2<S>> is M (deepr) and the
    // projection route through Box<Box2<S>> (deepv) hold while S is C is verified, though Box2<S> needs S is C; with Box's Constraint
    // `T is D` and Box2 is D when T is C, Box<Box2<S>> is Copy rests on S is C (deepcn).
    [Theory]
    [InlineData("deepc", "contract C\n    associate Item is Copy\nstruct Box2<T>\n    T is C\nstruct Box<T>\n    Self is Copy\n    var value: i32\nstruct S\n    Self is C\n    associate C.Item is Box<Box2<S>>\n", true)]
    [InlineData("deepcn", "contract D\ncontract C\n    associate Item is Copy\nstruct Box2<T>\n    Self is D when T is C\nstruct Box<T>\n    T is D\n    Self is Copy\n    var value: i32\nstruct S\n    Self is C\n    associate C.Item is Box<Box2<S>>\n", false)]
    [InlineData("deepr", "contract M\ncontract C\n    associate Item is M\nstruct Box2<T>\n    T is C\nstruct Box<T>\n    Self is M\nstruct S\n    Self is C\n    associate C.Item is Box<Box2<S>>\n", true)]
    [InlineData("deepv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box2<T>\n    T is C\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<Box2<S>>.(K).X\n", true)]
    public void AnInstanceRestsOnItsDeclarationsConstraintsAtItsArguments(string id, string source, bool valid)
        => Assert.True(valid == (DiagnosticCorpus.RecordProfile(source) == "clean"), id);

    // SPEC 8.6.3, 8.3.3 Finality (R7-C2e2 e2-3b Revision 4): a requirement's evidence rests on the parts of the specified Types its
    // judgments read, a part named by a projection on that projection's route (8.5.2); in the evidence, a route depending on the
    // conformance being verified leaves its projection unresolved, so only a judgment reading that part supplies none. Valid, no judgment
    // reads such a part: through the binding cache (pp1, oa2), nested parts (pp3, nst1, nst2), a conjunct (pc1, oa3, tj1v), several
    // identity atoms (8.5.1: pid5v, nqi, nqq, nqself, cid2), a part decided later in the pass (pp4, pl1), a reentrant read (rr2, rr2c,
    // rr2s, rr3), the declarations reversed (ord2) and C-391's controls. An inferred binding's evidence is PLAN G1001's
    // (AnInferredBindingIsReadAsWrittenByAnEvidence).
    [Theory]
    [InlineData("cellkp", "contract K\n    associate X\ncontract D\n    associate Item\nstruct Cell<T>\n    T is D\n    Self is K\n    associate K.X is i32\nstruct R\n    Self is D\n    associate D.Item is Cell<R>.(K).X\n")]
    [InlineData("cellkpg", "contract K\n    associate X\ncontract D\n    associate Item\nstruct Cell<T>\n    T is D\n    Self is K\n    associate K.X is i32\nstruct R\n    Self is D\n    associate D.Item is Cell<R>.(K).X\nfunc get(x: R.(D).Item) -> i32\n    return x\n")]
    [InlineData("nq1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n")]
    [InlineData("nq1g", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\nfunc get(x: S.(C).Item) -> R\n    return x@move\n")]
    [InlineData("nq1b", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n    public func mk(self: ref/Self, x: R) -> R\n        let y: Box<S>.(K).X = x@move\n        return y@move\n")]
    [InlineData("nq3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K when T is C\n        associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n")]
    [InlineData("nq3g", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K when T is C\n        associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\nfunc get(x: S.(C).Item) -> R\n    return x@move\n")]
    [InlineData("ncc", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\ncontract D\nstruct Wrap<T>\n    Self is D when T is C\nstruct Box<T>\n    Self is K when T is D\n        associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<Wrap<S>>.(K).X\n")]
    [InlineData("implq", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self, x: Self.Item) -> Self.Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n    public func get(self: ref/Self, x: Box<S>.(K).X) -> Box<S>.(K).X => x@move\n")]
    [InlineData("otherv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract D\n    associate Y\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n    associate D.Y is Box2<S>.(K).X\n")]
    [InlineData("circ2v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract D\n    associate Y\nstruct Box<T>\n    T is C or D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n    associate D.Y is Box<S>.(K).X\n")]
    [InlineData("nqav", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X and M\n")]
    [InlineData("nq1d", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\ncontract P\n    func take(self: ref/Self, x: Box<S>.(K).X) -> R\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is P\n    associate C.Item is Box<S>.(K).X\n    public func take(self: ref/Self, x: Box<S>.(K).X) -> R => x@move\n")]
    [InlineData("d1", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Wrap<T>\n    T is C or D\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is C\n    associate C.Item is Wrap<S>\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<Q>.(K).X\n")]
    [InlineData("d1r", "struct S\n    Self is C\n    Self is D\n    associate C.Item is Box<Q>.(K).X\nstruct Q\n    Self is C\n    associate C.Item is Wrap<S>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Wrap<T>\n    T is C or D\n    Self is M\nstruct R\n    Self is M\ncontract C\n    associate Item is M\ncontract K\n    associate X\ncontract D\ncontract M\n")]
    [InlineData("stq", "contract M\ncontract K\n    associate X\ncontract D\ncontract C: D\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is D\n    Self is C\n    associate C.Item is Box<S>.(K).X\n")]
    [InlineData("stq2", "contract M\ncontract K\n    associate X\ncontract D\ncontract C: D\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n")]
    [InlineData("nqi", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X and Box<S>.(K).X\n")]
    [InlineData("nqip", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is (Box<S>.(K).X and Box<S>.(K).X)\n")]
    [InlineData("nqp", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is (Box<S>.(K).X)\n")]
    [InlineData("reqf", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Box<Self>.(K).X\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n")]
    [InlineData("nqs2v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n    associate C.Item is M\n")]
    [InlineData("nqad", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\ncontract D\nstruct R\n    Self is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X and M\n")]
    [InlineData("ind1v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n")]
    [InlineData("ind2w", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\ncontract E\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is D\n    associate D.Y is Box<S>.(K).X\nstruct S\n    Self is C\n    Self is E\n    associate C.Item is Q.(D).Y\n")]
    [InlineData("ind3v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\ncontract E\n    associate Z\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    Self is E\n    associate E.Z is Box<S>.(K).X\n    associate D.Y is S.(E).Z\n    associate C.Item is S.(D).Y\n")]
    [InlineData("ind1av", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y and M\n")]
    [InlineData("ind1ov", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract C2\n    associate W\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct Z\n    Self is C2\n    associate C2.W is S.(D).Y\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n")]
    [InlineData("ind1g", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\nfunc get(x: S.(C).Item) -> R => x@move\n")]
    [InlineData("cidv", "contract M\ncontract K\n    associate X\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\ncontract C\n    associate Item is M\n    Self.(C).Item is Box<Self>.(K).X\nstruct S\n    Self is C\n")]
    [InlineData("oa1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Box<S>.(K).X, i32>\n")]
    [InlineData("oa2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is Pair<S.(D).Y, i32>\n")]
    [InlineData("oa3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Box<S>.(K).X, i32> and M\n")]
    [InlineData("oa3r", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is M and Pair<Box<S>.(K).X, i32>\n")]
    [InlineData("rr2", "contract M\ncontract E\n    associate W\ncontract F\ncontract K\n    associate X\ncontract K2\n    associate X2\ncontract C\n    associate Item is M\nstruct R\nstruct R2\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is F\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is E\n    Self is K2\n    associate K2.X2 is R2\nstruct S\n    Self is C\n    Self is E\n    Self is F\n    associate C.Item is Pair<Box<S>.(K).X, Box2<S>.(K2).X2>\n    associate E.W is S.(C).Item\n")]
    [InlineData("rr2c", "contract M\ncontract E\n    associate W\ncontract F\ncontract K\n    associate X\ncontract K2\n    associate X2\ncontract C\n    associate Item is M\nstruct R\nstruct R2\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is F\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is E\n    Self is K2\n    associate K2.X2 is R2\nstruct S\n    Self is C\n    Self is E\n    Self is F\n    associate C.Item is Pair<Box<S>.(K).X, Box2<S>.(K2).X2>\n    associate E.W is R2\n")]
    [InlineData("rr2s", "contract M\ncontract E\n    associate W\ncontract F\ncontract K\n    associate X\ncontract K2\n    associate X2\ncontract C\n    associate Item is M\nstruct R\nstruct R2\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is F\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is E\n    Self is K2\n    associate K2.X2 is R2\nstruct S\n    Self is C\n    Self is E\n    Self is F\n    associate C.Item is Pair<Box2<S>.(K2).X2, Box<S>.(K).X>\n    associate E.W is S.(C).Item\n")]
    [InlineData("pid2v", "contract K\n    associate X\ncontract C\n    associate Item\nstruct R\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is R and Box<S>.(K).X\n")]
    [InlineData("pl1", "contract M\ncontract K\n    associate X\ncontract E\n    associate W\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is E\n    Self is C\n    Self is D\n    associate E.W is S.(D).Y\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n")]
    [InlineData("rt2v", "contract M\ncontract E\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    associate Item2 is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    Self is E\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n    associate C.Item2 is S.(D).Y\n")]
    [InlineData("rx2", "contract M\ncontract E\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract C2\n    associate V is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is C2\n    associate C2.V is S.(D).Y\nstruct S\n    Self is C\n    Self is D\n    Self is E\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n")]
    [InlineData("pp1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is Pair<Q, S.(D).Y>\n")]
    [InlineData("pp3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Pair<Q, Box<S>.(K).X>, i32>\n")]
    [InlineData("pc1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Q, Box<S>.(K).X> and M\n")]
    [InlineData("pid5v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Q, R> and Pair<Q, Box<S>.(K).X>\n")]
    [InlineData("cidn", "contract K\n    associate X\ncontract C\n    associate Item\n    Self.(C).Item is Box<Self>.(K).X\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n")]
    [InlineData("pp4", "contract M\ncontract E\n    associate W is M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is E\n    associate C.Item is Box<S>.(K).X\n    associate E.W is R\n")]
    [InlineData("tj1v", "contract M\ncontract N\ncontract K\n    associate X\ncontract C\n    associate Item is M and N\nstruct R\n    Self is N\nstruct Pair<A, B>\n    Self is M\n    Self is N when B is N\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Box<S>.(K).X, R>\n")]
    [InlineData("nst1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Pair<Box<S>.(K).X, i32>, i32>\n")]
    [InlineData("nst2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when B is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Pair<Box<S>.(K).X, R>\n    associate C.Item is Pair<i32, S.(D).Y>\n")]
    [InlineData("nqq", "contract K\n    associate X\ncontract C\n    associate Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X and Box<S>.X\n")]
    [InlineData("nqself", "contract K\n    associate X\ncontract C\n    associate Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<Self>.(K).X and Box<S>.(K).X\n")]
    [InlineData("cid2", "contract K\n    associate X\ncontract C\n    associate Item\n    Self.(C).Item is Box<Self>.(K).X\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n")]
    [InlineData("rr3", "contract M\ncontract E\n    associate W is M\ncontract F\ncontract K\n    associate X\ncontract K2\n    associate X2\ncontract C\n    associate Item is M\nstruct R\nstruct R2\nstruct Pair<A, B>\n    Self is M\nstruct Box<T>\n    T is F\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is E\n    Self is K2\n    associate K2.X2 is R2\nstruct S\n    Self is C\n    Self is E\n    Self is F\n    associate C.Item is Pair<Box<S>.(K).X, Box2<S>.(K2).X2>\n    associate E.W is S.(C).Item\n")]
    [InlineData("ord2", "struct S\n    Self is D\n    Self is C\n    associate C.Item is Pair<S.(D).Y, i32>\n    associate D.Y is Box<S>.(K).X\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Pair<A, B>\n    Self is M\nstruct R\n    Self is M\ncontract D\n    associate Y\ncontract C\n    associate Item is M\ncontract K\n    associate X\ncontract M\n")]
    public void AnEvidenceReadingNoDependentPartHolds(string id, string source)
        => Assert.True(Errors(source).Length == 0, id);

    // SPEC 8.6.3, 8.5.1 (R7-C2e2 e2-3b Revision 4; PLAN G629, G995): the invalid twins: a judgment reads a part whose route rests on the
    // conformance being verified, so the evidence supplies none: the n-forms, through the cache (pp1n, ind1), nested parts (pp3n, nst1n,
    // nst2n), a conjunct (pc1n, nqa, tj1), an identity atom other than the one the binding is formed from (pid2, pid5, cid), a part
    // decided later (pp4n, pl1n), a reentrant read (rr3n), the declarations reversed (ord1) and an unbounded Type through bindings (cyc1,
    // cyc2: no fault). Each pins its independent Error records (Records: UnprovenConstraint_Kd at the conformance; a failure the
    // conformance's implementation check owns, IncompatibleContractImplementation_Kd there with the result's formation).
    [Theory]
    [InlineData("n1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("n1r", "struct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct R\n    Self is M\ncontract C\n    associate Item is M\ncontract K\n    associate X\ncontract M\n", "UnprovenConstraint_Kd@2:5")]
    [InlineData("n3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K when T is C\n        associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@12:5")]
    [InlineData("n5", "contract K\n    associate X\ncontract C\n    associate Item is Copy\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@10:5")]
    [InlineData("cc1", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Wrap<T>\n    Self is D when T is C\nstruct Box<T>\n    Self is K when T is D\n        associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<Wrap<S>>.(K).X\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("q2fn", "contract M\ncontract D\ncontract E\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C or E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("q2pn", "contract M\ncontract D\ncontract D2: D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Wrap<T>\n    Self is D when T is C\n    Self is D2 when T is M\nstruct Box<T>\n    Self is K when T is D\n        associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<Wrap<S>>.(K).X\n", "UnprovenConstraint_Kd@17:5")]
    [InlineData("q2fn-noD", "contract M\ncontract D\ncontract E\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C or E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("q2fn-Dfirst", "contract M\ncontract D\ncontract E\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C or E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is D\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@16:5")]
    [InlineData("n1+D", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@14:5")]
    [InlineData("n3+D", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K when T is C\n        associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("q2pn+N", "contract M\ncontract N\ncontract D\ncontract D2: D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Wrap<T>\n    Self is D when T is C\n    Self is D2 when T is M\nstruct Box<T>\n    Self is K when T is D\n        associate K.X is R\nstruct S\n    Self is C\n    Self is N\n    associate C.Item is Box<Wrap<S>>.(K).X\n", "UnprovenConstraint_Kd@18:5")]
    [InlineData("mut2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S1\n    Self is C\n    associate C.Item is Box<S2>.(K).X\nstruct S2\n    Self is C\n    associate C.Item is Box<S1>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("mut3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S1\n    Self is C\n    associate C.Item is Box<S2>.(K).X\nstruct S2\n    Self is C\n    associate C.Item is Box<S3>.(K).X\nstruct S3\n    Self is C\n    associate C.Item is Box<S1>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("gen1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct G<U>\n    Self is C\n    associate C.Item is Box<G<U>>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("gen3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct G<U>\n    Self is C when U is M\n        associate C.Item is Box<G<U>>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("cnj", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract D\nstruct Box<T>\n    T is C and D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@14:5")]
    [InlineData("cpyn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C or Copy\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("deepn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract D\nstruct Box2<T>\n    Self is D when T is C\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<Box2<S>>.(K).X\n", "UnprovenConstraint_Kd@16:5")]
    [InlineData("other", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract D\n    associate Y is M\nstruct Box<T>\n    T is D\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n    associate D.Y is Box2<S>.(K).X\n", "UnprovenConstraint_Kd@19:5")]
    [InlineData("circ2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract D\n    associate Y is M\nstruct Box<T>\n    T is C or D\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>.(K).X\n    associate D.Y is Box<S>.(K).X\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("genqn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract D\ncontract E\nstruct Box<T>\n    T is C or E\n    Self is K\n    associate K.X is R\nstruct G<U>\n    Self is C\n    Self is D\n    associate C.Item is Box<G<U>>.(K).X\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("refn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\ncontract C2: C\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C2\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@14:5")]
    [InlineData("two2n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item1 is M\n    associate Item2 is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item1 is Box<S>.(K).X\n    associate C.Item2 is R\n", "UnprovenConstraint_Kd@14:5")]
    [InlineData("impl1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self, x: Self.Item) -> Self.Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n    public func get(self: ref/Self, x: Box<S>.(K).X) -> Box<S>.(K).X => x@move\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@16:17")]
    [InlineData("d1n", "contract M\ncontract D\ncontract E\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Wrap<T>\n    T is C or E\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is C\n    associate C.Item is Wrap<S>\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<Q>.(K).X\n", "UnprovenConstraint_Kd@18:5")]
    [InlineData("d1nr", "struct S\n    Self is C\n    Self is D\n    associate C.Item is Box<Q>.(K).X\nstruct Q\n    Self is C\n    associate C.Item is Wrap<S>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Wrap<T>\n    T is C or E\n    Self is M\nstruct R\n    Self is M\ncontract C\n    associate Item is M\ncontract K\n    associate X\ncontract D\ncontract E\ncontract M\n", "UnprovenConstraint_Kd@2:5")]
    [InlineData("nqa", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X and M\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("nqar", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is M and Box<S>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("nqa3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X and M and Box<S>.(K).X\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("nqs2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n    associate C.Item is M\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("ccq", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\n    Self.(C).Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@14:5")]
    [InlineData("cid", "contract M\ncontract K\n    associate X\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\ncontract C\n    associate Item is M\n    Self.(C).Item is Box<Self>.(K).X\nstruct S\n    Self is C\n", "UnprovenConstraint_Kd@14:5")]
    [InlineData("ind1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("ind1r", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is D\n    Self is C\n    associate C.Item is S.(D).Y\n    associate D.Y is Box<S>.(K).X\n", "UnprovenConstraint_Kd@16:5")]
    [InlineData("ind2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is D\n    associate D.Y is Box<S>.(K).X\nstruct S\n    Self is C\n    associate C.Item is Q.(D).Y\n", "UnprovenConstraint_Kd@18:5")]
    [InlineData("ind3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\ncontract E\n    associate Z\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    Self is E\n    associate E.Z is Box<S>.(K).X\n    associate D.Y is S.(E).Z\n    associate C.Item is S.(D).Y\n", "UnprovenConstraint_Kd@17:5")]
    [InlineData("ind1c", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\n    Self.(C).Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n", "UnprovenConstraint_Kd@16:5")]
    [InlineData("ind1a", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y and M\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("ind1o", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract C2\n    associate W\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Z\n    Self is C2\n    associate C2.W is S.(D).Y\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n", "UnprovenConstraint_Kd@20:5")]
    [InlineData("oa1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Box<S>.(K).X, i32>\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("pid2", "contract K\n    associate X\ncontract C\n    associate Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is R and Box<S>.(K).X\n", "UnprovenConstraint_Kd@11:5")]
    [InlineData("pid2r", "contract K\n    associate X\ncontract C\n    associate Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X and R\n", "UnprovenConstraint_Kd@11:5")]
    [InlineData("pid3", "contract K\n    associate X\ncontract C\n    associate Item\ncontract D\n    associate Y\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is R and S.(D).Y\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("pid2s", "contract K\n    associate X\ncontract C\n    associate Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is R\n    associate C.Item is Box<S>.(K).X\n", "UnprovenConstraint_Kd@11:5")]
    [InlineData("pid2sr", "contract K\n    associate X\ncontract C\n    associate Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n    associate C.Item is R\n", "UnprovenConstraint_Kd@11:5")]
    [InlineData("cidc", "contract K\n    associate X\ncontract C\n    associate Item\n    Self.(C).Item is Box<Self>.(K).X\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is R\n", "UnprovenConstraint_Kd@12:5")]
    [InlineData("cidr", "contract K\n    associate X\ncontract C\n    Self.(C).Item is Box<Self>.(K).X\n    associate Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is R\n", "UnprovenConstraint_Kd@12:5")]
    [InlineData("pnest", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X and (Box<S>.(K).X and M)\n", "UnprovenConstraint_Kd@13:5")]
    [InlineData("pl1n", "contract M\ncontract K\n    associate X\ncontract E\n    associate W is M\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is E\n    Self is C\n    Self is D\n    associate E.W is S.(D).Y\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n", "UnprovenConstraint_Kd@17:5")]
    [InlineData("rt2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    associate Item2 is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n    associate C.Item2 is S.(D).Y\n", "UnprovenConstraint_Kd@16:5")]
    [InlineData("ind2r", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Q.(D).Y\nstruct Q\n    Self is D\n    associate D.Y is Box<S>.(K).X\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("ind1i", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\ncontract E\n    associate Z\n    func z(self: ref/Self) -> Z\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is E\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is S.(D).Y\n    public func z(self: ref/Self) -> S.(D).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@18:5 UnprovenConstraint_Kd@19:5")] // R7-C3 (F's C2 ruling 283 (d), Q13): S's E conformance keeps its own record (IncompatibleContractImplementation_Kd for its InvalidConstraint_Kd); `Self is D`'s InvalidConstraint_Kd goes, its failure recorded at `associate D.Y is Box<S>.(K).X`
    [InlineData("pp1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    associate C.Item is Pair<S.(D).Y, Q>\n", "UnprovenConstraint_Kd@19:5")]
    [InlineData("pp3n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Pair<Box<S>.(K).X, Q>, i32>\n", "UnprovenConstraint_Kd@17:5")]
    [InlineData("pc1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Box<S>.(K).X, Q> and M\n", "UnprovenConstraint_Kd@17:5")]
    [InlineData("pid5", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Q, R> and Pair<Q, Box<S>.(K).X>\n", "UnprovenConstraint_Kd@16:5")]
    [InlineData("pp4n", "contract M\ncontract E\n    associate W is M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is E\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is E\n    associate C.Item is Box<S>.(K).X\n    associate E.W is S.(C).Item\n", "UnprovenConstraint_Kd@16:5")]
    [InlineData("tj1", "contract M\ncontract N\ncontract K\n    associate X\ncontract C\n    associate Item is M and N\nstruct R\n    Self is N\nstruct Pair<A, B>\n    Self is M\n    Self is N when A is N\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Box<S>.(K).X, i32>\n", "UnprovenConstraint_Kd@17:5")]
    [InlineData("nst1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Pair<Pair<Box<S>.(K).X, i32>, i32>\n", "UnprovenConstraint_Kd@15:5")]
    [InlineData("nst2n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Pair<Box<S>.(K).X, i32>\n    associate C.Item is Pair<S.(D).Y, i32>\n", "UnprovenConstraint_Kd@17:5")]
    [InlineData("rr3n", "contract M\ncontract E\n    associate W is M\ncontract F\ncontract K\n    associate X\ncontract K2\n    associate X2\ncontract C\n    associate Item is M\nstruct R\nstruct R2\n    Self is M\nstruct Pair<A, B>\n    Self is M when B is M\nstruct Box<T>\n    T is F\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    T is E\n    Self is K2\n    associate K2.X2 is R2\nstruct S\n    Self is C\n    Self is E\n    Self is F\n    associate C.Item is Pair<Box<S>.(K).X, Box2<S>.(K2).X2>\n    associate E.W is S.(C).Item\n", "UnprovenConstraint_Kd@26:5")]
    [InlineData("ord1", "struct S\n    Self is D\n    Self is C\n    associate C.Item is S.(D).Y\n    associate D.Y is Box<S>.(K).X\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct R\n    Self is M\ncontract D\n    associate Y\ncontract C\n    associate Item is M\ncontract K\n    associate X\ncontract M\n", "UnprovenConstraint_Kd@3:5")]
    [InlineData("cyc1", "contract M\ncontract C\n    associate Item is M\nstruct S\n    Self is C\n    associate C.Item is Option<S.(C).Item>\n", "")]
    [InlineData("cyc2", "contract M\ncontract C\n    associate Item is M\ncontract D\n    associate Y\nstruct Pair<A, B>\n    Self is M\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Pair<S.(D).Y, i32>\n    associate D.Y is Pair<S.(C).Item, i32>\n", "")]
    public void AnEvidenceReadingADependentPartSuppliesNone(string id, string source, string records)
    {
        var errors = Errors(source);
        Assert.True(errors.Length != 0, id);
        Assert.Equal(records, Records(source, errors));
    }

    // SPEC 8.5.5, 8.6.3 (PLAN G1001): a requirement's evidence reads an inferred binding as its sources wrote it (Binding.AsWritten,
    // the one extraction walk's positions, InferredEvidence), a formation as its normal form: D-780's family (infrv, infn, ovr, infr), the
    // inference beside the cache (pp2, pi1, pp2n, pi1n), the direct inference forms (inf2v-inftupv, inf2n-inftup), the hole in each Type
    // syntax form (fv1-fv3, arr1, opt1, org1 and their twins), a hole inside a Function Type (inffn, inffv: AssociatedTypeInferenceFailed_Kd)
    // and several sources (two1-two4, two6, two6r, two6n; two1-two3, two6 and two6r are invalid by e2 ruling 80, owner G629). Each row
    // is labelled from SPEC and pins its independent Error records (Records); "" is accepted, no Error at all.
    [Theory]
    [InlineData("infrv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("infn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("ovr", "contract M\ncontract M2\ncontract K\n    associate X\ncontract C\n    associate Item\n    associate Other is M2\n    func get(self: ref/Self) -> Pair<Item, Other>\nstruct R\n    Self is M\nstruct R2\n    Self is M2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, R2> => $abort(\"x\")\n", "")] // V
    [InlineData("pp2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Q, Box<S>.(K).X> => $abort(\"x\")\n", "")] // V
    [InlineData("pi1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    public func get(self: ref/Self) -> Pair<Q, S.(D).Y> => $abort(\"x\")\n", "")] // V
    [InlineData("inf2v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, i32>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, i32> => $abort(\"x\")\n", "")] // V
    [InlineData("infrefv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<ref/Item, i32>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<ref/Box<S>.(K).X, i32> => $abort(\"x\")\n", "")] // V
    [InlineData("inftupv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> (Item, i32)\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X, i32) => $abort(\"x\")\n", "")] // V
    [InlineData("fv1", "contract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Pair<Item, (i32) -> i32>\nstruct R\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<R, (Box<S>.(K).X) -> i32> => $abort(\"x\")\n", "")] // V
    [InlineData("fv2", "contract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Pair<Item, (ref/i32) -> i32>\nstruct R\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<R, (ref/Box<S>.(K).X) -> i32> => $abort(\"x\")\n", "")] // V
    [InlineData("fv3", "contract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X) -> i32 => $abort(\"x\")\n", "")] // V
    [InlineData("arr1", "contract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Pair<Item, [2 of i32]>\nstruct R\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<R, [2 of Box<S>.(K).X]> => $abort(\"x\")\n", "")] // V
    [InlineData("arr1v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> [2 of Item]\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> [2 of Box<S>.(K).X] => $abort(\"x\")\n", "")] // V
    [InlineData("opt1", "contract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Pair<Item, i32?>\nstruct R\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<R, Box<S>.(K).X?> => $abort(\"x\")\n", "")] // V
    [InlineData("opt1v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item?\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X? => $abort(\"x\")\n", "")] // V
    [InlineData("org1", "contract K\n    associate X(a)\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Pair<Item, i32>\nstruct R\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X(a) is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Pair<R, Box<S>.(K).X(a)> => $abort(\"x\")\n", "")] // V
    [InlineData("org1v", "contract M\ncontract K\n    associate X(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X(a) is R\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Box<S>.(K).X(a) => $abort(\"x\")\n", "")] // V
    [InlineData("infr", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("pp2n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, Q> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@18:5 UnprovenConstraint_Kd@19:17")] // I
    [InlineData("pi1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Q\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    public func get(self: ref/Self) -> Pair<S.(D).Y, Q> => $abort(\"x\")\n", "UnprovenConstraint_Kd@20:5")] // I
    [InlineData("inf2n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, i32>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, i32> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@15:5 UnprovenConstraint_Kd@16:17")] // I
    [InlineData("inf3n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<Self>.(K).X => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("inf4n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\ncontract D\n    associate Y\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    Self is D\n    associate D.Y is Box<S>.(K).X\n    public func get(self: ref/Self) -> S.(D).Y => $abort(\"x\")\n", "UnprovenConstraint_Kd@16:5")] // I
    [InlineData("inf6n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, i32>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<Self>.(K).X, i32> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@15:5 UnprovenConstraint_Kd@16:17")] // I
    [InlineData("infref", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<ref/Item, i32>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<ref/Box<S>.(K).X, i32> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@15:5 UnprovenConstraint_Kd@16:17")] // I
    [InlineData("inftup", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> (Item, i32)\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X, i32) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("arr1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> [2 of Item]\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> [2 of Box<S>.(K).X] => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("opt1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item?\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X? => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("org1n", "contract M\ncontract K\n    associate X(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X(a) is R\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Box<S>.(K).X(a) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("two1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> R => $abort(\"x\")\n    public func get2(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 accepts it as dev
    [InlineData("two2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get2(self: ref/Self) -> R => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 accepts it as dev
    [InlineData("two3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get2(self: ref/Self) -> R => $abort(\"x\")\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 accepts it as dev
    [InlineData("two4", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get2(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@15:5 UnprovenConstraint_Kd@16:17")] // I
    [InlineData("two6", "contract M\ncontract N\ncontract K\n    associate X\ncontract C\n    associate Item is M and N\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct R\n    Self is M\n    Self is N\nstruct Pair<A, B>\n    Self is M when A is M\n    Self is N when B is N\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, R> => $abort(\"x\")\n    public func get2(self: ref/Self) -> Pair<R, Box<S>.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 accepts it as dev
    [InlineData("two6r", "contract M\ncontract N\ncontract K\n    associate X\ncontract C\n    associate Item is M and N\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct R\n    Self is M\n    Self is N\nstruct Pair<A, B>\n    Self is M when A is M\n    Self is N when B is N\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<R, Box<S>.(K).X> => $abort(\"x\")\n    public func get2(self: ref/Self) -> Pair<Box<S>.(K).X, R> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 accepts it as dev
    [InlineData("two6n", "contract M\ncontract N\ncontract K\n    associate X\ncontract C\n    associate Item is M and N\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct R\n    Self is M\n    Self is N\nstruct Pair<A, B>\n    Self is M when A is M\n    Self is N when B is N\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, R> => $abort(\"x\")\n    public func get2(self: ref/Self) -> Pair<Box<S>.(K).X, Box<S>.(K).X> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@20:5 UnprovenConstraint_Kd@21:17")] // I
    [InlineData("inffn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> (ref/Item) -> i32\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (ref/Box<S>.(K).X) -> i32 => $abort(\"x\")\n", "AssociatedTypeInferenceFailed_Kd@14:5")] // I
    [InlineData("inffv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> (ref/Item) -> i32\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (ref/Box<S>.(K).X) -> i32 => $abort(\"x\")\n", "AssociatedTypeInferenceFailed_Kd@13:5")] // I
    public void AnInferredBindingIsReadAsWrittenByAnEvidence(string id, string source, string records)
    {
        var errors = Errors(source);
        var actual = Records(source, errors);
        Assert.True(records == actual && (records.Length != 0 || errors.Length == 0), $"{id}: {actual} ({errors.Length} Error records)");
    }

    // SPEC 8.5.5, 8.6.3, 8.3.3 (PLAN G1001; design-g1001.md and its reviews' twins, each labelled from SPEC before any run): what a source
    // wrote is read per position and as written: the environment of a nested Type (the qualifier itself, a base layer, the enclosing
    // declaration, three levels), a root path, a Scalar, a group, a Tuple, a Semantics layer, a Function Type, an Origin application, a
    // projection's receiver and Contract argument (a refining selector at one, two and three levels, two parents), the hole inside or
    // beside a written projection, several sources, several positions and anchors (each order family one verdict). Ruling 80's family is
    // invalid by result compatibility and keeps dev's verdict here (G629, e2-5b). Each row pins its independent Error records (Records).
    [Theory]
    [InlineData("envh", "contract M\ncontract K\n    associate X\nstruct Wrap<T>\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U> : Base<Wrap<U>>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Der<Item>.Inner\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "")] // V
    [InlineData("envhn", "contract M\ncontract K\n    associate X\nstruct Wrap<T>\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U> : Base<Wrap<U>>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Der<Item>.Inner\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@19:5 UnprovenConstraint_Kd@20:17")] // I
    [InlineData("wrap2n", "contract K\n    associate X\ncontract C\n    associate Item is Copy\n    func get(self: ref/Self) -> Item\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Wrapping<Box<S>.(K).X> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@11:5 UnprovenConstraint_Kd@12:17")] // I
    [InlineData("nst4c", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U> : Base<U>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@18:5 UnprovenConstraint_Kd@19:17")] // I
    [InlineData("nst3e", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Outer<T>\n    public struct Inner<U>\n        Self is M when U is M\n    public struct S\n        Self is C\n        public func get(self: ref/Self) -> Inner<Box<S>.(K).X> => $abort(\"x\")\n", "")] // V
    [InlineData("pjr", "contract M\ncontract D\n    associate Y is M\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct S<U>\n    U is D\n    Self is C\n    public func get(self: ref/Self) -> U.(D).Y => $abort(\"x\")\n", "")] // V
    [InlineData("noreq", "contract K\n    associate X\ncontract C\n    associate Item\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("ppa", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when B is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, R> => $abort(\"x\")\n", "")] // V
    [InlineData("ppan", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, R> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@16:5 UnprovenConstraint_Kd@17:17")] // I
    [InlineData("nestp", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Y is T\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box2<Box<S>.(K).X>.(K2).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@19:5 UnprovenConstraint_Kd@20:17")] // I
    [InlineData("nestpv", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Y is T\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box2<Box<S>.(K).X>.(K2).Y => $abort(\"x\")\n", "")] // V
    [InlineData("wrap1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Wrapping<i32>, Item>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Wrapping<Box<S>.(K).X>, R> => $abort(\"x\")\n", "")] // V
    [InlineData("wrap1c", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Wrapping<i32>, Item>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Wrapping<Box<S>.(K).X>, R> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("wrap2", "contract K\n    associate X\ncontract C\n    associate Item is Copy\n    func get(self: ref/Self) -> Item\nstruct Box<T>\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Wrapping<Box<S>.(K).X> => $abort(\"x\")\n", "")] // V
    [InlineData("nst3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Outer<T>\n    public struct Inner<U>\n        Self is M\n    public struct S\n        Self is C\n        public func get(self: ref/Self) -> Inner<Box<S>.(K).X> => $abort(\"x\")\n", "")] // V
    [InlineData("nst3n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Outer<T>\n    public struct Inner<U>\n        Self is M when U is M\n    public struct S\n        Self is C\n        public func get(self: ref/Self) -> Inner<Box<S>.(K).X> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@17:9 UnprovenConstraint_Kd@18:21")] // I
    [InlineData("nst4n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Wrap<T>\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U> : Base<Wrap<U>>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "UnsatisfiedConstraint_Kd@18:5")] // I
    [InlineData("nst4v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Wrap<T>\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U> : Base<U>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "")] // V
    [InlineData("nst5", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U, W> : Base<W>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X, R>.Inner => $abort(\"x\")\n", "")] // V
    [InlineData("nst5n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U, W> : Base<W>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<R, Box<S>.(K).X>.Inner => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@18:5 UnprovenConstraint_Kd@19:17")] // I
    [InlineData("pj1", "contract M\ncontract D\n    associate Y is M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct Box<T, P>\n    T is C\n    Self is K\n    associate K.X is P\nstruct S<U>\n    U is D\n    Self is C\n    public func get(self: ref/Self) -> U.(D).Y => $abort(\"x\")\n    public func get2(self: ref/Self) -> Box<S<U>, U.(D).Y>.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("pj2", "contract M\ncontract D\n    associate Y is M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct Box<T, P>\n    T is C\n    Self is K\n    associate K.X is P\nstruct S<U>\n    U is D\n    Self is C\n    public func get(self: ref/Self) -> Box<S<U>, U.(D).Y>.(K).X => $abort(\"x\")\n    public func get2(self: ref/Self) -> U.(D).Y => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("pj1n", "contract M\ncontract D\n    associate Y is M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\nstruct Box<T, P>\n    T is C\n    Self is K\n    associate K.X is P\nstruct S<U>\n    U is D\n    Self is C\n    public func get(self: ref/Self) -> Box<S<U>, U.(D).Y>.(K).X => $abort(\"x\")\n    public func get2(self: ref/Self) -> Box<S<U>, U.(D).Y>.(K).X => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@16:5 UnprovenConstraint_Kd@17:17")] // I
    [InlineData("pos1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Item>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, R> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("pos2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Item>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<R, Box<S>.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("pos1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Item>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Box<S>.(K).X, Box<S>.(K).X> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@15:5 UnprovenConstraint_Kd@16:17")] // I
    [InlineData("thr1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\n    func get3(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> R => $abort(\"x\")\n    public func get2(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get3(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("thr2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\n    func get3(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get2(self: ref/Self) -> R => $abort(\"x\")\n    public func get3(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("thr3", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\n    func get3(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get2(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get3(self: ref/Self) -> R => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("thr0n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\n    func get2(self: ref/Self) -> Item\n    func get3(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get2(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n    public func get3(self: ref/Self) -> Box<S>.(K).X => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@16:5 UnprovenConstraint_Kd@17:17")] // I
    [InlineData("tup1n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> (Item, i32)\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X, i32) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("tup1v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> (Item, i32)\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X, i32) => $abort(\"x\")\n", "")] // V
    [InlineData("tup2", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<(i32, i32), Item>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<(Box<S>.(K).X, i32), R> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("tupw", "contract K\n    associate X\ncontract C\n    associate Item is Owned\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X, i32) => $abort(\"x\")\n", "")] // V
    [InlineData("tupwn", "contract K\n    associate X\ncontract C\n    associate Item is Owned\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X, i32) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@12:5 UnprovenConstraint_Kd@13:17")] // I
    [InlineData("rawn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> raw/Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> raw/Box<S>.(K).X => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("rawv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> raw/Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> raw/Box<S>.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("fv1e", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<(i32) -> i32, Item>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<(Box<S>.(K).X) -> i32, R> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("fv3o", "contract K\n    associate X\ncontract C\n    associate Item is Owned\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ((Box<S>.(K).X) -> i32) -> i32 => $abort(\"x\")\n", "")] // V
    [InlineData("qnest", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Outer<T>\n    public struct Inner2<V>\n        Self is M when T is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Outer<Box<S>.(K).X>.Inner2<R> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@17:5 UnprovenConstraint_Kd@18:17")] // I
    [InlineData("qnestw", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Outer<T>\n    public struct Inner2<V>\n        Self is M when V is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Outer<R>.Inner2<Box<S>.(K).X> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@17:5 UnprovenConstraint_Kd@18:17")] // I
    [InlineData("qnestv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Outer<T>\n    public struct Inner2<V>\n        Self is M when T is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Outer<Box<S>.(K).X>.Inner2<R> => $abort(\"x\")\n", "")] // V
    [InlineData("deep3n", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct A<T>\n    public struct B<U>\n        public struct Cc\n            Self is M when T is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> A<Box<S>.(K).X>.B<R>.Cc => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@18:5 UnprovenConstraint_Kd@19:17")] // I
    [InlineData("deep3w", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct A<T>\n    public struct B<U>\n        public struct Cc\n            Self is M when U is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> A<R>.B<Box<S>.(K).X>.Cc => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@18:5 UnprovenConstraint_Kd@19:17")] // I
    [InlineData("deep3v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct A<T>\n    public struct B<U>\n        public struct Cc\n            Self is M when T is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> A<Box<S>.(K).X>.B<R>.Cc => $abort(\"x\")\n", "")] // V
    [InlineData("envg", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Wrap<T>\n    Self is M when T is M\nopen struct Base<T>\n    public struct Inner2<V>\n        Self is M when T is M\nstruct Der<U> : Base<Wrap<U>>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X>.Inner2<R> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@20:5 UnprovenConstraint_Kd@21:17")] // I
    [InlineData("envgv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Wrap<T>\n    Self is M when T is M\nopen struct Base<T>\n    public struct Inner2<V>\n        Self is M when T is M\nstruct Der<U> : Base<Wrap<U>>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Der<Box<S>.(K).X>.Inner2<R> => $abort(\"x\")\n", "")] // V
    [InlineData("rootnst", "contract M\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct Outer\n    public struct Inner\n        Self is M\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Outer.Inner => $abort(\"x\")\n", "")] // V
    [InlineData("rootenv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U> : Base<U>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Der<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "AssociatedTypeInferenceFailed_Kd@17:5 UnresolvedBinding_Kd@18:40")] // V, rejected on dev too (pre-existing, review-g1001.md N3)
    [InlineData("rootenvn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nopen struct Base<T>\n    public struct Inner\n        Self is M when T is M\nstruct Der<U> : Base<U>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Der<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "AssociatedTypeInferenceFailed_Kd@18:5 UnresolvedBinding_Kd@19:40")] // I
    [InlineData("rootq", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Outer<T>\n    public struct Inner2<V>\n        Self is M when T is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Outer<Box<S>.(K).X>.Inner2<R> => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@17:5 UnprovenConstraint_Kd@18:17")] // I
    [InlineData("rootqv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Outer<T>\n    public struct Inner2<V>\n        Self is M when T is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Outer<Box<S>.(K).X>.Inner2<R> => $abort(\"x\")\n", "")] // V
    [InlineData("rootp", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Box<S>.(K).X => $abort(\"x\")\n", "AssociatedTypeInferenceFailed_Kd@14:5 UnresolvedBinding_Kd@15:40")] // I
    [InlineData("rootpv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Box<S>.(K).X => $abort(\"x\")\n", "AssociatedTypeInferenceFailed_Kd@13:5 UnresolvedBinding_Kd@14:40")] // V, rejected on dev too (pre-existing, review-g1001.md N3)
    [InlineData("shortn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.X => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("shortv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box<S>.X => $abort(\"x\")\n", "")] // V
    [InlineData("parn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@14:5 UnprovenConstraint_Kd@15:17")] // I
    [InlineData("parv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (Box<S>.(K).X) => $abort(\"x\")\n", "")] // V
    [InlineData("orgp", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Y(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Y(a) is T\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Box2<Box<S>.(K).X>.(K2).Y(a) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@19:5 UnprovenConstraint_Kd@20:17")] // I
    [InlineData("orgpv", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Y(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Y(a) is T\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Box2<Box<S>.(K).X>.(K2).Y(a) => $abort(\"x\")\n", "")] // V
    [InlineData("cargn", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K2<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K2<Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@19:5 UnprovenConstraint_Kd@20:17")] // I
    [InlineData("cargv", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K2<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K2<Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("inpj", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Box<Self>.(K2).W>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("inpjo", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    associate Other\n    func get(self: ref/Self) -> Pair<Item, Other>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    associate C.Other is Box<S>.(K2).W\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("inpjn", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, R2>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("inpjv", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Box<Self>.(K2).W>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("inpjs", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Box<Self>.(K2).W, Item>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("inpj3", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Box<Self>.(K2).W>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, Box<S>.(K2).W>\nstruct Q3\n    Self is K\n    associate K.X is Q.(K).X\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q3.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("inpjh", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<R, Item>\nstruct R\n    Self is M\nstruct R2\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "UnprovenConstraint_Kd@22:5")] // I
    [InlineData("inpjm", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Item>\nstruct R2\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R2>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("rootgen", "contract M\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Outer<T>\n    public struct Inner\n        Self is M\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Outer<R>.Inner => $abort(\"x\")\n", "")] // V
    [InlineData("rootdeep", "contract M\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct A\n    public struct B\n        public struct Cc\n            Self is M\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::A.B.Cc => $abort(\"x\")\n", "")] // V
    [InlineData("rootgn", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Outer<T>\n    public struct Inner\n        Self is M when T is M\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Outer<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@17:5 UnprovenConstraint_Kd@18:17")] // I
    [InlineData("rootgv", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Outer<T>\n    public struct Inner\n        Self is M when T is M\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Outer<Box<S>.(K).X>.Inner => $abort(\"x\")\n", "")] // V
    [InlineData("carg2", "contract M\ncontract K\n    associate X\ncontract K3<A, B>\n    associate Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R, R>\n    associate K3<R, R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<R, Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@19:5 UnprovenConstraint_Kd@20:17")] // I
    [InlineData("carg2v", "contract M\ncontract K\n    associate X\ncontract K3<A, B>\n    associate Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R, R>\n    associate K3<R, R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<R, Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("cargg", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K2<Pair<R, R>>\n    associate K2<Pair<R, R>>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K2<Pair<Box<S>.(K).X, R>>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@20:5 UnprovenConstraint_Kd@21:17")] // I
    [InlineData("orgq", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Y(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Y(a) is T\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Box2<Pair<Box<S>.(K).X, R>>.(K2).Y(a) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@21:5 UnprovenConstraint_Kd@22:17")] // I
    [InlineData("orgqv", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Y(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when B is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Y(a) is T\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Box2<Pair<Box<S>.(K).X, R>>.(K2).Y(a) => $abort(\"x\")\n", "")] // V
    [InlineData("cargr", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@20:5 UnprovenConstraint_Kd@21:17")] // I
    [InlineData("cargrv", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("cargr3", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K3<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@20:5 UnprovenConstraint_Kd@21:17")] // I
    [InlineData("cargr3v", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K3<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("orgc", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K2<R>\n    associate K2<R>.Y(a) is R\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Q.(K2<Box<S>.(K).X>).Y(a) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@19:5 UnprovenConstraint_Kd@20:17")] // I
    [InlineData("orgcv", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K2<R>\n    associate K2<R>.Y(a) is R\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Q.(K2<Box<S>.(K).X>).Y(a) => $abort(\"x\")\n", "")] // V
    [InlineData("inpjf", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, (Box<Self>.(K2).W) -> i32>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, (Box<S>.(K2).W) -> i32>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("inpjt", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<(Item, i32), Box<Self>.(K2).W>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<(R, i32), Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K).X => $abort(\"x\")\n", "")] // V
    [InlineData("inpja", "contract M\ncontract K2\n    associate W\ncontract K3\n    associate Z(a)\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Item, Box<Self>.(K2).W>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K3\n    associate K3.Z(a) is Pair<R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Q.(K3).Z(a) => $abort(\"x\")\n", "")] // V
    [InlineData("rootopt", "contract C\n    associate Item\n    func get(self: ref/Self) -> Item\nstruct Outer\n    public struct Inner\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::Outer.Inner? => $abort(\"x\")\n", "")] // V
    [InlineData("rootarg", "contract M\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\nstruct Pair<A, B>\n    Self is M when A is M\nstruct Outer\n    public struct Inner\n        Self is M\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<::Outer.Inner, R> => $abort(\"x\")\n", "")] // V
    [InlineData("rootfn", "contract C\n    associate Item\n    func get(self: ref/Self) -> Item\nstruct Outer\n    public struct Inner\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> (::Outer.Inner) -> i32 => $abort(\"x\")\n", "")] // V
    [InlineData("rootgrp", "contract M\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\ngroup G\n    public struct Inner\n        Self is M\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> ::G.Inner => $abort(\"x\")\n", "")] // V
    [InlineData("posb1", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<Item, R>, Pair<R, Item>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posb2", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<R, Item>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posb1d", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<Item, R>, Pair<R, Item>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct Q2\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Q.(K).X, Q2.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("cargr4", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract K4<A>: K3<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K4<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K4<Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@21:5 UnprovenConstraint_Kd@22:17")] // I
    [InlineData("cargr4v", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract K4<A>: K3<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K4<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K4<Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("cargrm", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<Pair<A, R>>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<Pair<R, R>>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@21:5 UnprovenConstraint_Kd@22:17")] // I
    [InlineData("cargrmv", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<Pair<A, R>>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<Pair<R, R>>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("cargsp", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    associate C.Item is Q.(K3<Box<S>.(K).X>).Y\n", "UnprovenConstraint_Kd@19:5")] // I
    [InlineData("cargspv", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    associate C.Item is Q.(K3<Box<S>.(K).X>).Y\n", "")] // V
    [InlineData("orgr", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y(a)\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y(a) is R\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Q.(K3<Box<S>.(K).X>).Y(a) => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@20:5 UnprovenConstraint_Kd@21:17")] // I
    [InlineData("orgrv", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y(a)\ncontract K3<A>: K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y(a) is R\nstruct S\n    Self is C\n    public func get(self: ref/Self during a) -> Q.(K3<Box<S>.(K).X>).Y(a) => $abort(\"x\")\n", "")] // V
    [InlineData("posc1", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Pair<Item, R>, Pair<R, Item>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posc2", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Pair<R, Item>, Pair<Item, R>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posc3", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Pair<Item, R>, Pair<Item, R>, Pair<R, Item>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("poscn", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Pair<Item, R>, Pair<Item, R>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "UnprovenConstraint_Kd@21:5")] // I
    [InlineData("posn1", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<Item, R>, Pair<Pair<R, Item>, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Q.(K).X, Pair<Q.(K).X, R>> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posn2", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<R, Item>, Pair<Pair<Item, R>, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Q.(K).X, Pair<Q.(K).X, R>> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("cargr5", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<B>: K2<Pair<B, R>>\ncontract K4<D>: K3<Wrap<D>>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Wrap<T>\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K4<R>\n    associate K2<Pair<Wrap<R>, R>>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K4<Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@23:5 UnprovenConstraint_Kd@24:17")] // I
    [InlineData("cargr5v", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<B>: K2<Pair<B, R>>\ncontract K4<D>: K3<Wrap<D>>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Wrap<T>\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K4<R>\n    associate K2<Pair<Wrap<R>, R>>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K4<Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("cargr2p", "contract M\ncontract K\n    associate X\ncontract K0\n    associate Z\ncontract K2<A>\n    associate Y\ncontract K3<A>: K0, K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K0.Z is R\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@23:5 UnprovenConstraint_Kd@24:17")] // I
    [InlineData("cargr2pv", "contract M\ncontract K\n    associate X\ncontract K0\n    associate Z\ncontract K2<A>\n    associate Y\ncontract K3<A>: K0, K2<A>\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K0.Z is R\n    associate K2<R>.Y is R\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q.(K3<Box<S>.(K).X>).Y => $abort(\"x\")\n", "")] // V
    [InlineData("spref", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract K5\n    associate V\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y is R\nstruct Q2\n    Self is K5\n    associate K5.V is Q.(K3<Box<S>.(K).X>).Y\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q2.(K5).V => $abort(\"x\")\n", "UnprovenConstraint_Kd@25:5")] // I
    [InlineData("sprefv", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K3<A>: K2<A>\ncontract K5\n    associate V\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K3<R>\n    associate K2<R>.Y is R\nstruct Q2\n    Self is K5\n    associate K5.V is Q.(K3<Box<S>.(K).X>).Y\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q2.(K5).V => $abort(\"x\")\n", "")] // V
    [InlineData("spcarg", "contract M\ncontract K\n    associate X\ncontract K2<A>\n    associate Y\ncontract K5\n    associate V\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Q\n    Self is K2<R>\n    associate K2<R>.Y is R\nstruct Q2\n    Self is K5\n    associate K5.V is Q.(K2<Box<S>.(K).X>).Y\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q2.(K5).V => $abort(\"x\")\n", "UnprovenConstraint_Kd@24:5")] // I
    [InlineData("posd1", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Pair<R, Item>, Pair<Item, R>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct Q2\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q2.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posd2", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Pair<Item, R>, Pair<R, Item>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct Q2\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q2.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posd3", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Pair<Item, R>, Pair<Item, R>, Pair<R, Item>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct Q2\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q2.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posm1", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<Item, R>, Pair<R, Item>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct Q3\n    Self is K\n    associate K.X is Pair<Q.(K).X, Q.(K).X>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q3.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posm2", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<R, Item>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct Q3\n    Self is K\n    associate K.X is Pair<Q.(K).X, Q.(K).X>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q3.(K).X => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posmn", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<Item, R>, Pair<Item, R>>\nstruct R\n    Self is M\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Pair<Box<S>.(K2).W, R>\nstruct Q3\n    Self is K\n    associate K.X is Pair<Q.(K).X, Q.(K).X>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Q3.(K).X => $abort(\"x\")\n", "UnprovenConstraint_Kd@20:5 UnprovenConstraint_Kd@23:5")] // I; 20:5 (Q3) is R7-C3's record form since e8f42d772 (F's e2 ruling 102)
    [InlineData("posb3a", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Tri<R, Item, R>, Tri<Item, R, R>, Tri<R, R, Item>>\nstruct R\n    Self is M\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Tri<Box<S>.(K2).W, R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posb3b", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Tri<Item, R, R>, Tri<R, Item, R>, Tri<R, R, Item>>\nstruct R\n    Self is M\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Tri<Box<S>.(K2).W, R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posb3c", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Tri<Tri<Item, R, R>, Tri<R, R, Item>, Tri<R, Item, R>>\nstruct R\n    Self is M\nstruct Tri<A, B, D>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R\nstruct Q\n    Self is K\n    associate K.X is Tri<Box<S>.(K2).W, R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Tri<Q.(K).X, Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // I by e2 ruling 80 (result compatibility through S is C): owner G629, e2-5b; G1001 gives dev's verdict, one per order
    [InlineData("posv", "contract M\ncontract K\n    associate X\ncontract K2\n    associate W\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Pair<Pair<Item, Box<Self>.(K2).W>, Pair<Item, Box<Self>.(K2).W>>\nstruct R\n    Self is M\nstruct R2\nstruct Pair<A, B>\nstruct Box<T>\n    T is C\n    Self is K2\n    associate K2.W is R2\nstruct Q\n    Self is K\n    associate K.X is Pair<R, Box<S>.(K2).W>\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Pair<Q.(K).X, Q.(K).X> => $abort(\"x\")\n", "")] // V
    [InlineData("recvp", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Z\ncontract D\n    associate Y\n    func y(self: ref/Self) -> Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when B is M\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Z is T\nstruct T2\n    Self is D\n    public func y(self: ref/Self) -> R => $abort(\"x\")\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box2<Pair<T2.(D).Y, Box<S>.(K).X>>.(K2).Z => $abort(\"x\")\n", "IncompatibleContractImplementation_Kd@27:5 UnprovenConstraint_Kd@28:17")] // I
    [InlineData("recvpv", "contract M\ncontract K\n    associate X\ncontract K2\n    associate Z\ncontract D\n    associate Y\n    func y(self: ref/Self) -> Y\ncontract C\n    associate Item is M\n    func get(self: ref/Self) -> Item\nstruct R\n    Self is M\nstruct Pair<A, B>\n    Self is M when B is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct Box2<T>\n    Self is K2\n    associate K2.Z is T\nstruct T2\n    Self is D\n    public func y(self: ref/Self) -> R => $abort(\"x\")\nstruct S\n    Self is C\n    public func get(self: ref/Self) -> Box2<Pair<T2.(D).Y, Box<S>.(K).X>>.(K2).Z => $abort(\"x\")\n", "")] // V
    public void AWrittenPositionIsReadAsWritten(string id, string source, string records)
    {
        var errors = Errors(source);
        var actual = Records(source, errors);
        Assert.True(records == actual && (records.Length != 0 || errors.Length == 0), $"{id}: {actual} ({errors.Length} Error records)");
    }

    // SPEC 8.3.3 Recursion and Finality (R7-C2e2 e2-3b's coverage rows, `design-e23b.md`): programs whose judgments read an open
    // obligation's neighbourhood, which the one obligation table leaves as they were: the controls of the projection route's n-forms (a
    // Box, a condition or a capability bound with no dependence on `S is C`, a second conditional conformance), an obligation opened and
    // closed inside an answer (iv1), a capability through c2-2's identity extension at depth 1 and 2, a source state read inside the
    // rounds (dv2, its invalid twins), r1's acyclic control and r1n, the clause subject t3 (rejected today for its subject form, PLAN
    // G982) and a coinductive Owned with no re-entry.
    [Theory]
    [InlineData("n1v", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", true)]
    [InlineData("n3v", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Box<T>\n    Self is K when T is D\n        associate K.X is R\nstruct S\n    Self is D\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", true)]
    [InlineData("n5v", "contract K\n    associate X\ncontract C\n    associate Item is Copy\nstruct Box<T>\n    Self is K\n    associate K.X is i32\nstruct S\n    Self is C\n    associate C.Item is Box<S>.(K).X\n", true)]
    [InlineData("cc1v", "contract M\ncontract D\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Wrap<T>\n    Self is D when T is M\nstruct Box<T>\n    Self is K when T is D\n        associate K.X is R\nstruct S\n    Self is C\n    Self is M\n    associate C.Item is Box<Wrap<S>>.(K).X\n", true)]
    [InlineData("iv1", "contract M\ncontract K\n    associate X\ncontract C\n    associate Item is M\nstruct R\n    Self is M\nstruct Q\n    Self is C\n    associate C.Item is R\nstruct Box<T>\n    T is C\n    Self is K\n    associate K.X is R\nstruct S\n    Self is C\n    associate C.Item is Box<Q>.(K).X\n", true)]
    [InlineData("x1", "contract K\n    associate A\n    associate B is Copy\n    Self.(K).A is Self.(K).B\nfunc dup<V>(v: V) -> (V, V)\n    V is Copy\n    return (v, v)\nfunc f<T>(a: T.(K).A) -> ()\n    T is K\n    let p = dup(a)\n", true)]
    [InlineData("x2", "contract K\n    associate A\n    associate B is Copy\n    Self.(K).A is Self.(K).B\ncontract L\n    associate Inner is K\nfunc dup<V>(v: V) -> (V, V)\n    V is Copy\n    return (v, v)\nfunc f<T>(a: T.(L).Inner.(K).A) -> ()\n    T is L\n    let p = dup(a)\n", true)]
    [InlineData("x2n", "contract K\n    associate A\n    associate B\n    Self.(K).A is Self.(K).B\ncontract L\n    associate Inner is K\nfunc dup<V>(v: V) -> (V, V)\n    V is Copy\n    return (v, v)\nfunc f<T>(a: T.(L).Inner.(K).A) -> ()\n    T is L\n    let p = dup(a)\n", false)]
    [InlineData("dv2", "contract Origin\nstruct Source\n    Self is Origin\ncontract K\n    Source is Origin\n    associate X\n    Self.(K).X is i32\nfunc g<T>(v: T.(K).X) -> (T.(K).X, T.(K).X)\n    T is K\n    return (v, v)\n", true)]
    [InlineData("dv2n", "contract Origin\nstruct Source\n    Self is Origin\ncontract K\n    Source is Origin\n    associate X\nfunc g<T>(v: T.(K).X) -> (T.(K).X, T.(K).X)\n    T is K\n    return (v, v)\n", false)]
    [InlineData("dv1n", "contract Origin\nstruct Source\ncontract K\n    Source is Origin\n    associate X\n    Self.(K).X is i32\nfunc f<T>(v: T.(K).X) -> i32\n    T is K\n    return v\n", false)]
    [InlineData("r1h", "contract P\ncontract Q\nstruct A\n    Self is P\n    B is Q\nstruct B\n    Self is Q\n", true)]
    [InlineData("r1n", "contract P\ncontract Q\nstruct A\n    B is Q\nstruct B\n    Self is Q\n    A is P\n", false)]
    [InlineData("t3", "contract D\ncontract C\n    associate X\nstruct Box<A>\n    Self is C when A is D\n        associate C.X is A\nfunc needD<V>(v: ref/V) -> ()\n    V is D\n    ()\nfunc f<U>(u: ref/U) -> ()\n    Box<U>.(C).X is D\n    needD(u)\n", false)]
    [InlineData("own0", "contract C\n    associate Item is Owned\nstruct Node<T>\n    var next: Option<rc/Node<T>>\nstruct S\n    Self is C\n    associate C.Item is Node<S>\n", true)]
    public void AnObligationNeighbourKeepsItsLabel(string id, string source, bool valid)
        => Assert.True(valid == (DiagnosticCorpus.RecordProfile(source) == "clean"), id);

    // SPEC 8.3.3 Finality (R7-C2e2 e2-3b): the capability engine's evaluations where one conformance requests a Copy (cap2) or an Owned
    // (own2) instance twice (two associates) and the instance's formation reads the obligation being proven, `S is C`, in a dead operand
    // (`T is C or D`, S both): the work is decided, so it holds with that obligation pending and is kept for its epoch, and the second
    // request reads it; with the other operand refuted (`T is C or E`, the Unknown twin) it depends and is judged again. The difference
    // between the twins cancels the library's fixed evaluations. It is the dependence's 2 and, since dev's f6324930d (PLAN G949 S1-1c:
    // a certificate round walks the environments again only after a failure), the program pass's round, which the rejected Unknown twin
    // runs (its validation walk recorded its failures) and the accepted decided twin skips: 2 evaluations, fixed (1, 15 and 30 such
    // conformances give 2 each), by that rule.
    [Theory]
    [InlineData("cap2", "contract D\ncontract C\n    associate Item is Copy\n    associate Other is Copy\nstruct Box<T>\n    T is C or D\n    Self is Copy\n    var value: i32\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>\n    associate C.Other is Box<S>\n", "contract D\ncontract E\ncontract C\n    associate Item is Copy\n    associate Other is Copy\nstruct Box<T>\n    T is C or E\n    Self is Copy\n    var value: i32\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Box<S>\n    associate C.Other is Box<S>\n", 4)]
    [InlineData("own2", "contract D\ncontract C\n    associate Item is Owned\n    associate Other is Owned\nstruct Node<T>\n    T is C or D\n    var next: Option<rc/Node<T>>\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Node<S>\n    associate C.Other is Node<S>\n", "contract D\ncontract E\ncontract C\n    associate Item is Owned\n    associate Other is Owned\nstruct Node<T>\n    T is C or E\n    var next: Option<rc/Node<T>>\nstruct S\n    Self is C\n    Self is D\n    associate C.Item is Node<S>\n    associate C.Other is Node<S>\n", 4)]
    public void ACapabilityReadingTheObligationIsEvaluated(string id, string decided, string unknown, int extra)
    {
        var (kept, judged) = (Evaluations(decided), Evaluations(unknown));
        Assert.True(extra == judged - kept, $"{id}: {judged} - {kept} evaluations");

        static int Evaluations(string source)
        {
            var c = CompilationTestHelper.ParseSuccess(source);
            c.Bind();
            return c.Binding.CapabilityEvaluationWork;
        }
    }

    // A Library program's Error records in source order.
    private static CheckDiagnostic[] Errors(string source)
        => [.. DiagnosticCorpus.Check(source, "Library").Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).OrderBy(static x => x.Span?.Start ?? -1)];

    // A program's independent Error records (Errors'), `Code@line:column`, with no dependent InvalidConstraint_Kd (record selection, Q13).
    private static string Records(string source, CheckDiagnostic[] errors)
        => string.Join(' ', errors.Where(static x => x.Code != nameof(DiagnosticCode.InvalidConstraint_Kd)).Select(x => $"{x.Code}@{Position(source, x.Span!.Value.Start)}"));

    // A source offset as `line:column`, both from 1.
    private static string Position(string source, int offset)
        => $"{source.AsSpan(0, offset).Count('\n') + 1}:{offset - source.LastIndexOf('\n', Math.Max(offset - 1, 0))}";

    // B1's ring family (R7-C2e2 e2-5a, design-engine): k declarations' Copy works and Owned works on one cycle, each order, with and without a use.
    public static TheoryData<int, string, bool, int> RingRows
        => [.. from k in new[] { 1, 2, 3, 4, 5 }
               from variant in new[] { "copy", "copy-bad", "copy-free", "owned", "owned-bad" }
               from use in new[] { false, true }
               from order in new[] { 0, 1, 2 }
               select (k, variant, use, order)];

    // SPEC 8.3.3 Recursion and Limits, 15.3.5 (R7-C2e2 e2-5a, the capability engine's termination): D stores H<C1>, C1 .. Ck store the
    // next, Ck stores S<D>, whose formation asks D is Owned. Through Copy works (copy) the cycle supplies no evidence (O-78 (a)), so the
    // program is invalid, and invalid with D's bad Field too; with S's Constraint gone (copy-free) nothing cycles; through Owned works alone
    // (owned) the cycle is Owned's structure, a greatest fixed point, and holds. Every row answers in every order, within a deadline.
    [Theory]
    [MemberData(nameof(RingRows))]
    public async Task ACycleOfCopyAndOwnedWorksEndsInEveryOrder(int k, string variant, bool use, int order)
    {
        var owned = variant.StartsWith("owned", StringComparison.Ordinal);
        var blocks = new List<string>
        {
            "struct S<U>\n" + (variant == "copy-free" ? string.Empty : "    U is Owned\n") + (owned ? string.Empty : "    Self is Copy\n") + "    public init() => ()\n",
            owned ? "struct H<A>\n    A is Owned\n    public init() => ()\n" : "struct H<A>\n    A is Copy\n    public init() => ()\n",
            "struct BoxK<A>\n    A is K\n    public init() => ()\ncontract K\n",
            "struct D\n    let h: H<C1>\n" + (variant.EndsWith("-bad", StringComparison.Ordinal) ? "    let bad: BoxK<i32>\n" : string.Empty),
        };
        for (var i = 1; i <= k; i++)
        {
            var next = i < k ? $"C{i + 1}" : "S<D>";
            blocks.Add($"struct C{i}\n{(owned ? string.Empty : "    Self is Copy\n")}    public var x: {next}\n    public init() => self.x = {next}.init()\n");
        }

        var ordered = order switch { 0 => blocks, 1 => Enumerable.Reverse(blocks).ToList(), _ => [.. blocks.Skip(3), .. blocks.Take(3)] };
        var source = string.Concat(ordered) + (use ? "func needOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc use1(d: ref/D) -> ()\n    needOwned(d)\n" : string.Empty);
        var cancel = TestContext.Current.CancellationToken;
        var run = Task.Run(() => DiagnosticCorpus.RecordProfile(source), cancel);
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30), cancel)));
        Assert.Equal(variant is "copy-free" or "owned", await run == "clean");
    }

    // SPEC 6.2, the owner's O-70 (a) (DECISIONS BU-128): a binding-only inline cycle is invalid at the binding, a Language record, never a
    // Resource one, used or not. These rows assert their records: each fails if S3 (VerifyConformance's formation of a binding, R7-C2 (b))
    // goes without that judge, which leaves them clean or Resource records. A binding on the cycle names no Type after the failed Binding,
    // inferred as written (F's C2 ruling 261), so a published body using the cyclic Type by value adds no generation limit: an inferred
    // one's S.get (InlineCycleInferred, InlineCycleInferredWithAValue) gave GenerationResourceLimit_Kd once PLAN G1071's gate published it.
    [Theory]
    [InlineData(InlineCycleDirect, "Library", "1*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleThroughOption, "Library", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleWithAValue, "Application", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleInferred, "Library", "3*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleInTupleAndArray, "Library", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleThroughTwoTypes, "Library", "2*InvalidConstraint_Kd 2*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleThroughAnotherContract, "Library", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleGrowing, "Library", "1*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleInferredWithAValue, "Library", "4*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleWrittenWithAMethod, "Library", "4*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    public void ABindingOnlyInlineCycleIsItsBindingsError(string source, string outputKind, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source, outputKind));

    [Theory]
    [InlineData("i32", "", true)]
    [InlineData("U", "", true)]
    [InlineData("string", "", false)]
    [InlineData("string", "    U is i64\n", true)]
    public void AssociatedSpecificationsAgreeAtEveryAdmittedIntersection(string right, string premise, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a)\ncontract Q<U>: C<i32>, C<U>\n" + premise +
            "    associate C<i32>.Item(a) is i32\n    associate C<U>.Item(a) is " + right + "\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("i64")]
    public void InheritedRequirementsMayConvergeWithoutLosingTheirMapping(string argument)
    {
        var source = """
            contract P<E>
                func read(self: ref/Self) -> i32
            contract Q<U>: P<i32>, P<U>
            open struct Base<U>
                Self is Q<U>
                public init() => ()
                public func read(self: ref/Self) -> i32 => 7
            struct Derived: Base<ARGUMENT>
                public init() : base() => ()
            func use<T>(source: ref/T) -> i32
                T is P<i32>
                return source.read()
            let source = Derived.init()
            Console.writeLine("\(use(source@ref))")
            """.Replace("ARGUMENT", argument, StringComparison.Ordinal);
        ScalarEmissionTest.EmitFixture("ConformanceConvergence" + argument, source, "7\n");
    }

    [Fact]
    public void DistinctRetainedImplementationsCannotBecomeOneInheritedMapping()
    {
        const string Source = """
            contract P<E>
                func read(self: ref/Self, value: ref/E) -> i32
            contract Q<U>: P<i32>, P<U>
            open struct Base<U>
                Self is Q<U>
                public init() => ()
                public func read(self: ref/Self, value: ref/i32) -> i32 => 1
                public func read(self: ref/Self, value: ref/U) -> i32 => 2
            struct Derived: Base<i32>
                public init() : base() => ()
            func use<T>(source: ref/T) -> i32
                T is P<i32>
                let value = 0
                return source.read(value@ref)
            let source = Derived.init()
            Console.writeLine("\(use(source@ref))")
            """;
        var definition = MinimalEmissionTest.Analyze(Source[..Source.IndexOf("struct Derived", StringComparison.Ordinal)] + "let present = 1");
        Assert.False(definition.Binding.Result.IsComplete, MinimalEmissionTest.Describe(definition));
        Assert.Contains(definition.Binding.Issues, static x => x.Code == Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd);
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("i32", false)]
    [InlineData("string", true)]
    public void AvailabilityConditionsDetermineWhetherMappingsMustAgree(string right, bool valid)
    {
        var source = "contract P<E>\n    func read(self: ref/Self, value: ref/E) -> i32\ncontract Left: P<i32>\ncontract Right<U>: P<U>\n" +
            "struct S<U, T>\n    Self is Left when T is i32\n    Self is Right<U> when T is " + right + "\n" +
            "    public func read(self: ref/Self, value: ref/i32) -> i32 => 1\n    public func read(self: ref/Self, value: ref/U) -> i32 => 2\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConvergedPropertyAndItemMappingsExecuteAcrossTwoBases(bool reversed)
    {
        var source = "contract P<E>\n    property value: i32 has get\n    func read(self: ref/Self) -> i32\ncontract Q<U>: " + (reversed ? "P<U>, P<i32>" : "P<i32>, P<U>") + "\n" +
            "open struct Base<U>\n    Self is Q<U>\n    public init() => ()\n    public computed value: i32\n        get() -> i32 => 7\n    public func read(self: ref/Self) -> i32 => 9\n" +
            "open struct Middle<T>: Base<T>\n    public init() : base() => ()\nstruct Derived: Middle<i32>\n    public init() : base() => ()\n" +
            "func use<T>(source: ref/T) -> i32\n    T is P<i32> and Owned\n    let read: (ref/T) -> i32 = T.read\n    return source.value + read(source)\n" +
            "let source = Derived.init()\nConsole.writeLine(\"\\(use(source@ref))\")";
        ScalarEmissionTest.EmitFixture("ConformanceConvergencePropertyItem" + reversed, source, "16\n");
    }

    [Fact]
    public void ABaseEditRevokesConvergedSources()
    {
        const string Source = "contract P<E>\n    func read(self: ref/Self) -> i32\ncontract Q<U>: P<i32>, P<U>\nopen struct Base<U>\n    Self is Q<U>\n    public func read(self: ref/Self) -> i32 => 7\nstruct Derived: Base<i32>\nfunc probe<T>(source: ref/T)\n    T is P<i32>\n    ()\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var derived = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "Derived");
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        var p = ((IsKoto)probe.TypeConstraints[0]).ConstraintOf()!.Contract!;
        var mapping = c.Binding.GetConformance(derived.TypeOf()!, p)!;
        Assert.Equal(2, Assert.Single(mapping.Paths).InheritedSourceCount);
        for (var i = 0; i < 4; i++)
        {
            var argument = i % 2 == 0 ? "i64" : "i32";
            var donor = CompilationTestHelper.ParseSuccess("struct Derived: Base<" + argument + ">").Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single();
            Assert.True(KotoHelper.Replace(derived, derived.Bases[0], ParseTestHelper.Detach(donor.Bases[0], new CodeBlockKoto(derived.CodeContext))));
            Assert.False(mapping.IsVerified);
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
            Assert.Same(mapping, c.Binding.GetConformance(derived.TypeOf()!, p));
            Assert.Single(mapping.Witnesses);
            Assert.Equal(argument == "i32" ? 2 : 1, Assert.Single(mapping.Paths).InheritedSourceCount);
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ConvergedInheritedSourcesReuseStorage()
    {
        const string Source = "contract P<E>\n    func read(self: ref/Self) -> i32\ncontract Q<U>: P<i32>, P<U>\nopen struct Base<U>\n    Self is Q<U>\n    public func read(self: ref/Self) -> i32 => 7\nstruct Derived: Base<i32>\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void IntersectionChecksReuseStorage(string right, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a)\ncontract Q<U>: C<i32>, C<U>\n    associate C<i32>.Item(a) is i32\n    associate C<U>.Item(a) is " + right + "\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    // SPEC 8.9.2, 23.1.2 (R7-E2 E2-5b, M1): two associated Types whose bindings disagree at one intersection are two failed requirements,
    // each its own record (RecordNumbered) with its own Types; with Other's bindings agreeing, Item's alone.
    [Theory]
    [InlineData("bool", 2)]
    [InlineData("i32", 1)]
    public void EachDisagreeingAssociatedTypeIsItsOwnRecord(string other, int count)
    {
        var records = DiagnosticCorpus.Check(TwoAssociatedPairs(other)).Diagnostics;
        Assert.All(records, static x => Assert.Equal("InvalidAssociatedType_Kd", x.Code));
        string[] labels = ["at C<U> = C<i32>, Item's Types string and i32 are not proven equal", "at C<U> = C<i32>, Other's Types bool and i32 are not proven equal"];
        Assert.Equal(labels[..count], records.Select(static x => x.Label));
    }

    // The two disagreeing pairs rebind without allocating: the further problem's numbering walks the subject's kept problems.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void TwoDisagreeingPairsRebindWithoutAllocation()
    {
        var c = MinimalEmissionTest.Analyze(TwoAssociatedPairs("bool"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void OverlapFailureExplainsTheConflictInCliAndLspAndKeepsIndependentErrors()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Q<U>: C<i32>, C<U>\n    associate C<i32>.Item(a) is i32\n    associate C<U>.Item(a) is string\nlet wrong: i32 = true";
        var path = Path.GetFullPath("conformance-convergence.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(["InvalidAssociatedType_Kd", "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var record = result.Diagnostics[0];
        Assert.Equal([("left", "C<U>"), ("right", "C<i32>"), ("associated", "Item"), ("first", "string"), ("second", "i32")], record.Reason!.Select(static x => (x.Name, x.Value)));
        Assert.Equal("at C<U> = C<i32>, Item's Types string and i32 are not proven equal", record.Label);
        Assert.Equal(new SourceSpan(Source.IndexOf("contract Q", StringComparison.Ordinal), "contract".Length), record.Span);
        Assert.Equal(["C<U>", "C<i32>"], record.Related!.Select(static x => x.Label).Order(StringComparer.Ordinal));
        Assert.Empty(record.Repairs ?? []);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var records = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(record.Code, records[0].Code);
            Assert.Equal(record.Display!.Range!.Value, records[0].Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(records));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MappingOverlapChecksReuseStorage(bool disjoint)
    {
        var source = "contract P<E>\n    func read(self: ref/Self, value: ref/E) -> i32\ncontract Q<U>: P<i32>, P<U>\nstruct S<U>\n" +
            (disjoint ? "    U is i64\n" : string.Empty) + "    Self is Q<U>\n" +
            "    public func read(self: ref/Self, value: ref/i32) -> i32 => 1\n    public func read(self: ref/Self, value: ref/U) -> i32 => 2\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(disjoint, c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Equal(disjoint, c.Binding.Result.IsComplete);
    }

    // SPEC 8.6, 8.6.1 (R7-V2a2): after an edit replaces a Self clause, a Bind reads only the conformances the edited source declares, so its
    // records are a fresh Compilation's of that source, and undoing the edit gives the original's. An earlier pass's identity, empty after
    // its reset, was read as a conformance: e6's call, whose argument conforms to Iter<i32> through the normalized reference (valid both
    // ways, SPEC 8.2.3, 8.6.1), was rejected by the Bind after the edit; t1 reaches the same identity through a Type's constraint
    // (Holder<B<SrcI>>'s `X is Iter<i32>`, valid both ways). e1, a `for` over a Type that no longer conforms to Iterable (invalid after
    // the edit, SPEC 4.6.4), is the same reading through the by-declaration lookup, kept as a guard.
    [Theory]
    [InlineData("e1", "Bin", "Self is Iterable", "Self is Nominals")]
    [InlineData("e6", "B", "Self is Iter<i32>", "Self is Iter<S.(HasItem).Item>")]
    [InlineData("t1", "B", "Self is Iter<i32>", "Self is Iter<S.(HasItem).Item>")]
    public void AnEditedSelfClauseBindsAsTheEditedSource(string id, string owner, string before, string after)
    {
        var source = EditedSelfClauses[id];
        var edited = source.Replace(before, after, StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        var clause = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == owner).ConstraintNodes[0];
        var original = clause.Right;
        var replacement = KotoTree.Walk(CompilationTestHelper.ParseSuccess(edited).Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == owner).ConstraintNodes[0].Right;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        foreach (var (text, from, to) in new[] { (edited, original, replacement), (source, replacement, original) })
        {
            Assert.True(KotoHelper.Replace(clause, from, to));
            var fresh = BindingRecords(MinimalEmissionTest.Analyze(text));
            for (var bind = 0; bind < 2; bind++)
            {
                c.Bind();
                Assert.Equal($"{id} {bind}: {fresh}", $"{id} {bind}: {BindingRecords(c)}");
            }
        }
    }

    private static string BindingRecords(Compilation c)
        => string.Join(" | ", c.Binding.Issues.Select(static x => $"{x.Code}@{x.Node.GetType().Name}").Order(StringComparer.Ordinal));

    private static string TwoAssociatedPairs(string other)
        => "contract C<E>\n    associate Item(a)\n    associate Other(b)\ncontract Q<U>: C<i32>, C<U>\n    associate C<i32>.Item(a) is i32\n" +
        "    associate C<U>.Item(a) is string\n    associate C<i32>.Other(b) is i32\n    associate C<U>.Other(b) is " + other + "\nlet present = 1";
}
