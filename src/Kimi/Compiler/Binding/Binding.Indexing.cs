// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Indexing (Index, IndexLibrary, IndexWalk, IndexMatchArms): the walk over the pass's source modules and the embedded library after
// ResetPass, before any header, Type or expression is bound. One part enters later: a try propagation's arms are deferred
// (SemanticsIndexed false), and BindMatch indexes them during body binding (IndexMatchArms), once the Subject's Type selects Option
// or Result (Binding.Patterns.cs).
// - Responsibility: one iterative pre-order walk over the child slots (IndexWalk, over IndexStack's entries). It reaches each node in
//   a slot of the trees it walks (the source roots; the library's non-intrinsic root containers and root members; each module's
//   configured paths, IndexConfiguredPaths) once per holding slot, in one context (IndexEntryKind), and resets it once before the
//   first write to it in the pass (ResetEntry: at its entry, or, for an attribute of a leading chain, at its chain owner's entry,
//   since the declaration's row judges the Attributes that attach to it, SPEC 6.7).
//   In the Index and Pattern contexts a node's scopes are created and its symbols declared (containers,
//   functions, parameters, locals, generic and length parameters, enum cases, pattern bindings and guard candidates); a test function
//   the build does not include is resolved without indexing its signature or body, which the ExcludedTest context walks for its
//   Layout and misplaced Test attributes only; a valid one is still declared, so its declaration is judged in every build (SPEC
//   6.7.2, 18.6), and access keeps it from every product candidate set. A node no context indexes (an effect item's selector and
//   Name, SPEC 8.11.1; a handled attribute's operand) has only its row reset (ResetOnly). The steps keep each action after the
//   subtree it follows: a loop binding's declaration after its entry, an arm's candidates after its Pattern's subtree, a match's arms
//   after its Expression's.
// - Inputs: the numbered source trees and the restored library (Library.Restore); the scopes and symbols kept across passes.
// - Outputs: the pass's nodes, pattern nodes, aliases, scopes and symbols, and the failures recorded in this file: Duplicate (Declare,
//   a repeated Semantics parameter), InvalidTypeFormation (a second self parameter, a property with neither storage nor getter),
//   Unsupported (an unknown marker on an excluded test), InvalidTestDefinition and InvalidLayoutAttribute (IndexInvalidTestAttribute,
//   IndexLayoutAttribute), with the attributes' targets invalidated (InvalidateAttributeTarget).
// - Invariants: the walk's depth is its stack's, not the host's; the stack is one array kept across passes, never re-entered, and
//   emptied when a walk ends, a faulted one included, so no edited-away tree stays reachable and a faulted Bind faults no later one.
//   A try's arms are the one deferred part: reached when BindMatch selects them, after TryKoto.SelectOption put the selected Pattern
//   and value in the slots (the swapped-out ones are in no slot); a try whose Subject fails or is neither Option nor Result leaves its
//   arms unreached in that pass.
// - Prohibitions: no name is resolved and no written Type, header or expression is bound here; no row resets a node (ResetEntry does).
public sealed partial class Binding
{
    private readonly IndexStack indexStack = new();

    // An entry of the walk: a context, in which a node's row runs (Index; Pattern, a match arm's Pattern and its subtree: pattern
    // nodes and their locals; ExcludedTest, an excluded test's subtree; ResetOnly, rows reset and nothing indexed), or a step a row
    // places after a subtree (Declare, a loop binding's declaration; Candidates, an arm's guard candidates; Arms, a match's arms).
    private enum IndexEntryKind : byte
    {
        Index,
        Pattern,
        ExcludedTest,
        ResetOnly,
        Declare,
        Candidates,
        Arms,
    }

