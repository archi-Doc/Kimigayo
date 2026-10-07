// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class VirtualEntryErasureTest(ITestOutputHelper output)
{
    private const string Base = "open struct Base<T>\n    public func ordinary(self: objref/Self) -> i32 => 1\n    public virtual func read(self: objref/Self, other: objref/Leaf<T>) -> i32 => 1\n";

    [Theory]
    [InlineData("        return base.ordinary()")]
    [InlineData("        return self.ordinary()")]
    [InlineData("        return (self@copy).ordinary()")]
    [InlineData("        return Base<T>.ordinary(self@objref/Base<T>)")]
    [InlineData("        let f = func[self]() -> i32 => base.ordinary()\n        return f()")]
    [InlineData("        let f = func[self@move]() -> i32 => self.ordinary()\n        return f()")]
    public void OverrideReceiverInheritsErasureWithoutAnOwnedTypePremise(string body)
    {
        var c = MinimalEmissionTest.Analyze(Source(body));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
        var use = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.CallStorage?.Target.Name == "ordinary");
        Assert.NotNull(use.CallStorage);
        var site = body.Contains("@objref/Base", StringComparison.Ordinal)
            ? (Koto)Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>(), x => x.ConversionBinding == ConversionBinding.ObjectUpcast) : use;
        Assert.True(c.Binding.TryGetObjectErasure(site, out var evidence));
        Assert.True(evidence.Entry!.IsOverride);
        Assert.Equal("Leaf", evidence.Source.Symbol!.Name);
        Assert.Equal("Base", evidence.Target.Symbol!.Name);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), failure);
    }

    [Fact]
    public void SuccessiveBaseViewsInheritTheSameEntryEvidence()
    {
        var source = Source("        return Base<T>.ordinary((self@objref/Middle<T>)@objref/Base<T>)")
            .Replace("struct Leaf<T> : Base<T>", "open struct Middle<U> : Base<U>\nstruct Leaf<T> : Middle<T>", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, x.Code));
        var conversions = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().ToArray();
        Assert.Equal(2, conversions.Length);
        Assert.All(conversions, conversion =>
        {
            Assert.True(c.Binding.TryGetObjectErasure(conversion, out var evidence));
            Assert.True(evidence.Entry!.IsOverride);
        });
    }

    [Fact]
    public void AnotherObjectOfTheSameTypeDoesNotInheritReceiverEvidence()
    {
        var c = MinimalEmissionTest.Analyze(Source("        _ = self.ordinary()\n        return other.ordinary()"));
        c.Binding.ReportDiagnostics();
        var failure = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "UnprovenConstraint_Kd");
        Assert.Equal("other.ordinary()", failure.Text);
        Assert.Contains("Owned", failure.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnedFailureForAnotherObjectKeepsIndependentCliAndLspEvidence()
    {
        var path = Path.GetFullPath("virtual-entry-erasure.kimi");
        var c = MinimalEmissionTest.Analyze(Source("        _ = self.ordinary()\n        _ = missing\n        return other.ordinary()"), path);
        c.Binding.ReportDiagnostics();
        Assert.Contains(TestDiagnostics.Of(c), x => x.Code == "UnresolvedBinding_Kd" && x.Text == "missing");
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == "UnprovenConstraint_Kd");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("Owned", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("Owned", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void EditingTheReceiverRevokesThePreviousOperationEvidence()
    {
        var c = MinimalEmissionTest.Analyze(Source("        return self.ordinary()"));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var member = (MemberAccessKoto)call.Method;
        var original = member.Left;
        Assert.True(c.Binding.TryGetObjectErasure(call, out var before));
        var donor = MinimalEmissionTest.Analyze(Source("        return other.ordinary()"));
        var changed = ((MemberAccessKoto)Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<InvocationKoto>()).Method).Left;
        Assert.True(KotoHelper.Replace(member, original, changed));
        Assert.False(c.Binding.TryGetObjectErasure(call, out _));
        c.Bind();
        Assert.False(c.Binding.TryGetObjectErasure(call, out _));
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.True(KotoHelper.Replace(member, changed, original));
        c.Bind();
        Assert.True(c.Binding.TryGetObjectErasure(call, out var after));
        Assert.Equal(before, after);
    }

    [Fact]
    public void AnOriginalVirtualHasNoImplicitOwnedReceiverGuarantee()
    {
        var source = "open struct Root\n    public func ordinary(self: objref/Self) -> i32 => 1\nopen struct Base<T> : Root\n    public virtual func read(self: objref/Self) -> i32 => base.ordinary()\n()";
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        Assert.Equal("base.ordinary()", Assert.Single(TestDiagnostics.Of(c), x => x.Code == "UnprovenConstraint_Kd").Text);
    }

    [Fact]
    public void AnInvalidImplementationIsAPrerequisiteRatherThanAnOwnedFailure()
    {
        var source = Source("        return base.ordinary()").Replace("override func read(self: objref/Self, other:", "override func read(self: objref/Self, renamed:", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.OverrideContractMismatch_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.False(c.Binding.TryGetObjectErasure(call, out _));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEntryErasureChecksReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze(Source("        let f = func[self]() -> i32 => base.ordinary()\n        return f() + self.ordinary()"));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Bind(), iterations: 64, warmupIterations: 32));
    }

    private static string Source(string body) => Base + "struct Leaf<T> : Base<T>\n    override func read(self: objref/Self, other: objref/Leaf<T>) -> i32\n" + body + "\n()";
}
