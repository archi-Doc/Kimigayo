// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Owns reusable Binding storage and updates semantic fields on the existing Koto tree.</summary>
public sealed partial class Binding
{
    private readonly Compilation compilation;
    private readonly List<Koto> nodes = new(256);
    private readonly Dictionary<object, BindingScope> scopes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, BindingSymbol> symbols = new(ReferenceEqualityComparer.Instance);
    private readonly List<AliasKoto> aliases = new();
    private readonly List<BindingIssue> issues = new();
    private readonly IndexVisitor indexer;
    private readonly Dictionary<int, List<BoundType>> types = new();
    private readonly Dictionary<GenericParameterKoto, BindingSymbol> pairSymbols = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(Koto Binder, OriginKind Kind, int Slot), BoundOrigin> originAtoms = new();
    private readonly Dictionary<int, List<BoundOrigin>> originExpressions = new();
    private readonly List<BindingObligation> obligations = new();
    private readonly HashSet<BindingObligation> obligationSet = new();
    private readonly HashSet<Koto> resolvingTypes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingSymbol> borrowVisiting = new(ReferenceEqualityComparer.Instance);
    private BindingScope rootScope = null!;
    private bool running;
    private bool coreValid;

    internal Binding(Compilation compilation)
    {
        this.compilation = compilation;
        this.indexer = new(this);
        this.TypeSystem = new BindingControlFlowTypes();
        this.Core = new(compilation);
        this.symbols.Add(this.Core.WriteLine.Declaration, this.Core.WriteLine);
        this.symbols.Add(this.Core.Option.Declaration, this.Core.Option);
        this.symbols.Add(this.Core.Result.Declaration, this.Core.Result);
    }

    /// <summary>Gets the latest pass summary. Results are replaced by the next Bind.</summary>
    public BindingResult Result { get; private set; }

    /// <summary>Gets the compiler-designated requirement identities for this compilation.</summary>
    public CoreIntrinsics Core { get; }

    /// <summary>Gets final failures; provisional passes do not publish missing-name diagnostics.</summary>
    public IReadOnlyList<BindingIssue> Issues => this.issues;

    /// <summary>Gets requirements to discharge during subsequent semantic analysis.</summary>
    public IReadOnlyList<BindingObligation> Obligations => this.obligations;

    /// <summary>Checks the latest final Binding without resolving names or rebuilding the tree.</summary>
    /// <returns>The final semantic completeness summary.</returns>
    public BindingResult CheckBound()
    {
        if (this.Result.Mode != BindingMode.Final)
        {
            throw new InvalidOperationException("Bound checking requires a final Binding pass.");
        }

        this.issues.Clear();
        return this.Result = this.Check(BindingMode.Final);
    }

    /// <summary>Gets the bridge supplying actual Binding facts to control-flow analysis.</summary>
    public ControlFlowTypeSystem TypeSystem { get; }