    // The walk's one reset step: a node's row is reset before the first write to it in the pass. An entry resets its node and the
    // node's leading attribute chain, which its row may write (an excluded test's markers, a nested function's marker); an attribute
    // of a leading chain (its Parent's chain is it) was reset at its chain owner's entry. A parameter's chain is a function's slot:
    // its head resets itself and the rest of that chain.
    private static void ResetEntry(Koto node)
    {
        if (ReferenceEquals(node.Parent?.AttributeChain, node))
        {
            return;
        }

        ResetRow(node);
        for (var attribute = node.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
        {
            ResetRow(attribute);
        }
    }

    // A node's per-pass fields: its HIR row, its comparison, arithmetic and formatting plans' activity, its identity premise, its
    // conversion binding and a clause's constraint and runtime test.
    private static void ResetRow(Koto node)
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

        if (node is ConversionKoto conversion)
        {
            conversion.ConversionBinding = ConversionBinding.None;
        }

        if (node is IsKoto clause)
        {
            clause.BoundConstraint = null;
            clause.BoundRuntimeTest = null;
        }
    }

    private static void ResetAccessor(BoundAccessor accessor, PropertyAccessorKoto? declaration, bool present)
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

    // The indexer resets every semantic field of the pass's modules before any header or expression is evaluated: its source trees and
    // their module references, then, when it binds the library, the library's.
    private void Index()
    {
        foreach (var module in this.pass.Sources)
        {
            var scope = this.GetScope(module.RootKoto, null);
            this.IndexWalk(new(module.RootKoto, scope, IndexEntryKind.Index));
            this.IndexConfiguredPaths(module, scope);
        }

        this.IndexModuleReferences();
        if (this.pass.Library is { } library)
        {
            this.IndexLibrary(library);
        }
    }

    // Restores and indexes the embedded Kimi library.
    private void IndexLibrary(Kotonoha library)
    {
        this.Library.Restore();
        this.scopes[library.RootKoto] = this.Library.Scope;
        var libraryRoot = library.RootKoto;
        for (var i = 0; i < libraryRoot.NestedContainers.Count; i++)
        {
            var declaration = libraryRoot.NestedContainers[i];
            if (declaration.BoundSymbol?.Intrinsic is null or IntrinsicKind.None)
            {
                this.IndexWalk(new(declaration, this.Library.Scope, IndexEntryKind.Index));
            }
        }

        for (var i = 0; i < libraryRoot.Members.Count; i++)
        {
            this.IndexWalk(new(libraryRoot.Members[i], this.Library.Scope, IndexEntryKind.Index));
        }

        this.IndexConfiguredPaths(library, this.Library.Scope);
    }

    // A module's configured paths (SPEC 18.2.3; F's rulings N1-4 (2), N1-9 (1)): each path's node, which lives for the Compilation in its
    // configuration document, is walked with its module's trees, so its rows are reset by the one reset step and its records, at the
    // component the path walk fails at, are the pass's like any node's.
    private void IndexConfiguredPaths(Kotonoha module, BindingScope scope)
    {
        foreach (var row in this.compilation.DefaultAliases(module))
        {
            this.IndexWalk(new(row.Target, scope, IndexEntryKind.Index));
        }
    }

    // A try's arms, once BindMatch selected them (TryKoto.SelectOption): the entries a match's Arms step pushes.
    private void IndexMatchArms(MatchKoto match, BindingScope scope) => this.IndexWalk(new(match, scope, IndexEntryKind.Arms));

    // Walks the trees below one entry to their end, an entry at a time in the order the rows push them.
    private void IndexWalk(IndexEntry first)
    {
        var stack = this.indexStack;
        if (stack.Count != 0)
        {
            throw new InvalidOperationException("The indexer's walk is entered while it walks (InternalInvariant; R7-D2 c).");
        }

        try
        {
            stack.Push(first);
            while (stack.Count != 0)
            {
                var entry = stack.Pop();
                switch (entry.Kind)
                {
                    case IndexEntryKind.Declare:
                        var name = (IdentifierNameKoto)entry.Node;
                        this.Declare(name, name.IdentifierName, BindingSymbolKind.Local, name, entry.Scope);
                        break;
                    case IndexEntryKind.Candidates:
                        this.IndexArmCandidates((MatchKoto)entry.Node, entry.Arm, entry.Scope);
                        break;
                    case IndexEntryKind.Arms:
                        this.PushArms((MatchKoto)entry.Node, entry.Scope);
                        break;
                    default:
                        ResetEntry(entry.Node);
                        this.IndexNode(entry.Node, entry.Scope, entry.Kind);
                        break;
                }
            }
        }
        finally
        {
            stack.Clear();
        }
    }

