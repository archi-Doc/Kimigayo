// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call slots (SPEC 10.8, 10.8.1, 8.1.2, 6.5.2 step 2): a callee's structural slots, the bindings fixed before any evidence, and inference.
// - Responsibility: the declaration owning a callee's slots (CallSlotOwner: a function its own, a constructor its structure's, an enum
//   constructing a Case its own), their count and the binder's Origin count; a member pattern as a call reads it (CallMemberPattern); a
//   solution's slot bindings fixed before any evidence (FixedSlots), produced by FixCallSlots for a candidate (a function's explicit Type
//   arguments and lengths; a constructor's declaring Type, its enclosing slots always and its own unless an inferred construction leaves
//   them open), by ConstructionSlots for a construction Type (also read by ConstructorType before the candidate's frame exists) and by
//   a Case construction's seeding (its qualifier's and expected Type's bindings, Binding.Enums.cs); the own slots an inferred
//   construction, a structure's or a Case's (SPEC 9.7), leaves open (OpenConstructionSlot); the adaptation entry's only input, a
//   pattern under those bindings (FixedPattern); a constructor's constructed Type (ConstructionType); the structural matching of a
//   pattern over the owner's slots against an actual Type, binding each slot it reaches (Infer), and an s/U whose s is bound, matched as
//   the Type it forms (FormedApplication) and, a structured one, adapted at as a parameter Type (FittedPattern).
// - Inputs: the callee's declaration and declaring Type; the call's explicit Types and lengths; a pattern, the actual Type at its position,
//   the solve's binder (SolveBinder: its declaration and slot owner) and one solution's slot and length arrays (a candidate's
//   SlotSolution, a Case construction's, a function reference's or a Callable proof's), with the evidence flags.
// - Outputs: the bindings in those arrays (null while unbound) and whether the actual matches the pattern structurally; a FixedSlots
//   (owner, each slot's fixed binding, lengths); a FixedPattern for AdaptInput.
// - Invariants: a slot's frame keeps its first binding's Type, and each evidence binding is recorded with its composed polarity
//   (SlotBinding; G1128), whose Origins the late solve values (SPEC 10.8 as revised); under `relateLater` or `evidence` an Origin never
//   fails a structurally equal binding, and a function reference's met bindings stay its Origin conflict or limit: Infer meets
//   Origins (CommonOriginType) only at a function reference's slot, which no late solve values (SPEC 10.5.3), until a reference's
//   solve owns its bindings and values the met Type's readers (PLAN's R1.3 (h) line). An open
//   slot in FixedSlots has one encoding, its own parameter, and only an inferred construction (a structure's `.init` or a Case's
//   qualifier with its own list omitted, SPEC 9.7) leaves one beside fixed slots; none fixed is no Types. An argument's adaptation is
//   planned only at a FixedPattern made by Of (a pattern under a solution's FixedSlots, an open slot staying a slot) or Open (a
//   completed or slot-free Type, stated at the call), so the build names every reader of the adaptation entry. A candidate's part
//   adapts, and its Function signature is read, at one pattern (SlotSolution.AdaptationPattern): Of over FittedPattern's, each
//   structured application among its outer layers formed under the binder its sources fixed and an exact source unformed (the owner's
//   O-283 (a)), every pair layer formed for a Literal, which is no evidence (O-294 (b)); the call-shape collection
//   (CheckCallShapeContracts) adapts at Of alone, since no source has fixed a binder there and it enters no application.
//   Completeness under the fixed bindings is read from the solution's frame before any evidence, where an open slot is unbound (a
//   Case's settled payloads): substituting FixedSlots leaves an open slot's own parameter, which looks complete.
// - Prohibitions: no Origin relation is judged and no applicability decided here (SPEC 15.6.1): the callers judge the solution; no
//   reader adapts at a pattern whose fixed slots are left open, and no FixedPattern is made but by Of or Open.
public sealed partial class Binding
{
    // The declaration owning structural inference is independent of the binder owning the Origins: a function owns its slots and a
    // constructor's structure owns them; an enum constructing a Case owns its own (SPEC 10.8.1).
    private static Koto CallSlotOwner(Koto binder)
        => binder is FunctionKoto { IsConstructor: true } constructor ? constructor.BoundSymbol!.Scope.Owner : binder;

    // SPEC 10.1 step 5, 10.8.1, 10.9: the Constraint clause lists a candidate is proven under, in order: 0 its own, 1 its owner
    // structure's, a constructor's, whose slots are the owner's (`slotOwner`, CallSlotOwner); null where it has none. The one
    // enumeration of StepFiveConjunction, which proves them, and of J2's CollectPositionNames, which reads the variables they name
    // (the owner's O-368 (a)).
    private static IReadOnlyList<Koto>? CandidateClauses(Koto binder, Koto? slotOwner, int list)
        => list == 0 ? (binder as FunctionKoto)?.TypeConstraints
            : list == 1 && slotOwner is StructKoto owner && !ReferenceEquals(owner, binder) ? owner.ConstraintNodes : null;

    private static IReadOnlyList<TypeKoto> CallOwnSlots(FunctionKoto function)
        => function.IsConstructor ? ((StructKoto)CallSlotOwner(function)).GenericParameterNodes : function.GenericArguments;

    private static int CallSlotCount(Koto binder)
        => binder is FunctionKoto { IsConstructor: false } function
            ? function.GenericArguments.Count : CallSlotOwner(binder).BoundSymbol!.Schema!.GenericSlots.Count;

