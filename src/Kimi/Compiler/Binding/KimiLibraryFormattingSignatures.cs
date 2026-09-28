// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private const sbyte NoOrigin = -1;
    private const sbyte SelfSource = -2;

    // SPEC UTF-8 formatting 1.2-1.5 and 22.4: complete bound signatures of the compiler-implemented
    // formatting operations and Contract requirements. An operand's Origin names the input whose Origin
    // it carries (its own index for an input's own elided Origin) or the Self Type's source parameter.
    private static readonly FormattingSignature[] FormattingSignatures =
    [
        new(KimiDeclarationId.Utf8Format, new(FormattingKind.Unit, Error: FormattingKind.BufferFull), [new(FormattingKind.RefSelf), new(FormattingKind.UniqWriter)]),
        new(KimiDeclarationId.BufferWriter, new(FormattingKind.WriteWindow, 0, FormattingKind.BufferFull), [new(FormattingKind.UniqSelf), new(FormattingKind.ISize)]),
        new(KimiDeclarationId.TextFixed, new(FormattingKind.FixedBuffer, 0), [new(FormattingKind.UniqBytes)]),
        new(KimiDeclarationId.TextHeap, new(FormattingKind.HeapBuffer), [new(FormattingKind.ISize)]),
        new(KimiDeclarationId.TextWriter, new(FormattingKind.Utf8Writer, 0), [new(FormattingKind.UniqDestination)]),
        new(KimiDeclarationId.TextUtf8, new(FormattingKind.Utf8Slice, 0), [new(FormattingKind.RefString)]),
        new(KimiDeclarationId.TextValidateUtf8, new(FormattingKind.Utf8Slice, 0, FormattingKind.InvalidUtf8), [new(FormattingKind.Bytes, 0)]),
        new(KimiDeclarationId.TextToString, new(FormattingKind.String), [new(FormattingKind.RefValue)]),
        new(KimiDeclarationId.TextTryFormat, new(FormattingKind.Utf8Slice, 1, FormattingKind.BufferFull), [new(FormattingKind.RefValue), new(FormattingKind.UniqBytes)]),
        new(KimiDeclarationId.TextRelease, new(FormattingKind.Unit), [new(FormattingKind.RawBytes)]),
        new(KimiDeclarationId.FixedBufferBytes, new(FormattingKind.Bytes, 0), [new(FormattingKind.RefSelf)]),
        new(KimiDeclarationId.FixedBufferText, new(FormattingKind.Utf8Slice, 0, FormattingKind.InvalidUtf8), [new(FormattingKind.RefSelf)]),
        new(KimiDeclarationId.FixedBufferValidate, new(FormattingKind.Unit, Error: FormattingKind.InvalidUtf8), [new(FormattingKind.UniqSelf)]),
        new(KimiDeclarationId.FixedBufferClear, new(FormattingKind.Unit), [new(FormattingKind.UniqSelf)]),
        new(KimiDeclarationId.FixedBufferReserve, new(FormattingKind.WriteWindow, 0, FormattingKind.BufferFull), [new(FormattingKind.UniqSelf), new(FormattingKind.ISize)]),
        new(KimiDeclarationId.FixedBufferIntoText, new(FormattingKind.Utf8Slice, SelfSource, FormattingKind.InvalidUtf8), [new(FormattingKind.Self)]),
        new(KimiDeclarationId.HeapBufferBytes, new(FormattingKind.Bytes, 0), [new(FormattingKind.RefSelf)]),
        new(KimiDeclarationId.HeapBufferText, new(FormattingKind.Utf8Slice, 0, FormattingKind.InvalidUtf8), [new(FormattingKind.RefSelf)]),
        new(KimiDeclarationId.HeapBufferValidate, new(FormattingKind.Unit, Error: FormattingKind.InvalidUtf8), [new(FormattingKind.UniqSelf)]),
        new(KimiDeclarationId.HeapBufferClear, new(FormattingKind.Unit), [new(FormattingKind.UniqSelf)]),
        new(KimiDeclarationId.HeapBufferReserve, new(FormattingKind.WriteWindow, 0, FormattingKind.BufferFull), [new(FormattingKind.UniqSelf), new(FormattingKind.ISize)]),
        new(KimiDeclarationId.HeapBufferIntoString, new(FormattingKind.String, Error: FormattingKind.InvalidUtf8), [new(FormattingKind.Self)]),
        new(KimiDeclarationId.WindowPush, new(FormattingKind.Unit, Error: FormattingKind.BufferFull), [new(FormattingKind.UniqSelf), new(FormattingKind.U8)]),
        new(KimiDeclarationId.WindowAppend, new(FormattingKind.Unit, Error: FormattingKind.BufferFull), [new(FormattingKind.UniqSelf), new(FormattingKind.Bytes, 1)]),
        new(KimiDeclarationId.WindowLimit, new(FormattingKind.Self), [new(FormattingKind.Self), new(FormattingKind.ISize)]),
        new(KimiDeclarationId.WindowCommit, new(FormattingKind.ISize), [new(FormattingKind.Self)]),
        new(KimiDeclarationId.WriterWrite, new(FormattingKind.Unit, Error: FormattingKind.BufferFull), [new(FormattingKind.UniqSelf), new(FormattingKind.RefValue)]),
        new(KimiDeclarationId.WriterStatus, new(FormattingKind.Unit, Error: FormattingKind.BufferFull), [new(FormattingKind.RefSelf)]),
        new(KimiDeclarationId.WriteLineUtf8, new(FormattingKind.Unit), [new(FormattingKind.Utf8Slice, 0)]),
    ];

    private enum FormattingKind : byte
    {
        None,
        Unit,
        ISize,
        U8,
        String,
        RawBytes,
        Self,
        RefSelf,
        UniqSelf,
        RefString,
        RefValue,
        UniqDestination,
        UniqBytes,
        UniqWriter,
        Bytes,
        FixedBuffer,
        HeapBuffer,
        Utf8Writer,
        Utf8Slice,
        WriteWindow,
        BufferFull,
        InvalidUtf8,
    }

    private static BoundOrigin? FormattingInputOrigin(FunctionKoto function, int input)
        => function.Parameters[input].Type.BoundType?.Origin;

    private static bool FormattingOwnOrigin(BoundOrigin? origin, FunctionKoto function, int input)
        => origin is { Kind: OriginKind.Input } && origin.InputIndex == input && ReferenceEquals(origin.Binder, function);

    private static bool FormattingInputBorrow(BoundType type, SemanticsKind semantics, FunctionKoto function, int input, out BoundType referent)
    {
        referent = type.Components.Count == 1 ? type.Components[0] : type;
        return type is { Kind: BoundTypeKind.Semantics, Symbol: null, OriginArguments.Count: 0, Components.Count: 1 } && type.Semantics == semantics &&
            FormattingOwnOrigin(type.Origin, function, input);
    }

    // Self of a member or requirement is its owner applied to the owner's own Origin parameters.
    private static bool FormattingSelf(BoundType type, BindingSymbol? owner)
    {
        if (owner is null || type is not { Kind: BoundTypeKind.Nominal, Semantics: SemanticsKind.Owner, Origin: null, Components.Count: 0 } || !ReferenceEquals(type.Symbol, owner))
        {
            return false;
        }

        var origins = owner.Schema?.Origins;
        if (type.OriginArguments.Count != (origins?.Count ?? 0))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!ReferenceEquals(type.OriginArguments[i], origins![i].Origin))
            {
                return false;
            }
        }

        return true;
    }

    private static KimiDeclarationId FormattingDeclaration(FormattingKind kind)
        => kind switch
        {
            FormattingKind.FixedBuffer => KimiDeclarationId.FixedBuffer,
            FormattingKind.HeapBuffer => KimiDeclarationId.HeapBuffer,
            FormattingKind.Utf8Writer => KimiDeclarationId.Utf8Writer,
            FormattingKind.Utf8Slice => KimiDeclarationId.Utf8Slice,
            FormattingKind.WriteWindow => KimiDeclarationId.WriteWindow,
            FormattingKind.BufferFull => KimiDeclarationId.BufferFull,
            FormattingKind.InvalidUtf8 => KimiDeclarationId.InvalidUtf8,
            _ => throw new InvalidOperationException(),
        };

    private bool ValidBoundFormattingSignature(BindingSymbol symbol, KimiDeclarationId id, KimiLibraryContainer container)
    {
        foreach (var signature in FormattingSignatures)
        {
            if (signature.Id != id)
            {
                continue;
            }

            FunctionKoto function;
            BindingSymbol? self;
            BoundType? result;
            if (symbol.Declaration is ContractKoto { Members: [FunctionKoto requirement] })
            {
                function = requirement;
                self = symbol;
                result = requirement.BoundSymbol?.Type;
                if (symbol.Contract is not { Requirements: [var bound] } || !ReferenceEquals(bound, requirement.BoundSymbol))
                {
                    return false;
                }
            }
            else if (symbol.Declaration is FunctionKoto declaration)
            {
                function = declaration;
                self = container switch
                {
                    KimiLibraryContainer.FixedBuffer => this.GetSymbol(KimiDeclarationId.FixedBuffer),
                    KimiLibraryContainer.HeapBuffer => this.GetSymbol(KimiDeclarationId.HeapBuffer),
                    KimiLibraryContainer.WriteWindow => this.GetSymbol(KimiDeclarationId.WriteWindow),
                    KimiLibraryContainer.Utf8Writer => this.GetSymbol(KimiDeclarationId.Utf8Writer),
                    _ => null,
                };
                result = symbol.Type;
            }
            else
            {
                return false;
            }

            if (function.Parameters.Count != signature.Inputs.Length || !this.BoundFormattingOperand(result, signature.Result, function, self, -1))
            {
                return false;
            }

            for (var i = 0; i < signature.Inputs.Length; i++)
            {
                if (!this.BoundFormattingOperand(function.Parameters[i].Type.BoundType, signature.Inputs[i], function, self, i))
                {
                    return false;
                }
            }

            return true;
        }

        return true; // Types and other catalog families have their own checks.
    }

    private bool BoundFormattingOperand(BoundType? type, FormattingOperand expected, FunctionKoto function, BindingSymbol? self, int input)
    {
        if (type is null)
        {
            return false;
        }

        if (expected.Error != FormattingKind.None)
        {
            return type is { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var success, var error] } &&
                ReferenceEquals(type.Symbol, this.GetSymbol(KimiDeclarationId.Result)) && this.BoundFormattingOperand(success, expected with { Error = FormattingKind.None }, function, self, input) &&
                this.BoundFormattingOperand(error, new(expected.Error), function, self, input);
        }

        // The Origin an origin-bearing operand carries: its own input Origin, an earlier input's or Self's source.
        var origin = expected.Origin == SelfSource ? (self?.Schema?.Origins is [var source] ? source.Origin : null) :
            expected.Origin >= 0 && expected.Origin != input ? FormattingInputOrigin(function, expected.Origin) : null;
        bool Carries(BoundOrigin? actual) => expected.Origin == input ? FormattingOwnOrigin(actual, function, input) : origin is not null && ReferenceEquals(actual, origin);
        var u8 = BoundType.Primitives["u8"];
        BoundType referent;
        return expected.Kind switch
        {
            FormattingKind.Unit => ReferenceEquals(type, BoundType.Unit),
            FormattingKind.ISize => ReferenceEquals(type, BoundType.ISize),
            FormattingKind.U8 => ReferenceEquals(type, u8),
            FormattingKind.String => ReferenceEquals(type, BoundType.String),
            FormattingKind.RawBytes => BoundStoragePointer(type, u8),
            FormattingKind.Self => FormattingSelf(type, self),
            FormattingKind.RefSelf => FormattingInputBorrow(type, SemanticsKind.Ref, function, input, out referent) && FormattingSelf(referent, self),
            FormattingKind.UniqSelf => FormattingInputBorrow(type, SemanticsKind.Uniq, function, input, out referent) && FormattingSelf(referent, self),
            FormattingKind.RefString => FormattingInputBorrow(type, SemanticsKind.Ref, function, input, out referent) && ReferenceEquals(referent, BoundType.String),
            FormattingKind.RefValue or FormattingKind.UniqDestination => FormattingInputBorrow(type, expected.Kind == FormattingKind.RefValue ? SemanticsKind.Ref : SemanticsKind.Uniq, function, input, out referent) &&
                function.GenericArguments.Count != 0 && referent is { Kind: BoundTypeKind.Parameter } && ReferenceEquals(referent, function.GenericArguments[0].BoundType),
            FormattingKind.UniqBytes => FormattingInputBorrow(type, SemanticsKind.Uniq, function, input, out referent) &&
                referent is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var element], LengthExpression: { Parameter: { } length, Left: null, Right: null } } &&
                ReferenceEquals(element, u8) && function.GenericArguments.Count != 0 && ReferenceEquals(length, function.GenericArguments[^1].BoundSymbol),
            FormattingKind.UniqWriter => FormattingInputBorrow(type, SemanticsKind.Uniq, function, input, out referent) && this.FormattingNamed(referent, KimiDeclarationId.Utf8Writer) &&
                referent.OriginArguments is [var target] && FormattingOwnOrigin(target, function, input) && !ReferenceEquals(target, type.Origin),
            FormattingKind.Bytes => type is { Kind: BoundTypeKind.Slice, Semantics: SemanticsKind.Owner, OriginArguments.Count: 0, Components: [var slice] } &&
                ReferenceEquals(type.Symbol, this.Slice) && ReferenceEquals(slice, u8) && Carries(type.Origin),
            FormattingKind.FixedBuffer or FormattingKind.Utf8Writer or FormattingKind.Utf8Slice or FormattingKind.WriteWindow =>
                this.FormattingNamed(type, FormattingDeclaration(expected.Kind)) && type.OriginArguments is [var carried] && Carries(carried),
            FormattingKind.HeapBuffer or FormattingKind.BufferFull or FormattingKind.InvalidUtf8 =>
                this.FormattingNamed(type, FormattingDeclaration(expected.Kind)) && type.OriginArguments.Count == 0 && ReferenceEquals(type, this.GetSymbol(FormattingDeclaration(expected.Kind))?.Type),
            _ => false,
        };
    }

    private bool FormattingNamed(BoundType type, KimiDeclarationId id)
        => type is { Kind: BoundTypeKind.Nominal, Semantics: SemanticsKind.Owner, Origin: null, Components.Count: 0 } && ReferenceEquals(type.Symbol, this.GetSymbol(id));

    private readonly record struct FormattingOperand(FormattingKind Kind, sbyte Origin = NoOrigin, FormattingKind Error = FormattingKind.None);

    private readonly record struct FormattingSignature(KimiDeclarationId Id, FormattingOperand Result, FormattingOperand[] Inputs);
}
