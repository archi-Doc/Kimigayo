// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ImplicitBaseCallTest
{
    [Fact]
    public void OmittedBaseCallUsesOrdinaryDefaultsBeforeOwnInitializers()
    {
        const string Source = """
            group Helpers
                public func make() -> i32
                    Console.writeLine("default")
                    return 42
                public func own() -> i32
                    Console.writeLine("field")
                    return 7
            open struct Base<T>
                public let item: i32
                protected init(item: i32 = Helpers.make())
                    self.item = item
                    Console.writeLine("base")
                drop => Console.writeLine("base drop")
            open struct Middle<U>: Base<U>
                protected init() => Console.writeLine("middle")
            struct Leaf: Middle<i64>
                public let extra: i32 = Helpers.own()
                public init() => Console.writeLine("leaf")
                drop => Console.writeLine("leaf drop")
            let x = Leaf.init()
            require x.item == 42 and x.extra == 7 else => $abort("initialization")
            """;
        ScalarEmissionTest.EmitFixture("ImplicitBaseDefaults", Source, "default\nbase\nmiddle\nfield\nleaf\nleaf drop\nbase drop\n");
    }

    [Theory]
    [InlineData("protected init(value: i32) => ()", "NoApplicableOverload_Kd")]
    [InlineData("private init() => ()", "NoApplicableOverload_Kd")]
    [InlineData("protected init(value: i32 = 1) => ()\n    protected init(value: string = \"x\") => ()", "AmbiguousBinding_Kd")]
    public void OmissionPreservesBaseSelectionFailures(string declaration, string code)
    {
        var source = "open struct Base\n    " + declaration + "\nstruct Leaf: Base\n    public init() => ()\n()";
        var result = DiagnosticCorpus.Check(source);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(code, record.Code);
        Assert.Equal("init()", source.Substring(record.Span!.Value.Start, record.Span.Value.Length));
        Assert.NotEmpty(record.Related!);
        Assert.NotNull(record.Reason);
        var explicitRecord = Assert.Single(DiagnosticCorpus.Check(source.Replace("public init() =>", "public init(): base() =>", StringComparison.Ordinal)).Diagnostics);
        Assert.Equal((record.Code, record.Label, record.Message), (explicitRecord.Code, explicitRecord.Label, explicitRecord.Message));
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new(result.Diagnostics, result.Sources), string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(result.Sources[record.Source].Path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(result, [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
        }
    }

    // SPEC 6.2.3.6: the omitted base query, which publishes nothing, decides as the bound base call does: the same selected
    // constructor or the same class of failed selection. It leaves the obligations and diagnostics of the pass unchanged.
    [Theory]
    [InlineData("open struct Base\n    public init() => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    protected init() => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    private init() => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.NoneApplicable))]
    [InlineData("open struct Base\n    protected init(x: i32 = 1) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    protected init(x: i32) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.NoneApplicable))]
    [InlineData("open struct Base\n    protected init() => ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    private init() => ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base\n    protected init(x: i32 = 1) => ()\n    protected init(y: string = \"x\") => ()\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Ambiguous))]
    [InlineData("open struct Base\n    public var count: i32\nstruct Leaf: Base\n    public init() => ()\n()", nameof(OmittedBaseOutcome.NoBaseConstructor))]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\nstruct Leaf: Base<i32>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\nstruct Leaf<U>: Base<U>\n    U is Equatable\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Unproven))]
    [InlineData("struct NoEq\n    public var v: i32 = 0\nopen struct Base<T>\n    protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()\nstruct Leaf: Base<NoEq>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init() => ()\n    protected init(x: i32 = 0)\n        T is Equatable\n        ()\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base<T>\n    protected init() => ()\nstruct Leaf: Base<i64>\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    [InlineData("open struct Base {a}\n    public var n: i32 = 1\n    public init() => ()\nstruct Leaf {b}: Base during b\n    public init() => ()\n()", nameof(OmittedBaseOutcome.Selected))]
    public void OmittedBaseQueryAgreesWithTheBoundCall(string source, string outcome)
        => AssertOmittedBaseAgreement(CompilationTestHelper.ParseSuccess(source), outcome);

    [Theory]
    [InlineData("internal", nameof(OmittedBaseOutcome.NoneApplicable))]
    [InlineData("protected internal", nameof(OmittedBaseOutcome.Selected))]
    public void OmittedBaseQueryUsesTheDerivedModulesAccess(string access, string outcome)
        => AssertOmittedBaseAgreement(ModuleBindingTest.Create("alias Lib.Api\nstruct Leaf: Base\n    public init() => ()\n()", "public group Api\n    public open struct Base\n        " + access + " init() => ()"), outcome);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedOmittedBaseQueriesAllocateNothing()
    {
        var c = CompilationTestHelper.ParseSuccess("open struct Base<T>\n    protected init() => ()\n    protected init(x: i32 = 0)\n        T is Equatable\n        ()\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()");
        Assert.True(c.Bind().IsComplete);
        var constructor = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.HasOmittedBaseInitializer);
        var selected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => selected &= c.Binding.SelectOmittedBaseConstructor(constructor).Outcome == OmittedBaseOutcome.Selected, iterations: 64, warmupIterations: 16));
        Assert.True(selected);
    }

    // SPEC 6.2.3.6: a base without constructors is one Language absence at the base call target, omitted or written, placed alike by the
    // CLI and the language server; a base that would receive a synthesized constructor remains unsupported until it is synthesized.
    [Theory]
    [InlineData("open struct Base\n    public var count: i32\n", "UnresolvedBinding_Kd")]
    [InlineData("open struct Root\n    public init() => ()\nopen struct Base: Root\n    public var count: i32 = 1\n", "UnsupportedBinding_Kd")]
    public void ABaseWithoutConstructorsIsOneLocatedProblem(string bases, string code)
    {
        const string Leaf = "struct Leaf: Base\n    public init() => ()\n()";
        foreach (var written in new[] { false, true })
        {
            var source = bases + (written ? Leaf.Replace("public init() =>", "public init(): base() =>", StringComparison.Ordinal) : Leaf);
            var result = DiagnosticCorpus.Check(source);
            var record = Assert.Single(result.Diagnostics);
            Assert.Equal((code, written ? "base" : "init()"), (record.Code, source.Substring(record.Span!.Value.Start, record.Span.Value.Length)));
            if (code == nameof(DiagnosticCode.UnresolvedBinding_Kd))
            {
                Assert.StartsWith("Base has no constructor: its Field count has no initializer", record.Note, StringComparison.Ordinal);
            }

            var identity = SourceIdentity.FromPath(result.Sources[record.Source].Path);
            var sent = Assert.Single(WorkspaceCheck.Place(result, [identity], identity, true)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
        }
    }

    // SPEC 8.4.8.2: an Unknown premise of a base constructor defers the omitted and the written base call alike, and only when it can
    // affect the selection: the plain init() beats a conditional one that uses a default, and a better conditional one stays unproven.
    [Theory]
    [InlineData("protected init() => ()\n    protected init(x: i32 = 0)\n        T is Equatable\n        ()", null)]
    [InlineData("protected init()\n        T is Equatable\n        ()\n    protected init(x: i32 = 0) => ()", "UnprovenConstraint_Kd")]
    public void AnUnknownBasePremiseDefersOnlyASelectionItCanAffect(string declarations, string? code)
    {
        var source = "open struct Base<T>\n    public var count: i32 = 1\n    " + declarations + "\nstruct Leaf<U>: Base<U>\n    public init() => ()\n()";
        foreach (var written in new[] { false, true })
        {
            var result = DiagnosticCorpus.Check(written ? source.Replace("public init() =>", "public init(): base() =>", StringComparison.Ordinal) : source);
            Assert.Equal(code is null ? [] : [code], result.Diagnostics.Select(static x => x.Code));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(": base()")]
    public void AccessibleZeroArgumentBaseHasOneInvocation(string clause)
    {
        var source = "open struct Base\n    protected init() => Console.writeLine(\"base\")\nstruct Leaf: Base\n    public init()" + clause + " => ()\nlet x = Leaf.init()";
        ScalarEmissionTest.EmitFixture("ImplicitBaseSingle" + clause.Length, source, "base\n");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RebindingReusesTheImplicitCallWithoutChangingWrittenSyntax()
    {
        const string Source = "open struct Base\n    protected init(value: i32 = 42) => ()\nstruct Leaf: Base\n    public init() => ()\nlet x = Leaf.init()";
        var c = MinimalEmissionTest.Analyze(Source);
        var constructor = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(x => x.IsConstructor);
        var initializer = constructor.BaseInitializer;
        Assert.NotNull(initializer);
        Assert.DoesNotContain(": base", constructor.ToString(), StringComparison.Ordinal);
        Assert.Equal(constructor.SignatureSpan, initializer.Span);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.Same(initializer, constructor.BaseInitializer);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    private static void AssertOmittedBaseAgreement(Compilation c, string expected)
    {
        c.Binding.CaptureOmittedBaseQueries = true;
        c.Bind();
        var constructor = c.SourceModules.SelectMany(static x => KotoTree.Walk(x.RootKoto)).OfType<FunctionKoto>().Single(static x => x.HasOmittedBaseInitializer);
        var query = c.Binding.OmittedBaseQueries![constructor];
        var call = constructor.BaseInitializer!;
        (OmittedBaseOutcome Outcome, FunctionKoto? Winner) bound = call.BoundCall is { } selected ? (OmittedBaseOutcome.Selected, (FunctionKoto?)selected.Target.Declaration)
            : call.Method.BindingFailure == BindingFailure.MissingName ? (OmittedBaseOutcome.NoBaseConstructor, null)
            : (call.BindingFailure switch
            {
                BindingFailure.NoApplicableCandidate => OmittedBaseOutcome.NoneApplicable,
                BindingFailure.Ambiguous => OmittedBaseOutcome.Ambiguous,
                BindingFailure.UnprovenConstraint => OmittedBaseOutcome.Unproven,
                _ => OmittedBaseOutcome.Unsupported,
            }, null);
        Assert.Equal((Enum.Parse<OmittedBaseOutcome>(expected), bound.Outcome, bound.Winner), (query.Outcome, query.Outcome, query.Winner));

        // A query inside the finished pass changes nothing the pass published.
        var issues = c.Binding.Issues.Count;
        var obligations = c.Binding.Obligations.Count;
        var state = (call.BindingState, call.BindingFailure, call.BoundType);
        Assert.Equal(query, c.Binding.SelectOmittedBaseConstructor(constructor));
        Assert.Equal((issues, obligations, state), (c.Binding.Issues.Count, c.Binding.Obligations.Count, (call.BindingState, call.BindingFailure, call.BoundType)));
    }
}
