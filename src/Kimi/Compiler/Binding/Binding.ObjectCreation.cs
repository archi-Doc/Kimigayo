// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 13.5.8: inference finishes before the common creation/identity/upcast table.
    // The retained intrinsic call reuses acquisition, Loans, effects, cleanup and generation.
    private BoundType? BindObjectAdaptation(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target)
    {
        Complete(conversion.Right, target);
        if (ObjectTypes.HandleMode(source) is not null || ObjectTypes.IsBorrow(source))
        {
            return ReferenceEquals(source, target) ? this.CompleteIdentity(conversion, target) : this.BindObjectUpcast(conversion, scope, source, target);
        }

        if (source.Semantics != SemanticsKind.Owner || !ReferenceEquals(source, target.Components[0]))
        {
            return this.FailMismatch(conversion, conversion, source, target);
        }

        if (source.Symbol?.ObjectPayloadOptOut is { } renounced)
        {
            return this.FailObjectPayload(conversion, renounced);
        }

        var proof = this.RequestCapability(source, this.Library.ObjectPayload, scope);
        if (proof != ConstraintProof.Proven)
        {
            this.RequireConstraint(conversion, proof, this.capabilityMode);
            return null;
        }

        if (IsBarePlace(conversion.Left) && this.ProveCopy(source, conversion) != ConstraintProof.Proven)
        {
            return this.FailAcquisition(conversion, BindingFailure.TransferRequired, conversion.Left);
        }

        var id = target.Semantics switch
        {
            SemanticsKind.Obj => KimiDeclarationId.MakeObj,
            SemanticsKind.Rc => KimiDeclarationId.MakeRc,
            _ => KimiDeclarationId.MakeArc,
        };
        if (this.Library.GetDeclarationState(id) != KimiDeclarationState.Validated || this.Library.GetSymbol(id) is not { } factory)
        {
            return this.Fail(conversion, BindingFailure.Unsupported, true);
        }

        var call = conversion.CreationStorage;
        if (call is null || !call.Span.Equals(conversion.Span))
        {
            var callee = new SyntheticKoto(conversion) { Parent = conversion };
            call = new InvocationKoto(conversion, new GenericsKoto(conversion, callee, [new SyntheticKoto(conversion)]), new Koto[1]);
            conversion.CreationStorage = call;
        }

        var generic = (GenericsKoto)call.Method;
        ((SyntheticKoto)generic.Identifier!).Resolve(factory, null, null);
        ((SyntheticKoto)generic.TypeArguments[0]).Resolve(null, source, null);
        ((Koto[])call.ArgumentNodes)[0] = conversion.Left;
        ResetSynthetic(generic);
        ResetSynthetic(call);
        this.nodes.Add(call);
        if (this.BindCall(call, scope, null) is null)
        {
            return Complete(conversion, null);
        }

        Complete(conversion.Right, target);
        conversion.ConversionBinding = ConversionBinding.ObjectCreation;
        return Complete(conversion, target);
    }
}
