// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// Signatures in the Type tier (SPEC 7.3, 8.2).
// - Responsibility: a Call's arguments and result against its callee's signature (Signature): a Fn, Requirement or Virtual callee's
//   function under its Subst, the receiver first, a default template's shared references to the parameters before its own; a Value
//   callee's Function Type, else its declared signature's arity; and the Type a body gives in _0 at a Return (Result).
// - Inputs: a Call whose arguments are in range, a Value callee's TypeToken constant in range (Call); a body's key.
// - Outputs: Arity and Type findings.
// - Invariants: a diverging Call writes no destination, whose Type is not checked.
// - Prohibitions: no callee kind or intrinsic rule (MirValidator.Calls); Types agree only through Agree (MirValidator.Types).
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // The Type a body gives in _0: its function's result (a constructor's object), a default template's parameter's or a static's; none
        // for the top-level statements, which have no symbol.
        private static BoundType? Result(MirDefKey key)
        {
            if (key.Decl is not { } decl)
            {
                return null;
            }

            if (key.Kind == MirBodyKind.StaticInit)
            {
                return decl.Type;
            }

            return TyCtxt.Signature(decl, null, key.Kind == MirBodyKind.DefaultTemplate ? key.Index : -1, out _);
        }

        // SPEC 7.3: a Fn, Requirement or Virtual callee takes its function's parameters under its Subst, the receiver first (a default
        // template, plan 5.6.2: shared references to the parameters before its own, in slot order), and gives its result; a Value callee of a
        // Function Type its inputs and result. A call that diverges writes no destination.
        private void Signature(in MirTerminator call)
        {
            var function = call.Callee.Symbol;
            var subst = call.Callee.Subst;
            var template = call.Callee.Kind == MirCalleeKind.Fn ? call.Callee.Arg - 1 : -1;
            var value = function is null ? Terminal(this.TypeOf(call.Operand)) : null;
            if (function is null && value is not { Kind: BoundTypeKind.Function, Components: [_, _] })
            {
                // A Closure's or a function item's signature is its declaration's, whose inputs give its arity (C8; retention numbers them).
                if (body.Consts[call.Callee.Arg].Type is not { Components: [var inputs, _] } || inputs.Components.Count != call.Count)
                {
                    this.Report(MirViolation.Arity);
                }

                return;
            }

            var count = 0;
            var result = function is null ? value!.Components[1] : TyCtxt.Signature(function, subst, template, out count);
            if (function is null)
            {
                count = value!.Components[0].Components.Count;
            }
            else if (template >= 0)
            {
                count = template;
            }

            if (count != call.Count)
            {
                this.Report(MirViolation.Arity);
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var actual = this.TypeOf(body.Operands[call.Start + i]);
                BoundType? expected;
                if (function is null)
                {
                    expected = value!.Components[0].Components[i];
                }
                else
                {
                    expected = TyCtxt.Signature(function, subst, template < 0 ? MirCallee.Slot(function, i) : i, out _);
                }

                if (template < 0 || actual is null || actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [_] })
                {
                    this.Agree(expected, template < 0 ? actual : actual?.Components[0]);
                }
                else
                {
                    this.Report(MirViolation.Type); // A template's argument is no shared reference.
                }
            }

            if (call.Target >= 0 && this.IsValid(call.Place))
            {
                this.Agree(body.TypeOf(call.Place), result);
            }
        }
    }
}
