// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private ArithmeticCheckKind NumericCallCheck(OwnershipBody body, int id)
        => body.Operations[id].Source is InvocationKoto { BoundCall: { ConformingType: { } conforming } call } &&
            ArithmeticContracts.Identity(call.Target.Scope.Owner.BoundSymbol) is { } identity &&
            SignatureType(this, conforming) is { } self && ArithmeticContracts.Supports(self, identity)
            ? NumericArithmetic.Check(ArithmeticContracts.Operator(identity), self) : ArithmeticCheckKind.None;

    private bool LowerBuiltinArithmetic(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, BoundCall call, int id, out string? failure)
    {
        failure = null;
        if (SignatureType(this, call.ConformingType) is not { } self ||
            ArithmeticContracts.Identity(call.Target.Scope.Owner.BoundSymbol) is not { } identity || !ArithmeticContracts.Supports(self, identity) ||
            !ReferenceEquals(ValueType(body, id), self))
        {
            return Fail("Arithmetic witness requires its selected numeric conformance and result Type.", out failure);
        }

        var operation = ArithmeticContracts.Operator(identity);
        if (ScalarTypes.Width(self) == 128 && operation is KotoKind.Slash or KotoKind.Percent)
        {
            return Fail("The initial profile does not supply 128-bit division or remainder.", out failure);
        }

        var unary = operation == KotoKind.PrefixMinus;
        // Extra SSA values are disjoint from ownership operation IDs; one stable pair per call, no runtime storage.
        var start = body.Operations.Count + (id * 2);
        for (var i = 0; i < (unary ? 1 : 2); i++)
        {
            var input = this.parameterArguments[i];
            if (ValueType(body, input) is not { Semantics: SemanticsKind.Ref, Components: [var target] } || !ReferenceTypes.StorageMatches(target, self))
            {
                return Fail("Arithmetic witness inputs must be shared borrows of the selected numeric Type.", out failure);
            }
        }

        var check = NumericArithmetic.Check(operation, self);
        var location = -1;
        if (this.checks[id] != check || (check != ArithmeticCheckKind.None && !this.TryGetLocation(body.Operations[id].Source, directory, constants, out location)))
        {
            return Fail("Arithmetic witness check has no matching continuation or source location.", out failure);
        }

        return NumericArithmetic.EmitBorrowed(function, self, operation, id, start, body.Operations.Count + id, location, this.PhysicalOperand(body, this.parameterArguments[0]), unary ? default : this.PhysicalOperand(body, this.parameterArguments[1]));
    }
}