    // A node's row in its context; the children it pushes take their context from it.
    private void IndexNode(Koto node, BindingScope scope, IndexEntryKind context)
    {
        var stack = this.indexStack;
        if (context == IndexEntryKind.ResetOnly)
        {
            stack.PushChildren(node, scope, IndexEntryKind.ResetOnly, IndexEntryKind.ResetOnly);
            return;
        }

        if (context == IndexEntryKind.ExcludedTest)
        {
            this.IndexExcludedTestNode(node, scope);
            return;
        }

        if (node is FunctionKoto test && TestDefinition.Marker(test) is { } marker && !TestDefinition.IsIncluded(this.compilation, test))
        {
            this.IndexExcludedTest(test, marker, scope);
            return;
        }

        if (node is AttributeKoto attribute && this.IndexAttribute(attribute))
        {
            stack.PushChildren(attribute, scope, context, IndexEntryKind.ResetOnly);
            return;
        }

        if (node is EffectBoundKoto)
        {
            // SPEC 8.11.1: the selector and Name of an effect item designate a Contract and its requirement; they are
            // resolved with the item, never as expressions, so they are reset here without being indexed.
            this.nodes.Add(node);
            stack.PushChildren(node, scope, IndexEntryKind.ResetOnly, IndexEntryKind.ResetOnly);
            return;
        }

        this.nodes.Add(node);
        if (context == IndexEntryKind.Pattern)
        {
            this.pass.Part.PatternNodes.Add(node);
            if (node is SyntaxFormKoto { Akind: KotoKind.BindingPattern } pattern && pattern.Operands[0] is IdentifierNameKoto name)
            {
                if (this.symbols.TryGetValue(pattern, out var existing) && existing.Name != name.IdentifierName)
                {
                    this.symbols.Remove(pattern);
                }

                this.Declare(pattern, name.IdentifierName, BindingSymbolKind.Local, pattern, scope);
            }

            stack.PushChildren(node, scope, IndexEntryKind.Pattern, IndexEntryKind.Pattern);
            return;
        }

        if (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } source)
        {
            scope = this.DocumentScope(source, this.ModuleScope(node), node.Parent);
        }

