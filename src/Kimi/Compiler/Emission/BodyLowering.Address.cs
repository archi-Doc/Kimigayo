// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerAddressOfI64(OwnershipBody body, EmissionFunction function, int id, InvocationKoto call, BoundCall plan, out string? failure)
    {
        failure = null;
        if (plan.Target.Declaration is not FunctionKoto target || plan.Receiver is not null || call.AttributeChain is not null ||
            plan.DefaultArguments.Length != 0 || plan.ArgumentOperations.Length != 1 || call.ArgumentNodes.Count != 1 ||
            plan.ArgumentToParameter.Length != 1 || target.Parameters.Count != 1 ||
            plan.ArgumentOperations[0].ParameterType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [var input] } storage ||
            !ReferenceEquals(input, BoundType.Primitives["i64"]) ||
            plan.ReturnType is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var result] } ||
            !ReferenceEquals(input, result) || !ReferenceEquals(call.BoundType, plan.ReturnType))
        {
            return Fail("Raw i64 address requires its verified exclusive input and pointer result.", out failure);
        }

        if (!this.PrepareCollectionArguments(body, id, call, plan, target, out var complete, out failure))
        {
            return false;
        }

        if (!complete)
        {
            return true;
        }

        if (!this.ScalarArrayArgument(body, id, 0, storage, out var address))
        {
            return Fail("Raw i64 address requires the original initialized slot.", out failure);
        }

        function.AddScalar(EmissionOpcode.BorrowAddress, id, [address]);
        return true;
    }
}
