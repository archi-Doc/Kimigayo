// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 14.3.3: an operation that requires an unsafe context uses the permission of the innermost enclosing Unsafe Block; a block
// whose permission no operation uses is a warning at its keyword, and the Advice recommends removing it only when that keeps the
// Body's scope and destruction and defer timing.
public class UnnecessaryUnsafeBlockTest
{
    private const string Pointer = "public func main()\n    var number: i32 = 1\n    let pointer: raw/i32 = null\n";

    [Theory]
    [InlineData("    unsafe => ()\n", "Remove 'unsafe' and keep the statements")]
    [InlineData("    unsafe\n        number += 1\n", "Remove 'unsafe' and keep the statements")]
    [InlineData("    unsafe\n        let address = pointer@usize\n", "would merge its Body's declarations")]
    [InlineData("    unsafe\n        defer => number += 1\n", "would merge its Body's declarations")]
    public void AnUnusedBlockIsWarnedAtItsKeyword(string block, string advice)
    {
        var source = Pointer + block;
        var warning = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnnecessaryUnsafeBlock_Kd), warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("unsafe", source.Substring(warning.Span!.Value.Start, warning.Span.Value.Length));
        Assert.Contains(advice, warning.Advice);
    }

    [Theory]
    [InlineData("    unsafe\n        let value = *pointer\n")]
    [InlineData("    unsafe => pointer[1] = 2\n")]
    [InlineData("    var moved = pointer\n    unsafe => moved += 1\n")]
    [InlineData("    unsafe\n        defer => *pointer = 3\n")]
    public void ABlockWhosePermissionIsUsedIsNotWarned(string block)
        => Assert.Empty(DiagnosticCorpus.Check(Pointer + block).Diagnostics);

    [Fact]
    public void AnUnsafeCallUsesTheBlock()
        => Assert.Empty(DiagnosticCorpus.Check("unsafe func touch(pointer: raw/i32) -> () => ()\n" + Pointer + "    unsafe => touch(pointer)\n").Diagnostics);

    // The innermost block grants the permission, so an enclosing block whose own operations need none is unused.
    [Fact]
    public void OnlyTheInnermostBlockIsUsed()
    {
        var source = Pointer + "    unsafe\n        let address = pointer@usize\n        unsafe => *pointer = 4\n";
        var warning = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnnecessaryUnsafeBlock_Kd), warning.Code);
        Assert.Equal(source.IndexOf("unsafe", StringComparison.Ordinal), warning.Span!.Value.Start);
    }
}
