// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<BindingSymbol> importDeclarations = new();

    /// <summary>Gets the import declarations of the latest pass (SPEC 22.6): the function of each <c>#LibraryImport</c> declaration whose
    /// signature Binding checks as an import's (ImportAbi), in source order, whatever its verdict; <see cref="LibraryImports"/> are the valid
    /// ones. One whose Attribute fails before (InvalidLibraryImport) is no import.</summary>
    internal IReadOnlyList<BindingSymbol> ImportDeclarations => this.importDeclarations;

    // SPEC 22.6: whether a function carries the import Attribute; its validity is checked separately.
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

    // SPEC 22.6: string, dynamic collections (SPEC 4.9), object handles (obj, rc, arc; an object borrow is none), function and Closure
    // values, and borrows of them have no C counterpart.
    private static bool HasNoCCounterpart(BoundType type)
        => ReferenceEquals(type, BoundType.String) || type.HandleMode is not null ||
            type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Function or BoundTypeKind.Closure or BoundTypeKind.FunctionItem ||
            (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var referent] } && HasNoCCounterpart(referent));

    // SPEC 22.6: a fixed-width integer Core or f32/f64 by width (i8/u8 share '1'); '?' for any other Type, a wrapping integer included.
    private static char ScalarCode(BoundType type)
        => type.Kind != BoundTypeKind.Primitive ? '?' : type.Name switch
        {
            "i8" or "u8" => '1',
            "i16" or "u16" => '2',
            "i32" or "u32" => '4',
            "i64" or "u64" => '8',
            "f32" => 'f',
            "f64" => 'd',
            _ => '?',
        };

    // A rejected selected attribute is the declaration's failed prerequisite, not another failed requirement.
    // These markers cannot resolve within this pass; removing or replacing them rebuilds the cause on the next Bind.
    private void InvalidateAttributeTarget(AttributeKoto attribute)
    {
        if (AttributeTarget(attribute) is { } target)
        {
            target.BindingState = BindingState.Invalid;
            this.AddPrerequisite(target, attribute);
        }
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
            this.InvalidateAttributeTarget(attribute);
        }
        else
        {
            Complete(attribute, BoundType.Unit);
        }
    }

    // SPEC 22.6: validate each selected import declaration and its defining module's
    // native requirement (SPEC 20.4) without reading native files.
    private void ValidateLibraryImports()
    {
        string? target = null;
        var symbols = this.importSymbols;
        symbols.Clear();
        this.importDeclarations.Clear();
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

            // SPEC 20.4/20.8.2.4: the Kind of the defining module's requirement, after self-targeted
            // supply expansion, decides dllimport generation; reserved supplies have their fixed kinds.
            string? kind = null;
            if (name is Kernel32Imports.LibraryName)
            {
                kind = "import"; // The generated kernel32 import library (SPEC 20.4).
                if (Array.IndexOf(Kernel32Imports.Symbols, symbol) < 0)
                {
                    // SPEC 20.4: the reserved supply exports only the reviewed project-owned definition.
                    this.Fail(attribute, BindingFailure.UnavailableReservedImport);
                }
            }
            else if (name is WindowsProfile.BackendLibrary)
            {
                kind = "static"; // The compiler-managed backend archive (SPEC 20.4).
                if (Array.IndexOf(WindowsProfile.ProvidedSymbols, symbol) < 0)
                {
                    // SPEC 21.4.7: the backend archive supplies only its catalog, whose names are reserved above.
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

            this.importDeclarations.Add(function.BoundSymbol!); // An import: its signature is checked, its by-value Types layout uses (R5-M150).
            var signature = this.ImportAbi(attribute, function, ordinal++);
            if (signature is not null)
            {
                // SPEC 21.2/22.5.6: the runtime's own kernel32 declarations share the same final symbol
                // table; an import joins one only through the reserved kernel32 supply with an equal
                // physical Type, and never a declaration carrying an inexpressible ABI attribute.
                if (IsRuntimeDeclaration(symbol, out var declared) && (name != Kernel32Imports.LibraryName || declared != signature))
                {
                    this.Fail(attribute, BindingFailure.ConflictingRuntimeSymbol);
                }

                // SPEC 21.2: one final symbol table; same-named declarations share only an equal physical
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
                this.libraryImports.Add(new(function.BoundSymbol!, name, symbol, kind));
            }
        }

        // SPEC 22.6: a direct group or receiverless struct type function without
        // generic/Origin parameters, specializations or argument defaults.
        static bool IsImportShape(FunctionKoto function)
        {
            // Safe or unsafe; borrow annotations introduce the import's own signature Origins (SPEC 22.6).
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

        // SPEC 22.6: one declaration selects one external symbol, so a repeated import is invalid.
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

        // SPEC 21.4.7 declarations generated beside the runtime; the signature is null when none can agree.
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

        // SPEC 21.2 reserves compiler, LLVM and profile-supply external names; the supply names
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

    // SPEC 22.6: the signature is one physical code per result and parameter (PhysicalCode), Unit only as the result; it is null
    // when a Type failed to bind or its decision rests on a failed struct, either having its own diagnostic, and when the import
    // fails. A Type without a C counterpart makes the import an error; otherwise a Type outside the table makes it Unsupported,
    // located at the first such Type. The attribute keeps one failure (DIAGNOSTICS rule 4), so the error takes precedence.
    private string? ImportAbi(AttributeKoto attribute, FunctionKoto function, int ordinal)
    {
        if (ordinal == this.importSignatures.Count)
        {
            this.importSignatures.Add(null);
        }

        var count = function.Parameters.Count + 1;
        Span<char> codes = count <= 64 ? stackalloc char[count] : new char[count];
        var complete = true;
        Koto? unsupported = null;
        for (var i = 1; i <= count; i++)
        {
            var p = i % count; // Source order: the parameters, then the result, whose code comes first.
            if ((p == 0 ? function.BoundSymbol!.Type : this.symbols[function.Parameters[p - 1]].Type) is not { } type)
            {
                complete = false;
            }
            else if ((codes[p] = p == 0 && ReferenceEquals(type, BoundType.Unit) ? 'v' : this.PhysicalCode(type)) == '\0')
            {
                this.Fail(attribute, BindingFailure.InvalidImportSignature);
                return null;
            }
            else
            {
                complete &= codes[p] != '!';
                unsupported ??= codes[p] == '?' ? (p == 0 ? function.ReturnType! : function.Parameters[p - 1].Type) : null;
            }
        }

        if (unsupported is not null)
        {
            this.FailExplained(ref this.unsupportedSpans, attribute, BindingFailure.Unsupported, unsupported.Span);
            return null;
        }

        string? signature = null;
        if (complete)
        {
            var cached = this.importSignatures[ordinal];
            signature = codes.SequenceEqual(cached.AsSpan()) ? cached : new string(codes);
        }

        return this.importSignatures[ordinal] = signature;
    }

    // SPEC 22.6: one decision per parameter or result Type. A Type of the table is its physical code: a scalar (ScalarCode), and a raw
    // pointer or a ref/uniq borrow of a C-exchangeable referent is 'p'. A Type without a C counterpart is '\0', an error; a borrow whose
    // decision rests on a failed struct is '!'. Any other Type is '?', Unsupported: bool, char, isize/usize, i128/u128, wrapping integers,
    // Never, Unit as a parameter, Tuples, enums (the nonnull Option of a borrow is not laid out yet), Slices, structs and fixed arrays by
    // value, object borrows and borrows of other referents.
    private char PhysicalCode(BoundType type)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components.Count: 1 } ? 'p' :
            HasNoCCounterpart(type) ? '\0' :
            type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [var referent] } ?
                this.CExchangeable(referent, 0) switch { true => 'p', false => '?', null => '!' } :
            ScalarCode(type);

    // SPEC 22.6: whether a referent is C-exchangeable: fixed-width integer Cores and f32/f64, raw pointers, ref/uniq borrows of eligible
    // referents, positive-length fixed arrays of eligible elements and owned C-layout structs (AdtDef.IsCLayout) with eligible prepared
    // Fields. Null when the decision rests on a struct whose declaration or Field Type already failed, which has its own diagnostic.
    // The nonnull Option representation of a borrow is not yet laid out, so Option<ref/T> is not admitted.
    private bool? CExchangeable(BoundType type, int depth)
    {
        if (depth > 32)
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Primitive)
        {
            return ScalarCode(type) != '?';
        }

        if (type is { Kind: BoundTypeKind.Semantics, Components: [var referent] })
        {
            return type.Semantics switch
            {
                SemanticsKind.Raw => true,
                SemanticsKind.Ref or SemanticsKind.Uniq => this.CExchangeable(referent, depth + 1),
                _ => false,
            };
        }

        if (type is { Kind: BoundTypeKind.FixedArray, Semantics: SemanticsKind.Owner, Length: > 0, Components: [var element] })
        {
            return this.CExchangeable(element, depth + 1);
        }

        if (type.Semantics != SemanticsKind.Owner || type.Symbol is not { } symbol)
        {
            return false;
        }

        if (symbol.Declaration.BindingFailure != BindingFailure.None || this.InvalidDeclarationCause(symbol.Declaration) is not null)
        {
            return null;
        }

        if (symbol.Adt is not { IsCLayout: true } shape)
        {
            return false;
        }

        for (var i = 0; i < shape.Types.Count; i++)
        {
            if ((this.StoredType(shape.Types[i], type) is { } field ? this.CExchangeable(field, depth + 1) : null) is not true and var decision)
            {
                return decision;
            }
        }

        return true;
    }

    // SPEC 6.7.1: the Attribute's own errors (its mode, fragments and storage fragment) and the published declaration facts: IsCLayout for
    // a valid C selection whose layout is available at the declaration, CLayoutUnavailable for one on an open, derived or empty struct,
    // which Layout judges CLayout at each by-value use (SPEC 8.15.3; DECISIONS R4-M134 Q8: no declaration error).
    private void ValidateLayoutFragments()
    {
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

                if (attribute.BindingState == BindingState.Invalid)
                {
                    this.InvalidateAttributeTarget(attribute);
                    valid = false;
                }

                previousFragment = attribute.FragmentOrdinal;
                if (attribute.LayoutMode is { } explicitMode)
                {
                    if (mode is not null && mode != explicitMode)
                    {
                        this.Fail(latestSpecification!, BindingFailure.ConflictingLayout);
                        this.InvalidateAttributeTarget(latestSpecification!);
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
                        this.Fail(container, BindingFailure.InvalidTypeFormation);
                        valid = false;
                    }

                    storageFragment = field.FragmentOrdinal;
                    instanceFields += (field.Modifier & ModifierKind.Static) == 0 ? 1 : 0;
                }
            }

            // The published facts, written for every struct in every pass; PrepareStorage reuses a shape created here on a first pass.
            var selected = mode == "C" && valid;
            var unavailable = selected && ((container.Modifier & ModifierKind.Open) != 0 || container.Bases.Count != 0 || instanceFields == 0);
            var adt = container.BoundSymbol!.Adt ??= new();
            adt.IsCLayout = selected && !unavailable;
            adt.CLayoutUnavailable = unavailable;
        }
    }
}
