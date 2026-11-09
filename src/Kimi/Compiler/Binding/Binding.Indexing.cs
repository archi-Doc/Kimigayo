// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Indexing (IndexSources, IndexLibrary, IndexVisitor, TestSyntaxVisitor): the walk over the source modules and the embedded library
// after ResetPass, before any header, Type or expression is bound. One part enters later: a try propagation's arms are deferred
// (SemanticsIndexed false), and BindMatch indexes them during body binding (IndexMatchArms), once the Subject's Type selects Option
// or Result (Binding.Patterns.cs).
// - Responsibility: per node, its semantic fields reset (the HIR row, plan activity, conversion and clause bindings), its scopes
//   created and its symbols declared (containers, functions, parameters, locals, generic and length parameters, enum cases,
//   pattern bindings and guard candidates); a test function the build does not include is resolved without indexing its signature
//   or body, and TestSyntaxVisitor records only its Layout and misplaced Test attributes; a valid one is still declared, so its
//   declaration is judged in every build (SPEC 6.7.2, 18.6), and access keeps it from every product candidate set.
// - Inputs: the numbered source trees and the restored library (Library.Restore); the scopes and symbols kept across passes.
// - Outputs: the pass's nodes, pattern nodes, aliases, scopes and symbols, and the failures recorded in this file: Duplicate (Declare,
//   a repeated Semantics parameter), InvalidTypeFormation (a second self parameter, a property with neither storage nor getter),
//   Unsupported (an unknown marker on an excluded test), InvalidTestDefinition and InvalidLayoutAttribute (IndexInvalidTestAttribute,
//   IndexLayoutAttribute), with the attributes' targets invalidated (InvalidateAttributeTarget).
// - Invariants: a recursive descent as deep as the syntax (R7-D3 bounds it); R7-D2 c replaces the walk with SyntaxTable's pre-order.
// - Prohibitions: no name is resolved and no written Type, header or expression is bound here.
public sealed partial class Binding
{
    private readonly IndexVisitor indexer;
    private readonly Dictionary<GenericParameterKoto, BindingSymbol> pairSymbols = new(ReferenceEqualityComparer.Instance);
    private TestSyntaxVisitor? testSyntaxVisitor;

    // The indexer resets every semantic field of the source trees before any header or expression is evaluated.
    private void IndexSources()
    {
        foreach (var module in this.compilation.SourceModules)
        {
            this.indexer.Scope = this.GetScope(module.RootKoto, null);
            this.indexer.Visit(module.RootKoto);
        }

        this.IndexModuleReferences();
    }

    // Restores and indexes the embedded Kimi library.
    private void IndexLibrary()
    {
        this.Library.Restore();
        this.scopes[this.Library.Kotonoha.RootKoto] = this.Library.Scope;
        this.scopes[this.Library.Intrinsics] = this.Library.IntrinsicsScope;
        this.scopes[this.Library.Console] = this.Library.ConsoleScope;
        this.scopes[this.Library.Test] = this.Library.TestScope;
        this.scopes[this.Library.Text] = this.Library.TextScope;
        this.indexer.Scope = this.Library.Scope;
        var libraryRoot = this.Library.Kotonoha.RootKoto;
        for (var i = 0; i < libraryRoot.NestedContainers.Count; i++)
        {
            var declaration = libraryRoot.NestedContainers[i];
            if (declaration.BoundSymbol?.Intrinsic is not (null or IntrinsicKind.None))
            {
                continue;
            }

            if (this.Library.SignatureScope(declaration) is { } signatureScope)
            {
                this.indexer.Scope = signatureScope;
                for (var m = 0; m < declaration.Members.Count; m++)
                {
                    this.indexer.Visit(declaration.Members[m]);
                }

                this.indexer.Scope = this.Library.Scope;
            }
            else
            {
                this.indexer.Visit(declaration);
            }
        }

        for (var i = 0; i < libraryRoot.Members.Count; i++)
        {
            this.indexer.Visit(libraryRoot.Members[i]);
        }
    }

    private sealed class TestSyntaxVisitor(Binding binding) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (node is AttributeKoto { Kind: AttributeKind.Layout } layout)
            {
                binding.IndexLayoutAttribute(layout);
                if (layout.AttributeChain is { } precedingAttribute)
                {
                    this.Visit(precedingAttribute);
                }

