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

    // Whether an instance's Types exist and are closed.
    private enum Instance : byte
    {
        Closed,
        Missing,
        Open,
    }

    // SPEC 6.1.3: whether a substitution carries the instance of the function enclosing its binder's definition, as its binder (a Closure's
    // context, R5-M36) or its declaring Type (Binding writes one level into a generic function).
    private static bool Carries(Subst subst, BindingSymbol enclosing)
        => ReferenceEquals(subst.Target, enclosing) || ReferenceEquals(subst.Declaring?.Symbol, enclosing);

    // The item of a callee body requested at a site of an item's body (its located subject), admitted once per key (KD1): keyed by its
    // definition and, when it instantiates, its erased substitution, represented by the substitution itself (KD3). A function nested in a
    // body whose substitution does not carry the enclosing instance, or a new key whose instance's Types TyCtxt cannot make, is a capability
    // gap; one past a limit is GenerationResourceLimit_Kd; a representative that leaves its body open is a broken contract. -1 when refused.
    private int Admit(int parent, SourceRef site, MirBody target, Subst? subst)
    {
        var set = this.Set;
        Subst? erased = null;
        if (target.Key.Decl is { } decl && this.types.Instantiates(decl) && (subst is null || (erased = this.types.Erase(subst)) is null))
        {
            set.Report(DiagnosticCode.InternalInvariant_Kd, site); // An instance without its substitution.
            return -1;
        }

        // SPEC 6.1.3, 8.13.3: an instance of a definition nested in an instantiating function holds that function's instance; a substitution
        // that does not carry it is the EnclosingInstance gap until a representation of the chain (DECISIONS R5-M43). A closed enclosing function has no
        // instance to carry.
        if (erased is not null && this.types.EnclosingFunction(target.Key.Decl!) is { } enclosing && this.types.Instantiates(enclosing) &&
            !Carries(subst!, enclosing))
        {
            set.Report(Capability.Code(CapabilityGap.EnclosingInstance), site);
            return -1;
        }

        var key = new MonoKey(MonoKind.Body, new(target.Key, erased));
        if (set.Find(key) is >= 0 and var found)
        {
            return found;
        }

        var representative = erased is null ? null : subst;
        if (representative is not null && this.Instantiable(target, representative) is var instance && instance != Instance.Closed)
        {
            set.Report(instance == Instance.Missing ? Capability.Code(CapabilityGap.StorageNesting) : DiagnosticCode.InternalInvariant_Kd, site);
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

    // K 4.3: the item of a type-driven key requested at a site, admitted once per erased Type and represented by the first unerased Type
    // that requests it, which is closed (an open one is a broken contract, InternalInvariant_Kd); past the depth limit
    // GenerationResourceLimit_Kd. -1 when refused.
    private int AdmitType(int parent, SourceRef site, MonoKind kind, BoundType type)
    {
        var set = this.Set;
        if (type.ContainsParameter)
        {
            set.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return -1;
        }

        var key = new MonoKey(kind, default, this.types.Erase(type));
        if (set.Find(key) is >= 0 and var found)
        {
            return found;
        }

        var depth = set.Items[parent].Depth + 1;
        if (depth > DepthLimit)
        {
            set.Report(DiagnosticCode.GenerationResourceLimit_Kd, site);
            return -1;
        }

        this.Work++;
        return set.Add(new() { Key = key, RepresentativeType = type, BodyOrdinal = -1, Parent = parent, Depth = (ushort)depth, Site = site });
    }

    // Whether a representative is closed and TyCtxt makes every local Type of its body in its instance, closed: Missing, a Type TyCtxt cannot
    // make (PD30: PLAN G128), Open, a Type left open, which K 4.3's closed representatives exclude.
    private Instance Instantiable(MirBody body, Subst representative)
    {
        if (Open(representative.Self) || Open(representative.Declaring))
        {
            return Instance.Open;
        }

        foreach (var type in representative.Types)
        {
            if (Open(type))
            {
                return Instance.Open;
            }
        }

        foreach (var local in body.Locals)
        {
            if (this.types.Apply(local.Type, representative) is not { } instance)
            {
                return Instance.Missing;
            }

            if (instance.ContainsParameter)
            {
                return Instance.Open;
            }
        }

        return Instance.Closed;

        static bool Open(BoundType? type) => type is { ContainsParameter: true };
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
