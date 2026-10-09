// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class RequirementFunctionItemTest
{
    private const string Source = """
        struct Counter
            Self is Addable<i32>
            associate Output is i32
            public let value: i32 = 40
            public func added(self: ref/Self, right: ref/i32) -> i32 => self.value + right
            drop => ()
        func apply<F, T>(operation: ref/F, left: ref/T, right: ref/i32) -> i32
            F is Callable<(ref/T, ref/i32) -> i32>
            return operation(left, right)
        func use<T>(left: ref/T, right: ref/i32) -> i32
            T is Addable<i32> and Owned
            T.(Addable<i32>).Output is i32
            let operation = T.added
            let erased: (ref/T, ref/i32) -> i32 = operation
            require operation(left, right) == erased(left, right) else => $abort("erased")
            require apply(operation, left, right) == apply(T.added, left, right) else => $abort("callable")
            return operation(left, right)
        let value = Counter.init()
        require use(value, 2) == 42 and use(40, 2) == 42 else => $abort("requirement")
        require value.value == 40 else => $abort("noncopy")
        """;

    [Fact]
    public void StoredCallableAndErasedRequirementsUseTheirSelectedWitness()
        => ScalarEmissionTest.EmitFixture("RequirementItemArithmetic", Source, string.Empty);

    [Fact]
    public void NumericLeftAndUnaryItemsPreserveTheirArgumentOrderAndArity()
    {
        const string Program = """
            struct Offset
                Self is LeftSubtractable<i32> and Negatable
                associate LeftSubtractable<i32>.Output is i32
                associate Negatable.Output is i32
                public let value: i32 = 2
                public func subtractedFrom(left: ref/i32, self: ref/Self) -> i32 => left - self.value
                public func negated(self: ref/Self) -> i32 => -self.value
            func apply<F, T>(operation: ref/F, left: ref/i32, right: ref/T) -> i32
                F is Callable<(ref/i32, ref/T) -> i32>
                return operation(left, right)
            func negate<F, T>(operation: ref/F, value: ref/T) -> i32
                F is Callable<(ref/T) -> i32>
                return operation(value)
            func use<T>(value: ref/T)
                T is LeftSubtractable<i32> and Negatable and Owned
                T.(LeftSubtractable<i32>).Output is i32
                T.(Negatable).Output is i32
                let subtract = T.subtractedFrom
                let negative = T.negated
                let erased: (ref/i32, ref/T) -> i32 = subtract
                let erasedNegative: (ref/T) -> i32 = negative
                require subtract(44, value) == 42 and erased(44, value) == 42 else => $abort("order")
                require apply(subtract, 44, value) == 42 else => $abort("callable")
                require negative(value) == -2 and erasedNegative(value) == -2 else => $abort("unary")
                require negate(negative, value) == -2 else => $abort("unary callable")
            let value = Offset.init()
            use(value)
            """;
        ScalarEmissionTest.EmitFixture("RequirementItemLeftUnary", Program, string.Empty);
    }

    [Fact]
    public void NonArithmeticRequirementsUseTheSameItemRoute()
    {
        const string Program = "contract Readable\n    func read(self: ref/Self) -> i32\nstruct Value\n    Self is Readable\n    public func read(self: ref/Self) -> i32 => 42\nfunc read<T>(value: ref/T) -> i32\n    T is Readable\n    let operation = T.read\n    return operation(value)\nlet value = Value.init()\nrequire read(value) == 42 else => $abort(\"read\")";
        ScalarEmissionTest.EmitFixture("RequirementItemOrdinary", Program, string.Empty);
    }

    [Fact]
    public void BuiltinScalarAndCompositeComparisonsUseTheirExistingWitnesses()
    {
        const string Program = "func equal<T>(left: ref/T, right: ref/T) -> bool\n    T is Equatable\n    let operation = T.equals\n    return operation(left, right)\nfunc compare<T>(left: ref/T, right: ref/T) -> i32\n    T is Comparable and Owned\n    let operation: (ref/T, ref/T) -> i32 = T.compare\n    return operation(left, right)\nrequire equal(42, 42) and equal((), ()) else => $abort(\"scalar\")\nrequire equal(\"a\", \"a\") else => $abort(\"string\")\nrequire equal((1, \"a\"), (1, \"a\")) else => $abort(\"tuple\")\nrequire compare(1, 2) < 0 else => $abort(\"scalar order\")\nrequire compare(\"b\", \"a\") > 0 else => $abort(\"string order\")\nrequire compare((1, 2), (1, 3)) < 0 else => $abort(\"tuple order\")";
        ScalarEmissionTest.EmitFixture("RequirementItemComparison", Program, string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumericItemChecksUseTheInvocationLocation(bool erased)
    {
        var program = "func add<T>(left: ref/T, right: ref/T) -> T\n    T is Addable<T> and Owned\n    T.(Addable<T>).Output is T\n    let operation" + (erased ? ": (ref/T, ref/T) -> T" : string.Empty) + " = T.added\n    return operation(left, right)\nlet value = add(2147483647, 1)";
        ScalarEmissionTest.EmitFixture("RequirementItemAbort" + erased, program, string.Empty, 1, "Hello.kimi:5:12: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConfinedUsesPublishedPremisesAndIsLostOnErasure(bool bounded, bool erased)
    {
        var c = MinimalEmissionTest.Analyze(ConfinedProgram(bounded, erased));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(bounded && !erased, c.Ownership.Result.IsVerified);
        if (!bounded || erased)
        {
            Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
        }
    }

    [Fact]
    public void ChangedPremisesRevokeTheStoredItemGuarantee()
    {
        var c = MinimalEmissionTest.Analyze(ConfinedProgram(true, false));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var donor = MinimalEmissionTest.Analyze(ConfinedProgram(false, false));
        var original = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), f => f.Name.ToString() == "use");
        var changed = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>(), f => f.Name.ToString() == "use");
        Assert.True(KotoHelper.Replace(original.Parent!, original, changed));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Analyze().IsVerified);
        Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmItemSignaturesAndWitnessContextsReuseStorage()
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

    [Fact]
    public void ExpectedSignatureSelectsTheBoundContractBeforeErasure()
    {
        const string Program = "struct Value\n    Self is Addable<i32> and Addable<i64>\n    associate Addable<i32>.Output is i32\n    associate Addable<i64>.Output is i64\n    public func added(self: ref/Self, right: ref/i32) -> i32 => right + 40\n    public func added(self: ref/Self, right: ref/i64) -> i64 => right + 41\nfunc use<T>(value: ref/T) -> i32\n    T is Addable<i32> and Addable<i64> and Owned\n    T.(Addable<i32>).Output is i32\n    T.(Addable<i64>).Output is i64\n    let operation: (ref/T, ref/i32) -> i32 = T.added\n    return operation(value, 2)\nlet value = Value.init()\nrequire use(value) == 42 else => $abort(\"selected contract\")";
        ScalarEmissionTest.EmitFixture("RequirementItemSelection", Program, string.Empty);
    }

    [Fact]
    public void ReceiverPreservationIsNotAFunctionValueGuarantee()
    {
        const string Program = "contract Source\n    associate Item\n    func take(self: uniq/Self, other: uniq/i32) -> Self.Item\n        effect confined\n        effect preserves results\nfunc accept<F, T>(operation: ref/F)\n    T is Source\n    F is Callable<(uniq/T, uniq/i32) -> T.Item>\n        effect preserves results\n    ()\nfunc use<T>()\n    T is Source\n    let operation = T.take\n    accept(operation)\npublic func main() => ()";
        var c = MinimalEmissionTest.Analyze(Program);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Equal(DiagnosticCode.UnsatisfiedEffectBound_Kd, Assert.Single(c.Ownership.Issues).Code);
    }

    [Fact]
    public void AmbiguousRequirementsRetainBothSignaturesInCliAndLsp()
    {
        const string Program = "func use<T>()\n    T is Addable<i32> and Addable<i64>\n    T.(Addable<i32>).Output is i32\n    T.(Addable<i64>).Output is i64\n    let operation = T.added\npublic func main() => ()";
        var check = DiagnosticCorpus.Check(Program);
        var record = Assert.Single(check.Diagnostics);
        Assert.Equal("AmbiguousBinding_Kd", record.Code);
        Assert.Contains(record.Related!, r => r.Label!.Contains("i32", StringComparison.Ordinal));
        Assert.Contains(record.Related!, r => r.Label!.Contains("i64", StringComparison.Ordinal));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(check.Diagnostics, check.Sources), string.Empty);
        Assert.Contains("T.added", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Contains("i32", sent.Message, StringComparison.Ordinal);
            Assert.Contains("i64", sent.Message, StringComparison.Ordinal);
        }
    }

    private static string ConfinedProgram(bool bounded, bool erased)
        => "contract ConfinedAdd<E>: Addable<E>\n    effect (Addable<E>).added confined\nfunc accept<F, T>(operation: ref/F)\n    F is Callable<(ref/T, ref/T) -> T>\n        effect confined\n    ()\nfunc use<T>()\n    T is " + (bounded ? "ConfinedAdd<T>" : "Addable<T>") + "\n" +
            "    T is Owned\n    T.(Addable<T>).Output is T\n    let operation" + (erased ? ": (ref/T, ref/T) -> T" : string.Empty) + " = T.added\n    accept(operation)\npublic func main() => ()";
}
