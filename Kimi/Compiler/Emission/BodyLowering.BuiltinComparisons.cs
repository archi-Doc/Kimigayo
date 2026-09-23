// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerBuiltinComparison(OwnershipBody body, EmissionFunction function, BoundCall call, int id, out string? failure)
    {
        failure = null;
        var equality = call.Target.CompilerFunction == CompilerFunctionKind.BuiltinEquals;
        var contract = equality ? KimiDeclarationId.Equatable : KimiDeclarationId.Comparable;
        if (SignatureType(this, call.ConformingType) is not { } self || !ComparisonTypes.IsBuiltin(self, contract) ||
            WindowsLowering.GetValue(self) is not { } representation ||
            !ReferenceEquals(ValueType(body, id), equality ? BoundType.Boolean : BoundType.I32) || body.Values[id].Kind != OwnershipValueKind.Call)
        {
            return Fail("Builtin comparison needs its concrete Contract witness and scalar result.", out failure);
        }

        for (var i = 0; i < 2; i++)
        {
            var entry = this.parameterArguments[i];
            if (ValueType(body, entry) is not { Semantics: SemanticsKind.Ref, Components.Count: 1 } reference ||
                !ReferenceTypes.StorageMatches(reference.Components[0], self))
            {
                return Fail("Builtin comparison requires two shared borrows of its concrete Self.", out failure);
            }
        }

        function.AddScalar(EmissionOpcode.BuiltinComparison, id, [this.PhysicalOperand(body, this.parameterArguments[0]), this.PhysicalOperand(body, this.parameterArguments[1])], representation.Layout.StorageType, equality ? "equals" : ScalarTypes.Signed(self) ? "s" : "u", representation: representation);
        return true;
    }
}
