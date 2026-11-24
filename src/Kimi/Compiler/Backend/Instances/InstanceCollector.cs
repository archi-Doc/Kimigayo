// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the checked set (SPEC 8.15.3, IMPL 21.3.1; R5 package K 4.3-4.9; DECISIONS R5-M35, R5-M40, R5-M43, R5-M44, R5-M52-55, R5-M60,
// R5-M67, R5-M70, R5-M74, R5-M146, R5-M149, R5-M150): every body instance, type-driven item and shim a root requires through runtime or
// check-only edges, its instance-stage records, and the one resolution of each site's target that code generation reads.
// Input: a built, validated MirProgram whose bodies the MIR analyses' stepping path has stepped (A2's table holds them, MirDropTable.Holds:
// a run stops before a body it does not hold, Round below), as code generation reads them; every A2 entry it reads is the
// version's, since its runs follow the version's library step and MirProgram cleared the program store where the version started
// (PLAN G951 U2-K-b, F18); the root range (Source.Module below it,
// not a mode); TyCtxt (Instantiates, Erase,
// Answered, Apply, BindingComplete, Implementation, Witness, Comparison, OwnRequirement, Published, Bodyless, Signature,
// SubstOfClosure, SubstOfItem, Synthesized, Applies, Imports, SignatureSite); MirCases (Views, an item's view for a PairLayer's case,
// an Adapt's AdaptSource and Operation); MirUses; MirTypes.MayNeedDrop; DropGlue.Destroyed, Level and Parts; AdtDef.FindField; VTableDef;
// MirProgram.StaticInit; the Target and Need columns of the intrinsic rows; LayoutNeeds; the capability table; the
// compilation's Layout (Indirect), whose outcomes the collector alone maps (R5-M37).
// Output: the CheckedSet of the program version: the roots, in MirProgram.Bodies order, then the items a FIFO worklist admits, each with one
// target per resolved site of its body, in location order, with whether a runtime path reaches the site; and its records, the program's
// collection records (MirProgram.Report with MirRecordClass.Collection, once per code and subject; R6-check H3-1), which Core's publisher
// (Compilation.Publication) publishes at check (K4b; DECISIONS BU-48, R5-M156, R5-M184); for code generation (R5 C0b1) also an
// item's Types (TypeOf), the location of a record at a site of its body (Subject)
// and a PropertyWitness item's key signature (StorageSignature; DECISIONS R5-M141, the owner's R5-M142); a borrowing callee's owner Value
// call also targets its operand's glue, after the body (DECISIONS R5-M102).
// Roots (K 4.4): each body of a root module whose definition does not instantiate, a module's top-level statements included.
// Edges (K 4.5): one template per body (InstanceCollector.Templates.cs: in the library store, kept while its library segment holds, PLAN
// G950, for a body A2's library store holds, MirDropTable.LibraryHolds, which is stable within a version; else in the program store)
// lists its sites that may carry a target (Fn, Requirement and Value calls and the intrinsics the Target column names, a Virtual
// call none, X5; a Closure environment, an erasure and a function item constant; a Drop), each with its runtime bit from one MirReach
// walk without CheckTarget edges. An item's targets are
// InstanceCollector.Edges.cs's: replaying a body item's template resolves each site's row under the item's representative, a callee's body
// (InstanceCollector.Callees.cs), and a Requirement call's, a requirement Item's, a bodyless callee's published row's and an erased writer's
// by the witness there (InstanceCollector.Witnesses.cs: a body, a Builtin or a property-witness shim keyed by its compiler function or Field
// and the erased Self, or a gap); a Dictionary from none targets the body of DictionaryStorage.initialize (Declared); a
// type-driven item's targets follow from its representative Type by its kind's row (Expand: an adapter's Item; InstanceCollector.Glue.cs:
// a DropGlue's level, DropGlue.Level, a collection's its one destruction body (Collection); InstanceCollector.Witnesses.cs: a
// comparison item's direct parts' items and witnesses (Comparison, DECISIONS R5-M166), a property-witness setter's Field glue;
// InstanceCollector.Objects.cs: a created payload's descriptor's glue and VTable, a VTable's slot bodies by VTableDef and Applies).
// Capability (K 4.7, InstanceCollector.Capabilities.cs): the definition-stage check (A3b) of every operation of a root-module body whose
// Types are closed, every block included, once per definition and location, and of a root-module static's declaration (static storage
// P42 has not brought, a generic container's included, DECISIONS R5-M82), judged in the root loop of every version's first run before
// LayOutImports (PLAN G950: never kept with a template); Binding's refusals are Opaque values, so it never repeats them.
// An operation whose Types depend on slots is a template site, checked in each instance with its Types there, once per location.
// Layout obligations (K 4.8, InstanceCollector.Layouts.cs): each item's by-value uses (its body's locals but drop flags and a constructor's
// construction receiver, DECISIONS BU-48; its aggregates; the inputs and result of an erasure's target Function Type, DECISIONS R5-M108),
// each type-driven item's Type and a DropGlue's collection element Types (X34), and each layout need a form's generated code has past its
// root's storage, at the form (LayoutNeeds' rows and the Need columns, the Type its TypeOf names, past Layout.Indirect's boundary or a
// pair layer the item's case makes a referent; DECISIONS R5-M149, R5-M166), and the parameters and result of each import a root module
// declares, at their written Types (no item; SPEC 22.6, DECISIONS R5-M150) take Layout's verdict: a limit is GenerationResourceLimit_Kd
// once per erased Type and declaration, at the least of that declaration's failing uses, a use in MirReport's limit table (MirReport.Limit),
// whose records the version's last run flushes at its end (DECISIONS R5-M171), a
// C-layout instance whose C layout is unavailable (open, derived, empty or storing a zero-sized Field) InvalidCLayout_Kd and
// a layout over 2^63 - 1 bytes OversizedLayout_Kd at each use (both at one use when both hold); Rejected storage is a broken contract in a
// complete compilation and otherwise a dependent that Report drops; a type-driven item needs an Ok verdict.
// Formation (DECISIONS BU-36, BU-38; R7-T1): a Type or substitution of an item's instance is the substitution alone, never its
// storage verdict (Binding's bound is retention's and A4's, not the collector's: a finite Type past it is generated, its growth R5-M170's
// rule). Each run (a stop falls between items, outside every formation) is Binding's one answer frame (TyCtxt.OpenAnswer, closed in a finally, so no throw leaves it open), so every Type the
// run causes Binding to form answers a refusal past IMPL A.4's height to the formation owning it, of three: a substitution at a site, an
// instance's stored Types, and a requirement's witness resolution (TyCtxt.Witness: the Contract instance, the conformances it forms and
// the selected conformance's premises, SPEC 8.6.1, 8.8.2, its target's Cause). Every row reads a Type of an instance at its site, a local of a new key's body and a call's composed
// substitution through TyCtxt's formation query (TyCtxt.Answered), and a witness target where it calls Witness, whose cause the collector
// reads once, at its requesting site (Formed): Formed makes it; HeightLimit is TypeHeightLimit_Kd (Resource, IMPL 21.3.2) at the site (a local's at the local, the Type it writes
// forming it), once per requesting declaration and forming site at its least use, as a Layout limit is (DECISIONS R5-M171); Incomplete is
// a broken contract at the site (Report). An instance's stored Types past the height are its own cause (AdtDef.InstanceStorage.HeightLimit),
// which every reader of the instance reads: Layout as its HeightLimit, mapped here (RecordOf) to TypeHeightLimit_Kd at the use, once per
// erased Type and declaration (R5-M171), never Rejected. A part Binding rejected passes through to Layout's Rejected row (RecordOf), a
// dependent of Binding's record. A refusal no formation owns is the frame's, R7-T1's broken contract: Run throws after the close. TypeOf
// reads TyCtxt.Apply, the same substitution outside a formation, for code generation and a Type whose cause its site already read (Rest's
// operand and environment, Agrees' signature under a witness's substitution whose Self the call's composition made, Bodyless's intrinsic
// row's Type argument, a part of the Item Type its site made).
// Admission (K 4.6, X19): a new key is admitted once; its representative is the first composed substitution or Type, erased only into the
// key (KD3). A request of a definition generic in its context whose substitution does not carry the instance it instantiates in
// (CarriesInstance, the instance of the declaration it holds, TyCtxt.EnclosingDeclaration: a nested function's call, DECISIONS R5-M43; a
// Closure no instance closed, in its own identity: a held instance with an Origin position or a generic container's group static,
// R5-M157, BU-60), OCC-X's refused witness and a requirement call's or requirement Item's witness without a MIR body (Called) are the
// capability table's gaps (an access of a static of a generic container's group is none: its declaration is, X33 (a), R5-M82); growth
// (DECISIONS R5-M170):
// a 17th generation of an identity (a body's definition, a DropGlue's declaration) on an admission chain, its height grown
// at each, and a 1,025th item of an identity are GenerationResourceLimit_Kd, once per key; a type-driven key whose Type is higher than
// 128 is its erased Type's limit, recorded as a Layout limit is (DECISIONS R5-M171); a
// representative that is open or leaves its body open, and a requirement call of a closed Self without a witness, are broken contracts.
// A refused edge is dropped and collection continues.
// Round (PLAN G951, G337 U2; DECISIONS R9-Bx): a version's runs replay its FIFO once. Its first run starts the set: it clears the set and
// the admission state (counts, refused, glueKeys, declaring), opens the version's round in the report (MirReport.Begin with
// MirRecordClass.Collection, the one commit A4's rounds share; a record made outside it is added at once), templates the bodies, judges
// A3b in the root loop, adds the roots and lays out the imports. Every run then templates the bodies built since (a stopped run's lookups'
// and the step's) and replays the items from the one the version's last run stopped before, in FIFO order, and stops before a body item
// whose body A2's table does not hold (one a lookup built since the last step): its set keeps every item admitted and every target made
// and holds no version (CheckedSet.Version 0, which no reader reads), and the admission state and the round stay open. Its driver
// (Compilation.Collect) steps the bodies built since (MirAnalysis.Step) and runs it again; the run that reaches the FIFO's end flushes the
// least uses (MirReport.FlushLimits), commits the round and stamps the set with the version. A run replays only stepped bodies, whose
// facts are the eager build's, so the version's runs equal the one run over a program whose bodies were all built before: its items in
// their order, targets and records, and its Work (each item, site and declaring step once); Work, RunCount (one more than the stops) and
// Templated sum the runs of the version of the latest run. Every run of a version takes its first run's root range (another is a broken
// contract). A run that throws leaves nothing to resume: the version's next run drops the round it left open and starts the version
// again, never resuming over a partially replayed item. A body a lookup built and the collector then refused (no item's) may end
// unstepped, so the bodies unstepped at the end are neither the eager build's nor a rerun's (R9-Bx: MirProgram.Keeps and hover read it).
// Records (DECISIONS R5-M151): every record goes through one path (Report). A broken contract is InternalInvariant_Kd where Binding is
// complete; in an incomplete compilation a fact the collector reads may be undecided because it rests on a part Binding rejected, which the
// collector cannot name, so the record is that error's dependent and is dropped (SPEC 23.1.2); the edge is refused all the same.
// Location (K 4.9): a record's subject is the cause's site, moved out of inlined library defaults to the outermost omitting call, else the
// root-module site the admission chain first reached; code generation's records take the same rule. A record's declaration is the body
// that holds its subject (Declaration; an import's own).
// Invariants: deterministic; one item per key; every representative is closed and closes its body (K 4.3); one target per site of a body,
// plus the operand's glue at a borrowing callee's owner Value call, after its body (SPEC 8.10; DECISIONS R5-M102); a type-driven item's
// targets are at its default site, a DropGlue's in DropGlue.Level's order, a ClosureErasure's body before its environment's glue; a repeated
// run of a version a run collected to its end is a no-op, the version alone keying the run (the set, its runs' counts, the item a run
// resumes at, the admission state, the walk's position and the program store), because an instance collects the programs of one builder: Compilation pairs its collector with its builder and
// analysis, and a test or measurement makes all three, so one collector over two programs of one Binding pass is outside its contract
// (only the library store is keyed on the program and its segment, PLAN G951 U2-K); a body with a finding (MirBody.HasFinding) is no template, root or item (DECISIONS
// R5-M183: the Templates walk, the root loop and Admit are the only ways a body enters, so what rests on it has no record); a warm run of a
// program as large as an earlier one allocates nothing, a program's size counting the library list it keeps (MirProgram.LibraryBodies):
// the library store grows once per body that joins the list, bounded by the library's range, once per library segment (PLAN G951 U2-K).
// Must not: read syntax, HIR or plans; depend on the MIR analyses' problems (it reads A2's table only: DropTargets, Holds for its stops
// and LibraryHolds for a body's template store) or edit their driver; decide acceptance or publish its records (Compilation does); prove
// anything on an erased value; add an IntrinsicId or BindingFailure (PD21); make a limit use outside its round (MirReport.Limit); replay
// a body A2's table does not hold.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>Collects the checked set of a program version (K package 4.3).</summary>
internal sealed partial class InstanceCollector
{
    private const int Faulted = -2; // The next run's position after a run threw: it drops the round left open and starts the version (Round).
    private readonly TyCtxt types;
    private readonly MirCases cases;
    private readonly Layout layout;
    private readonly HashSet<MonoKey> refused = new(); // The keys refused for growth, each reported once (DECISIONS R5-M170).
    private readonly Dictionary<(MonoKind Kind, MirDefKey Definition), int> counts = new(); // Items per identity (X19, DECISIONS R5-M170).
    private readonly HashSet<BoundType> seen = new(); // MirTypes.MayNeedDrop's scratch.
    private readonly List<DropPart> parts = new(); // DropGlue.Parts' scratch.
    private readonly List<DropStep> steps = new(); // DropGlue.Destroyed's and DropGlue.Level's steps.
    private readonly List<MirDropStep> walk = new(); // Their scratch of Mir's steps.
    private readonly Dictionary<BoundType, MonoKey> glueKeys = new(ReferenceEqualityComparer.Instance); // GlueKey's per handle Type, per version.
    private readonly Dictionary<ComparisonNode, byte> uses = new(ReferenceEqualityComparer.Instance); // Uses' positions of each node walked.
    private int[] declaring = []; // Declaration's memo per item: the item whose body holds its subjects, plus one (0: not walked).
    private MirProgram? program; // The program of the latest run, whose bodies Declaration reads and whose report takes the records.
    private int rootModules;
    private ulong version; // The version of the latest run, whose runs Work, RunCount and Templated sum.
    private int next = -1; // The item the version's next run resumes at; -1: that run starts the version; Faulted (Round).

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

