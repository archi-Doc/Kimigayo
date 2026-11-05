// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // PLAN G128's implementation limits on storage preparation (DECISIONS BU-12): the height of a Type's storage, and the Types on the
    // storage walk's path, the query's root included, which ends a Type that grows. Refused and Capped are the walk's two limits; a part
    // that does not substitute ends no walk (BU-36). A finished Type's mark is StorageRejected in place of its open position when its
    // storage reaches such a part.
    private const int StorageHeightLimit = 64;
    private const int StoragePathLimit = 256;
    private const int StorageRefused = int.MaxValue;
    private const int StorageCapped = int.MaxValue - 1;
    private const int StorageRejected = -1;

    private readonly Dictionary<FunctionKoto, BindingSymbol> specialReceivers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> inferredConstructorTargets = new(ReferenceEqualityComparer.Instance);

    // The storage walk's marks for the pass, by Type: a finished Type's height (StorageRefused when refused) with Open 0, or
    // StorageRejected when its storage is Rejected; or an open Type's position on the walk's stack (Open), which every query leaves empty;
    // ResetPass clears both, after a walk a fault aborted too.
    private readonly Dictionary<BoundType, (int Height, int Open)> storageMarks = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundType> storageStack = new();
    private Dictionary<Koto, ConstructorAbsence>? constructorAbsences;
    private HelperCaptures? helperCaptures;
    private ulong storageVersion;

    /// <summary>Storage preparation's verdict on a Type (DECISIONS BU-12, BU-36); a limit takes precedence, and the verdict depends on the
    /// Type alone. The order is the precedence, so the verdict of several parts is the greatest (TyCtxt.Compose, BU-38). Layout's Rejected
    /// (R5-M37) names the same fact over the parts it places.</summary>
    internal enum StorageOutcome : byte
    {
        /// <summary>Every part the storage reaches substitutes, within PLAN G128's limits.</summary>
        Prepared,

        /// <summary>Within the limits, the storage reaches a part that does not substitute: a Field or Case payload whose Type Binding did not
        /// bind, a dependent of that failure, never reported; in a complete compilation a broken contract.</summary>
        Rejected,

        /// <summary>The storage's height passes 64 or a walk from it passes 256 Types (PLAN G128's implementation limits).</summary>
        Limit,
    }

    internal BindingSymbol? SpecialReceiver(FunctionKoto function) => this.specialReceivers.GetValueOrDefault(function);

    // A Type's storage verdict (StorageOutcome): Limit when its height passes 64 or a walk from it passes 256 Types, else Rejected when it
    // reaches a part that does not substitute, else Prepared. A limit marks every Type left open Refused, each of which reaches the
    // refusing part; a capped walk unmarks them, since the cap depends on the path. Instance substitution never queries storage, so the
    // walk's stack is empty at entry.
    internal StorageOutcome PrepareTypeStorage(BoundType type)
    {
        if (this.storageStack.Count != 0)
        {
            throw new InvalidOperationException($"Storage preparation of {type.Name} was queried during another storage walk.");
        }

        var rejected = false;
        var height = this.StorageHeight(type, 1, out _, ref rejected);
        foreach (var open in this.storageStack)
        {
            if (height == StorageRefused)
            {
                this.storageMarks[open] = (StorageRefused, 0);
            }
            else
            {
                this.storageMarks.Remove(open);
            }
        }

        this.storageStack.Clear();
        return height > StorageHeightLimit ? StorageOutcome.Limit : rejected ? StorageOutcome.Rejected : StorageOutcome.Prepared;
    }

    internal BoundType? InstantiateStorageType(BoundType type, CallPlan call) => this.InstantiateStorageType(type, this.types.StorageSubst(call));

    // A declared Type in the instance a substitution makes (StorageInstance) unless its storage is a Limit, null then, the instantiation
    // Binding's passes and the old analyses read until R7 (TyCtxt.Apply reads TyCtxt.Instance, which publishes the verdict).
    // Rejected storage passes through, as retention reads it (DECISIONS BU-36, BU-38): a part Binding rejected holds, destroys and
    // violates nothing, while its other parts stay checked; outside Binding's passes it is read only on a complete Binding, where no
    // storage is Rejected.
    internal BoundType? InstantiateStorageType(BoundType type, Subst subst)
        => this.StorageInstance(type, subst) is { } result && this.PrepareTypeStorage(result) != StorageOutcome.Limit ? result : null;

    // A declared Type in the instance a substitution makes: its binder's slots, then its stored Origins substituted, normalized in the
    // binder's Constraint scope (a Contract's Self the conforming Type), its closures closed in the substitution; null where one of these
    // steps does not complete. Its storage is not prepared.
    internal BoundType? StorageInstance(BoundType type, Subst subst)
    {
        var binder = subst.Binder;
        var originBinder = binder is FunctionKoto { Accessor: { } accessor } ? accessor.Binder : binder;
        var result = this.MemberType(type, subst.Declaring) is { } member && this.SubstituteType(member, binder, subst.Types, subst.Lengths) is { } substituted
            ? this.SubstituteStoredOrigins(substituted, originBinder, subst.Origins, subst.Inputs) : null;
        if (result is not null)
        {
            result = this.ContractType(result, this.ConstraintScope(binder), subst.Self);
        }

        return result is null ? null : this.CloseClosureTypes(result, subst);
    }

    // SPEC 6.3.4, 15.1.5, 16.3.1: what the receiver `self` of a constructor or destructor grants, the one decision the Place checks read
    // (DECISIONS BU-15); null for a node or symbol naming no such receiver. Every special receiver writes its own initialized Fields
    // (Writable), no part of it gives up a Non-Copy value (CompleteTransfer) and no capture entry takes it (Binding.Closures).
    // Parts: the authority its path gives every part (PathAuthority). Whole: whether a use of the whole receiver is judged by the
    // general rules under Parts (BindReference), a borrow of the whole receiver then being the helper borrow (HelperOrigin), which no
    // Closure captures (JudgeHelperCaptures). LendsFields: whether its writable Fields are lent exclusively (BorrowablePlace);
    // where SPEC lends them but Binding does not, the exclusive borrow is a located Unsupported_Kd (AccessFailure).
    // - Construction (6.3.4): an owner's path; the whole receiver is never named. Its Fields are not lent exclusively until PLAN
    //   G166 models the Loans the old analysis and A5 lack (6.3.4's "Field borrows end before completion"); LendsFields then goes.
    // - Destruction (16.3.1): access equivalent to uniq/Self, so no part offers Take, a Copy one included; of the whole receiver only a
    //   shared borrow is valid (no Take, Copy, exclusive acquisition or assignment), the helper borrow.
    private static ReceiverAuthority? Receiver(Koto? node) => Receiver((node as IdentifierNameKoto)?.BoundSymbol);

    private static ReceiverAuthority? Receiver(BindingSymbol? symbol)
        => symbol is { Name: "self", Declaration: FunctionKoto function } ? function switch
        {
            { IsConstructor: true } => new(SemanticsKind.Owner, Whole: false, LendsFields: false),
            { IsDestructor: true } => new(SemanticsKind.Uniq, Whole: true, LendsFields: true),
            _ => null,
        } : null;

    // The special receiver at the root of an inline part's path (a Field, Tuple element or constant-index element below it).
    private static ReceiverAuthority? ReceiverOfPart(Koto place)
        => KotoHelper.UnwrapParentheses(place) is BinaryKoto part ? Receiver(ElementAccess.OwnedPathRoot(part)) : null;

    // One row of the special-receiver table (Receiver).
    private readonly record struct ReceiverAuthority(SemanticsKind Parts, bool Whole, bool LendsFields);

    // A member selected directly on a special receiver (`self.f`) and the receiver's function, which the construction rules read (SPEC
    // 6.3.4, 11.7: own Fields used as storage and placed first).
    private static bool IsSpecialField(Koto node, out FunctionKoto function)
    {
        function = node is MemberAccessKoto { Left: IdentifierNameKoto { BoundSymbol.Declaration: FunctionKoto receiver } left } &&
            Receiver(left) is not null ? receiver : null!;
        return function is not null;
    }

    // SPEC 16.3.1: the Origin of the helper borrow, a borrow of the whole receiver of a row with Whole (the destruction row): the
    // destructor's HelperSlot Projection (H), so every value that carries the helper borrow carries H (SPEC 15.6), while the receiver's
    // parts keep its own Projection, its ReceiverSlot; null for any other place. PlaceOrigin and SlotOrigin ask it first.
    private BoundOrigin? HelperOrigin(Koto place)
        => KotoHelper.UnwrapParentheses(place) is IdentifierNameKoto { BoundSymbol: { Declaration: FunctionKoto function } symbol } &&
            Receiver(symbol) is { Whole: true } ? this.OriginAtom(function, OriginKind.Projection, HelperSlot) : null;

    // The helper borrow's Origin H of a function whose special receiver's row has Whole, once a borrow of the whole receiver interned it;
    // it is only looked up, never interned to check.
    private BoundOrigin? InternedHelper(FunctionKoto function)
        => Receiver(this.SpecialReceiver(function)) is { Whole: true } &&
            this.originAtoms.TryGetValue((function, OriginKind.Projection, HelperSlot), out var helper) ? helper : null;

    // SPEC 16.3.1, 7.7.2: no Closure created in the body of a destructor captures a value that carries its helper borrow (SPEC 15.6),
    // judged on Origins without flow (15.6.5) where the capture is made (an explicit entry, an omitted list's use), over the facts
    // recorded so far: the Origin through which the captured binding carries H, and H; null when it does not.
    private (BoundOrigin Carrier, BoundOrigin Helper)? HelperCarrier(FunctionKoto closure, BindingSymbol source)
        => source.Type is { } type && this.EnclosingHelper(closure) is { } helper && this.CarrierOf(type, helper, closure) is { } carrier
            ? (carrier, helper) : null;

    // The helper borrow's Origin H of the destructor whose body creates a Closure (InternedHelper).
    private BoundOrigin? EnclosingHelper(FunctionKoto closure)
    {
        for (var node = closure.Parent; node is not null; node = node.Parent)
        {
            if (node is FunctionKoto { IsAnonymous: false } named)
            {
                return this.InternedHelper(named);
            }
        }

        return null;
    }

    // SPEC 15.6.5: once a destructor's body is bound, every region source of its locals is recorded; a var a Closure captured before a
    // later value brought the helper borrow into its region is judged then, the one capture the capture site could not judge (OQ2).
    // Only a body that formed the helper borrow is walked.
    private void JudgeHelperCaptures(FunctionKoto function)
    {
        if (this.InternedHelper(function) is { } helper)
        {
            (this.helperCaptures ??= new(this)).Judge(function, helper);
        }
    }

    // A captured binding that now carries the helper borrow, which only a region's later source can bring: that source fails.
    private void JudgeHelperCapture(FunctionKoto closure, BoundClosure plan, BoundOrigin helper)
    {
        for (var i = 0; i < plan.Storage.Count; i++)
        {
            if (plan.Storage[i].Environment.Type is { } type && this.CarrierOf(type, helper, closure) is { } carrier)
            {
                this.FailLateHelperSource(closure, plan.Storage[i], carrier, helper);
            }
        }
    }

    // The storage height of a Type the walk meets as the path-th Type from the query's root, one strongly connected component at a time
    // (Tarjan; DECISIONS BU-12): an edge between two members of a component counts 0, an edge to a non-member 1 + its height, so every
    // member gets its component's height, the largest exit, when the member that opened the component closes it. The answer depends on
    // the Type alone. An open Type gives 0 and its stack position as low; StorageRefused when a height passes 64; StorageCapped past 256
    // Types on the path. Rejected is set when the Type reaches a part that does not substitute: each Type's walk keeps its own, as it keeps
    // its height, and ORs it into its caller's, so the member closing a component marks every member with the component's (all of them
    // reach each other) and a finished Type's mark gives its own. It reads storage metadata only; it never rebinds or selects a body.
    private int StorageHeight(BoundType type, int path, out int low, ref bool rejected)
    {
        low = int.MaxValue;
        if (this.storageMarks.TryGetValue(type, out var mark))
        {
            rejected |= mark.Open == StorageRejected;
            low = mark.Open > 0 ? mark.Open : low;
            return mark.Open > 0 ? 0 : mark.Height;
        }

        if (path > StoragePathLimit)
        {
            return StorageCapped;
        }

        var position = this.storageStack.Count + 1;
        this.storageStack.Add(type);
        this.storageMarks[type] = (0, position);
        var height = 0;
        var lowest = position;
        var own = false;
        var failure = this.StorageParts(type, path + 1, ref height, ref lowest, ref own);
        rejected |= own;
        if (failure != 0 || lowest < position)
        {
            low = lowest;
            return failure != 0 ? failure : height;
        }

        for (var i = position - 1; i < this.storageStack.Count; i++)
        {
            this.storageMarks[this.storageStack[i]] = (height, own ? StorageRejected : 0);
        }

        this.storageStack.RemoveRange(position - 1, this.storageStack.Count - position + 1);
        return height;
    }

    // Walks a Type's parts in order (a struct's or enum's base, Fields and Case payload Tuples, then its components): 0, or the limit that
    // ends the walk. A Field or Case that did not substitute (null) is rejected and walked past, its siblings walked (DECISIONS BU-37).
    private int StorageParts(BoundType type, int path, ref int height, ref int lowest, ref bool rejected)
    {
        if (AdtDef.IsStruct(type) || AdtDef.IsEnum(type))
        {
            var instance = this.Instance(type);
            var failure = instance.Base is { } parent ? this.StorageEach([parent], path, ref height, ref lowest, ref rejected) : 0;
            failure = failure != 0 ? failure : this.StorageEach(instance.Fields, path, ref height, ref lowest, ref rejected);
            failure = failure != 0 ? failure : this.StorageEach(instance.Cases, path, ref height, ref lowest, ref rejected);
            if (failure != 0)
            {
                return failure;
            }
        }

        return this.StorageEach((BoundType[])type.Components, path, ref height, ref lowest, ref rejected);
    }

    // Raises height over parts: a part that reaches an open Type is a member of the component (no level), any other an exit (1 + its
    // height); lowest takes the lowest open position a part reaches; a part that does not substitute (null) is rejected and walked past.
    // 0, or the limit that ends the walk.
    private int StorageEach(ReadOnlySpan<BoundType?> parts, int path, ref int height, ref int lowest, ref bool rejected)
    {
        foreach (var part in parts)
        {
            if (part is null)
            {
                rejected = true;
                continue;
            }

            var value = this.StorageHeight(part, path, out var low, ref rejected);
            if (value > StorageHeightLimit)
            {
                return value;
            }

            height = Math.Max(height, low == int.MaxValue ? value + 1 : value);
            lowest = Math.Min(lowest, low);
            if (height > StorageHeightLimit)
            {
                return StorageRefused;
            }
        }

        return 0;
    }

    private void IndexSpecialReceiver(FunctionKoto function, BindingScope scope)
    {
        if (!function.IsConstructor && !function.IsDestructor)
        {
            return;
        }

        if (!this.specialReceivers.TryGetValue(function, out var receiver))
        {
            this.specialReceivers.Add(function, receiver = new("self", BindingSymbolKind.Parameter, function, scope) { Slot = ReceiverSlot });
        }

        receiver.Scope = scope;
        receiver.Type = scope.Parent?.Owner.BoundSymbol?.Type;
        if (!scope.Values.TryAdd("self", receiver))
        {
            this.Fail(function, BindingFailure.Duplicate);
        }
    }

    private BindingSymbol? ConstructorMember(MemberAccessKoto member, BindingScope scope)
    {
        if (member.Parent is not InvocationKoto invocation || !ReferenceEquals(invocation.Method, member))
        {
            this.Fail(member, BindingFailure.NotCallable);
            return null;
        }

        var qualifier = this.TypeName(member.Left, scope, false, arity: -1);
        if (qualifier is null)
        {
            // A qualifier that names no Type is reported by the call as missing or inaccessible (BindCall).
            return null;
        }

        var type = this.ConstructorQualifierType(member.Left, qualifier, scope);
        if (type is not { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto declaration } ||
            !this.scopes[declaration].Values.TryGetValue("init", out var constructor))
        {
            this.FailConstructorAbsence(member, type);
            return null;
        }

        member.Right.BindingState = BindingState.Resolved;
        member.Right.BoundSymbol = constructor;
        member.BoundSymbol = constructor;
        return constructor;
    }

    private BoundType? ConstructorQualifierType(Koto syntax, BindingSymbol symbol, BindingScope scope)
    {
        var unwrapped = UnwrapTypeSyntax(syntax);
        while (unwrapped is ParenthesizedTypeKoto grouped)
        {
            unwrapped = UnwrapTypeSyntax(grouped.Type);
        }

        if (unwrapped is SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root)
        {
            unwrapped = UnwrapTypeSyntax(root.Operands[0]);
        }

        if (unwrapped is GenericsKoto || TypeSpelling(unwrapped) == "Self" ||
            symbol.Declaration is not StructKoto { GenericParameterNodes.Count: > 0 } declaration)
        {
            return this.EnumQualifierType(syntax, symbol, scope, null);
        }

        var pattern = this.SelfType(symbol);
        var type = this.BindContainerReference(syntax, symbol, scope, this.TypeContext(syntax, scope), ((BoundType[])pattern.Components).AsSpan(0, declaration.GenericParameterNodes.Count));
        if (type is not null)
        {
            this.inferredConstructorTargets.Add(syntax);
        }

        syntax.BoundSymbol = symbol;
        return Complete(syntax, type);
    }

    private BoundType? BindBaseConstructor(SyntaxFormKoto node, BindingScope scope)
    {
        if (node.Parent is InvocationKoto { Parent: FunctionKoto { IsConstructor: true } constructor } call &&
            ReferenceEquals(call.Method, node) && ReferenceEquals(constructor.BaseInitializer, call))
        {
            if (this.BaseConstructorGroup(constructor, out var parent) is { } initializer)
            {
                return this.BindReference(node, initializer, scope);
            }

            if (parent is not null)
            {
                return this.FailConstructorAbsence(node, parent);
            }
        }

        return this.Fail(node, BindingFailure.InvalidTypeFormation);
    }

    // SPEC 6.3.3: the direct base's constructor group seen from a derived constructor, and the base Type it constructs.
    private BindingSymbol? BaseConstructorGroup(FunctionKoto constructor, out BoundType? parent)
    {
        parent = AdtDef.Base(this.SelfType(constructor.BoundSymbol!.Scope.Owner.BoundSymbol!));
        return parent?.Symbol?.Declaration is StructKoto declaration && this.scopes[declaration].Values.TryGetValue("init", out var group) ? group : null;
    }

    // SPEC 6.3.6, 22.1: a Type without constructors is a located absence at its construction or base call. A derived structure whose
    // omitted base clause selects no base constructor has none either; an Unknown premise that can change that selection leaves it
    // unproven, and a selection resting on another failure is derived from it. A Type parameter under an identity premise waits for
    // its substitution and remains unsupported, as does a qualifier that did not bind.
    private BoundType? FailConstructorAbsence(Koto node, BoundType? type)
    {
        var structure = type is { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto declaration } ? declaration : null;
        if (structure is { ConstructorAvailability: ConstructorAvailability.Eligible } && this.implicitConstructorDecisions.TryGetValue(structure, out var decision))
        {
            return decision.Outcome switch
            {
                OmittedBaseOutcome.NoBaseConstructor or OmittedBaseOutcome.NoneApplicable or OmittedBaseOutcome.Ambiguous =>
                    this.FailExplained(ref this.constructorAbsences, node, BindingFailure.MissingName, new ConstructorAbsence(type!, decision)),
                OmittedBaseOutcome.Unproven => this.FailExplained(ref this.constructorAbsences, node, BindingFailure.UnprovenConstraint, new ConstructorAbsence(type!, decision)),
                OmittedBaseOutcome.Dependent when decision.Cause is { } cause => this.CompleteDependent(node, cause),
                _ => this.Fail(node, BindingFailure.Internal),
            };
        }

        if (type is null || structure is { ConstructorAvailability: not (ConstructorAvailability.MissingInitializer or ConstructorAvailability.CompilerManaged) } ||
            (type.Kind == BoundTypeKind.Parameter && HasParameterIdentity(this.ConstraintScope(node))))
        {
            return this.Fail(node, BindingFailure.Unsupported);
        }

        return this.FailExplained(ref this.constructorAbsences, node, BindingFailure.MissingName, new ConstructorAbsence(type, null));
    }

    private void ReportConstructorAbsence(Koto node, ConstructorAbsence absence, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var type = absence.Type;
        var name = DiagnosticTypeName(type);
        if (absence.Decision is { } decision && type.Symbol?.Declaration is StructKoto { Bases: [var clause, ..] })
        {
            // The decision is made once, from the derived declaration, so its Types are the declaration's own.
            var declared = DiagnosticTypeName(this.SelfType(type.Symbol));
            var baseName = clause.BoundType is { } baseType ? DiagnosticTypeName(baseType) : clause.ToString();
            var omitted = declared == name ? "its omitted base clause" : $"the omitted base clause of {declared}";
            if (decision.Outcome == OmittedBaseOutcome.Unproven)
            {
                // SPEC 6.3.6, 8.3.3: the synthesized constructor is decided once, generically, from the derived declaration.
                var note = $"{name} has a synthesized constructor only when {omitted} selects a constructor of {baseName}; an Unknown premise of a candidate can change that selection, and the constructor is decided once for every {declared} (SPEC 6.3.6, 8.3.3)";
                if (decision.Cause is { } premise)
                {
                    node.Report(requirement, code, evidence: [name, "init", premise.ToString()], note: note, related: [("constraint", premise, "unproven premise"), ("declaration", clause, "omitted base clause")]);
                }
                else
                {
                    node.Report(requirement, code, evidence: [name, "init"], note: note, related: [("declaration", clause, "omitted base clause")]);
                }

                return;
            }

            var reason = decision.Outcome switch
            {
                OmittedBaseOutcome.NoBaseConstructor => $"{baseName} has no constructor",
                OmittedBaseOutcome.NoneApplicable => $"no constructor of {baseName} applies without arguments",
                _ => $"more than one constructor of {baseName} applies without arguments",
            };
            node.Report(requirement, code, note: $"{name} has no constructor: {omitted} selects none, since {reason} (SPEC 6.3.6)", related: [("declaration", clause, "omitted base clause")]);
        }
        else if (type is { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto { ConstructorAvailability: ConstructorAvailability.MissingInitializer, UninitializedField: { } field } })
        {
            node.Report(requirement, code, note: $"{name} has no constructor: its Field {field.NameKoto.IdentifierName} has no initializer, so none is synthesized (SPEC 6.3.6)", related: [("declaration", field.NameKoto, "no initializer")]);
        }
        else if (type.IsWrappingInteger || type.Symbol?.Declaration is StructKoto { ConstructorAvailability: ConstructorAvailability.CompilerManaged })
        {
            node.Report(requirement, code, note: $"{name} is a Kimi Type whose representation the compiler manages; it has only the constructors its declaration declares (SPEC 22.1)");
        }
        else
        {
            var subject = type.Kind == BoundTypeKind.Parameter ? "The Type parameter " + name : name;
            node.Report(requirement, code, note: $"{subject} declares no constructor; only a structure declares constructors (SPEC 6.3)");
        }
    }

    // A construction or base call of a Type without constructors: the Type, and for a derived structure its omitted base selection.
    private readonly record struct ConstructorAbsence(BoundType Type, OmittedBaseSelection? Decision);

    private BoundType? ConstructorType(InvocationKoto call, FunctionKoto function, BindingScope scope, ReadOnlySpan<int> mapping)
    {
        var owner = (StructKoto)function.BoundSymbol!.Scope.Owner;
        var type = call.Parent is FunctionKoto { IsConstructor: true } constructor && ReferenceEquals(constructor.BaseInitializer, call)
            ? AdtDef.Base(this.SelfType(constructor.BoundSymbol!.Scope.Owner.BoundSymbol!))!
            : ((MemberAccessKoto)call.Method).Left.BoundType!;
        var count = owner.BoundSymbol!.Schema!.Origins.Count;
        if (count == 0)
        {
            return type;
        }

        var origins = this.originScratch.Rent(count);
        Array.Clear(origins, 0, count);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            origins[i] = type.OriginArguments[i];
        }

        try
        {
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var slot = mapping[i];
                if (function.Parameters[slot].Type.BoundType is { } pattern && call.ArgumentNodes[i].BoundType is { } actual &&
                    this.AdaptInput(call.ArgumentNodes[i], pattern, actual, scope, null, null, out var adapted, out _, out _))
                {
                    this.MatchInputOrigins(this.StoredType(pattern, type)!, adapted, owner, origins, []);
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (origins[i] is null)
                {
                    return null;
                }
            }

            return this.WithOrigins(type, null, origins.AsSpan(0, count));
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    // One reusable visitor walks a destructor's body to each Closure it creates, judging the bound ones; it stops at a Closure, whose
    // storage holds every binding its nested Closures forward, and at a nested declaration (a container or named function).
    private sealed class HelperCaptures(Binding binding) : KotoVisitor
    {
        private BoundOrigin helper = null!;

        public void Judge(FunctionKoto function, BoundOrigin helper)
        {
            this.helper = helper;
            function.VisitChildren(this);
        }

        public override void Visit(Koto node)
        {
            if (node is FunctionKoto { ClosureDefinition: { } plan } closure)
            {
                binding.JudgeHelperCapture(closure, plan, this.helper);
            }

            if (node is not (FunctionKoto or DeclarationContainerKoto))
            {
                node.VisitChildren(this);
            }
        }
    }
}
