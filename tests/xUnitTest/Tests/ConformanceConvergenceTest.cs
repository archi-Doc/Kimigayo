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
        Assert.False(definition.Binding.Result.IsComplete, MinimalEmissionTest.Describe(definition, null));
        Assert.Contains(definition.Binding.Issues, static x => x.Code == Kimi.DiagnosticCode.IncompatibleContractImplementation_Kd);
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var derived = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single(static x => x.Name == "Derived");
        var probe = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe");
        var p = ((IsKoto)probe.TypeConstraints[0]).ConstraintOf()!.Contract!;
        var mapping = c.Binding.GetConformance(derived.TypeOf()!, p)!;
        Assert.Equal(2, Assert.Single(mapping.Paths).InheritedSourceCount);
        for (var i = 0; i < 4; i++)
        {
            var argument = i % 2 == 0 ? "i64" : "i32";
            var donor = CompilationTestHelper.ParseSuccess("struct Derived: Base<" + argument + ">").Kotonoha.RootKoto.NestedDeclarationContainers.OfType<StructKoto>().Single();
            Assert.True(KotoHelper.Replace(derived, derived.Bases[0], donor.Bases[0]));
            Assert.False(mapping.IsVerified);
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
