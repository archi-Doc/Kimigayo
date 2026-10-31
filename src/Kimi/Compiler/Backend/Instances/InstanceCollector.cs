// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the checked set (SPEC 8.15.3, IMPL 21.3.1; R5 package K 4.3-4.9; DECISIONS R5-M35, R5-M40, R5-M43, R5-M44, R5-M52-55, R5-M60,
// R5-M67, R5-M70, R5-M74, R5-M146, R5-M149, R5-M150): every body instance, type-driven item and shim a root requires through runtime or
// check-only edges, its instance-stage records, and the one resolution of each site's target that code generation reads.
// Input: a built, validated MirProgram; the number of root modules (a range of Source.Module, not a mode); TyCtxt (Instantiates, Erase,
// Compose, Instance, BindingComplete, Implementation, Witness, Comparison, OwnRequirement, Published, Bodyless, Signature,
// SubstOfClosure, SubstOfItem, Synthesized, Applies, Imports, SignatureSite); MirCases (Views, and an item's view for a PairLayer's
// case); MirUses; MirTypes.MayNeedDrop; DropGlue.Parts; AdtDef.FindField; VTableDef; MirProgram.StaticInit; the Target and Need columns
// of the intrinsic rows and the legacy rows; LayoutNeeds; the capability table; the compilation's Layout (Indirect), whose outcomes the
// collector alone maps (R5-M37).
// Output: the CheckedSet of the program version: the roots, in MirProgram.Bodies order, then the items a FIFO worklist admits, each with one
// target per resolved site of its body, in location order, with whether a runtime path reaches the site; and its records, which
// Compilation.MirRecords publishes at check on every pipeline (K4b; DECISIONS BU-48, R5-M156); for code generation (R5 C0b1) also an
// item's Types (TypeOf), the location of a record at a site of its body (Subject)
// and a PropertyWitness item's key signature (StorageSignature; DECISIONS R5-M141, the owner's R5-M142); a borrowing callee's owner Value
// call also targets its operand's glue, after the body (DECISIONS R5-M102).
// Roots (K 4.4): each body of a root module whose definition does not instantiate, a module's top-level statements included.
// Edges (K 4.5): one template per body and version (InstanceCollector.Templates.cs) lists its sites that may carry a target (Fn,
// Requirement and Value calls and the intrinsics the Target column names, a Virtual call none, X5; a Closure environment, an erasure and a
// function item constant; a Drop), each with its runtime bit from one MirReach walk without CheckTarget edges. An item's targets are
// InstanceCollector.Edges.cs's: replaying a body item's template resolves each site's row under the item's representative, a callee's body
// (InstanceCollector.Callees.cs), and a Requirement call's, a requirement Item's, a bodyless callee's published row's and an erased writer's
// by the witness there (InstanceCollector.Witnesses.cs: a body, a Builtin or a property-witness shim keyed by its compiler function or Field
// and the erased Self, or a gap); a bodyless callee whose legacy row writes its call in its instance (the Array operations and storage
// kinds, placeEntry) targets its function instance's LegacyRow item (AdmitRow: the function and the erased Subst, no body, represented by
// the storage its handle parameter designates; DECISIONS R5-M121, R5-M124); a Dictionary from none targets the body of the
// DictionaryStorage function the Dictionary runtime declares for construction, initialize (Declared, through TyCtxt's F2c view); a
// type-driven item's targets follow from its representative Type by its kind's row (Expand: an adapter's Item; InstanceCollector.Glue.cs:
// a DropGlue's direct parts, DropGlue.Parts, a Dictionary's the clearEntries instance its Drop row declares when an entry may need a drop,
// and a LegacyRow's declared callee and needs, placeEntry's append body and an ElementGlue need its Element parts' glue;
// InstanceCollector.Witnesses.cs: a comparison item's direct parts' items and witnesses (Comparison, DECISIONS R5-M166), a
// property-witness setter's Field glue; InstanceCollector.Objects.cs: a created payload's descriptor's
// glue and VTable, a VTable's slot bodies by VTableDef and Applies).
// Capability (K 4.7, InstanceCollector.Capabilities.cs): the definition-stage check (A3b) of every operation of a root-module body whose
// Types are closed, every block included, once per definition and location, and of a root-module static's declaration (static storage
// P42 has not brought, a generic container's included, DECISIONS R5-M82); Binding's refusals are Opaque values, so it never repeats them.
// An operation whose Types depend on slots is a template site, checked in each instance with its Types there, once per location.
// Layout obligations (K 4.8, InstanceCollector.Layouts.cs): each item's by-value uses (its body's locals but drop flags and a constructor's
// construction receiver, DECISIONS BU-48; its aggregates; the inputs and result of an erasure's target Function Type, DECISIONS R5-M108),
// each type-driven item's Type and a DropGlue's collection element Types (X34), and each layout need a form's generated code has past its
// root's storage (LayoutNeeds, DECISIONS R5-M149: a projection step past a Deref, PairLayer or sequence handle, a raw pointer's
// displacement, a call's Type arguments where its intrinsic or legacy row's need says so, a compared Tuple past a reference layer of a
// comparison call's node, at the call, R5-M166), and the parameters and result of each import a root module declares, at their written Types (no item; SPEC
// 22.6, DECISIONS R5-M150) take Layout's verdict: a limit is GenerationResourceLimit_Kd once per erased Type at its
// first use, a C-layout instance whose C layout is unavailable (open, derived, empty or storing a zero-sized Field) InvalidCLayout_Kd and
// a layout over 2^63 - 1 bytes OversizedLayout_Kd at each use (both at one use when both hold); Rejected storage is a broken contract in a
// complete compilation and otherwise a dependent that Report drops; a type-driven item needs an Ok verdict.
// Storage (DECISIONS BU-36, BU-38): TyCtxt's verdict on a Type or substitution of an item's instance is read once, at its requesting site
// (Made): Prepared makes it; Rejected rests on a part Binding rejected, as Layout's Rejected, which a complete compilation has none of
// (TyCtxt.BindingComplete), so it is a broken contract at its site (Report); a limit is the storage-nesting gap. Every row reads a Type of
// an instance at its site through In, Below or Instantiable, so through Made, never through TypeOf, TyCtxt.Apply or TyCtxt.Signature
// under a substitution; those read no verdict and serve code generation and a Type whose verdict its site already read (Rest's operand and
// environment, Agrees' signature under a witness's substitution whose Self the call's composition made, Bodyless's intrinsic row's Type
// argument, a part of the Item Type its site made).
// Admission (K 4.6, X19): a new key is admitted once; its representative is the first composed substitution or Type, erased only into the
// key (KD3). A request of a definition generic in its context whose substitution does not carry the instance it instantiates in
// (CarriesInstance, the instance of the declaration it holds, TyCtxt.EnclosingDeclaration: a nested function's call, DECISIONS R5-M43; a
// Closure no instance closed, in its own identity: a held instance with an Origin position or a generic container's group static,
// R5-M157, BU-60), a substitution or Type whose storage is a limit (Made),
// OCC-X's refused witness and a requirement call's or requirement Item's witness without a MIR body (Called) are the capability table's
// gaps (an access of a static of a generic container's group is none: its declaration is, X33 (a), R5-M82); growth (DECISIONS R5-M170):
// a type-driven key whose Type is higher than 128, a 17th generation of an identity (a body's or LegacyRow's definition, a DropGlue's
// declaration) on an admission chain, its height grown at each, and a 1,025th item of an identity are GenerationResourceLimit_Kd; a
// representative that is open or leaves its body open, and a requirement call of a closed Self without a witness, are broken contracts.
// A refused edge is dropped and collection continues.
// Records (DECISIONS R5-M151): every record goes through one path (Report). A broken contract is InternalInvariant_Kd where Binding is
// complete; in an incomplete compilation a fact the collector reads may be undecided because it rests on a part Binding rejected, which the
// collector cannot name, so the record is that error's dependent and is dropped (SPEC 23.1.2); the edge is refused all the same.
// Location (K 4.9): a record's subject is the cause's site, moved out of inlined library defaults to the outermost omitting call, else the
// root-module site the admission chain first reached; code generation's records take the same rule.
// Invariants: deterministic; one item per key; every representative is closed and closes its body (K 4.3); one target per site of a body,
// plus the operand's glue at a borrowing callee's owner Value call, after its body (SPEC 8.10; DECISIONS R5-M102); a type-driven item's
// targets are at its default site, a DropGlue's in DropGlue.Parts' order, a ClosureErasure's body before its environment's glue; a repeated
// run of a version is a no-op; a program with validator findings has an empty set; a warm run of a
// program as large as an earlier one allocates nothing.
// Must not: read syntax, HIR or plans; depend on the MIR analyses' results or edit their driver; decide acceptance or publish its records
// (Compilation does); prove anything on an erased value; add an IntrinsicId, CompilerFunctionKind or BindingFailure (PD21).

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>Collects the checked set of a program version (K package 4.3).</summary>
internal sealed partial class InstanceCollector
{
    private readonly TyCtxt types;
    private readonly MirCases cases;
    private readonly Layout layout;
    private readonly HashSet<BoundType> limited = new(); // The erased Types whose verdict is a limit, each reported once (K 4.8).
    private readonly HashSet<MonoKey> refused = new(); // The keys refused for growth, each reported once (DECISIONS R5-M170).
    private readonly Dictionary<(MonoKind Kind, MirDefKey Definition), int> counts = new(); // Items per identity (X19, DECISIONS R5-M170).
    private readonly HashSet<BoundType> seen = new(); // MirTypes.MayNeedDrop's scratch.
    private readonly List<DropPart> parts = new(); // DropGlue.Parts' scratch.
    private readonly Dictionary<ComparisonNode, byte> uses = new(ReferenceEqualityComparer.Instance); // Uses' positions of each node walked.
    private int rootModules;