    /// <summary>Gets the work of the runs of the latest run's version (PLAN G951): the sites replayed, the items admitted and the identity
    /// items admission reads (at most the window per key), which grow linearly with the instances.</summary>
    internal int Work { get; private set; }

    /// <summary>Gets the runs of the latest run's version, each but the last stopped (Round; PLAN G337 U2): one more than the stops.</summary>
    internal int RunCount { get; private set; }

    /// <summary>Gets the bodies the runs of the latest run's version templated, each once, a body with a finding (an empty template)
    /// included: every body its store did not hold, so all of them where the library store was cleared and the bodies outside the kept
    /// library store otherwise (PLAN G950's row g8): in a version that keeps its library segment, the version's bodies and the bodies
    /// its library step newly holds, which joined the library list (PLAN G951 U2-K, row g8-T).</summary>
    internal int Templated { get; private set; }

    /// <summary>Collects the checked set of a program version unless a run already reached its end; a run that stopped (Round) leaves a
    /// set of no version (<see cref="CheckedSet.Version"/> 0), which the version's next run resumes, and no record of the round.</summary>
    /// <param name="program">The built, validated and stepped program.</param>
    /// <param name="rootModules">The root range: the bodies whose module is below it are roots. Any bound is a range: Compilation passes
    /// the source modules' count, and <see cref="Parsing.SyntaxTable.LibraryModule"/> + 1 includes the library's bodies. Every run of a
    /// version takes the same.</param>
    /// <returns>The checked set.</returns>
    internal CheckedSet Run(MirProgram program, int rootModules)
    {
        var set = this.Set;
        if (program.Version == set.Version)
        {
            return set;
        }

        if (program.Version != this.version)
        {
            (this.Work, this.RunCount, this.Templated, this.version, this.extent, this.next) = (0, 0, 0, program.Version, 0, -1);
            this.programTemplates.Clear(); // Kept across the version's runs (Round).
        }

        this.RunCount++;
        var (first, frame) = (this.next, this.types.OpenAnswer()); // R7-T1: every substitution the run causes is inside the collector's answer frame.
        this.next = Faulted; // Round: a run that throws leaves nothing to resume.
        int stop;
        bool unowned;
        try
        {
            stop = this.Collect(program, rootModules, set, first);
        }
        finally
        {
            unowned = this.types.CloseAnswer(frame);
        }

        this.next = unowned ? throw new InvalidOperationException("The collector's run took a height refusal no formation owned (InternalInvariant; R7-T1).") : stop;
        return set;
    }

