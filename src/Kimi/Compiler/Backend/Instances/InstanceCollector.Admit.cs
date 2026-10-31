// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// Admission (K package 4.6, X19; DECISIONS R5-M170) and the location rule (K 4.9).
internal sealed partial class InstanceCollector
{
    private const int GrowthLimit = 16; // Generations of one identity on an admission chain: its key's height grew 15 times.
    private const int SetLimit = 1024; // Items of one identity.
    private const int HeightLimit = 128; // A type-driven key's Type height (BoundType.Height).
    private const int Window = 1024; // Identity items searched for the nearest one of the same identity.

    /// <summary>Gets the subject of a record caused at a site of an item's body (K 4.9), the one location rule of the collector's records and
    /// code generation's (R5 C0b1): the site located in its body, if that lies in a root module; else the root-module site the item's
    /// admission chain first reached, which each item keeps.</summary>
    /// <param name="item">The item.</param>
    /// <param name="body">Its body.</param>
    /// <param name="source">The source of a statement, a terminator or a local of the body, an analysis problem's subject in its location's
    /// scope (MirDropTable.Incomplete), or none for the body itself.</param>
    /// <returns>The subject.</returns>
    internal SourceRef Subject(int item, MirBody body, in SourceInfo source)
        => this.Located(body, source) is var located && located.Module < this.rootModules ? located : this.Set.Items[item].Site;

    // The storage a legacy row's handle, its parameter 0, designates (K 4.3; DECISIONS R5-M121, R5-M124), which AdmitRow applies to every
    // row: the LegacyRow item's representative Type, whose components, the element or the key and the value its routine stores, it lays out.
    // It is the Type below the parameter's implicit layers (Below, TyCtxt.Layer), an Array's, a UniqSlice's or a fixed array's own, and
    // through a raw pointer, whose dereference is explicit (SPEC 3.4.3, 5.1), its pointee (placeEntry's raw handle to a Dictionary, SPEC
    // 22.2); a part of the Type Below made, so its verdict was read at the site.
    private static BoundType Designated(BoundType below)
        => below is { Kind: BoundTypeKind.Semantics, Semantics: Parsing.SemanticsKind.Raw, Components: [var pointee] } ? pointee : below;

    // DIAGNOSTICS rule 6 (DECISIONS R5-M171): the order of two subjects, by module and then by id, the syntax table's pre-order of parsed
    // nodes, after which nodes outside the tree (Binding's synthesized ones) follow in creation order.
    private static int Order(SourceRef x, SourceRef y) => x.Module != y.Module ? x.Module - y.Module : x.Id.CompareTo(y.Id);

    // DECISIONS R5-M170: the declaration a DropGlue item's identity names, that of a Nominal, Constructed or Closure Type; none for a
    // structural Type, whose parts are its subterms.
    private static BindingSymbol? Declaration(BoundType type)
        => type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed or BoundTypeKind.Closure ? type.Symbol : null;

    // The identity whose growth and breadth an item's admission counts (DECISIONS R5-M170): a body's or LegacyRow's definition, a DropGlue's
    // declaration (Declaration); null for every other item.
    private static (MonoKind Kind, MirDefKey Definition)? Identity(in MonoItem item) => item.Key.Kind switch
    {
        MonoKind.Body or MonoKind.LegacyRow => (item.Key.Kind, item.Key.Body.Def),
        MonoKind.DropGlue when Declaration(item.RepresentativeType!) is { } declaration => (MonoKind.DropGlue, new(declaration, default)),
        _ => null,
    };

    // SPEC 6.1.3, 8.13.3 (DECISIONS R5-M43, R5-M157, BU-60): whether the substitution of a definition generic in its context carries the
    // instance it instantiates in, that of the declaration whose instance it holds (TyCtxt.EnclosingDeclaration), as its binder (a Closure's
    // context, R5-M36) or its declaring Type (Binding writes one level into a generic function; a held context, BU-60). A closed declaration
    // has none to carry; a member or top-level declaration has no enclosing one, its own substitution being its instance (Instantiable's
    // contract).
    private bool CarriesInstance(Subst subst, BindingSymbol definition)
        => this.types.EnclosingDeclaration(definition) is not { } enclosing || !this.types.Instantiates(enclosing) ||
            ReferenceEquals(subst.Target, enclosing) || ReferenceEquals(subst.Declaring?.Symbol, enclosing);

