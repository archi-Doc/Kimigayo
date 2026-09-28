// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private static readonly FunctionAbi TestObserve = new("__kimi_test_observe", "i64", [new("i1", "condition", AbiParameterKind.Value), new("i32", "site", AbiParameterKind.Value)]);
    private static readonly FunctionAbi TestMessage = new("__kimi_test_message", "void", [new("i64", "issue", AbiParameterKind.Value), new("ptr", "text", AbiParameterKind.OwnedSlot)]);
    private static readonly FunctionAbi TestAbort = new("__kimi_test_require_abort", "void", [new("i64", "issue", AbiParameterKind.Value), new("i32", "site", AbiParameterKind.Value)], noReturn: true);

    private bool LowerVerification(OwnershipBody body, EmissionFunction function, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        if (operation.Source is not TestVerificationKoto { SiteId: >= 0 } node || !TestDefinition.IsIncluded(node) || !TestDefinition.IsTestOnly(node))
        {
            return Fail("Verification requires a verified test site.", out failure);
        }

        switch (operation.Kind)
        {
            case OwnershipOperationKind.TestObserve:
                if (body.Values[id].Count != 1)
                {
                    return !body.IsReachable(id) || Fail("Verification lacks a secured condition.", out failure);
                }

                function.AddCall(id, TestObserve, [Operand(body, Input(body, id, 0)), new(EmissionOperandKind.Integer, node.SiteId)]);
                var condition = Definition(body, Input(body, id, 0));
                if (body.Values[condition] is { Kind: OwnershipValueKind.Binary, Operator: KotoKind.EqualsEquals or KotoKind.ExclamationEquals or KotoKind.LessThan or KotoKind.LessThanEquals or KotoKind.GreaterThan or KotoKind.GreaterThanEquals })
                {
                    for (var side = 0; side < 2; side++)
                    {
                        var input = Input(body, condition, side);
                        var type = ValueType(body, input);
                        if (type is not null && IsScalar(type) && WindowsLowering.GetValue(type) is { } representation)
                        {
                            var kind = ReferenceEquals(type, BoundType.Boolean) ? 1 : FloatingTypes.Supports(type) ? 4 : ScalarTypes.Signed(type) ? 2 : 3;
                            function.AddScalar(EmissionOpcode.TestSnapshot, id, [Operand(body, input)], representation.ComputationType, place: side, location: kind, representation: representation);
                        }
                    }
                }

                break;
            case OwnershipOperationKind.TestMessage:
                if (operation.Place < 0)
                {
                    return !body.IsReachable(id) || Fail("Verification message has no acquired value.", out failure);
                }

                function.AddCall(id, TestMessage, [new(EmissionOperandKind.Value, operation.Input), new(EmissionOperandKind.SlotAddress, operation.Place)]);
                break;
            case OwnershipOperationKind.TestAbort:
                function.AddCall(id, TestAbort, [new(EmissionOperandKind.Value, operation.Input), new(EmissionOperandKind.Integer, node.SiteId)]);
                function.Add(EmissionOpcode.Unreachable, id);
                break;
        }

        return true;
    }
}