    /// <summary>Gets a Type of an item's body in the item's instance (S2-S4 X2), the one instance rule of the collector's edges and layout
    /// obligations and of code generation (R5 C0b1): the Type itself for a closed item, else its instance under the representative, the
    /// substitution alone (TyCtxt.Apply; a site reads its cause, In); none for none.</summary>
    /// <param name="item">The item.</param>
    /// <param name="type">A Type of its body; none for a local of no Type (a slot Binding failed, PLAN G1073), which a body of a rejected
    /// program alone has, so code generation, which runs on an accepted one, never meets it.</param>
    /// <returns>The Type in the instance, or null where the substitution does not form it or it has no Type.</returns>
    internal BoundType? TypeOf(int item, BoundType? type)
        => type is not null && this.Set.Items[item].Representative is { } representative ? this.types.Apply(type, representative).Type : type;

    /// <summary>Gets a length of an item's body in the item's instance (SPEC 4.4.1: a Length constant's value), TypeOf's rule for a length:
    /// the length itself for a closed item, else its instance under the representative (TyCtxt.InstanceLength).</summary>
    /// <param name="item">The item.</param>
    /// <param name="length">A length of its body.</param>
    /// <returns>The length in the instance, or null where the representative does not complete it.</returns>
    internal BoundLength? LengthOf(int item, BoundLength length)
        => this.Set.Items[item].Representative is { } representative ? this.types.InstanceLength(length, representative) : length;

