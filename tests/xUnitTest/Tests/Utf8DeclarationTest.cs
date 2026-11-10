// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Formatting profile: library identities, API shape and fixed dependency metadata.</summary>
public class Utf8DeclarationTest
{
    [Theory]
    [InlineData(KimiDeclarationId.FixedBuffer)]
    [InlineData(KimiDeclarationId.WriteWindow)]
    [InlineData(KimiDeclarationId.Utf8Writer)]
    public void ManagedTypesRetainAnExclusiveCovariantOrigin(KimiDeclarationId id)
    {
        var c = MinimalEmissionTest.Analyze("()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        var symbol = c.Library.GetSymbol(id)!;
        var origin = Assert.Single(symbol.Schema!.Origins);
        Assert.Equal(new SlotRequirement(LoanRequirement.Uniq, default), origin.Requirement);
        Assert.Equal(OriginVariance.Covariant, origin.Variance);
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(symbol.Type!, symbol.Declaration));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveOwned(symbol.Type!, symbol.Declaration));
    }

    [Fact]
    public void StringifyIsAnOrdinaryUserName()
    {
        var c = MinimalEmissionTest.Analyze("struct Stringify\n    public init() => ()\nlet value = Stringify.init()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Null(c.Library.GetSymbol(KimiDeclarationId.Stringify));
    }

    [Theory]
    [InlineData("var bytes = [8 of 0@u8]\nlet buffer = Text.fixed(bytes@uniq)\n_ = buffer.length")]
    [InlineData("let buffer = Text.heap(0)\n_ = buffer.capacity")]
    [InlineData("var buffer = Text.heap(0)\nlet writer = Text.writer(buffer@uniq)\n_ = writer.status()")]
    [InlineData("var bytes = [8 of 0@u8]\nvar buffer = Text.fixed(bytes@uniq)\n_ = (buffer@uniq).reserve(1)")]
    public void PublicBufferSignaturesBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
    }

    // SPEC 22.9.3 (R8-W1a): `length` and `capacity` are getters for user code; the buffers' bodies write `length` and HeapBuffer's
    // `capacity` through a private standard setter, so a user write is an access error at the member, for both buffers
    // (FixedBuffer's `capacity` is a `let`, the same answer by another rule).
    [Theory]
    [InlineData("var bytes = [8 of 0@u8]\nvar buffer = Text.fixed(bytes@uniq)\nbuffer.length = 9", "buffer.length")]
    [InlineData("var bytes = [8 of 0@u8]\nvar buffer = Text.fixed(bytes@uniq)\nbuffer.capacity = 9", "buffer.capacity")]
    [InlineData("var buffer = Text.heap(4)\nbuffer.length = 9", "buffer.length")]
    [InlineData("var buffer = Text.heap(4)\nbuffer.capacity = 9", "buffer.capacity")]
    [InlineData("var buffer = Text.heap(4)\nbuffer.length += 1", "buffer.length")]
    public void BufferLengthsAreNotWritable(string source, string site)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal([(DiagnosticCode.InaccessibleBinding_Kd, site)], c.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));
    }

    // SPEC 22.9.4 (R8-W1b): a window's `written` and `remaining` are getters for user code; the window's bodies write them through a
    // private standard setter (a raised `remaining` would let `push` and `append` write past the buffer), so a user write through the
    // window is an access error at the member.
    [Theory]
    [InlineData("func f(window: uniq/WriteWindow)\n    window.written = 9\n", "window.written")]
    [InlineData("func f(window: uniq/WriteWindow)\n    window.remaining = 9\n", "window.remaining")]
    [InlineData("func f(window: uniq/WriteWindow)\n    window.remaining += 1\n", "window.remaining")]
    public void WindowCountsAreNotWritable(string source, string site)
        => this.BufferLengthsAreNotWritable(source, site);

    private static string Describe(Compilation c)
        => MinimalEmissionTest.Describe(c) + "\n" + string.Join("\n", TestDiagnostics.Of(c).Select(x => x.ToString())) +
            "\n" + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node.ToString()));
}
