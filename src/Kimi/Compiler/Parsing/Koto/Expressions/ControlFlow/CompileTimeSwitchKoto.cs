// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the source of one compile-time directive (an #if, a #switch or an orphan #case; SPEC 19.1-19.4): its Conditions, its selection
// and its excluded parts. Input: the parts Parsing's directive selection (Parser.Directives) hands it while the directive is parsed.
// Output: one source slot (ChildSlots.Source) holding them in parse order; an excluded member's parts move in after the member from
// its list's parse-time host (DeclarationContainerKoto.ExcludedOwner), which never enters the tree.
// Invariants: a decided directive's marker stands in its list owner's source slot, or in the source slot of the directive whose
// excluded syntax it is; a #switch outside excluded syntax that selected no arm is the list item itself, the recovery of the Error
// that kept it from selecting (DIAGNOSTICS.md §4.3); the marker's parts are reached only through KotoVisitor.VisitSource.
// Must not register anything or be read by a semantic phase, except as that failed-selection item (SPEC 19.4).

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>The marker of one compile-time directive, which holds its source: Conditions and excluded parts (SPEC 19.4). A
/// <c>#switch</c> that selected no arm is also the list item on which later analyses explain no cascade (SPEC 19.3).</summary>
public sealed class CompileTimeSwitchKoto : ExpressionKoto
{
    private List<Koto>? source;

    /// <summary>Initializes a new instance of the <see cref="CompileTimeSwitchKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="range">The directive's span; a Case Group's is set when it ends.</param>
    public CompileTimeSwitchKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.CompileTimeSwitch;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        // Unparsing reproduces the original Case Group, whose arms exist only as source text.
        var source = this.CodeContext.SourceDocument?.SourceText;
        var span = this.Span;
        if (source is null || span.Length == 0 || span.End > source.Length)
        {
            builder.Append(Parser.SwitchDirectiveText);
            return;
        }

        var lineStart = span.Start == 0 ? 0 : source.LastIndexOf('\n', span.Start - 1) + 1;
        var baseline = span.Start - lineStart;
        var text = source.AsSpan(span.Start, span.Length).TrimEnd();
        var first = true;
        foreach (var range in text.Split('\n'))
        {
            var line = text[range].TrimEnd('\r');
            if (!first)
            {
                builder.AppendLine();
                var indent = line.Length - line.TrimStart(' ').Length;
                line = line[Math.Min(baseline, indent)..];
            }

            builder.Append(line);
            first = false;
        }
    }

    /// <summary>Adds a Condition or an excluded part to the source slot, in parse order.</summary>
    /// <param name="part">The part.</param>
    internal void AddSource(Koto part)
    {
        (this.source ??= []).Add(part);
        part.Parent = this;
    }

    /// <summary>Moves parts to the source slot in their order, emptying their list.</summary>
    /// <typeparam name="T">The part type.</typeparam>
    /// <param name="parts">The parts, or <see langword="null"/>.</param>
    internal void MoveToSource<T>(List<T>? parts)
        where T : Koto
    {
        if (parts is null)
        {
            return;
        }

        foreach (var part in parts)
        {
            this.AddSource(part);
        }

        parts.Clear();
    }

    protected override void ForEachChildSlot(ref ChildSlots slots)
        => slots.Source(this.source);
}
