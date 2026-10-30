// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class InheritedConformanceTest
{
    private const string Arithmetic = """
        open struct Base<T>
            T is PrimitiveInteger
            Self is Addable<T>
            associate Output is T
            public let value: T
            public init(value: T) => self.value = value
            public func added(self: ref/Self, right: ref/T) -> T => self.value + right
        open struct Middle<T>: Base<T>
            T is PrimitiveInteger
            public init(value: T) : base(value) => ()
        struct Derived: Middle<i32>
            public init() : base(40) => ()
        func sum<T>(value: ref/T, right: ref/i32) -> T.(Addable<i32>).Output
            T is Addable<i32>
            return value + right
        func invoke<F, T>(action: ref/F, value: ref/T) -> i32
            F is Callable<(ref/T, ref/i32) -> i32>
            return action(value, 2)
        let value = Derived.init()
        require value + 2 == 42 and sum(value, 2) == 42 else => $abort("inherited call")
        """;

    private const string Bumps = "contract Bumps\n    func bump(self: uniq/Self) -> ()\n";

    // BW and BX declare HasItem before its refinement HasMore, conditionally and unconditionally.
    private const string Refinements = """
        contract HasItem
            associate Item
        contract HasMore: HasItem
            associate Elem
            associate HasItem.Item is Elem
        struct Src
            Self is HasMore
            associate HasMore.Elem is i32
        struct SrcI
            Self is HasItem
            associate HasItem.Item is i32
        struct HA<S>
            S is HasItem
            public var x: S.Item
        struct HM<S>
            S is HasMore
            public var x: S.Elem
        open struct BW<S>
            S is HasItem
            Self is HasItem
            associate HasItem.Item is S.(HasItem).Item
            Self is HasMore when S is HasMore
                associate HasMore.Elem is S.(HasMore).Elem
        open struct BX
            Self is HasItem
            associate HasItem.Item is i32
            Self is HasMore
            associate HasMore.Elem is i32

        """;

    // SPEC 12.7, 23.1.1, 23.1.2: an exclusive implementation reached through a base has no computed ObjectCallCompatible status
    // (OCC-X). Its conformance is one location-only Unsupported_Kd at its declaration: an explicit `Self is C`, or the derived Type's base
    // clause for an inherited conformance once a check that needs it fails. Every check resting on it is derived; an unused inherited
    // conformance says nothing.
    [Theory]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nvar x = Leaf.init()\nup(x@uniq)\n", "struct Leaf: |Base")]
    [InlineData(Bumps + "open struct Base\n    public var count: i32 = 1\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    Self is Bumps\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nvar x = Leaf.init()\nup(x@uniq)\n", "|Self is Bumps")]
    [InlineData("contract Counted\n    property count: i32 has get, set\nopen struct Base\n    var raw: i32 = 1\n    public computed count: i32\n        get(self: ref/Self) -> i32 => self.raw\n        set(self: uniq/Self, value: i32) -> () => self.raw = value\nstruct Leaf: Base\n    Self is Counted\n    public init() => ()\nvar x = Leaf.init()\n", "|Self is Counted")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nvar x = Leaf.init()\n", null)]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps and Owned\n    x.bump()\nvar x = Leaf.init()\nup(x@uniq)\n", "struct Leaf: |Base")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is not Bumps\n    ()\nvar x = Leaf.init()\nup(x@uniq)\n", "struct Leaf: |Base")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\n_ = up<Leaf>\n", "struct Leaf: |Base")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nlet f: (uniq/Leaf) -> () = up\n", "struct Leaf: |Base")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nvar x = Leaf.init()\nup(x@uniq)\nup(x@uniq)\n", "struct Leaf: |Base")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nfunc up(x: uniq/Leaf) -> () => ()\nvar x = Leaf.init()\nup(x@uniq)\n", "struct Leaf: |Base")] // Reduction regression: no longer selected past the limit (STATUS).
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nstruct Box<T>\n    T is Bumps\n    var item: T\nfunc peek(b: ref/Box<Leaf>) -> () => ()\nvar x = Leaf.init()\n", "struct Leaf: |Base")]
    [InlineData("contract Reader\n    Self is ObjectPayload\n    func score(self: objref/Self, bias: i32) -> i32\nopen struct Base\n    Self is Reader\n    public init() => ()\n    public virtual func score(self: objref/Self, bias: i32 = 1) -> i32 => bias\nfunc call<T>(value: objref/T) -> i32\n    T is Reader\n    return value.score(1)\nstruct Leaf: Base\n    public init() : base() => ()\n    override func score(self: objref/Self, bias: i32) -> i32 => base.score(bias) + 1\nlet value = Leaf.init()@obj\n_ = call(value@objref)\n", "struct Leaf: |Base")] // An object receiver projection is the same kind of limit (R1g; it refuted the conformance).
    public void AnUncomputedExclusiveWitnessIsOneLimitAtItsConformance(string source, string? at)
    {
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        if (at is null)
        {
            Assert.Empty(diagnostics);
            return;
        }

        var record = Assert.Single(diagnostics);
        var marker = at.IndexOf('|', StringComparison.Ordinal);
        var start = source.IndexOf(at.Remove(marker, 1), StringComparison.Ordinal) + marker;
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), start, at.Length - marker - 1), (record.Code, record.Span!.Value.Start, record.Span.Value.Length));

        // The limit is published from its recorded cause only; an independent error is still reported.
        var independent = DiagnosticCorpus.Check(source + "let bad: bool = 1\n").Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.TypeMismatch_Kd), nameof(DiagnosticCode.Unsupported_Kd)], independent.Select(static x => x.Code).Order());
    }

    // SPEC 23.1.2: a clause or candidate whose proof rests on a conformance limited by OCC-X is not decided whatever its other operands
    // and clauses (a refuted limit would refute it), so it is derived from that conformance's one record.
    [Theory]
    [InlineData("func up<T>(x: uniq/T) -> ()\n    T is Bumps and Owned\n    ()\nfunc outer<U>(x: uniq/GLeaf<U>) -> () => up(x)\n")]
    [InlineData("func up<T>(x: uniq/T) -> ()\n    T is Owned and Bumps\n    ()\nfunc outer<U>(x: uniq/GLeaf<U>) -> () => up(x)\n")]
    [InlineData("func up<T>(x: uniq/T) -> ()\n    T is Bumps\n    T is Owned\n    ()\nfunc outer<U>(x: uniq/GLeaf<U>) -> () => up(x)\n")]
    [InlineData("func up<T>(x: uniq/T) -> ()\n    T is Bumps and Owned\n    ()\nfunc outer<U>(x: uniq/GLeaf<U>) -> ()\n    _ = up<GLeaf<U>>\n")]
    [InlineData("func up<T>(x: uniq/T) -> ()\n    T is Bumps and Owned\n    ()\nfunc outer<U>(x: uniq/GLeaf<U>) -> ()\n    U is Owned\n    up(x)\n")]
    public void AClauseRestingOnTheLimitIsDerived(string use)
    {
        var source = Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct GLeaf<U>: Base\n    var item: U\n    public init(item: U) => self.item = item@move\n" + use + "()\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.Unsupported_Kd), source.IndexOf("struct GLeaf<U>: Base", StringComparison.Ordinal) + 17, 4), (record.Code, record.Span!.Value.Start, record.Span.Value.Length));
    }

    [Fact]
    public void MultipleLevelsRetainGenericWitnessesAndFunctionValues()
        => ScalarEmissionTest.EmitFixture("InheritedConformanceArithmetic", Arithmetic + "\nfunc items<T>(value: ref/T) -> i32\n    T is Addable<i32> and Owned\n    T.(Addable<i32>).Output is i32\n    let item = T.added\n    let erased: (ref/T, ref/i32) -> i32 = T.added\n    return item(value, 2) + invoke(item, value) + erased(value, 2)\nrequire items(value) == 126 else => $abort(\"inherited item\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InheritedMappingsReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze(Arithmetic);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssociatedBasesRetainArithmeticAndHeaderTypes(bool projected)
    {
        const string Source = "contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate Parent is Middle<i32>\n";
        var program = Arithmetic.Replace("struct Derived: Middle<i32>", Source + "struct Derived: " + (projected ? "Source.HasBase.Parent" : "Middle<i32>"), StringComparison.Ordinal)
            + "\nfunc answer() -> Derived.(Addable<i32>).Output => 42\nrequire answer() == 42 else => $abort(\"associated header\")";
        ScalarEmissionTest.EmitFixture("InheritedConformanceProjected" + projected, program, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedSelfDependentPathDoesNotInvalidateTheDerivedDeclaration(bool requireConformance)
    {
        const string Types = "contract C\n    func same(self: ref/Self, other: ref/Self) -> bool\nopen struct Base\n    Self is C\n    public init() => ()\n    public func same(self: ref/Self, other: ref/Self) -> bool => true\nstruct Derived: Base\n    public init() : base() => ()\n";
        var source = Types + (requireConformance ? "func use<T>(value: ref/T)\n    T is C\n    ()\nlet value = Derived.init()\nuse(value)" : "let value = Derived.init()");
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.Equal(!requireConformance, c.Bind().IsComplete);
        var derived = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        Assert.NotEqual(BindingState.Invalid, derived.StateOf());
        if (requireConformance)
        {
            var result = DiagnosticCorpus.Check(source);
            var error = Assert.Single(result.Diagnostics);
            Assert.Equal(nameof(DiagnosticCode.NoApplicableOverload_Kd), error.Code);
            Assert.Equal("use(value)", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
            Assert.Contains("Base.same keeps Base as Self", error.Note, StringComparison.Ordinal);
            Assert.Contains("input signature does not match Self = Derived", error.Note, StringComparison.Ordinal);
            var console = new DiagnosticContractTest.DiagnosticConsole();
            new Kimigayo(console).Render(new(result.Diagnostics, result.Sources), string.Empty);
            Assert.Contains("Base.same keeps Base as Self", console.Text, StringComparison.Ordinal);
            var identity = SourceIdentity.FromPath(result.Sources[error.Source].Path);
            var sent = Assert.Single(WorkspaceCheck.Place(result, [identity], identity, true)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("Base.same keeps Base as Self", sent.Message, StringComparison.Ordinal);
            var independent = DiagnosticCorpus.Check(source + "\nlet bad: bool = 1");
            Assert.Equal(2, independent.Diagnostics.Length);
            Assert.Contains(independent.Diagnostics, x => x.Code == nameof(DiagnosticCode.TypeMismatch_Kd));
            Assert.Contains(independent.Diagnostics, x => x.Code == nameof(DiagnosticCode.NoApplicableOverload_Kd) && x.Note!.Contains("Base.same keeps Base as Self", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void InheritedConditionsKeepTheirBaseSubstitution(string argument, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func read(self: ref/Self) -> i32\nopen struct Base<T>\n    Self is C when T is Copy\n    public func read(self: ref/Self) -> i32 => 42\nstruct Derived<T>: Base<T>\nfunc use<T>(value: ref/T)\n    T is C\n    ()\nfunc test(value: ref/Derived<" + argument + ">) => use(value)");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefinementDoesNotInheritCopyWithoutTheDerivedOptIn(bool optIn)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C: Copy\nopen struct Base\n    Self is C\nstruct Derived: Base\n" + (optIn ? "    Self is Copy\n" : string.Empty) + "func use<T>(value: ref/T)\n    T is C\n    ()\nfunc test(value: ref/Derived) => use(value)");
        Assert.Equal(optIn, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void ExplicitAndInheritedAssociatedBindingsMustAgree(string associated, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E\nopen struct Base\n    Self is C\n    associate E is i32\nstruct Derived: Base\n    Self is C\n    associate E is " + associated);
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    // SPEC 8.6.2, 8.9.2: a derived Type inherits every path of its base, also a refinement path the base declares after the refined
    // Contract's own conformance (registration read its root path before registering it); it gives the base's verdict.
    [Theory]
    [InlineData("struct D: BW<Src>\n", "HA<D>, g: HM<D>", true)]
    [InlineData("struct D<T>: BW<T>\n    T is HasItem\n", "HA<D<Src>>, g: HM<D<Src>>", true)]
    [InlineData("struct D<T>: BW<T>\n    T is HasItem\n", "HA<D<SrcI>>, g: HM<D<SrcI>>", false)] // The condition is refuted.
    [InlineData("struct D<T>: BW<T>\n    T is HasItem\n", "HA<D<i32>>, g: HM<D<Src>>", false)]
    [InlineData("open struct D<T>: BW<T>\n    T is HasItem\nstruct E<T>: D<T>\n    T is HasItem\n", "HA<E<Src>>, g: HM<E<Src>>", true)]
    [InlineData("struct D: BX\n", "HA<D>, g: HM<D>", true)]
    [InlineData(null, "HA<BW<Src>>, g: HM<BW<Src>>", true)] // The base itself.
    public void ARefinementDeclaredAfterItsParentIsInherited(string? derived, string parameters, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess(Refinements + derived + "func f(h: " + parameters + ") -> i32 => h.x + g.x\n");
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.All(c.Binding.Issues, static x => Assert.Equal(DiagnosticCode.UnsatisfiedConstraint_Kd, x.Code));
    }

    // SPEC 8.6.2, DIAGNOSTICS I2: the derived Type lists its base's conformances in the base's order, whatever order registration
    // meets their paths in (KB's root path is met first, through HasItem), so DB.Z's ambiguity is BB.Z's record.
    [Fact]
    public void AnInterleavedBaseKeepsItsConformanceOrder()
    {
        const string Types = "contract HasItem\n    associate Item\ncontract KA: HasItem\ncontract KC\n    associate Z\ncontract KB: HasItem\n    associate Z\nopen struct BB\n    Self is KA\n    associate HasItem.Item is i32\n    Self is KC\n    associate KC.Z is i64\n    Self is KB\n    associate KB.Z is u8\nstruct DB: BB\n";
        var records = Array.ConvertAll(["BB", "DB"], static x => Assert.Single(DiagnosticCorpus.Check(Types + "func f(z: " + x + ".Z) -> () => ()\n()\n").Diagnostics));
        Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), records[0].Code);
        Assert.Equal((records[0].Code, records[0].Message, records[0].Note), (records[1].Code, records[1].Message, records[1].Note));
        Assert.Equal(records[0].Related, records[1].Related);
    }

    [Fact]
    public void RefinementAndConditionalBodiesKeepTheOriginalMapping()
    {
        const string Source = "contract C: Addable<i32>\nopen struct Base<T>\n    Self is C when T is Copy\n        associate Output is i32\n        public func added(self: ref/Self, right: ref/i32) -> i32 => 40 + right\n    public init() => ()\nstruct Derived: Base<i32>\n    public init() : base() => ()\nlet value = Derived.init()\nrequire value + 2 == 42 else => $abort(\"conditional witness\")";
        ScalarEmissionTest.EmitFixture("InheritedConformanceConditional", Source, string.Empty);
    }

    [Fact]
    public void BaseOriginArgumentsPreserveAnExternalBorrowOutput()
    {
        const string Source = "open struct Base {a}\n    Self is Addable<()>\n    associate Output is ref/i32 during a\n    let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n    public func added(self: ref/Self, right: ref/()) -> ref/i32 during a => self.value\nstruct Derived {b}: Base during b\n    public init(value: ref/i32 during b) : base(value) => ()\nlet n = 42\nlet value = Derived.init(n@ref)\nlet result = value + ()\nrequire result == 42 else => $abort(\"inherited external output\")";
        ScalarEmissionTest.EmitFixture("InheritedConformanceOrigin", Source, string.Empty);
    }

    [Fact]
    public void ReplacingTheBaseRevokesItsInheritedMapping()
    {
        var c = MinimalEmissionTest.Analyze(Arithmetic);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var original = c.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>().Single(x => x.Name == "Base");
        var donor = CompilationTestHelper.ParseSuccess("open struct Base<T>\n    T is PrimitiveInteger\n    public let value: T\n    public init(value: T) => self.value = value\n    public func added(self: ref/Self, right: ref/T) -> T => self.value + right");
        var changed = Assert.Single(donor.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>());
        ParseTestHelper.Detach(changed, new GroupKoto(changed.CodeContext, default, default));
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ArithmeticSelection_Kd);
        Assert.False(c.WriteIr(TextWriter.Null, out _));
    }
}
