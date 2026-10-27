// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// A value call's result independent of its call receiver (SPEC 15.6.4, 15.8.2; DECISIONS R4-M126 §2.13).
// - Responsibility: ResultFixedForCall, whether a value call's result that names none of the callee's per-call inputs is fixed for the
//   call: each Origin static, carried only by Copy captures of a Copy result, or, carried by no capture, a fixed Origin of a function
//   enclosing the call (FixedInBody, FixedInBodyOrigin); a result it does not fix depends on the call receiver.
// - Inputs: the result Type, the callee expression (its Closure Type and BoundClosure's captures) and the call.
// - Outputs: the answer, read by BindValueCall's receiver-dependence condition (R1's).
// - Invariants: a meet is fixed when each operand is; a Non-Copy carrier is never fixed.
// - Prohibitions: no record, failure or substitution.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A result whose Origins are static or fixed Origins of a function that encloses the call: the signature's own written contract,
    // which the callee's body was checked against.
    private static bool FixedInBody(BoundType type, Koto use)
    {
        if (!FixedOrigin(type.Origin, use))
        {
            return false;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!FixedOrigin(type.OriginArguments[i], use))
            {
                return false;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!FixedInBody(type.Components[i], use))
            {
                return false;
            }
        }

        return true;

        static bool FixedOrigin(BoundOrigin? origin, Koto use)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!FixedOrigin(origin.Operands[i], use))
                    {
                        return false;
                    }
                }

                return true;
            }

            return FixedInBodyOrigin(origin, use);
        }
    }

    // A fixed Origin of a named function that encloses the use: its input or a parameter Origin of its signature.
    private static bool FixedInBodyOrigin(BoundOrigin origin, Koto use)
        => IsLocalRegion(origin) || (origin.Kind is OriginKind.Projection or OriginKind.Anchor && origin.Binder is not FunctionKoto) ||
            (origin.Kind is OriginKind.Input or OriginKind.Parameter && origin.Binder is FunctionKoto { IsAnonymous: false } owner && IsWithin(use, owner));

    // SPEC 15.6.4, 15.8.2: a value call whose result names none of the callee's per-call inputs keeps that result as written when
    // it is independent of the call: each Origin is static, carried only by Copy captures of a Copy result (a Copy of a captured
    // shared reference keeps its external Origin), or, carried by no capture, a fixed Origin of a function enclosing the call. A Non-Copy
    // carrier would make the result depend on the call receiver, and equal lifetime names never prove which capture supplied the result.
    private bool ResultFixedForCall(BoundType result, Koto receiver, Koto use)
    {
        var type = receiver.BoundType!;
        type = type.Kind == BoundTypeKind.Semantics ? type.Components[0] : type;
        if (type.Kind != BoundTypeKind.Closure)
        {
            return FixedInBody(result, use);
        }

        if (type.Symbol?.Declaration is not FunctionKoto { BoundClosure: { } closure })
        {
            return false;
        }

        var copy = this.ProveCopy(result, receiver) == ConstraintProof.Proven;
        return FixedType(result);

        bool FixedType(BoundType part)
        {
            if (!FixedOrigin(part.Origin))
            {
                return false;
            }

            for (var i = 0; i < part.OriginArguments.Count; i++)
            {
                if (!FixedOrigin(part.OriginArguments[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < part.Components.Count; i++)
            {
                if (!FixedType(part.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        bool FixedOrigin(BoundOrigin? origin)
        {
            if (origin is null || origin.Kind == OriginKind.Static)
            {
                return true;
            }

            if (origin.Kind == OriginKind.Intersection)
            {
                for (var i = 0; i < origin.Operands.Count; i++)
                {
                    if (!FixedOrigin(origin.Operands[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            var carried = false;
            for (var i = 0; i < closure.Captures.Count; i++)
            {
                var captured = closure.Captures[i].Environment.Type!;
                if (ContainsOrigin(captured, origin))
                {
                    if (this.ProveCopy(captured, receiver) != ConstraintProof.Proven)
                    {
                        return false;
                    }

                    carried = true;
                }
            }

            return carried ? copy : FixedInBodyOrigin(origin, use);
        }
    }
}
