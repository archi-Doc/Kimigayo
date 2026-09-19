// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    /// <summary>Checks recognition and syntax contracts before normal binding. Does not reparse or allocate.</summary>
    /// <returns>Whether available declarations match their compiler contracts.</returns>
    internal bool ValidateDeclarations()
    {
        this.InvalidDeclaration = null;
        var valid = !this.Kotonoha.DiagnosticCollection.HasErrors && this.Kotonoha.GeneratedFunction is null &&
            this.ValidCompilerGroup(this.Intrinsics, this.IntrinsicsSymbol, this.IntrinsicsScope) &&
            this.ValidCompilerGroup(this.Console, this.ConsoleSymbol, this.ConsoleScope) &&
            this.ValidCompilerGroup(this.Test, this.TestSymbol, this.TestScope) &&
            this.SliceIterator is { Declaration: StructKoto helper } &&
            ReferenceEquals(FindDeclaration(this.Kotonoha.RootKoto, "SliceIterator", false), helper);
        this.ValidatedDeclarationCount = 0;
        for (var i = 0; i < this.declarations.Length; i++)
        {
            var entry = this.declarations[i];
            ref readonly var rule = ref KimiLibraryCatalog.Entries[i];
            var state = KimiDeclarationState.Missing;
            if (entry.Symbol is { } symbol)
            {
                var matches = ReferenceEquals(FindDeclaration((DeclarationContainerKoto)symbol.Scope.Owner, entry.Name, symbol.Kind == BindingSymbolKind.Function), symbol.Declaration) &&
                    (rule.Intrinsic != IntrinsicKind.None ? this.Valid(symbol, rule.Intrinsic) : entry.Id switch
                    {
                        KimiDeclarationId.Replace or KimiDeclarationId.Exchange or KimiDeclarationId.Swap => this.ValidUpdate(symbol, entry.Id),
                        KimiDeclarationId.WriteLine => this.ValidWriteLine(),
                        KimiDeclarationId.TestTempDirectory => this.ValidTempDirectory(symbol),
                        KimiDeclarationId.MakeObj => this.ValidMakeObj(),
                        KimiDeclarationId.Iterator => this.ValidIterator(symbol),
                        KimiDeclarationId.Slice => this.ValidSlice(symbol),
                        _ => this.ValidEnum(symbol, entry.Id),
                    });
                state = matches ? KimiDeclarationState.Validated : KimiDeclarationState.Invalid;
                valid &= matches;
                this.ValidatedDeclarationCount += matches ? 1 : 0;
                if (!matches)
                {
                    this.InvalidDeclaration ??= symbol.Declaration;
                }
            }
            else if (rule.SourceExpected)
            {
                // A supported declaration removed from an embedded source is a broken
                // library, distinct from a catalog API which has no implementation yet.
                valid = false;
                this.InvalidDeclaration ??= (Koto?)FindDeclaration(this.Kotonoha.RootKoto, rule.Name, rule.IsFunction) ?? this.Kotonoha.RootKoto;
            }

            this.declarations[i] = entry with { State = state };
        }

        return this.IsValid = valid;
    }

    /// <summary>Checks compilation-local identities after the normal semantic pipeline.</summary>
    /// <returns>Whether the recognized declarations retained their required bound identities.</returns>
    internal bool ValidateBoundDeclarations()
    {
        var valid = this.IsValid;
        for (var i = 0; i < this.declarations.Length; i++)
        {
            var entry = this.declarations[i];
            if (entry.State != KimiDeclarationState.Validated || entry.Symbol is not { } symbol)
            {
                continue;
            }

            var matches = ReferenceEquals(symbol.Declaration.BoundSymbol, symbol) && symbol.Declaration.BindingState == BindingState.Resolved;
            if (matches && entry.Id == KimiDeclarationId.Iterator)
            {
                var next = (FunctionKoto)((ContractKoto)symbol.Declaration).Members[1];
                matches = ReferenceEquals(next.BoundSymbol?.Type?.Symbol, this.Option);
            }

            if (!matches)
            {
                this.InvalidDeclaration ??= symbol.Declaration;
                this.declarations[i] = entry with { State = KimiDeclarationState.Invalid };
                this.ValidatedDeclarationCount--;
                valid = false;
            }
        }

        return this.IsValid = valid;
    }

    private static Koto? BareType(Koto? node)
    {
        while (node is TypeSemanticsKoto { Type: not null, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null } type)
        {
            node = type.Type;
        }

        return node;
    }

    private static bool BareName(Koto? node, string name) => BareType(node) is IdentifierNameKoto identifier ? identifier.IdentifierName == name :
        BareType(node) is TypeSemanticsKoto { Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null } type && type.Identifier == name;

    private bool ValidEnum(BindingSymbol symbol, KimiDeclarationId id)
    {
        var option = id == KimiDeclarationId.Option;
        if (id is not (KimiDeclarationId.Option or KimiDeclarationId.Result) ||
            symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not EnumKoto { HasIncompatibleBindingHeader: false, Modifier: ModifierKind.Public, AttributeChain: null, Bases.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, ConstraintNodes.Count: 0 } declaration ||
            declaration.Name != (option ? "Option" : "Result") || !ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) ||
            declaration.GenericParameterNodes.Count != (option ? 1 : 2) || declaration.Members.Count < (option ? 3 : 2))
        {
            return false;
        }

        for (var i = 0; i < declaration.GenericParameterNodes.Count; i++)
        {
            if (declaration.GenericParameterNodes[i] is not GenericParameterKoto { SemanticsParameter: null, AttributeChain: null } parameter || parameter.Identifier != (i == 0 ? "T" : "E"))
            {
                return false;
            }
        }

        // Ordinary helper methods do not change the required Case layout or order.
        for (var i = option ? 3 : 2; i < declaration.Members.Count; i++)
        {
            if (declaration.Members[i] is not FunctionKoto)
            {
                return false;
            }
        }

        if (option && (declaration.Members[0] is not SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, AttributeChain: null } conditional ||
            conditional.Operands.Length != 2 || !CopyClause(conditional.Operands[0], "Self") ||
            conditional.Operands[1] is not SyntaxFormKoto premises || premises.Operands.Length != 1 || !CopyClause(premises.Operands[0], "T")))
        {
            return false;
        }

        return Case(declaration.Members[option ? 1 : 0], option ? "Some" : "Ok", "T") &&
            Case(declaration.Members[option ? 2 : 1], option ? "None" : "Err", option ? null : "E");

        static bool CopyClause(Koto node, string subject)
            => node is IsKoto { Left: IdentifierNameKoto left, Right: IdentifierNameKoto right, AttributeChain: null } && left.IdentifierName == subject && right.IdentifierName == "Copy";

        static bool Case(Koto node, string name, string? type)
            => node is SyntaxFormKoto { Akind: KotoKind.EnumCase, AttributeChain: null } form && form.Operands.Length == 2 &&
            form.Operands[0] is IdentifierNameKoto identifier && identifier.IdentifierName == name &&
            form.Operands[1] is SyntaxFormKoto payload && payload.Operands.Length == (type is null ? 0 : 1) &&
            (type is null || (payload.Operands[0] is TypeSemanticsKoto { Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null } element && element.Identifier == type));
    }

    private bool ValidUpdate(BindingSymbol symbol, KimiDeclarationId id)
    {
        var kind = KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function;
        var swap = id == KimiDeclarationId.Swap;
        if (symbol.CompilerFunction != kind || !ReferenceEquals(symbol.Scope, this.IntrinsicsScope) ||
            symbol.Declaration is not FunctionKoto function || !ReferenceEquals(function.Parent, this.Intrinsics) ||
            function.Name != symbol.Name || function.Modifier != ModifierKind.Public || function.AttributeChain is not null ||
            function.GenericArguments.Count != 1 || function.GenericArguments[0] is not GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } ||
            function.Origins.Count != 0 || function.Parameters.Count != 2 || function.TypeConstraints.Count != 0 ||
            function.Body is not null || function.ExpressionBody is not null || function.IsRequirement || function.IsGenerated || function.IsSpecialization)
        {
            return false;
        }

        for (var i = 0; i < 2; i++)
        {
            var parameter = function.Parameters[i];
            var name = swap ? (i == 0 ? "first" : "second") : (i == 0 ? "target" : "value");
            if (parameter.InternalName != name || parameter.ExternalName != (!swap && i == 1 ? "with" : name) ||
                parameter.IsOptional || parameter.DefaultValue is not null || parameter.AttributeChain is not null ||
                !Type(parameter.Type, swap || i == 0))
            {
                return false;
            }
        }

        return id == KimiDeclarationId.Exchange ? Type(function.ReturnType, false) : function.ReturnType is TupleTypeKoto { ElementNodes.Count: 0 };

        static bool Type(Koto? node, bool borrow) => node is TypeSemanticsKoto { OriginName: null, OriginExpression: null, OriginArguments: null, SemanticsParameter: null } type &&
            (borrow ? type.SemanticsKind == SemanticsKind.Uniq && Type(type.Type, false) : type is { SemanticsKind: SemanticsKind.Owner, Type: null, Identifier: "T" });
    }

    private bool ValidTempDirectory(BindingSymbol symbol)
        => symbol.CompilerFunction == CompilerFunctionKind.TestTempDirectory && ReferenceEquals(symbol.Scope, this.TestScope) &&
            symbol.Declaration is FunctionKoto function && ReferenceEquals(function.Parent, this.Test) && function is
            {
                Name: "tempDirectory", Modifier: ModifierKind.Public, GenericArguments.Count: 0, Origins.Count: 0,
                Parameters.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null,
                IsRequirement: false, IsGenerated: false, IsSpecialization: false,
                ReturnType: TypeSemanticsKoto { Type: null, Identifier: "string", SemanticsKind: SemanticsKind.Owner, OriginExpression: null, OriginName: null, OriginArguments: null },
            };

    private bool ValidWriteLine()
        => this.WriteLine.CompilerFunction == CompilerFunctionKind.WriteLine && ReferenceEquals(this.WriteLine.Scope, this.ConsoleScope) &&
        this.WriteLine.Declaration is FunctionKoto function &&
        ReferenceEquals(function.Parent, this.Console) &&
        function.Name == "writeLine" && function.Modifier == ModifierKind.Public &&
        function.GenericArguments.Count == 0 && function.Origins.Count == 0 && function.Parameters.Count == 1 &&
        function.TypeConstraints.Count == 0 && function.ReturnType is null && function.Body is null && function.ExpressionBody is null &&
        function.AttributeChain is null && !function.IsRequirement && !function.IsGenerated && !function.IsSpecialization &&
        function.Parameters[0] is
        {
            ExternalName: "text", InternalName: "text", IsOptional: false, DefaultValue: null, AttributeChain: null,
            Type: TypeSemanticsKoto { Type: null, Identifier: "string", SemanticsKind: SemanticsKind.Owner, OriginExpression: null, OriginName: null, OriginArguments: null },
        };

    private bool ValidIterator(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Name: "Iterator", HasIncompatibleBindingHeader: false, Members.Count: 2, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
        declaration.Members[0] is SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 1, AttributeChain: null } associated &&
        associated.Operands[0] is IdentifierNameKoto { IdentifierName: "Element" } &&
        declaration.Members[1] is FunctionKoto { Name: "next", IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 1, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function &&
        function.Parameters[0] is { InternalName: "self", ExternalName: "self", IsOptional: false, DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: TypeSemanticsKoto { Identifier: "Self", Type: null, SemanticsKind: SemanticsKind.Owner, OriginName: null, OriginExpression: null, OriginArguments: null } } } &&
        BareType(function.ReturnType) is GenericsKoto { TypeArguments.Count: 1 } option && BareName(option.Identifier, "Option") &&
        BareType(option.TypeArguments[0]) is MemberAccessKoto element && BareName(element.Left, "Self") && BareName(element.Right, "Element");

    private bool ValidSlice(BindingSymbol symbol)
    {
        if (symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not StructKoto { Name: "Slice", HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 1, OriginNames.Count: 1, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } slice ||
            !ReferenceEquals(slice.Parent, this.Kotonoha.RootKoto) || slice.OriginNames[0] != "source" ||
            slice.GenericParameterNodes[0] is not GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } ||
            FindDeclaration(slice, "iterate", true) is not FunctionKoto)
        {
            return false;
        }

        for (var i = 0; i < slice.Members.Count; i++)
        {
            if (slice.Members[i] is not FunctionKoto)
            {
                return false; // Slice storage is compiler-managed; helpers cannot add fields.
            }
        }

        return true;
    }

    private bool ValidCompilerGroup(GroupKoto group, BindingSymbol identity, BindingScope scope)
    {
        if (!ReferenceEquals(FindDeclaration(this.Kotonoha.RootKoto, identity.Name, false), group) ||
            !ReferenceEquals(group.Parent, this.Kotonoha.RootKoto) ||
            group is not { Modifier: ModifierKind.Public, AttributeChain: null, HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 0, OriginNames.Count: 0, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0 })
        {
            this.InvalidDeclaration ??= group;
            return false;
        }

        // The signature-only groups are closed to unregistered implementations.
        // Adding a catalog intrinsic requires no hard-coded group member count.
        for (var i = 0; i < group.Members.Count; i++)
        {
            var member = group.Members[i];
            if (member.BoundSymbol is not { CompilerFunction: not CompilerFunctionKind.None } symbol ||
                !ReferenceEquals(symbol.Scope, scope) || !ReferenceEquals(symbol.Declaration, member) ||
                Array.IndexOf(this.registeredSymbols, symbol) < 0)
            {
                this.InvalidDeclaration ??= member;
                return false;
            }
        }

        return true;
    }

    private bool ValidMakeObj()
        => this.MakeObj.CompilerFunction == CompilerFunctionKind.MakeObj && ReferenceEquals(this.MakeObj.Scope, this.IntrinsicsScope) &&
        ReferenceEquals(this.MakeObj.Declaration.Parent, this.Intrinsics) &&
        this.MakeObj.Declaration is FunctionKoto { Name: "makeObj", Modifier: ModifierKind.Public, AttributeChain: null, GenericArguments.Count: 1, Parameters.Count: 1, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false } f &&
        f.GenericArguments[0] is GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } &&
        f.Parameters[0] is { InternalName: "value", ExternalName: "value", IsOptional: false, DefaultValue: null, AttributeChain: null } p &&
        BareName(p.Type, "T") && f.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Obj, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: { } inner } && BareName(inner, "T");

    private bool Valid(BindingSymbol symbol, IntrinsicKind kind)
        => symbol.Intrinsic == kind && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Members.Count: 0, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        declaration.Name == symbol.Name && ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto);
}
