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

    // SPEC 12.4.4.1: an exclusive implementation reached through a base waits for the receiver-preservation proof of OCC-X. The wait is
    // one located UnsupportedBinding_Kd, never an unproven Constraint: at an explicit conformance, whose uses derive from it, and at the
    // use of an inherited one, which has no declaration record; an inherited one without a use says nothing.
    [Theory]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nvar x = Leaf.init()\nup(x@uniq)\n", "up(x@uniq)", "bump")]
    [InlineData(Bumps + "open struct Base\n    public var count: i32 = 1\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    Self is Bumps\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nvar x = Leaf.init()\nup(x@uniq)\n", "Self is Bumps", "bump")]
    [InlineData("contract Counted\n    property count: i32 has get, set\nopen struct Base\n    var raw: i32 = 1\n    public computed count: i32\n        get(self: ref/Self) -> i32 => self.raw\n        set(self: uniq/Self, value: i32) -> () => self.raw = value\nstruct Leaf: Base\n    Self is Counted\n    public init() => ()\nvar x = Leaf.init()\n", "Self is Counted", "count")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nvar x = Leaf.init()\n", null, null)]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps and Owned\n    x.bump()\nvar x = Leaf.init()\nup(x@uniq)\n", "up(x@uniq)", "bump")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is not Bumps\n    ()\nvar x = Leaf.init()\nup(x@uniq)\n", "up(x@uniq)", "bump")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\n_ = up<Leaf>\n", "up<Leaf>", "bump")]
    [InlineData(Bumps + "open struct Base\n    Self is Bumps\n    public var count: i32 = 1\n    public init() => ()\n    public func bump(self: uniq/Self) -> () => self.count += 1\nstruct Leaf: Base\n    public init() => ()\nfunc up<T>(x: uniq/T) -> ()\n    T is Bumps\n    x.bump()\nlet f: (uniq/Leaf) -> () = up\n", "up", "bump")]
    public void APendingExclusiveWitnessIsOneLocatedLimit(string source, string? at, string? implementation)
    {
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        if (at is null)
        {
            Assert.Empty(diagnostics);
            return;
        }

        var record = Assert.Single(diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsupportedBinding_Kd), at), (record.Code, source.Substring(record.Span!.Value.Start, record.Span.Value.Length)));
        Assert.Equal($"the exclusive receiver of {implementation}, reached through a base, waits for its preservation proof", record.Label);
        Assert.Equal("exclusive implementation", Assert.Single(record.Related!).Label);

        // The limit is published from its recorded cause only; an independent error is still reported.
        var independent = DiagnosticCorpus.Check(source + "let bad: bool = 1\n").Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.TypeMismatch_Kd), nameof(DiagnosticCode.UnsupportedBinding_Kd)], independent.Select(static x => x.Code).Order());
    }

    [Fact]
    public void MultipleLevelsRetainGenericWitnessesAndFunctionValues()
        => ScalarEmissionTest.EmitFixture("InheritedConformanceArithmetic", Arithmetic + "\nfunc items<T>(value: ref/T) -> i32\n    T is Addable<i32> and Owned\n    T.(Addable<i32>).Output is i32\n    let item = T.added\n    let erased: (ref/T, ref/i32) -> i32 = T.added\n    return item(value, 2) + invoke(item, value) + erased(value, 2)\nrequire items(value) == 126 else => $abort(\"inherited item\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InheritedMappingsReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze(Arithmetic);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
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
        Assert.NotEqual(BindingState.Invalid, derived.BindingState);
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
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var original = c.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>().Single(x => x.Name == "Base");
        var donor = CompilationTestHelper.ParseSuccess("open struct Base<T>\n    T is PrimitiveInteger\n    public let value: T\n    public init(value: T) => self.value = value\n    public func added(self: ref/Self, right: ref/T) -> T => self.value + right");
        var changed = Assert.Single(donor.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>());
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ArithmeticSelection_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
