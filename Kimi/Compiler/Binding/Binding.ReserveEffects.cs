// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private ReserveEffects? reserveEffects;

    private void ValidateReserveEffects()
    {
        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var path = this.activeConformancePaths[i];
            if (!path.IsVerified || path.Contract.LibraryDeclaration != KimiDeclarationId.BufferWriter)
            {
                continue;
            }

            for (var w = 0; w < path.WitnessStorage.Count; w++)
            {
                if (!(this.reserveEffects ??= new(this)).Check(path.WitnessStorage[w].Implementation))
                {
                    path.Invalid = true;
                    path.IsVerified = false;
                    path.Identity.Invalid = true;
                    path.Identity.IsVerified = false;
                    Fail(path.Use, BindingFailure.IncompatibleImplementation);
                    break;
                }
            }
        }
    }

    // A reusable transitive walk: recursion shares a visited set and every body,
    // lazy initializer and owning destruction path is checked once per query.
    // No per-value effects or runtime checks are introduced by writer erasure.
    private sealed class ReserveEffects(Binding binding) : KotoVisitor
    {
        private readonly HashSet<(Koto Node, int Context)> seen = new();
        private readonly HashSet<BoundType> types = new(ReferenceEqualityComparer.Instance);
        private readonly List<(Koto Node, int Context)> pending = new();
        private readonly List<BoundCall?> contexts = new();
        private int context;
        private bool valid;

        public override void Visit(Koto node)
        {
            if (!this.valid || node is FunctionKoto or DeclarationContainerKoto)
            {
                return; // A declaration is not an invocation.
            }

            if (node is MacroKoto { Formatting: { Acquisition: { } acquisition } root })
            {
                this.Queue(acquisition.ArgumentNodes[0]);
                foreach (var write in root.Writes)
                {
                    if (write.BoundCall is { } selected)
                    {
                        this.Call(selected);
                    }

                    this.Queue(write.ArgumentNodes[1]);
                }

                return;
            }

            if (node is FieldKoto local)
            {
                this.Queue(local.InitializerKoto);
                if (local.BoundSymbol?.Type is { } localType)
                {
                    this.Destruction(this.Type(localType), local);
                }

                return;
            }

            if (node.BoundSymbol?.Kind is BindingSymbolKind.Type or BindingSymbolKind.TypeParameter)
            {
                return;
            }

            if (node is InvocationKoto invocation && !binding.TryGetEnumConstruction(node, out _))
            {
                if (invocation.BoundCall is not { } call)
                {
                    this.valid = false; // An erased/unknown call has no bounded effect.
                    return;
                }

                this.Call(call);
            }

            if (node is BinaryKoto { ComparisonCall.BoundCall: { } comparison })
            {
                this.Call(comparison);
            }

            if (node.Formatting is { } formatting)
            {
                foreach (var write in formatting.Writes)
                {
                    if (write.BoundCall is { } selected)
                    {
                        this.Call(selected);
                    }
                }
            }

            if (node.BoundSymbol is { Declaration: PropertyKoto property } symbol && node is IdentifierNameKoto or MemberAccessKoto)
            {
                if (symbol.Scope.Owner is GroupKoto)
                {
                    if (property.VariableKind == VariableKind.Var)
                    {
                        this.valid = false; // Reading ambient mutable storage is also forbidden.
                        return;
                    }

                    this.Queue(property.InitializerKoto);
                }

                if (symbol.Property is { } plan)
                {
                    var parent = node.Parent is MemberAccessKoto member && ReferenceEquals(member.Right, node) ? member.Parent : node.Parent;
                    var write = parent is BinaryKoto binary && binary.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals && ReferenceEquals(binary.Left, node);
                    var read = !write || parent!.Akind != KotoKind.Equals;
                    if (read)
                    {
                        this.Accessor(plan.Getter);
                        if (!plan.Getter.IsStandard && node.BoundType is { } result)
                        {
                            this.Destruction(this.Type(result), node);
                        }
                    }

                    if (write)
                    {
                        this.Accessor(plan.Setter);
                    }
                }
            }

            if (node is BinaryKoto { Akind: KotoKind.Equals, Left.BoundType: { } replaced })
            {
                this.Destruction(this.Type(replaced), node);
            }

            // A borrowed receiver/field access creates no owned temporary and
            // does not execute that value's destructor. Calls and explicit Moves
            // do acquire owned results; local/parameter destruction is checked separately.
            if (node is InvocationKoto or ConversionKoto { ConversionBinding: ConversionBinding.Transfer } && node.BoundType is { } type)
            {
                this.Destruction(this.Type(type), node);
            }

            node.VisitChildren(this);
        }

        internal bool Check(BindingSymbol implementation)
        {
            this.seen.Clear();
            this.types.Clear();
            this.pending.Clear();
            this.contexts.Clear();
            this.contexts.Add(null);
            this.context = 0;
            this.valid = true;
            this.Function(implementation);
            for (var i = 0; this.valid && i < this.pending.Count; i++)
            {
                this.context = this.pending[i].Context;
                this.Visit(this.pending[i].Node);
            }

            return this.valid;
        }

        private void Call(BoundCall call)
        {
            if (this.contexts[this.context] is { } outer)
            {
                if (binding.InstantiateForwardedCall(call, outer) is not { } concrete)
                {
                    this.valid = false;
                    return;
                }

                call = concrete;
            }

            var caller = this.context;
            this.context = this.Context(call);
            for (var i = 0; i < call.DefaultArguments.Length; i++)
            {
                this.Queue(call.DefaultArguments[i].Expression);
            }

            this.context = caller;

            if (call.Target.CompilerFunction is CompilerFunctionKind.ArrayClear or CompilerFunctionKind.Replace)
            {
                var receiver = call.ReceiverOperation.ParameterType;
                for (var i = 0; receiver is null && i < call.ArgumentOperations.Length; i++)
                {
                    if (call.ArgumentOperations[i].ParameterIndex == 0)
                    {
                        receiver = call.ArgumentOperations[i].ParameterType;
                    }
                }

                var storage = receiver is null ? null : this.Type(receiver);
                if (storage is { Semantics: SemanticsKind.Uniq, Components.Count: 1 })
                {
                    storage = storage.Components[0];
                }

                this.Destruction(storage, call.Target.Declaration);
            }

            if (call.Target.CompilerFunction is CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat)
            {
                if (call.TypeArguments.Length == 0 || call.TypeArguments[0] is not { } valueType)
                {
                    this.valid = false;
                }
                else if (!FormattingTypes.IsBuiltin(valueType))
                {
                    if (binding.RequirementImplementation(call, valueType, KimiDeclarationId.Utf8Format) is { } implementation)
                    {
                        this.Function(implementation.Target, implementation);
                    }
                    else
                    {
                        this.valid = false;
                    }
                }

                return;
            }

            if (call.Target.CompilerFunction is CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare && ComparisonTypes.IsComposite(call.ConformingType))
            {
                this.Comparison(binding.ComparisonPlan(call));
                return;
            }

            this.Function(call.Target, call);
        }

        private void Comparison(BoundComparison? plan)
        {
            if (plan is null)
            {
                this.valid = false;
                return;
            }

            if (plan.Implementation is { } implementation)
            {
                this.Function(implementation.Target, implementation);
            }

            foreach (var part in plan.Parts)
            {
                this.Comparison(part);
            }
        }

        private void Function(BindingSymbol symbol, BoundCall? call = null)
        {
            if (symbol.CompilerFunction != CompilerFunctionKind.None)
            {
                // These operations use their inputs and the allocator only.
                // Formatting dispatch is checked through its selected witness.
                this.valid &= symbol.CompilerFunction is
                    CompilerFunctionKind.Abort or CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap or
                    >= CompilerFunctionKind.ArrayReserve and <= CompilerFunctionKind.TextHeap or
                    CompilerFunctionKind.TextWriter or CompilerFunctionKind.TextUtf8 or CompilerFunctionKind.TextValidateUtf8 or
                    >= CompilerFunctionKind.TextRelease and <= CompilerFunctionKind.WindowCommit or CompilerFunctionKind.WriterStatus or CompilerFunctionKind.BuiltinFormat or
                    CompilerFunctionKind.BuiltinEquals or CompilerFunctionKind.BuiltinCompare;
                return;
            }

            if (symbol.Declaration is not FunctionKoto function)
            {
                this.valid = false;
                return;
            }

            if (function.IsRequirement && symbol.Scope.Owner.BoundSymbol?.LibraryDeclaration == KimiDeclarationId.BufferWriter)
            {
                return; // Calls through this requirement have the same declared upper bound.
            }

            if (function.Body is null && function.ExpressionBody is null && !(function.IsConstructor && function.IsGenerated))
            {
                this.valid = false;
                return;
            }

            var previous = this.context;
            this.context = this.Context(call);
            try
            {
                var selected = (call is null ? null : binding.SelectSpecialization(call)) ?? function;
                this.Queue(selected.Body);
                this.Queue(selected.ExpressionBody);
                if (function.IsConstructor && StructStorage.ReceiverType(function) is { } receiver)
                {
                    for (var i = 0; i < StructStorage.Count(receiver); i++)
                    {
                        this.Queue(StructStorage.Field(receiver, i).InitializerKoto);
                    }
                }

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].Type.BoundType is { } parameter)
                    {
                        this.Destruction(this.Type(parameter), function);
                    }
                }
            }
            finally
            {
                this.context = previous;
            }
        }

        private void Accessor(BoundAccessor accessor)
        {
            if (!accessor.IsPresent || accessor.IsStandard)
            {
                return;
            }

            if (accessor.Declaration?.Body is { } body)
            {
                this.Queue(body);
            }
            else
            {
                this.valid = false;
            }
        }

        private void Destruction(BoundType? type, Koto use)
        {
            if (type is null)
            {
                this.valid = false;
                return;
            }

            if (type.Semantics != SemanticsKind.Owner || !this.types.Add(type))
            {
                return;
            }

            if (type.Kind == BoundTypeKind.Parameter)
            {
                this.valid &= binding.ProveCopy(type, use) == ConstraintProof.Proven;
            }
            else if (StructStorage.IsStruct(type))
            {
                if (StructStorage.Destructor(type)?.BoundSymbol is { } destructor)
                {
                    var call = new BoundCall();
                    call.Set(destructor, BoundType.Unit, null, [], [], declaringType: type);
                    this.Function(destructor, call);
                }

                for (var i = 0; i < StructStorage.Count(type); i++)
                {
                    if (StructStorage.FieldType(type, i) is { } field)
                    {
                        this.Destruction(field, use);
                    }
                }
            }
            else
            {
                for (var i = 0; i < type.Components.Count; i++)
                {
                    this.Destruction(type.Components[i], use);
                }

                if (type.StoredCases is { } cases)
                {
                    for (var i = 0; i < cases.Length; i++)
                    {
                        this.Destruction(cases[i], use);
                    }
                }
            }
        }

        private void Queue(Koto? node)
        {
            if (node is not null && this.seen.Add((node, this.context)))
            {
                this.pending.Add((node, this.context));
            }
        }

        private BoundType? Type(BoundType type)
            => this.contexts[this.context] is { } call ? binding.InstantiateStorageType(type, call) : type;

        private int Context(BoundCall? call)
        {
            if (call is null || (call.TypeArguments.Length == 0 && call.LengthArguments.Length == 0 && call.DeclaringType?.Components.Count is null or 0))
            {
                return 0;
            }

            for (var i = 1; i < this.contexts.Count; i++)
            {
                var existing = this.contexts[i]!;
                if (ReferenceEquals(existing.Target, call.Target) && ReferenceEquals(existing.DeclaringType, call.DeclaringType) &&
                    existing.TypeArguments.SequenceEqual(call.TypeArguments) && existing.LengthArguments.SequenceEqual(call.LengthArguments))
                {
                    return i;
                }
            }

            if (this.contexts.Count >= 1024)
            {
                this.valid = false; // An unbounded effect expansion cannot prove conformance.
                return 0;
            }

            this.contexts.Add(call);
            return this.contexts.Count - 1;
        }
    }
}
