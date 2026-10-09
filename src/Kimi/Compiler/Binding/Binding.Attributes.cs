// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 22.3.1: whether a function carries the import Attribute; its validity is checked separately.
    internal static bool IsLibraryImport(FunctionKoto function)
    {
        for (var attribute = function.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (attribute.IdentifierKoto is IdentifierNameKoto { IdentifierName: "LibraryImport" })
            {
                return true;
            }
        }

        return false;
    }

    private static Koto? AttributeTarget(AttributeKoto attribute)
    {
        var target = attribute.Parent;
        while (target is AttributeKoto)
        {
            target = target.Parent;
        }

        return target;
    }

    private static bool HasCLayout(StructKoto structure)
    {
        for (var attribute = structure.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
        {
            if (attribute.LayoutMode == "C")
            {
                return true;
            }
        }

        return false;
    }

    private void IndexLayoutAttribute(AttributeKoto attribute)
    {
        attribute.BindingFailure = BindingFailure.None;
        attribute.BindingState = BindingState.Unvisited;
        attribute.BoundType = null;
        this.nodes.Add(attribute);
        var target = AttributeTarget(attribute);

        if (target is not StructKoto || attribute.LayoutMode is null)
        {
            this.Fail(attribute, BindingFailure.InvalidLayoutAttribute);
            if (target is not null)
            {
                this.Fail(target, BindingFailure.InvalidTypeFormation);
            }
        }
        else
        {
            Complete(attribute, BoundType.Unit);
        }
    }

    // SPEC 22.3: validate each selected import declaration and its defining module's
    // native requirement (SPEC 20.8.2.1) without reading native files.
    private void ValidateLibraryImports()
    {
        string? target = null;
        var symbols = this.importSymbols;
        symbols.Clear();
        var ordinal = 0;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is not AttributeKoto { IdentifierKoto: IdentifierNameKoto { IdentifierName: "LibraryImport" } } attribute ||
                AttributeTarget(attribute) is not FunctionKoto function)
            {
                continue;
            }

            if (function.Body is not null || function.ExpressionBody is not null || !IsImportShape(function) || RepeatedImport(function) ||
                attribute.Operand is not InvocationKoto { ArgumentNodes.Count: 2 } call ||
                !IsImportName(call, 0, out var name) || !IsImportName(call, 1, out var symbol) || IsReservedExternalName(symbol))
            {
                this.Fail(attribute, BindingFailure.InvalidLibraryImport);
                continue;
            }

            // SPEC 20.8.2.1/20.8.2.4: the Kind of the defining module's requirement, after self-targeted
            // supply expansion, decides dllimport generation; reserved supplies have their fixed kinds.
            string? kind = null;
            if (name is Kernel32Imports.LibraryName)
            {
                kind = "import"; // The generated kernel32 import library (SPEC 20.8.2.4).
                if (Array.IndexOf(Kernel32Imports.Symbols, symbol) < 0)
                {
                    // SPEC 20.8.2.4: the reserved supply exports only the reviewed project-owned definition.
                    this.Fail(attribute, BindingFailure.UnavailableReservedImport);
                }
            }
            else if (name is WindowsProfile.BackendLibrary)
            {
                kind = "static"; // The compiler-managed backend archive (SPEC 20.8.2.4).
                if (Array.IndexOf(WindowsProfile.ProvidedSymbols, symbol) < 0)
                {
                    // SPEC 21.5.7: the backend archive supplies only its catalog, whose names are reserved above.
                    this.Fail(attribute, BindingFailure.UnavailableReservedImport);
                }
            }
            else
            {
                target ??= this.compilation.TargetTriple.ToString();
                var configuration = this.compilation.Configuration(attribute.CodeContext.Kotonoha);
                var requirement = configuration is not null && configuration.NativeRequirements.TryGetValue(target, out var requirements) ? requirements.GetValueOrDefault(name) : null;
                var supply = configuration is not null && configuration.NativeLibraries.TryGetValue(target, out var supplies) ? supplies.GetValueOrDefault(name) : null;
                if (requirement is null && supply is null)
                {
                    this.Fail(attribute, BindingFailure.MissingNativeRequirement);
                }
                else
                {
                    kind = requirement?.Kind ?? supply?.Kind;
                }
            }

            if (this.ImportAbi(function, ordinal++, out var signature) is var failure and not BindingFailure.None)
            {
                this.Fail(attribute, failure);
            }
            else if (signature is not null)
            {
                // SPEC 21.5.2/22.5.6: the runtime's own kernel32 declarations share the same final symbol
                // table; an import joins one only through the reserved kernel32 supply with an equal
                // physical Type, and never a declaration carrying an inexpressible ABI attribute.
                if (IsRuntimeDeclaration(symbol, out var declared) && (name != Kernel32Imports.LibraryName || declared != signature))
                {
                    this.Fail(attribute, BindingFailure.ConflictingRuntimeSymbol);
                }

                // SPEC 21.5.2: one final symbol table; same-named declarations share only an equal physical
                // Type and dllimport setting. An unresolved kind already has its own diagnostic.
                if (!symbols.TryAdd(symbol, (signature, kind)))
                {
                    var previous = symbols[symbol];
                    if (previous.Signature != signature)
                    {
                        this.Fail(attribute, BindingFailure.ConflictingImportSignature);
                    }
                    else if (kind is not null && previous.Kind is not null && previous.Kind != kind)
                    {
                        this.Fail(attribute, BindingFailure.ConflictingImportSupply);
                    }
                }
            }

            // A failure above already made the attribute Invalid; Complete keeps that state.
            Complete(attribute, BoundType.Unit);
            if (attribute.BindingState == BindingState.Resolved && signature is not null && kind is not null)
            {
                this.libraryImports.Add(new(function, name, symbol, kind));
            }
        }

        // SPEC 22.3.1: a direct group or receiverless struct type function without
        // generic/Origin parameters, specializations or argument defaults.
        static bool IsImportShape(FunctionKoto function)
        {
            // Safe or unsafe; borrow annotations introduce the import's own signature Origins (SPEC 22.3.1).
            if (function.BoundSymbol is not { ReceiverIndex: < 0 } symbol ||
                symbol.Scope.Owner is not (GroupKoto or StructKoto) || function.GenericArguments.Count != 0 ||
                function.IsSpecialization || function.IsConstructor || function.IsDestructor || function.IsAnonymous || function.IsRequirement)
            {
                return false;
            }

            for (var p = 0; p < function.Parameters.Count; p++)
            {
                if (function.Parameters[p].DefaultValue is not null)
                {
                    return false;
                }
            }

            // Enclosing generic/Origin parameters would parameterize the import as well.
            for (var container = symbol.Scope.Owner as DeclarationContainerKoto; container is not null; container = container.Parent as DeclarationContainerKoto)
            {
                if (container.GenericParameterNodes.Count != 0 || container.OriginNames.Count != 0)
                {
                    return false;
                }
            }

            return true;
        }

        // SPEC 22.3.1: one declaration selects one external symbol, so a repeated import is invalid.
        static bool RepeatedImport(FunctionKoto function)
        {
            var imports = 0;
            for (var attribute = function.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
            {
                if (attribute.IdentifierKoto is IdentifierNameKoto { IdentifierName: "LibraryImport" } && ++imports > 1)
                {
                    return true;
                }
            }

            return false;
        }

        // SPEC 22.5.6 declarations generated beside the runtime; the signature is null when none can agree.
        static bool IsRuntimeDeclaration(string symbol, out string? declared)
        {
            foreach (var declaration in WindowsProfile.RuntimeDeclarations)
            {
                if (string.Equals(declaration.Symbol, symbol, StringComparison.Ordinal))
                {
                    declared = declaration.Signature;
                    return true;
                }
            }

            declared = null;
            return false;
        }

        // SPEC 21.5.2 reserves compiler, LLVM and profile-supply external names; the supply names
        // are the profile's own catalog, so the two cannot drift apart.
        static bool IsReservedExternalName(string symbol)
            => symbol.StartsWith("__kimi_", StringComparison.Ordinal) || symbol.StartsWith("llvm.", StringComparison.Ordinal) ||
                symbol == WindowsProfile.FloatMarker || Array.IndexOf(WindowsProfile.ProvidedSymbols, symbol) >= 0;

        static bool IsImportName(InvocationKoto call, int index, out string value)
        {
            value = call.ArgumentNodes[index] is StringLiteralKoto literal && call.GetArgumentLabel(index) is null ? literal.Literal : string.Empty;
            return value.Length != 0 && !value.Contains('\0');
        }
    }

    // SPEC 22.3.2: the initial Windows C ABI accepts fixed-width integers, f32/f64 and raw
    // pointers, with Unit only as a result. The signature is one physical code per result and
    // parameter (i8/u8 share i8, every raw/T is ptr); it is null when a Type failed to bind,
    // since that Type already has its own diagnostic. A nonnull Option of a borrow is permitted
    // but its representation is not laid out, so it is unsupported.
    private BindingFailure ImportAbi(FunctionKoto function, int ordinal, out string? signature)
    {
        signature = null;
        if (ordinal == this.importSignatures.Count)
        {
            this.importSignatures.Add(null);
        }

        var count = function.Parameters.Count + 1;
        Span<char> codes = count <= 64 ? stackalloc char[count] : new char[count];
        var complete = true;
        var result = function.BoundSymbol!.Type;
        if (result is null)
        {
            complete = false;
        }
        else if (ReferenceEquals(result, BoundType.Unit))
        {
            codes[0] = 'v';
        }
        else if ((codes[0] = this.PhysicalCode(result)) is '\0' or '?')
        {
            return codes[0] == '?' ? BindingFailure.Unsupported : BindingFailure.InvalidImportSignature;
        }

        for (var p = 1; p < count; p++)
        {
            if (this.symbols[function.Parameters[p - 1]].Type is not { } type)
            {
                complete = false;
            }
            else if ((codes[p] = this.PhysicalCode(type)) is '\0' or '?')
            {
                return codes[p] == '?' ? BindingFailure.Unsupported : BindingFailure.InvalidImportSignature;
            }
        }

        if (complete)
        {
            var cached = this.importSignatures[ordinal];
            signature = codes.SequenceEqual(cached.AsSpan()) ? cached : new string(codes);
        }

        this.importSignatures[ordinal] = signature;
        return BindingFailure.None;
    }

    // SPEC 22.3.2: a raw pointer, or a ref/uniq borrow of a C-exchangeable referent, passes as a pointer; scalars by width. A nonnull
    // Option of such a borrow is '?', unsupported.
    private char PhysicalCode(BoundType type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components.Count: 1 } || this.IsImportBorrow(type) ? 'p' :
            ReferenceEquals(type.Symbol, this.Library.Option) && type.Components is [var inner] && this.IsImportBorrow(inner) ? '?' :
            type.Kind != BoundTypeKind.Primitive ? '\0' :
            type.Underlying.Name switch
            {
                "i8" or "u8" => '1',
                "i16" or "u16" => '2',
                "i32" or "u32" => '4',
                "i64" or "u64" => '8',
                "f32" => 'f',
                "f64" => 'd',
                _ => '\0',
            };

    private bool IsImportBorrow(BoundType type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var referent] } && this.CExchangeable(referent, 0);

    // IMPL 21.1.6: the initially C-exchangeable storage: owned fixed-width integers and f32/f64, raw pointers, ref/uniq borrows of
    // eligible referents, positive-length fixed arrays of eligible elements and owned C-layout structs with eligible Fields.
    // The nonnull Option representation of a borrow is not yet laid out, so Option<ref/T> is not admitted.
    private bool CExchangeable(BoundType type, int depth)
    {
        if (depth > 32)
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Primitive)
        {
            return type.Underlying.Name is "i8" or "u8" or "i16" or "u16" or "i32" or "u32" or "i64" or "u64" or "f32" or "f64";
        }

        if (type is { Kind: BoundTypeKind.Semantics, Components: [var referent] })
        {
            return type.Semantics == SemanticsKind.Raw || (type.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq && this.CExchangeable(referent, depth + 1));
        }

        if (type is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner, Length: > 0, Components: [var element] })
        {
            return this.CExchangeable(element, depth + 1);
        }

        if (type.Semantics != SemanticsKind.Owner || type.Symbol?.Declaration is not StructKoto structure || !HasCLayout(structure) ||
            !this.storageShapes.TryGetValue(structure, out var shape) || shape.Types.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < shape.Types.Count; i++)
        {
            if (this.StoredType(shape.Types[i], type) is not { } field || !this.CExchangeable(field, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 21.1: direct zero-sized Fields reject C layout. Struct sizes need prepared storage
    // shapes; a Field whose size depends on a Type argument is checked when its layout is formed.
    private void ValidateCLayoutFields()
    {
        foreach (var container in this.cLayouts)
        {
            for (var m = 0; m < container.Members.Count; m++)
            {
                if (container.Members[m] is PropertyKoto field && IsStoredVariable(field) && (field.Modifier & ModifierKind.Static) == 0 &&
                    field.BoundSymbol?.Property?.Type is { } type && this.HasZeroStride(type))
                {
                    this.Fail(field, BindingFailure.InvalidCLayout);
                    this.Fail(container, BindingFailure.InvalidTypeFormation);
                }
            }
        }

        this.storagePrepared = true;
        foreach (var (syntax, type) in this.cLayoutInstances)
        {
            this.CheckCLayoutInstance(syntax, type);
        }
    }

    // SPEC 21.1: a written instantiation of a generic C-layout struct, such as Pair<()>,
    // must not make a direct Field zero-sized. Inferred instantiations reject at generation.
    private void CheckCLayoutInstance(GenericsKoto syntax, BoundType type)
    {
        if (!this.storagePrepared)
        {
            this.cLayoutInstances.Add((syntax, type));
            return;
        }

        if (type.Symbol?.Declaration is StructKoto declaration && this.storageShapes.TryGetValue(declaration, out var shape))
        {
            foreach (var field in shape.Types)
            {
                if (this.StoredType(field, type) is { } stored && this.HasZeroStride(stored))
                {
                    this.Fail(syntax, BindingFailure.InvalidCLayout);
                    return;
                }
            }
        }
    }

    private void ValidateLayoutFragments()
    {
        this.cLayouts.Clear();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is not StructKoto container)
            {
                continue;
            }

            string? mode = null;
            AttributeKoto? latestSpecification = null;
            var previousFragment = -1;
            var valid = true;
            for (var attribute = container.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
            {
                if (attribute.IdentifierKoto is not IdentifierNameKoto { IdentifierName: "Layout" })
                {
                    continue;
                }

                if (previousFragment == attribute.FragmentOrdinal)
                {
                    this.Fail(attribute, BindingFailure.InvalidLayoutAttribute);
                }

                valid &= attribute.BindingState != BindingState.Invalid;

                previousFragment = attribute.FragmentOrdinal;
                if (attribute.LayoutMode is { } explicitMode)
                {
                    if (mode is not null && mode != explicitMode)
                    {
                        this.Fail(latestSpecification!, BindingFailure.ConflictingLayout);
                        valid = false;
                    }
                    else if (mode is null)
                    {
                        mode = explicitMode;
                        latestSpecification = attribute;
                    }
                }
            }

            var storageFragment = -1;
            var instanceFields = 0;
            for (var m = 0; mode == "C" && m < container.Members.Count; m++)
            {
                if (container.Members[m] is PropertyKoto field && IsStoredVariable(field))
                {
                    if (storageFragment >= 0 && storageFragment != field.FragmentOrdinal)
                    {
                        this.Fail(field, BindingFailure.SplitCLayoutStorage);
                        valid = false;
                    }

                    storageFragment = field.FragmentOrdinal;
                    instanceFields += (field.Modifier & ModifierKind.Static) == 0 ? 1 : 0;
                }
            }

            // SPEC 21.1: C layout rejects open, derived and empty structs.
            if (mode == "C" && ((container.Modifier & ModifierKind.Open) != 0 || container.Bases.Count != 0 || instanceFields == 0))
            {
                this.Fail(latestSpecification!, BindingFailure.InvalidCLayout);
                valid = false;
            }
            else if (mode == "C" && valid)
            {
                this.cLayouts.Add(container);
            }

            if (!valid)
            {
                this.Fail(container, BindingFailure.InvalidTypeFormation);
            }
        }
    }
}
