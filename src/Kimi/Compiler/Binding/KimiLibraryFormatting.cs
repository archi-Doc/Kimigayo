// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private static bool FormattingName(Koto? node, string name)
        => FormattingType(node) is TypeSemanticsKoto { Type: null } type ? type.Identifier == name :
            BareName(node, name) || (FormattingType(node) is MemberAccessKoto member && BareName(member.Left, "Text") && BareName(member.Right, name));

    private static Koto? FormattingType(Koto? node)
    {
        while (node is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Owner, Type: not null } type)
        {
            node = type.Type;
        }

        return node;
    }

    private static bool FormattingBorrow(Koto? node, SemanticsKind semantics, string name)
        => node is TypeSemanticsKoto type && type.SemanticsKind == semantics && FormattingName(type.Type, name);

    private static bool FormattingValue(Koto? node, string name)
        => name == "()" ? BareType(node) is TupleTypeKoto { ElementNodes.Count: 0 } :
            name == "Slice" ? FormattingType(node) is GenericsKoto { TypeArguments.Count: 1 } slice && BareName(slice.Identifier, "Slice") && BareName(slice.TypeArguments[0], "u8") :
            FormattingName(node, name);

    private static bool FormattingBytes(Koto? node)
        => node is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq } borrow &&
            BareType(borrow.Type) is FixedArrayTypeKoto array && BareName(array.Length, "N") && BareName(array.ElementType, "u8");

    private static bool FormattingPremise(FunctionKoto function, string parameter, string contract)
        => function.TypeConstraints.Count == 1 && function.TypeConstraints[0] is IsKoto clause && BareName(clause.Left, parameter) && BareName(clause.Right, contract);

    private static bool FormattingField(PropertyKoto field, KimiDeclarationId id, int index)
    {
        var expected = id switch
        {
            KimiDeclarationId.FixedBuffer or KimiDeclarationId.HeapBuffer => index switch { 0 => "data", 1 => "capacity", 2 => "length", 3 => "validated", 4 when id == KimiDeclarationId.FixedBuffer => "loan", _ => null },
            KimiDeclarationId.WriteWindow => index switch { 0 => "state", 1 => "start", 2 => "written", 3 => "remaining", 4 => "loan", _ => null },
            KimiDeclarationId.Utf8Writer => index switch { 0 => "destination", 1 => "dispatch", 2 => "kind", 3 => "failed", 4 => "hint", 5 => "pending", 6 => "pendingLength", 7 => "logicalLength", 8 => "loan", _ => null },
            KimiDeclarationId.Utf8Slice when index == 0 => "value",
            _ => null,
        };
        var pointer = expected is "data" or "state" or "destination" or "dispatch" or "pending";
        var exposed = expected is "capacity" or "length" or "written" or "remaining";
        return expected is not null && field.NameKoto.IdentifierName == expected && field.DeclarationKind == PropertyDeclarationKind.Let &&
            field.Modifier == (exposed ? ModifierKind.Public : ModifierKind.NoModifier) && field.AttributeChain is null && field.InitializerKoto is null && field.Accessors.Count == 0 &&
            (expected == "loan" ? BareType(field.TypeKoto) is GenericsKoto { TypeArguments: [TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null } lent] } loan &&
                BareName(loan.Identifier, "Loan") && BareName(lent.Type, "u8") && lent.OriginName == (id == KimiDeclarationId.Utf8Writer ? "target" : "source") :
                pointer ? FormattingBorrow(field.TypeKoto, SemanticsKind.Raw, "u8") : expected == "value" ? FormattingValue(field.TypeKoto, "Slice") : BareName(field.TypeKoto, expected == "failed" ? "bool" : "isize"));
    }

    private static bool ValidFormattingFunction(FunctionKoto function, KimiDeclarationId id)
    {
        var generics = id == KimiDeclarationId.TextTryFormat ? 2 : id is KimiDeclarationId.TextFixed or KimiDeclarationId.TextWriter or KimiDeclarationId.TextToString or KimiDeclarationId.WriterWrite ? 1 : 0;
        var signature = FormattingSignatures[KimiLibraryCatalog.Index(id)]!.Value;
        if (function.GenericArguments.Count != generics || function.Parameters.Count != signature.Inputs.Length ||
            !FormattingSyntaxOperand(function.ReturnType, signature.Result))
        {
            return false;
        }

        for (var i = 0; i < signature.Inputs.Length; i++)
        {
            if (!FormattingSyntaxOperand(function.Parameters[i].Type, signature.Inputs[i]))
            {
                return false;
            }
        }

        return id == KimiDeclarationId.TextWriter ? FormattingPremise(function, "W", "BufferWriter") :
            id is not (KimiDeclarationId.TextToString or KimiDeclarationId.TextTryFormat or KimiDeclarationId.WriterWrite) || FormattingPremise(function, "T", "Utf8Format");
    }

    private static bool FormattingSyntaxOperand(Koto? type, FormattingOperand expected, bool nested = false)
    {
        if (expected.Error != FormattingKind.None)
        {
            return BareType(type) is GenericsKoto { TypeArguments.Count: 2 } result && BareName(result.Identifier, "Result") &&
                FormattingSyntaxOperand(result.TypeArguments[0], expected with { Error = FormattingKind.None }, nested: true) &&
                FormattingSyntaxOperand(result.TypeArguments[1], new(expected.Error), nested: true);
        }

        return expected.Kind switch
        {
            FormattingKind.Unit => nested ? BareType(type) is TupleTypeKoto { ElementNodes.Count: 0 } : type is null,
            FormattingKind.ISize => BareName(type, "isize"),
            FormattingKind.U8 => BareName(type, "u8"),
            FormattingKind.String => BareName(type, "string"),
            FormattingKind.RawBytes => FormattingBorrow(type, SemanticsKind.Raw, "u8"),
            FormattingKind.Self => FormattingName(type, "Self"),
            FormattingKind.RefSelf => FormattingBorrow(type, SemanticsKind.Ref, "Self"),
            FormattingKind.UniqSelf => FormattingBorrow(type, SemanticsKind.Uniq, "Self"),
            FormattingKind.RefString => FormattingBorrow(type, SemanticsKind.Ref, "string"),
            FormattingKind.RefValue => FormattingBorrow(type, SemanticsKind.Ref, "T"),
            FormattingKind.UniqDestination => FormattingBorrow(type, SemanticsKind.Uniq, "W"),
            FormattingKind.UniqBytes => FormattingBytes(type),
            FormattingKind.UniqWriter => FormattingBorrow(type, SemanticsKind.Uniq, "Utf8Writer"),
            FormattingKind.Bytes => FormattingValue(type, "Slice"),
            _ => FormattingName(type, KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(FormattingDeclaration(expected.Kind))].Name),
        };
    }

    // These are compiler ABI declarations, not name-based recognition of arbitrary user functions.
    // All type/origin clauses still go through ordinary Binding after these source-shape checks.
    private bool ValidFormatting(BindingSymbol symbol, in KimiLibraryCatalog.Entry rule)
    {
        if (symbol.Intrinsic != IntrinsicKind.None || symbol.LibraryDeclaration != rule.Id || symbol.Name != rule.Name ||
            !ReferenceEquals(symbol.Declaration.CodeContext.Kotonoha, this.Kotonoha))
        {
            return false;
        }

        if (symbol.Declaration is FunctionKoto function)
        {
            if (symbol.CompilerFunction != rule.Function || function.NameBoundaryIndex >= 0 || function.Name != rule.Name ||
                function.Modifier != (rule.Id == KimiDeclarationId.TextRelease ? ModifierKind.Private : ModifierKind.Public) ||
                function.Body is not null || function.ExpressionBody is not null || function.AttributeChain is not null ||
                function.IsRequirement || function.IsGenerated || function.IsSpecialization)
            {
                return false;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                if (parameter.AttributeChain is not null || parameter.DefaultValue is not null || parameter.ExternalName != parameter.InternalName)
                {
                    return false;
                }
            }

            return ValidFormattingFunction(function, rule.Id);
        }

        if (symbol.Declaration is not DeclarationContainerKoto container || container.Modifier != ModifierKind.Public ||
            container.AttributeChain is not null || container.HasIncompatibleBindingHeader || container.Bases.Count != 0 ||
            container.GenericParameterNodes.Count != 0 || container.NestedContainers.Count != 0)
        {
            return false;
        }

        if (container is ContractKoto contract)
        {
            if (contract.OriginNames.Count != 0 || contract.ConstraintNodes.Count != 0 || contract.Members.Count != 1 ||
                contract.Members[0] is not FunctionKoto { IsRequirement: true, Parameters.Count: 2, GenericArguments.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null } requirement)
            {
                return false;
            }

            // SPEC 8.4.10.2, utf8-formatting 1.2: reserve declares confined; format declares no bound.
            var signature = FormattingSignatures[KimiLibraryCatalog.Index(rule.Id)]!.Value;
            return (rule.Id == KimiDeclarationId.BufferWriter
                ? requirement.Name == "reserve" && requirement.EffectBounds is [{ Bound: EffectBoundKind.Confined, IsSpecification: false }]
                : rule.Id == KimiDeclarationId.Utf8Format && requirement.Name == "format" && requirement.EffectBounds.Count == 0) &&
                FormattingSyntaxOperand(requirement.Parameters[0].Type, signature.Inputs[0]) &&
                FormattingSyntaxOperand(requirement.Parameters[1].Type, signature.Inputs[1]) && FormattingSyntaxOperand(requirement.ReturnType, signature.Result);
        }

        var dependent = rule.Id is KimiDeclarationId.FixedBuffer or KimiDeclarationId.WriteWindow or KimiDeclarationId.Utf8Writer or KimiDeclarationId.Utf8Slice;
        if (container is not StructKoto || container.OriginNames.Count != (dependent ? 1 : 0) ||
            (dependent && container.OriginNames[0] != (rule.Id == KimiDeclarationId.Utf8Writer ? "target" : "source")))
        {
            return false;
        }

        var fields = 0;
        var constructors = 0;
        var destructors = 0;
        for (var i = 0; i < container.Members.Count; i++)
        {
            var member = container.Members[i];
            if (member is FunctionKoto { IsConstructor: true } constructor)
            {
                constructors++;
                if (rule.Id is not (KimiDeclarationId.BufferFull or KimiDeclarationId.InvalidUtf8) || constructor.Modifier != ModifierKind.Public || constructor.Parameters.Count != 0)
                {
                    return false;
                }
            }
            else if (member is FunctionKoto { IsDestructor: true })
            {
                destructors++;
            }
            else if (member is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Let or PropertyDeclarationKind.Var } field)
            {
                if (!FormattingField(field, rule.Id, fields))
                {
                    return false;
                }

                fields++;
            }
        }

        if (destructors != (rule.Id == KimiDeclarationId.HeapBuffer ? 1 : 0))
        {
            return false;
        }

        var premise = rule.Id is KimiDeclarationId.BufferFull or KimiDeclarationId.InvalidUtf8 or KimiDeclarationId.Utf8Slice ? "Copy" :
            rule.Id is KimiDeclarationId.FixedBuffer or KimiDeclarationId.HeapBuffer ? "BufferWriter" : null;
        // SPEC utf8-formatting 1.1: the Loan-bound adapters opt out of ObjectPayload (SPEC 8.4.7.2).
        var optOut = rule.Id is KimiDeclarationId.WriteWindow or KimiDeclarationId.Utf8Writer or KimiDeclarationId.FixedBuffer;
        var clauses = container.ConstraintNodes;
        if (clauses.Count != (premise is null ? 0 : 1) + (optOut ? 1 : 0) ||
            (premise is not null && (!BareName(clauses[0].Left, "Self") || !BareName(clauses[0].Right, premise))) ||
            (optOut && (!BareName(clauses[^1].Left, "Self") || clauses[^1].Right is not NotKoto { Operand: { } renounced } || !BareName(renounced, "ObjectPayload"))))
        {
            return false;
        }

        return rule.Id switch
        {
            KimiDeclarationId.BufferFull or KimiDeclarationId.InvalidUtf8 => fields == 0 && constructors == 1 && container.ConstraintNodes.Count == 1 &&
                BareName(container.ConstraintNodes[0].Left, "Self") && BareName(container.ConstraintNodes[0].Right, "Copy"),
            KimiDeclarationId.FixedBuffer or KimiDeclarationId.WriteWindow => fields == 5 && constructors == 0,
            KimiDeclarationId.HeapBuffer => fields == 4 && constructors == 0,
            KimiDeclarationId.Utf8Writer => fields == 9 && constructors == 0,
            KimiDeclarationId.Utf8Slice => fields == 1 && constructors == 0,
            _ => false,
        };
    }
}