    /// <summary>Gets whether a substitution a site requests formed its instance, the collector's one reading of a cause (TyCtxt.Formation;
    /// R7-T1; DECISIONS R5-M151, R5-M171), the cause of TyCtxt's answer-frame query (TyCtxt.Answered): Formed makes the instance;
    /// HeightLimit (a Type past IMPL A.4's height, IMPL 21.3.2's Resource) is TypeHeightLimit_Kd at the site, a use of its forming site over
    /// a limit in the requesting item's declaration (MirReport.Limit), recorded once at its least use at the version's last run's end; Incomplete is a broken
    /// contract at the site (Report). The request is refused but for Formed.</summary>
    /// <param name="cause">The cause.</param>
    /// <param name="item">The requesting item.</param>
    /// <param name="forming">The site in its template that forms the instance (its body's syntax, unlocated), the height record's key.</param>
    /// <param name="subject">The requesting site, located (Subject).</param>
    /// <returns>Whether the cause is Formed.</returns>
    internal bool Formed(TyCtxt.Formation cause, int item, SourceRef forming, SourceRef subject)
    {
        switch (cause)
        {
            case TyCtxt.Formation.Formed:
                return true;
            case TyCtxt.Formation.HeightLimit:
                this.program!.Problems.Limit(DiagnosticCode.TypeHeightLimit_Kd, null, forming, this.Declaration(item), subject);
                return false;
            case TyCtxt.Formation.Incomplete:
                this.Report(DiagnosticCode.InternalInvariant_Kd, subject);
                return false;
            default:
                throw new InvalidOperationException($"The formation {cause} has no row.");
        }
    }

