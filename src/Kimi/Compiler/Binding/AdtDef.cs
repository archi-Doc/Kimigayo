// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the stored shape of one struct or enum declaration.
// Input: its bound declaration; an instance's stored Types through the reader's TyCtxt (TyCtxt.Storage), a receiver Type through the
// reader's Binding. Output: ordered fields, cases, destructor, inline storage types, whether it has C layout and whether
// it lies on an inline storage cycle; a struct's dispatch shape (VTableDef); a stored Field's owner in an instance, its layer and own
// index (FieldOwner, the one walk by Field), and its storage position (FindField).
// Binding fills the shape once per pass before bodies; field and case ordinals stay consistent.
// Must not make MIR or Backend rediscover storage from declaration syntax.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The stored shape of one struct or enum declaration, kept on its symbol: stored Fields, Cases by ordinal, the
/// destructor and the Type syntax of its inline storage. Binding refills it once per pass, before any body is bound.</summary>
internal sealed class AdtDef
{
    /// <summary>Gets the inline storage Type syntax in declaration order: direct bases, then stored Field Types and Case payloads.</summary>
    internal List<Koto> Types { get; } = [];

    /// <summary>Gets the stored Fields in declaration order; a Field's logical index is its position here.</summary>
    internal PropertyKoto[] Fields { get; private set; } = [];

    /// <summary>Gets the Cases by ordinal; a Case form without a name keeps its ordinal and has no Case.</summary>
    internal BoundEnumCase?[] Cases { get; private set; } = [];

    internal FunctionKoto? DestructorKoto { get; set; }

    /// <summary>Gets or sets a value indicating whether the struct selected C layout with a valid Attribute and its layout is available at
    /// the declaration (SPEC 6.7.1: neither open, derived nor empty). Layout validation writes it for every struct in every pass, before
    /// storage is prepared; header binding runs earlier and reads the attributes instead.</summary>
    internal bool IsCLayout { get; set; }

    /// <summary>Gets or sets a value indicating whether the struct selected C layout with a valid Attribute but its layout is unavailable at
    /// the declaration (SPEC 6.7.1: open, derived or empty), a fact without a Binding record: Layout judges such a struct CLayout, the error
    /// at each by-value use (SPEC 8.15.3; DECISIONS R4-M134 Q8). Layout validation writes it for every struct in every pass.</summary>
    internal bool CLayoutUnavailable { get; set; }

    /// <summary>Gets or sets a value indicating whether the declaration lies on an inline storage cycle (InvalidInlineLayout_Kd).
    /// Inline layout validation writes it for every struct and enum in every pass; Layout reads it (DECISIONS R5-M37), and so does
    /// <see cref="Base(TyCtxt, BoundType)"/>, which header binding reaches before this pass writes it.</summary>
    internal bool InlineCycle { get; set; }

    /// <summary>Gets or sets the declaration a struct's base clause names, valid once its DirectBase query is Done (Binding.BaseLayers).</summary>
    internal BindingSymbol? DirectBase { get; set; }

    /// <summary>Gets or sets the storage version of the pass whose PrepareStorage refilled this shape.</summary>
    internal ulong Version { get; set; }

    /// <summary>Gets or sets a struct's dispatch shape, which slot selection refills in every pass (<see cref="VTableDef.Of"/>; DECISIONS
    /// R5-M62).</summary>
    internal VTableDef? VTable { get; set; }

    internal static bool IsStruct(BoundType? type) => type is { Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto };

    internal static bool IsEnum(BoundType? type) => type?.Semantics == SemanticsKind.Owner && type.Symbol?.Declaration is EnumKoto;

    internal static StructKoto? Declaration(BoundType? type) => IsStruct(type) ? (StructKoto)type!.Symbol!.Declaration : null;

    // An instance's stored Types are its substitution of the declared ones (SPEC 8.15): Type, Length and Origin arguments.
    internal static BoundType? FieldType(TyCtxt types, BoundType type, int index) => Instance(types, type).Fields[index];

    // A base on an inline cycle (SPEC 6.2.2) is an error at its container (ValidateInlineLayouts): no walk over an instance's layers
    // reads it.
    internal static BoundType? Base(TyCtxt types, BoundType type) => IsStruct(type) && type.Symbol!.Adt is not { InlineCycle: true } ? Instance(types, type).Base : null;

