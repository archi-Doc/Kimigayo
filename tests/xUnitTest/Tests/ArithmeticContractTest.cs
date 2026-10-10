// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ArithmeticContractTest(ITestOutputHelper output)
{
    private const string Source = """
        struct Counter
            Self is Addable<i32>
            Self is LeftSubtractable<i32>
            Self is Negatable
            associate Addable<i32>.Output is i32
            associate LeftSubtractable<i32>.Output is i32
            associate Negatable.Output is i32
            public let value: i32 = 40
            public func added(self: ref/Self, right: ref/i32) -> i32 => self.value + right
            public func subtractedFrom(left: ref/i32, self: ref/Self) -> i32 => left - self.value
            public func negated(self: ref/Self) -> i32 => -self.value
        func add<T>(left: ref/T, right: ref/i32) -> i32
            T is Addable<i32>
            T.(Addable<i32>).Output is i32
            return left.added(right)
        func subtract<T>(left: ref/i32, right: ref/T) -> i32
            T is LeftSubtractable<i32>
            T.(LeftSubtractable<i32>).Output is i32
            return T.subtractedFrom(left, right)
        func negate<T>(value: ref/T) -> i32
            T is Negatable
            T.(Negatable).Output is i32
            return value.negated()
        let value = Counter.init()
        require add(value, 2) == 42 else => $abort("sum")
        require subtract(100, value) == 60 else => $abort("direction")
        require negate(value) == -40 else => $abort("negation")
        require value.value == 40 else => $abort("shared")
        """;

    [Fact]
    public void RecognizedDeclarationsHaveOneOutputAndSharedRequirement()
    {
        var c = MinimalEmissionTest.Analyze("()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        for (var id = KimiDeclarationId.Addable; id <= KimiDeclarationId.Negatable; id++)
        {
            var contract = c.Library.GetSymbol(id)!.Contract!;
            Assert.Equal("Output", Assert.Single(contract.AssociatedTypes).Symbol.Name);
            var function = (FunctionKoto)Assert.Single(contract.Requirements).Symbol.Declaration;
            Assert.All(function.Parameters, static x => Assert.Equal(SemanticsKind.Ref, x.Type.BoundType!.Semantics));
            Assert.Equal(id == KimiDeclarationId.Negatable ? 1 : 2, function.Parameters.Count);
        }
    }

    [Fact]
    public void GenericCallsPreserveOrdinaryLeftAndUnaryWitnesses()
        => ScalarEmissionTest.EmitFixture("ArithmeticContractExplicitCalls", Source, string.Empty);

    [Theory]
    [InlineData("Addable<ref/i32 during a>", "ref", "Rhs")]
    [InlineData("Addable<raw/i32>", "raw/i32", "Rhs")]
    [InlineData("Addable<string>", "string", "Rhs")]
    [InlineData("LeftAddable<bool>", "bool", "Lhs")]
    [InlineData("LeftAddable<char>", "char", "Lhs")]
    [InlineData("LeftAddable<()>", "()", "Lhs")]
    public void IneligibleCounterpartsAreLanguageErrorsAtTheConformance(string contract, string type, string side)
    {
        var source = "struct Bad {a}\n    Self is " + contract + "\n    associate Output is i32\n()";
        var result = DiagnosticCorpus.Check(source);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        var error = Assert.Single(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidArithmeticConformance_Kd));
        Assert.False(result.Accepted);
        Assert.Equal(DiagnosticCategory.Language, error.Category);
        Assert.Contains(type, error.Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "type", "contract", "condition" }, error.Reason!.Select(static x => x.Name));
        Assert.Contains(side, error.Reason![2].Value, StringComparison.Ordinal);
        Assert.False(error.Reason[2].Elided);
        Assert.Equal(error.Reason[2].Value, error.Label);
        Assert.Equal("Self is " + contract, source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal("contract", Assert.Single(error.Related!).Role);
        Assert.DoesNotContain(result.Diagnostics, static x => x.Category == DiagnosticCategory.Unsupported);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("T is Sealed", false)]
    [InlineData("T is Owned and not string", false)]
    [InlineData("T is Sealed and not string", true)]
    [InlineData("T is PrimitiveInteger", true)]
    public void GenericCounterpartEligibilityMustBeEstablishedAtTheDefinition(string premise, bool proven)
    {
        var source = "struct Box<T>\n" + (premise.Length != 0 ? "    " + premise + "\n" : string.Empty) +
            "    Self is Addable<T>\n    associate Output is i32\n    public func added(self: ref/Self, right: ref/T) -> i32 => 1\n()";
        var result = DiagnosticCorpus.Check(source);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Equal(proven, result.Accepted);
        Assert.Equal(!proven, result.Diagnostics.Any(static x => x.Code == nameof(DiagnosticCode.InvalidArithmeticConformance_Kd)));
    }

    [Fact]
    public void ConditionalEligibilityDoesNotRestrictTheUnconstrainedType()
    {
        const string Program = "struct Box<T>\n    Self is Addable<T> when T is PrimitiveInteger\n    associate Output is i32\n    public func added(self: ref/Self, right: ref/T) -> i32 => 1\nlet value = Box<string>.init()\n()";
        var result = DiagnosticCorpus.Check(Program);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        Assert.True(result.Accepted);
    }

    [Fact]
    public void OuterOwnerCounterpartsMayRetainExternalBorrows()
    {
        const string Program = "struct Box {a}\n    Self is Addable<(ref/i32 during a, i32)>\n    associate Output is i32\n    public func added(self: ref/Self, right: ref/(ref/i32 during a, i32)) -> i32 => 1\n()";
        var result = DiagnosticCorpus.Check(Program);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        Assert.True(result.Accepted);
    }

    [Fact]
    public void EligibilityDiagnosticsRetainTheirCauseInCliAndLsp()
    {
        const string Program = "struct Bad\n    Self is Addable<string>\n    associate Output is i32\n()";
        var path = Path.GetFullPath("arithmetic-contract.kimi");
        var c = MinimalEmissionTest.Analyze(Program, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.InvalidArithmeticConformance_Kd), error.Code);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("arithmetic-contract.kimi:2:5", console.Text, StringComparison.Ordinal);
        Assert.Contains("excluding string", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Code, sent.Code);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("excluding string", sent.Message, StringComparison.Ordinal);
            // Embedded compiler sources have no editor URI; both client modes retain their location as text.
            Assert.Null(sent.RelatedInformation);
            Assert.Contains("compiler://Kimi/", sent.Message, StringComparison.Ordinal);

            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void AnOrdinarySameNamedContractDoesNotGainArithmeticEligibilityRules()
    {
        const string Program = "contract Addable<Rhs>\nstruct Example\n    Self is Addable<string>\n()";
        var result = DiagnosticCorpus.Check(Program);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
        Assert.True(result.Accepted);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmConformanceAndExplicitCallsReuseTheirStorage()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
