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
        // Only the library's own syntax precedes this decision; later phases rest on it.
        var valid = !this.Kotonoha.Compilation.Diagnostics.HasSyntaxErrors(this.Kotonoha) && this.Kotonoha.GeneratedFunction is null &&
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
                var matches = ReferenceEquals(FindDeclaration((DeclarationContainerKoto)symbol.Scope.Owner, entry.Name, symbol.Kind == BindingSymbolKind.Function, rule.Overload), symbol.Declaration) &&
                    (rule.Intrinsic != IntrinsicKind.None ? this.Valid(symbol, rule.Intrinsic) : entry.Id switch
                    {
                        KimiDeclarationId.Replace or KimiDeclarationId.Exchange or KimiDeclarationId.Swap => this.ValidUpdate(symbol, entry.Id),
                        KimiDeclarationId.WriteLine => this.ValidWriteLine(),
                        KimiDeclarationId.TestTempDirectory => this.ValidTempDirectory(symbol),
                        KimiDeclarationId.MakeObj => this.ValidMakeObj(),
                        KimiDeclarationId.Iterator => this.ValidIterator(symbol),
                        KimiDeclarationId.LendingIterator => this.ValidLendingIterator(symbol),
                        KimiDeclarationId.Iterable or KimiDeclarationId.UniqIterable => this.ValidBorrowingIterable(symbol, entry.Id == KimiDeclarationId.UniqIterable),
                        KimiDeclarationId.IntoIterable => this.ValidIntoIterable(symbol),
                        KimiDeclarationId.Position or KimiDeclarationId.PositionRange => this.ValidPositionContract(symbol, entry.Id),
                        KimiDeclarationId.Equatable or KimiDeclarationId.Comparable => this.ValidComparisonContract(symbol, entry.Id),
                        KimiDeclarationId.Indexable or KimiDeclarationId.UniqIndexable => this.ValidIndexableContract(symbol, entry.Id),
                        KimiDeclarationId.Slice => this.ValidSlice(symbol),
                        KimiDeclarationId.Array => this.ValidArray(symbol),
                        KimiDeclarationId.Dictionary => this.ValidDictionary(symbol),
                        KimiDeclarationId.FromEnd => this.ValidFromEnd(symbol),
                        KimiDeclarationId.Start or KimiDeclarationId.End => this.ValidBoundary(symbol, entry.Id),
                        KimiDeclarationId.Range or KimiDeclarationId.ClosedRange => this.ValidRange(symbol, entry.Id),
                        KimiDeclarationId.ResolvedRange => this.ValidResolvedRange(symbol),
                        >= KimiDeclarationId.ArrayReserve and <= KimiDeclarationId.ArrayShrinkToFit or KimiDeclarationId.ArraySwap => this.ValidArrayOperation(symbol, entry.Id),
                        KimiDeclarationId.ArrayWithCapacity => this.ValidArrayConstructor(symbol),
                        >= KimiDeclarationId.DictionaryReserve and <= KimiDeclarationId.DictionaryShrinkToFit => this.ValidDictionaryOperation(symbol, entry.Id),
                        KimiDeclarationId.DictionaryIndex or KimiDeclarationId.DictionaryIndexUniq => this.ValidDictionaryOperation(symbol, entry.Id),
                        >= KimiDeclarationId.RefRemainder and <= KimiDeclarationId.OwnedRemainder => this.ValidRemainder(symbol, entry.Id),
                        >= KimiDeclarationId.StorageBorrowShared and <= KimiDeclarationId.StorageRelease => this.ValidStorageOperation(symbol, entry.Id),
                        KimiDeclarationId.DictionaryRefRemainder or KimiDeclarationId.DictionaryUniqRemainder or KimiDeclarationId.DictionaryOwnedRemainder => this.ValidDictionaryRemainder(symbol, entry.Id),
                        >= KimiDeclarationId.StorageBorrowDictionary and <= KimiDeclarationId.StorageLendValue or
                            >= KimiDeclarationId.StorageBorrowDictionaryExclusive and <= KimiDeclarationId.StorageSplitValue or
                            >= KimiDeclarationId.StorageOwnDictionary and <= KimiDeclarationId.StorageValueAt => this.ValidDictionaryStorageOperation(symbol, entry.Id),
                        KimiDeclarationId.StorageBorrowFixedShared or KimiDeclarationId.StorageBorrowFixedExclusive => this.ValidFixedStorageOperation(symbol, entry.Id),
                        KimiDeclarationId.InlineStorage => this.ValidInlineStorage(symbol),
                        KimiDeclarationId.StorageOwnFixed or KimiDeclarationId.StorageInlineBase => this.ValidFixedOwningOperation(symbol, entry.Id),
                        KimiDeclarationId.StorageDictionaryLayout => this.ValidDictionaryLayout(symbol),
                        KimiDeclarationId.StorageMissingDictionaryKey => this.ValidMissingDictionaryKey(symbol),
                        >= KimiDeclarationId.Utf8Format => this.ValidFormatting(symbol, rule),
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

            var matches = ReferenceEquals(symbol.Declaration.BoundSymbol, symbol) && symbol.Declaration.BindingState == BindingState.Resolved &&
                this.ValidBoundStorageOperation(symbol, entry.Id) && this.ValidBoundCollectionOperation(symbol, entry.Id) && ValidBoundPrimitive(symbol, entry.Id) &&
                this.ValidBoundFormattingLayout(symbol, entry.Id) && this.ValidBoundFormattingSignature(symbol, entry.Id, KimiLibraryCatalog.Entries[i].Container) &&
                this.ValidBoundRecordLayout(symbol, entry.Id);
            if (matches && entry.Id == KimiDeclarationId.LendingIterator)
            {
                matches = this.ValidBoundLendingIterator(symbol);
            }

            if (matches && entry.Id == KimiDeclarationId.Iterator)
            {
                matches = this.ValidBoundIterator(symbol);
            }

            if (matches && entry.Id is KimiDeclarationId.Iterable or KimiDeclarationId.UniqIterable or KimiDeclarationId.IntoIterable)
            {
                matches = this.ValidBoundIterationEntry(symbol, entry.Id);
            }

            if (matches && entry.Id is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable)
            {
                matches = this.ValidBoundComparisonContract(symbol, entry.Id);
            }

            if (matches && entry.Id is KimiDeclarationId.Indexable or KimiDeclarationId.UniqIndexable)
            {
                matches = this.ValidBoundIndexableContract(symbol, entry.Id);
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

    // SPEC 14.6.2, 22.1.2.3: the clauses from `start` on are the standard entry conformances and their iterator Types only.
    private static bool ValidEntryConformances(StructKoto array, int start)
    {
        for (var i = start; i < array.ConstraintNodes.Count; i++)
        {
            var clause = array.ConstraintNodes[i];
            if (clause.IsNegated || clause.FormationType is not null || clause.AttributeChain is not null ||
                !(clause.IsAssociatedConstraint || (BareName(clause.Left, "Self") && (BareName(clause.Right, "Iterable") || BareName(clause.Right, "UniqIterable") || BareName(clause.Right, "IntoIterable") ||
                    (array.Name == "Dictionary" && clause.Right is GenericsKoto { TypeArguments: [var key] } indexing && BareName(indexing.Identifier, "UniqIndexable") && BareName(key, "K"))))))
            {
                return false;
            }
        }

        return true;
    }

    // The number of fields, or -1 when the record declares anything else; Binding may add a generated constructor.
    private static int StorageFields(DeclarationContainerKoto declaration, bool requireDestructor = false)
    {
        var count = 0;
        var destructors = 0;
        for (var i = 0; i < declaration.Members.Count; i++)
        {
            if (declaration.Members[i] is VariableKoto)
            {
                count++;
            }
            else if (declaration.Members[i] is FunctionKoto { IsDestructor: true })
            {
                destructors++;
            }
            else if (declaration.Members[i] is not FunctionKoto { IsGenerated: true })
            {
                return -1;
            }
        }

        return destructors == (requireDestructor ? 1 : 0) ? count : -1;
    }

    private static VariableKoto? StorageField(DeclarationContainerKoto declaration, int ordinal)
    {
        for (var i = 0; i < declaration.Members.Count; i++)
        {
            if (declaration.Members[i] is VariableKoto field && ordinal-- == 0)
            {
                return field;
            }
        }

        return null;
    }

    private static bool ValidStorageField(DeclarationContainerKoto declaration, int index, VariableKind kind, string name, string type, bool allowInternal = false)
        => StorageField(declaration, index) is { InitializerKoto: null, AttributeChain: null } field && field.VariableKind == kind &&
        (field.Modifier is ModifierKind.NoModifier or ModifierKind.Private || (allowInternal && field.Modifier == ModifierKind.Internal)) &&
        field.NameKoto.IdentifierName == name &&
        (name == "storage" ? field.TypeKoto is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe, SemanticsParameter: null, OriginName: null, OriginExpression: null } pointer && BareName(pointer.Type, type) : BareName(field.TypeKoto, type));

    // Every member from `index` on is a function: the declaration adds no storage the compiler does not lay out.
    private static bool OnlyFunctionsFrom(DeclarationContainerKoto declaration, int index)
    {
        for (var i = index; i < declaration.Members.Count; i++)
        {
            if (declaration.Members[i] is not FunctionKoto)
            {
                return false;
            }
        }

        return true;
    }

    // A public read-only field of the named Type at the member position.
    private static int FirstStorage(DeclarationContainerKoto declaration)
    {
        var index = 0;
        while (index < declaration.Members.Count && declaration.Members[index] is IsKoto)
        {
            index++;
        }

        return index;
    }

    private static bool ValidField(DeclarationContainerKoto declaration, int index, string name, string type)
        => index < declaration.Members.Count &&
        declaration.Members[index] is VariableKoto { VariableKind: VariableKind.Let, Modifier: ModifierKind.Public, InitializerKoto: null, AttributeChain: null } field &&
        field.NameKoto.IdentifierName == name && BareName(field.TypeKoto, type);

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
                function.AllowsPositionalArgument(i) != (swap || i == 0) || parameter.DefaultValue is not null || parameter.AttributeChain is not null ||
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
        function.NameBoundaryIndex < 0 && function.Name == "writeLine" && function.Modifier == ModifierKind.Public &&
        function.GenericArguments.Count == 0 && function.Origins.Count == 0 && function.Parameters.Count == 1 &&
        function.TypeConstraints.Count == 0 && function.ReturnType is null && function.Body is null && function.ExpressionBody is null &&
        function.AttributeChain is null && !function.IsRequirement && !function.IsGenerated && !function.IsSpecialization &&
        function.Parameters[0] is
        {
            ExternalName: "text", InternalName: "text", DefaultValue: null, AttributeChain: null,
            // SPEC 22.4: writeLine borrows its text as ref/string.
            Type: TypeSemanticsKoto
            {
                Type: TypeSemanticsKoto { Type: null, Identifier: "string", SemanticsKind: SemanticsKind.Owner },
                SemanticsKind: SemanticsKind.Ref, SemanticsParameter: null, OriginExpression: null, OriginName: null, OriginArguments: null,
            },
        };

    // SPEC 4.6.9: contract Indexable<Key> with associate Element and index(self: ref/Self, key: ref/Key) -> place ref/Element
    // during self; contract UniqIndexable<Key>: Indexable<Key> with indexUniq(self: uniq/Self, key: ref/Key) -> place uniq/Element during self.
    private bool ValidIndexableContract(BindingSymbol symbol, KimiDeclarationId id)
    {
        var exclusive = id == KimiDeclarationId.UniqIndexable;
        if (symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not ContractKoto { HasIncompatibleBindingHeader: false, ConstraintNodes.Count: 0, GenericParameterNodes.Count: 1, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration ||
            !ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) || declaration.Name != (exclusive ? "UniqIndexable" : "Indexable") ||
            declaration.GenericParameterNodes[0] is not GenericParameterKoto { Identifier: "Key", SemanticsParameter: null, AttributeChain: null } ||
            declaration.Bases.Count != (exclusive ? 1 : 0) || declaration.Members.Count != (exclusive ? 1 : 2))
        {
            return false;
        }

        if (exclusive && (BareType(declaration.Bases[0]) is not GenericsKoto { TypeArguments.Count: 1 } parent || !BareName(parent.Identifier, "Indexable") || !BareName(parent.TypeArguments[0], "Key")))
        {
            return false;
        }

        if (!exclusive && (declaration.Members[0] is not SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 1, AttributeChain: null } associated ||
            associated.Operands[0] is not IdentifierNameKoto { IdentifierName: "Element" }))
        {
            return false;
        }

        if (declaration.Members[exclusive ? 0 : 1] is not FunctionKoto { IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 2, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function ||
            function.Name != (exclusive ? "indexUniq" : "index") || function.NameBoundaryIndex >= 0 ||
            function.ReturnType is not PlaceResultKoto place || place.IsExclusive != exclusive ||
            place.Type is not TypeSemanticsKoto { SemanticsParameter: null, OriginName: "self", OriginExpression: IdentifierNameKoto, OriginArguments: null, Type: { } element } || !BareName(element, "Element"))
        {
            return false;
        }

        var receiver = function.Parameters[0];
        var key = function.Parameters[1];
        return receiver.InternalName == "self" && receiver.ExternalName == "self" && receiver.DefaultValue is null && receiver.AttributeChain is null &&
            receiver.Type is TypeSemanticsKoto { SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null } receiverType &&
            receiverType.SemanticsKind == (exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref) && BareName(receiverType.Type, "Self") &&
            key.InternalName == "key" && key.ExternalName == "key" && key.DefaultValue is null && key.AttributeChain is null &&
            key.Type is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Ref, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null } keyType && BareName(keyType.Type, "Key");
    }

    private bool ValidComparisonContract(BindingSymbol symbol, KimiDeclarationId id)
    {
        var ordering = id == KimiDeclarationId.Comparable;
        if (symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not ContractKoto { HasIncompatibleBindingHeader: false, Members.Count: 1, ConstraintNodes.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration ||
            !ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) ||
            declaration.Bases.Count != (ordering ? 1 : 0) || (ordering && !BareName(declaration.Bases[0], "Equatable")) ||
            declaration.Members[0] is not FunctionKoto { IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 2, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function ||
            function.Name != (ordering ? "compare" : "equals") || function.NameBoundaryIndex >= 0 ||
            !BareName(function.ReturnType, ordering ? "i32" : "bool"))
        {
            return false;
        }

        for (var i = 0; i < 2; i++)
        {
            var parameter = function.Parameters[i];
            if (parameter.InternalName != (i == 0 ? "self" : "other") || parameter.ExternalName != parameter.InternalName ||
                parameter.DefaultValue is not null || parameter.AttributeChain is not null ||
                parameter.Type is not TypeSemanticsKoto
                {
                    SemanticsKind: SemanticsKind.Ref, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null,
                    Type: TypeSemanticsKoto { Identifier: "Self", Type: null, SemanticsKind: SemanticsKind.Owner, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, AttributeChain: null },
                })
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidLendingIterator(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Name: "LendingIterator", HasIncompatibleBindingHeader: false, Members.Count: 2, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) && OriginClauses.Get(declaration).Count == 0 &&
        declaration.Members[0] is SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 2, AttributeChain: null } associated && OriginClauses.Get(associated).Count == 0 &&
        BareType(associated.Operands[0]) is OriginApplicationKoto { ArgumentNodes.Count: 1 } family && BareName(family.Type, "LentItem") && BareName(family.ArgumentNodes[0], "step") &&
        associated.Operands[1] is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: "step", OriginArguments: null, Type: { } formation } && BareName(formation, "Self") &&
        declaration.Members[1] is FunctionKoto { Name: "next", IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 1, GenericArguments.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function && OriginClauses.Get(function).Count == 0 &&
        (function.Origins.Count == 0 || (function.Origins.Count == 1 && function.Origins[0] == "step")) &&
        function.Parameters[0] is { InternalName: "self", ExternalName: "self", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: "step", OriginArguments: null, Type: { } target } } && BareName(target, "Self") &&
        BareType(function.ReturnType) is GenericsKoto { TypeArguments.Count: 1 } option && BareName(option.Identifier, "Option") &&
        BareType(option.TypeArguments[0]) is OriginApplicationKoto { ArgumentNodes.Count: 1 } result && BareName(result.ArgumentNodes[0], "step") &&
        BareType(result.Type) is MemberAccessKoto element && BareName(element.Left, "Self") && BareName(element.Right, "LentItem");

    private bool ValidIterator(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Name: "Iterator", HasIncompatibleBindingHeader: false, Members.Count: 1, ConstraintNodes.Count: 1, Bases.Count: 1, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) && BareName(declaration.Bases[0], "LendingIterator") &&
        declaration.Members[0] is SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 1, AttributeChain: null } associated &&
        associated.Operands[0] is IdentifierNameKoto { IdentifierName: "Item" } &&
        declaration.ConstraintNodes[0] is IsKoto { IsAssociatedConstraint: true, IsNegated: false, FormationType: null, AttributeChain: null } refinement && OriginClauses.Get(refinement).Count == 0 &&
        BareType(refinement.Left) is OriginApplicationKoto { ArgumentNodes.Count: 1 } family && BareName(family.ArgumentNodes[0], "step") &&
        BareType(family.Type) is MemberAccessKoto parent && BareName(parent.Left, "LendingIterator") && BareName(parent.Right, "LentItem") && BareName(refinement.Right, "Item");

    private bool ValidBorrowingIterable(BindingSymbol symbol, bool exclusive)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { HasIncompatibleBindingHeader: false, Members.Count: 1, ConstraintNodes.Count: 1, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        declaration.Name == (exclusive ? "UniqIterable" : "Iterable") &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) && OriginClauses.Get(declaration).Count == 0 &&
        declaration.ConstraintNodes[0] is IsKoto { IsAssociatedConstraint: true, IsNegated: false, AttributeChain: null, FormationType: TypeSemanticsKoto formation } associated && OriginClauses.Get(associated).Count == 0 &&
        BareType(associated.Left) is OriginApplicationKoto { ArgumentNodes.Count: 1 } family && BareName(family.Type, "IteratorType") && BareName(family.ArgumentNodes[0], "source") &&
        formation.SemanticsKind == (exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref) && formation.SemanticsParameter is null && formation.OriginName == "source" && formation.OriginArguments is null && BareName(formation.Type, "Self") &&
        declaration.Members[0] is FunctionKoto { IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 1, GenericArguments.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function && OriginClauses.Get(function).Count == 0 &&
        function.Name == (exclusive ? "iterateUniq" : "iterate") &&
        (function.Origins.Count == 0 || (function.Origins.Count == 1 && function.Origins[0] == "source")) &&
        function.Parameters[0] is { InternalName: "self", ExternalName: "self", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto receiver } &&
        receiver.SemanticsKind == formation.SemanticsKind && receiver.SemanticsParameter is null && receiver.OriginName == "source" && receiver.OriginArguments is null && BareName(receiver.Type, "Self") &&
        BareType(function.ReturnType) is OriginApplicationKoto { ArgumentNodes.Count: 1 } result && BareName(result.ArgumentNodes[0], "source") &&
        BareType(result.Type) is MemberAccessKoto element && BareName(element.Left, "Self") && BareName(element.Right, "IteratorType");

    // SPEC 22.1.2.2: associate IteratorType is ::Kimi.LendingIterator; func intoIterator(self: Self) -> Self.IteratorType.
    // The IteratorType requirement's identity is checked after Binding.
    private bool ValidIntoIterable(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Name: "IntoIterable", HasIncompatibleBindingHeader: false, Members.Count: 1, ConstraintNodes.Count: 1, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
        declaration.ConstraintNodes[0] is IsKoto { IsAssociatedConstraint: true, IsNegated: false, AttributeChain: null } iterator && BareName(iterator.Left, "IteratorType") &&
        declaration.Members[0] is FunctionKoto { Name: "intoIterator", IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 1, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function &&
        function.Parameters[0] is { InternalName: "self", ExternalName: "self", DefaultValue: null, AttributeChain: null } receiver && BareName(receiver.Type, "Self") &&
        BareType(function.ReturnType) is MemberAccessKoto result && BareName(result.Left, "Self") && BareName(result.Right, "IteratorType");

    // SPEC 4.6.2, 4.6.4, 8.4.7: a closed Contract refining Equatable and Utf8Format, with Self is Copy and Self is Owned,
    // whose one requirement resolves the value against a length.
    private bool ValidPositionContract(BindingSymbol symbol, KimiDeclarationId id)
    {
        var range = id == KimiDeclarationId.PositionRange;
        return symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
            symbol.Declaration is ContractKoto { HasIncompatibleBindingHeader: false, Members.Count: 1, ConstraintNodes.Count: 2, Bases.Count: 2, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
            declaration.Name == (range ? "PositionRange" : "Position") && ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
            BareName(declaration.Bases[0], "Equatable") && BareName(declaration.Bases[1], "Utf8Format") &&
            SelfRequirement(declaration.ConstraintNodes[0], "Copy") && SelfRequirement(declaration.ConstraintNodes[1], "Owned") &&
            declaration.Members[0] is FunctionKoto { Name: "tryResolve", IsRequirement: true, IsGenerated: false, IsSpecialization: false, Parameters.Count: 2, GenericArguments.Count: 0, Origins.Count: 0, TypeConstraints.Count: 0, Body: null, ExpressionBody: null, AttributeChain: null } function &&
            function.NameBoundaryIndex < 0 &&
            function.Parameters[0] is { InternalName: "self", ExternalName: "self", DefaultValue: null, AttributeChain: null } receiver && BareName(receiver.Type, "Self") &&
            function.Parameters[1] is { InternalName: "length", ExternalName: "length", DefaultValue: null, AttributeChain: null } length && BareName(length.Type, "isize") &&
            BareType(function.ReturnType) is GenericsKoto { TypeArguments: [var resolved] } result && BareName(result.Identifier, "Option") &&
            BareName(resolved, range ? "ResolvedRange" : "isize");

        static bool SelfRequirement(IsKoto clause, string requirement)
            => clause is { IsAssociatedConstraint: false, IsNegated: false, FormationType: null, AttributeChain: null } && BareName(clause.Left, "Self") && BareName(clause.Right, requirement);
    }

    private bool ValidSlice(BindingSymbol symbol)
    {
        if (symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not StructKoto { Name: "Slice", HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 1, OriginNames.Count: 1, Bases.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } slice ||
            !ReferenceEquals(slice.Parent, this.Kotonoha.RootKoto) || slice.OriginNames[0] != "source" || !ValidEntryConformances(slice, 0) ||
            slice.GenericParameterNodes[0] is not GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } ||
            FindDeclaration(slice, "iterate", true) is not FunctionKoto)
        {
            return false;
        }

        for (var i = 0; i < slice.Members.Count; i++)
        {
            if (slice.Members[i] is IsKoto { IsAssociatedConstraint: true, IsNegated: false, FormationType: null, AttributeChain: null })
            {
                continue; // SPEC 22.1.2.3: the iterator Types of the standard entry conformances.
            }

            if (slice.Members[i] is not FunctionKoto)
            {
                return false; // Slice storage is compiler-managed; helpers cannot add fields.
            }
        }

        return true;
    }

    private bool ValidArray(BindingSymbol symbol) => this.ValidCollection(symbol, dictionary: false);

    private bool ValidDictionary(BindingSymbol symbol) => this.ValidCollection(symbol, dictionary: true);

    private bool ValidCollection(BindingSymbol symbol, bool dictionary)
    {
        if (symbol.Intrinsic != IntrinsicKind.None || !ReferenceEquals(symbol.Scope, this.Scope) ||
            symbol.Declaration is not StructKoto { HasIncompatibleBindingHeader: false, OriginNames.Count: 0, Bases.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } array ||
            array.Name != (dictionary ? "Dictionary" : "Array") || array.GenericParameterNodes.Count != (dictionary ? 2 : 1) || !ValidEntryConformances(array, dictionary ? 1 : 0) ||
            !ReferenceEquals(array.Parent, this.Kotonoha.RootKoto) ||
            array.GenericParameterNodes[0] is not GenericParameterKoto { SemanticsParameter: null, AttributeChain: null } first || first.Identifier != (dictionary ? "K" : "T") ||
            (dictionary && (array.GenericParameterNodes[1] is not GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null } ||
                array.ConstraintNodes.Count == 0 || !BareName(array.ConstraintNodes[0].Left, "K") || !BareName(array.ConstraintNodes[0].Right, "Equatable"))))
        {
            return false;
        }

        for (var i = 0; i < array.Members.Count; i++)
        {
            if (array.Members[i] is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed })
            {
                continue; // A computed Property has accessors but no storage.
            }

            if (array.Members[i] is IsKoto { IsAssociatedConstraint: true, IsNegated: false, FormationType: null, AttributeChain: null })
            {
                continue; // SPEC 22.1.2.3: the iterator Types of the standard entry conformances.
            }

            if (array.Members[i] is not FunctionKoto member)
            {
                return false; // Array storage is compiler-managed; helpers cannot add fields.
            }

            // A signature without a body is a catalog operation the compiler implements; helpers keep their bodies.
            if (member.Body is null && member.ExpressionBody is null &&
                (member.BoundSymbol is not { CompilerFunction: not CompilerFunctionKind.None } operation || !ReferenceEquals(operation.Declaration, member) || Array.IndexOf(this.registeredSymbols, operation) < 0))
            {
                this.InvalidDeclaration ??= member;
                return false;
            }
        }

        return true;
    }

    // SPEC 22.1.2.5: an internal remainder record of the standard storage boundary. The compiler writes its fields.
    private bool ValidRemainder(BindingSymbol symbol, KimiDeclarationId id)
        => symbol.Intrinsic == IntrinsicKind.None &&
        symbol.Declaration is StructKoto { HasIncompatibleBindingHeader: false, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Internal, AttributeChain: null, GenericParameterNodes: [GenericParameterKoto { Identifier: "E", SemanticsParameter: null, AttributeChain: null }] } declaration &&
        declaration.Name == symbol.Name && ReferenceEquals(declaration.Parent, this.StorageScope.Owner) &&
        declaration.OriginNames.Count == (id == KimiDeclarationId.OwnedRemainder ? 0 : 1) && (id == KimiDeclarationId.OwnedRemainder || declaration.OriginNames[0] == "source") &&
        StorageFields(declaration, id == KimiDeclarationId.OwnedRemainder) == (id == KimiDeclarationId.OwnedRemainder ? 4 : 3) &&
        ValidStorageField(declaration, 0, VariableKind.Let, "storage", "E", true) && ValidStorageField(declaration, 1, VariableKind.Var, "position", "isize", true) && ValidStorageField(declaration, 2, VariableKind.Var, "count", "isize", true) &&
        (id != KimiDeclarationId.OwnedRemainder || ValidStorageField(declaration, 3, VariableKind.Var, "capacity", "isize", true));

    // PLAN G33: InlineStorage<A> is an internal Storage struct with exactly one Field `value: A` and no destructor; the
    // compiler never destroys it, so its owner destroys the contents it still holds.
    private bool ValidInlineStorage(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None &&
        symbol.Declaration is StructKoto { HasIncompatibleBindingHeader: false, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Internal, AttributeChain: null, GenericParameterNodes: [GenericParameterKoto { Identifier: "A", SemanticsParameter: null, AttributeChain: null }] } declaration &&
        declaration.Name == "InlineStorage" && ReferenceEquals(declaration.Parent, this.StorageScope.Owner) && declaration.OriginNames.Count == 0 &&
        StorageFields(declaration) == 1 && ValidStorageField(declaration, 0, VariableKind.Var, "value", "A", true);

    // SPEC 22.1.2.5: a Dictionary remainder follows the slot links from `link` for `count` live entries.
    // The owning remainder also keeps the last unreturned entry's link and destroys the unreturned entries itself.
    private bool ValidDictionaryRemainder(BindingSymbol symbol, KimiDeclarationId id)
    {
        var owned = id == KimiDeclarationId.DictionaryOwnedRemainder;
        return symbol.Intrinsic == IntrinsicKind.None &&
            symbol.Declaration is StructKoto { HasIncompatibleBindingHeader: false, Bases.Count: 0, ConstraintNodes.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Internal, AttributeChain: null } declaration &&
            declaration.Name == (owned ? "DictionaryOwnedRemainder" : id == KimiDeclarationId.DictionaryUniqRemainder ? "DictionaryUniqRemainder" : "DictionaryRefRemainder") &&
            (owned ? declaration.OriginNames.Count == 0 : declaration.OriginNames is ["source"]) &&
            declaration.GenericParameterNodes is [GenericParameterKoto { Identifier: "K", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null }] &&
            ReferenceEquals(declaration.Parent, this.StorageScope.Owner) && StorageFields(declaration, owned) == (owned ? 5 : 4) &&
            ValidStorageField(declaration, 0, VariableKind.Let, "storage", "u8", true) && ValidStorageField(declaration, 1, VariableKind.Let, "stride", "isize", true) &&
            ValidStorageField(declaration, 2, VariableKind.Var, "link", "isize", true) &&
            (owned ? ValidStorageField(declaration, 3, VariableKind.Var, "tail", "isize", true) && ValidStorageField(declaration, 4, VariableKind.Var, "count", "isize", true)
                : ValidStorageField(declaration, 3, VariableKind.Var, "count", "isize", true));
    }

    // SPEC 22.1.2.5: bodiless internal operations over Dictionary<K, V> that the compiler implements: borrowStorage takes
    // the shared or exclusive Loan; the unsafe primitives lend a slot's key, or lend or split its value, for the source.
    private bool ValidDictionaryStorageOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        var (name, semantics, source, result, lent) = id switch
        {
            KimiDeclarationId.StorageBorrowDictionary => ("borrowStorage", SemanticsKind.Ref, "Dictionary", "DictionaryRefRemainder", SemanticsKind.Owner),
            KimiDeclarationId.StorageBorrowDictionaryExclusive => ("borrowStorage", SemanticsKind.Uniq, "Dictionary", "DictionaryUniqRemainder", SemanticsKind.Owner),
            KimiDeclarationId.StorageLendKey => ("lendKey", SemanticsKind.Ref, "DictionaryRefRemainder", "K", SemanticsKind.Ref),
            KimiDeclarationId.StorageLendValue => ("lendValue", SemanticsKind.Ref, "DictionaryRefRemainder", "V", SemanticsKind.Ref),
            KimiDeclarationId.StorageLendUniqKey => ("lendKey", SemanticsKind.Ref, "DictionaryUniqRemainder", "K", SemanticsKind.Ref),
            KimiDeclarationId.StorageSplitValue => ("splitValue", SemanticsKind.Uniq, "DictionaryUniqRemainder", "V", SemanticsKind.Uniq),
            KimiDeclarationId.StorageOwnDictionary => ("ownStorage", SemanticsKind.Owner, "Dictionary", "DictionaryOwnedRemainder", SemanticsKind.Owner),
            KimiDeclarationId.StorageKeyAt => ("keyAt", SemanticsKind.Unsafe, "u8", "K", SemanticsKind.Unsafe),
            _ => ("valueAt", SemanticsKind.Unsafe, "u8", "V", SemanticsKind.Unsafe),
        };
        var borrow = lent == SemanticsKind.Owner;
        var owning = semantics == SemanticsKind.Owner;
        if (semantics == SemanticsKind.Unsafe)
        {
            // keyAt and valueAt take only the slot; the caller names K and V.
            return symbol.CompilerFunction == KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function &&
                symbol.Declaration is FunctionKoto { AttributeChain: null, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, TypeConstraints.Count: 0 } addressing &&
                ReferenceEquals(addressing.Parent, this.StorageScope.Owner) && addressing.Name == name && addressing.Modifier == (ModifierKind.Internal | ModifierKind.Unsafe) &&
                addressing.GenericArguments is [GenericParameterKoto { Identifier: "K", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null }] &&
                addressing.Parameters is [{ InternalName: "slot", ExternalName: "slot", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe } slotType }] && BareName(slotType.Type, source) &&
                BareType(addressing.ReturnType) is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe, OriginExpression: null } address && BareName(address.Type, result);
        }

        if (symbol.CompilerFunction != KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function ||
            symbol.Declaration is not FunctionKoto function || !ReferenceEquals(function.Parent, this.StorageScope.Owner) || function.Name != name ||
            function.Modifier != (borrow ? ModifierKind.Internal : ModifierKind.Internal | ModifierKind.Unsafe) || function.AttributeChain is not null ||
            function.GenericArguments is not [GenericParameterKoto { Identifier: "K", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null }] ||
            function.Parameters.Count != (borrow ? 1 : 2) || function.ReturnType is null ||
            function.Body is not null || function.ExpressionBody is not null || function.IsRequirement || function.IsGenerated || function.IsSpecialization ||
            function.Parameters[0] is not { DefaultValue: null, AttributeChain: null } parameter ||
            (owning ? BareType(parameter.Type) : parameter.Type is TypeSemanticsKoto reference && reference.SemanticsKind == semantics ? reference.Type : null) is not GenericsKoto { TypeArguments: [var first, var second] } input ||
            parameter.InternalName != (borrow ? "value" : "state") || parameter.ExternalName != parameter.InternalName ||
            !BareName(input.Identifier, source) || !BareName(first, "K") || !BareName(second, "V"))
        {
            return false;
        }

        if (borrow)
        {
            // Dictionary<K, V> is formed only under its Equatable key requirement.
            return function.TypeConstraints is [IsKoto { IsNegated: false } key] && BareName(key.Left, "K") && BareName(key.Right, "Equatable") &&
                (!owning && function.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Owner, OriginName: "a", Type: { } during } ? during : BareType(function.ReturnType)) is GenericsKoto { TypeArguments: [var resultKey, var resultValue] } remainder &&
                BareName(remainder.Identifier, result) && BareName(resultKey, "K") && BareName(resultValue, "V");
        }

        return function.TypeConstraints.Count == 0 &&
            function.Parameters[1] is { InternalName: "slot", ExternalName: "slot", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe } pointer } && BareName(pointer.Type, "u8") &&
            BareType(function.ReturnType) is TypeSemanticsKoto element && element.SemanticsKind == lent && BareName(element.Type, result) &&
            (lent == SemanticsKind.Unsafe ? element.OriginExpression is null : element.OriginExpression is MemberAccessKoto origin && BareName(origin.Left, "state") && BareName(origin.Right, "source"));
    }

    // SPEC 22.1.2.5: borrowStorage over ref/[N of E] or uniq/[N of E] during a returns the contiguous RefRemainder<E> or
    // UniqRemainder<E> during a; the compiler implements it.
    private bool ValidFixedStorageOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        var exclusive = id == KimiDeclarationId.StorageBorrowFixedExclusive;
        return symbol.CompilerFunction == KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function &&
            symbol.Declaration is FunctionKoto { Name: "borrowStorage", AttributeChain: null, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, TypeConstraints.Count: 0, Modifier: ModifierKind.Internal } function &&
            ReferenceEquals(function.Parent, this.StorageScope.Owner) &&
            function.GenericArguments is [GenericParameterKoto { Identifier: "E", SemanticsParameter: null, AttributeChain: null }, LengthParameterKoto] &&
            function.Parameters is [{ InternalName: "value", ExternalName: "value", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { OriginName: "a", Type: FixedArrayTypeKoto array } input }] &&
            input.SemanticsKind == (exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref) && BareName(array.ElementType, "E") && BareName(array.Length, "N") &&
            function.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Owner, OriginName: "a", Type: GenericsKoto { TypeArguments: [var element] } result } &&
            BareName(result.Identifier, exclusive ? "UniqRemainder" : "RefRemainder") && BareName(element, "E");
    }

    // PLAN G33: ownStorage over a consumed [N of E] returns FixedOwnedRemainder<E, [N of E]>, and the unsafe inlineBase
    // publishes the element address of an exclusively borrowed InlineStorage<A> as unsafe/E; the compiler implements both.
    private bool ValidFixedOwningOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (symbol.CompilerFunction != KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function ||
            symbol.Declaration is not FunctionKoto { AttributeChain: null, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, TypeConstraints.Count: 0 } function ||
            !ReferenceEquals(function.Parent, this.StorageScope.Owner))
        {
            return false;
        }

        if (id == KimiDeclarationId.StorageOwnFixed)
        {
            return function is { Name: "ownStorage", Modifier: ModifierKind.Internal } &&
                function.GenericArguments is [GenericParameterKoto { Identifier: "E", SemanticsParameter: null, AttributeChain: null }, LengthParameterKoto] &&
                function.Parameters is [{ InternalName: "value", ExternalName: "value", DefaultValue: null, AttributeChain: null } parameter] &&
                BareType(parameter.Type) is FixedArrayTypeKoto input && BareName(input.ElementType, "E") && BareName(input.Length, "N") &&
                BareType(function.ReturnType) is GenericsKoto { TypeArguments: [var element, var storedType] } result && BareType(storedType) is FixedArrayTypeKoto owned && BareName(result.Identifier, "FixedOwnedRemainder") &&
                BareName(element, "E") && BareName(owned.ElementType, "E") && BareName(owned.Length, "N");
        }

        return function is { Name: "inlineBase", Modifier: ModifierKind.Internal | ModifierKind.Unsafe } &&
            function.GenericArguments is [GenericParameterKoto { Identifier: "E", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "A", SemanticsParameter: null, AttributeChain: null }] &&
            function.Parameters is [{ InternalName: "storage", ExternalName: "storage", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, OriginName: null, OriginExpression: null, Type: GenericsKoto { TypeArguments: [var stored] } storage } }] &&
            BareName(storage.Identifier, "InlineStorage") && BareName(stored, "A") &&
            function.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe, OriginName: null, OriginExpression: null } pointer && BareName(pointer.Type, "E");
    }

    // SPEC 22.1.2.5: a bodiless internal generic operation over Array<E>; the compiler implements it. The borrowing
    // operations take a reference to the Array or remainder; ownStorage takes the Array by value.
    private bool ValidStorageOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        var kind = KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function;
        var capability = id is KimiDeclarationId.StorageLend or KimiDeclarationId.StorageSplit;
        var release = id == KimiDeclarationId.StorageRelease;
        var (name, parameterName, semantics, argument) = id switch
        {
            KimiDeclarationId.StorageBorrowShared => ("borrowStorage", "value", SemanticsKind.Ref, "Array"),
            KimiDeclarationId.StorageBorrowExclusive => ("borrowStorage", "value", SemanticsKind.Uniq, "Array"),
            KimiDeclarationId.StorageLend => ("lend", "state", SemanticsKind.Ref, "RefRemainder"),
            KimiDeclarationId.StorageSplit => ("split", "state", SemanticsKind.Uniq, "UniqRemainder"),
            KimiDeclarationId.StorageOwn => ("ownStorage", "value", SemanticsKind.Owner, "Array"),
            _ => ("release", "storage", SemanticsKind.Unsafe, "E"),
        };
        if (symbol.CompilerFunction != kind ||
            symbol.Declaration is not FunctionKoto function || !ReferenceEquals(function.Parent, this.StorageScope.Owner) ||
            function.Name != name || function.Modifier != (capability || release ? ModifierKind.Internal | ModifierKind.Unsafe : ModifierKind.Internal) || function.AttributeChain is not null ||
            function.GenericArguments is not [GenericParameterKoto { Identifier: "E", SemanticsParameter: null, AttributeChain: null }] ||
            function.Parameters.Count != (capability ? 2 : 1) || function.TypeConstraints.Count != 0 || (release ? function.ReturnType is not null : function.ReturnType is null) ||
            function.Body is not null || function.ExpressionBody is not null || function.IsRequirement || function.IsGenerated || function.IsSpecialization ||
            function.Parameters[0] is not { DefaultValue: null, AttributeChain: null } parameter || parameter.InternalName != parameterName || parameter.ExternalName != parameter.InternalName)
        {
            return false;
        }

        if (capability && (function.Parameters[1] is not { InternalName: "element", ExternalName: "element", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe } pointer } || !BareName(pointer.Type, "E") ||
            BareType(function.ReturnType) is not TypeSemanticsKoto result || result.SemanticsKind != semantics || !BareName(result.Type, "E") ||
            result.OriginExpression is not MemberAccessKoto origin || !BareName(origin.Left, "state") || !BareName(origin.Right, "source")))
        {
            return false;
        }

        if (release)
        {
            return BareType(parameter.Type) is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe } releasePointer && BareName(releasePointer.Type, "E");
        }

        return semantics == SemanticsKind.Owner
            ? BareType(parameter.Type) is GenericsKoto { TypeArguments.Count: 1 } owned && BareName(owned.Identifier, argument)
            : BareType(parameter.Type) is TypeSemanticsKoto { Type: GenericsKoto { TypeArguments.Count: 1 } borrowed } reference && reference.SemanticsKind == semantics && BareName(borrowed.Identifier, argument);
    }

    // SPEC 4.6.2: public struct FromEnd<T> under T is PrimitiveInteger with its T offset; prefix ^ constructs it through
    // PositionSyntax.fromEnd, and a directly applied `^x` key reads only the operand.
    private bool ValidFromEnd(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is StructKoto { Name: "FromEnd", HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 1, OriginNames.Count: 0, Bases.Count: 0, ConstraintNodes.Count: >= 2, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
        declaration.GenericParameterNodes[0] is GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } &&
        BareName(declaration.ConstraintNodes[0].Left, "T") && BareName(declaration.ConstraintNodes[0].Right, "PrimitiveInteger") &&
        ValidField(declaration, FirstStorage(declaration), "offset", "T") && this.ValidPositionSyntax("fromEnd");

    // SPEC 4.6.2, 4.6.8: public zero-sized structs Start and End, which omitted range boundaries construct.
    private bool ValidBoundary(BindingSymbol symbol, KimiDeclarationId id)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is StructKoto { HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 0, OriginNames.Count: 0, Bases.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        declaration.Name == (id == KimiDeclarationId.Start ? "Start" : "End") && ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
        OnlyFunctionsFrom(declaration, FirstStorage(declaration));

    // SPEC 4.6.3.2: public structs Range<S, E> and ClosedRange<S, E> under S is Position and E is Position with their start
    // and end fields; range syntax constructs them through the PositionSyntax functions of its shape.
    private bool ValidRange(BindingSymbol symbol, KimiDeclarationId id)
    {
        var closed = id == KimiDeclarationId.ClosedRange;
        return symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
            symbol.Declaration is StructKoto { HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 2, OriginNames.Count: 0, Bases.Count: 0, ConstraintNodes.Count: >= 3, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
            declaration.Name == (closed ? "ClosedRange" : "Range") && ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
            declaration.GenericParameterNodes[0] is GenericParameterKoto { Identifier: "S", SemanticsParameter: null, AttributeChain: null } &&
            declaration.GenericParameterNodes[1] is GenericParameterKoto { Identifier: "E", SemanticsParameter: null, AttributeChain: null } &&
            BareName(declaration.ConstraintNodes[0].Left, "S") && BareName(declaration.ConstraintNodes[0].Right, "Position") &&
            BareName(declaration.ConstraintNodes[1].Left, "E") && BareName(declaration.ConstraintNodes[1].Right, "Position") &&
            FirstStorage(declaration) is var first && ValidField(declaration, first, "start", "S") && ValidField(declaration, first + 1, "end", "E") &&
            (closed ? this.ValidPositionSyntax("through") && this.ValidPositionSyntax("upTo") :
                this.ValidPositionSyntax("between") && this.ValidPositionSyntax("from") && this.ValidPositionSyntax("to") && this.ValidPositionSyntax("all"));
    }

    // A PositionSyntax function the compiler calls by declaration: one internal, non-overloaded group function.
    private bool ValidPositionSyntax(string name)
        => this.PositionSyntax is { } group && FindDeclaration(group, name, true) is FunctionKoto { IsSpecialization: false, NameBoundaryIndex: < 0 } function &&
        ReferenceEquals(function.Parent, group);

    // SPEC 4.6.3.3: public struct ResolvedRange with isize start and end; its init(! start, end) is internal, because only
    // indices, resolve and tryResolve produce a validated interval.
    private bool ValidResolvedRange(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is StructKoto { Name: "ResolvedRange", HasIncompatibleBindingHeader: false, GenericParameterNodes.Count: 0, OriginNames.Count: 0, Bases.Count: 0, ConstraintNodes.Count: >= 1, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
        BareName(declaration.ConstraintNodes[0].Left, "Self") && BareName(declaration.ConstraintNodes[0].Right, "Copy") &&
        FirstStorage(declaration) is var first && // The associated iterator Types precede the fields.
        ValidField(declaration, first, "start", "isize") && ValidField(declaration, first + 1, "end", "isize") &&
        declaration.Members.Count > first + 4 && declaration.Members[first + 4] is FunctionKoto { IsConstructor: true, Modifier: ModifierKind.Internal, Parameters.Count: 2, NameBoundaryIndex: 0, ReturnType: null, Body: not null, AttributeChain: null } constructor && // The computed length and isEmpty precede it (STYLE 2.2).
        constructor.Parameters[0] is { InternalName: "start", ExternalName: "start", DefaultValue: null } startParameter && BareName(startParameter.Type, "isize") &&
        constructor.Parameters[1] is { InternalName: "end", ExternalName: "end", DefaultValue: null } endParameter && BareName(endParameter.Type, "isize");

    // SPEC 4.7.2, 4.7.4: the bodiless signature public init(! capacity: isize); the compiler allocates the Array at each construction.
    private bool ValidArrayConstructor(BindingSymbol symbol)
        => symbol.CompilerFunction == CompilerFunctionKind.ArrayWithCapacity &&
        symbol.Declaration is FunctionKoto { IsConstructor: true, Modifier: ModifierKind.Public, NameBoundaryIndex: 0, ReturnType: null, Body: null, ExpressionBody: null, AttributeChain: null, Parameters.Count: 1 } function &&
        ReferenceEquals(function.Parent, this.ArrayScope.Owner) && function.GenericArguments.Count == 0 && function.Origins.Count == 0 && function.TypeConstraints.Count == 0 &&
        function.Parameters[0] is { ExternalName: "capacity", InternalName: "capacity", DefaultValue: null, AttributeChain: null } parameter && BareName(parameter.Type, "isize");

    // SPEC 4.7.2, 4.7.4: an exclusive receiver, isize positions, T inputs and T or Option<T> results; the isize position
    // operations are internal, and the public position entries of Array resolve a position before calling them.
    private bool ValidArrayOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        var (kind, name) = id switch
        {
            KimiDeclarationId.ArrayReserve => (CompilerFunctionKind.ArrayReserve, "reserve"),
            KimiDeclarationId.ArrayAppend => (CompilerFunctionKind.ArrayAppend, "append"),
            KimiDeclarationId.ArrayInsert => (CompilerFunctionKind.ArrayInsert, "insertAt"),
            KimiDeclarationId.ArrayPop => (CompilerFunctionKind.ArrayPop, "pop"),
            KimiDeclarationId.ArrayRemove => (CompilerFunctionKind.ArrayRemove, "removeAt"),
            KimiDeclarationId.ArraySwap => (CompilerFunctionKind.ArraySwap, "swapAt"),
            KimiDeclarationId.ArrayClear => (CompilerFunctionKind.ArrayClear, "clear"),
            _ => (CompilerFunctionKind.ArrayShrinkToFit, "shrinkToFit"),
        };
        if (symbol.CompilerFunction != kind || symbol.Declaration is not FunctionKoto function || !ReferenceEquals(function.Parent, this.ArrayScope.Owner) ||
            function.NameBoundaryIndex >= 0 || function.Name != name ||
            function.Modifier != (id is KimiDeclarationId.ArrayInsert or KimiDeclarationId.ArrayRemove or KimiDeclarationId.ArraySwap ? ModifierKind.Internal : ModifierKind.Public) ||
            function.GenericArguments.Count != 0 || function.Origins.Count != 0 || function.TypeConstraints.Count != 0 ||
            function.Body is not null || function.ExpressionBody is not null || function.AttributeChain is not null ||
            function.IsRequirement || function.IsGenerated || function.IsSpecialization || function.Parameters.Count == 0 ||
            function.Parameters[0] is not { ExternalName: "self", InternalName: "self", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null } receiver } ||
            !BareName(receiver.Type, "Self"))
        {
            return false;
        }

        var inputs = function.Parameters.Count - 1;
        return id switch
        {
            KimiDeclarationId.ArrayReserve => inputs == 1 && Input(function, 1, "additional", "isize") && function.ReturnType is null,
            KimiDeclarationId.ArrayAppend => inputs == 1 && Input(function, 1, "value", "T") && function.ReturnType is null,
            KimiDeclarationId.ArrayInsert => inputs == 2 && Input(function, 1, "index", "isize") && Input(function, 2, "value", "T") && function.ReturnType is null,
            KimiDeclarationId.ArrayPop => inputs == 0 && BareType(function.ReturnType) is GenericsKoto { TypeArguments.Count: 1 } option && BareName(option.Identifier, "Option") && BareName(option.TypeArguments[0], "T"),
            KimiDeclarationId.ArrayRemove => inputs == 1 && Input(function, 1, "index", "isize") && BareName(function.ReturnType, "T"),
            KimiDeclarationId.ArraySwap => inputs == 2 && Input(function, 1, "first", "isize") && Input(function, 2, "second", "isize") && function.ReturnType is null,
            _ => inputs == 0 && function.ReturnType is null,
        };

        static bool Input(FunctionKoto function, int index, string name, string type)
            => function.Parameters[index] is { DefaultValue: null, AttributeChain: null } parameter && parameter.ExternalName == name && parameter.InternalName == name && BareName(parameter.Type, type);
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
        this.MakeObj.Declaration is FunctionKoto { Name: "makeObj", NameBoundaryIndex: -1, Modifier: ModifierKind.Public, AttributeChain: null, GenericArguments.Count: 1, Parameters.Count: 1, Origins.Count: 0, TypeConstraints.Count: 1, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false } f &&
        f.GenericArguments[0] is GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null } &&
        f.TypeConstraints[0] is IsKoto { Left: { } constrained, Right: { } required } && BareName(constrained, "T") && BareName(required, "ObjectPayload") && // SPEC 13.5.8

        f.Parameters[0] is { InternalName: "value", ExternalName: "value", DefaultValue: null, AttributeChain: null } p &&
        BareName(p.Type, "T") && f.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Obj, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: { } inner } && BareName(inner, "T");

    private bool Valid(BindingSymbol symbol, IntrinsicKind kind)
        => symbol.Intrinsic == kind && ReferenceEquals(symbol.Scope, this.Scope) &&
        symbol.Declaration is ContractKoto { Members.Count: 0, ConstraintNodes.Count: 0, Bases.Count: 0, GenericParameterNodes.Count: 0, OriginNames.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
        declaration.Name == symbol.Name && ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto);
}
