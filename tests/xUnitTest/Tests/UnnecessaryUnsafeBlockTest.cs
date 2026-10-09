// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 14.3.3: an operation that requires an unsafe context uses the permission of the innermost enclosing Unsafe Block; a block
// whose permission no operation uses is a warning at its keyword. SPEC 23.3.6.9: removing it is a repair candidate exactly when
// that keeps the Body's scope and destruction and defer timing and the statement form allows the edit.
public class UnnecessaryUnsafeBlockTest
{
    private const string Pointer = "public func main()\n    var number: i32 = 1\n    let pointer: raw/i32 = null\n";

    // A statement of an indented body whose Body declares no Name and registers no defer offers its removal with Structure verified:
    // `unsafe => ` is deleted before an inline Body, or the unsafe line is deleted and every Body line loses one indentation level.
    [Theory]
    [InlineData("    unsafe => ()\n", "    ()\n")]
    [InlineData("    unsafe\n        number += 1\n", "    number += 1\n")]
    [InlineData("    unsafe\n        if number == 1\n            number += 1\n\n        number += 2\n", "    if number == 1\n        number += 1\n\n    number += 2\n")]
    public void AnUnusedBlockOffersItsRemoval(string block, string removed)
    {
        var source = Pointer + block;
        var warning = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnnecessaryUnsafeBlock_Kd), warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("unsafe", source.Substring(warning.Span!.Value.Start, warning.Span.Value.Length));
        var repair = Assert.Single(warning.Repairs!);
        Assert.Equal("Repair.RemoveUnsafe", repair.Kind);
        Assert.Equal("Remove unsafe and keep its statements", repair.Title);
        Assert.Equal([RepairCondition.Structure], repair.Verified);
        Assert.Empty(repair.Required);
        Assert.Equal(Pointer + removed, Apply(source, repair.Edits));
    }

    // Structure is refuted for a declaring or deferring Body and for a block outside a statement position, and the edit is left to
    // the author when the unsafe line holds a comment or a raw string spans lines: no candidate.
    [Theory]
    [InlineData("    unsafe\n        let address = pointer@usize\n")]
    [InlineData("    unsafe\n        defer => number += 1\n")]
    [InlineData("    if number == 1 => unsafe => number += 1\n")]
    [InlineData("    unsafe // the permission is unused\n        number += 1\n")]
    [InlineData("    unsafe\n        Console.writeLine(\"\"\"\n    raw\n    \"\"\")\n")]
    public void AnUnusedBlockMayHaveNoCandidate(string block)
    {
        var source = Pointer + block;
        var warning = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnnecessaryUnsafeBlock_Kd), warning.Code);
        Assert.Null(warning.Repairs);
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

    /// <summary>Applies a candidate's edits to the source they were recorded against.</summary>
    /// <param name="source">The source text.</param>
    /// <param name="edits">The edits in position order.</param>
    /// <returns>The edited text.</returns>
    internal static string Apply(string source, RepairEdit[] edits)
    {
        var text = source;
        for (var i = edits.Length - 1; i >= 0; i--)
        {
            text = text.Remove(edits[i].Span.Start, edits[i].Span.Length).Insert(edits[i].Span.Start, edits[i].Text);
        }

        return text;
    }
}
