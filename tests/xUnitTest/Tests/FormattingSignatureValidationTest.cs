// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FormattingSignatureValidationTest
{
    public static TheoryData<KimiDeclarationId> Operations
    {
        get
        {
            var data = new TheoryData<KimiDeclarationId> { KimiDeclarationId.Utf8Format, KimiDeclarationId.BufferWriter };
            for (var id = KimiDeclarationId.TextFixed; id <= KimiDeclarationId.WriteLineUtf8; id++)
            {
                data.Add(id);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryOperandRequiresItsCompleteBoundType(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var function = Function(symbol);
        for (var index = -1; index < function.Parameters.Count; index++)
        {
            var original = index < 0 ? function.BoundSymbol!.Type! : function.Parameters[index].Type.BoundType!;
            Set(function, index, BoundType.Boolean);
            Assert.False(c.Library.ValidateBoundDeclarations());
            Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
            Set(function, index, original);
            Assert.True(c.Library.ValidateDeclarations());
            Assert.True(c.Library.ValidateBoundDeclarations());
        }
    }

    [Theory]
    [InlineData(KimiDeclarationId.TextFixed, 0, "mode")]
    [InlineData(KimiDeclarationId.TextFixed, 0, "length")]
    [InlineData(KimiDeclarationId.TextFixed, -1, "static")]
    [InlineData(KimiDeclarationId.TextWriter, 0, "mode")]
    [InlineData(KimiDeclarationId.TextUtf8, -1, "static")]
    [InlineData(KimiDeclarationId.TextValidateUtf8, -1, "static")]
    [InlineData(KimiDeclarationId.TextToString, 0, "mode")]
    [InlineData(KimiDeclarationId.TextTryFormat, -1, "input0")]
    [InlineData(KimiDeclarationId.FixedBufferBytes, 0, "mode")]
    [InlineData(KimiDeclarationId.FixedBufferBytes, -1, "static")]
    [InlineData(KimiDeclarationId.FixedBufferText, -1, "static")]
    [InlineData(KimiDeclarationId.FixedBufferReserve, 0, "mode")]
    [InlineData(KimiDeclarationId.FixedBufferReserve, -1, "static")]
    [InlineData(KimiDeclarationId.FixedBufferIntoText, -1, "static")]
    [InlineData(KimiDeclarationId.HeapBufferValidate, 0, "mode")]
    [InlineData(KimiDeclarationId.WindowAppend, 1, "static")]
    [InlineData(KimiDeclarationId.WindowLimit, -1, "static")]
    [InlineData(KimiDeclarationId.WriterWrite, 1, "mode")]
    [InlineData(KimiDeclarationId.WriterStatus, 0, "mode")]
    [InlineData(KimiDeclarationId.WriteLineUtf8, 0, "static")]
    [InlineData(KimiDeclarationId.Utf8Format, 1, "writer-origin")]
    [InlineData(KimiDeclarationId.BufferWriter, 0, "mode")]
    [InlineData(KimiDeclarationId.BufferWriter, -1, "static")]
    public void CapabilitiesAndOriginsAreFixed(KimiDeclarationId id, int index, string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var function = Function(symbol);
        var original = index < 0 ? function.BoundSymbol!.Type! : function.Parameters[index].Type.BoundType!;
        var mutated = mutation switch
        {
            "mode" => Copy(original, semantics: original.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref),
            "length" => Copy(original, components: [Copy(original.Components[0], length: new BoundLength(KotoKind.NumberLiteral, 4, null, null, null))]),
            "static" => ReplaceOrigin(original, BoundOrigin.Static),
            "input0" => ReplaceOrigin(original, function.Parameters[0].Type.BoundType!.Origin!),
            "writer-origin" => Copy(original, components: [Copy(original.Components[0], origins: [original.Origin!])]),
            _ => throw new ArgumentException(mutation),
        };
        Set(function, index, mutated);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
    }

    [Fact]
    public void ContractRequirementIdentityIsFixed()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(KimiDeclarationId.Utf8Format)!;
        symbol.Contract!.RequirementStorage[0] = c.Library.GetSymbol(KimiDeclarationId.BufferWriter)!.Contract!.Requirements[0];
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
    }

    private static FunctionKoto Function(BindingSymbol symbol)
        => symbol.Declaration as FunctionKoto ?? (FunctionKoto)((ContractKoto)symbol.Declaration).Members[0];

    private static void Set(FunctionKoto function, int index, BoundType type)
    {
        if (index < 0)
        {
            function.BoundSymbol!.Type = type;
        }
        else
        {
            function.Parameters[index].Type.BoundType = type;
        }
    }

    // Replaces the Origin carried by a view, a borrow or a dependent Type, looking through a Result's success Type.
    private static BoundType ReplaceOrigin(BoundType type, BoundOrigin origin)
        => type.Kind == BoundTypeKind.Constructed ? Copy(type, components: [ReplaceOrigin(type.Components[0], origin), type.Components[1]]) :
            type.OriginArguments.Count == 1 ? Copy(type, origins: [origin]) : Copy(type, origin: origin);

    private static BoundType Copy(BoundType type, SemanticsKind? semantics = null, BoundType[]? components = null, BoundOrigin? origin = null, BoundOrigin[]? origins = null, BoundLength? length = null)
        => new(type.Name, type.Kind, type.Symbol, semantics ?? type.Semantics, components ?? type.Components.ToArray(), type.Length, origin ?? type.Origin, origins ?? type.OriginArguments.ToArray(), length ?? type.LengthExpression);
}
