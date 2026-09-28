// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Formatting profile: library identities, API shape and fixed dependency metadata.</summary>
public class Utf8DeclarationTest
{
    [Fact]
    public void StandardDeclarationsBind()
    {
        var c = MinimalEmissionTest.Analyze("()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        foreach (var declaration in c.Library.Declarations)
        {
            if (declaration.Id >= KimiDeclarationId.Utf8Format)
            {
                Assert.Equal(KimiDeclarationState.Validated, declaration.State);
            }
        }
    }

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
        Assert.Equal(LoanRequirement.Uniq, origin.LoanRequirement);
        Assert.Equal(OriginVariance.Covariant, origin.Variance);
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(symbol.Type!, symbol.Declaration));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveOwned(symbol.Type!, symbol.Declaration));
    }

    [Fact]
    public void StringifyIsAnOrdinaryUserName()
    {
        var c = MinimalEmissionTest.Analyze("struct Stringify\n    public init() => ()\nlet value = Stringify.init()");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.DoesNotContain(c.Library.Declarations.ToArray(), x => x.Id == KimiDeclarationId.Stringify);
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

    [Theory]
    [InlineData(KimiDeclarationId.FixedBuffer)]
    [InlineData(KimiDeclarationId.WriteWindow)]
    [InlineData(KimiDeclarationId.Utf8Writer)]
    public void ManagedLayoutsRejectExtraFieldsAndDestruction(KimiDeclarationId id)
    {
        foreach (var added in new[] { "let extra: i32 = 0", "deinit => ()" })
        {
            var c = Compilation.CreateForTest();
            var type = (StructKoto)c.Library.GetSymbol(id)!.Declaration;
            c.Library.Kotonoha.CreateCodeContext().Parse(type, added);
            Assert.False(c.Bind().IsComplete);
            Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(id));
        }
    }

    private static string Describe(Compilation c)
        => MinimalEmissionTest.Describe(c, null) + "\n" + string.Join("\n", c.Library.Kotonoha.DiagnosticCollection.GetArray().Select(x => x.ToString())) +
            "\n" + string.Join("\n", c.Binding.Issues.Select(x => x.Code + ": " + x.Node.ToString())) +
            "\nLibrary invalid: " + c.Library.InvalidDeclaration;
}
