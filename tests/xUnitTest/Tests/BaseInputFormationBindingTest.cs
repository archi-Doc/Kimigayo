// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BaseInputFormationBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizedBaseCannotHideInvalidQualifier(bool reverseOrder)
    {
        const string middle = "open struct Middle: Source<string>.(Origin).Item\n";
        const string child = "struct S: Middle\n    Self is C\n";
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n" + (reverseOrder ? child + middle : middle + child));
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, Type(c).StateOf());
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Type(restored).StateOf());
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("Source<string>.(Origin).Item", false)]
    [InlineData("(Source<string>.(Origin).Item, i32)", false)]
    [InlineData("[2 of Source<string>.(Origin).Item]", false)]
    [InlineData("Source<i32>.(Origin).Item", true)]
    [InlineData("(Source<i32>.(Origin).Item, i32)", true)]
    [InlineData("[2 of Source<i32>.(Origin).Item]", true)]
    public void NestedBaseProjectionArgumentsRetainValidity(string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "open struct Generic<T>\ncontract C\nstruct S: Generic<" + argument + ">\n    Self is C");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Definition(c).IsVerified);
    }

    [Theory]
    [InlineData("struct S: Source<i32>.(Origin).Item\n    Self is C")]
    [InlineData("struct S<U>: Source<U>.(Origin).Item\n    U is i32\n    Self is C")]
    public void ValidBaseInputsUseDeclarationEvidence(string declaration)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\n" + declaration);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(Definition(c).IsVerified);
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Fact]
    public void ReplacingBaseQualifierRestoresSameCertificate()
    {
        const string source = Prefix + "contract C\nstruct S: Source<i32>.(Origin).Item\n    Self is C";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var definition = Definition(c);
        var structure = Type(c);
        var original = structure.Bases[0];
        var donor = MinimalEmissionTest.Analyze(source.Replace("Source<i32>", "Source<string>", StringComparison.Ordinal));
        var replacement = Type(donor).Bases[0];
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(structure, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(definition.IsVerified);
        Assert.True(KotoHelper.Replace(structure, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(definition, Definition(c));
        Assert.True(definition.IsVerified);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBaseInputChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "contract C\nstruct S: Source<i32>.(Origin).Item\n    Self is C");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Base input formation failed.");
            }
        }));
    }

    // SPEC 9.4.2, 9.5, 9.6 (R7-N2d): a name read before a projected base can be bound is read again once it is; a difference binds the
    // declarations once more with the base known. j4, rj10, nt33 and nt34 were rejected before; the others are guards.
    [Theory]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Middle\nopen struct Middle\n    public func m(self) -> i32 => 1\n    protected func pm(self) -> i32 => 1\n    protected struct N\n        public init() => ()\n    public init() => ()\ncontract K<T>\nstruct D: Source.(HasBase).Parent\n    Self is K<D.N>\n    public init(): base() => ()")]
    [InlineData("contract HasBase\n    associate Parent\ncontract HasItem\n    associate Item\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Middle\nopen struct Middle\n    Self is HasItem\n    associate HasItem.Item is i32\n    public struct N\n    public init() => ()\ncontract K<T>\ncontract K2<T>\nstruct D: Source.(HasBase).Parent\n    Self is K<D.N>\n    Self is K2<D.(HasItem).Item>\n    public init(): base() => ()")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    public struct N\n    public init() => ()\nstruct T: Source.(HasBase).Parent\n    Self is K<Self.N>\n    public init(): base() => ()")]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    public open struct N\n        public init() => ()\n    public init() => ()\nstruct T: Source.(HasBase).Parent\n    public init(): base() => ()\nstruct S2\n    Self is HasBase\n    associate HasBase.Parent is T.N\nstruct V: S2.(HasBase).Parent\n    public init(): base() => ()")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    public init() => ()\nopen struct B\n    public struct Inner\n    public init() => ()\nstruct T: Source.(HasBase).Parent\n    public init(): base() => ()\n    public struct U: B\n        public init(): base() => ()\nstruct W\n    Self is K<T.U.Inner>")]
    [InlineData("contract HasBase\n    associate Parent\nstruct S1\n    Self is HasBase\n    associate HasBase.Parent is T2.N\nstruct S2\n    Self is HasBase\n    associate HasBase.Parent is T1.M\nopen struct T1: S1.(HasBase).Parent\n    public open struct M\n        public init() => ()\n    public init(): base() => ()\nopen struct T2: S2.(HasBase).Parent\n    public open struct N\n        public init() => ()\n    public init(): base() => ()")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        Self is K<Q.N>\n        public init(): base() => ()\nstruct S2\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct M: S2.(HasBase).Parent\n    public open struct X\n        public init() => ()\n    public init(): base() => ()\nstruct S3\n    Self is HasBase\n    associate HasBase.Parent is M.X\nstruct V: S3.(HasBase).Parent\n    public init(): base() => ()")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q<i32>\nopen struct Q<X>: R\n    protected open struct N\n        public init() => ()\n    public var x: X\n    public init(x: X) => self.x = x@move\nopen struct R\n    private open struct N\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        Self is K<Q<i32>.N>\n        public init(): base(1) => ()")]
    public void AProjectedBaseIsReadOnceItIsBound(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 9.4.2 (R7-N2d): T derives from Q through its projected base, so Q's own protected N is reached, not R's private one, which was
    // selected before: in T's own Constraint (rx1) and in a struct nested in T (rx1b).
    [Theory]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        Self is K<Q.N>\n        public init(): base() => ()", "T")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        public init(): base() => ()\n        struct U\n            Self is K<Q.N>", "U")]
    public void AConstraintThroughAProjectedBaseSelectsTheNearDeclaration(string source, string owner)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var clause = Assert.IsType<IsKoto>(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == owner).ConstraintNodes.Single());
        var argument = Assert.IsType<GenericsKoto>(clause.Right).TypeArguments.Single();
        Assert.Equal("Q", ((DeclarationContainerKoto)argument.TypeOf()!.Symbol!.Scope.Owner).Name);
    }

    // R7-N2d: j4's first Bind runs its declarations twice; its warm Bind starts from the known bases, runs once and allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmBindAfterASecondRunAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Middle\nopen struct Middle\n    public func m(self) -> i32 => 1\n    protected func pm(self) -> i32 => 1\n    protected struct N\n        public init() => ()\n    public init() => ()\ncontract K<T>\nstruct D: Source.(HasBase).Parent\n    Self is K<D.N>\n    public init(): base() => ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("The second run failed.");
            }
        }));
    }

    // R7-N2d: the invalid twins keep their own records; nt46's second run differs, so its use is outside the implemented subset.
    [Theory]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        public init(): base() => ()\n        struct U: Q.N\n            public init() => ()\n            func g(self) -> i32 => self.far()", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Other\nopen struct Other\n    public init() => ()\nopen struct Q: R\n    protected open struct N\n        public func near(self) -> i32 => 1\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.(HasBase).Parent\n        public init(): base() => ()\n        struct U: Q.N\n            public init() => ()\n            func g(self) -> i32 => self.near()", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    public open struct N\n        public init() => ()\n    public init() => ()\nstruct T: Source.(HasBase).Parent\n    public init(): base() => ()\nstruct S2\n    Self is HasBase\n    associate HasBase.Parent is T.Nope\nstruct V: S2.(HasBase).Parent\n    public init(): base() => ()", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("contract K<T>\nstruct D: Nope\n    Self is K<D.N>", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    public init() => ()\nstruct T: Source.(HasBase).Parent\n    Self is K<T.Nope>\n    public init(): base() => ()", DiagnosticCode.UnresolvedBinding_Kd)]
    [InlineData(Paradox, DiagnosticCode.Unsupported_Kd)]
    public void AProjectedBaseKeepsItsInvalidTwinsRecords(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    // R7-N2d: a Bind's records are a function of the source and the mode. nt46's second run differs; a later Bind of it starts as that
    // run, so each Bind of the unchanged program, a Provisional one between, reports what a fresh Compilation reports. They alternated.
    [Fact]
    public void EveryBindAfterASecondRunReportsWhatAFreshBindReports()
    {
        var fresh = Records(MinimalEmissionTest.Analyze(Paradox));
        var c = MinimalEmissionTest.Analyze(Paradox);
        Assert.Equal(fresh, Records(c));
        for (var i = 0; i < 6; i++)
        {
            if (i == 4)
            {
                c.Binding.Bind(BindingMode.Provisional);
            }

            Assert.False(c.Bind().IsComplete);
            Assert.Equal(fresh, Records(c));
        }
    }

    private const string Paradox = "contract HasBase\n    associate Parent\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N: Q\n        public init(): base() => ()\n    public init() => ()\n    struct T: T.S.(HasBase).Parent\n        public init(): base() => ()\n        struct S\n            Self is HasBase\n            associate HasBase.Parent is Q.N";

    private const string Prefix = "open struct Base\ncontract Origin\n    associate Item\nstruct Source<T>\n    T is i32\n    Self is Origin\n    associate Origin.Item is Base\n";

    private static StructKoto Type(Compilation c)
        => Assert.IsType<StructKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S"));

    private static BoundConformance Definition(Compilation c)
    {
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(Type(c).TypeOf()!, contract.SymbolOf()!));
    }

    private static string Records(Compilation c) => string.Join(" | ", c.Binding.Issues.Select(static x => $"{x.Code}@{x.Node.Akind}:{x.Node.Span}").Order());
}