    /// <summary>Initializes a new instance of the <see cref="InstanceCollector"/> class.</summary>
    /// <param name="types">The Type context of the programs.</param>
    /// <param name="layout">The layout of the programs' compilation.</param>
    internal InstanceCollector(TyCtxt types, Layout layout)
    {
        this.types = types;
        this.layout = layout;
        this.cases = new(types);
        this.cases.Deselect(); // A representative is closed, so the views it selects need no caller view.
    }

    /// <summary>Gets the checked set of the latest run.</summary>
    internal CheckedSet Set { get; } = new();

    /// <summary>Gets the work of the latest run: the sites replayed and the items admitted, which grow linearly with the instances.</summary>
    internal int Work { get; private set; }

    /// <summary>Collects the checked set of a program version unless this version already was.</summary>
    /// <param name="program">The built and validated program.</param>
    /// <param name="rootModules">The number of root modules: the bodies of modules below it are roots.</param>
    /// <returns>The checked set.</returns>
    internal CheckedSet Run(MirProgram program, int rootModules)
    {
        var set = this.Set;
        if (program.Version == set.Version)
        {
            return set;
        }

        set.Clear(program.Version);
        this.counts.Clear();
        this.limited.Clear();
        this.refused.Clear();
        (this.rootModules, this.Work) = (rootModules, 0);
        if (program.Findings.Count > 0)
        {
            return set;
        }

        this.Templates(program);
        foreach (var body in program.Bodies)
        {
            if (body.Source.Module < rootModules && (body.Key.Decl is null || !this.types.Instantiates(body.Key.Decl)))
            {
                var key = new MonoKey(MonoKind.Body, new(body.Key, null), null, null, 0, null);
                set.Add(new() { Key = key, BodyOrdinal = body.Ordinal, Parent = -1, Growth = new(1, 0, -1), Site = body.Source });
            }
        }

        set.EndRoots();
        this.LayOutImports();
        for (var item = 0; item < set.Items.Length; item++)
        {
            this.Replay(program, item);
        }

        return set;
    }