    // The Origin slots a binder binds: a function's, or a declaration's.
    private static int CallOriginCount(Koto binder) => binder is FunctionKoto function ? function.Origins.Count : binder.BoundSymbol!.Schema!.Origins.Count;

    // SPEC 10.8.1: the slots a constructor's declaring Type fixes: its components, an open own slot as its own parameter; also read before
    // its candidate's frame is seeded (ConstructorType).
    private static FixedSlots ConstructionSlots(FunctionKoto constructor, BoundType declaringType)
        => new(CallSlotOwner(constructor), (BoundType[])declaringType.Components, default);

    // An inferred construction (a structure's or an enum Case's, SPEC 9.7) leaves open each own slot that its constructed Type gives as its
    // own parameter; an outer slot is always a fixed binding, even when it is itself a parameter.
    private static bool OpenConstructionSlot(DeclarationContainerKoto owner, BoundType constructed, bool inferred, int slot)
        => inferred && slot < owner.GenericParameterNodes.Count && constructed.Components[slot] is { Kind: BoundTypeKind.Parameter } open &&
            ReferenceEquals(open.Symbol?.Scope.Owner, owner);

    // SPEC 8.1.2, 10.8: what forms the applications of pair slot `slot`'s binder: the slot's binding, else its selector evidence.
    private static BoundType? Selector(SlotFrame frame, int slot)
        => slot < frame.Types.Length ? frame.Types[slot] ?? frame.Selectors[slot] : null;

    // The slots a source has fixed: bound, or a pair binder known by its selector evidence. A pass that changes it has made progress
    // (SettleWaiting, InferExpectedResult, InferReferenceSlots).
    private static int SourcedSlots(SlotFrame frame)
    {
        var count = 0;
        for (var i = 0; i < frame.Types.Length; i++)
        {
            count += (frame.Types[i] is null ? 0 : 1) + (frame.Selectors[i] is null ? 0 : 1);
        }

        return count;
    }

    // SPEC 10.8 (the owner's readings O-263 (a), O-338 (a), O-340 (a)): whether an application is an exact source of its binder, as an
    // occurrence of the original is: its target has no layer, structure or open slot of its own that could claim the evidence's outer
    // Semantics, so that Semantics is the binder's alone: a bare Type slot being inferred (`s/V`) or a closed leaf Type (`s/i64`, a pair layer of
    // `s` whose typed evidence counts as at any other, SPEC 10.5.1). The pattern is classified under the solve's fixed bindings
    // (FixedSlots, read from the frame), where only an own slot of the slot owner stays open, as its own parameter: an enclosing slot
    // is always a fixed binding (OpenConstructionSlot; SPEC 9.7.1), so `s/V` over one is `s/(its binding)` on every path and is
    // classified as its binding is, whether the pattern holds the slot (a constructor's, a Case's) or has it substituted (a
    // method's): never as a slot being inferred; explicit Type arguments or a construction Type that fix the own slots fix the
    // binder too. Any other target (`s/(ref/i32)`, `s/(t/i32)`, `s/Option<U>`, another pair's target, alone as `U` of `t/U` too: an
    // abstract leaf, never a closed one, PLAN G1266; a caller's Type parameter) has its own layer, structure or slot: that application
    // is formed under the binder its exact sources fixed, a parameter Type its argument fits (FittedPattern) and is then compared with
    // (SourcedLayer), never evidence, so an owner binding of the outer layer is no layer, even above a reference.
    private static bool IsExactSource(BoundType application, Koto function, SlotFrame frame)
    {
        var target = application.Components[0];
        if (target is { Kind: BoundTypeKind.Parameter, Symbol: { Kind: not BindingSymbolKind.SemanticsTarget } bare })
        {
            if (ReferenceEquals(bare.Scope.Owner, function))
            {
                return true;
            }

            target = ContainerSlot(function, bare) is >= 0 and var slot && slot < frame.Types.Length && frame.Types[slot] is { } binding
                ? binding : target;
        }

        return target is { Components.Count: 0, ContainsParameter: false, HasAbstractPart: false };
    }

    // SPEC 10.8: whether an application is an exact source (IsExactSource) of a binder whose sources gave selector evidence, which
    // IsSource compares its typed part with; a binder fixed before any evidence has none, and its part is judged with the layer open.
    private static bool IsEvidencedSource(BoundType application, Koto owner, SlotFrame frame)
        => ContainerSlot(owner, application.Symbol!) is >= 0 and var slot && slot < frame.Types.Length && frame.Selectors[slot] is not null &&
            IsExactSource(application, owner, frame);

    // SPEC 8.1.2: whether a formed pattern is a layer over the callee's `target` (a pair's application or original, a Semantics layer),
    // matched by LayerTarget, rather than a Type matched as it is (an owner binding's target, a family, case by case).
    private static bool IsFormedLayer(BoundType formed, BoundType target)
        => !ReferenceEquals(formed, target) && formed.Kind is BoundTypeKind.SemanticsApplication or BoundTypeKind.Parameter or BoundTypeKind.Semantics;

