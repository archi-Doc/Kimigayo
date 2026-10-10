// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static KimiDeclarationId ObjectFactoryId(SemanticsKind mode)
        => mode switch
        {
            SemanticsKind.Obj => KimiDeclarationId.MakeObj,
            SemanticsKind.Rc => KimiDeclarationId.MakeRc,
            _ => KimiDeclarationId.MakeArc,
        };

    // SPEC 13.5.8: inference finishes before the common creation/identity/upcast table.
    // The retained intrinsic call reuses acquisition, Loans, effects, cleanup and generation.
    private BoundType? BindObjectAdaptation(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target)
    {
        Complete(conversion.Right, target);
        var operation = ExplicitAdaptationPlan.Select(source, target);
        if (operation == ConversionBinding.Identity)
        {
            return this.CompleteIdentity(conversion, target);
        }

        if (operation == ConversionBinding.ObjectUpcast)
        {
            return this.BindObjectUpcast(conversion, scope, source, target);
        }

        if (!this.CheckAdaptationCase(conversion, scope, source, target, operation, []))
        {
            return Complete(conversion, null);
        }

        return this.BindObjectCreationCall(conversion, scope, source, target);
    }

    private BoundType? BindObjectCreationCall(ConversionKoto conversion, BindingScope scope, BoundType source, BoundType target, bool selectedCase = false)
    {
        var id = ObjectFactoryId(target.Semantics);
        if (this.Library.GetSymbol(id) is not { } factory)
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
        if (selectedCase)
        {
            // Every row already proved the exact payload and acquisition. The written operand remains symbolic;
            // checking it again against one representative row would apply that row to the other cases.
            call.BoundSymbol = this.ResolveObjectCreation(conversion, source, target, call.CallStorage ??= new()).Target;
            Complete(call, target);
        }
        else if (this.BindCall(call, scope, null) is null)
        {
            return Complete(conversion, null);
        }

        Complete(conversion.Right, target);
        conversion.ConversionBinding = ConversionBinding.ObjectCreation;
        return Complete(conversion, target);
    }
}
