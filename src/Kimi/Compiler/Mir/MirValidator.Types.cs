// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// Shared Type agreement and Call signatures (SPEC 3.2-3.4, 7.3, 8.9): Types agree modulo Origins or as the body's premises make them,
// and every Semantics case view resolves each Adapt. Per-rvalue operand and Type rules live in MirValidator.Rvalues.cs.
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // SPEC 8.14, 8.15: under each Semantics case view an Adapt selects an operation (MirCases.Adapt), and a borrow or an address takes
        // a Place of its reference's referent Type there; one finding per Adapt, at the first view that breaks either.
        internal void Views(MirCases cases)
        {
            var views = MirCases.Count(body);
            foreach (var (adapt, source) in adapts)
            {
                this.at = source;
                for (var view = 0L; view < views; view++)
                {
                    cases.Select(body, view);
                    var resolved = cases.Adapt(adapt, out var operation);
                    var borrowed = operation is ConversionBinding.Borrow or ConversionBinding.Address && body.TypeOf(resolved.Place) is { } stored
                        ? cases.Type(stored) : null;
                    if (operation == ConversionBinding.None || (borrowed is not null && !this.Agrees(resolved.Type!.Components[0], borrowed)))
                    {
                        this.Report(MirViolation.Operation);
                        break;
                    }
                }
            }
        }

        // A Type below its reference and pair layers (TyCtxt.Layer), which a comparison compares (SPEC 13.4).
        private static BoundType? Terminal(BoundType? type)
            => TyCtxt.Layer(type, out var below, out _) is var _ && !ReferenceEquals(below, type) ? Terminal(below) : type;

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

        // An operand's Type, unknown (null) for a Place out of range or a local an Opaque value defines, and for the Never value, which
        // fits any operand position (SPEC 3.8; DECISIONS R4-M62).
        private readonly BoundType? TypeOf(in MirOperand operand)
        {
            if (operand.Kind == MirOperandKind.Const)
            {
                return (uint)operand.Arg < (uint)body.Consts.Length ? body.Consts[operand.Arg].Type : null;
            }

            var type = this.IsValid(operand.Place) && !(operand.Place.Root >= 0 && unknown[operand.Place.Root]) ? body.TypeOf(operand.Place) : null;
            return ReferenceEquals(type, BoundType.Never) ? null : type;
        }

        // SPEC 8.2: Types agree (MirTypes.Agree) as they are, or as the body's premises make them (TyCtxt.Premised): E and S under `E is S`.
        private readonly bool Agrees(BoundType? expected, BoundType? actual)
            => MirTypes.Agree(expected, actual) || (body.Key.Decl is { } definition && MirTypes.Agree(
                TyCtxt.Premised(definition, expected!),
                TyCtxt.Premised(definition, actual!)));

        private readonly void Agree(BoundType? expected, BoundType? actual)
        {
            if (!this.Agrees(expected, actual))
            {
                this.Report(MirViolation.Type);
            }
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