    // SPEC 8.1.2: what a formed layer meets in the actual: the direct target below the actual's layer of the same Semantics or the same
    // pair (an application, an original or an annotated occurrence), or null when the actual has no such layer. The outer slot is Origin
    // evidence, never compared here. Infer and MatchInputOrigins read it.
    private static BoundType? LayerTarget(BoundType layer, BoundType actual)
    {
        if (layer.Kind == BoundTypeKind.Semantics)
        {
            return actual is { Kind: BoundTypeKind.Semantics, Components: [var component] } && actual.Semantics == layer.Semantics ? component : null;
        }

        return TryPairLayer(layer, out var whole, out _) && TryPairLayer(actual, out var supplied, out var target) && ReferenceEquals(whole, supplied)
            ? target : null;
    }

    // Constructor slots already include their fixed enclosing environment. Apply structural
    // bindings once, to declaration patterns; never substitute inside an already-bound argument.
    private BoundType? CallMemberPattern(BoundType type, Koto binder, BoundType? declaringType)
        => !ReferenceEquals(CallSlotOwner(binder), binder) && declaringType is not null
            ? this.SubstituteStoredOrigins(type, CallSlotOwner(binder), (BoundOrigin[])declaringType.OriginArguments)
            : this.MemberType(type, declaringType);

    private BoundType? ConstructionType(FunctionKoto function, BoundType template, ReadOnlySpan<BoundType?> slots)
    {
        var count = CallSlotCount(function);
        for (var i = 0; i < count; i++)
        {
            if (slots[i] is null)
            {
                return null;
            }
        }

        return this.types.Rebuild(template, slots[..count]!, template.Origin, (BoundOrigin[])template.OriginArguments);
    }

    // SPEC 10.8, 10.8.1, 8.1.3: seeds a candidate's frame with the slot bindings fixed before any evidence and returns them (FixedSlots):
    // a function's explicit Type arguments and lengths, which supply every own slot, or a constructor's declaring Type, whose enclosing
    // slots are always fixed and whose own slots are open only where an inferred construction leaves them its own parameters.
    private FixedSlots FixCallSlots(
        InvocationKoto call,
        FunctionKoto function,
        BoundType? declaringType,
        ReadOnlySpan<BoundType?> explicitTypes,
        ReadOnlySpan<BoundLength?> explicitLengths,
        SlotFrame slots,
        bool fixedConstruction = false)
    {
        var count = CallSlotCount(function);
        slots.ClearTypes(count);
        slots.ClearLengths(count);
        if (!function.IsConstructor)
        {
            for (var i = 0; i < Math.Min(count, explicitTypes.Length); i++)
            {
                if (explicitLengths[i] is { } length)
                {
                    slots.SetLength(i, length);
                }
                else
                {
                    slots.SetType(i, explicitTypes[i]);
                }
            }

            return new(function, explicitTypes, explicitLengths);
        }

        if (declaringType is null)
        {
            return default;
        }

        var inferred = !fixedConstruction && this.InfersConstruction(call);
        var owner = (DeclarationContainerKoto)CallSlotOwner(function);
        for (var i = 0; i < count; i++)
        {
            if (!OpenConstructionSlot(owner, declaringType, inferred, i))
            {
                slots.SetType(i, declaringType.Components[i]);
            }
        }

        return ConstructionSlots(function, declaringType);
    }

    // Whether a constructor call infers its construction's Type (SPEC 10.8.1): its qualifier names the Type without its arguments.
    private bool InfersConstruction(InvocationKoto call)
        => call.Method is MemberAccessKoto member && this.inferredConstructionTargets.Contains(member.Left);

