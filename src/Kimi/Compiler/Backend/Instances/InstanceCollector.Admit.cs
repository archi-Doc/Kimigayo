// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Admission (K package 4.6, X19) and the location rule (K 4.9).
internal sealed partial class InstanceCollector
{
    private const int DepthLimit = 128; // Every item's depth on its admission chain.
    private const int GrowthLimit = 16; // Ancestors of one definition with other keys: a growing key.
    private const int SetLimit = 1024; // Items of one definition.

    // The item of a callee body requested at a site of an item's body, admitted once per key (KD1): keyed by its definition and, when it
    // instantiates, its erased substitution, represented by the substitution itself (KD3). A new key whose instance's Types TyCtxt cannot
    // make is the storage-nesting gap, one past a limit GenerationResourceLimit_Kd; the edge is dropped either way. -1 when refused.
    private int Admit(int parent, MirBody body, in SourceInfo source, MirBody target, Subst? subst)
    {
        var set = this.Set;
        Subst? erased = null;
        if (target.Key.Decl is { } decl && this.types.Instantiates(decl) && (subst is null || (erased = this.types.Erase(subst)) is null))
        {
            set.Report(DiagnosticCode.InternalInvariant_Kd, this.Subject(parent, body, source)); // An instance without its substitution.
            return -1;
        }

        var key = new MonoKey(MonoKind.Body, new(target.Key, erased));
        if (set.Find(key) is >= 0 and var found)
        {
            return found;
        }

        var site = this.Subject(parent, body, source);
        var representative = erased is null ? null : subst;
        if (representative is not null && !this.Instantiable(target, representative))
        {
            set.Report(Capability.Code(CapabilityGap.StorageNesting), site);
            return -1;
        }

        var depth = set.Items[parent].Depth + 1;
        this.counts.TryGetValue(target.Key, out var count);
        if (depth > DepthLimit || count >= SetLimit || this.Grows(parent, target.Key))
        {
            set.Report(DiagnosticCode.GenerationResourceLimit_Kd, site);
            return -1;
        }

        this.counts[target.Key] = count + 1;
        this.Work++;
        var view = this.View(target, representative, site);
        return set.Add(new()
        {
            Key = key, Representative = representative, BodyOrdinal = target.Ordinal, View = view, Parent = parent, Depth = (ushort)depth, Site = site,
        });
    }

    // Whether TyCtxt makes every local Type of a body in an instance, so that each of the item's Types exists (PD30: else PLAN G128).
    private bool Instantiable(MirBody body, Subst representative)
    {
        foreach (var local in body.Locals)
        {
            if (this.types.Apply(local.Type, representative) is null)
            {
                return false;
            }
        }

        return true;
    }

    // Whether the admission chain from an item already holds GrowthLimit items of a definition, all with other keys than a new one's.
    private bool Grows(int item, MirDefKey definition)
    {
        var count = 0;
        for (var current = item; current >= 0; current = this.Set.Items[current].Parent)
        {
            count += this.Set.Items[current].Key.Body.Def == definition ? 1 : 0;
            if (count >= GrowthLimit)
            {
                return true;
            }
        }

        return false;
    }

    // K 4.3: the one view of a body its representative selects (a closed item's body has one); none or several is a broken contract.
    private byte View(MirBody body, Subst? representative, SourceRef site)
    {
        var views = this.cases.Views(body, representative);
        if (BitOperations.PopCount(views) != 1)
        {
            this.Set.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return 0;
        }

        return (byte)BitOperations.TrailingZeroCount(views);
    }

    // K 4.9: the subject of a record caused at a site of an item's body: the site, moved out of the inline defaults of templates outside
    // the root modules to the outermost omitting call, if that lies in a root module; else the root-module site the item's admission chain
    // first reached, which each item keeps.
    private SourceRef Subject(int item, MirBody body, in SourceInfo source)
    {
        var subject = body.Subject(source);
        for (int scope = source.Scope; scope > 0; scope = body.Scopes[scope].Outer)
        {
            if (body.Scopes[scope].Kind == MirScopeKind.Inlined && subject.Module >= this.rootModules)
            {
                subject = body.Scopes[scope].Source;
            }
        }

        return subject.Module < this.rootModules ? subject : this.Set.Items[item].Site;
    }
}