    // An enum's Cases by ordinal, each its payload Types as one Tuple, never null for an enum (DECISIONS BU-37); a Case whose payload does
    // not substitute, or a Case form without a name, is null, as a Field is, a part Binding rejected, which its readers take as they take a
    // null Field. Null for any other Type.
    internal static BoundType?[]? CaseTypes(TyCtxt types, BoundType type) => IsEnum(type) ? Instance(types, type).Cases : null;

    // R7-T1: whether a struct's or enum's instance has a stored Type past IMPL A.4's height (InstanceStorage.HeightLimit).
    internal static bool HeightLimit(TyCtxt types, BoundType type) => (IsStruct(type) || IsEnum(type)) && Instance(types, type).HeightLimit;

    // DECISIONS BU-37, SPEC 23.1.2 (G651 a2-2): a base part its instance does not store: the struct's declaration writes a base clause and
    // the instance has no Base, because the clause names no Type or does not substitute, its part passes IMPL A.4's height
    // (InstanceStorage.HeightLimit), or the struct lies on an inline cycle (Base). Each carries a reported record (the clause's or one it
    // rests on, the limit's at its site, the container's InvalidInlineLayout_Kd), so a reader of the instance's storage reads this one
    // statement as it reads FieldType's null (MirBody.SelectsPart): a value derived from that part's error (rule W), never "no base" of its
    // own. A selection reads the clause instead (Binding.SelectionBase).
    internal static bool BaseNotStored(TyCtxt types, BoundType type) => Declaration(type) is { BaseClause: not null } && Base(types, type) is null;

    internal static int Count(BoundType type) => Struct(type)?.Fields.Length ?? 0;

    // Selection identities include inherited fields, base first. Construction and destruction keep their separate layers.
    internal static int StorageCount(TyCtxt types, BoundType type)
        => Count(type) + (Base(types, type) is { } parent ? StorageCount(types, parent) : 0);

    // SPEC 6.2.2, 11.2.2: the Field owner of a struct instance's stored Field: the layer that stores it (the instance itself or the base
    // instance Depth Base steps up) and its stored slot, the Field's own index among that layer's stored Fields (Index); null when no layer
    // stores it. The one walk over a struct's layers by Field: FindField's storage position and the MIR builder's Base steps derive from it.
    // A walk that ends at a layer whose base part its instance does not store (BaseNotStored) answers that layer with no stored slot (Index
    // none): the Field can lie in that part, a value derived from its error (SPEC 23.1.2), never "no such Field". Index is the one member
    // naming the slot: every reader (FindField, MirBuilder.Field, FunctionCodegen.Storage, Binding.FailedField) indexes a layer's Fields
    // only through it.
    internal static (BoundType Layer, int Depth, int? Index)? FieldOwner(TyCtxt types, BoundType type, BindingSymbol? symbol)
    {
        var depth = 0;
        for (var layer = type; ; depth++)
        {
            var fields = Struct(layer)?.Fields;
            for (var i = 0; fields is not null && i < fields.Length; i++)
            {
                if (ReferenceEquals(fields[i].BoundSymbol, symbol))
                {
                    return (layer, depth, i);
                }
            }

            if (Base(types, layer) is not { } parent)
            {
                return BaseNotStored(types, layer) ? (layer, depth, null) : null;
            }

            layer = parent;
        }
    }

    // A Field's storage position, its owning layer's own index after its bases' Fields (base first), and its stored Type in the instance:
    // false where no layer stores it in a stored slot (position -1) or Binding formed no stored Type for it.
    internal static bool FindField(TyCtxt types, BoundType type, BindingSymbol? symbol, out BoundType? fieldType, out int position)
    {
        if (FieldOwner(types, type, symbol) is not { Index: { } index } owner)
        {
            (fieldType, position) = (null, -1);
            return false;
        }

        position = index + (Base(types, owner.Layer) is { } parent ? StorageCount(types, parent) : 0);
        fieldType = FieldType(types, owner.Layer, index);
        return fieldType is not null;
    }

    internal static PropertyKoto Field(BoundType type, int index) => Struct(type)!.Fields[index];

    // BU-234: whether a stored Field's own slot failed, its symbol of no Type: the part every instance stores of no Type (a MIR Field step
    // of no Type, MirBody.SelectsPart, PLAN G1184). FieldType is null for it, and also for a part past IMPL A.4's height (R7-T1) or one
    // that does not substitute, which the instance does not store.
    internal static bool FieldSlotFailed(BoundType type, int index) => Field(type, index).BoundSymbol is { Type: null };