    // One run of a version (Run), inside the collector's answer frame (Round): the version's first run (first < 0) starts it; every run
    // replays from its first item and stops before a body A2's table does not hold. The item it stopped before, or -1 at the FIFO's end.
    private int Collect(MirProgram program, int rootModules, CheckedSet set, int first)
    {
        if (first < 0)
        {
            this.Start(program, rootModules, set, first == Faulted);
        }
        else if (rootModules == this.rootModules)
        {
            this.Templates(program); // The bodies built since the stopped run's walk: its lookups' and the step's.
        }
        else
        {
            throw new InvalidOperationException("A resumed collector run has another root range (InternalInvariant).");
        }

        for (var item = Math.Max(first, 0); item < set.Items.Length; item++)
        {
            if (set.Items[item].BodyOrdinal is >= 0 and var body && !program.Drops.Holds(body))
            {
                return item; // A body a lookup built since the last step: the next run resumes here, after the driver's step.
            }

            this.Replay(program, item);
        }

        program.Problems.FlushLimits(); // DECISIONS R5-M171: each use over a limit's record at its least use, after the round's records.
        program.Problems.Commit();
        set.End(program.Version);
        return -1;
    }

    // The version's first run's start (Round): the set and the admission state cleared, the round opened (after dropping the one a run
    // that threw left open), the templates, A3b and the roots in the root loop, then the imports' layouts.
    private void Start(MirProgram program, int rootModules, CheckedSet set, bool faulted)
    {
        set.Clear();
        this.counts.Clear();
        this.refused.Clear();
        this.glueKeys.Clear();
        Array.Clear(this.declaring);
        (this.program, this.rootModules) = (program, rootModules);
        if (faulted)
        {
            program.Problems.Drop();
        }

        program.Problems.Begin((int)MirRecordClass.Collection);
        this.Templates(program);
        foreach (var body in program.Bodies)
        {
            this.CheckStatic(body); // A3b, judged once per version (PLAN G950, G337 U2), in body order: a static's declaration row first.
            if (!body.HasFinding && body.Source.Module < rootModules)
            {
                this.CheckOperations(body);
                if (body.Key.Decl is null || !this.types.Instantiates(body.Key.Decl))
                {
                    var key = new MonoKey(MonoKind.Body, new(body.Key, null), null, null, 0, null);
                    set.Add(new() { Key = key, BodyOrdinal = body.Ordinal, Parent = -1, Growth = new(1, 0, -1), Site = body.Source });
                }
            }
        }

        set.EndRoots();
        this.LayOutImports();
    }