                return;
            }

            var marker = node is FunctionKoto function && !TestDefinition.IsValidSyntax(function) ? TestDefinition.Marker(function) : null;
            if (node is AttributeKoto { Kind: AttributeKind.Test } attribute &&
                (attribute.Parent is not FunctionKoto owner || TestDefinition.Marker(owner) is null))
            {
                marker = attribute;
            }

            if (marker is not null)
            {
                binding.IndexInvalidTestAttribute(marker);
            }

            node.VisitChildren(this);
        }
    }

    private sealed class IndexVisitor(Binding binding) : KotoVisitor
    {
        private void IndexCandidates(Koto syntax, bool guarded)
        {
            syntax = KotoHelper.UnwrapParentheses(syntax);
            if (syntax is not SyntaxFormKoto form)
            {
                return;
            }

            if (form.Akind == KotoKind.BindingPattern && form.Operands[0] is IdentifierNameKoto name)
            {
                if (!guarded)
                {
                    binding.symbols.Remove(name);
                    return;
                }

                if (binding.symbols.TryGetValue(name, out var candidate) && candidate.Name != name.IdentifierName)
                {
                    binding.symbols.Remove(name);
                }

                var bodySymbol = form.BoundSymbol;
                binding.Declare(name, name.IdentifierName, BindingSymbolKind.PatternCandidate, form, this.Scope);
                form.BoundSymbol = bodySymbol;
                return;
            }

            foreach (var operand in form.Operands)
            {
                this.IndexCandidates(operand, guarded);
            }
        }

        private int patternDepth;

        internal BindingScope Scope { get; set; } = null!;

        public void IndexMatchArms(MatchKoto match, BindingScope scope)
        {
            var previous = this.Scope;
            this.Scope = scope;
            var outer = this.Scope;
            for (var i = 0; i < match.Arms.Count; i++)
            {
                var arm = match.Arms[i];
                var armScope = binding.GetScope(arm.Pattern, outer);
                this.Scope = armScope;
                this.patternDepth++;
                this.Visit(arm.Pattern);
                this.patternDepth--;
                if (arm.Guard is { } guard)
                {
                    this.Scope = binding.GetScope(guard, outer);
                    binding.candidateScopes.Add(guard);
                    this.IndexCandidates(arm.Pattern, true);

                    this.Visit(guard);
                }
                else
                {
                    this.IndexCandidates(arm.Pattern, false);
                }

                this.Scope = armScope;
                this.Visit(arm.Body);
                this.Scope = outer;
            }

            this.Scope = previous;
        }

        public override void Visit(Koto node)
        {
            HirTables.Reset(node);
            if (node is BinaryKoto binary)
            {
                binary.ComparisonActive = false;
                if (binary.ArithmeticStorage is { } arithmetic)
                {
                    arithmetic.Active = false;
                }

                ClearIdentityPremise(binary);
            }

            if (node is UnaryKoto { ArithmeticStorage: { } unaryArithmetic })
            {
                unaryArithmetic.Active = false;
            }

            if (node.FormattingStorage is { } formatting)
            {
                formatting.Active = false;
            }

            if (node is FunctionKoto test && TestDefinition.Marker(test) is { } marker && !TestDefinition.IsIncluded(test))
            {
                // Product lookup and analysis never visit the test's signature names or body.
                test.BoundType = BoundType.Unit;
                test.BindingState = BindingState.Resolved;
                if (!TestDefinition.IsValidSyntax(test))
                {
                    // Its signature has names to bind, so it stays undeclared: its declaration's own records appear only in a Test
                    // build (PLAN G660's records-only part).
                    binding.IndexInvalidTestAttribute(marker);
                    binding.InvalidateAttributeTarget(marker);
                }
                else
                {
                    // A valid definition's header holds no name to bind: its declaration is declared and judged like any function's
                    // (kind conflicts, signatures, inherited Names, virtual eligibility, protected placement); access keeps it from every
                    // product candidate set.
                    this.DeclareFunction(test);
                    binding.GetScope(test, this.Scope);
                    binding.nodes.Add(test);
                    for (var attribute = test.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
                    {
                        if (!ReferenceEquals(attribute, marker))
                        {
                            if (attribute.Kind == AttributeKind.Layout)
                            {
                                // The syntax visitor reports Layout's wrong target below.
                                continue;
                            }

                            // No Mod marker registry exists yet; selection does not recognize an unknown marker.
                            binding.ClearFailure(attribute);
                            binding.Fail(attribute, BindingFailure.Unsupported, true);
                            binding.nodes.Add(attribute);
                        }
                    }
                }

                test.VisitChildren(binding.testSyntaxVisitor ??= new(binding));
                return;
            }

            if (node is AttributeKoto { Kind: AttributeKind.Test } invalidTest)
            {
                if (invalidTest.Parent is FunctionKoto testOwner && TestDefinition.IsIncluded(testOwner) && TestDefinition.IsValidSyntax(testOwner))
                {
                    invalidTest.BoundType = BoundType.Unit;
                    invalidTest.BindingState = BindingState.Resolved;
                    if (invalidTest.AttributeChain is { } earlier)
                    {
                        this.Visit(earlier);
                    }

                    return;
                }

                binding.IndexInvalidTestAttribute(invalidTest);
                binding.InvalidateAttributeTarget(invalidTest);

                if (invalidTest.AttributeChain is { } precedingMarker)
                {
                    this.Visit(precedingMarker);
                }

                return;
            }

            if (node is AttributeKoto { Kind: AttributeKind.Layout } layout)
            {
                binding.IndexLayoutAttribute(layout);
                if (layout.AttributeChain is { } precedingAttribute)
                {
                    this.Visit(precedingAttribute);
                }

                return;
            }

            if (node is AttributeKoto { Kind: AttributeKind.LibraryImport } import && AttributeTarget(import) is FunctionKoto)
            {
                // SPEC 22.6: the operand names a library and symbol; it is validated, never evaluated.
                binding.ClearFailure(import);
                import.BindingState = BindingState.Unvisited;
                import.BoundType = null;
                binding.nodes.Add(import);
                if (import.AttributeChain is { } precedingImport)
                {
                    this.Visit(precedingImport);
                }

                return;
            }

            if (node is AttributeKoto selectedAttribute)
            {
                // Handled attributes returned above. Other selected attributes still bind their own lookup failure.
                binding.InvalidateAttributeTarget(selectedAttribute);
            }

            if (node is EffectBoundKoto)
            {
                // SPEC 8.11.1: the selector and Name of an effect item designate a Contract and its requirement; they are
                // resolved with the item, never as expressions.
                binding.nodes.Add(node);
                return;
            }

            if (node is ConversionKoto conversion)
            {
                conversion.ConversionBinding = ConversionBinding.None;
            }

            if (node is IsKoto clause)
            {
                clause.BoundConstraint = null;
                clause.BoundRuntimeTest = null;
            }

            binding.nodes.Add(node);
            if (this.patternDepth != 0)
            {
                binding.patternNodes.Add(node);
                if (node is SyntaxFormKoto { Akind: KotoKind.BindingPattern } pattern && pattern.Operands[0] is IdentifierNameKoto name)
                {
                    if (binding.symbols.TryGetValue(pattern, out var existing) && existing.Name != name.IdentifierName)
                    {
                        binding.symbols.Remove(pattern);
                    }

                    binding.Declare(pattern, name.IdentifierName, BindingSymbolKind.Local, pattern, this.Scope);
                }

                node.VisitChildren(this);
                return;
            }

            var previous = this.Scope;
            if (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } source)
            {
                this.Scope = binding.GetScope(source, binding.ModuleScope(node), node.Parent);
            }

            switch (node)
            {
                case IsKoto or SyntaxFormKoto when AssociatedHead(node) is OriginApplicationKoto:
                    this.Scope = binding.GetScope(node, this.Scope);
                    break;
                case ForKoto iteration:
                    this.Visit(iteration.Iterable);
                    this.Scope = binding.GetScope(iteration.Body, this.Scope);
                    for (var i = 0; i < iteration.Bindings.Count; i++)
                    {
                        var name = iteration.Bindings[i];
                        binding.Declare(name, name.IdentifierName, BindingSymbolKind.Local, name, this.Scope);
                    }

                    this.Visit(iteration.Body);
                    this.Scope = previous;
                    return;
                case MatchKoto match:
                    binding.IndexMatch(match);
                    this.Visit(match.Expression);
                    if (match is TryKoto propagation)
                    {
                        propagation.SemanticsIndexed = false;
                    }
                    else
                    {
                        this.IndexMatchArms(match, this.Scope);
                    }

                    this.Scope = previous;
                    return;
                case SyntaxFormKoto { Akind: KotoKind.EnumCase, Parent: EnumKoto } enumeration when TryEnumPayload(enumeration, out _) && enumeration.Operands[0] is IdentifierNameKoto caseName:
                    var caseSymbol = binding.Declare(node, caseName.IdentifierName, BindingSymbolKind.EnumCase, node, this.Scope);
                    caseSymbol.EnumCase ??= new(caseSymbol, this.Scope.Owner.BoundSymbol!);
                    caseSymbol.EnumCase.Owner = this.Scope.Owner.BoundSymbol!;
                    break;
                case SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto }:
                    this.Scope = binding.GetScope(node, this.Scope);
                    this.Scope.ConformancePath = null;
                    break;
                case DeclarationContainerKoto container:
                    if (!container.IsRoot)
                    {
                        var kind = container is GroupKoto ? BindingSymbolKind.Container : BindingSymbolKind.Type;
                        var symbol = binding.Declare(node, container.Name, kind, node, this.Scope);
                        if (kind == BindingSymbolKind.Type)
                        {
                            symbol.Type ??= binding.types.Intern(BoundTypeKind.Nominal, symbol, SemanticsKind.Owner, []);
                        }

                        // The declared symbol carries the Kimi catalog identity (SPEC 22.1).
                        (container as StructKoto)?.PrepareImplicitConstructor(KimiLibraryCatalog.ManagesRepresentation(symbol));
                    }

                    this.Scope = container.IsRoot ? binding.ModuleScope(node) : binding.GetScope(node, this.Scope);
                    break;
                case FunctionKoto function:
                    function.PrepareBaseInitializer();
                    if (function.IsAnonymous)
                    {
                        if (!binding.symbols.TryGetValue(function, out var closureSymbol))
                        {
                            closureSymbol = new(string.Empty, BindingSymbolKind.Function, function, this.Scope);
                            binding.symbols.Add(function, closureSymbol);
                        }

                        closureSymbol.Scope = this.Scope;
                    }

                    if (!function.IsGenerated && !function.IsAnonymous)
                    {
                        this.DeclareFunction(function);
                    }

                    this.Scope = binding.GetScope(node, this.Scope);
                    for (var i = 0; i < function.Parameters.Count; i++)
                    {
                        var parameter = function.Parameters[i];
                        var symbol = binding.Declare(parameter, parameter.InternalName, BindingSymbolKind.Parameter, node, this.Scope);
                        symbol.Slot = i;
                    }

                    node.BoundSymbol = binding.symbols.GetValueOrDefault(node);
                    binding.IndexSpecialReceiver(function, this.Scope);
                    break;
                case PropertyAccessorKoto accessor:
                    this.Scope = binding.GetScope(node, this.Scope);
                    binding.IndexAccessor(accessor, this.Scope);
                    break;
                case CodeBlockKoto:
                    if (node.Parent is ForKoto || node.Parent is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ||
                        (node.Parent is MatchKoto && binding.patternNodes.Contains(this.Scope.Owner)))
                    {
                        binding.scopes[node] = this.Scope;
                    }
                    else if (node.Parent is not FunctionKoto)
                    {
                        this.Scope = binding.GetScope(node, this.Scope);
                    }
                    else
                    {
                        binding.scopes[node] = this.Scope;
                    }

                    break;
                case VariableKoto variable:
                    var declarationScope = node is PropertyKoto && this.Scope.Owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ? this.Scope.Parent! : this.Scope;
                    var variableSymbol = binding.Declare(node, variable.NameKoto.IdentifierName, node is PropertyKoto ? BindingSymbolKind.Property : BindingSymbolKind.Local, node, declarationScope);
                    variableSymbol.ConditionalDeclaration = ReferenceEquals(declarationScope, this.Scope) ? null : (SyntaxFormKoto)this.Scope.Owner;
                    if (node is PropertyKoto property)
                    {
                        var bound = variableSymbol.Property ??= new(variableSymbol);
                        bound.IsVerified = false;
                        var getter = property.GetAccessor(PropertyAccessorKind.Get);
                        Reset(bound.Getter, getter, bound.IsStored || getter is not null);
                        Reset(bound.Setter, property.GetAccessor(PropertyAccessorKind.Set), property.DeclarationKind == PropertyDeclarationKind.Var || property.GetAccessor(PropertyAccessorKind.Set) is not null);
                        if (!bound.IsStored && getter is null)
                        {
                            binding.Fail(property, BindingFailure.InvalidTypeFormation);
                        }
                    }

                    break;
                case GenericParameterKoto parameter:
                    var typeSymbol = binding.Declare(node, parameter.Identifier, parameter.SemanticsParameter is null ? BindingSymbolKind.TypeParameter : BindingSymbolKind.SemanticsTarget, node, this.Scope);
                    typeSymbol.Slot = this.Scope.Types.Count - 1;
                    typeSymbol.WholeType ??= new(parameter.Identifier, BoundTypeKind.Parameter, typeSymbol);
                    typeSymbol.Type ??= parameter.SemanticsParameter is null ? typeSymbol.WholeType : new(parameter.Identifier, BoundTypeKind.TargetProjection, typeSymbol);
                    if (parameter.SemanticsParameter is not null)
                    {
                        if (!binding.pairSymbols.TryGetValue(parameter, out var semanticsSymbol))
                        {
                            semanticsSymbol = new(parameter.SemanticsParameter, BindingSymbolKind.SemanticsParameter, node, this.Scope);
                            binding.pairSymbols.Add(parameter, semanticsSymbol);
                        }

                        semanticsSymbol.Scope = this.Scope;
                        semanticsSymbol.Pair = typeSymbol;
                        typeSymbol.Pair = semanticsSymbol;
                        if (!this.Scope.Types.TryAdd(parameter.SemanticsParameter, semanticsSymbol))
                        {
                            binding.Fail(node, BindingFailure.Duplicate);
                        }
                    }

                    break;
                case LengthParameterKoto parameter:
                    binding.Declare(node, parameter.Identifier, BindingSymbolKind.LengthParameter, node, this.Scope).Type = BoundType.ISize;
                    break;
                case AliasKoto alias:
                    binding.aliases.Add(alias);
                    break;
            }

            node.VisitChildren(this);
            this.Scope = previous;
        }

        private static void Reset(BoundAccessor accessor, PropertyAccessorKoto? declaration, bool present)
        {
            accessor.Declaration = declaration;
            if (accessor.SignatureSymbol is { } signature && !ReferenceEquals(signature.Declaration, declaration))
            {
                accessor.SignatureSymbol = null;
            }

            accessor.IsPresent = present;
            accessor.Receiver = accessor.Input = accessor.Result = null;
            if (accessor.SelfSymbol is { } self)
            {
                self.Type = null;
            }

            if (accessor.ValueSymbol is { } value)
            {
                value.Type = null;
            }

            if (accessor.StorageSymbol is { } storage)
            {
                storage.Type = null;
            }
        }

        // A named function's symbol in its member scope, an excluded test's included (G660): the declaration is judged in every build.
        private void DeclareFunction(FunctionKoto function)
        {
            var memberScope = this.Scope.Owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ? this.Scope.Parent! : this.Scope;
            var conditional = this.Scope.Owner as SyntaxFormKoto;
            if (IsRootMain(function))
            {
                memberScope = binding.ModuleScope(function);
            }

            var symbol = binding.Declare(function, function.Name, BindingSymbolKind.Function, function, memberScope);
            symbol.ConditionalDeclaration = conditional is { Akind: KotoKind.ConditionalConformance } ? conditional : null;
            // The members of the fixed array and the integer Position witness are receiver functions of internal
            // Kimi groups (SPEC 22.1, 4.6.1, PLAN G32).
            if (memberScope.Owner is StructKoto or EnumKoto or ContractKoto || binding.Library.IsBuiltinMemberGroup(memberScope.Owner))
            {
                for (var p = 0; p < function.Parameters.Count; p++)
                {
                    if (function.Parameters[p].InternalName == "self")
                    {
                        if (symbol.ReceiverIndex >= 0)
                        {
                            binding.Fail(function, BindingFailure.InvalidTypeFormation);
                        }

                        symbol.ReceiverIndex = p;
                    }
                }
            }
        }
    }
}