    // The item of a callee body requested at a site of an item's body (its located subject), admitted once per key (KD1): keyed by its
    // definition and, when it instantiates, its erased substitution, represented by the substitution itself (KD3). A definition whose
    // substitution does not carry the instance it instantiates in (CarriesInstance) is a capability gap, and a new key's instance needs its
    // local Types (Instantiable); one past a limit is GenerationResourceLimit_Kd; a representative that leaves its body open is a broken
    // contract. -1 when refused.
    private int Admit(int parent, SourceRef site, MirBody target, Subst? subst)
    {
        var set = this.Set;
        Subst? erased = null;
        if (target.Key.Decl is { } decl && this.types.Instantiates(decl) && (subst is null || (erased = this.types.Erase(subst)) is null))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site); // An instance without its substitution.
            return -1;
        }

        // SPEC 6.1.3, 8.13.3: an instance of a definition generic in its context holds the instance it instantiates in; a substitution that
        // does not carry it is the EnclosingInstance gap (Capability's row: a nested function's call until a representation of the chain,
        // DECISIONS R5-M43; a Closure in no instance, the closure's own identity, R5-M157, BU-60).
        if (erased is not null && !this.CarriesInstance(subst!, target.Key.Decl!))
        {
            this.Report(Capability.Code(CapabilityGap.EnclosingInstance), site);
            return -1;
        }

        var key = new MonoKey(MonoKind.Body, new(target.Key, erased), null, null, 0, null);
        if (set.Find(key) is >= 0 and var found)
        {
            return found;
        }

        if (this.refused.Contains(key))
        {
            return -1; // Refused once, with its one record (DECISIONS R5-M170).
        }

        var representative = erased is null ? null : subst;
        if (representative is not null && !this.Instantiable(target, representative, site))
        {
            return -1;
        }

        if (!this.Grows(parent, key, (MonoKind.Body, target.Key), representative?.Height ?? 0, site, out var growth))
        {
            return -1;
        }

        this.Work++;
        var view = this.View(target, representative, site);
        return set.Add(new()
        {
            Key = key, Representative = representative, BodyOrdinal = target.Ordinal, View = view, Parent = parent, Growth = growth, Site = site,
        });
    }

    // K 4.3, 4.8: the item of a type-driven key (its kind, a property witness's Field and a shim's variant) requested at a site, admitted
    // once per erased Type (the key's own when preset: a comparison item's node Type, DECISIONS R5-M166) whose layout verdict is Ok, a DropGlue's with its collection elements' (X34; a failure is the Type's record, or
    // derived), and represented by the first unerased Type that requests it, which is closed (an open one is a broken contract,
    // InternalInvariant_Kd), and by that request's substitution, a shim's: its admitting call's, whose storage verdict the call's edge read
    // (Made), so TypeOf gives the shim's signature in its instance (DECISIONS R5-M126); null for another kind. A Type higher than the
    // height cap is, like a Layout limit, a use of its erased Type over a limit in the requesting declaration (Limit, DECISIONS R5-M171),
    // read again at each request, not a refused key. -1 when refused.
    private int AdmitType(int parent, SourceRef site, MonoKey key, BoundType type, Subst? representative)
    {
        var set = this.Set;
        if (type.ContainsParameter)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return -1;
        }

        key = key with { Type = key.Type ?? this.types.Erase(type) }; // A comparison item's is its node's (InstanceCollector.Compared).
        if (set.Find(key) is >= 0 and var found)
        {
            return found;
        }

        if (this.refused.Contains(key))
        {
            return -1; // Refused once, with its one record (DECISIONS R5-M170).
        }

        if (type.Height > HeightLimit)
        {
            this.Limit(this.types.Erase(type), this.Declaration(parent), site); // Not the key's Type: a comparison's may be a template's.
            return -1;
        }

        if (!this.LaysOut(parent, site, type) || (key.Kind == MonoKind.DropGlue && !this.ElementsLayOut(parent, site, type)))
        {
            return -1;
        }

        var growth = new Growth(0, 0, this.Above(parent));
        if (key.Kind == MonoKind.DropGlue && Declaration(type) is { } declaration &&
            !this.Grows(parent, key, (MonoKind.DropGlue, new(declaration, default)), type.Height, site, out growth))
        {
            return -1;
        }

        this.Work++;
        return set.Add(new()
        {
            Key = key, Representative = representative, RepresentativeType = type, BodyOrdinal = -1, Parent = parent, Growth = growth, Site = site,
        });
    }

    // K 4.3, 4.5 row 2 (DECISIONS R5-M121, R5-M124): the LegacyRow item of a bodyless compiler function whose legacy row writes its call in its
    // instance, requested at a site, admitted once per key: its function and its erased substitution (as a body instance's, null exactly when
    // the function does not instantiate), represented by the substitution, under which TyCtxt.Signature gives its Types, and by the storage
    // its parameter 0 designates (Designated: an Array, a UniqSlice, a fixed array or a Dictionary; Below, its verdict read at the site),
    // each of whose components, the elements its helper stores, is a by-value use (K 4.8, X34); past the depth limit
    // GenerationResourceLimit_Kd; an open or missing instance is a broken contract. -1 when refused.
    private int AdmitRow(int parent, SourceRef site, BindingSymbol function, Subst? subst)
    {
        var set = this.Set;
        Subst? erased = null;
        var instantiates = this.types.Instantiates(function);
        if (this.Below(site, function, subst, 0) is not { } below)
        {
            return -1;
        }

        var collection = Designated(below);
        if ((instantiates && (subst is null || (erased = this.types.Erase(subst)) is null)) ||
            collection is not { ContainsParameter: false, Components.Count: > 0 })
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return -1;
        }

        var key = new MonoKey(MonoKind.LegacyRow, new(new(function, MirBodyKind.Fn), erased), null, null, 0, null);
        if (set.Find(key) is >= 0 and var found)
        {
            return found;
        }

        if (this.refused.Contains(key))
        {
            return -1; // Refused once, with its one record (DECISIONS R5-M170).
        }

        var laidOut = true;
        for (var i = 0; i < collection.Components.Count; i++)
        {
            laidOut &= this.LaysOut(parent, site, collection.Components[i]);
        }

        if (!laidOut)
        {
            return -1;
        }

        if (!this.Grows(parent, key, (MonoKind.LegacyRow, key.Body.Def), subst?.Height ?? 0, site, out var growth))
        {
            return -1;
        }

        this.Work++;
        return set.Add(new()
        {
            Key = key, Representative = erased is null ? null : subst, RepresentativeType = collection, BodyOrdinal = -1, Parent = parent,
            Growth = growth, Site = site,
        });
    }

    // Whether a representative is closed and makes every local Type of its body in its instance (TyCtxt.Instance), closed, the locals' worst
    // verdict read at the requesting site (Made; every local is read, so a limit wins over a part Binding rejected, DECISIONS BU-36); an open
    // representative or local Type, which K 4.3's closed representatives exclude, is a broken contract.
    private bool Instantiable(MirBody body, Subst representative, SourceRef site)
    {
        var open = Open(representative.Self) || Open(representative.Declaring);
        foreach (var type in representative.Types)
        {
            open |= Open(type);
        }

        var storage = Binding.StorageOutcome.Prepared;
        for (var i = 0; !open && i < body.Locals.Length; i++)
        {
            var (instance, verdict) = this.types.Instance(body.Locals[i].Type, representative);
            (open, storage) = (Open(instance), verdict > storage ? verdict : storage);
        }

        if (open)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return false;
        }

        return this.Made(storage, site);

        static bool Open(BoundType? type) => type is { ContainsParameter: true };
    }

    // The nearest item with an identity at or above an item: where a new child's search begins.
    private int Above(int item) => Identity(this.Set.Items[item]) is null ? this.Set.Items[item].Growth.Above : item;

    // SPEC 8.15.3, IMPL 21.3.2 (DECISIONS R5-M170, superseding K 4.6's depth): whether a new key of an identity, requested below an item,
    // stays within the growth limits. The nearest item of its identity among the Window identity items above (the Above links; a walk only
    // when the identity has an admitted item, each step counted in Work, so at most Window per key, review 1's N5) gives its
    // generation, one more when the key's height passes that item's peak (growth; both saturate at 255, past which no growth is seen and
    // SetLimit stops the identity), else the same (a repeat at a height already reached);
    // none within the Window is the first generation. Past GrowthLimit generations, or past SetLimit items of the identity, it is
    // GenerationResourceLimit_Kd at the requesting site, the key's one record: the key is refused (refused), so a later request of it is
    // refused without a record; else the identity counts the item.
    private bool Grows(int parent, in MonoKey key, (MonoKind Kind, MirDefKey Definition) identity, int height, SourceRef site, out Growth growth)
    {
        var items = this.Set.Items;
        var above = this.Above(parent);
        growth = new(1, (byte)Math.Min(height, byte.MaxValue), above);
        this.counts.TryGetValue(identity, out var count);
        var current = count == 0 ? -1 : above; // No admitted item of the identity: none above (a root is closed, one key).
        for (var steps = 0; current >= 0 && steps < Window; steps++, current = items[current].Growth.Above)
        {
            this.Work++;
            if (Identity(items[current]) == identity)
            {
                var found = items[current].Growth;
                growth = growth.Peak > found.Peak ? new((byte)Math.Min(found.Generation + 1, byte.MaxValue), growth.Peak, above) : found with { Above = above };
                break;
            }
        }

        if (growth.Generation > GrowthLimit || count >= SetLimit)
        {
            this.refused.Add(key);
            this.Report(DiagnosticCode.GenerationResourceLimit_Kd, site);
            return false;
        }

        this.counts[identity] = count + 1;
        return true;
    }

    // K 4.3: the one view of a body its representative selects (a closed item's body has one); none or several is a broken contract.
    private byte View(MirBody body, Subst? representative, SourceRef site)
    {
        var views = this.cases.Views(body, representative);
        if (BitOperations.PopCount(views) != 1)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, site);
            return 0;
        }

        return (byte)BitOperations.TrailingZeroCount(views);
    }

    // K 4.9 (DECISIONS R5-M171): the declaration whose body holds the subjects of an item's records (Subject): the body of the nearest item
    // on its admission chain, itself included, whose body lies in a root module, since every other item's Site is its parent's located
    // site. Memoized per item for the run (declaring, cleared by Run, grown with the items), each walk filling every item it passed, so an
    // item is walked once per run; each step counts in Work.
    private MirDefKey Declaration(int item)
    {
        var items = this.Set.Items;
        var bodies = this.program!.Bodies;
        if (this.declaring.Length < items.Length)
        {
            Array.Resize(ref this.declaring, Math.Max(64, 2 * items.Length));
        }

        var found = item;
        for (; this.declaring[found] == 0; found = items[found].Parent, this.Work++)
        {
            if (items[found].BodyOrdinal >= 0 && bodies[items[found].BodyOrdinal].Source.Module < this.rootModules)
            {
                this.declaring[found] = found + 1;
                break;
            }
        }

        var declaring = this.declaring[found];
        for (var i = item; this.declaring[i] == 0; i = items[i].Parent)
        {
            this.declaring[i] = declaring;
        }

        return bodies[items[declaring - 1].BodyOrdinal].Key;
    }

    // K 4.9: a site of a body, moved out of the inline defaults of templates outside the root modules to the outermost omitting call; in a
    // root-module body, always a root-module site.
    private SourceRef Located(MirBody body, in SourceInfo source)
    {
        var subject = body.Subject(source);
        for (int scope = source.Scope; scope > 0; scope = body.Scopes[scope].Outer)
        {
            if (body.Scopes[scope].Kind == MirScopeKind.Inlined && subject.Module >= this.rootModules)
            {
                subject = body.Scopes[scope].Source;
            }
        }

        return subject;
    }
}
