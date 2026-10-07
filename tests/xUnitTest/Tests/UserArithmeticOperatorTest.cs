// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class UserArithmeticOperatorTest
{
    private const string Declarations = """
        struct Value
            Self is Addable<i32>
            Self is Subtractable<Value>
            Self is LeftMultipliable<f32>
            Self is Negatable
            associate Addable<i32>.Output is i32
            associate Subtractable<Value>.Output is i32
            associate LeftMultipliable<f32>.Output is f32
            associate Negatable.Output is i32
            public let value: i32 = 40
            public func added(self: ref/Self, right: ref/i32) -> i32 => self.value + right
            public func subtracted(self: ref/Self, right: ref/Value) -> i32 => self.value - right.value
            public func multipliedFrom(left: ref/f32, self: ref/Self) -> f32 => left * self.value@f32
            public func negated(self: ref/Self) -> i32 => -self.value
            drop => ()
        """;

    [Fact]
    public void SharedNonCopyProvidersKeepDistinctInputsAndOutputs()
        => ScalarEmissionTest.EmitFixture("UserArithmeticShared", Declarations + "\nlet value = Value.init()\nrequire value + 2 == 42 and value - value == 0 and -value == -40 else => $abort(\"ordinary\")\nrequire 2.0 * value == 80.0 and (1.0 + 1.0) * value == 80.0 else => $abort(\"left\")\nrequire value.value == 40 else => $abort(\"not consumed\")", string.Empty);

    [Fact]
    public void AnEnumCanProvideAnArithmeticRequirement()
        => ScalarEmissionTest.EmitFixture("UserArithmeticEnum", "enum Marker\n    Self is Negatable\n    associate Output is i32\n    On\n    public func negated(self: ref/Self) -> i32 => -42\nlet value = Marker.On\nrequire -value == -42 else => $abort(\"enum provider\")", string.Empty);

    [Fact]
    public void InheritedArithmeticUsesTheOriginalWitness()
    {
        const string Types = "open struct Base\n    Self is Addable<i32> and LeftSubtractable<i32> and Negatable\n    associate Addable<i32>.Output is i32\n    associate LeftSubtractable<i32>.Output is i32\n    associate Negatable.Output is i32\n    public let value: i32 = 40\n    public init() => ()\n    public func added(self: ref/Self, right: ref/i32) -> i32 => self.value + right\n    public func subtractedFrom(left: ref/i32, self: ref/Self) -> i32 => left - self.value\n    public func negated(self: ref/Self) -> i32 => -self.value\nstruct Derived: Base\n    public init() : base() => ()\nlet value = Derived.init()\n";
        ScalarEmissionTest.EmitFixture("UserArithmeticInherited", Types + "require value.added(2) == 42 and value + 2 == 42 and 82 - value == 42 and -value == -40 else => $abort(\"inherited provider\")", string.Empty);
    }

    [Fact]
    public void PublishedContractsFixGenericOperatorsBeforeInstantiation()
    {
        const string Generic = "\nfunc sum<L, R>(left: ref/L, right: ref/R) -> L.(Addable<R>).Output\n    L is Addable<R>\n    return left + right\nlet value = Value.init()\nrequire sum(value, 2) == 42 and sum(40, 2) == 42 else => $abort(\"generic\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticGeneric", Declarations + Generic, string.Empty);
    }

    [Fact]
    public void KnownReferenceLayersUseTheSameSharedAcquisition()
    {
        const string Program = "\nfunc inspect(value: ref/(ref/Value during b)) -> i32 => value + 2\nlet value = Value.init()\nlet inner = value@ref\nlet outer = inner@ref\nrequire inspect(outer) == 42 else => $abort(\"layers\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticLayers", Declarations + Program, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericLeftContractBorrowsBeforeEvaluatingTheRight(bool copied)
    {
        var program = "struct Value\n    Self is LeftAddable<i32>\n    associate Output is i32\n    public func addedFrom(left: ref/i32, self: ref/Self) -> i32 => left + 1\nfunc bump(value: uniq/i32) -> Value\n    value@follow += 1\n    return Value.init()\nvar number = 40\nlet result = number" + (copied ? "@copy" : string.Empty) + " + bump(number@uniq)\nrequire result == 41 and number == 41 else => $abort(\"order\")";
        var c = MinimalEmissionTest.Analyze(program);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(copied, c.Ownership.Result.IsVerified);
        if (copied)
        {
            ScalarEmissionTest.EmitFixture("UserArithmeticCopiedLeft", program, string.Empty);
        }
    }

    [Theory]
    [InlineData("2")]
    [InlineData("scale")]
    public void LeftProviderDoesNotInventNumericConversions(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "\nlet value = Value.init()\nlet scale = 2.0\nlet result = " + expression + " * value");
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void PrimitiveIntegerCounterpartCanGuideASharedLiteral()
    {
        const string Program = "func increment<L, R>(left: ref/L) -> L.(Addable<R>).Output\n    L is Addable<R>\n    R is PrimitiveInteger\n    return left + 1\nrequire increment<i32, i32>(41) == 42 else => $abort(\"generic literal\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticGenericLiteral", Program, string.Empty);
    }

    [Fact]
    public void OppositeProviderPremisesConflictWithoutAnOperatorUse()
    {
        const string Program = "func conflict<L, R>(left: ref/L, right: ref/R)\n    L is Addable<R>\n    R is LeftAddable<L>\n    ()\n()";
        var result = DiagnosticCorpus.Check(Program);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticDirection_Kd));
        Assert.Contains("opposite providers", error.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "ordinary", "reverse" }, error.Related!.Select(static x => x.Role));
    }

    [Fact]
    public void LiteralAmbiguityRetainsBothConformancesInPublicRecords()
    {
        var source = Declarations.Replace("    Self is Negatable", "    Self is LeftMultipliable<f64>\n    Self is Negatable", StringComparison.Ordinal).Replace("    associate Negatable.Output", "    associate LeftMultipliable<f64>.Output is f64\n    associate Negatable.Output", StringComparison.Ordinal).Replace("    public func negated", "    public func multipliedFrom(left: ref/f64, self: ref/Self) -> f64 => left * self.value@f64\n    public func negated", StringComparison.Ordinal) + "\nlet value = Value.init()\nlet result: f32 = 2.0 * value";
        var path = Path.GetFullPath("arithmetic-selection.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ArithmeticSelection_Kd), error.Code);
        Assert.Equal("2.0 * value", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(new[] { "operator", "contract", "provider", "counterpart", "condition" }, error.Reason!.Select(static x => x.Name));
        Assert.Equal("Multiple proven conformances fit the counterpart structure; provider: Value; counterpart: not independently typed", error.Label);
        Assert.Equal(2, error.Related!.Length);
        Assert.All(error.Related!, static x => Assert.Equal("conformance", x.Role));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("LeftMultipliable<f32>", console.Text, StringComparison.Ordinal);
        Assert.Contains("LeftMultipliable<f64>", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("Multiple proven", sent.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AFixedProviderGuidesOneNestedCall()
    {
        var source = Declarations + "\nfunc make<T>() -> T\n    T is PrimitiveInteger\n    return 2@T\nlet value = Value.init()\nrequire value + make() == 42 else => $abort(\"expected counterpart\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticNested", source, string.Empty);
    }

    [Fact]
    public void LeftPremiseKeepsItsDirectionAndNoncommutativeArgumentOrder()
    {
        const string Program = "struct Difference\n    Self is LeftSubtractable<i32>\n    associate Output is i32\n    public func subtractedFrom(left: ref/i32, self: ref/Self) -> i32 => left - 2\nfunc subtract<L, R>(left: ref/L, right: ref/R) -> R.(LeftSubtractable<L>).Output\n    R is LeftSubtractable<L>\n    return left - right\nlet value = Difference.init()\nrequire subtract(44, value) == 42 else => $abort(\"left direction\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticLeftGeneric", Program, string.Empty);
    }

    [Fact]
    public void ResultsMayKeepExistingExternalDependencies()
    {
        const string Program = "struct View {a}\n    Self is Addable<()>\n    associate Output is ref/i32 during a\n    let value: ref/i32 during a\n    public init(value: ref/i32 during a) => self.value = value\n    public func added(self: ref/Self, right: ref/()) -> ref/i32 during a => self.value\nlet n = 42\nlet view = View.init(n@ref)\nlet result = view + ()\nrequire result == 42 else => $abort(\"external dependency\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticExternal", Program, string.Empty);
    }

    [Fact]
    public void OriginsCannotChooseBetweenStructurallyEqualCounterparts()
    {
        const string Program = "struct View {source}\n    let value: ref/i32 during source\nfunc select<L>(left: ref/L, right: ref/(View during a), marker: ref/i32 during b) -> i32\n    L is Addable<View during a> and Addable<View during b>\n    return left + right\n()";
        var result = DiagnosticCorpus.Check(Program);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticSelection_Kd));
        Assert.Equal("Multiple proven conformances fit the counterpart structure", error.Reason![4].Value);
        Assert.Contains("provider: L; counterpart: View", error.Label, StringComparison.Ordinal);
        Assert.Equal(2, error.Related!.Length);
    }

    [Fact]
    public void UnknownConditionalCandidatesCannotEstablishUniqueness()
    {
        const string Program = "struct Box<T>\n    Self is Addable<i32>\n    Self is Addable<i64> when T is Copy\n    associate Addable<i32>.Output is i32\n    associate Addable<i64>.Output is i64\n    public func added(self: ref/Self, right: ref/i32) -> i32 => right\n    public func added(self: ref/Self, right: ref/i64) -> i64 => right\nfunc use<T>(value: ref/Box<T>) -> i32 => value + 1\n()";
        var result = DiagnosticCorpus.Check(Program);
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticSelection_Kd));
        Assert.Equal("A structurally fitting conditional conformance remains unproven; provider: Box<T>; counterpart: not independently typed", error.Label);
        Assert.Equal(2, error.Related!.Length);
    }

    [Theory]
    [InlineData("0..1")]
    [InlineData("Option<i32>.Some(1)")]
    public void OrdinaryLibraryTypesUseTheOrdinaryProviderRule(string value)
    {
        var result = DiagnosticCorpus.Check("let value = " + value + "\nlet result = value + ()");
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ArithmeticSelection_Kd), error.Code);
        Assert.Contains("Addable", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("value + missing")]
    [InlineData("missing * value")]
    public void AnInvalidOperandDoesNotInventAConformanceFailure(string expression)
    {
        var source = Declarations + "\nlet value = Value.init()\nlet result = " + expression;
        var result = DiagnosticCorpus.Check(source);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnresolvedBinding_Kd), error.Code);
        Assert.Equal("missing", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.DoesNotContain(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.ArithmeticSelection_Kd));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericOperatorEffectsUseOnlyPublishedBounds(bool bounded)
    {
        var source = "contract ConfinedAdd<T>: Addable<T>\n    effect (Addable<T>).added confined\ncontract Operation<T>\n    func run(self: ref/Self, left: ref/T, right: ref/T) -> i32\n        effect confined\nstruct Forward<T>\n    T is " + (bounded ? "ConfinedAdd<T>" : "Addable<T>") + "\n    T.(Addable<T>).Output is i32\n    Self is Operation<T>\n    public func run(self: ref/Self, left: ref/T, right: ref/T) -> i32 => left + right\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(bounded, c.Binding.Result.IsComplete);
        if (!bounded)
        {
            Assert.Equal(DiagnosticCode.IncompatibleContractImplementation_Kd, Assert.Single(c.Binding.Issues).Code);
        }
    }

    [Fact]
    public void AnEditedProviderCannotReuseItsPreviousOperatorSelection()
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "\nlet value = Value.init()\nlet result = value + 2");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var original = Assert.Single(c.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>());
        var donor = MinimalEmissionTest.Analyze("struct Value\n    public let value: i32 = 40\nlet value = Value.init()");
        var changed = Assert.Single(donor.Kotonoha.RootKoto.NestedContainers.OfType<StructKoto>());
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.ArithmeticSelection_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void AnImplementationCannotReturnItsFreshInspectionLoan()
    {
        const string Source = "struct View {a}\n    Self is Addable<()>\n    associate Output is ref/i32 during a\n    let value: i32 = 42\n    public func added(self: ref/Self, right: ref/()) -> ref/i32 during a => self.value@ref\n()";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        var error = Assert.Single(c.Binding.Issues);
        Assert.Equal(DiagnosticCode.UnprovenOriginRelation_Kd, error.Code);
        Assert.Equal("self.value@ref", error.Node.ToString());
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure is OwnershipFailure.Unsupported or OwnershipFailure.Internal);
    }

    [Fact]
    public void UserAbortKeepsItsImplementationLocationAndSkipsWriteback()
    {
        const string Source = "struct Value\n    Self is Addable<i32>\n    associate Output is Self\n    public func added(self: ref/Self, right: ref/i32) -> Self => $abort(\"operation\")\n    drop => Console.writeLine(\"unexpected cleanup\")\nvar value = Value.init()\nvalue += 1\nConsole.writeLine(\"unexpected continuation\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticAbort", Source, string.Empty, 1, "Hello.kimi:4:66: abort KIMI_E_ABORT: operation\n");
    }

    [Fact]
    public void ResultOutputPropagatesOnlyThroughExplicitTry()
    {
        const string Source = "struct Value\n    Self is Dividable<i32>\n    associate Output is Result<i32, i32>\n    public func divided(self: ref/Self, right: ref/i32) -> Result<i32, i32>\n        require right != 0 else => return .Err(7)\n        return .Ok(84 / right)\nfunc divide(right: i32) -> Result<i32, i32>\n    let value = Value.init()\n    let result = try (value / right)\n    return .Ok(result)\nmatch divide(2)\n    .Ok(let value) => require value == 42 else => $abort(\"value\")\n    .Err(_) => $abort(\"unexpected failure\")\nmatch divide(0)\n    .Ok(_) => $abort(\"unexpected success\")\n    .Err(let value) => require value == 7 else => $abort(\"error\")";
        ScalarEmissionTest.EmitFixture("UserArithmeticResult", Source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmOperatorPlansReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Declarations + "\nlet value = Value.init()\nlet added = value + 2\nlet scaled = 2.0 * value\nlet negative = -value");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
