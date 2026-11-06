// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns Parsing's syntax nesting limit L (IMPL A.4, 21.3.2; DECISIONS BU-112): the depth of every tree a parse publishes.
// Input: a module's tree right after one document's parse, and that document. Output: the same tree, in which a node at depth L (its
// distance from the module root) that has a child is cut: replaced in its slot by an ErrorKoto, or, where its slot holds only its own
// form (a body, a name, an attribute), its parent is cut instead; one NestingLimit_Kd record per cut, at the cut's span, which the
// ErrorKoto rests on.
// Invariants: a published tree has depth at most L, so every later walk recurses at most L levels and needs no guard of its own;
// nothing under a cut is walked or recorded; the walk itself recurses at most L + 1 levels; nodes of earlier documents are walked
// only at the module's merge points (declaration containers, the generated function and its body). One exception: declaration
// containers nested past L keep their depth (no slot above them takes an ErrorKoto) and are recorded where they stand. The library's
// trees (KimiLibrarySources) are checked by KimiLibraryShapeTest, not walked at run time: compiler-owned source fixed at build time.
// Must not decide anything but depth, and must not be bypassed by another parse entry that publishes syntax to later phases.

using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Bounds the syntax nesting of a module after a parse: the one walk of IMPL A.4's syntax nesting limit.
/// </summary>
internal sealed class SyntaxNesting : KotoVisitor
{
    /// <summary>The syntax nesting limit L (IMPL A.4): no node of a published tree lies deeper than L below its module root.</summary>
    internal const int Limit = 1024;

    private readonly SourceDocument? document;
    private readonly FunctionKoto? generated;
    private List<Waiting>? waiting;
    private List<Koto>? cuts;
    private int depth;
    private bool deeper;

    private SyntaxNesting(SourceDocument? document, FunctionKoto? generated)
    {
        this.document = document;
        this.generated = generated;
    }

    /// <inheritdoc/>
    public override void Visit(Koto node)
    {
        if (!this.InScope(node))
        {
            return;
        }

        if (this.depth > Limit)
        {
            this.deeper = true; // A child of a node at depth L: that node is cut, and nothing under it is walked.
            return;
        }

        var made = this.cuts?.Count ?? 0;
        var first = this.waiting?.Count ?? 0;
        this.depth++;
        node.VisitChildren(this);
        this.depth--;
        if (this.deeper)
        {
            this.deeper = false;
            (this.waiting ??= []).Add(new(node, made, made));
        }
        else if (this.waiting is { } waiting && waiting.Count > first)
        {
            this.Cut(node, waiting, first, made);
        }
    }

    /// <summary>Cuts the nodes of one parse that lie deeper than <see cref="Limit"/> and records each cut.</summary>
    /// <param name="parse">The context of the parse, whose document's nodes are bounded.</param>
    internal static void Bound(CodeContext parse)
    {
        var walk = new SyntaxNesting(parse.SourceDocument, parse.Kotonoha.GeneratedFunction);
        walk.Visit(parse.Kotonoha.RootKoto);
        if (walk.cuts is not { } cuts)
        {
            return;
        }

        foreach (var cut in cuts)
        {
            cut.AddDiagnostic(DiagnosticCode.NestingLimit_Kd, Limit);
            if (cut is ErrorKoto error)
            {
                error.Cause = cut.DiagnosticCollection?.LastError;
            }
        }
    }

    // Replaces each child of node that waits for its cut, the last first so the earlier ranges stay put; a cut drops the cuts made
    // under it. A slot that refuses an ErrorKoto makes node wait for its own parent's cut instead; at the root the child is recorded
    // where it stands.
    private void Cut(Koto node, List<Waiting> waiting, int first, int made)
    {
        var cuts = this.cuts ??= [];
        for (var i = waiting.Count - 1; i >= first; i--)
        {
            var (child, from, to) = waiting[i];
            var error = new ErrorKoto(child.CodeContext, child.Span);
            if (node.ReplaceChild(child, error))
            {
                cuts.RemoveRange(from, to - from);
                cuts.Insert(from, error);
            }
            else if (node.Parent is null)
            {
                cuts.Insert(from, child);
            }
            else
            {
                waiting.RemoveRange(first, waiting.Count - first);
                waiting.Add(new(node, made, cuts.Count));
                return;
            }
        }

        waiting.RemoveRange(first, waiting.Count - first);
    }

    // A node of this parse, or a merge point of the module, which may hold one.
    private bool InScope(Koto node)
        => ReferenceEquals(node.CodeContext.SourceDocument, this.document) || node is DeclarationContainerKoto ||
            (this.generated is { } generated && (ReferenceEquals(node, generated) || ReferenceEquals(node, generated.Body)));

    // A node waiting for its parent's cut; the cuts made under it are cuts[From..To).
    private readonly record struct Waiting(Koto Node, int From, int To);
}
