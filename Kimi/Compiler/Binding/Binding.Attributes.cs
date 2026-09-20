// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static Koto? AttributeTarget(AttributeKoto attribute)
    {
        var target = attribute.Parent;
        while (target is AttributeKoto)
        {
            target = target.Parent;
        }

        return target;
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
            Fail(attribute, BindingFailure.InvalidLayoutAttribute);
            if (target is not null)
            {
                Fail(target, BindingFailure.InvalidTypeFormation);
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
        Dictionary<string, (string Signature, string? Kind)>? symbols = null;
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
                Fail(attribute, BindingFailure.InvalidLibraryImport);
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
                    Fail(attribute, BindingFailure.UnavailableReservedImport);
                }
            }
            else if (name is WindowsProfile.BackendLibrary)
            {
                kind = "static"; // The compiler-managed backend archive (SPEC 20.8.2.4).
                if (Array.IndexOf(WindowsProfile.ProvidedSymbols, symbol) < 0)
                {
                    // SPEC 21.5.7: the backend archive supplies only its catalog, whose names are reserved above.
                    Fail(attribute, BindingFailure.UnavailableReservedImport);
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
                    Fail(attribute, BindingFailure.MissingNativeRequirement);
                }
                else
                {
                    kind = requirement?.Kind ?? supply?.Kind;
                }
            }

            if (!this.TryGetImportAbi(function, out var signature))
            {
                Fail(attribute, BindingFailure.UnsupportedImportSignature);
            }
            else if (signature is not null)
            {
                // SPEC 21.5.2/22.5.6: the runtime's own kernel32 declarations share the same final symbol
                // table; an import joins one only through the reserved kernel32 supply with an equal
                // physical Type, and never a declaration carrying an inexpressible ABI attribute.
                if (IsRuntimeDeclaration(symbol, out var declared) && (name != Kernel32Imports.LibraryName || declared != signature))
                {
                    Fail(attribute, BindingFailure.ConflictingRuntimeSymbol);
                }

                // SPEC 21.5.2: one final symbol table; same-named declarations share only an equal physical
                // Type and dllimport setting. An unresolved kind already has its own diagnostic.
                symbols ??= new(StringComparer.Ordinal);
                if (!symbols.TryAdd(symbol, (signature, kind)))
                {
                    var previous = symbols[symbol];
                    if (previous.Signature != signature)
                    {
                        Fail(attribute, BindingFailure.ConflictingImportSignature);
                    }
                    else if (kind is not null && previous.Kind is not null && previous.Kind != kind)
                    {
                        Fail(attribute, BindingFailure.ConflictingImportSupply);
                    }
                }
            }
        }

        // SPEC 22.3.1: a direct group or receiverless struct type function without
        // generic/Origin parameters, specializations or default/optional arguments.
        static bool IsImportShape(FunctionKoto function)
        {
            if ((function.Modifier & ModifierKind.Unsafe) == 0 || function.BoundSymbol is not { ReceiverIndex: < 0 } symbol ||
                symbol.Scope.Owner is not (GroupKoto or StructKoto) || function.GenericArguments.Count != 0 || function.Origins.Count != 0 ||
                function.IsSpecialization || function.IsConstructor || function.IsDestructor || function.IsAnonymous || function.IsRequirement)
            {
                return false;
            }

            foreach (var parameter in function.Parameters)
            {
                if (parameter.IsOptional || parameter.DefaultValue is not null)
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
    // parameter (i8/u8 share i8, every unsafe/T is ptr); it is null when a Type failed to bind,
    // since that Type already has its own diagnostic.
    private bool TryGetImportAbi(FunctionKoto function, out string? signature)
    {
        signature = null;
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
        else if ((codes[0] = PhysicalCode(result)) == '\0')
        {
            return false;
        }

        for (var p = 1; p < count; p++)
        {
            if (this.symbols[function.Parameters[p - 1]].Type is not { } type)
            {
                complete = false;
            }
            else if ((codes[p] = PhysicalCode(type)) == '\0')
            {
                return false;
            }
        }

        signature = complete ? new string(codes) : null;
        return true;

        static char PhysicalCode(BoundType type)
            => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Components.Count: 1 } ? 'p' :
                type.Kind != BoundTypeKind.Primitive ? '\0' :
                type.Name switch
                {
                    "i8" or "u8" => '1',
                    "i16" or "u16" => '2',
                    "i32" or "u32" => '4',
                    "i64" or "u64" => '8',
                    "f32" => 'f',
                    "f64" => 'd',
                    _ => '\0',
                };
    }

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
                    Fail(attribute, BindingFailure.InvalidLayoutAttribute);
                }

                valid &= attribute.BindingState != BindingState.Invalid;

                previousFragment = attribute.FragmentOrdinal;
                if (attribute.LayoutMode is { } explicitMode)
                {
                    if (mode is not null && mode != explicitMode)
                    {
                        Fail(latestSpecification!, BindingFailure.ConflictingLayout);
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
            for (var m = 0; mode == "C" && m < container.Members.Count; m++)
            {
                if (container.Members[m] is PropertyKoto field && IsStoredVariable(field))
                {
                    if (storageFragment >= 0 && storageFragment != field.FragmentOrdinal)
                    {
                        Fail(field, BindingFailure.SplitCLayoutStorage);
                        valid = false;
                    }

                    storageFragment = field.FragmentOrdinal;
                }
            }

            if (!valid)
            {
                Fail(container, BindingFailure.InvalidTypeFormation);
            }
        }
    }
}
