// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class ObjectHandleModeTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(SemanticsKind.Obj, LoanRequirement.Uniq, (int)ObjectCountingStep.None)]
    [InlineData(SemanticsKind.Rc, LoanRequirement.Ref, (int)ObjectCountingStep.NonAtomic)]
    [InlineData(SemanticsKind.Arc, LoanRequirement.Ref, (int)ObjectCountingStep.Atomic)]
    public void StaticModesSeparateAuthorityFromCounting(SemanticsKind semantics, LoanRequirement authority, int counting)
    {
        var type = new BoundType("handle", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.I32]);
        var mode = type.HandleMode;
        Assert.NotNull(mode);
        Assert.Equal(semantics, mode.Value.Semantics);
        Assert.Equal(authority, mode.Value.PayloadAuthority);
        Assert.Equal(counting, (int)mode.Value.Counting);
    }

    [Theory]
    [InlineData(SemanticsKind.Owner)]
    [InlineData(SemanticsKind.Ref)]
    [InlineData(SemanticsKind.Uniq)]
    [InlineData(SemanticsKind.ObjRef)]
    [InlineData(SemanticsKind.ObjUniq)]
    [InlineData(SemanticsKind.Raw)]
    public void OtherSemanticsHaveNoStrongHandleMode(SemanticsKind semantics)
        => Assert.Null(new BoundType("other", BoundTypeKind.Semantics, semantics: semantics, components: [BoundType.I32]).HandleMode);

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void OwningUpcastsPreserveTheirMode(string mode)
    {
        var c = CompilationTestHelper.Parse($"open struct Base\nstruct Leaf: Base\nfunc widen(value: {mode}/Leaf) -> {mode}/Base => value@move@{mode}/Base");
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Theory]
    [InlineData("obj")]
    [InlineData("rc")]
    [InlineData("arc")]
    public void SharedObjectViewsCanWidenEveryStrongMode(string mode)
    {
        var c = CompilationTestHelper.Parse($"open struct Base\nstruct Leaf: Base\nfunc inspect(value: objref/Base) => ()\nfunc view(value: {mode}/Leaf) => inspect(value@objref/Base)");
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Theory]
    [InlineData("obj", "rc")]
    [InlineData("obj", "arc")]
    [InlineData("rc", "obj")]
    [InlineData("rc", "arc")]
    [InlineData("arc", "obj")]
    [InlineData("arc", "rc")]
    public void AnUpcastCannotChangeTheCountingMode(string source, string target)
    {
        var text = $"open struct Base\nstruct Leaf: Base\nfunc bad(value: {source}/Leaf) -> {target}/Base => value@{target}/Base";
        var path = Path.GetFullPath("object-mode.kimi");
        var c = MinimalEmissionTest.Analyze(text, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("TypeMismatch_Kd", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal("value@" + target + "/Base", error.Text);
        Assert.Equal($"expected {target}/Base, found {source}/Leaf", error.Label);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var diagnostic = Assert.Single(result.Diagnostics);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(error.Label!, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal(diagnostic.Display!.Range, sent.Range);
            Assert.Contains(error.Label!, sent.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("arc/i32")]
    [InlineData("uniq/arc/i32")]
    [InlineData("Option<arc/i32>")]
    public void AtomicOwnershipUsesTheCommonStoragePlan(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func consume(value: {type}) => ()\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("rc/i32")]
    [InlineData("ref/rc/i32")]
    [InlineData("(rc/i32, i32)")]
    [InlineData("Array<rc/i32>")]
    public void RcOwnershipUsesOrdinaryStorageAndCleanup(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func consume(value: {type}) => ()\n()");
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }
}
