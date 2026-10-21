// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC F.3, 3.3, 5: the raw pointer Semantics is spelled raw; unsafe marks only the places where a promise is made. A raw layer may
// state an Origin (SPEC 5.7).
public class RawPointerSyntaxTest
{
    [Fact]
    public void UnsafeIsNoSemanticsName()
    {
        Assert.True(CompilerHelper.TryParse("raw", out var raw));
        Assert.Equal(SemanticsKind.Raw, raw);
        Assert.False(CompilerHelper.TryParse("unsafe", out _));
        Assert.Equal("raw", SemanticsKind.Raw.ToText());
    }

    // raw is reserved only in Semantics positions and after '@'; elsewhere it is an ordinary Name.
    [Fact]
    public void RawIsAnOrdinaryNameElsewhere()
    {
        var c = MinimalEmissionTest.Analyze("struct Sample\n    public let raw: i32\n    public init(raw: i32) => self.raw = raw\n" +
            "func read(pointer: raw/i32) -> raw/i32 => pointer\npublic func main()\n    let raw = Sample.init(raw: 1)\n    let total = raw.raw + 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheOldSpellingIsNoSemantics()
    {
        var c = MinimalEmissionTest.Analyze("func read(pointer: unsafe/i32) -> () => ()\npublic func main() => ()\n");
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 5.7, 3.3.2, 13.5.1: a raw layer states an Origin wherever a Type is written, and a grouped adaptation target its outer raw
    // layer's (the claim). Binding represents none until G160-1, so each is one location-only Unsupported_Kd at the annotation.
    [Theory]
    [InlineData("func f(b: ref/i32, p: raw/i32 during b) => ()", "during b")]
    [InlineData("func f(b: ref/i32) -> raw/i32 during b => b@follow@raw", "during b")]
    [InlineData("func f(x: ref/i32)\n    let p: raw/i32 during x = x@follow@raw", "during x")]
    [InlineData("struct View {source}\n    let value: ref/i32 during source\n    let at: raw/i32 during source", "during source")]
    [InlineData("struct Buffer\n    let data: raw/i32 during self", "during self")]
    [InlineData("enum Cell\n    Some(raw/i32 during self)\n    None", "during self")]
    [InlineData("func f(b: ref/i32, p: Option<raw/i32 during b>) => ()", "during b")]
    [InlineData("func f(b: ref/i32, p: raw/(raw/i32 during b)) => ()", "during b")]
    [InlineData("func f(x: ref/i32)\n    let p: raw/ref/i32 during x = x@raw", "during x")]
    [InlineData("func f(p: raw/i32) -> raw/i32\n    unsafe => return p@(raw/i32 during static)", "during static")]
    public void ARawOriginIsTheLocatedLimit(string declarations, string marked)
    {
        var source = declarations + "\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics);
        Assert.Equal(("Unsupported_Kd", DiagnosticCategory.Unsupported), (error.Code, error.Category));
        Assert.Equal(new SourceSpan(source.LastIndexOf(marked, StringComparison.Ordinal), marked.Length), error.Span);
    }

    // A requirement is one more Type position; its function's InvalidConstraint_Kd beside the limit is a dependent error (PLAN G206).
    [Fact]
    public void ARawOriginInARequirementIsTheLocatedLimit()
    {
        const string Source = "func f<T>(x: T)\n    T is raw/i32 during static\n    ()\npublic func main() => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        var located = new SourceSpan(Source.IndexOf("during static", StringComparison.Ordinal), "during static".Length);
        var error = Assert.Single(c.Diagnostics.Finalize(rejected: true).Diagnostics, x => x.Span == located);
        Assert.Equal(("Unsupported_Kd", DiagnosticCategory.Unsupported), (error.Code, error.Category));
    }

    [Fact]
    public void ARawOriginKeepsIndependentErrorsAcrossRebinding()
    {
        const string Source = "func f(b: ref/i32, p: raw/i32 during b) => ()\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["Unsupported_Kd", "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, c.Diagnostics.Finalize(rejected: true).Diagnostics);
    }
}