        switch (node)
        {
            case IsKoto or SyntaxFormKoto when AssociatedHead(node) is OriginApplicationKoto:
                scope = this.GetScope(node, scope);
                break;
            case ForKoto iteration:
                var iterationStart = stack.Count;
                stack.Push(new(iteration.Iterable, scope, IndexEntryKind.Index));
                var body = this.GetScope(iteration.Body, scope);
                for (var i = 0; i < iteration.Bindings.Count; i++)
                {
                    // A loop's binding name is a node of the pass as a pattern's binding is: visited (its rows reset, then
                    // declared), so the summary counts its state and publishes its failure, such as a repeated name's (SPEC 6.1,
                    // 23.1.2), which no other walk reaches.
                    stack.Push(new(iteration.Bindings[i], body, IndexEntryKind.Index));
                    stack.Push(new(iteration.Bindings[i], body, IndexEntryKind.Declare));
                }

                stack.Push(new(iteration.Body, body, IndexEntryKind.Index));
                stack.Reverse(iterationStart);
                return;
            case MatchKoto match:
                this.IndexMatch(match);
                var matchStart = stack.Count;
                stack.Push(new(match.Expression, scope, IndexEntryKind.Index));
                if (match is TryKoto propagation)
                {
                    propagation.SemanticsIndexed = false;
                }
                else
                {
                    stack.Push(new(match, scope, IndexEntryKind.Arms));
                }

                stack.Reverse(matchStart);
                return;
            case SyntaxFormKoto { Akind: KotoKind.EnumCase, Parent: EnumKoto } enumeration when TryEnumPayload(enumeration, out _) && enumeration.Operands[0] is IdentifierNameKoto caseName:
                var caseSymbol = this.Declare(node, caseName.IdentifierName, BindingSymbolKind.EnumCase, node, scope);
                caseSymbol.EnumCase ??= new(caseSymbol, scope.Owner.BoundSymbol!);
                caseSymbol.EnumCase.Owner = scope.Owner.BoundSymbol!;
                break;
            case SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto }:
                scope = this.GetScope(node, scope);
                scope.ConformancePath = null;
                break;
            case DeclarationContainerKoto container:
                if (!container.IsRoot)
                {
                    var kind = container is GroupKoto ? BindingSymbolKind.Container : BindingSymbolKind.Type;
                    var symbol = this.Declare(node, container.Name, kind, node, scope);
                    if (kind == BindingSymbolKind.Type)
                    {
                        symbol.Type ??= this.types.Intern(BoundTypeKind.Nominal, symbol, SemanticsKind.Owner, []);
                    }

                    // The declared symbol carries the Kimi catalog identity (SPEC 22.1).
                    (container as StructKoto)?.PrepareImplicitConstructor(KimiLibraryCatalog.ManagesRepresentation(symbol));
                }

                scope = container.IsRoot ? this.ModuleScope(node) : this.GetScope(node, scope);
                break;
            case FunctionKoto function:
                function.PrepareBaseInitializer();
                if (function.IsAnonymous)
                {
                    if (!this.symbols.TryGetValue(function, out var closureSymbol))
                    {
                        closureSymbol = new(string.Empty, BindingSymbolKind.Function, function, scope);
                        DeclaredSymbols.Add(function, closureSymbol);
                    }

                    closureSymbol.Scope = scope;
                }

                if (!function.IsGenerated && !function.IsAnonymous)
                {
                    this.DeclareFunction(function, scope);
                }

