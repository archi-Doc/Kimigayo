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

        // Interim until R7-D2 d4 (Binding.DeclQuery): the variable binding and the header read each other's Running state.
        if (symbol.Resolving || this.QueryState(symbol, DeclQueryKind.Signature) == DeclState.Running)
        {
            return this.Record(variable, BindingFailure.Cycle, [], unresolved: true);
        }

        // SPEC 11.6, 23.1.2 (DECISIONS BU-105; G812 rulings 173, 174): a header whose query failed (Error, its re-entry's cycle) is final in
        // the pass, even where the computation the re-entry interrupted formed its Type: the declaration rests at that record and records
        // nothing more (its Type stays the header's and it is not completed), while its parts are still bound (the initializer, the
        // literal bodies its header left and the accessor bodies), so their independent records stay visible.
        var rests = false;
        if (symbol.Property is not null)
        {
            this.BindHeader(symbol);
            rests = this.QueryRecord(symbol, DeclQueryKind.Signature) is not null;
            this.CheckCaseBound(variable);
        }

        symbol.Resolving = true;
        if (symbol.Kind == BindingSymbolKind.Local && variable.TypeKoto is { } annotation && variable.InitializerKoto is { } arrayInitializer and not NoInitKoto)
        {
            this.InferArrayAnnotation(annotation, arrayInitializer, scope);
        }

        var originDeclaration = this.BeginOriginDeclaration(variable, scope);
        var declared = symbol.Property is not null ? symbol.Type : variable.TypeKoto is { } type ? this.BindType(type, scope) : null;
        if (symbol.Kind == BindingSymbolKind.Local && declared is not null)
        {
            declared = this.ClassForm(declared, scope, out _); // SPEC 8.2.3: one Type in the premise's scope.
        }

        if (originDeclaration is not null)
        {
            this.BindOriginSetTypes(originDeclaration, scope);
            this.CompleteOriginDeclaration(originDeclaration);
            declared = variable.TypeKoto?.BoundType ?? declared;
        }

        // A Property's initializer and its fit are its initializer's body (SPEC 11.8, 6.3.3; DECISIONS R4-M101 E1, E4); a local's are
        // the body that binds the local. The local's region slots are numbered once, in binding order: the slots its equalities fix first
        // (FixedRegions, whoever completed the declaration), then its initializer's Origins with no candidate (ResolveInitializerOrigins),
        // then its Type's (InferLocalOrigins, LocalBorrowType; SPEC 15.3.6).
        var slot = -1 - FixedRegions(this.originDeclarations.GetValueOrDefault(variable));
        var saved = this.EnterBody(symbol.Property is null ? this.formingBody : InitializerBody(symbol));
        BoundType? inferred;
        try
        {
            inferred = variable.InitializerKoto is { } initializer ? this.BindExpected(initializer, scope, declared, variable.TypeKoto) : null;
            symbol.Resolving = false;
            if (inferred is not null && this.initializerOrigins.TryGetValue(variable, out var initializerOrigins) && !initializerOrigins.Resolves)
            {
                // SPEC 15.4.4: finish all initializer acquisition bounds before checking the local's fixed Type.
                inferred = this.ResolveInitializerOrigins(inferred, initializerOrigins, variable, scope, ref slot);
            }

            if (symbol.Kind == BindingSymbolKind.Local && declared is not null && inferred is not null)
            {
                // Select the Copy-read shape before inferring omitted Origins. Comparing the
                // unresolved Origin with the stored reference's Origin would reject ref/ref/T
                // initializers before the ordinary lifetime constraints can be inferred.
                if (variable.InitializerKoto is { } value && !TyCtxt.StorageMatches(inferred, declared) &&
                    this.ReadTypeReferent(inferred, scope, variable) is { } referent && TyCtxt.StorageMatches(referent, declared))
                {
                    this.adaptations[value] = new(ExpectedAdaptationKind.ReferentRead, referent);
                    inferred = referent;
                }

                declared = this.InferLocalOrigins(declared, inferred, variable, scope, slot);
            }

            // The misfit fails the node that delivers the value: a Property's initializer, as a body's delivering node does (a return
            // jump, an expression body, an accessor body: BindAccessorBody), so the Property keeps its Type (SPEC 11.6, 23.1.2); a local's
            // declaration, the statement of the body that binds it.
            if (declared is not null && inferred is not null && !this.CheckTypeUse(inferred, declared, variable, out var failure))
            {
                var value = variable.InitializerKoto ?? variable;
                this.FailMismatch(symbol.Property is null ? variable : value, value, inferred, declared, failure);
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
        symbol.Type = variable.TypeKoto is null && !rests ? declared ?? inferred : declared;
        variable.NameKoto.BoundSymbol = symbol;
        Complete(variable.NameKoto, symbol.Type);
        if (variable is PropertyKoto property)
        {
            // SPEC 23.1.2 (DECISIONS G52-1): a Property's verdict, stored or computed, reads no binding result of its accessors (its Type is
            // its header's or its initializer's, SPEC 11.4, 11.6), nor of the literal bodies its header left (G812 ruling 174, SPEC 10.5.4),
            // so they are bound outside its frame, as a container's declarations are (BindContainedDeclarations).
            var consultation = this.SuspendConsultation();
            try
            {
                this.BindLeftBodies(symbol.Property!);
                for (var i = 0; i < property.Accessors.Count; i++)
                {
                    this.BindNode(property.Accessors[i], scope);
                }
            }
            finally
            {
                this.ResumeConsultation(consultation);
            }
        }

        if (symbol.Type is null && variable.BindingFailure == BindingFailure.None)
        {
            if (variable.TypeKoto is { } written)
            {
                // A property's written Type is bound with its header; the variable consulted it all the same.
                this.Consulted(written);
            }

            this.Record(variable, BindingFailure.MissingType, [], unresolved: true);
        }

        return rests ? null : Complete(variable, symbol.Type);
    }
}