    /// <summary>Gets a Type of an item's body in the item's instance (S2-S4 X2), the one instance rule of the collector's edges and layout
    /// obligations and of code generation (R5 C0b1): the Type itself for a closed item, else its instance under the representative when its
    /// storage is Prepared (Instance).</summary>
    /// <param name="item">The item.</param>
    /// <param name="type">A Type of its body.</param>
    /// <returns>The Type in the instance, or null when its storage is not Prepared (a site maps the verdict, In).</returns>
    internal BoundType? TypeOf(int item, BoundType type) => this.Instance(item, type) is ({ } closed, Binding.StorageOutcome.Prepared) ? closed : null;

    /// <summary>Gets whether a storage verdict of TyCtxt makes the instance a site requests, the collector's one reading of it (DECISIONS
    /// BU-36, BU-38, R5-M37): Prepared does; Rejected rests on a part Binding rejected, a broken contract (Report); Limit is the
    /// storage-nesting gap (PLAN G128) at the site.</summary>
    /// <param name="storage">The verdict.</param>
    /// <param name="subject">The requesting site.</param>
    /// <returns>Whether the verdict is Prepared.</returns>
    internal bool Made(Binding.StorageOutcome storage, SourceRef subject)
    {
        DiagnosticCode? record = storage switch
        {
            Binding.StorageOutcome.Prepared => null,
            Binding.StorageOutcome.Rejected => DiagnosticCode.InternalInvariant_Kd,
            Binding.StorageOutcome.Limit => Capability.Code(CapabilityGap.StorageNesting),
            _ => throw new InvalidOperationException($"The storage verdict {storage} has no row."),
        };

        if (record is { } code)
        {
            this.Report(code, subject);
        }

        return storage == Binding.StorageOutcome.Prepared;
    }

