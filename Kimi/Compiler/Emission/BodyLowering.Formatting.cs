// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool PrepareWriterDispatch(KimiLibrary library, BoundCall call, EmissionFunction function, BoundType result, out long kind, out EmissionOperand dispatch, out string? failure)
    {
        kind = -1;
        dispatch = new(EmissionOperandKind.NullAddress, 0);
        failure = null;
        if (call.ArgumentOperations.Length != 1 || SignatureType(this, call.ArgumentOperations[0].ParameterType) is not { Components.Count: 1 } input ||
            input.Semantics != SemanticsKind.Uniq || result.Symbol?.LibraryDeclaration != KimiDeclarationId.Utf8Writer ||
            this.aggregateLayouts.Get(result) is not { Value.Layout.Size: 64, Fields.Length: 8 } layout)
        {
            return Fail("Writer erasure requires a concrete exclusive input and its verified adapter layout.", out failure);
        }

        ReadOnlySpan<int> offsets = [0, 8, 16, 56, 24, 32, 40, 48];
        for (var i = 0; i < offsets.Length; i++)
        {
            if (layout.Offset(i) != offsets[i])
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

        var binding = call.Target.Declaration.CodeContext.Compilation.Binding;
        if (library.GetSymbol(KimiDeclarationId.BufferWriter) is not { } contract ||
            binding.ResolveConformance(destination, contract, call.Target.Declaration, out var path) != ConstraintProof.Proven ||
            path is not { IsVerified: true, Witnesses.Count: 1 } ||
            path.Witnesses[0] is not { Implementation.Declaration: FunctionKoto implementation, Function.BasePath: null } ||
            this.functions!.GetValueOrDefault(implementation) is not { Result: "void", NoReturn: false, ResultSlot: true, Parameters.Length: 3 } abi ||
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
}