    // SPEC 11.7: whether a constructor may place a stored Field only once: a let Field, or a var with a custom set.
    internal static bool PlacedOnce(BoundType type, int index)
        => Field(type, index) is not { VariableKind: VariableKind.Var, BoundSymbol.Property.Setter.IsStandard: true };

    // The logical stored index of the Field `name`. Generated code addresses Kimi library record Fields by name; a missing
    // Field is a defect of the compiler build.
    internal static int IndexOf(BoundType type, string name)
    {
        var fields = Struct(type)?.Fields;
        for (var i = 0; fields is not null && i < fields.Length; i++)
        {
            if (fields[i].NameKoto.IdentifierName == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"{type} stores no Field {name}.");
    }

    internal static FunctionKoto? Destructor(BoundType type) => Struct(type)?.DestructorKoto;

    // Whether a Type is the library's InlineStorage, whose contents the compiler never destroys: its owner destroys what remains (its
    // contract in Storage.kimi; SPEC 22.2).
    internal static bool IsInlineStorage(BoundType type) => type.Symbol?.LibraryDeclaration == KimiDeclarationId.InlineStorage;

    // The destructor's symbol, by which MIR names its body.
    internal static BindingSymbol? DestructorSymbol(BoundType type) => Destructor(type)?.BoundSymbol;

    // SPEC 6.3: whether a function is a constructor, whose body builds its object from fresh storage (the MIR analyses' fact).
    internal static bool IsConstructor(BindingSymbol? function) => function?.Declaration is FunctionKoto { IsConstructor: true };

    internal static BoundType? ReceiverType(Binding binding, FunctionKoto function)
        => (function.IsConstructor || function.IsDestructor) && function.BoundSymbol?.Scope.Owner.BoundSymbol is { } owner
            ? binding.SelfType(owner) : null;

    internal static BoundEnumCase? Case(BoundType type, int ordinal)
        => type.Symbol is { Declaration: EnumKoto, Adt.Cases: { } cases } && (uint)ordinal < (uint)cases.Length ? cases[ordinal] : null;

    // Publishes a pass's Fields (PrepareStorage) or Cases (PrepareCases, before the headers), reusing an array while its length is unchanged.
    internal void SetFields(List<PropertyKoto> fields) => this.Fields = Refill(this.Fields, fields);

    internal void SetCases(List<BoundEnumCase?> cases) => this.Cases = Refill(this.Cases, cases);

    private static AdtDef? Struct(BoundType? type) => IsStruct(type) ? type!.Symbol!.Adt : null;

    private static InstanceStorage Instance(TyCtxt types, BoundType type) => types.Storage(type);

    private static T[] Refill<T>(T[] array, List<T> items)
    {
        if (array.Length != items.Count)
        {
            array = new T[items.Count];
        }

        items.CopyTo(array);
        return array;
    }

    /// <summary>The stored Types of one struct or enum instance under its substitution, in two forms of the one substitution: the kept
    /// parts with the raw Origins it gives (Parts, PartsBase; TyCtxt.StoredParts) and AdtDef's view, the same Types without them (Base,
    /// Fields, Cases; DECISIONS BU-24, BU-41).</summary>
    internal sealed class InstanceStorage
    {
        internal BoundType? Base { get; set; }

        internal BoundType?[] Fields { get; set; } = [];

        // An enum's Case payload Tuples by ordinal, null where one did not form (CaseTypes); empty for a struct.
        internal BoundType?[] Cases { get; set; } = [];

        // A struct's Fields or an enum's Case payload Tuples with their raw Origins, `self` kept as Holder; null exactly where Fields or
        // Cases are.
        internal BoundType?[] Parts { get; set; } = [];

        // The base with its raw Origins; null exactly where Base is.
        internal BoundType? PartsBase { get; set; }

        // Whether a stored Type passes IMPL A.4's height, refused inside the instance collector's answer frame (Binding.Instance; R7-T1)
        // and, after Binding's run, in every frame (Binding.OpenStorageFormation; PLAN G651 U2a): its part is null, and the instance's
        // readers read it as a limit (Binding's storage walk, Layout's HeightLimit), never Rejected.
        internal bool HeightLimit { get; set; }

        // The pass that substituted these Types; not current otherwise.
        internal ulong Version { get; set; }
    }
}
