// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace XunitTest;

// The ledger's compared set (DECISIONS R4-M4, R4-M17, R4-M67): the old records each analysis owns and the requirement its MIR problems
// name, the one split of a compilation's records by it (Sides, R4-M45), and the matching of the two sides on code and subject (Match).
public sealed partial class MirDifferenceLedger
{
    // R4-M4: the compared old records, by requirement and each code it reports (OwnershipIssue.Code, Binding's code of the failure), with
    // the analysis that owns them and the requirement a MIR problem of that code names (R4-M67; none for the old expansion limit). Every
    // other old record passes through: TransferRequired and Internal, control flow's other codes and Binding's other failures, CaseLimit
    // included. Binding's IncompatibleImplementation and Unsupported records are compared only where an effect check made them
    // (Binding.IsEffectRecord, R4-M23), and the MIR names the Ownership requirement of each decision the old path reports under Binding
    // (DIAGNOSTICS rule 1; renamed at H5). The MIR pairs are the consistency set of PLAN10 §7: a MIR problem outside it is a recording
    // rule's defect, ledgered as a contract break (B), as is MirReport.Add's disagreement record (Ownership.Internal).
    internal static readonly (DiagnosticRequirement Requirement, DiagnosticCode Code, string Analysis, DiagnosticRequirement? Mir)[] Compared =
    [
        Same(Ownership(OwnershipRequirement.UninitializedUse), DiagnosticCode.UninitializedPlace_Kd, "A1"),
        Same(Ownership(OwnershipRequirement.PossiblyMovedUse), DiagnosticCode.MovedPlace_Kd, "A1"),
        Same(Ownership(OwnershipRequirement.ReassignedLet), DiagnosticCode.ReassignedLet_Kd, "A1"),
        Same(Ownership(OwnershipRequirement.StaticMovePathRequired), DiagnosticCode.StaticMovePathRequired_Kd, "A1"),
        Same(Ownership(OwnershipRequirement.DefaultArgumentMove), DiagnosticCode.DefaultArgumentMove_Kd, "A1"),
        Same(DiagnosticRequirement.ControlFlow, DiagnosticCode.RequireFallthrough_Kd, "A3"),
        Same(Ownership(OwnershipRequirement.EffectBound), DiagnosticCode.IncompatibleContractImplementation_Kd, "A4"),
        Same(Ownership(OwnershipRequirement.CallableEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        Same(Ownership(OwnershipRequirement.VirtualEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (DiagnosticRequirement.Binding(BindingFailure.VirtualEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4",
            Ownership(OwnershipRequirement.VirtualEffectBound)),
        (DiagnosticRequirement.Binding(BindingFailure.IncompatibleImplementation), DiagnosticCode.IncompatibleContractImplementation_Kd, "A4",
            Ownership(OwnershipRequirement.EffectBound)),
        (DiagnosticRequirement.Binding(BindingFailure.Unsupported), DiagnosticCode.Unsupported_Kd, "U", Ownership(OwnershipRequirement.Unsupported)),
        Same(Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.ComparisonLoanConflict_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.CallReservationConflict_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.CallActivationConflict_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.PlacementActivationConflict_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.DefaultArgumentAccess), DiagnosticCode.DefaultArgumentAccess_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.DefaultArgumentBorrow), DiagnosticCode.DefaultArgumentBorrow_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnprovenOriginRelation_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnprovenConstraint_Kd, "A5"),
        (DiagnosticRequirement.Binding(BindingFailure.OriginRelation), DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5",
            Ownership(OwnershipRequirement.UnprovenOrigin)),
        (DiagnosticRequirement.Binding(BindingFailure.OriginRelation), DiagnosticCode.UnprovenOriginRelation_Kd, "A5",
            Ownership(OwnershipRequirement.UnprovenOrigin)),
        Same(Ownership(OwnershipRequirement.StorageLimit), DiagnosticCode.OwnershipStorageLimit_Kd, "A5"),
        Same(Ownership(OwnershipRequirement.Unsupported), DiagnosticCode.Unsupported_Kd, "U"),
        (Ownership(OwnershipRequirement.ExpansionLimit), DiagnosticCode.DeferredExpansionLimit_Kd, "-", null),
    ];

    /// <summary>Gets the analysis that owns an old record of the compared set (R4-M4), or null for a record that passes through.</summary>
    /// <param name="requirement">The record's requirement.</param>
    /// <param name="code">The record's code.</param>
    /// <returns>The analysis.</returns>
    internal static string? AnalysisOf(DiagnosticRequirement requirement, DiagnosticCode code)
        => Array.Find(Compared, x => x.Requirement == requirement && x.Code == code).Analysis;

    /// <summary>Gets the analysis that owns a MIR problem: its pair's in the compared set's MIR column (R4-M67), where each code names one
    /// analysis; null outside it, a recording rule's defect.</summary>
    /// <param name="requirement">The requirement the problem names.</param>
    /// <param name="code">The problem's code.</param>
    /// <returns>The analysis, or null.</returns>
    internal static string? MirAnalysisOf(DiagnosticRequirement requirement, DiagnosticCode code)
        => Array.Find(Compared, x => x.Mir == requirement && x.Code == code).Analysis;

    /// <summary>Matches the records of both sides as multisets on code and subject node, by reference: each MIR entry takes the first
    /// unmatched old entry of its code and node. Old entries come first in their order, on side both (with the old entry's facts, its
    /// requirement written <c>old&gt;mir</c> when the MIR entry names another; a MIR entry that names none, -, agrees) or old, then the
    /// unmatched MIR entries on side new.</summary>
    /// <param name="old">The old path's A-owned records.</param>
    /// <param name="mir">The MIR's records and problems.</param>
    /// <returns>The sides and entries.</returns>
    internal static List<(string Side, Entry Entry)> Match(List<Entry> old, List<Entry> mir)
    {
        var matched = new bool[old.Count];
        var requirements = old.Select(static x => x.Requirement).ToArray();
        var unmatched = new List<Entry>();
        foreach (var entry in mir)
        {
            var index = 0;
            while (index < old.Count && (matched[index] || old[index].Code != entry.Code || !ReferenceEquals(old[index].Node, entry.Node)))
            {
                index++;
            }

            if (index < old.Count)
            {
                matched[index] = true;
                var agrees = entry.Requirement is "-" || entry.Requirement == old[index].Requirement;
                requirements[index] = agrees ? old[index].Requirement : $"{old[index].Requirement}>{entry.Requirement}";
            }
            else
            {
                unmatched.Add(entry);
            }
        }

        return [.. old.Select((x, i) => (matched[i] ? "both" : "old", x with { Requirement = requirements[i] })), .. unmatched.Select(static x => ("new", x))];
    }

    /// <summary>Lists an analyzed compilation's records by side: Binding's records outside the compared set (R4-M4), which both paths
    /// publish; the old path's A-owned records, which the ledger compares; the old analyses' other records, which pass through; and the
    /// MIR's construction records, validator findings (as <c>InternalInvariant_Kd</c>) and problems, one entry each (MirReport.Add keeps
    /// one per code and subject, R4-M67) with its requirement and its pair's analysis in the compared set, or B for a pair outside it, a
    /// recording rule's defect such as MirReport's disagreement record.</summary>
    /// <param name="compilation">The compilation after <see cref="Compilation.Analyze"/>.</param>
    /// <param name="program">Its MIR (<see cref="Compilation.BuildMir"/>).</param>
    /// <returns>The common, old, passed and MIR records.</returns>
    internal static (List<Entry> Common, List<Entry> Old, List<Entry> Passed, List<Entry> Mir) Sides(Compilation compilation, MirProgram program)
    {
        var (common, old, passed) = (new List<Entry>(), new List<Entry>(), new List<Entry>());
        foreach (var (requirement, code, node) in LegacyRecords.Of(compilation))
        {
            var effectOnly = requirement == DiagnosticRequirement.Binding(BindingFailure.IncompatibleImplementation) ||
                requirement == DiagnosticRequirement.Binding(BindingFailure.Unsupported);
            var analysis = AnalysisOf(requirement, code) is { } owner && (!effectOnly || compilation.Binding.IsEffectRecord(node)) ? owner : null;
            (analysis is not null ? old : requirement.Partition == DiagnosticPartition.Binding ? common : passed).Add(new(code, requirement.Name, analysis ?? "-", node));
        }

        var mir = program.Records.Select(x => new Entry(x.Code, "-", "B", compilation.SyntaxOf(x.Subject)))
            .Concat(program.Findings.Select(x => new Entry(DiagnosticCode.InternalInvariant_Kd, "-", "B", compilation.SyntaxOf(x.Subject)))).ToList();
        foreach (var problem in program.Problems.All)
        {
            var analysis = MirAnalysisOf(problem.Requirement, problem.Code) ?? "B";
            mir.Add(new(problem.Code, problem.Requirement.Name, analysis, compilation.SyntaxOf(problem.Subject)));
        }

        return (common, old, passed, mir);
    }

    private static DiagnosticRequirement Ownership(OwnershipRequirement requirement) => DiagnosticRequirement.Ownership(requirement);

    // A compared pair whose MIR problem names the old record's requirement.
    private static (DiagnosticRequirement Requirement, DiagnosticCode Code, string Analysis, DiagnosticRequirement? Mir) Same(
        DiagnosticRequirement requirement, DiagnosticCode code, string analysis) => (requirement, code, analysis, requirement);

    /// <summary>A record of either side: its code, its requirement's name (- for a MIR construction record or validator finding), the
    /// analysis owning it and its subject.</summary>
    /// <param name="Code">The code.</param>
    /// <param name="Requirement">The requirement's name.</param>
    /// <param name="Analysis">The analysis.</param>
    /// <param name="Node">The subject.</param>
    internal readonly record struct Entry(DiagnosticCode Code, string Requirement, string Analysis, Koto Node);
}