    /// <summary>Rebinds all selected syntax, reusing nodes, symbols, types, scopes, and collection capacity.</summary>
    /// <param name="mode">Whether this is a provisional or final pass.</param>
    /// <returns>The current pass summary.</returns>
    public BindingResult Bind(BindingMode mode)
    {
        if (this.running)
        {
            throw new InvalidOperationException("Binding cannot be reentered.");
        }

        this.running = true;
        try
        {
            this.issues.Clear();
            this.ResetStartup();
            this.compilation.InvalidateOwnership();
            this.receiverOperations.Clear();
            foreach (var construction in this.enumConstructions.Values)
            {
                construction.IsValid = false;
            }

            this.nodes.Clear();
            this.aliases.Clear();
            this.obligations.Clear();
            this.obligationSet.Clear();
            this.ResetCapabilities(mode);
            this.ResetContracts();
            foreach (var scope in this.scopes.Values)
            {
                scope.Reset();
            }

            foreach (var symbol in this.symbols.Values)
            {
                if (symbol.Property is { } property)
                {
                    property.IsVerified = false;
                }

                if (symbol.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget))
                {
                    symbol.Type = null;
                }

                symbol.Next = null;
                symbol.ConditionalDeclaration = null;
                symbol.Resolving = false;
                symbol.HeaderBound = false;
                symbol.ReceiverIndex = -1;
            }

            // The indexer resets every semantic field before any header or expression is evaluated.
            this.rootScope = this.GetScope(this.compilation.Kotonoha.RootKoto, null);
            this.indexer.Scope = this.rootScope;
            this.indexer.Visit(this.compilation.Kotonoha.RootKoto);
            this.Core.Restore();
            this.coreValid = this.Core.IsValid;
            if (!this.coreValid)
            {
                Fail(this.compilation.Kotonoha.RootKoto, BindingFailure.InvalidCore);
            }

            this.scopes[this.Core.Kotonoha.RootKoto] = this.Core.Scope;
            this.indexer.Scope = this.Core.Scope;
            // These declarations use ordinary indexing, schemas, storage and constraints.
            this.Core.Scope.Types.Remove("Option");
            this.Core.Scope.Types.Remove("Result");
            this.indexer.Visit(this.Core.Option.Declaration);
            this.indexer.Visit(this.Core.Result.Declaration);
            this.indexer.Visit(this.Core.WriteLine.Declaration);
            this.BindSchemas();
            this.PrepareContracts();
            this.BindConstraints();
            for (var i = 0; i < this.nodes.Count; i++)
            {
                if (this.nodes[i].BoundSymbol is { Kind: BindingSymbolKind.Function or BindingSymbolKind.Property } symbol && ReferenceEquals(symbol.Declaration, this.nodes[i]))
                {
                    this.BindHeader(symbol);
                }
            }

            this.ValidateBaseDeclarations();
            this.PrepareStorage();
            this.ComputeOriginRequirements();
            this.ValidateSignatures();
            this.ValidateContractDeclarations();
            this.capabilitiesReady = true;
            this.ValidateConformances(mode, false);
            this.ValidateConstraintEnvironments();
            this.BindNode(this.compilation.Kotonoha.RootKoto, this.rootScope);
            this.BindNode(this.Core.WriteLine.Declaration, this.scopes[this.Core.WriteLine.Declaration]);
            this.BindNode(this.Core.Option.Declaration, this.Core.Scope);
            this.BindNode(this.Core.Result.Declaration, this.Core.Scope);
            this.ClearCapabilityResults();
            this.ValidateCopyDeclarations(mode);
            this.ValidateProperties(mode);
            this.ComputeOriginRequirements();
            this.ValidateOriginRequirements();
            this.ValidateConformances(mode, true);
            this.ValidateConstraintUses(mode);
            this.ClearCapabilityResults();
            this.CompleteEnumAcquisitions();
            this.Result = this.Check(mode);
            return this.Result;
        }
        finally
        {
            this.running = false;
        }
    }

    /// <summary>Publishes final diagnostics to the source contexts. Invoke after final analysis, not between Mods.</summary>
    public void ReportDiagnostics()
    {
        for (var i = 0; i < this.issues.Count; i++)
        {
            var issue = this.issues[i];
            issue.Node.AddDiagnostic(issue.Code);
        }
    }

    internal BindingSymbol ParameterSymbol(FunctionKoto function, int index) => this.symbols[function.Parameters[index]];

    private static BoundType? Fail(Koto node, BindingFailure failure, bool unresolved = false)
    {
        if (node.BindingFailure != BindingFailure.None)
        {
            return null;
        }

        node.BindingState = unresolved ? BindingState.Unresolved : BindingState.Invalid;
        node.BindingFailure = failure;
        return null;
    }

    private static BoundType? Complete(Koto node, BoundType? type)
    {
        node.BoundType = type;
        if (node.BindingState != BindingState.Invalid)
        {
            node.BindingState = type is null ? BindingState.Unresolved : BindingState.Resolved;
        }

        return type;
    }

    private static bool SignatureSlotEquals(BindingSymbol? a, BindingSymbol? b, Koto aBinder, Koto bBinder)
        => ReferenceEquals(a, b) || (a is not null && b is not null && ReferenceEquals(a.Scope.Owner, aBinder) && ReferenceEquals(b.Scope.Owner, bBinder) && a.Slot == b.Slot);

    private static bool SignatureEquals(BoundType a, BoundType b, Koto aBinder, Koto bBinder)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Kind != b.Kind || a.Semantics != b.Semantics || a.Length != b.Length || !SameLengthSignature(a.LengthExpression, b.LengthExpression, aBinder, bBinder) || a.Components.Count != b.Components.Count)
        {
            return false;
        }

        if (a.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection)
        {
            return SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder);
        }

        if (a.Components.Count == 0)
        {
            return a.Symbol is not null && ReferenceEquals(a.Symbol, b.Symbol);
        }

        if (a.Kind == BoundTypeKind.SemanticsApplication ? !SignatureSlotEquals(a.Symbol, b.Symbol, aBinder, bBinder) : a.Symbol != b.Symbol)
        {
            return false;
        }

        for (var i = 0; i < a.Components.Count; i++)
        {
            if (!SignatureEquals(a.Components[i], b.Components[i], aBinder, bBinder))
            {
                return false;
            }
        }

        return true;
    }

    private BindingResult Check(BindingMode mode)
    {
        if (mode == BindingMode.Final)
        {
            for (var i = 0; i < this.obligations.Count; i++)
            {
                if (this.obligations[i].Deadline == BindingDeadline.Definition)
                {
                    Fail(this.obligations[i].Use, BindingFailure.UnprovenConstraint);
                }
            }
        }

        var resolved = 0;
        var unresolved = 0;
        var invalid = 0;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node.BindingState == BindingState.Unvisited)
            {
                Fail(node, BindingFailure.Unsupported, true);
            }

            switch (node.BindingState)
            {
                case BindingState.Resolved:
                    resolved++;
                    break;
                case BindingState.Invalid:
                    invalid++;
                    break;
                default:
                    unresolved++;
                    break;
            }

            if (mode == BindingMode.Final && node.BindingState != BindingState.Resolved && node.BindingFailure != BindingFailure.None)
            {
                var code = node.BindingFailure switch
                {
                    BindingFailure.MissingName or BindingFailure.MissingType => DiagnosticCode.UnresolvedBinding_Kd,
                    BindingFailure.Ambiguous => DiagnosticCode.AmbiguousBinding_Kd,
                    BindingFailure.Duplicate => DiagnosticCode.DuplicateBinding_Kd,
                    BindingFailure.TypeMismatch => DiagnosticCode.TypeMismatch_Kd,
                    BindingFailure.NotCallable => DiagnosticCode.NotCallable_Kd,
                    BindingFailure.NoApplicableCandidate => DiagnosticCode.NoApplicableOverload_Kd,
                    BindingFailure.Cycle => DiagnosticCode.CyclicBinding_Kd,
                    BindingFailure.InvalidAssignment => DiagnosticCode.InvalidAssignment_Kd,
                    BindingFailure.InvalidLiteral => DiagnosticCode.InvalidNumericLiteral_Kd,
                    BindingFailure.Access => DiagnosticCode.InaccessibleBinding_Kd,
                    BindingFailure.Capture => DiagnosticCode.InvalidCaptureBinding_Kd,
                    BindingFailure.InvalidOrigin => DiagnosticCode.InvalidOriginBinding_Kd,
                    BindingFailure.MissingOrigin => DiagnosticCode.MissingOriginBinding_Kd,
                    BindingFailure.InvalidTypeFormation => DiagnosticCode.InvalidTypeFormation_Kd,
                    BindingFailure.InvalidConstraint => DiagnosticCode.InvalidConstraint_Kd,
                    BindingFailure.UnprovenConstraint => DiagnosticCode.UnprovenConstraint_Kd,
                    BindingFailure.UnsatisfiedConstraint => DiagnosticCode.UnsatisfiedConstraint_Kd,
                    BindingFailure.InvalidCore => DiagnosticCode.InvalidCoreIntrinsics_Kd,
                    BindingFailure.MissingImplementation => DiagnosticCode.MissingContractImplementation_Kd,
                    BindingFailure.IncompatibleImplementation => DiagnosticCode.IncompatibleContractImplementation_Kd,
                    BindingFailure.InvalidAssociatedType => DiagnosticCode.InvalidAssociatedType_Kd,
                    _ => DiagnosticCode.UnsupportedBinding_Kd,
                };
                this.issues.Add(new(node, code));
            }
        }

        if (mode == BindingMode.Final && unresolved != 0 && this.issues.Count == 0)
        {
            for (var i = 0; i < this.nodes.Count; i++)
            {
                if (this.nodes[i].BindingState == BindingState.Unresolved)
                {
                    this.issues.Add(new(this.nodes[i], DiagnosticCode.UnresolvedBinding_Kd));
                    break;
                }
            }
        }

        return new(mode, resolved, unresolved, invalid);
    }

    private BindingScope GetScope(object key, BindingScope? parent, Koto? owner = null)
    {
        if (!this.scopes.TryGetValue(key, out var scope))
        {
            scope = new(owner ?? (Koto)key);
            this.scopes.Add(key, scope);
        }

        scope.Parent = parent;
        scope.Function = scope.Owner is FunctionKoto function ? function : parent?.Function;
        return scope;
    }

    private BindingScope NodeScope(Koto node, BindingScope fallback)
    {
        if (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } source && this.scopes.TryGetValue(source, out var sourceScope))
        {
            fallback = sourceScope;
        }

        return this.scopes.TryGetValue(node, out var scope) ? scope : fallback;
    }

    private BindingSymbol Declare(object key, string name, BindingSymbolKind kind, Koto node, BindingScope scope)
    {
        if (!this.symbols.TryGetValue(key, out var symbol))
        {
            symbol = new(name, kind, node, scope);
            this.symbols.Add(key, symbol);
        }

        symbol.Scope = scope;
        var table = kind is BindingSymbolKind.Container or BindingSymbolKind.Type or BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.AssociatedType ? scope.Types : scope.Values;
        if (table.TryGetValue(name, out var previous))
        {
            symbol.Next = previous;
            if (kind != BindingSymbolKind.Function || previous.Kind != BindingSymbolKind.Function)
            {
                Fail(node, BindingFailure.Duplicate);
                Fail(previous.Declaration, BindingFailure.Duplicate);
            }
        }

        table[name] = symbol;
        node.BoundSymbol = symbol;
        return symbol;
    }

    private void BindHeader(BindingSymbol symbol)
    {
        if (symbol.HeaderBound || symbol.Resolving)
        {
            return;
        }

        symbol.Resolving = true;
        if (symbol.Declaration is FunctionKoto function)
        {
            var scope = this.scopes[function];
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                this.symbols[parameter].Type = this.BindType(parameter.Type, scope);
            }

            // Named signatures never infer a result from their body or callers (SPEC 10.5).
            symbol.Type = function.ReturnType is { } result ? this.BindType(result, scope) : BoundType.Unit;
            if (symbol.ReceiverIndex >= 0)
            {
                var receiver = function.Parameters[symbol.ReceiverIndex];
                var type = receiver.Type.BoundType;
                var valid = type is not null && SameType(EffectiveCore(type), this.SelfType(symbol.Scope.Owner.BoundSymbol!)) && type.Semantics is not (SemanticsKind.Unsafe or SemanticsKind.Parameter);
                if (!valid || receiver.ExternalName != "self" || receiver.IsOptional || receiver.DefaultValue is not null)
                {
                    Fail(function, BindingFailure.InvalidTypeFormation);
                }
            }
        }
        else if (symbol.Property is { } property)
        {
            this.BindPropertyHeader(property);
        }
        else if (symbol.Declaration is VariableKoto variable && variable.TypeKoto is { } type)
        {
            symbol.Type = this.BindType(type, symbol.Scope);
        }

        symbol.HeaderBound = true;
        symbol.Resolving = false;
    }

    private void ValidateSignatures()
    {
        foreach (var scope in this.scopes.Values)
        {
            foreach (var first in scope.Values.Values)
            {
                for (var a = first; a is not null; a = a.Next)
                {
                    if (a.Declaration is not FunctionKoto fa)
                    {
                        continue;
                    }

                    for (var b = a.Next; b is not null; b = b.Next)
                    {
                        if (b.Declaration is not FunctionKoto fb || fa.GenericArguments.Count != fb.GenericArguments.Count || fa.Parameters.Count != fb.Parameters.Count)
                        {
                            continue;
                        }

                        var equal = true;
                        for (var i = 0; i < fa.Parameters.Count; i++)
                        {
                            var ta = fa.Parameters[i].Type.BoundType;
                            var tb = fb.Parameters[i].Type.BoundType;
                            if (ta is null || tb is null || !SignatureEquals(ta, tb, fa, fb))
                            {
                                equal = false;
                                break;
                            }
                        }

                        if (equal)
                        {
                            Fail(fa, BindingFailure.Duplicate);
                            Fail(fb, BindingFailure.Duplicate);
                        }
                    }
                }
            }
        }
    }

    private sealed class IndexVisitor(Binding binding) : KotoVisitor
    {
        internal BindingScope Scope { get; set; } = null!;

        public override void Visit(Koto node)
        {
            node.BindingState = BindingState.Unvisited;
            node.BindingFailure = BindingFailure.None;
            node.BoundMeaning = null;
            node.BoundSymbol = null;
            if (node is IsKoto clause)
            {
                clause.BoundConstraint = null;
            }

            binding.nodes.Add(node);
            var previous = this.Scope;
            if (node.Parent is CodeBlockKoto { Parent: FunctionKoto { IsGenerated: true } } && node.CodeContext.SourceDocument is { } source)
            {
                this.Scope = binding.GetScope(source, binding.rootScope, node.Parent);
            }

            switch (node)
            {
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
                            symbol.Type ??= new(container.Name, BoundTypeKind.Nominal, symbol);
                        }
                    }

                    this.Scope = container.IsRoot ? binding.rootScope : binding.GetScope(node, this.Scope);
                    break;
                case FunctionKoto function:
                    if (!function.IsGenerated && !function.IsAnonymous)
                    {
                        var memberScope = this.Scope.Owner is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } ? this.Scope.Parent! : this.Scope;
                        var conditional = this.Scope.Owner as SyntaxFormKoto;
                        if (IsRootMain(function))
                        {
                            memberScope = binding.rootScope;
                        }

                        var symbol = binding.Declare(node, function.Name, BindingSymbolKind.Function, node, memberScope);
                        symbol.ConditionalDeclaration = conditional is { Akind: KotoKind.ConditionalConformance } ? conditional : null;
                        if (memberScope.Owner is StructKoto or EnumKoto or ContractKoto)
                        {
                            for (var p = 0; p < function.Parameters.Count; p++)
                            {
                                if (function.Parameters[p].InternalName == "self")
                                {
                                    if (symbol.ReceiverIndex >= 0)
                                    {
                                        Fail(function, BindingFailure.InvalidTypeFormation);
                                    }

                                    symbol.ReceiverIndex = p;
                                }
                            }
                        }
                    }

                    this.Scope = binding.GetScope(node, this.Scope);
                    for (var i = 0; i < function.Parameters.Count; i++)
                    {
                        var parameter = function.Parameters[i];
                        var symbol = binding.Declare(parameter, parameter.InternalName, BindingSymbolKind.Parameter, node, this.Scope);
                        symbol.Slot = i;
                    }

                    node.BoundSymbol = binding.symbols.GetValueOrDefault(node);
                    break;
                case PropertyAccessorKoto accessor:
                    this.Scope = binding.GetScope(node, this.Scope);
                    binding.IndexAccessor(accessor, this.Scope);
                    break;
                case CodeBlockKoto:
                    if (node.Parent is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance })
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
                        Reset(bound.Getter, property.GetAccessor(PropertyAccessorKind.Get), true);
                        Reset(bound.Setter, property.GetAccessor(PropertyAccessorKind.Set), property.DeclarationKind == PropertyDeclarationKind.Var || property.GetAccessor(PropertyAccessorKind.Set) is not null);
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
                            Fail(node, BindingFailure.Duplicate);
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
    }
}
