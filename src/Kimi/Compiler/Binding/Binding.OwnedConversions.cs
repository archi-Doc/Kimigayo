// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the failure of a common Function conversion whose subject is not proven Owned (SPEC 7.7.4, 15.2.3), a Constraint failure: an
// Error rests on its cause (SPEC 23.1.2), else the record explains the first member of the subject's OwnedOrigins through which a
// non-static Origin enters (an OwnedConversionFact).
// Input: the converted Closure or Function Item, the required Function Type, the use. Output: the use failed, or false when the
// conversion's Owned proof holds or the signature does not match. Must not change the Owned proof or select a conversion.
public sealed partial class Binding
{
    // SPEC 15.2.3, 7.7.4: the explaining member is a capture of a closure with a Shared receiver, or a bound Type argument of an Item,
    // whose signature matches structurally. The first Refuted member (a body-local Origin) wins, else the first Unknown one.
    private bool FailOwnedConversion(BoundType implementation, BoundType required, Koto at, Koto use)
    {
        BoundType? signature;
        FunctionKoto? closure = null;
        IReadOnlyList<BoundCapture>? captures = null;
        if (implementation.Kind == BoundTypeKind.FunctionItem)
        {
            signature = this.FunctionItemSignature(implementation);
        }
        else if (implementation is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { ClosureDefinition: { Receiver: SemanticsKind.Ref } plan } declaration })
        {
            signature = this.ClosureSignature(implementation);
            closure = declaration;
            captures = plan.Captures;
        }
        else
        {
            return false;
        }

        if (signature is null || !ReferenceTypes.StorageMatches(required, signature))
        {
            return false;
        }

        this.failedCause = null;
        var proof = this.ProveOwned(this.EnvironmentTypeG353(implementation), use);
        if (proof == ConstraintProof.Proven)
        {
            return false;
        }

        if (proof == ConstraintProof.Error && this.TakeFailedCause() is { } cause)
        {
            this.CompleteDependent(use, cause);
            return true;
        }

        OwnedConversionFact? unknown = null;
        var count = captures?.Count ?? implementation.Components.Count;
        for (var i = 0; i < count; i++)
        {
            var member = captures is null ? implementation.Components[i]
                : implementation.Components.Count == captures.Count ? implementation.Components[i] : captures[i].Environment.Type;
            if (member is null || this.ProveOwned(member, use) == ConstraintProof.Proven)
            {
                continue;
            }

            var origin = this.FirstUnownedOrigin(member, use, 0);
            var name = captures is null ? $"{Ordinal(i + 1)} Type argument" : captures[i].Environment.Name;

            // A captured binding initialized by a Borrow shows that Borrow (SPEC 15.6.5).
            var borrow = captures is not null && origin is not null && captures[i].Source.Declaration is VariableKoto { InitializerKoto: { } initializer } &&
                KotoHelper.UnwrapParentheses(initializer) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } source && ReferenceEquals(source.BoundType?.Origin, origin) ? source : null;
            var local = origin is not null && BodyLocalIn(origin, this.formingBody);
            var fact = new OwnedConversionFact(at, implementation, name, member, origin, closure is null ? null : CaptureEntryOf(closure, name), closure, borrow, local);
            if (fact.Refuted)
            {
                unknown = fact;
                break;
            }

            unknown ??= fact;
        }

        if (unknown is not { } failure)
        {
            return false;
        }

        this.FailExplained(ref this.ownedConversions, use, failure.Refuted ? BindingFailure.UnsatisfiedConstraint : BindingFailure.UnprovenConstraint, failure);
        return true;

        static string Ordinal(int n) => n + (n % 100 is >= 11 and <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
    }

    // The first Origin in a Type's OwnedOrigins, in the order of SPEC 15.2.3, that is not proven static; a common Function Type
    // contributes no per-call Origin (SPEC 15.2.3).
    private BoundOrigin? FirstUnownedOrigin(BoundType type, Koto use, int depth)
    {
        if (depth > 32)
        {
            return null;
        }

        if (OwnedOrigin(type) is { } origin && !this.ProvesOriginOutlives(origin, BoundOrigin.Static, use))
        {
            return origin;
        }

        for (var i = 0; i < type.Components.Count && type.Kind != BoundTypeKind.Function; i++)
        {
            if (this.FirstUnownedOrigin(type.Components[i], use, depth + 1) is { } inner)
            {
                return inner;
            }
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (type.OriginArguments[i] is { } argument && !this.ProvesOriginOutlives(argument, BoundOrigin.Static, use))
            {
                return argument;
            }
        }

        return null;
    }
}

// PROTOTYPE G353: SPEC 7.7.4's complete environment.
public sealed partial class Binding
{
    internal BoundType EnvironmentTypeG353(BoundType callable)
        => callable.Kind == BoundTypeKind.Closure ? this.types.Tuple((BoundType[])callable.Components) : callable;
}

// PROTOTYPE G353 rev 1: whether a Type holds a Closure whose value signature names an enclosing container's Origin.
public sealed partial class Binding
{
    internal bool HoldsContainerOriginG353(BoundType type)
    {
        if (type.Kind == BoundTypeKind.Closure && this.ClosureSignature(type) is { } signature && Names(signature))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (type.Components[i].ContainsClosure && this.HoldsContainerOriginG353(type.Components[i]))
            {
                return true;
            }
        }

        return false;

        static bool Names(BoundType type)
        {
            if (Container(OwnedOrigin(type)))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (Container(type.OriginArguments[i]))
                {
                    return true;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (Names(type.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        static bool Container(BoundOrigin? origin)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (Container(origin.Operands[i]))
                {
                    return true;
                }
            }

            return origin.Binder is DeclarationContainerKoto;
        }
    }
}
