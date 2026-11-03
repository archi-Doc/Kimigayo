// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns Core's one publisher of MIR records (R6-check H3-2; DECISIONS R5-M184): it decides how a MIR record is published, never whether it
// exists, whether it is dependent or its requirement (the recording rule's, R5-M23); and the one location form, Locate, which code
// generation's records take too (DECISIONS R5-M65).
// Input: the program of the latest Analyze, its construction records (MirProgram.Records with MirRecordClass.Construction, R6-check H3-1)
// and validator findings, and the program the latest collection recorded into, its collection records (MirRecordClass.Collection), the
// library's as well as the source modules'.
// Output: adapter calls only (DiagnosticCollection.ReportLocation, in the subject's collection); no table of its own. MirRecords is the one
// selection of the records, which Analysis, Problems, the generation gate and PublishMirRecords read.
// Invariants: until H5, which publishes every merged problem and removes the selection, the publisher publishes today's rows only, on every
// pipeline (DECISIONS R5-M156): the construction records, the findings as InternalInvariant_Kd, then the collection records, each located
// in the ControlFlow requirement. A record is a code without facts, so its location is its subject (DIAGNOSTICS 4.1, DECISIONS R4-M71): the
// records of two nodes at one location, such as the siblings a for expansion synthesizes with its span, are one problem. The MIR analyses'
// problems stay unpublished until H5 (R4-M1).
// Must not: be steered by a pipeline mode, flag or optional parameter, or publish a MIR record any other way.

using Kimi.Diagnostics;

namespace Kimi.Compiler;

public partial class Compilation
{
    // The MIR's records of the latest Analyze, each located in the ControlFlow requirement, in MirRecords' order (today's rows, until H5).
    private void PublishMirRecords()
    {
        this.problems.Clear();
        this.MirRecords(this.problems);
        foreach (var problem in this.problems)
        {
            this.Locate(DiagnosticRequirement.ControlFlow, problem.Code, problem.Subject);
        }
    }

    // A code without facts keyed by its subject's location, in the subject's collection (DIAGNOSTICS 4.1; DECISIONS R5-M65, R4-M71).
    private void Locate(DiagnosticRequirement requirement, DiagnosticCode code, SourceRef subject)
    {
        var node = this.SyntaxOf(subject);
        node.DiagnosticCollection?.ReportLocation(requirement, node.Span, code, node.CodeContext.SourceDocument);
    }

    // The MIR records of the latest Analyze, added to the list when one is given: the construction records and validator findings, the
    // library's as well as the source modules', and every record of the checked set (SPEC 23.2.2: check reports every error of the checked
    // set, its layout obligations among them; DECISIONS R5-M156), on every pipeline.
    private int MirRecords(List<Problem>? into)
    {
        var count = 0;
        if (this.mir is { } program)
        {
            count = Add(program.Records(Mir.MirRecordClass.Construction), into);
            count += program.Findings.Count;
            foreach (var finding in program.Findings)
            {
                into?.Add(new(DiagnosticCode.InternalInvariant_Kd, finding.Subject));
            }
        }

        return this.collected is { } collected ? count + Add(collected.Records(Mir.MirRecordClass.Collection), into) : count;

        static int Add(Mir.MirProgram.RecordRows rows, List<Problem>? into)
        {
            var count = 0;
            foreach (var row in rows)
            {
                count++;
                into?.Add(row);
            }

            return count;
        }
    }
}
