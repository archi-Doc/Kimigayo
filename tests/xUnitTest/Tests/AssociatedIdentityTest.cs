// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class AssociatedIdentityTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("C<i32>, C<i64>")]
    [InlineData("C<i64>, C<i32>")]
    public void DistinctBoundParentsKeepBothAssociatedTypes(string parents)
    {
        var c = MinimalEmissionTest.Analyze(Declarations(parents));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        Assert.Equal(2, q.SymbolOf()!.Contract!.AssociatedTypes.Count);
    }

    [Theory]
    [InlineData("Forward", "C<i32>, C<i64>")]
    [InlineData("Reverse", "C<i64>, C<i32>")]
    public void BothFamiliesExecuteThroughTheRefiningContract(string name, string parents)
    {
        const string Use = """

            func relay<T>(source: ref/T, small: ref/i32 during a, large: ref/i64 during a) -> T.(C<i64>).Item(a)
                T is Q
                let first: T.(C<i32>).Item(a) = source.borrow(small)
                return source.borrow(large)
            let source = S.init()
            let small: i32 = 7
            let large: i64 = 9
            require relay(source@ref, small@ref, large@ref) == 9 else => $abort("family")
            Console.writeLine("families")
            """;
        ScalarEmissionTest.EmitFixture("AssociatedIdentity" + name, Declarations(parents) + Use, "families\n");
    }

    [Theory]
    [InlineData("C<i32>, C<i64>")]
    [InlineData("C<i64>, C<i32>")]
    public void AnUnqualifiedFamilyIsAmbiguousAcrossDistinctBindings(string parents)
    {
        var c = MinimalEmissionTest.Analyze("contract C<E>\n    associate Item(a)\ncontract Q: " + parents + "\nfunc use<T>(value: ref/T during a) -> T.Item(a)\n    T is Q\n    $abort(\"unused\")");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Theory]
    [InlineData("i32", 1)]
    [InlineData("i64", 2)]
    public void SubstitutionDeduplicatesOnlyEqualFamilyReferences(string argument, int count)
    {
        var source = "contract C<E>\n    associate Item(a) is E\ncontract Q<U>: C<i32>, C<U>\nstruct S\n    Self is Q<" + argument + ">\n    public init() => ()\nlet value = S.init()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var s = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "S");
        var reference = Assert.Single(s.ConstraintNodes).ConstraintOf()!.Contract!;
        Assert.Equal(count, reference.Contract!.AssociatedTypes.Count);
        Assert.Equal(count, c.Binding.GetConformance(s.TypeOf()!, reference)!.AssociatedTypes.Count);
    }

    [Theory]
    [InlineData("C<i32>", true)]
    [InlineData("C<i64>", true)]
    [InlineData("C", false)]
    [InlineData("C<i16>", false)]
    public void RefinementsSelectAnExactAvailableReference(string qualifier, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a) for ref/E during a\ncontract Q: C<i32>, C<i64>\n    associate " + qualifier + ".Item(b) is ref/i32 during b\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (qualifier == "C")
        {
            Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
        }
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("i64", false)]
    public void UniversalFamilyFactsDoNotCrossBoundReferences(string projected, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>\n    associate C<i32>.Item(a) is Copy\n" +
            "func copy<T>(value: T.(C<" + projected + ">).Item(a), marker: ref/T during a)\n    T is Q\n    let duplicate = value\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(valid == c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("U", true)]
    [InlineData("V", false)]
    public void FormationDomainsUseTheSelectedDeclaringReference(string target, bool valid)
    {
        var source = "contract C<E>\n    associate Item(a) for ref/E during a\ncontract Q<U, V>: C<U>, C<V>\n" +
            "func use<T, U, V>(source: ref/T, value: ref/U during a) -> T.(C<" + target + ">).Item(a)\n    T is Q<U, V>\n    $abort(\"unused\")\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("i32", 1)]
    [InlineData("i64", 2)]
    public void InheritedAssociatedIdentitiesComposeBaseSubstitution(string argument, int count)
    {
        var source = "contract C<E>\n    associate Item(a) is E\ncontract Q<U>: C<i32>, C<U>\nopen struct Base<U>\n    Self is Q<U>\n    public init() => ()\n" +
            "struct Derived: Base<" + argument + ">\n    Self is Q<" + argument + ">\n    public init() : base() => ()\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var derived = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "Derived");
        var reference = Assert.Single(derived.ConstraintNodes).ConstraintOf()!.Contract!;
        Assert.All(c.Binding.GetConformanceDefinition(derived.TypeOf()!, reference)!.Paths, path =>
        {
            Assert.True(path.IsVerified);
            Assert.Equal(count, path.AssociatedTypes.Count);
            foreach (var (identity, type) in path.AssociatedTypes)
            {
                Assert.Same(identity.Contract.Type!.Components[0], type);
            }
        });
    }

    [Fact]
    public void EqualBoundDiamondsNeedOnlyOneSpecification()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Left: C<i32>\ncontract Right: C<i32>\ncontract Q: Left, Right\nstruct S\n    Self is Q\n    associate Item(a) is i32\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        Assert.Single(q.SymbolOf()!.Contract!.AssociatedTypes);
    }

    [Fact]
    public void EditsRemoveObsoleteAssociatedMappings()
    {
        const string Source = "contract C<E>\n    associate Item(a) is E\ncontract Q: C<i32>, C<i64>\nstruct S\n    Self is Q\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var q = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single(static x => x.Name == "Q");
        var s = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single();
        var mapping = c.Binding.GetConformance(s.TypeOf()!, q.SymbolOf()!)!;
        var old = mapping.AssociatedTypes.Keys.ToArray();
        Assert.Equal(2, old.Length);
        var donor = CompilationTestHelper.ParseSuccess("contract Q: C<i64>").Kotonoha.RootKoto.NestedDeclarationContainers.OfType<ContractKoto>().Single();
        Assert.True(KotoHelper.Replace(q, q.Bases[0], donor.Bases[0]));
        Assert.False(mapping.IsVerified);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(old[1], Assert.Single(mapping.AssociatedTypes).Key);
        Assert.False(mapping.AssociatedTypes.ContainsKey(old[0]));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBoundAssociatedRebindDoesNotAllocate()
    {
        var c = MinimalEmissionTest.Analyze(Declarations("C<i32>, C<i64>"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var bytes = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void AmbiguityExplainsBothBoundReferencesInCliAndLsp()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>\n    associate C.Item(a) is i32\nlet present = 1";
        var path = Path.GetFullPath("associated-identity.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal("AmbiguousBinding_Kd", record.Code);
        Assert.Equal("no unique best candidate among 2 candidates", record.Label);
        Assert.Equal(new SourceSpan(Source.IndexOf("C.Item", StringComparison.Ordinal), "C.Item".Length), record.Span);
        Assert.Equal(["C<i32>.Item", "C<i64>.Item"], record.Related!.Select(static x => x.Label));
        Assert.Contains("distinct associated-Type identities", record.Note);
        Assert.Empty(record.Repairs ?? []);
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

    [Fact]
    public void AnAmbiguousSubjectKeepsIndependentRequirementAndExpressionFailures()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>\n    associate C.Item(a) is Missing\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        // A requirement name may denote either a Type or a Contract, so a missing one is UnresolvedBinding.
        Assert.Equal(["AmbiguousBinding_Kd", "UnresolvedBinding_Kd", "TypeMismatch_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AmbiguousBindingsCountDistinctReferencesAndReuseFailureStorage()
    {
        const string Source = "contract C<E>\n    associate Item(a)\ncontract Q: C<i32>, C<i64>, C<i16>\n    associate C.Item(a) is i32\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        var bytes = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.Equal(0, bytes);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal("no unique best candidate among 3 candidates", Assert.Single(TestDiagnostics.Of(c)).Label);
    }

    [Fact]
    public void InheritedFamiliesExecuteWithTheirOriginalMappings()
    {
        var declarations = Declarations("C<i32>, C<i64>").Replace("struct S", "open struct S", StringComparison.Ordinal);
        const string Use = """

            struct Derived: S
                public init() : base() => ()
            func relay<T>(source: ref/T, small: ref/i32 during a, large: ref/i64 during a) -> T.(C<i64>).Item(a)
                T is Q
                let first: T.(C<i32>).Item(a) = source.borrow(small)
                return source.borrow(large)
            let source = Derived.init()
            let small: i32 = 7
            let large: i64 = 9
            require relay(source@ref, small@ref, large@ref) == 9 else => $abort("inherited family")
            Console.writeLine("inherited")
            """;
        ScalarEmissionTest.EmitFixture("AssociatedIdentityInherited", declarations + Use, "inherited\n");
    }

    private static string Declarations(string parents) => """
        contract C<E>
            associate Item(a)
            func borrow(self: ref/Self, value: ref/E during a) -> Self.Item(a)
        contract Q: PARENTS
        struct S
            Self is Q
            associate C<i32>.Item(a) is ref/i32 during a
            associate C<i64>.Item(a) is ref/i64 during a
            public init() => ()
            public func borrow(self: ref/Self, value: ref/i32 during a) -> ref/i32 during a => value
            public func borrow(self: ref/Self, value: ref/i64 during a) -> ref/i64 during a => value

        """.Replace("PARENTS", parents, StringComparison.Ordinal);
}
