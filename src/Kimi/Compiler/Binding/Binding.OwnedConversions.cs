// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Owns the explanation of a common Function conversion whose subject is not proven Owned (SPEC 7.7.4, 15.2.3): the first member of the
// subject's OwnedOrigins through which a non-static Origin enters, which RecordMismatch records as the Constraint failure's fact.
// Input: the converted Closure or Function Item, the required Function Type, the use. Output: an OwnedConversionFact, or none when the
// conversion's Owned proof holds or the signature does not match. Must not change the Owned proof or select a conversion.
public sealed partial class Binding
{
    // SPEC 15.2.3, 7.7.4: the first member of a conversion subject's OwnedOrigins through which a non-static Origin enters: a capture of a
    // closure with a Shared receiver, or a bound Type argument of an Item, whose signature matches structurally. The first Refuted member
    // (a body-local Origin) wins, else the first Unknown one; the Owned proof itself stays unchanged.
    private OwnedConversionFact? OwnedConversionFailure(BoundType implementation, BoundType required, Koto at, Koto use)
    {
        BoundType? signature;
        FunctionKoto? closure = null;
        IReadOnlyList<BoundCapture>? captures = null;
        if (implementation.Kind == BoundTypeKind.FunctionItem)
        {
            signature = this.FunctionItemSignature(implementation);
        }
        else if (implementation is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto { ClosureStorage: { Receiver: SemanticsKind.Ref, Signature: not null } plan } declaration })
        {
            signature = this.ClosureSignature(implementation);
            closure = declaration;
            captures = plan.Captures;
        }
        else
        {
            return null;
        }

        if (signature is null || !ReferenceTypes.StorageMatches(required, signature) || this.ProveOwned(implementation, use) == ConstraintProof.Proven)
        {
            return null;
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
            var fact = new OwnedConversionFact(at, implementation, name, member, origin, closure is null ? null : CaptureEntryOf(closure, name), closure, borrow, origin is not null && BodyLocalOrigin(origin));
            if (fact.Refuted)
            {
                return fact;
            }

            unknown ??= fact;
        }

        return unknown;

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

        if (type.Origin is { } origin && !this.ProvesOriginOutlives(origin, BoundOrigin.Static, use))
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
