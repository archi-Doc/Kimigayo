// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// The binding of a variable declaration: a local, a Field or a Property with its initializer and accessor bodies.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BoundType? BindVariable(VariableKoto variable, BindingScope scope)
    {
        // A variable is checked in its own frame, also when a use binds it first (BindReference).
        var frame = this.BeginConsultation(variable);
        var type = this.BindVariableCore(variable, scope);
        this.EndConsultation(variable, frame);
        this.Consulted(variable);
        return type;
    }

    private BoundType? BindVariableCore(VariableKoto variable, BindingScope scope)
    {
        var symbol = this.symbols[variable];
        if (variable.InitializerKoto is NoInitKoto directive && !this.CheckNoInitDeclaration(directive, variable))
        {
            return this.CompleteDependent(variable, directive);
        }

        if (symbol.Resolving)
        {
            return this.Fail(variable, BindingFailure.Cycle, true);
        }

        if (symbol.Property is not null)
        {
            this.BindHeader(symbol);
            this.CheckCaseBound(variable);
        }

        symbol.Resolving = true;
        if (symbol.Kind == BindingSymbolKind.Local && variable.TypeKoto is { } annotation && variable.InitializerKoto is { } arrayInitializer and not NoInitKoto)
        {
            this.InferArrayAnnotation(annotation, arrayInitializer, scope);
        }

        var originDeclaration = this.BeginOriginDeclaration(variable, scope);
        var declared = symbol.Property is not null ? symbol.Type : variable.TypeKoto is { } type ? this.BindType(type, scope) : null;
        if (symbol.Kind == BindingSymbolKind.Local && declared is { ContainsParameter: true })
        {
            declared = this.SubstituteIdentityPremises(declared, scope); // SPEC 8.2: one Type in the premise's scope.
        }

        if (originDeclaration is not null)
        {
            foreach (var set in originDeclaration.Sets.Values)
            {
                this.BindOriginSetType(set, scope);
            }

            this.CompleteOriginDeclaration(originDeclaration);
            declared = variable.TypeKoto?.BoundType ?? declared;
        }

        // A Property's initializer and its fit are its initializer's body (SPEC 11.8, 6.3.3; DECISIONS R4-M101 E1, E4); a local's are
        // the body that binds the local. The local's region slots are numbered once, in binding order: its initializer's Origins with no
        // candidate first (ResolveInitializerOrigins), then its Type's (InferLocalOrigins, LocalBorrowType; SPEC 15.3.6).
        var slot = -1;
        var saved = this.EnterBody(symbol.Property is null ? this.formingBody : InitializerBody(symbol));
        BoundType? inferred;
        try
        {
            inferred = variable.InitializerKoto is { } initializer ? this.BindExpected(initializer, scope, declared, variable.TypeKoto) : null;
            symbol.Resolving = false;
            if (inferred is not null && this.initializerOrigins.TryGetValue(variable, out var initializerOrigins) && initializerOrigins.State < 2)
            {
                // SPEC 15.4.4: finish all initializer acquisition bounds before checking the local's fixed Type.
                inferred = this.ResolveInitializerOrigins(inferred, initializerOrigins, variable, scope, ref slot);
            }

            if (symbol.Kind == BindingSymbolKind.Local && declared is not null && inferred is not null)
            {
                // Select the Copy-read shape before inferring omitted Origins. Comparing the
                // unresolved Origin with the stored reference's Origin would reject ref/ref/T
                // initializers before the ordinary lifetime constraints can be inferred.
                if (variable.InitializerKoto is { } value && !ReferenceTypes.StorageMatches(inferred, declared) &&
                    this.ReadTypeReferent(inferred, scope) is { } referent && ReferenceTypes.StorageMatches(referent, declared))
                {
                    this.adaptations[value] = new(ExpectedAdaptationKind.ReferentRead, referent);
                    inferred = referent;
                }

                declared = this.InferLocalOrigins(declared, inferred, variable, scope, slot);
            }

            if (declared is not null && inferred is not null && !this.CheckTypeUse(inferred, declared, variable, out var failure))
            {
                this.FailMismatch(variable, variable.InitializerKoto ?? variable, inferred, declared, failure);
            }
        }
        finally
        {
            this.formingBody = saved;
        }

        if (symbol.Kind == BindingSymbolKind.Local && declared is not null && variable.TypeKoto is { } occurrence)
        {
            this.AddTypeClauseObligations(declared, occurrence); // SPEC 15.3.3: the Type's clauses at this use.
        }

        if (symbol.Kind == BindingSymbolKind.Local && variable.TypeKoto is null && declared is null && inferred is not null)
        {
            declared = this.LocalBorrowType(inferred, variable, variable.InitializerKoto!, slot);
        }

        // An unavailable written Type cannot be replaced by the initializer's Type.
        symbol.Type = variable.TypeKoto is null ? declared ?? inferred : declared;
        variable.NameKoto.BoundSymbol = symbol;
        Complete(variable.NameKoto, symbol.Type);
        if (variable is PropertyKoto property)
        {
            for (var i = 0; i < property.Accessors.Count; i++)
            {
                this.BindNode(property.Accessors[i], scope);
            }
        }

        if (symbol.Type is null && variable.BindingFailure == BindingFailure.None)
        {
            if (variable.TypeKoto is { } written)
            {
                // A property's written Type is bound with its header; the variable consulted it all the same.
                this.Consulted(written);
            }

            this.Fail(variable, BindingFailure.MissingType, true);
        }

        return Complete(variable, symbol.Type);
    }
}
