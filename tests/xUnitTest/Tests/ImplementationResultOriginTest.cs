// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ImplementationResultOriginTest(ITestOutputHelper output)
{
    private const string Empty = "func empty<T>() -> Option<ref/T during fresh> => .None\n";

    private const string Bounded = "func choose<T>(a: ref/T, b: ref/T) -> ref/T during result\n    origin a outlives result\n    origin b outlives result\n    return a\n";

    [Theory]
    [InlineData("")]
    [InlineData(" during fresh")]
    public void SpecializationsInheritUniversallyQuantifiedResultOrigins(string annotation)
    {
        var c = MinimalEmissionTest.Analyze(Empty + "specialize func empty<i32>() -> Option<ref/i32" + annotation + "> => .None\nlet value = empty<i32>()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsSpecialization);
        Assert.Equal(OriginKind.Parameter, implementation.BoundSymbol!.Type!.Components[0].Origin!.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" during fresh")]
    public void OverridesInheritTheSameResultOriginContract(string annotation)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base<T>\n    public virtual func empty(self: objref/Self) -> Option<ref/T during fresh> => .None\nstruct Derived : Base<i32>\n    override func empty(self: objref/Self) -> Option<ref/i32" + annotation + "> => .None\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var implementation = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.IsOverride);
        Assert.True(c.Binding.TryGetVirtualOverride(implementation, out _));
        Assert.Equal(OriginKind.Parameter, implementation.BoundSymbol!.Type!.Components[0].Origin!.Kind);
    }

    [Fact]
    public void AnOmittedBinderRemainsVisibleInTheImplementationBody()
    {
        var c = MinimalEmissionTest.Analyze(Empty + "specialize func empty<i32>() -> Option<ref/i32>\n    let value: Option<ref/i32 during fresh> = .None\n    return value@move\nlet value = empty<i32>()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(" during static", DiagnosticCode.UnprovenOriginContract_Kd)]
    [InlineData(" during renamed", DiagnosticCode.IncompatibleContractImplementation_Kd)]
    public void ImplementationsCannotFixOrRenameTheResultBinder(string annotation, DiagnosticCode expected)
    {
        var c = MinimalEmissionTest.Analyze(Empty + "specialize func empty<i32>() -> Option<ref/i32" + annotation + "> => .None\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == expected);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void AnInheritedResultOnlyBinderKeepsItsOriginalRelations()
        => ScalarEmissionTest.EmitFixture("SpecializationResultOnlyOrigin", Bounded + "specialize func choose<i32>(a: ref/i32, b: ref/i32) -> ref/i32\n    let value: ref/i32 during result = b\n    return value\nlet a = 7\nlet b = 9\nrequire choose(a@ref, b@ref) == 9 else => $abort(\"result\")", string.Empty);

    [Fact]
    public void AnUnboundedResultBinderDoesNotAcceptAnInputLoan()
    {
        var c = MinimalEmissionTest.Analyze("func empty<T>(x: ref/T) -> Option<ref/T during fresh> => .None\nspecialize func empty<i32>(x: ref/i32) -> Option<ref/i32> => .Some(x)\n()");
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnprovenOriginRelation_Kd");
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void ResultOnlyOriginsExecuteThroughVirtualDispatch()
        => ScalarEmissionTest.EmitFixture("OverrideResultOnlyOrigin", "open struct Base\n    public init() => ()\n    public virtual func empty(self: objref/Self) -> (Option<ref/i32 during fresh>, i32) => (.None, 1)\nstruct Derived : Base\n    public init() => ()\n    override func empty(self: objref/Self) -> (Option<ref/i32>, i32) => (.None, 2)\nlet owner = Derived.init()@obj\nlet view = owner@objref/Base\nrequire view.empty().1 == 2 else => $abort(\"override\")", string.Empty);

    [Fact]
    public void EditingTheOriginalRevokesTheInheritedBinder()
    {
        var c = MinimalEmissionTest.Analyze(Empty + "specialize func empty<i32>() -> Option<ref/i32> => .None\nlet value = empty<i32>()");
        var functions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(x => x.Name == "empty").ToArray();
        var original = functions.Single(x => !x.IsSpecialization);
        var implementation = functions.Single(x => x.IsSpecialization);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var previous = implementation.BoundSymbol!.Type!.Components[0].Origin!;
        var donor = MinimalEmissionTest.Analyze(Empty.Replace("fresh", "updated", StringComparison.Ordinal) + "()");
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "empty").ReturnType!;
        Assert.True(KotoHelper.Replace(original, original.ReturnType!, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        var current = implementation.BoundSymbol.Type!.Components[0].Origin!;
        Assert.NotSame(previous, current);
        Assert.Equal("fresh", previous.Name);
        Assert.Equal("updated", current.Name);
    }

    [Fact]
    public void ResultContractFailuresRelateTheOriginalInCliAndLsp()
    {
        const string Annotation = "Option<ref/i32 during static>";
        var source = Empty + "specialize func empty<i32>() -> " + Annotation + " => .None\n()";
        var path = Path.GetFullPath("result-origin.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("UnprovenOriginContract_Kd", error.Code);
        Assert.Equal(DiagnosticCategory.Proof, error.Category);
        Assert.Equal(new SourceSpan(source.IndexOf(Annotation, StringComparison.Ordinal), Annotation.Length), error.Span);
        Assert.Contains(error.Related!, x => x.Role == "requirement");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("fresh", console.Text, StringComparison.Ordinal);
        Assert.Contains("static", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(error.Display!.Range, sent.Range);
            Assert.Contains("not proven", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void InheritedResultOnlyOriginsReuseBindingStorage()
    {
        var c = MinimalEmissionTest.Analyze(Empty + "specialize func empty<i32>() -> Option<ref/i32> => .None\nlet value = empty<i32>()");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
