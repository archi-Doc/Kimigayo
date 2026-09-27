// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private IteratorEffects? iteratorEffects;

    private void ValidateIteratorEffects()
    {
        for (var i = 0; i < this.activeConformancePaths.Count; i++)
        {
            var path = this.activeConformancePaths[i];
            if (!path.IsVerified || !IsRefinement(path.Contract, this.Library.Iterator))
            {
                continue;
            }

            for (var w = 0; w < path.WitnessStorage.Count; w++)
            {
                var witness = path.WitnessStorage[w];
                if (ReferenceEquals(witness.Requirement.Scope.Owner, this.Library.LendingIterator.Declaration) &&
                    witness.Implementation.Declaration is FunctionKoto function &&
                    !(this.iteratorEffects ??= new(this)).Check(function))
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

    // SPEC 22.1.2.4: a previous item's external Loans remain live throughout the next step.
    // The receiver's own Origin is distinct from the stored source Origins of the published result.
    private sealed class IteratorEffects(Binding binding) : KotoVisitor
    {
        private readonly HashSet<(Koto Node, int Context)> seen = new();
        private readonly HashSet<BoundType> destroyed = new(ReferenceEqualityComparer.Instance);
        private readonly List<(Koto Node, int Context)> pending = new();
        private readonly List<BoundCall?> contexts = new();
        private readonly List<BoundCall> forwarded = new();
        private BoundType result = BoundType.Unit;
        private int context;
        private int forwardedCount;
        private bool valid;

        public override void Visit(Koto node)
        {
            if (!this.valid || node is FunctionKoto or DeclarationContainerKoto)
            {
                return;
            }

            if (node is FieldKoto local)
            {
                this.Queue(local.InitializerKoto);
                this.Destruction(local.BoundSymbol?.Type is { } localType ? this.Type(localType) : null, local);
                return;
            }

            if (node is BinaryKoto assignment && assignment.Akind is >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals)
            {
                this.Access(PlaceReference(assignment.Left), LoanRequirement.Uniq, node);
                this.Setter(assignment.Left, node);
                if (assignment.Akind == KotoKind.Equals && assignment.Left.BoundType is { } replaced)
                {
                    this.Destruction(this.Type(replaced), node);
                }
            }
            else if (node is UnaryKoto update && ElementAccess.UpdateOperator(update.Akind) != KotoKind.Invalid)
            {
                this.Access(PlaceReference(update.Operand), LoanRequirement.Uniq, node);
                this.Setter(update.Operand, node);
            }
            else if (node is ConversionKoto { ConversionBinding: ConversionBinding.Borrow } borrow)
            {
                this.Access(borrow.BoundType, borrow.BoundType?.Semantics == SemanticsKind.Uniq ? LoanRequirement.Uniq : LoanRequirement.Ref, node);
            }
            else if (node is ConversionKoto { ConversionBinding: ConversionBinding.Follow } follow)
            {
                this.Access(follow.Left.BoundType, LoanRequirement.Ref, node);
            }
            else if (node is MemberAccessKoto or IndexKoto)
            {
                this.Access(PlaceReference(node), LoanRequirement.Ref, node);
            }

            if (node is InvocationKoto invocation && !binding.TryGetEnumConstruction(node, out _))
            {
                if (invocation.BoundCall is { } call)
                {
                    this.Call(call, node);
                }
                else
                {
                    this.valid = false; // An indirect call has no published independence bound yet.
                }
            }

            if (node is BinaryKoto { ComparisonCall.BoundCall: { } comparison })
            {
                this.Call(comparison, node);
            }

            if (binding.PropertyCall(node, PropertyAccessorKind.Get)?.BoundCall is { } getter)
            {
                this.Call(getter, node);
            }

            if (node is InvocationKoto or ConversionKoto { ConversionBinding: ConversionBinding.Transfer } && node.BoundType is { } temporary)
            {
                this.Destruction(this.Type(temporary), node);
            }

            node.VisitChildren(this);
        }

        internal bool Check(FunctionKoto function)
        {
            this.result = function.BoundSymbol!.Type!;
            this.valid = true;
            this.seen.Clear();
            this.destroyed.Clear();
            this.pending.Clear();
            this.contexts.Clear();
            this.contexts.Add(null);
            this.context = 0;
            this.forwardedCount = 0;
            if (this.result.CarriesOrigin)
            {
                this.Queue(function.Body);
                this.Queue(function.ExpressionBody);
                for (var i = 0; this.valid && i < this.pending.Count; i++)
                {
                    this.context = this.pending[i].Context;
                    this.Visit(this.pending[i].Node);
                }
            }

            return this.valid;
        }

        private static BoundType? PlaceReference(Koto node)
        {
            node = KotoHelper.UnwrapParentheses(node);
            return node switch
            {
                ConversionKoto { ConversionBinding: ConversionBinding.Follow } follow => follow.Left.BoundType,
                BinaryKoto part when ElementAccess.IsSyntax(part) => ReferenceTypes.IsBorrow(part.Left.BoundType) || part.Left.BoundType?.Kind == BoundTypeKind.Slice
                    ? part.Left.BoundType : PlaceReference(part.Left),
                _ => null,
            };
        }

        private static bool MaySelect(Specialization candidate, BoundCall call)
        {
            for (var i = 0; i < call.TypeArguments.Length; i++)
            {
                if (call.TypeArguments[i] is { } argument && candidate.Arguments[i] is { } closed && !MayMatch(argument, closed))
                {
                    return false;
                }
            }

            for (var i = 0; i < call.LengthArguments.Length; i++)
            {
                if (call.LengthArguments[i] is { IsConstant: true } length && !SameLengthSignature(length, candidate.Lengths[i], null!, null!))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MayMatch(BoundType pattern, BoundType closed)
        {
            if (ReferenceEquals(pattern, closed) || pattern.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection)
            {
                return true;
            }

            if (pattern.Kind == BoundTypeKind.Primitive || pattern.Kind != closed.Kind || pattern.Semantics != closed.Semantics ||
                !ReferenceEquals(pattern.Symbol, closed.Symbol) || pattern.Components.Count != closed.Components.Count ||
                (pattern.LengthExpression is null && pattern.Length != closed.Length))
            {
                return false;
            }

            for (var i = 0; i < pattern.Components.Count; i++)
            {
                if (!MayMatch(pattern.Components[i], closed.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void Access(BoundType? type, LoanRequirement mode, Koto use)
        {
            if (type is not null && this.Type(type)?.Origin is { } origin && this.Conflicts(this.result, origin, mode, use))
            {
                this.valid = false;
            }
        }

        private void Call(BoundCall call, Koto use)
        {
            this.Argument(call.ReceiverOperation, use);
            foreach (var argument in call.ArgumentOperations)
            {
                this.Argument(argument, use);
            }

            if (this.contexts[this.context] is { } outer)
            {
                if (binding.InstantiateForwardedCall(call, outer, this.NextCall()) is not { } instantiated)
                {
                    this.valid = false;
                    return;
                }

                call = instantiated;
            }

            var previous = this.context;
            this.context = this.Context(call);
            foreach (var omitted in call.DefaultArguments)
            {
                this.Queue(omitted.Expression);
            }

            if (call.Target.CompilerFunction != CompilerFunctionKind.None)
            {
                // These recognized operations act only on their acquired inputs and platform-owned state.
                this.valid &= call.Target.CompilerFunction is CompilerFunctionKind.Abort or CompilerFunctionKind.WriteLine or
                    CompilerFunctionKind.WriteLineUtf8 or CompilerFunctionKind.Exchange or CompilerFunctionKind.Swap or CompilerFunctionKind.MakeObj;
            }
            else if (call.Target.Declaration is FunctionKoto function)
            {
                if (binding.SelectSpecialization(call) is { } selected)
                {
                    this.Specialization(selected, call);
                }
                else
                {
                    this.Function(function, call);
                    if (binding.specializationsByOriginal.TryGetValue(call.Target, out var candidates))
                    {
                        for (var i = 0; i < candidates.Count; i++)
                        {
                            if (MaySelect(binding.specializations[candidates[i]], call))
                            {
                                this.Specialization(candidates[i], call);
                            }
                        }
                    }
                }
            }
            else
            {
                this.valid = false;
            }

            this.context = previous;
        }

        private BoundCall NextCall()
        {
            if (this.forwardedCount == this.forwarded.Count)
            {
                this.forwarded.Add(new());
            }

            return this.forwarded[this.forwardedCount++];
        }

        private void Specialization(FunctionKoto function, BoundCall call)
        {
            // The specialization has its own input/Origin binders but inherits their slots from the original.
            var specialized = this.NextCall();
            specialized.Set(function.BoundSymbol!, call.ReturnType, call.Receiver, call.ArgumentToParameter, [], conformingType: call.ConformingType, declaringType: call.DeclaringType, origins: call.Origins, inputOrigins: call.InputOrigins);
            var previous = this.context;
            this.context = this.Context(specialized);
            this.Function(function, specialized);
            this.context = previous;
        }

        private void Function(FunctionKoto function, BoundCall call)
        {
            this.valid &= function.Body is not null || function.ExpressionBody is not null || (function.IsConstructor && function.IsGenerated);
            this.Queue(function.Body);
            this.Queue(function.ExpressionBody);
            this.Queue(function.BaseInitializer);
            if (function.IsConstructor && call.DeclaringType is { } owner)
            {
                for (var i = 0; i < StructStorage.Count(owner); i++)
                {
                    this.Queue(StructStorage.Field(owner, i).InitializerKoto);
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

        private void Setter(Koto target, Koto use)
        {
            if (binding.PropertyCall(target, PropertyAccessorKind.Set)?.BoundCall is { } setter)
            {
                this.Call(setter, use);
            }
        }

        private void Destruction(BoundType? type, Koto use)
        {
            if (type is null)
            {
                this.valid = false;
                return;
            }

            if (!this.destroyed.Add(type))
            {
                return;
            }

            if (ObjectTypes.IsOwner(type))
            {
                if (binding.ProveSealed(type.Components[0], use) == ConstraintProof.Proven)
                {
                    this.Destruction(type.Components[0], use);
                }
                else
                {
                    this.valid = false; // An open dynamic destructor has no complete effect bound.
                }

                return;
            }

            if (type.Semantics != SemanticsKind.Owner)
            {
                return;
            }

            if (type.Kind == BoundTypeKind.Parameter)
            {
                this.valid &= binding.ProveCopy(type, use) == ConstraintProof.Proven;
            }
            else if (StructStorage.IsStruct(type))
            {
                if (binding.DestructionCall(type) is { } destructor)
                {
                    this.Call(destructor, use);
                }

                for (var i = 0; i < StructStorage.Count(type); i++)
                {
                    this.Destruction(StructStorage.FieldType(type, i), use);
                }

                if (binding.StoredBase(type) is { } parent)
                {
                    this.Destruction(parent, use);
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

        private void Argument(BoundArgumentOperation argument, Koto use)
        {
            if (argument.Kind is ArgumentOperationKind.Borrow or ArgumentOperationKind.Reborrow or ArgumentOperationKind.PayloadProjection)
            {
                this.Access(argument.ParameterType, argument.ParameterType?.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq ? LoanRequirement.Uniq : LoanRequirement.Ref, use);
            }
            else if (argument.Kind == ArgumentOperationKind.CopyRead)
            {
                this.Access(argument.SourceType, LoanRequirement.Ref, use);
            }
        }

        private BoundType? Type(BoundType type)
            => this.contexts[this.context] is { } call ? binding.InstantiateStorageType(type, call) : type;

        private int Context(BoundCall call)
        {
            for (var i = 1; i < this.contexts.Count; i++)
            {
                var existing = this.contexts[i]!;
                if (ReferenceEquals(existing.Target, call.Target) && ReferenceEquals(existing.DeclaringType, call.DeclaringType) &&
                    existing.TypeArguments.SequenceEqual(call.TypeArguments) && existing.LengthArguments.SequenceEqual(call.LengthArguments) &&
                    existing.Origins.SequenceEqual(call.Origins) && existing.InputOrigins.SequenceEqual(call.InputOrigins))
                {
                    return i;
                }
            }

            if (this.contexts.Count >= 1024)
            {
                this.valid = false;
                return 0;
            }

            this.contexts.Add(call);
            return this.contexts.Count - 1;
        }

        private void Queue(Koto? node)
        {
            if (node is not null && this.seen.Add((node, this.context)))
            {
                this.pending.Add((node, this.context));
            }
        }

        private bool Conflicts(BoundType type, BoundOrigin origin, LoanRequirement mode, Koto use)
        {
            if (type.Origin is { } retained && (mode == LoanRequirement.Uniq || type.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq) &&
                binding.ProvesOriginOutlives(origin, retained, use) && binding.ProvesOriginOutlives(retained, origin, use))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if ((mode == LoanRequirement.Uniq || type.Symbol?.Schema?.Origins[i].LoanRequirement == LoanRequirement.Uniq) &&
                    binding.ProvesOriginOutlives(origin, type.OriginArguments[i], use) && binding.ProvesOriginOutlives(type.OriginArguments[i], origin, use))
                {
                    return true;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (this.Conflicts(type.Components[i], origin, mode, use))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