                scope = this.GetScope(node, scope);
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    this.DeclareParameter(function, i, scope);
                }

                node.BoundSymbol = this.symbols.GetValueOrDefault(node);
                this.IndexSpecialReceiver(function, scope);
                break;
            case PropertyAccessorKoto accessor:
                scope = this.GetScope(node, scope);
                this.IndexAccessor(accessor, scope);
                break;
            case CodeBlockKoto:
                if (node.Parent is ForKoto || node.Parent is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ||
                    (node.Parent is MatchKoto && this.pass.Part.PatternNodes.Contains(scope.Owner)))
                {
                    this.scopes[node] = scope;
                }
                else if (node.Parent is not FunctionKoto)
                {
                    scope = this.GetScope(node, scope);
                }
                else
                {
                    this.scopes[node] = scope;
                }

                break;
            case VariableKoto variable:
                var declarationScope = node is PropertyKoto && scope.Owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ? scope.Parent! : scope;
                var variableSymbol = this.Declare(node, variable.NameKoto.IdentifierName, node is PropertyKoto ? BindingSymbolKind.Property : BindingSymbolKind.Local, node, declarationScope);
                variableSymbol.ConditionalDeclaration = ReferenceEquals(declarationScope, scope) ? null : (SyntaxFormKoto)scope.Owner;
                if (node is PropertyKoto property)
                {
                    var bound = variableSymbol.Property ??= new(variableSymbol);
                    bound.IsVerified = false;
                    var getter = property.GetAccessor(PropertyAccessorKind.Get);
                    ResetAccessor(bound.Getter, getter, bound.IsStored || getter is not null);
                    ResetAccessor(bound.Setter, property.GetAccessor(PropertyAccessorKind.Set), property.DeclarationKind == PropertyDeclarationKind.Var || property.GetAccessor(PropertyAccessorKind.Set) is not null);
                    if (!bound.IsStored && getter is null)
                    {
                        this.Fail(property, BindingFailure.InvalidTypeFormation);
                    }
                }

                break;
            case GenericParameterKoto parameter:
                var typeSymbol = this.Declare(node, parameter.Identifier, parameter.SemanticsParameter is null ? BindingSymbolKind.TypeParameter : BindingSymbolKind.SemanticsTarget, node, scope);
                typeSymbol.Slot = scope.Types.Count - 1;
                typeSymbol.WholeType ??= this.types.Parameter(parameter.Identifier, BoundTypeKind.Parameter, typeSymbol);
                typeSymbol.Type ??= parameter.SemanticsParameter is null ? typeSymbol.WholeType : this.types.Parameter(parameter.Identifier, BoundTypeKind.TargetProjection, typeSymbol);
                if (parameter.SemanticsParameter is not null)
                {
                    // The Semantics symbol is the kept target symbol's Pair (its listed slot, DECISIONS BU-220), made once.
                    var semanticsSymbol = typeSymbol.Pair ?? new(parameter.SemanticsParameter, BindingSymbolKind.SemanticsParameter, node, scope);
                    semanticsSymbol.Scope = scope;
                    semanticsSymbol.Pair = typeSymbol;
                    typeSymbol.Pair = semanticsSymbol;
                    this.PairOuterOrigin(typeSymbol); // SPEC 8.1.1: the binder's outer Origin, interned with its binder (PLAN G949 S1-e).
                    if (!scope.Types.TryAdd(parameter.SemanticsParameter, semanticsSymbol))
                    {
                        this.Fail(node, BindingFailure.Duplicate);
                    }
                }

                break;
            case LengthParameterKoto parameter:
                this.Declare(node, parameter.Identifier, BindingSymbolKind.LengthParameter, node, scope).Type = BoundType.ISize;
                break;
            case AliasKoto alias:
                this.aliases.Add(alias);
                break;
        }

        stack.PushChildren(node, scope, IndexEntryKind.Index, IndexEntryKind.Index);
    }

    // An attribute's row: true when its operand is no expression to bind (a Test marker, Layout, a function's LibraryImport, an
    // implementation Attribute), so the walk resets the operand only and walks the leading chain in the attribute's context.
    private bool IndexAttribute(AttributeKoto attribute)
    {
        if (attribute.Kind == AttributeKind.Test)
        {
            if (attribute.Parent is FunctionKoto testOwner && TestDefinition.IsIncluded(this.compilation, testOwner) && TestDefinition.IsValidSyntax(testOwner))
            {
                attribute.BoundType = BoundType.Unit;
                attribute.BindingState = BindingState.Resolved;
            }
            else
            {
                this.IndexInvalidTestAttribute(attribute);
                this.InvalidateAttributeTarget(attribute);
            }

            return true;
        }

        if (attribute.Kind == AttributeKind.Layout)
        {
            this.IndexLayoutAttribute(attribute);
            return true;
        }

        if (attribute.Kind == AttributeKind.LibraryImport && AttributeTarget(attribute) is FunctionKoto)
        {
            // SPEC 22.6: the operand names a library and symbol; it is validated, never evaluated.
            this.ClearFailure(attribute);
            attribute.BindingState = BindingState.Unvisited;
            attribute.BoundType = null;
            this.nodes.Add(attribute);
            return true;
        }

        if (attribute.IsImplementation)
        {
            // IMPL 21.3.4: an implementation Attribute states a fact about its declaration, which the Kimi library's registration
            // owns; its operand is a Name or a code, never an expression to bind. The kinds exist only in the library's sources.
            attribute.BoundType = BoundType.Unit;
            attribute.BindingState = BindingState.Resolved;
            return true;
        }

        // Other selected attributes still bind their own lookup failure.
        this.InvalidateAttributeTarget(attribute);
        return false;
    }

    // A test function the build does not include: product lookup and analysis never visit its signature names or body, which the
    // ExcludedTest context walks.
    private void IndexExcludedTest(FunctionKoto test, AttributeKoto marker, BindingScope scope)
    {
        test.BoundType = BoundType.Unit;
        test.BindingState = BindingState.Resolved;
        if (!TestDefinition.IsValidSyntax(test))
        {
            // Its signature has names to bind, so it stays undeclared: its declaration's own records appear only in a Test
            // build (PLAN G660's records-only part).
            this.IndexInvalidTestAttribute(marker);
            this.InvalidateAttributeTarget(marker);
        }
        else
        {
            // A valid definition's header holds no name to bind: its declaration is declared and judged like any function's
            // (kind conflicts, signatures, inherited Names, virtual eligibility, protected placement); access keeps it from every
            // product candidate set.
            this.DeclareFunction(test, scope);
            this.GetScope(test, scope);
            this.nodes.Add(test);
            for (var attribute = test.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
            {
                if (!ReferenceEquals(attribute, marker))
                {
                    if (attribute.Kind == AttributeKind.Layout)
                    {
                        // The ExcludedTest context reports Layout's wrong target.
                        continue;
                    }

                    // No Mod marker registry exists yet; selection does not recognize an unknown marker.
                    this.ClearFailure(attribute);
                    this.Fail(attribute, BindingFailure.Unsupported, unresolved: true);
                    this.nodes.Add(attribute);
                }
            }
        }

        this.indexStack.PushChildren(test, scope, IndexEntryKind.ExcludedTest, IndexEntryKind.ExcludedTest);
    }

    // The ExcludedTest context's row: a Layout attribute's wrong target, whose operand is reset only, a misplaced Test marker and the
    // marker of a nested function that cannot be a test definition (SPEC 6.7.2).
    private void IndexExcludedTestNode(Koto node, BindingScope scope)
    {
        if (node is AttributeKoto { Kind: AttributeKind.Layout } layout)
        {
            this.IndexLayoutAttribute(layout);
            this.indexStack.PushChildren(layout, scope, IndexEntryKind.ExcludedTest, IndexEntryKind.ResetOnly);
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
            this.IndexInvalidTestAttribute(marker);
        }

        this.indexStack.PushChildren(node, scope, IndexEntryKind.ExcludedTest, IndexEntryKind.ExcludedTest);
    }

    // A match's arms, in order: per arm its Pattern (Pattern context, the arm's scope), its candidates step (in the guard's scope when it
    // has a Guard), its Guard and its Body (the arm's scope).
    private void PushArms(MatchKoto match, BindingScope outer)
    {
        var stack = this.indexStack;
        var start = stack.Count;
        for (var i = 0; i < match.Arms.Count; i++)
        {
            var arm = match.Arms[i];
            var armScope = this.GetScope(arm.Pattern, outer);
            stack.Push(new(arm.Pattern, armScope, IndexEntryKind.Pattern));
            if (arm.Guard is { } guard)
            {
                var guardScope = this.GetScope(guard, outer);
                stack.Push(new(match, guardScope, IndexEntryKind.Candidates, i));
                stack.Push(new(guard, guardScope, IndexEntryKind.Index));
            }
            else
            {
                stack.Push(new(match, armScope, IndexEntryKind.Candidates, i));
            }

            stack.Push(new(arm.Body, armScope, IndexEntryKind.Index));
        }

        stack.Reverse(start);
    }

    // An arm's guard candidates, after its Pattern's subtree, whose binding patterns' Declare set the form's BoundSymbol: a guarded
    // arm's are declared in the guard's scope (its CandidateScopes entry), an unguarded arm's are dropped.
    private void IndexArmCandidates(MatchKoto match, int arm, BindingScope scope)
    {
        var guard = match.Arms[arm].Guard;
        if (guard is not null)
        {
            this.pass.Part.CandidateScopes.Add(guard);
        }

        this.IndexCandidates(match.Arms[arm].Pattern, guard is not null, scope);
    }

    private void IndexCandidates(Koto syntax, bool guarded, BindingScope scope)
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
                this.symbols.Remove(name);
                return;
            }

            if (this.symbols.TryGetValue(name, out var candidate) && candidate.Name != name.IdentifierName)
            {
                this.symbols.Remove(name);
            }

            var bodySymbol = form.BoundSymbol;
            this.Declare(name, name.IdentifierName, BindingSymbolKind.PatternCandidate, form, scope);
            form.BoundSymbol = bodySymbol;
            return;
        }

        foreach (var operand in form.Operands)
        {
            this.IndexCandidates(operand, guarded, scope);
        }
    }

    // A named function's symbol in its member scope, an excluded test's included (G660): the declaration is judged in every build.
    private void DeclareFunction(FunctionKoto function, BindingScope scope)
    {
        var memberScope = scope.Owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ? scope.Parent! : scope;
        var conditional = scope.Owner as SyntaxFormKoto;
        if (IsRootMain(function))
        {
            memberScope = this.ModuleScope(function);
        }

        var symbol = this.Declare(function, function.Name, BindingSymbolKind.Function, function, memberScope);
        symbol.ConditionalDeclaration = conditional is { Akind: KotoKind.ConditionalConformance } ? conditional : null;
        // The members of the fixed array and the integer Position witness are receiver functions of internal
        // Kimi groups (SPEC 22.1, 4.6.1, PLAN G32).
        if (memberScope.Owner is StructKoto or EnumKoto or ContractKoto || this.Library.IsBuiltinMemberGroup(memberScope.Owner))
        {
            for (var p = 0; p < function.Parameters.Count; p++)
            {
                if (function.Parameters[p].InternalName == "self")
                {
                    if (symbol.ReceiverIndex >= 0)
                    {
                        this.Fail(function, BindingFailure.InvalidTypeFormation);
                    }

                    symbol.ReceiverIndex = p;
                }
            }
        }
    }

    private readonly record struct IndexEntry(Koto Node, BindingScope Scope, IndexEntryKind Kind, int Arm = 0);

    // The walk's pending entries: one array, grown by doubling and kept across passes, so a warm Bind allocates nothing. As a visitor
    // it pushes the children Koto.VisitChildren walks, which PushChildren gives their scope and context and reverses, so they pop in
    // slot order.
    private sealed class IndexStack : KotoVisitor
    {
        private IndexEntry[] entries = [];

        internal int Count { get; private set; }

        public override void Visit(Koto node) => this.Push(new(node, null!, IndexEntryKind.Index));

        internal void Push(IndexEntry entry)
        {
            if (this.Count == this.entries.Length)
            {
                Array.Resize(ref this.entries, Math.Max(16, this.Count * 2));
            }

            this.entries[this.Count++] = entry;
        }

        internal IndexEntry Pop()
        {
            var entry = this.entries[--this.Count];
            this.entries[this.Count] = default;
            return entry;
        }

        // A node's children: its leading chain, its first slot (Koto.ForEachSlot), in `chain`; its other slots in `slots`.
        internal void PushChildren(Koto node, BindingScope scope, IndexEntryKind chain, IndexEntryKind slots)
        {
            var start = this.Count;
            node.VisitChildren(this);
            for (var i = start; i < this.Count; i++)
            {
                this.entries[i] = new(this.entries[i].Node, scope, i == start && node.AttributeChain is not null ? chain : slots);
            }

            this.Reverse(start);
        }

        // The entries pushed since `start` reversed, so the first pushed pops first.
        internal void Reverse(int start) => Array.Reverse(this.entries, start, this.Count - start);

        internal void Clear()
        {
            Array.Clear(this.entries, 0, this.Count);
            this.Count = 0;
        }
    }
}
