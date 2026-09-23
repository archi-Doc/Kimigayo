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
        private readonly HashSet<Koto> seen = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<BoundType> types = new(ReferenceEqualityComparer.Instance);
        private readonly List<Koto> pending = new();
        private bool valid;

        public override void Visit(Koto node)
        {
            if (!this.valid || node is FunctionKoto or DeclarationContainerKoto)
            {
                return; // A declaration is not an invocation.
            }

            if (node is FieldKoto local)
            {
                this.Queue(local.InitializerKoto);
                if (local.BoundSymbol?.Type is { } localType)
                {
                    this.Destruction(localType, local);
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

                this.Function(call.Target);
                for (var i = 0; i < call.DefaultArguments.Length; i++)
                {
                    this.Queue(call.DefaultArguments[i].Expression);
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
                    }

                    if (write)
                    {
                        this.Accessor(plan.Setter);
                    }
                }
            }

            if (node.BoundType is { } type)
            {
                this.Destruction(type, node);
            }

            node.VisitChildren(this);
        }

        internal bool Check(BindingSymbol implementation)
        {
            this.seen.Clear();
            this.types.Clear();
            this.pending.Clear();
            this.valid = true;
            this.Function(implementation);
            for (var i = 0; this.valid && i < this.pending.Count; i++)
            {
                this.Visit(this.pending[i]);
            }

            return this.valid;
        }

        private void Function(BindingSymbol symbol)
        {
            if (symbol.CompilerFunction != CompilerFunctionKind.None)
            {
                // These operations use their inputs and the allocator only. A
                // formatting call can invoke arbitrary user effects and is excluded.
                this.valid &= symbol.CompilerFunction is
                    CompilerFunctionKind.Abort or CompilerFunctionKind.Replace or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap or
                    >= CompilerFunctionKind.ArrayReserve and <= CompilerFunctionKind.TextHeap or
                    CompilerFunctionKind.TextWriter or CompilerFunctionKind.TextUtf8 or CompilerFunctionKind.TextValidateUtf8 or
                    >= CompilerFunctionKind.TextRelease and <= CompilerFunctionKind.WindowCommit or CompilerFunctionKind.WriterStatus;
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

            this.Queue(function.Body);
            this.Queue(function.ExpressionBody);
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
                    this.Destruction(parameter, function);
                }
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

        private void Destruction(BoundType type, Koto use)
        {
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
                    this.Function(destructor);
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
            if (node is not null && this.seen.Add(node))
            {
                this.pending.Add(node);
            }
        }
    }
}