    // SPEC 8.1.2: the pattern with each s/U whose s is already inferred formed as the Type it forms under the binding, by the one rule
    // substitution reads (ApplySemantics), at every depth. s is known from the slot's binding or, while that is unbound, from its selector
    // evidence (SlotFrame.Selectors). A formed layer is in the caller's terms at its top (a layer, an abstract binding's application, a
    // family; an owner binding gives the target) over the callee's target, which is formed first: a caller's pair is never formed again
    // under the callee's bindings (a recursive call), and Origin matching never meets an unformed application (MatchInputOrigins). An
    // application whose s is still unknown is kept as it is.
    // Null where a binding forms no Type (Unformed, BU-240): the part fails and the call is Unsupported.
    private BoundType? FormedApplication(BoundType pattern, Koto function, SlotFrame frame)
    {
        var count = pattern.Components.Count;
        if (!pattern.HasAbstractPart || count == 0)
        {
            return pattern;
        }

        this.termWalks++;
        if (pattern is { Kind: BoundTypeKind.SemanticsApplication, Symbol: { } selector, Components: [var target] } &&
            ContainerSlot(function, selector) is >= 0 and var slot && Selector(frame, slot) is not null)
        {
            return this.FormedApplication(target, function, frame) is { } formedTarget &&
                this.FormedUnder(frame, slot, formedTarget, pattern.Origin, out var formed) ? formed : null;
        }

        var components = this.RentTypes(count);
        try
        {
            var changed = false;
            for (var i = 0; i < count; i++)
            {
                if (this.FormedApplication(pattern.Components[i], function, frame) is not { } component)
                {
                    return null;
                }

                components[i] = component;
                changed |= !ReferenceEquals(component, pattern.Components[i]);
            }

            return changed ? this.types.Rebuild(pattern, components.AsSpan(0, count), pattern.Origin, (BoundOrigin[])pattern.OriginArguments) : pattern;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // SPEC 10.1 step 5, 10.2, 10.8 (the owner's O-283 (a)): the pattern an argument part adapts at (SlotSolution.AdaptationPattern): each
    // structured application among its outer layers formed under the binder a source has fixed (ApplySemantics, as FormedApplication), a
    // parameter Type the argument fits by the common adaptation as any argument fits its parameter. The adaptation reaches only the outer
    // layers, so nothing below them is formed. `part` is the argument part (null for a position of several typed parts, their common
    // Type), whose kind its owner decides (KindOf). An exact source (IsExactSource) of a part that is evidence is kept, since its evidence
    // is its own outer Semantics, never converted (IsSource), so owner and ref evidence still conflict in any order. A Literal is no
    // evidence (SPEC 10.5.1, 10.2.1; the owner's O-294 (b)): its default fits every pair layer whose binder evidence fixed, an exact
    // source and an occurrence of the original whose Type is still open alike (a borrowed temporary under ref), and binds only what no
    // evidence binds.
    // Null where a binding forms no Type (Unformed, BU-240).
    private BoundType? FittedPattern(BoundType pattern, Koto function, SlotFrame frame, Koto? part)
    {
        var literal = part is not null && this.KindOf(part) == PartKind.Literal;
        if (literal && pattern is { Kind: BoundTypeKind.Parameter, Symbol: { Kind: BindingSymbolKind.SemanticsTarget, Type: { } original } pair } &&
            ContainerSlot(function, pair) is >= 0 and var own && own < frame.Types.Length && frame.Types[own] is null &&
            this.FormedUnder(frame, own, original, pattern.Origin, out var fixedForm))
        {
            return fixedForm;
        }

        if (pattern is not { Kind: BoundTypeKind.SemanticsApplication or BoundTypeKind.Semantics, HasAbstractPart: true, Components: [var target] })
        {
            return pattern;
        }

        if (this.FittedPattern(target, function, frame, part) is not { } fitted)
        {
            return null;
        }

        if (pattern.Kind == BoundTypeKind.SemanticsApplication && ContainerSlot(function, pattern.Symbol!) is >= 0 and var slot &&
            (literal || !IsExactSource(pattern, function, frame)) && this.FormedUnder(frame, slot, fitted, pattern.Origin, out var formed))
        {
            return formed;
        }

        return ReferenceEquals(fitted, target) ? pattern : this.types.Rebuild(pattern, [fitted], pattern.Origin, (BoundOrigin[])pattern.OriginArguments);
    }

    // SPEC 10.8: whether a pattern still holds an application whose pair binder no source has fixed, read as FormedApplication forms
    // (a bound application's target is still in the callee's terms). Such an input waits until every source of its binder is in.
    private bool HasOpenApplication(BoundType pattern, Koto function, SlotFrame frame)
    {
        if (!pattern.HasAbstractPart)
        {
            return false;
        }

        this.termWalks++;
        if (pattern is { Kind: BoundTypeKind.SemanticsApplication, Symbol: { } selector, Components: [var target] } &&
            ContainerSlot(function, selector) is >= 0 and var slot && slot < frame.Types.Length)
        {
            return Selector(frame, slot) is null || this.HasOpenApplication(target, function, frame);
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (this.HasOpenApplication(pattern.Components[i], function, frame))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 10.8 ("An unbound s is inferred from the outer Semantics of the evidence matched against each pair layer of s"; "owner and
    // ref evidence for one s conflict in any argument order"; the owner's readings O-243 (a), O-255 (a) and O-263 (a)): whether
    // `actual` stands as an exact source of pair slot `slot`'s binder: the evidence at an occurrence of the pair's original or at an
    // application of a bare slot or a closed leaf Type (IsExactSource), the expected result included. Each gives its outer Semantics: the first exact source
    // of a slot nothing has fixed becomes the slot's selector evidence, and every other one must agree with it, whichever comes first:
    // both form the same layer over one `target` under the premises at the solve's node (ApplySemantics, EnterEvidence: a conditional
    // member's premise included; an owner source forms the target itself, so owner evidence is
    // compared as any other is; a binding that forms none, a pair target alone, agrees only with itself outside a split's run: a
    // Language verdict, no Unformed fact (F's O1 (a)), which only refuses the candidate (refusedOnSplit: never dropped so that another
    // wins, SPEC 10.1, G812 ruling 141, ClassifySelection); a run of a split check compares its case's layer). A slot bound with no
    // selector evidence was fixed before any evidence (explicit Type arguments, a construction's
    // fixed slots): its binder is not inferred, so nothing is compared. Never supplies no evidence.
    private bool IsSource(SlotFrame frame, int slot, BoundType actual, BoundType target)
    {
        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        if (frame.Selectors[slot] is not { } evidence)
        {
            if (frame.Types[slot] is null)
            {
                frame.SetSelector(slot, actual);
            }

            return true;
        }

        if (ReferenceEquals(evidence, actual))
        {
            return true;
        }

        var saved = this.EnterEvidence(frame);
        var formsUnder = this.FormsUnder(evidence) && this.FormsUnder(actual);
        var agrees = (formsUnder || this.splitOpen) &&
            this.ApplySemantics(evidence, target, null) is { } formed && ReferenceEquals(formed, this.ApplySemantics(actual, target, null));
        this.refusedOnSplit |= !formsUnder && !this.splitOpen;
        this.premiseUse = saved;
        return agrees;
    }

    // SPEC 8.1.2, 10.8, 8.3.4: `s/target` formed under pair slot `slot`'s binder (ApplySemantics); false while no source has fixed it. One
    // meaning each (PremiseScope): the slot's stored binding is the Type every phase substitutes, so it forms as that Type is; while the
    // slot is unbound, its selector evidence is a source's outer Semantics at this call, so it forms under the premises at the solve's
    // node (EnterEvidence), as IsSource compares it. `formed` is null where the binder forms no Type (Unformed, BU-240).
    private bool FormedUnder(SlotFrame frame, int slot, BoundType target, BoundOrigin? origin, out BoundType? formed)
    {
        formed = null;
        if (slot >= frame.Types.Length)
        {
            return false;
        }

        if (frame.Types[slot] is { } binding)
        {
            formed = this.ApplySemantics(binding, target, origin);
            return true;
        }

        if (frame.Selectors[slot] is not { } evidence)
        {
            return false;
        }

        var saved = this.EnterEvidence(frame);
        formed = this.ApplySemantics(evidence, target, origin);
        this.premiseUse = saved;
        return true;
    }

    // SPEC 8.1.2, 10.8: the Type an application pattern forms against `actual`, in two phases whatever the order of the sources: an
    // exact source (IsExactSource) first stands as one (IsSource; false when it disagrees with the binder's other exact sources); then
    // any application is formed under the binder's binding or, while that is unbound, its selector evidence, which only exact sources
    // give. None: an application whose binder no exact source has fixed yet supplies nothing (a structured one is formed when its
    // input is inferred again, SettleWaiting and InferExpectedResult; Never with no evidence). The pattern itself for a pair the
    // callee does not bind (an enclosing declaration's, a fixed layer).
    private bool SourcedLayer(BoundType pattern, BoundType actual, Koto function, SlotFrame frame, out BoundType? formed)
    {
        formed = pattern;
        if (pattern.Symbol is not { } selector || ContainerSlot(function, selector) is not (>= 0 and var slot) || slot >= frame.Types.Length)
        {
            return true;
        }

        var target = pattern.Components[0];
        formed = null;
        if (IsExactSource(pattern, function, frame) && !this.IsSource(frame, slot, actual, target))
        {
            return false;
        }

        return !this.FormedUnder(frame, slot, target, pattern.Origin, out formed) || formed is not null; // Unformed (BU-240): the part fails and the call is Unsupported.
    }

    // SPEC 10.8, 8.1.2: the actual an occurrence of a pair's original binds its slot with: null when it disagrees with the binder's
    // other sources (IsSource), the candidate then inapplicable and an expected result filling nothing; an annotated occurrence's
    // (`s/T during b`) with its outer Origin replaced by the solve's open region for the slot (keyed by the frame's node), since the
    // annotation binds only the occurrence's slot and other evidence fills the region, in either order. Any other slot pattern's
    // actual is itself.
    private BoundType? OccurrenceActual(BoundType pattern, BoundType actual, SlotFrame frame, int slot)
    {
        if (pattern.Symbol is not { Kind: BindingSymbolKind.SemanticsTarget, Type: { } target })
        {
            return actual;
        }

        if (!this.IsSource(frame, slot, actual, target))
        {
            return null;
        }

        return pattern.Origin is not null && actual.Origin is not null
            ? this.WithOrigins(actual, this.AnnotatedOuterRegion(frame.Node, slot), (BoundOrigin[])actual.OriginArguments) : actual;
    }

    // With `evidence`, the actual is a known call signature (SPEC 10.5, 10.8): an argument's, or the parts an anonymous header writes.
    // Its structure and Semantics are evidence, and its Origins are not compared: applicability judges no Origin part (SPEC 15.6.1),
    // and the argument fits the substituted parameter after selection. An Origin the signature itself quantifies, such as a per-call
    // input, lies beyond the call and never solves an Origin in a slot: it becomes an open region that other evidence fills.
    // With `relateLater`, the actual is a value's Type whose Origin relations to the solution are judged after selection (SPEC 15.6.1),
    // so Origin evidence never fails a structurally equal slot binding; `polarity` is the position's (ComponentPolarity), 0 below an
    // exclusive layer or an invariant schema slot. `binder` is the solve's binder (SolveBinder): its Declaration's Constraints decide a
    // read Type, and its SlotOwner's slots are the ones bound.
    private bool Infer(BoundType pattern, BoundType actual, SolveBinder binder, SlotFrame frame, bool inferOrigins = false, bool lengths = false, bool commonOrigins = false, bool structural = false, SignatureEvidence? evidence = null, bool relateLater = false, int polarity = 1)
    {
        // R7-C3 (design §3, J4 at the head; D-779): the evidence in its class's form in the solve's view (SlotFrame.View), once per
        // step (the table keeps each form), so every branch below, a decomposition and a first binding included, reads formed evidence.
        actual = this.ClassForm(actual, frame.View, out _);
        var arguments = frame.Types;
        if (pattern.Kind == BoundTypeKind.AssociatedProjection)
        {
            // An associated Type is not an injective constructor: infer its receiver from
            // other inputs, then check the substituted complete parameter in candidate fitting.
            return true;
        }

        if (pattern.Kind == BoundTypeKind.TargetProjection && pattern.Symbol is { } projected &&
            ContainerSlot(binder.SlotOwner, projected) is >= 0 and var projectionSlot)
        {
            // The target alone does not determine a pair's Semantics; final candidate fitting checks a still-open projection. Once another
            // input, an explicit argument or the result fixes the whole Type, its direct target is the target's binding: evidence agrees
            // with it in structure and Semantics (SPEC 10.8; Origins are judged after selection). A binding is the caller's Type and never a
            // pattern: in a recursive call or a construction of the enclosing structure its symbols are the callee's own slots.
            return projectionSlot < arguments.Length && (arguments[projectionSlot] is not { } whole ||
                (this.DirectTarget(whole) is { } target && (ReferenceEquals(target, actual) || (this.FitsStructuralPart(actual, target) && this.FitsStructuralPart(target, actual)))));
        }

        if (pattern.Kind == BoundTypeKind.SemanticsApplication)
        {
            // SPEC 8.1.2: matched as the Type it forms (SourcedLayer): a layer, in the caller's terms, by its target against the actual's
            // part below the same layer; an owner binding's target, in the callee's terms, or a family as the Type it is.
            // DECISIONS BU-240 (G812 rulings 104 C-E5, 106 C-I1): in a structural walk each compared split layer is its own part of a split
            // search. What lies below it is compared with the part its case formed, so the layers below are handed its reads
            // (CarrySplitPart) until they fit; a sibling layer is handed nothing.
            var target = pattern.Components[0];
            var handed = this.splitCarried;
            if (structural)
            {
                this.EnterSplitPart();
            }

            if (!this.SourcedLayer(pattern, actual, binder.SlotOwner, frame, out var formed))
            {
                return false;
            }

            if (formed is null)
            {
                return true;
            }

            if (structural)
            {
                this.CarrySplitPart(this.splitPartStamp);
            }

            var fits = IsFormedLayer(formed, target)
                ? LayerTarget(formed, actual) is { } below && this.Infer(
                    target,
                    below,
                    binder,
                    frame,
                    inferOrigins,
                    lengths,
                    commonOrigins,
                    structural,
                    evidence,
                    relateLater,
                    this.ComponentPolarity(formed, 0, polarity, default))
                : this.Infer(formed, actual, binder, frame, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, polarity);
            if (fits)
            {
                this.splitCarried = handed;
            }

            return fits;
        }

        // SPEC 3.1.1: the wrapping Scalar Wrapping<u8> is the instance of the pattern Wrapping<T>, so T is inferred from
        // the Scalar's integer argument; the substituted parameter then normalizes to the same Scalar.
        if (actual.IsWrappingInteger && pattern is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && pattern.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping)
        {
            return this.Infer(pattern.Components[0], actual.Underlying, binder, frame, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, polarity);
        }

        if (pattern.Kind == BoundTypeKind.Parameter && ContainerSlot(binder.SlotOwner, pattern.Symbol!) is var slot && slot >= 0)
        {
            if ((uint)slot >= (uint)arguments.Length || this.OccurrenceActual(pattern, actual, frame, slot) is not { } occurrence)
            {
                return false;
            }

            actual = occurrence;

            if (HasArrayLengthHole(actual))
            {
                // An incomplete annotation cannot become a generic argument. Established argument evidence is checked
                // against it at the ordinary result fit; without such evidence the slot remains unsolved.
                return arguments[slot] is not { } establishedArray || this.FitsStructuralPart(establishedArray, actual);
            }

            // SPEC 10.2.2: a parameter constrained to Position, PositionRange or PrimitiveInteger binds the referent; the
            // argument is then value-read.
            if (!structural && actual.SafeReferent is not null &&
                ComparisonReferent(actual) is var referent && !ReferenceEquals(referent, actual) && this.InfersReadReferent(pattern, binder.Declaration, frame.Node))
            {
                actual = referent;
            }

            if (evidence is { Binder: FunctionKoto { ClosureDefinition: not null } closure } && this.HasEnvironmentOrigin(actual, closure))
            {
                // SPEC 8.10, 7.7.4: a result over the closure's hidden environment receiver satisfies no Callable signature and no
                // erasure, so that part is no evidence for the slot; the Callable proof or the conversion judges it against the rest.
                if (frame.EnvironmentJudgment(slot) is null)
                {
                    frame.SetEnvironmentJudgment(slot, new(PendingJudgmentKind.EnvironmentResult, evidence.Value.Source, pattern.Symbol));
                }

                return true;
            }

            if (evidence is { Binder: { } signatureBinder } known)
            {
                actual = this.OpenKnownOrigins(actual, signatureBinder, known.Source);
            }

            // G1128 R1.3 (h): S's position at a function reference (structural, neither a value's Type nor a known signature) is a
            // binding too: its first and every later one that differs in its Origins (the met branch below), so the reference's limit
            // reads its distinct bindings (VariableReferenceSlot). A later one of the same normalized Type is that binding (SPEC 10.8).
            var reference = structural && !relateLater && evidence is null;
            if (arguments[slot] is { } previous)
            {
                // R7-C2c2 c2-4b (design R4.12c, J4): a slot's bindings agree (SPEC 10.8 Solving: an explicit Type argument is kept; the
                // bindings agree in structure and Semantics) over identical Types (8.2.3): the kept binding and the evidence in their
                // class's form in the solve's view (SlotFrame.View), once, before every comparison below; the Completion unthreaded
                // (ClassForm's contract). The evidence is formed at Infer's head (R7-C3), so a first binding and a decomposition read it formed.
                previous = this.ClassForm(previous, frame.View, out _); // the evidence is formed at Infer's head
                if (!ReferenceEquals(previous, actual) && (HasOpenOrigin(previous) || HasOpenOrigin(actual)))
                {
                    // SPEC 10.8: no argument fixes a slot first by traversal order. Another argument's Origin at an open position
                    // solves it, whichever comes first; two open positions keep the earlier region.
                    previous = this.FillOpenOrigins(previous, actual, false);
                    actual = this.FillOpenOrigins(actual, previous, true);
                }

                var held = false;
                if (((relateLater && !structural) || evidence is not null) && (ReferenceEquals(previous, actual) ||
                    (this.FitsStructuralPart(actual, previous, out held) && this.FitsStructuralPart(previous, actual))))
                {
                    // SPEC 10.8 (as revised; O-450 (a), O-451 (a)), 15.3.6: Origin evidence for a slot never makes the candidate
                    // inapplicable, and the bindings agree in structure and Semantics. The frame keeps the first binding's Type until the
                    // late solve; every evidence binding is recorded with its composed polarity, this later one as the first below, by
                    // whichever caller matched it (an argument, a header, a Callable signature; G147 ruling 59: no caller's flag decides
                    // it), and its Origins bound the slot's variables, so Infer merges no Origins. Whether the slot is fixed is the slot
                    // step's one test (SlotSolution.FixedSlot, read before the bindings): a fixed slot keeps its Type. Evidence held
                    // against a fill's atom waits and records nothing (Binding.OpenSlots: fill first, K's D-1132).
                    if (!held)
                    {
                        frame.AddBinding(slot, actual, polarity);
                    }

                    frame.SetType(slot, previous);
                    return true;
                }

                // Binding.OpenSlots (SPEC 10.8 Solving; K's D-1132, ArgFlow ruling 154): the expected result's match fills a slot with
                // the structure its fills agree in (FillOpenSlots), so no traversal order keeps the first atom met; fills that still meet
                // an atom against a part agree in none, and the match fails.
                if (!structural && inferOrigins && !relateLater && evidence is null && (previous.ContainsOpenSlot || actual.ContainsOpenSlot))
                {
                    var kept = this.FillOpenSlots(previous, actual);
                    var met = this.FillOpenSlots(actual, previous);
                    if (!ReferenceEquals(kept, met) && (!this.FitsStructuralPart(met, kept, out var apart) || apart || !this.IsSubtype(met, kept)))
                    {
                        return false;
                    }

                    frame.SetType(slot, kept);
                    return true;
                }

                // SPEC 10.8: structural matching compares normalized Types, so two spellings of one Function Type, whose per-call
                // inputs have distinct binders, are one binding.
                if (ReferenceEquals(previous, actual) || (!structural && inferOrigins && this.IsSubtype(actual, previous)) ||
                    (structural && previous.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function &&
                    this.IsSubtype(actual, previous) && this.IsSubtype(previous, actual)))
                {
                    frame.SetType(slot, previous);
                    return true;
                }

                // G1128 (design R1.3 (h), R1.4; the code review's C1, G147 ruling 59): Infer's one merge of Origins outside a call's
                // solve. Only function references pass `commonOrigins` (InferReferenceSlots and the result's solve; SPEC 10.5.3, no
                // late solve; G147 ruling 62: a call passes none), so only they reach it: it meets their bindings that differ in covariant
                // Origins and records each. The met Type is read by the reference's Constraint proof (ReferenceArgumentProof), SPEC 10.6's per-call
                // rule (PerCallReferenceSlot) and the limit's fixed-Origin test (VariableReferenceSlot's InputOrigins), and the recorded
                // bindings make a slot that would need variables the located limit. A call records its bindings above. It goes when a
                // reference's solve owns its bindings and gives those three readers its values (PLAN's R1.3 (h) line).
                if (commonOrigins && this.CommonOriginType(previous, actual, frame.Node, out _) is { } common)
                {
                    if (reference)
                    {
                        frame.AddBinding(slot, actual, polarity);
                    }

                    frame.SetType(slot, common);
                    return true;
                }

                return false;
            }

            frame.SetType(slot, actual);
            if ((relateLater && !structural) || evidence is not null || reference)
            {
                frame.AddBinding(slot, actual, polarity);
            }

            return true;
        }

        // R7-C3 (F's C2 ruling 276 (1); SPEC 8.2.3): a Type parameter that is no slot of the solve (an enclosing declaration's, as a
        // nested callee's signature names it) is a Type of the solve's view, so it meets the formed evidence in its class's form there,
        // by the judge that formed the evidence at the head.
        if (pattern.Kind == BoundTypeKind.Parameter)
        {
            pattern = this.ClassForm(pattern, frame.View, out _);
        }

        if (ReferenceEquals(pattern, actual))
        {
            return true;
        }

        // PLAN G924 AF-3b v3-A (Binding.OpenSlots; S1): an atom on the actual side holds against a pattern part that is no slot of the
        // solve and binds nothing; a slot of the solve takes it above, as any first binding (a fill).
        if (IsOpenAtom(actual))
        {
            return true;
        }

        // An interned leaf that passes these tests differs from the pattern in its Origin alone, which is evidence or a later relation
        // where Origins are not compared here: a pair layer's outer slot is such an Origin (SPEC 8.1.2, 15.3.6; PLAN G758).
        if (pattern.Kind != actual.Kind || pattern.Symbol != actual.Symbol || pattern.Semantics != actual.Semantics ||
            !(lengths && pattern.Kind == BoundTypeKind.FixedArray ? actual.Length < 0 || InferLengthSlot(this.LengthOf(pattern), this.LengthOf(actual)) : pattern.Length == actual.Length && ReferenceEquals(pattern.LengthExpression, actual.LengthExpression)) ||
            (!inferOrigins && evidence is null && !ReferenceEquals(pattern.Origin, actual.Origin)) || pattern.OriginArguments.Count != actual.OriginArguments.Count || pattern.Components.Count != actual.Components.Count || pattern.LengthArguments.Length != actual.LengthArguments.Length || (pattern.Components.Count == 0 && pattern.LengthArguments.Length == 0 && pattern.OriginArguments.Count == 0 && (ReferenceEquals(pattern.Origin, actual.Origin) || !ReferenceEquals(pattern.InstanceContext, actual.InstanceContext))))
        {
            return false;
        }

        for (var i = 0; i < pattern.LengthArguments.Length; i++)
        {
            var required = pattern.LengthArguments[i];
            var supplied = actual.LengthArguments[i];
            if (!ReferenceEquals(required, supplied) && (required is null || supplied is null || !lengths || !InferLengthSlot(required, supplied)))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.OriginArguments.Count; i++)
        {
            if (!inferOrigins && evidence is null && !ReferenceEquals(pattern.OriginArguments[i], actual.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            if (!this.Infer(
                pattern.Components[i],
                actual.Components[i],
                binder,
                frame,
                inferOrigins,
                lengths,
                commonOrigins,
                structural,
                evidence,
                relateLater,
                this.ComponentPolarity(pattern, i, polarity, default)))
            {
                return false;
            }
        }

        return true;

        // A local annotation's length hole (a negative Length, above) supplies no evidence for a callee's length slot. SPEC 10.8 Solving,
        // Binding.OpenSlots (ArgFlow rulings 166, 167): a length slot's bindings agree by the Type slot's rule above: the first is kept and an
        // equal one agrees; evidence held against a fill's atom waits; in the expected result's match a length atom takes the closed length
        // its fill meets, and two distinct atoms agree in none. InferLength keeps the length-specific forms.
        bool InferLengthSlot(BoundLength required, BoundLength supplied)
        {
            var holds = this.InferLength(required, supplied, binder.SlotOwner, frame, out var slot);
            if (slot < 0)
            {
                return holds;
            }

            if (frame.Lengths[slot] is not { } previous)
            {
                frame.SetLength(slot, supplied);
                return true;
            }

            if (ReferenceEquals(previous, supplied) || (((relateLater && !structural) || evidence is not null) && previous.ContainsOpenSlot != supplied.ContainsOpenSlot))
            {
                return true;
            }

            if (structural || !inferOrigins || previous.ContainsOpenSlot == supplied.ContainsOpenSlot)
            {
                return false;
            }

            if (previous.ContainsOpenSlot)
            {
                frame.SetLength(slot, supplied);
            }

            return true;
        }
    }

    // SPEC 10.8, 10.8.1, 6.5.2 step 2 (DECISIONS: the fixed bindings): a solution's slot bindings fixed before any evidence, over the slot
    // owner's slots (FixCallSlots for a candidate, ConstructionSlots for a construction Type, a Case construction's seeding): each slot's
    // binding, an open slot as its own parameter (only an inferred construction, a structure's or a Case's, leaves one beside fixed ones),
    // and the lengths, null where none is fixed; no Types when no slot is fixed. The adaptation reads it through FixedPattern; whether a
    // pattern is complete under it is read from the frame before any evidence instead (the contract above). Its spans live as long as
    // the call's explicit Types, the declaring Type or the Case's scratch.
    private readonly ref struct FixedSlots(Koto owner, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths)
    {
        internal Koto? Owner { get; } = owner;

        internal ReadOnlySpan<BoundType?> Types { get; } = types;

        internal ReadOnlySpan<BoundLength?> Lengths { get; } = lengths;
    }

    // SPEC 10.2.2 step 1, 10.8, 6.5.2 step 2: the only input of the adaptation entry (AdaptInput): a pattern under the slot bindings fixed
    // before any evidence. Of substitutes a solution's FixedSlots (a slot still open stays a slot); Open states that its reader fixes no
    // slot: a completed parameter Type, or a conversion's, a property's, a receiver's or a value call's pattern, which holds none.
    private readonly struct FixedPattern
    {
        private FixedPattern(BoundType type) => this.Type = type;

        internal BoundType Type { get; }

        internal static FixedPattern Of(Binding binding, BoundType pattern, in FixedSlots fixedSlots)
            => new(fixedSlots.Types.IsEmpty ? pattern
                : binding.SubstituteType(pattern, fixedSlots.Owner!, fixedSlots.Types, fixedSlots.Lengths) ?? pattern);

        internal static FixedPattern Open(BoundType pattern) => new(pattern);
    }

    // SPEC 10.2.2, 10.8, 10.8.1: the binder of one solve, which the unifier (Infer) is given: a candidate's function (a constructor
    // included), an enum constructing a Case, a referenced function. Its Declaration's Constraints decide a read Type
    // (InfersReadReferent); its slots are SlotOwner's (CallSlotOwner: the structure for a constructor), formed once per solve. Made only
    // from a function or an enum, so a slot owner (a structure) is never passed where the binder is meant.
    private readonly struct SolveBinder
    {
        internal SolveBinder(FunctionKoto function)
            : this((Koto)function)
        {
        }

        internal SolveBinder(EnumKoto declaration)
            : this((Koto)declaration)
        {
        }

        private SolveBinder(Koto binder) => (this.Declaration, this.SlotOwner) = (binder, CallSlotOwner(binder));

        internal Koto Declaration { get; }

        internal Koto SlotOwner { get; }
    }
}