    // A Type of an item's body in the item's instance with its storage verdict (TyCtxt.Instance), the one path of TypeOf and In: the Type
    // itself, Prepared, for a closed item.
    private (BoundType? Type, Binding.StorageOutcome Storage) Instance(int item, BoundType type)
        => this.Set.Items[item].Representative is { } representative ? this.types.Instance(type, representative) : (type, Binding.StorageOutcome.Prepared);

    // A Type of an item's body in the item's instance (Instance), its verdict read at the site (Made); null unless Prepared.
    private BoundType? In(int item, SourceRef subject, BoundType type)
        => this.Instance(item, type) is var (closed, storage) && this.Made(storage, subject) ? closed : null;

    // K 4.5 rows 2 and 7 (DECISIONS BU-38): the Type below a bodyless callee's parameter at a slot in its call's instance (TyCtxt.Instance of
    // the declared Type; the declared Type without a substitution), its verdict read at the site (Made); null unless Prepared. A Prepared
    // slot without a Type below is a broken contract (a published row past the parameters).
    private BoundType? Below(SourceRef subject, BindingSymbol function, Subst? subst, int slot)
    {
        var declared = TyCtxt.Signature(function, null, slot, out _);
        var (type, storage) = declared is null || subst is null ? (declared, Binding.StorageOutcome.Prepared) : this.types.Instance(declared, subst);
        _ = TyCtxt.Layer(type, out var below, out _);
        if (storage == Binding.StorageOutcome.Prepared && below is null)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, subject);
        }

        return this.Made(storage, subject) ? below : null;
    }

    // SPEC 23.1.2, DIAGNOSTICS rules 1 and 9 (DECISIONS R5-M151, amending BU-38's guard): the collector's one record path. A broken contract,
    // InternalInvariant_Kd, presupposes a complete Binding; in an incomplete compilation it may rest on a part Binding rejected, which the
    // collector cannot name, so it is that error's dependent, without a record. Every other record is kept.
    private void Report(DiagnosticCode code, SourceRef subject)
    {
        if (code != DiagnosticCode.InternalInvariant_Kd || this.types.BindingComplete)
        {
            this.Set.Report(code, subject);
        }
    }
}
