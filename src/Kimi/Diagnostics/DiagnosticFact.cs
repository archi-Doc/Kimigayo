// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The fact and its keys are one unit.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Diagnostics;

/// <summary>
/// The identity of one check (docs/dev/DIAGNOSTICS.md §4.1): its subject, requirement, condition and context. A syntax node is
/// compared by reference; any other subject is its location. The location also orders subjects (SPEC 23.3.6.6).
/// </summary>
internal readonly struct DiagnosticKey : IEquatable<DiagnosticKey>
{
    private static readonly object UnresolvedSubject = new();

    /// <summary>Initializes a new instance of the <see cref="DiagnosticKey"/> struct.</summary>
    /// <param name="subject">The subject node, or <see langword="null"/> when the location is the subject.</param>
    /// <param name="source">The source table index of the subject, or -1.</param>
    /// <param name="start">The subject's span start.</param>
    /// <param name="length">The subject's span length, or -1 for a whole input.</param>
    /// <param name="requirement">The requirement.</param>
    /// <param name="condition">The condition within the requirement.</param>
    /// <param name="context">The context: an instantiation, or the observed failure of an input; <see langword="null"/> for none.</param>
    public DiagnosticKey(object? subject, int source, int start, int length, DiagnosticRequirement requirement, ushort condition = 0, object? context = null)
    {
        this.Subject = subject;
        this.Source = source;
        this.Start = start;
        this.Length = length;
        this.Requirement = requirement;
        this.Condition = condition;
        this.Context = context;
    }

    /// <summary>Gets the mark of a prerequisite whose check is not known; it never resolves.</summary>
    public static DiagnosticKey Unresolved { get; } = new(UnresolvedSubject, -1, 0, -1, default);

    public object? Subject { get; }

    public int Source { get; }

    public int Start { get; }

    public int Length { get; }

    public DiagnosticRequirement Requirement { get; }

    public ushort Condition { get; }

    public object? Context { get; }

    public bool IsUnresolved => ReferenceEquals(this.Subject, UnresolvedSubject);

    public bool Equals(DiagnosticKey other)
        => ReferenceEquals(this.Subject, other.Subject) && (this.Subject is not null || (this.Source == other.Source && this.Start == other.Start && this.Length == other.Length)) &&
            this.Requirement == other.Requirement && this.Condition == other.Condition && ContextEquals(this.Context, other.Context);

    public override bool Equals(object? obj)
        => obj is DiagnosticKey other && this.Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(this.Subject is null ? HashCode.Combine(this.Source, this.Start, this.Length) : RuntimeHelpers.GetHashCode(this.Subject), this.Requirement, this.Condition, this.Context is string text ? text.GetHashCode(StringComparison.Ordinal) : this.Context is null ? 0 : RuntimeHelpers.GetHashCode(this.Context));

    /// <summary>Compares two subjects at one location by structure (SPEC 23.3.6.6): the enclosing node first, then the node kind.</summary>
    /// <param name="other">The other key.</param>
    /// <returns>The order; zero when the subjects cannot be told apart by structure.</returns>
    public int CompareSubject(in DiagnosticKey other)
    {
        var order = Depth(this.Subject).CompareTo(Depth(other.Subject));
        return order != 0 ? order : ((this.Subject as Koto)?.Akind ?? 0).CompareTo((other.Subject as Koto)?.Akind ?? 0);
    }

    /// <summary>Compares the requirement, condition and context of two keys; the context of an input failure is its observed failure.</summary>
    /// <param name="other">The other key.</param>
    /// <returns>The order.</returns>
    public int CompareCheck(in DiagnosticKey other)
    {
        var order = this.Requirement.Partition.CompareTo(other.Requirement.Partition);
        if (order == 0)
        {
            order = this.Requirement.Kind.CompareTo(other.Requirement.Kind);
        }

        if (order == 0)
        {
            order = this.Condition.CompareTo(other.Condition);
        }

        return order == 0 && this.Context is string left && other.Context is string right ? string.CompareOrdinal(left, right) : order;
    }

    private static bool ContextEquals(object? left, object? right)
        => ReferenceEquals(left, right) || (left is string x && right is string y && string.Equals(x, y, StringComparison.Ordinal));

    private static int Depth(object? subject)
    {
        var depth = 0;
        for (var node = (subject as Koto)?.Parent; node is not null; node = node.Parent)
        {
            depth++;
        }

        return depth;
    }
}

/// <summary>One recorded fact (docs/dev/DIAGNOSTICS.md §4.1).</summary>
/// <param name="Code">The code; <c>PrerequisiteUnavailable_Kd</c> exactly when <paramref name="DerivedFrom"/> is not empty.</param>
/// <param name="Key">The check key; with the code it identifies the problem.</param>
/// <param name="Source">The source table index of the primary location, or -1.</param>
/// <param name="Start">The primary span start.</param>
/// <param name="Length">The primary span length, or -1 without a span.</param>
/// <param name="First">The first message argument, captured as a value.</param>
/// <param name="Second">The second message argument, captured as a value.</param>
/// <param name="Note">A Note formed from the facts.</param>
/// <param name="Advice">Conditional advice formed from the facts.</param>
/// <param name="DerivedFrom">The check keys of the unmet prerequisites.</param>
/// <param name="Unit">The former collection, for the transitional start-offset filter.</param>
/// <param name="Legacy">Whether the fact comes from a recorder not yet migrated to problem identities (D2b).</param>
internal readonly record struct DiagnosticFact(
    DiagnosticCode Code, DiagnosticKey Key, int Source, int Start, int Length, object? First, object? Second, string? Note, string? Advice, DiagnosticKey[]? DerivedFrom, int Unit, bool Legacy);
