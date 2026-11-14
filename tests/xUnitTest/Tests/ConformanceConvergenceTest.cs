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
    // goes without that judge, which leaves them clean or Resource records.
    [Theory]
    [InlineData(InlineCycleDirect, "Library", "1*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleThroughOption, "Library", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleWithAValue, "Application", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleInferred, "Library", "3*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleInTupleAndArray, "Library", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleThroughTwoTypes, "Library", "2*InvalidConstraint_Kd 2*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleThroughAnotherContract, "Library", "2*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
    [InlineData(InlineCycleGrowing, "Library", "1*InvalidConstraint_Kd 1*InvalidInlineLayout_Kd")]
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
}
