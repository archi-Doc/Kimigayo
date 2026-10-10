// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    // The runtime ABI offsets of the named Utf8Writer Fields; a static table, because materializing a constant span's field
    // handle on each adapter validation allocates a RuntimeFieldInfoStub on this runtime.
    private static readonly (string Name, int Offset)[] FormattingWriterOffsets =
        [("destination", 0), ("dispatch", 8), ("kind", 16), ("failed", 56), ("hint", 24), ("pending", 32), ("pendingLength", 40), ("logicalLength", 48)];

    internal static int BuiltinFormatKind(BoundType type)
    {
        var width = ScalarTypes.Width(type);
        if (width != 0)
        {
            return width switch { 8 => 0, 16 => 2, 32 => 4, 64 => 6, _ => 8 } + (ScalarTypes.Signed(type) ? 1 : 2);
        }

        return ReferenceEquals(type, BoundType.F32) ? 11 : ReferenceEquals(type, BoundType.F64) ? 12 : ReferenceEquals(type, BoundType.Char) ? 13 : ReferenceEquals(type, BoundType.Boolean) ? 14 : ReferenceEquals(type, BoundType.Unit) ? 15 :
            ReferenceEquals(type, BoundType.String) ? 16 : FormattingTypes.IsUtf8Slice(type) ? 17 : -1;
    }

    internal bool PrepareWriterDispatch(CallPlan call, EmissionFunction function, BoundType result, out long kind, out EmissionOperand dispatch, out string? failure)
    {
        kind = -1;
        dispatch = new(EmissionOperandKind.NullAddress, 0);
        failure = null;
        if (call.ArgumentOperations.Length != 1 || this.Resolve(call.ArgumentOperations[0].ParameterType, InterpretationContext.Root) is not { Components.Count: 1 } input ||
            input.Semantics != SemanticsKind.Uniq || result.Symbol?.LibraryDeclaration != KimiDeclarationId.Utf8Writer ||
            this.aggregateLayouts.Get(result) is not { Value.Layout.Size: 64, Fields.Length: 9 } layout || layout.Fields[StructStorage.IndexOf(result, "loan")].Layout.Size != 0)
        {
            return Fail("Writer erasure requires a concrete exclusive input and its verified adapter layout.", out failure);
        }

        foreach (var (name, offset) in FormattingWriterOffsets)
        {
            if (layout.Offset(result, name) != offset)
            {
                return Fail("Writer fields do not match the erased runtime layout.", out failure);
            }
        }

        var destination = input.Components[0];
        kind = destination.Symbol?.LibraryDeclaration switch
        {
            KimiDeclarationId.FixedBuffer => 0,
            KimiDeclarationId.HeapBuffer => 1,
            _ => 2,
        };
        if (kind != 2)
        {
            return true;
        }

        if (this.FormattingCalls?.GetValueOrDefault(call) is not { Result: "void", NoReturn: false, ResultSlot: true, Parameters.Length: 3 } abi ||
            abi.Parameters[0].Kind != AbiParameterKind.ResultSlot || abi.Parameters[1].Type != "ptr" || abi.Parameters[2].Type != "i64")
        {
            return Fail("User Writer erasure requires the selected, effect-checked reserve implementation ABI.", out failure);
        }

        var index = function.FunctionAddresses.IndexOf(abi);
        if (index < 0)
        {
            index = function.FunctionAddresses.Count;
            function.FunctionAddresses.Add(abi);
        }

        dispatch = new(EmissionOperandKind.FunctionAddress, index);
        return true;
    }

    private int BuiltinFormatKind(CallPlan call)
    {
        var logical = call.Target.CompilerFunction == CompilerFunctionKind.WriterWrite ? 1 : 0;
        var required = call.Receiver is not null && call.ReceiverOperation.ParameterIndex == logical ? call.ReceiverOperation.ParameterType : null;
        for (var i = 0; required is null && i < call.ArgumentOperations.Length; i++)
        {
            if (call.ArgumentOperations[i].ParameterIndex == logical)
            {
                required = call.ArgumentOperations[i].ParameterType;
            }
        }

        if (this.Resolve(required, InterpretationContext.Root) is not { Semantics: SemanticsKind.Ref, Components.Count: 1 } reference)
        {
            return -1;
        }

        return BuiltinFormatKind(reference.Components[0]);
    }
}