    // A Type of an item's body in the item's instance read at a site (TyCtxt.Answered, its cause read there, Formed): the Type itself for a
    // closed item; null where it does not form. SPEC 23.1.2 (rule W, PLAN G1073): a local of no Type (a slot Binding failed, MirLocal) has
    // no instance and no obligation: none, never substituted; BuildMir collects every validated body at check (SPEC 8.15.3), a rejected
    // program's included.
    private BoundType? In(int item, SourceRef subject, BoundType? type)
    {
        if (type is null || this.Set.Items[item].Representative is not { } representative)
        {
            return type;
        }

        var (instance, cause) = this.types.Answered(type, representative);
        return this.Formed(cause, item, subject, subject) ? instance : null;
    }

    // SPEC 23.1.2, DIAGNOSTICS rules 1 and 9 (DECISIONS R5-M151, amending BU-38's guard): the collector's one record path, with the record's
    // facts (a height limit's value, R7-T1). A broken contract, InternalInvariant_Kd, presupposes a complete Binding; in an incomplete
    // compilation it may rest on a part Binding rejected, which the collector cannot name, so it is that error's dependent, without a record.
    // Every other record is kept.
    private void Report(DiagnosticCode code, SourceRef subject, ReadOnlySpan<ProblemFact> facts = default)
    {
        if (code != DiagnosticCode.InternalInvariant_Kd || this.types.BindingComplete)
        {
            this.program!.Report(subject, code, MirRecordClass.Collection, facts);
        }
    }
}
