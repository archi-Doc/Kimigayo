// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class RequirementIdentityTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("P<i32>, P<i64>")]
    [InlineData("P<i64>, P<i32>")]
    public void DistinctBoundParentsKeepBothRequirements(string parents)
    {
        var c = MinimalEmissionTest.Analyze(Declarations(parents));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        Assert.Equal(2, q.BoundSymbol!.Contract!.Requirements.Count);
    }

    [Theory]
    [InlineData("Forward", "P<i32>, P<i64>")]
    [InlineData("Reverse", "P<i64>, P<i32>")]
    public void BothBoundSignaturesExecuteThroughTheRefiningContract(string name, string parents)
    {
        const string Use = """

            func use<T>(source: ref/T, small: i32, large: i64) -> i64
                T is Q
                let first: i32 = source.value(small)
                require first == small else => $abort("small")
                return source.value(large)
            let source = S.init()
            let small: i32 = 7
            let large: i64 = 9
            Console.writeLine("\(use(source@ref, small, large))")
            """;
        ScalarEmissionTest.EmitFixture("RequirementIdentity" + name, Declarations(parents) + Use, "9\n");
    }

    [Fact]
    public void EqualBoundReferencesStillDeduplicateAcrossADiamond()
    {
        const string Source = """
            contract P<T>
                func value(self: ref/Self, x: T) -> T
            contract Left: P<i32>
            contract Right: P<i32>
            contract Q: Left, Right
            struct S
                Self is Q
                public init() => ()
                public func value(self: ref/Self, x: i32) -> i32 => x
            let source = S.init()
            """;
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        Assert.Single(q.BoundSymbol!.Contract!.Requirements);
    }

    [Theory]
    [InlineData("i32", 1)]
    [InlineData("i64", 2)]
    public void BoundSubstitutionDeduplicatesOnlyEqualReferences(string argument, int count)
    {
        var source = Declarations("P<i32>, P<U>").Replace("contract Q:", "contract Q<U>:", StringComparison.Ordinal)
            .Replace("Self is Q", "Self is Q<" + argument + ">", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var s = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "S");
        var reference = Assert.Single(s.ConstraintNodes).BoundConstraint!.Contract!;
        Assert.Equal(count, reference.Contract!.Requirements.Count);
        Assert.Equal(count, c.Binding.GetConformance(s.BoundType!, reference)!.Witnesses.Count);
    }

    [Theory]
    [InlineData("P<i32>")]
    [InlineData("P<i64>")]
    public void AQualifiedEffectBoundAppliesOnlyToItsBoundRequirement(string bounded)
    {
        var source = Declarations("P<i32>, P<i64>").Replace("struct S", "    effect (" + bounded + ").value confined\nstruct S", StringComparison.Ordinal);
        source = source.Replace("-> " + (bounded == "P<i32>" ? "i64" : "i32") + " => x", "-> " + (bounded == "P<i32>" ? "i64" : "i32") + "\n        Console.writeLine(\"unbounded\")\n        return x", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void DistinctBoundRequirementsCanDeclareTheSameEffect()
    {
        var source = Declarations("P<i32>, P<i64>").Replace("struct S", "    effect (P<i32>).value confined\n    effect (P<i64>).value confined\nstruct S", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("i64", false)]
    public void ACallCannotBorrowAnEffectBoundFromASiblingReference(string argument, bool valid)
    {
        var source = "contract P<T>\n    func value(self: ref/Self, x: T) -> T\ncontract Q: P<i32>, P<i64>\n    effect (P<i32>).value confined\n" +
            "contract Safe\n    func run(self: ref/Self) -> " + argument + "\n        effect confined\n" +
            "struct Wrapper<T>\n    T is Q\n    Self is Safe\n    let inner: T\n    public func run(self: ref/Self) -> " + argument + "\n        let x: " + argument + " = 7\n        return self.inner.value(x)\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
        }
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("i64", false)]
    public void PreservedResultsAreSpecificToTheBoundRequirement(string second, bool valid)
    {
        var source = "contract P<K, R>\n    func take(self: uniq/Self, key: K) -> R\n        effect preserves results\n" +
            "contract Q<R>: P<i32, R>, P<i64, R>\n" +
            "func pair<S, R>(source: uniq/S) -> (R, R)\n    S is Q<R>\n    let firstKey: i32 = 1\n    let secondKey: " + second + " = 2\n" +
            "    let first = source.take(firstKey)\n    let second = source.take(secondKey)\n    return (first@move, second@move)\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(valid == c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        if (!valid)
        {
            c.Ownership.ReportDiagnostics();
            Assert.Equal("CallEffectConflict_Kd", Assert.Single(TestDiagnostics.Of(c)).Code);
        }
    }

    [Fact]
    public void InheritedWitnessesAndErasedItemsKeepTheirBoundReferences()
    {
        var declarations = Declarations("P<i32>, P<i64>").Replace("struct S", "open struct S", StringComparison.Ordinal);
        const string Use = """

            struct Derived: S
                public init() : base() => ()
            func use<T>(source: ref/T) -> i64
                T is Q and Owned
                let small: (ref/T, i32) -> i32 = T.value
                let large: (ref/T, i64) -> i64 = T.value
                require small(source, 7) == 7 else => $abort("item")
                return large(source, 9)
            let source = Derived.init()
            Console.writeLine("\(use(source@ref))")
            """;
        ScalarEmissionTest.EmitFixture("RequirementIdentityInheritedItems", declarations + Use, "9\n");
    }

    [Fact]
    public void RebindingRebuildsTheBoundMappings()
    {
        var c = MinimalEmissionTest.Analyze(Declarations("P<i32>, P<i64>"));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q").BoundSymbol!;
        var s = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "S");
        var mapping = c.Binding.GetConformance(s.BoundType!, q)!;
        var previous = mapping.Witnesses.ToArray();
        Assert.Equal(2, previous.Length);
        var declaration = (ContractKoto)q.Declaration;
        var donor = CompilationTestHelper.ParseSuccess("contract Q: P<i64>").Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single();
        Assert.True(KotoHelper.Replace(declaration, declaration.Bases[0], donor.Bases[0]));
        Assert.False(mapping.IsVerified);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(mapping.IsVerified);
        Assert.Equal(previous[1].Identity, Assert.Single(mapping.Witnesses).Identity);
        Assert.Null(mapping.GetImplementation(previous[0].Identity));
        Assert.Same(previous[1].Implementation, mapping.GetImplementation(previous[1].Identity));
    }

    [Fact]
    public void AQualifiedBoundDiagnosticKeepsTheSelectedRequirementAndEarlierBound()
    {
        const string Source = "contract P<T>\n    func value(self: ref/Self, x: T) -> T\ncontract Q: P<i32>, P<i64>\n    effect (P<i32>).value confined\n    effect (P<i64>).value confined\n    effect (P<i64>).value confined\nlet present = 1";
        var path = Path.GetFullPath("requirement-identity.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("InvalidEffectBound_Kd", record.Code);
        Assert.Equal("value already has confined", record.Label);
        Assert.Equal(new SourceSpan(Source.LastIndexOf("effect", StringComparison.Ordinal), "effect (P<i64>).value confined".Length), record.Span);
        Assert.Equal(["bound", "requirement"], record.Related!.Select(static x => x.Role));
        Assert.Equal(Source.IndexOf("effect (P<i64>)", StringComparison.Ordinal), record.Related![0].Span!.Value.Start);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range!.Value, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBoundRequirementRebindDoesNotAllocate()
    {
        var c = MinimalEmissionTest.Analyze(Declarations("P<i32>, P<i64>").Replace("struct S", "    effect (P<i32>).value confined\nstruct S", StringComparison.Ordinal));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var bytes = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, bytes);
    }

    private static string Declarations(string parents)
        => "contract P<T>\n    func value(self: ref/Self, x: T) -> T\ncontract Q: " + parents + "\n" +
            "struct S\n    Self is Q\n    public init() => ()\n" +
            "    public func value(self: ref/Self, x: i32) -> i32 => x\n" +
            "    public func value(self: ref/Self, x: i64) -> i64 => x\nlet present = 1\n";
}
