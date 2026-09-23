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

    private static bool FormattingResult(Koto? node, string success, string error)
        => BareType(node) is GenericsKoto { TypeArguments.Count: 2 } result && BareName(result.Identifier, "Result") &&
            FormattingValue(result.TypeArguments[0], success) && FormattingName(result.TypeArguments[1], error);

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
            KimiDeclarationId.FixedBuffer or KimiDeclarationId.HeapBuffer => index switch { 0 => "data", 1 => "capacity", 2 => "length", 3 => "validated", _ => null },
            KimiDeclarationId.WriteWindow => index switch { 0 => "state", 1 => "start", 2 => "written", 3 => "remaining", _ => null },
            KimiDeclarationId.Utf8Writer => index switch { 0 => "destination", 1 => "dispatch", 2 => "kind", 3 => "failed", 4 => "hint", 5 => "pending", 6 => "pendingLength", 7 => "logicalLength", _ => null },
            KimiDeclarationId.Utf8Slice when index == 0 => "value",
            _ => null,
        };
        var pointer = expected is "data" or "state" or "destination" or "dispatch" or "pending";
        var exposed = expected is "capacity" or "length" or "written" or "remaining";
        return expected is not null && field.NameKoto.IdentifierName == expected && field.DeclarationKind == PropertyDeclarationKind.Let &&
            field.Modifier == (exposed ? ModifierKind.Public : ModifierKind.NoModifier) && field.AttributeChain is null && field.InitializerKoto is null && field.Accessors.Count == 0 &&
            (pointer ? FormattingBorrow(field.TypeKoto, SemanticsKind.Unsafe, "u8") : expected == "value" ? FormattingValue(field.TypeKoto, "Slice") : BareName(field.TypeKoto, expected == "failed" ? "bool" : "isize"));
    }

    private static bool ValidFormattingFunction(FunctionKoto function, KimiDeclarationId id)
    {
        var generics = id == KimiDeclarationId.TextTryFormat ? 2 : id is KimiDeclarationId.TextFixed or KimiDeclarationId.TextWriter or KimiDeclarationId.TextToString or KimiDeclarationId.WriterWrite ? 1 : 0;
        var inputs = id is KimiDeclarationId.TextTryFormat or KimiDeclarationId.FixedBufferReserve or KimiDeclarationId.HeapBufferReserve or KimiDeclarationId.WindowPush or KimiDeclarationId.WindowAppend or KimiDeclarationId.WindowLimit or KimiDeclarationId.WriterWrite ? 2 : 1;
        if (function.GenericArguments.Count != generics || function.Parameters.Count != inputs)
        {
            return false;
        }

        var first = function.Parameters[0].Type;
        var result = function.ReturnType;
        var second = inputs == 2 ? function.Parameters[1].Type : null;
        return id switch
        {
            KimiDeclarationId.TextFixed => FormattingBytes(first) && FormattingName(result, "FixedBuffer"),
            KimiDeclarationId.TextHeap => BareName(first, "isize") && FormattingName(result, "HeapBuffer"),
            KimiDeclarationId.TextWriter => FormattingBorrow(first, SemanticsKind.Uniq, "W") && FormattingName(result, "Utf8Writer") && FormattingPremise(function, "W", "BufferWriter"),
            KimiDeclarationId.TextUtf8 => FormattingBorrow(first, SemanticsKind.Ref, "string") && FormattingName(result, "Utf8Slice"),
            KimiDeclarationId.TextValidateUtf8 => FormattingValue(first, "Slice") && FormattingResult(result, "Utf8Slice", "InvalidUtf8"),
            KimiDeclarationId.TextToString => FormattingBorrow(first, SemanticsKind.Ref, "T") && BareName(result, "string") && FormattingPremise(function, "T", "Utf8Format"),
            KimiDeclarationId.TextTryFormat => FormattingBorrow(first, SemanticsKind.Ref, "T") && FormattingBytes(second) && FormattingResult(result, "Utf8Slice", "BufferFull") && FormattingPremise(function, "T", "Utf8Format"),
            KimiDeclarationId.TextRelease => first is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe } && result is null,
            KimiDeclarationId.FixedBufferBytes or KimiDeclarationId.HeapBufferBytes => FormattingBorrow(first, SemanticsKind.Ref, "Self") && FormattingValue(result, "Slice"),
            KimiDeclarationId.FixedBufferText or KimiDeclarationId.HeapBufferText => FormattingBorrow(first, SemanticsKind.Ref, "Self") && FormattingResult(result, "Utf8Slice", "InvalidUtf8"),
            KimiDeclarationId.FixedBufferValidate or KimiDeclarationId.HeapBufferValidate => FormattingBorrow(first, SemanticsKind.Uniq, "Self") && FormattingResult(result, "()", "InvalidUtf8"),
            KimiDeclarationId.FixedBufferClear or KimiDeclarationId.HeapBufferClear => FormattingBorrow(first, SemanticsKind.Uniq, "Self") && result is null,
            KimiDeclarationId.FixedBufferReserve or KimiDeclarationId.HeapBufferReserve => FormattingBorrow(first, SemanticsKind.Uniq, "Self") && BareName(second, "isize") && FormattingResult(result, "WriteWindow", "BufferFull"),
            KimiDeclarationId.FixedBufferIntoText => BareName(first, "Self") && FormattingResult(result, "Utf8Slice", "InvalidUtf8"),
            KimiDeclarationId.HeapBufferIntoString => BareName(first, "Self") && FormattingResult(result, "string", "InvalidUtf8"),
            KimiDeclarationId.WindowPush => FormattingBorrow(first, SemanticsKind.Uniq, "Self") && BareName(second, "u8") && FormattingResult(result, "()", "BufferFull"),
            KimiDeclarationId.WindowAppend => FormattingBorrow(first, SemanticsKind.Uniq, "Self") && FormattingValue(second, "Slice") && FormattingResult(result, "()", "BufferFull"),
            KimiDeclarationId.WindowLimit => BareName(first, "Self") && BareName(second, "isize") && BareName(result, "Self"),
            KimiDeclarationId.WindowCommit => BareName(first, "Self") && BareName(result, "isize"),
            KimiDeclarationId.WriterWrite => FormattingBorrow(first, SemanticsKind.Uniq, "Self") && FormattingBorrow(second, SemanticsKind.Ref, "T") && FormattingResult(result, "()", "BufferFull") && FormattingPremise(function, "T", "Utf8Format"),
            KimiDeclarationId.WriterStatus => FormattingBorrow(first, SemanticsKind.Ref, "Self") && FormattingResult(result, "()", "BufferFull"),
            KimiDeclarationId.WriteLineUtf8 => FormattingName(first, "Utf8Slice") && result is null,
            _ => false,
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

            return rule.Id == KimiDeclarationId.BufferWriter
                ? requirement.Name == "reserve" && FormattingBorrow(requirement.Parameters[0].Type, SemanticsKind.Uniq, "Self") &&
                    BareName(requirement.Parameters[1].Type, "isize") && FormattingResult(requirement.ReturnType, "WriteWindow", "BufferFull")
                : rule.Id == KimiDeclarationId.Utf8Format && requirement.Name == "format" && FormattingBorrow(requirement.Parameters[0].Type, SemanticsKind.Ref, "Self") &&
                    FormattingBorrow(requirement.Parameters[1].Type, SemanticsKind.Uniq, "Utf8Writer") && FormattingResult(requirement.ReturnType, "()", "BufferFull");
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
        if (premise is null ? container.ConstraintNodes.Count != 0 : container.ConstraintNodes.Count != 1 ||
            !BareName(container.ConstraintNodes[0].Left, "Self") || !BareName(container.ConstraintNodes[0].Right, premise))
        {
            return false;
        }

        return rule.Id switch
        {
            KimiDeclarationId.BufferFull or KimiDeclarationId.InvalidUtf8 => fields == 0 && constructors == 1 && container.ConstraintNodes.Count == 1 &&
                BareName(container.ConstraintNodes[0].Left, "Self") && BareName(container.ConstraintNodes[0].Right, "Copy"),
            KimiDeclarationId.FixedBuffer or KimiDeclarationId.HeapBuffer or KimiDeclarationId.WriteWindow => fields == 4 && constructors == 0,
            KimiDeclarationId.Utf8Writer => fields == 8 && constructors == 0,
            KimiDeclarationId.Utf8Slice => fields == 1 && constructors == 0,
            _ => false,
        };
    }
}
