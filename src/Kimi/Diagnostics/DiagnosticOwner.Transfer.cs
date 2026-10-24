// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Diagnostics;

public sealed partial class DiagnosticOwner
{
    // One attachment's recovery evidence. Index the donor once, select only requested syntax causes and their closure,
    // then register their sources in donor order before remapping facts. Never import unrelated errors or semantic results.
    internal sealed class SyntaxTransfer
    {
        private readonly DiagnosticOwner source;
        private readonly DiagnosticOwner destination;
        private readonly int module;
        private readonly Dictionary<DiagnosticKey, List<DiagnosticFact>> facts = [];
        private readonly HashSet<DiagnosticKey> selected = [];
        private readonly SortedSet<int> sources = [];
        private readonly Dictionary<int, int> sourceMap = [];

        internal SyntaxTransfer(DiagnosticOwner source, DiagnosticCollection destination)
        {
            this.source = source;
            this.destination = destination.Owner;
            this.module = destination.CurrentModule();
            foreach (var partition in source.syntax)
            {
                foreach (var fact in partition.Facts)
                {
                    if (!this.facts.TryGetValue(fact.Key, out var values))
                    {
                        values = [];
                        this.facts.Add(fact.Key, values);
                    }

                    values.Add(fact);
                }
            }
        }

        internal void Include(DiagnosticKey key)
        {
            if (key.IsUnresolved || !this.selected.Add(key))
            {
                return;
            }

            if (!this.facts.TryGetValue(key, out var values))
            {
                throw new DiagnosticContractException(DiagnosticFault.Collection, "A moved recovery has no recorded syntax cause.");
            }

            this.sources.Add(key.Source);
            foreach (var fact in values)
            {
                this.sources.Add(fact.Source);
                foreach (var related in fact.Related ?? [])
                {
                    this.sources.Add(related.Source);
                }

                foreach (var repair in fact.Repairs ?? [])
                {
                    foreach (var edit in repair.Edits)
                    {
                        this.sources.Add(edit.Source);
                    }
                }

                foreach (var prerequisite in fact.DerivedFrom ?? [])
                {
                    this.Include(prerequisite);
                }
            }
        }

        internal void Copy()
        {
            foreach (var index in this.sources)
            {
                if (index < 0)
                {
                    continue;
                }

                var entry = this.source.sources[index];
                var mapped = entry.Document is { } document
                    ? this.destination.DocumentSource(document, this.module, entry.IsInput)
                    : this.destination.PathSource(entry.Path);
                this.sourceMap.Add(index, mapped);
            }

            foreach (var key in this.selected)
            {
                foreach (var fact in this.facts[key])
                {
                    if (!DiagnosticEntries.TryGet(fact.Code, out var entry))
                    {
                        throw new DiagnosticContractException(DiagnosticFault.UnknownCode, fact.Code.ToString());
                    }

                    this.destination.Record(DiagnosticPartition.Syntax, this.module, this.Fact(fact, entry), entry.Severity == DiagnosticSeverity.Error);
                }
            }
        }

        internal DiagnosticKey Key(DiagnosticKey key)
            => key.IsUnresolved ? key : new(key.Subject, this.Source(key.Source), key.Start, key.Length, key.Requirement, key.Condition, key.Context);

        private int Source(int index) => index < 0 ? index : this.sourceMap[index];

        private DiagnosticFact Fact(DiagnosticFact fact, DiagnosticEntry entry)
            => fact with
            {
                Key = this.Key(fact.Key),
                Source = this.Source(fact.Source),
                DerivedFrom = fact.DerivedFrom?.Select(this.Key).ToArray(),
                Related = fact.Related is { } related ? OrderRelated(related.Select(x => x with { Source = this.Source(x.Source) }).ToArray()) : null,
                Repairs = fact.Repairs is { Length: > 0 } repairs
                    ? DiagnosticCollection.ValidateRepairs(entry, repairs.Select(this.Repair).ToArray(), fact.DerivedFrom is { Length: > 0 }) : null,
            };

        private DiagnosticRepairFact Repair(DiagnosticRepairFact repair)
            => repair with { Edits = repair.Edits.Select(x => x with { Source = this.Source(x.Source) }).ToArray() };
    }
}
