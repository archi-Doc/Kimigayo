# Introduction-Site Requirements and Implementation Plan

Specification change and implementation plan — 2026-10-21. The owner's draft calls this change "R1"; this plan names its
units GR0–GR6 so that they are not confused with compiler stage R1.

## 1. Authority, scope and start gate

- **Decision.** The owner adopts introduction-site requirements for generic parameters: a requirement on an ordinary
  Type slot is written on the slot (`<T is R>`), as an associated Type's requirement already is (`associate X is R`).
  For the changes stated here, this plan takes precedence over [SPEC](../../docs/SPEC.md) and its referenced documents;
  existing specifications govern everything else. This task changes no current specification or executable source and
  claims no support.
- **Start gate.** Implementation starts only after R9 is complete, as recorded in the [compiler stage
  ledger](../../docs/dev/COMPILER_ARCHITECTURE.md#stage-ledger). Every unit in §4, including restructuring, executable
  probes, migration, cleanup and optimization, is behind that gate; read-only inspection, design and document review may
  precede it. Nothing of this change goes into R9 or an earlier stage.
- **SPEC integration.** Formal integration into SPEC needs the owner's explicit instruction (§6). Because the compiler's
  tests compile the SPEC examples, it lands only inside the GR3–GR5 group merge (§4), never on dev on its own. The
  INTEGRATED registration made with this plan's commit (一部取り込み, intake only) changes no SPEC.
- **Out of scope (R2).** Declaration contracts written as body items remain: the leading region, the
  misplaced-Constraint error for the items that stay there, parenthesized escapes such as `(value is Dog)` and the item
  counting of [§19.4](../../docs/spec/19-compile-time-directives.md#194-excluded-syntax). This plan removes one kind of
  clause from that region and fixes nothing that R2 would decide differently.
- **Unchanged forms.** `length N` slots and Origin parameters (an OriginHeader `{a}`, an associated Type's Origin
  parameters) take no requirement. A family `associate View(a) …` keeps its requirement at its declaration (§2.6).
- **Related plans.** The [As Naming plan](2026-10-20%20As%20Naming%20Syntax%20and%20Implementation%20Plan.md) also
  changes the parser after R9; the two are independent in meaning and serialize on shared files (§4). [G170
  PlanB](2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) (its selected design C) brings R7's
  ContractSolver premise responsibility forward and keeps declaration clause storage with its owner; GR1's rows (§3.2)
  are that solver's direct input per conjunct, not a second store of declared premises.
- **Owner decisions.** §7 lists the rules this plan adds to the draft, the answers to the draft's questions, and the
  decisions requested.

### 1.1. Current rules and problems

[Appendix F](../../docs/spec/appendices/F-syntax-summary.md) (F.5) defines `GenericParameter := Name | Name "/" Name |
"length" Name`. By [§7.5](../../docs/spec/07-functions-and-callable-values.md#75-function-constraints) and
[§8.2.1](../../docs/spec/08-generics-constraints-and-contracts.md#821-constraint-clauses), a Constraint Clause `subject
is requirement` is written in the leading region of a function body, before the members of a struct or enum, as a
Contract-wide clause, or in the indented region under a function requirement. This plan calls each of these a
**Constraint region**.

| Problem | Principle |
| --- | --- |
| One concept has two forms: a Contract writes an associated Type's requirement where it introduces the name (`associate Element is R`), a generic declaration writes its parameter's requirement in a separate region. | 1 |
| A parameter and its requirement are apart: reading a parameter's meaning needs the header and the start of the body. | 2 |
| A `=>` body has no region, so one requirement forces an indented body; a function requirement needs an indented region for a one-word requirement. Foreign imports are never generic ([§22.6](../../docs/spec/22-core-execution-and-foreign-functions.md#226-foreign-function-imports)), so they are unaffected. | 1, 2 |
| (R2) Declaration contracts are body items, which needs the misplaced-Constraint error, parenthesized escapes and directive item counting. | 3 |

## 2. Language changes

### 2.1. Concept and grammar

An **introduction-site requirement** is the requirement written where a Type name is introduced: on an ordinary Type
slot, `<T is R>`, and on an associated Type a Contract declares, `associate X is R`
([§8.5.1](../../docs/spec/08-generics-constraints-and-contracts.md#851-declaration-and-specification), unchanged). Its
subject is the introduced name. In a conforming Type, `associate X is R` and `associate C.X is R` still specify or
constrain an associated Type; they are not this concept.

```ebnf
GenericParameters := "<" TrailingList<GenericParameter> ">"
GenericParameter  := Name ("is" IsRequirement)? | Name "/" Name | "length" Name
```

- **Productions.** The change reaches every production that uses GenericParameters: struct, enum, contract, function
  header and function requirement (F.6.1–F.6.3). Specialization TypeArguments, constructors and function expressions
  take no generic parameters and are unchanged.
- **Requirement.** `IsRequirement` is the nonterminal `associate` uses, with the same precedence: `is` consumes its
  right side through `or`, and a `not` directly after `is` negates all of it. The requirement ends at the next `,` or
  generic closer outside its own nested delimiters, since IsRequirement has no top-level comma.
- **Generic closers.** F.2.1 splits a token beginning with `>` when the parser closes Type arguments or generic
  parameters. This plan extends the rule to the closer of a CallableRequirement, which no `>` can follow today: `<F is
  Callable<(i32) -> i32>>` reads `>` (closing `Callable<…>`) then `>` (closing the list).
- **No other forms.** The production takes IsRequirement, which, as for `associate`, carries no effect list; layout
  alone would not forbid one, since a body may nest in a delimiter (F.4.2). `<s/T is R>`, `<length N is R>` and an
  effect line inside `<…>` are syntax errors with the existing reports. `length` at the start of a GenericParameter is
  always the keyword ([F.3.2](../../docs/spec/appendices/F-syntax-summary.md#f32-contextual-keywords)), so `<length>`
  and `<length is R>` are syntax errors. Rejecting `<length>` changes today's acceptance; §7 asks the owner to confirm
  it.
- **Slot kind.** `<T is R>` is a Type slot. A requirement is no part of a slot's kind, of GenericArity or of a Signature
  ([§9.1](../../docs/spec/09-names-signatures-and-access.md#91-signatures)): `f<T is Copy>(x: T)` and `f<T is Owned>(x:
  T)` conflict, as declarations differing only in Constraints do today.

### 2.2. Placement

Each Constraint Clause has exactly one place, decided by its own shape.

| Clause | Place |
| --- | --- |
| The subject is a single Name spelled as an ordinary Type slot of the same declaration, and no effect list | Introduction site only: `<T is R>` |
| A Contract-wide clause whose subject is spelled `X`, `Self.X` or `Self.(C).X` (`Self.(C<P1, …>).X` for a generic Contract, applied to its own parameters in order), where `C` is that Contract and `X` names one of its own `associate` declarations without Origin parameters, and no effect list | Introduction site only: `associate X is R` (§2.6) |
| A Callable clause with an effect list, whatever its subject | Constraint region |
| `Self`, including conformance declarations | Constraint region |
| Other associated projections: `T.(C).Item`, inherited associated Types, deeper projections, families and their applications, other qualifications such as `Self.(C<i32>).X` | Constraint region |
| Pair binder or pair target | Constraint region |
| A member's clause on a declaring-Type parameter (conditional member), an outer parameter (a function requirement's clause on its Contract's parameter or associated Type included), a closed subject | Constraint region |
| `origin` relations and effect items | Constraint region (unchanged) |
| Conformance conditions `Self is C when …` | Unchanged; a separate grammar |

- **Same declaration.** A function's own list for its function Constraints; a struct's, enum's or Contract's own list
  for its Container Constraints; a function requirement's own list for its requirement region. Lookup finds the nearest
  declaration: in a member that declares its own `<E>`, the subject `E` is the member's slot.
- **Syntactic test.** The test compares the subject's spelling with the declaration's own parameter list and, in a
  Contract, with the Contract's own name and `associate` declarations, and checks the region kind and whether an effect
  list follows; it resolves nothing. It is therefore judged before directive selection and in excluded syntax, with the
  same code and location ([§19.4](../../docs/spec/19-compile-time-directives.md#194-excluded-syntax)).
- **Kinds without ordinary Type slots.** Constructors, destructors, accessors, virtual functions and overrides, explicit
  specializations and foreign imports introduce none
  ([§6.3.1](../../docs/spec/06-declarations-and-containers.md#631-declaration),
  [§6.4.1](../../docs/spec/06-declarations-and-containers.md#641-declarations),
  [§8.13.1](../../docs/spec/08-generics-constraints-and-contracts.md#8131-declaration-and-target)), so nothing moves
  there; their region clauses and inherited Constraints are unchanged.
- **Other rules still apply.** A clause that stays in a region keeps that region's rules. An effect-bearing Callable
  clause on a Contract's own parameter stays in the Contract-wide region, where SPEC §8.11.6 rejects its effect list,
  and a function requirement's clause on its Contract's parameter keeps SPEC §8.4.2's subject rule; neither gets a
  placement report.

### 2.3. Effect-bearing Callable clauses

A Callable clause with an effect list stays in the Constraint region: the introduction-site production carries no effect
list (§2.1), and the bounds stay attached to their clause. Only that clause stays; the parameter's other requirements go
to its introduction site, so such a parameter has its requirements in two places. The `offload` entry of
[§22.3.6](../../docs/spec/22-core-execution-and-foreign-functions.md#2236-structured-worker-offload)'s listing becomes
(a Kimi declaration the implementation supplies, without a body); in `offloadEach`, `I is Iterator` and `F is
Transferable` move the same way, while `I.Item is Transferable` and its effect-bearing Callable clause stay:

```kimi
public group Async
    public func offload<F is Transferable, R>(task; child: F) -> R
        F is Callable<owner, () -> R>
            effect confined
```

The draft's option (i) keeps all of such a parameter's clauses in the region, so that its requirements stay in one
place, which is the owner's reason for preferring it. Its costs: one clause's place depends on another clause; a second
placement error (header to region) and a second repair are needed; and unrelated requirements move when an effect list
is added or removed. Option (ii), chosen here, places each clause by its own shape and splits such a parameter's
requirements. Callable candidates and bounds combine premises from every site (§2.4), so neither option changes meaning.
§7 asks the owner to confirm.

### 2.4. Several requirements and the Constraint unit

- **One requirement per slot.** Requirements on one slot are joined by `and` in one introduction-site requirement, in
  this canonical form, which the repair and the migration write. A single requirement is written as it stands. When two
  or more are joined, each operand keeps its meaning under F.6.2: an operand `not Q` (a `not` directly after `is`, which
  negates all of Q) is written `not (Q)` unless Q is one RequirementAtom; an operand with a top-level `or` is written
  `(R)`; and the first operand is parenthesized when it begins with `not`, since the join is a PositiveRequirement,
  which begins with a RequirementAtom.

  | Requirements | Joined |
  | --- | --- |
  | `T is not Copy`, `T is Owned` | `<T is (not Copy) and Owned>` |
  | `T is Owned`, `T is not Copy` | `<T is Owned and not Copy>` |
  | `T is not A or B`, `T is C` | `<T is (not (A or B)) and C>` |
  | `T is C`, `T is not A and B` | `<T is C and not (A and B)>` |
  | `T is A or B`, `T is C` | `<T is (A or B) and C>` |
  | `T is Owned and Copy`, `T is C` | `<T is Owned and Copy and C>` |

  Inside parentheses `not` is a RequirementUnary that binds one operand, so wrapping `not A or B` as `(not A or B)`
  would change its meaning. The canonical forms parse the same under F.6.2 and under today's parser, which reads a
  leading `not` inside parentheses as negating the whole parenthesized requirement (§7).
- **Constraint unit.** A Constraint is a condition ([Appendix E](../../docs/spec/appendices/E-terminology.md)), and
  requirement precedence splits a requirement into conjuncts: `T is (A and B)` supplies `T is A` and `T is B` (SPEC
  §8.3.1). Each conjunct is one Constraint, and every rule and report that names a Constraint applies per conjunct:
  - judgment at step 5 of [§10.1](../../docs/spec/10-overload-resolution-and-inference.md#101-candidate-applicability)
    and "Constraints needing the slot are not judged" in
    [§10.8](../../docs/spec/10-overload-resolution-and-inference.md#108-generic-argument-inference);
  - obligations after selection, function-reference conditions and the failing Constraint a report names;
  - roles (§2.5), Callable candidates, and the explanations and related locations of Callable reports.
- **Grouping is notation.** Among the places §2.2 admits, how conjuncts are grouped into clauses never changes
  applicability, selection, inference, obligations or the set of failing Constraints. Two rules stay on the whole
  clause: the effect-list rule (below) and the `Self is not ObjectPayload` opt-out, which stands alone (SPEC §8.7.3,
  unchanged), so `Self is Copy and not ObjectPayload` stays an error while the two separate clauses are valid.
- **Report order.** A report that names one failing Constraint names the first decisive one (Refuted before Unknown, as
  today) in published order: requirements on generic parameters in parameter order, then the body's clauses in source
  order (region clauses and, in a Contract, `associate X is R` requirements). This is the implementation's reporting
  contract in DIAGNOSTICS, not a SPEC rule.
- **Callable Constraints** ([§8.10](../../docs/spec/08-generics-constraints-and-contracts.md#810-callable-constraints)).
  The Callable Constraints on `F` at a call are the propositions `F is Callable<r, S>` available there by exact
  assumption, conjunction and Contract refinement (SPEC §8.3.2), after the normalization of SPEC §8.3.1, from every site
  of the declaration and its enclosing declarations. A `Callable<…>` under `or` or under one `not` supplies none; `not
  not Callable<S>` supplies one. Their distinct normalized signatures are the call's candidates, each with the weakest
  receiver among the Callable Constraints with that signature;
  [§10.5.2](../../docs/spec/10-overload-resolution-and-inference.md#1052-call-signatures) uses this definition. On the
  2026-10-17 build, candidates and receivers already agree for `and`-joined, separate and enclosing premises; three
  whole-clause reports differ (the related location, the caller-side explanation and cascade suppression), which GR1
  aligns.
- **Effect lists.** Only a region clause whose whole requirement is one unparenthesized `Callable<…>` carries an effect
  list (SPEC §8.11.6, clarified: parentheses around one atom group nothing, so `F is (Callable<A>)` with a list would be
  a second spelling of the canonical form). Its bounds belong to that clause's Callable Constraint; a call through `F`
  uses the bounds of premises with its signature and an equal or stronger receiver, wherever the premise is written.

### 2.5. Meaning, scope and fragments

- **One Constraint set.** A declaration's Constraints are its introduction-site requirements together with its region
  clauses. Every rule that reads Constraints or premises reads this set, never the site or the order; only the report
  order of §2.4 follows the published order. In SPEC these rules are roles
  ([§6.1.4](../../docs/spec/06-declarations-and-containers.md#614-constraint-roles)), signature reliance (§7.5, §8.5.2),
  proof (§8.3), implementation matching (§8.6.3), common premises of overrides (§6.4.2), specialization inheritance
  (§8.13), identity classes (§8.2.3), collisions (§8.9.1), applicability and inference (§10.1, §10.5.2, §10.8), API
  accessibility (§9.4.4) and parameter defaults (§7.2.3).
- **Roles.** Writing a Constraint at the introduction site never changes its SPEC §6.1.4 role, which this plan assigns
  per conjunct (§2.4). On a struct, enum or function parameter it is an input condition. On a Contract's own parameter,
  a Constraint that mentions `Self` or an associated Type of `Self` is an implementation requirement, and any other is a
  reference input condition (§6.1.4's inherited arguments include the Contract's own and enclosing parameters); so
  `contract C<Key is Copy and Element>` has one of each.
- **Conditional members.** A member is conditional when any of its Constraints, at either site, mentions a generic
  parameter of its declaring Type, as the implementation already judges them; placement follows the subject only. A
  member's `<U is Box<E>>` is written on `U` and still takes part in applicability.
- **Name scope.** Names in an introduction-site requirement resolve exactly as in the declaration's Constraint region:
  every generic parameter of the declaration (earlier, later or the same one), `Self` where the region admits it, and
  for a struct, enum or Contract its body as lookup stage 2, base layers included
  ([§9.5](../../docs/spec/09-names-signatures-and-access.md#95-unqualified-lookup)), unlike the base clause in the same
  header, which resolves without base-layer examination. Resolution is independent of order: `<T is Indexable<U>, U>`
  and `<T is Addable<T>>` are valid.
- **Origins.** An introduction-site requirement is not part of the named callable signature for Origin introduction
  ([§15.3.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1534-names-scope-and-quantification)). Its borrow
  annotations only reference exactly the Origins and binding sets visible in the declaration's Constraint region: the
  Origin slots (SPEC §15.3.2) of the declaration itself when it is a struct or enum and of its enclosing Types, the
  writing fragment's base-occurrence sets, value-parameter names, signature Origins and an enclosing function's Origins.
  An unbound name is an error, as in the region. Binding-set names written in a Callable requirement belong to the
  declaration and are visible to its relations, clauses and body (SPEC §8.10).
- **Identity.** Identity classes, cycles and conflicts (SPEC §8.2.3) are computed over the whole set. The error is
  located at the declaration, with every participating Constraint related, since its clauses may lie at two sites.
- **Fragments.** The fragment that supplies the shared Constraint region also writes the introduction-site requirements;
  every other fragment writes the bare parameters. Requirements are no part of the agreement on generic parameter count,
  kinds, order and names ([§6.1.2](../../docs/spec/06-declarations-and-containers.md#612-container-fragments)), and each
  resolves in the SourceDocument of the fragment that writes it.

```kimi
// A.kimi
struct Box<T is Comparable>
    var count: i32 = 0

// B.kimi
struct Box<T>
    func countValue(self: ref/Self) -> i32 => self.count
// Writing `<T is Comparable>` or a Constraint region in B.kimi as well is an error.
```

### 2.6. Contracts

- **Contract Type parameters.** A Contract-wide clause on the Contract's own parameter moves to it: `contract
  Indexable<Key is Equatable>`.
- **Own associated Types.** A requirement on an associated Type the Contract declares, its subject spelled as §2.2's
  second row lists, is written only at its declaration, `associate X is R`, joined by `and` with any requirement already
  there (§2.4); each introduced Type name thus has one canonical requirement site. A qualifier that names the Contract
  by another spelling (a path such as `Self.(G.C).X`, a `::`-rooted path or an alias) is an other qualification: the
  test resolves no name, so such a clause stays in the region (§7). Inside Contract `C`, `associate C.X is R` is
  invalid, because a qualifier names a direct conformance or an ancestor (SPEC §8.5.1); it is no second site.
- **Families.** A family `associate X(a) …` takes its requirement only at its declaration
  ([§8.5.4](../../docs/spec/08-generics-constraints-and-contracts.md#854-origin-parameters)); a region clause on the
  unapplied `X` stays in the region and keeps its unapplied-family error.
- **Inherited names.** In a Contract with parents, a bare `X` or `Self.X` may also name an inherited associated Type;
  the spelling test still classifies it as own (§2.2), the clause keeps its ambiguity error
  ([§8.5.2](../../docs/spec/08-generics-constraints-and-contracts.md#852-projections)), and the repair is withheld
  (§2.8).
- **Stays in the region.** Inherited associated Types (`Self.(Parent).X`, or a bare name unique among own and inherited
  declarations that the Contract does not declare), deeper projections (`Self.(A).X.(B).Y`), fixed applications of a
  family (`Self.View(static)`), `Self` clauses and closed clauses.
- **Function requirements.** `func create<T is Copy>(value: T) -> Self`; the indented region under a requirement remains
  for its other items and is omitted when empty.

```kimi
contract Source
    associate Element
    func read(self: ref/Self) -> Element
    Element is Equatable
    // Error: write `associate Element is Equatable`.

contract OrderedSource: Source
    Self.(Source).Element is Comparable     // An inherited associated Type stays in the region.

contract Factory
    func create<T is Copy>(value: T) -> Self
```

### 2.7. Compile-time directives

The generic parameter list admits no directive
([§19.1](../../docs/spec/19-compile-time-directives.md#191-placement-and-structure)). A clause of §2.2's first two rows
is the placement error in every configuration, directives included, because the test is syntactic. Its repair is
withheld when a directive inside the declaration encloses the clause or any declaration of its `associate X`, since the
move could change which configurations hold the requirement; a declaration that a directive controls as a whole moves
normally. A configuration-dependent requirement on an ordinary Type slot is written as separate whole declarations, each
under its directive (`trace` below); directives still select the region items that stay. Today such a clause is valid
and makes a contract depend on the configuration; three clauses in two tests use it, and no library source or SPEC
example does.

```kimi
func process<T>(value: T) -> ()
    #if debug
        T is Describe        // Error in every build: a requirement on T is written only on <T ...>.
    return

#if debug
    func trace<T is Describe>(value: T) -> () => log(value.describe())
#if not debug
    func trace<T>(value: T) -> () => ()
```

### 2.8. Diagnostics and repair

- **One report.** A clause of §2.2's first two rows written anywhere but its introduction site gets exactly one
  placement report: `MisplacedSyntax_Kd` with a new SyntaxForm row (named at GR0), located at the whole clause, with the
  parameter or `associate` declaration related. The clause neither belongs to nor ends a leading region, so the
  misplaced-Constraint error of SPEC §7.5 never also fires for it; nor does it take part in a struct's, enum's or
  Contract's member-order check (`DeclarationOrderWarning_Kd`, SPEC §8.2.1). It is bound normally, not as a recovery, so
  its uses report nothing further; its own errors, such as an ambiguous associated name, stay.
- **Repair.** One candidate of a new repair kind (named at GR0, e.g. `Repair.MoveRequirement`), with edits in the
  record's document. It deletes the clause's line and writes its requirement R at the introduction site: ` is R` after
  the parameter Name or `associate X`; or, when the site already has a requirement E, `E and R` in place of E, with E as
  the first operand and R as the next under §2.4's rule (`<T is not Copy>` with `T is Owned` gives `<T is (not Copy) and
  Owned>`). It has no conditions (`verified` and `required` empty), since §2.4–§2.5 make the move equivalent. It is
  withheld when:
  - a directive inside the declaration encloses the clause or any declaration of the target `associate X` (§2.7);
  - any part involved is a recovery or holds a syntax error;
  - the subject is a bare `X` or `Self.X` in a Contract with parents (§2.6);
  - the clause spans more than one line, or its line holds other text, comments included;
  - deleting it would leave an executable body without a source item.
- **Other shapes.** A Semantics or Semantics-category requirement on an ordinary Type slot (`<T is ref>`) gets the
  diagnostic the same region clause gets today (SPEC §8.2.1).
- **Vocabulary.** The SyntaxForm row and the repair kind are new vocabulary entries; the repair kind is a value of the
  closed enumeration of the [check output schema](../../docs/spec/schemas/check-output.schema.json), a SPEC 23 change
  without a version change. Both need owner approval, recorded in DECISIONS with the design at GR0; the schema value
  lands no later than the commit that first emits the kind. Offering a repair is implementation-defined (SPEC §23.1.3),
  so the owner may approve the report without it; the repair kind, its schema value and GR5's repair work are then
  dropped, not deferred.

### 2.9. Bodies and layout

- **Bodies.** Introduction-site requirements work with every body form, `=>` included. A declaration whose remaining
  items need the region keeps an indented body. A body that held only Constraint Clauses has no item left: an executable
  declaration then needs a body, such as `=> ()` ([§14.2.1](../../docs/spec/14-control-flow.md#1421-body-forms)); the
  compiler-signature declarations of the Kimi library become header-only, as their files already allow.
- **Layout.** A declaration's generic parameter list is a delimiter. F.4.3's first bullet, which today reads "the angle
  brackets of Type arguments and generic parameters in Type positions" (generic parameters never occur in a Type
  position), becomes "The delimiters are parentheses, brackets, Origin braces, the angle brackets of GenericParameters,
  and the angle brackets of Type arguments in Type positions." The implementation already follows this for an adjacent
  `<`. A long list continues across lines under the delimiter rules, and a continuation line inside it may start with a
  requirement's `and` or `or`. The migration (§4.1) wraps a long list with one parameter on each indented line and the
  closer at the header's indentation; this is a convention, not a grammar rule.

```kimi
func countMatches<
    T is Equatable,
    F is Callable<(ref/T) -> bool>
>(values: ref/Array<T>, test: ref/F) -> isize => 0
```

### 2.10. Examples

```kimi
func apply<F is Callable<(i32) -> i32>>(value: i32, transform: ref/F) -> i32 => transform(value)

func same<T is Copy and Equatable>(a: T, b: T) -> bool => a == b

contract Describe
    func describe(self: ref/Self) -> string

struct Labeled<T is Describe>
    Self is Describe                         // A conformance declaration stays in the region.
    var value: T
    public func describe(self: ref/Self) -> string => self.value.describe()

func keep<s/T>(x: s/T)                       // A pair binder's clause stays in the region.
    s is ref or uniq
    let empty: Option<T> = .None

func readInt<T is Source>(source: ref/T) -> i32
    T.(Source).Element is i32                // A projection stays in the region.
    return source.read()

struct Holder<E>
    public var item: E
    public init(item: E) => self.item = item@move
    public func same(self, other: ref/Self) -> bool
        E is Equatable                       // A conditional member stays in the region.
        return self.item == other.item

func bad<T>(x: T) -> T
    T is Copy
    // Error: a requirement on T belongs on <T>; the repair writes <T is Copy>.
    return x

func clamp<T is Comparable>(value: T, low: T, high: T) -> T
    T is Copy
    // Error: the repair writes <T is Comparable and Copy>.
    return value
```

## 3. Implementation design

### 3.1. Cause and sites

Required meaning (§2): one Constraint set per declaration, written at two sites, placed by clause shape and judged per
conjunct → what the representation lacks or wrongly allows:

- **Syntax.** `GenericParameterKoto` is childless, so `<T is R>` cannot be represented. A declaration's clauses are
  published under three members in two shapes (`FunctionKoto.TypeConstraints`,
  `DeclarationContainerKoto.ConstraintNodes` and its mutable `TypeConstraints`), so consumers choose a list by
  declaration kind.
- **Placement** has no owner: four parse sites decide it by different rules (the body's leading region, Container items,
  requirement regions, `associate`), and their spelling test also matches pair binders.
- **Binding's unit is the clause.** Seven consumers judge or report whole clauses; `AddConstraintFact` drops a whole
  `and` when one operand is unresolved and adds the `and` itself as a fact; the §6.1.4 role is recomputed at three sites
  from the whole requirement or the subject's symbol kind.

→ owners: Syntax publishes the node, one Constraint sequence and placement (a new `ConstraintPlacement` component); the
Binding declaration query publishes one row per Constraint with its role and source; the diagnostics catalog owns the
new SyntaxForm and repair rows → unnecessary: the deletions of §3.4.

Inspection baseline: `e307e9f9`. These are source observations; the probes ran on the Release build of 2026-10-17. R7
consolidates Binding (resolver, ContractSolver, declaration queries) and R9 the item loops and type-grammar entry
points, so many sites move; GR0 finds their owners through [CODEMAP](../../docs/dev/CODEMAP.md). Paths are relative to
`src/Kimi/`; a bare file name is unique under it.

| Area | Baseline site | Work |
| --- | --- | --- |
| Generic parameters | `Compiler/Parsing/Parser.cs`: `ParseGenericArguments` (4751-4843), steered by `allowLength` and `specialization`; `GenericParameterKoto` | A requirement after a plain Name followed by `is`; specialization TypeArguments move to the Type-argument parser first (GR2). |
| Requirement grammar | `Parser.ParseTypeConstraint`'s local functions (2257-2391) | Extract one IsRequirement entry, behavior-preserving; `associate` and `when` lists use it already. |
| Placement | `Parser.ParseExecutableItem` (2730-2754); `DeclarationContainerKoto.ParseMemberItem` (1079-1172) with its order check (940-948); `Parser.ParseSignatureClauses` (`Parser.Syntax.cs:205-273`); `associate` (`DeclarationContainerKoto.cs:728-781`) | One classifier, recorder and parse-local collector in `ConstraintPlacement`; the item loops keep only the parse-time choice of clause or expression. |
| Published sequence | `FunctionKoto.TypeConstraints`; `DeclarationContainerKoto.ConstraintNodes`, `TypeConstraints`; `BoundContract.ClauseStorage` | One `ConstraintClauses` sequence replaces the three Syntax members; `ClauseStorage` is filled from it, or removed if GR0 finds no reader after G170 C. |
| Fragments | `DeclarationContainerKoto.AddHeader` (207-265), `AcceptsTypeConstraints` (606-613) | The supplying fragment's list is published, replacing a bare list adopted from an earlier fragment; every other fragment's list is compared and dropped. |
| Constraint unit | `Binding.Constraints.cs`: `AddConstraintFact` (584-617), roles in `BindConstraint` (476-484); `Binding.Constraints.Substitution.cs` (40-117); `Binding.CandidateEvaluation.cs:231-245`; `Binding.FunctionItems.cs:522-547`; `Binding.CallableSelection.cs:221-235`; `Binding.CallableProofs.cs:81-91`; `Binding.Diagnostics.cs:1485-1555`; `Binding.Contracts.cs:787` | Rows per conjunct with role and source; consumers read rows. |
| Count gates and owner walks | `Binding.FunctionItems.cs:148, 313`; `Binding.Startup.cs:163`; `Binding.VirtualDeclarations.cs:59`; `Binding.Specializations.cs:394`; `Binding.ConstraintValidation.cs:107-165`; `Binding.Access.cs:77-106, 200-211` | Read the sequence; find a clause's declaration through one Syntax rule. |
| Layout and recovery | `Compiler/Lexing/TokenReader.cs:845-860` (`SkipListItem` swallows a closing `>>`); `Parser.cs:2345-2355` (a missing atom consumes `>`); `Compiler/Lexing/Tokenizer.cs:1369-1411` (`PreviousLineStartsBody` ignores `<` in function headers) | Three recovery fixes that §2.9's layout makes reachable. |
| Writers and displays | `GenericParameterKoto.WriteTo`; `FunctionKoto.cs:610-622, 718-746`; `DeclarationContainerKoto.cs:310-323`; `Checking/Hover/HoverBuilder.Details.cs:11-45, 132-145`; `SemanticQuery.ConstraintOf`; `Binding.Diagnostics.cs:629` (`ReportArity` joins parameters through `ToString`) | Write the requirement in the parameter; Details and identity read the sequence; name displays use the parameter's name, not its text. Effect hover needs nothing (environment premises carry no clause). |
| Diagnostics | `SyntaxForm.cs`, `DiagnosticRequirement.tinyhand`, `RepairKind.tinyhand`, `DiagnosticRepair.cs`, the check output schema; `DiagnosticCollection.ReportSyntax` (165) | One form row, one repair kind; `ReportSyntax` already takes related locations and several edits. |
| Unchanged | Documentation (reads names only), the LSP (multi-edit code actions in one document), conformance conditions, the TextMate rules | Editor: grammar test rows only (`src/kimi-ext/src/test/unit/grammar.test.ts`). |

### 3.2. Representation and owners

- **Node.** `GenericParameterKoto` gains one child slot, `Requirement: IsKoto?`. The IsKoto is the class every
  Constraint Clause uses: its subject is a name reference over the parameter's name token and its right side the
  IsRequirement, built by the existing constraint parser without an effect list. The parameter's span covers `T is R`;
  its hover anchor stays on the name, which outranks the subject reference. Recovery inside the requirement is recorded
  on the IsKoto, as for a region clause.
- **One sequence.** `ConstraintClauses` publishes a declaration's clauses in §2.4's published order. It is created in
  P1, reads through the child slots so that edits never leave it stale, and enumerates without allocation.
- **Site.** No flag. An introduction-site clause is the child of a `GenericParameterKoto` or a Contract `associate`;
  writers, the placement recorder and Hover headers read that. One Syntax rule, `OwnerOf(clause)`, gives the declaration
  for walks that use `Parent` today. Binding never reads the site.
- **Writers.** `GenericParameterKoto.WriteTo` writes `T is R`; region writers write region clauses only. Function,
  Container and Hover headers then show `<T is R>`, and parse, write and parse again yields the same structure.
- **Placement.** `ConstraintPlacement` (Parsing; its entry file states its contract) owns the leading-region rule of
  SPEC §7.5, moved unchanged, and §2.2, so R2 later changes one owner.
  - Each item loop (executable, member, requirement region) still decides at parse time whether a line is a clause or an
    expression, using the component's subject test.
  - It passes every clause it parses, and the member loop every `associate` declaration (bare or with a requirement),
    selected or excluded, to a collector in its existing parse-local item state, with these facts: the region kind,
    after an executable item (excluded ones included, as today), after a member, enclosed by a directive inside the
    declaration, and excluded. The collector lives only for the declaration's parse.
  - At the end of the declaration, which a Contract's `associate` declarations require, one classifier judges each
    collected clause from those facts, the declaration's ordinary Type slots, and the Contract's own name and collected
    `associate` declarations (selected and excluded alike, never the published declaration). It records the §2.8 report
    through `ReportSyntax`, builds or withholds the repair (the target slot or `associate` declaration gives the related
    location, the insertion point and any existing requirement E), records the misplaced-Constraint recovery, and runs
    the member-order check only for clauses that stay. Excluded clauses are reported but never added.
- **Constraint rows.** The Binding declaration query binds each clause in the region scope (§2.5), walks its conjuncts
  iteratively (parentheses are transparent, `and` splits, `not not` folds) and publishes one row per Constraint:
  `(proposition, conjunct source, clause source, role)`. Facts keep their row, so a fact or Callable candidate maps to
  its conjunct without a scan. Every judgment and first-cause report reads rows; writers and placement read syntax. The
  whole-clause rules of §2.4 read the clause source; effect bounds stay attached to their region clause, which is
  exactly one row.
- **Fragments.** The published generic list is that of the fragment that supplies the Constraint region; a fragment
  supplies it when its header writes a requirement or its body a clause or relation. A field naming the supplying
  fragment replaces the count test that would make header-time clauses duplicates; a second supplier keeps
  `DuplicateTypeConstraintDefinition_Kd`.
- **No new tables.** No Koto-keyed table, flag parameter or second copy of a rule; the collector is parse state, not a
  table on the tree, and the existing Koto-keyed tables leave with R7.

### 3.3. Alternatives

| Option | Decision |
| --- | --- |
| Requirement as a child IsKoto of the parameter, one published sequence, Constraint rows (selected) | One node kind, one sequence and one row form; spans nest; owner-local in Syntax and Binding. |
| Requirement as a bare child without a subject node, every consumer moved to rows | Rejected: a second clause shape with no place to record recovery, and three subject sources in the binder. |
| Header IsKoto owned by the declaration's list, referenced by the parameter | Rejected: overlapping sibling spans, a reference that goes stale under edits, and a site column for writers. |
| Binding merges the two syntax places for each consumer | Rejected: each consumer decides by syntax what the owner should publish. |
| Moving a later fragment's requirement onto the adopted parameter | Rejected: a child from another document under a parameter whose span does not contain it. |
| The repair writes `=> ()` for an emptied body | Rejected: the result depends on the file kind and the result Type; offering a repair is optional (SPEC §23.1.3), and migration does not need it. |
| Withholding the repair by comparing directive arms of the clause and its site | Rejected: needs arm identities in the collector; a directive inside the declaration withholds it, which is safe because offering a repair is optional. |
| A compile-time clause node separate from runtime `is`; one node for every `associate` form; `FunctionKoto`'s child slots in source order | Not GR's cause; open questions and issues (§7). |

### 3.4. Deletions, size and speed

- **Deleted:** the three published members and the kind switches in Hover and access; the inline placement decision in
  `ParseExecutableItem` and the member loop's order check for moved clauses; the whole-clause special cases of the seven
  consumers; the node scan in Callable selection; the `and` recursion and its special case in `AddConstraintFact`, and
  `and` entries among direct facts; the role recomputation at three sites; the `specialization` parameter of
  `ParseGenericArguments` (`allowLength` stays, within the one-`bool` limit); the region lines of every moved clause in
  §4.1's inputs (about 1,400). If R7 or R9 already removed one, GR0 records that instead of recreating it.
- **Added:** one child slot and its writer; one sequence; `ConstraintPlacement` with its collector; the
  supplying-fragment field; the row walk; one SyntaxForm row and one repair kind; three recovery fixes.
- **Effect:** about +250 lines in Syntax and a net reduction in Binding that depends on R7's shape; GR6 records the
  actual net size by area (DECISIONS Q5). Proofs are unchanged. One token test per generic parameter and one classifier
  call per clause are added; no measurable compile-time change is expected or claimed, and generated code is unchanged
  (§5).

## 4. Units after R9

All units follow [AGENTS](../../AGENTS.md), the post-R9 architecture and [VERIFICATION](../../docs/dev/VERIFICATION.md)
on the completed R9 tree, never retired Analysis/Emission or pre-consolidation Binding owners.

- **GR0 — Baseline and design gate**
  - Work: record R9's completion commit and this plan's Git blob (Verify excludes `draft/`). On a current build,
    reproduce `<T is R>`'s rejection; a clause-level result the conjunct unit changes (an `and` with a conjunct on an
    unbound slot; a merged Callable's related location); the role cases of GR1b; and §7's open issues. Re-run the census
    and recheck §4.1's counts. Rebase §3.1 onto the post-R7/R9 owners, including ContractSolver's input and fact
    representation as G170 C records them; if it already carries clause sources, GR1 adds only the conjunct source and
    role, and if R7 publishes per-conjunct rows, GR1 records that.
  - Exit: the rebased design is reviewed (review weight below) and the owner-approved design is recorded in DECISIONS as
    the DECISIONS Q6 basis for GR1 and the group: its contracts and the names of the rows, the child slot,
    `ConstraintClauses`, `ConstraintPlacement`, the supplying-fragment field, the SyntaxForm row and the repair kind.
- **GR1 — Constraint unit**
  - GR1a, behavior-preserving: rows, one per clause, with today's roles computed once; every judgment, premise and
    first-cause consumer reads rows.
  - GR1b, behavior change: rows per conjunct, deleting the `and` handling; roles per SPEC §6.1.4 (§2.5). Where GR0
    reproduces them, this changes, each with a regression that fails before:
    - a split-dependent role of `Self is Copy and Owned` in a Contract;
    - a Contract-parameter requirement mentioning `Self`, today an input condition (`Binding.Constraints.cs:478`);
    - the check of `Self`-rooted Contract clauses when a bound reference is formed
      (`Binding.Constraints.Substitution.cs:51-59`).
  - Exit: split and merged forms give equal records except §2.4's two whole-clause rules, with related spans at the
    conjuncts (today's Callable selection tests assert only counts); changed expectations cite §2.4; DIAGNOSTICS'
    Callable selection fact and its first-decisive-failure contract ("clause" becomes "Constraint", the conjunct
    related) and CODEMAP are updated.
- **GR2 — Syntax structure**
  - Work, behavior-preserving: extract the IsRequirement entry; parse specialization TypeArguments with the
    Type-argument parser, leaving one GenericParameters parser; create `ConstraintPlacement` with the
    misplaced-Constraint rule and the collector; publish `ConstraintClauses` and move every consumer to it; split files
    and members over the structure limits that GR3 grows; CODEMAP and the COMPILER_ARCHITECTURE canonical-component row.
  - Exit: syntax corpus and snapshots unchanged.
- **GR3 — Introduction-site form**
  - Syntax: the child slot, span, writer, sequence order, fragment rule and field, `length` keyword (the corpus row
    `length-as-generic-parameter-name` becomes a negative case citing F.3.2), the generic-closer rule, the three
    recovery fixes; `contract C<E is R>` and `=>` declarations.
  - Services: `OwnerOf`; Hover Details and identity with HOVER.md; name displays; DIAGNOSTICS' report-order contract
    (§2.4); the extension's grammar test rows.
  - Placement: the §2.2 classifier, without its report.
  - Both forms are accepted, on the group branch only.
  - Exit: §5's grammar, source, scope and Constraint-unit rows, two-site forms included, for every form and invalid
    counterpart.
- **GR4 — Migration and integration**
  - Precondition: the owner's integration instruction.
  - Work: every active input (§4.1) and the whole integration: §6's SPEC text and examples, the schema's repair-kind
    value (if the repair is approved, §2.8), the spec-cases keys, the milestone respellings, SETTLED's examples if
    instructed, and the update of this plan's INTEGRATED entry. The group review reviews it as a SPEC revision.
  - Exit: §5's equivalence row over every migrated source; counts reconcile with the census.
- **GR5 — Cutover**
  - Work: the classifier's report and repair (§2.8) and the directive rule (§2.7), so region clauses of §2.2's first two
    rows become errors and the two-form acceptance ends. The two directive tests change:
    `ExcludedSyntaxTest.SelectedFunctionArmsKeepTheLeadingConstraintRegion` becomes a negative case with the error in
    each arm, and `SpecConformanceParseTest.UnknownDirectiveDoesNotBecomeAnUnconditionalFunctionConstraint` expects it
    as well. DIAGNOSTICS' recorders and recovery rules and STATUS's boundary row.
  - Exit: no clause of §2.2's first two rows remains outside intended negative tests; corpus rows for each repair edit
    and withheld case; apply and recheck gives equal rows; excluded syntax reports the same code and location.
- **GR6 — Completion**
  - Work: §5's evidence; area sizes, hotspots, duplication candidates, net size by area, deletions and remaining
    uncertainty; the O2-corpus bridge (§5).
  - Exit: the final review passes.

- **Review weight.** A shared representation across Syntax, Binding and services takes the full process. This plan
  compared two independently written designs (§3.3); after the owner approves it, GR0 reviews the rebased design before
  coding. GR1 and GR2 each take one review and merge alone. GR3–GR5 form one group under DECISIONS Q6 form 2, sharing
  one review and merging into dev together, so dev never holds the two-form acceptance or a SPEC whose examples the
  compiler rejects. GR6 is the final review. A departure from §3.2, such as a new representation, component or
  vocabulary entry, takes the full process.
- **Records.** Each unit's commit adds the DECISIONS entry for what it first implements (GR1a and GR1b the rows, GR2 the
  sequence and `ConstraintPlacement`, GR3 the child slot and the supplying-fragment field, GR5 the SyntaxForm row and
  repair kind with the owner's approval) and updates the CODEMAP, DIAGNOSTICS, HOVER and STATUS facts it invalidates.
- **Holds.** Whichever of this plan and the As Naming plan starts a unit first records a hold (plan, branch, files) in
  PLAN and removes it when the unit or group merges; the other plan merges dev before its next change to a held file.

### 4.1. Migration

- **Inputs.** The Kimi library: 77 clauses, 66 of them in 60 functions whose regions become empty and 11 in 9 structs
  that keep their `Self is` clauses; of the 60, the 11 compiler signatures become header-only and the other 49 keep
  their items, so no library body needs `=> ()`. `KimiLibraryShapeTest`'s strings; `src/Benchmark` and
  `tests/Workloads`; `docs/examples`; milestone programs (28 clauses in 16 programs) with `tests/milestones/README.md`'s
  respelling record; `tests/mir` (goldens regenerated); about 1,250 clauses in C# test sources; the JSON corpora
  (`syntax.json` rows; `spec-cases.json` anchors of 13.5.5, 15.6.3, 15.6.4 and the two 7.2.3 line keys); SPEC examples
  (63 lines in 53 declarations). Counts are the census's (the draft's 71 clauses and 57 regions become 77 and 60) and
  are rechecked at GR0.
- **Method.** Edit identified clauses, never text matches. GR3's classifier, which GR5's report reuses, classifies each
  clause; a disposable rewriter outside the repository calls it in process and writes no second classifier. The rewriter
  joins a slot's clauses by §2.4's rule, inserts them at the slot, deletes the region lines, writes `=> ()` for an
  emptied executable body (header-only in compiler-signature files), and wraps a header over 120 columns as §2.9 shows.
  C# literals that hold whole declarations without interpolation are rewritten the same way; sources built by
  concatenation, interpolation or member fragments are listed and edited by hand. One reviewed manifest lists every
  edit, and a checker confirms that each hunk only deletes Constraint Clause lines and inserts their requirements at the
  introduction sites of the same declaration, adding nothing but `is`, §2.4's `and` and parentheses, `=> ()` for an
  emptied executable body, and §2.9's line breaks and indentation. No permanent migration tool ships.
- **Kept.** Region items that stay (§2.2), conditional members, `when` conditions, strings and comments, and frozen
  historical documents. Negative legacy cases become placement tests. Semantic expectations stay fixed; changed syntax
  expectations cite this plan.

## 5. Verification and performance

This plan is documentation only: diff, reference and independent review; no build or test.

| Rule or risk | Required evidence |
| --- | --- |
| Grammar and layout | Every production (function, member, requirement, struct, enum, contract); `>>` and `>>>` closers, including a CallableRequirement's (`<F is Callable<(i32) -> Box<i32>>>`); `during a and X`, `not`, `or`, parentheses, multiline lists with leading `and`/`or`, `=>` bodies, `{a}`, `:` and `(` after `>`; invalid `<s/T is R>`, `<length N is R>`, `<length>`, `<length is R>`, `<T is >`, `<T is A and>`, an effect line inside `<…>`, an over-indented continuation in a function list; specialization arguments unchanged. |
| Source and services | Parse, write and parse yields the same structure; Hover header `<T is R>`, hover on `T` shows the parameter, Details and identity include the requirement; spans of the parameter, subject and requirement; an edit of a requirement through its slot; the arity note of `struct Box<T is Copy>` equals that of `Box<T>`. |
| Placement | Each row of §2.2, including shadowed member parameters, pair binders, own, inherited and ambiguous associated Types, families, other qualifications, Contract parameters, function requirements (a clause on the Contract's parameter or associated Type: no placement report) and compiler-signature files; an effect-bearing Callable clause on a Contract parameter (only SPEC §8.11.6's error); directives (§2.7) in selected and excluded arms, and an `associate X` only in a directive arm with an unconditional `X is R` (the same report in either configuration); a struct clause on its own parameter after a member (no order warning; a later region clause still warned); one report per clause, never also the misplaced-Constraint error; no cascade at uses. |
| Repair | Insertion and `and` merge; each row of §2.4's table on either side, checked against the SPEC parse; withheld cases: a directive inside the declaration around the clause or an `associate X`, a bare name in a child Contract, a multi-line clause, a comment on the clause's line, an emptied body, recovery; offered: a whole declaration under a directive; apply and recheck accepts the result with equal rows; edits confined to the record's document. |
| Constraint unit and meaning | Split, merged and two-site forms give equal results for applicability, unbound slots (SPEC §10.8), obligations, function references, Callable candidates with receivers and split effect bounds (§2.3), roles, matching (SPEC §8.6.3), specialization, identity, collisions, API access, defaults and fragments; `Self is Copy and not ObjectPayload` stays an error; reports name the first decisive failing Constraint (Refuted before Unknown) in published order and relate its conjunct. |
| Scope | Nested-Type lookup (a nested `M` wins over a root `M`), later and same parameters, `Self`, Origins referenced and never introduced, an unbound Origin name, binding-set names in Callable requirements. |
| Equivalence of migrated sources | GR3's compiler, accepting both forms, compares old and new text of every migrated source: per declaration, equal multisets of (normalized proposition, role) and equal diagnostic codes; MIR dumps equal with SyntaxIds masked; native fixtures through `compare-fixtures.ps1` equal except Abort location text; diagnostic snapshots equal after each old range is mapped through the manifest's edit map, with a text difference allowed only where a record names a Constraint whose published order changed; any other difference stops GR4. This shows unchanged O2 generated code. |

- **Representative suites** at the baseline (use their post-R9 equivalents, rechecked at GR0): `ParserRegressionTest`,
  `HeaderDelimiterContinuationTest`, `LayoutRecoveryTest`, `ConstraintRecoveryTest`, `SpecConformanceParseTest`;
  `HoverSyntaxTest`, `SyntaxIdTest`; `ExcludedSyntaxTest`, `SyntaxDiagnosticTest`, `ContainerFragmentBindingTest`,
  `ConditionalMemberBindingTest`; `ConstraintBindingTest`, `CallableConstraintRecordTest`,
  `CallableSignatureSelectionTest`, `CallableEffectBoundTest`, `CallConstraintFailureTest`,
  `FunctionReferenceConditionTest`, `GenericIdentityAdaptationTest`; `VirtualAssociatedHeaderTest`,
  `AssociatedContractParameterTest`; `DiagnosticSnapshotTest`, `LspCodeActionTest`. Library changes add
  `KimiLibraryShapeTest`; editor rows use the extension's `grammar.test.ts`.
- **Performance.** `ParseBenchmark` (its source has no clauses, so it guards the unchanged path) and
  `FrontEndBenchmark`, pinned and interleaved A/B; warm and cold Bind time, allocations and retained memory at GR1 and
  GR3 on unchanged inputs; the existing `Purpose=Allocation` regressions unchanged, including
  `SyntaxIdTest.AWarmRebindAllocatesNothing` and the warm Callable, `TyCtxt` and generic-reference tests; after GR4 the
  migrated library and test sources make these and the Callable scaling tests cover `ConstraintClauses` and the conjunct
  walk. Cold parse allocation of the library with the sequence objects is measured and recorded. The O2-corpus series is
  bridged by builds that accept both forms: from the pre-GR checkout with B = the GR3 build, and from the GR5 checkout
  with A = the GR4 build, so O2 code and timings are compared on each side; the pipeline series cannot span GR4's source
  change, which is recorded, not a regression. No new benchmark.
- **Completion.** `scripts/verify.ps1 -Mode Session` with explicitly selected native checks, `KimiLibraryShapeTest` and
  the extension's workflow checks.
- **Unverified here.** The post-R7/R9 sites, the C# counts (heuristic), and the open issues of §7.

## 6. Integration map

Formal integration, when explicitly instructed, updates the authoritative definitions together, in GR4 (§4). Line
references are to `e307e9f9`.

| Plan scope | Specification targets |
| --- | --- |
| §2.1 concept and grammar | F.5 `GenericParameter` and its note; F.2.1's generic-closer rule ("Type arguments, generic parameters or a CallableRequirement") with the example `<F is Callable<(i32) -> i32>>`; F.3.2's `length` row (unchanged; the implementation now also rejects `<length>`); §8.1.1 (the requirement form); §8.2.2's first sentence (generic parameters among the compile-time `is` positions); §13.6.4's context list; Appendix E (a new row "Introduction-site requirement"; the "Constraint / Constraint Clause" row). |
| §2.2–§2.3 placement | §7.5 (placement by clause shape; "leading Constraints" becomes the declaration's Constraints; the misplaced-Constraint rule covers only the items that stay); §8.2.1 L72, L74 (the member-order rule covers region clauses only); §8.4.1 L190; §8.4.2 L210 and its example; §8.11.6 L841; F.6.2's placement note (L436) and F.6.3's note (L479); §6.1.4 L94; optionally §15.3.2 L257 and §15.3.3 L277 (their extra premises are `origin` relations, which stay). |
| §2.4 Constraint unit | §8.3.1 (a Constraint is a conjunct; grouping is notation except the whole-clause rules); §8.7.3's opt-out (unchanged, a whole-clause rule); §8.10 "Several Constraints" (Callable Constraints) with a two-site example; §8.11.6 L841 (unparenthesized); §10.5.2 L232, L234; §10.8 "Unbound slots"; Appendix E "Callable Constraint". The report order is DIAGNOSTICS', not SPEC's. |
| §2.5 meaning | §7.5 L258 and §9.1 L37 (requirements on generic parameters are Constraints, not Signature parts); §7.5 L260 and Appendix E "Conditional Member" (mentions, at either site); §6.1.4 (roles per conjunct); §8.5.2 L269; §9.4.4 L171; §9.5 (scope inside one parameter list; region lookup unlike the base clause); §15.3.4 (introduction-site requirements only reference the Origins visible in the Constraint region; the base-occurrence-set row also covers that fragment's requirements); §8.2.3 (identity over the set, error location, its example comment); §6.1.2 (fragment rule and its example). |
| §2.6 Contracts | §8.4.1, §8.5.1 (one concept; own associated Types, spelled as this plan's §2.2 second row lists, only at their declaration; no self-qualification), §8.5.3 (own versus inherited), §8.5.4 (a family's requirement at its declaration). |
| §2.7 directives | §19.1's table (requirements on ordinary Type slots are not directive-selected); §19.4's placement-rule list and its example (a new demonstration subject). |
| §2.8 diagnostics | §23.1.3 and the [check output schema](../../docs/spec/schemas/check-output.schema.json)'s repair-kind enumeration (one value; no version change). |
| §2.9 layout | F.4.3's first bullet; F.4.3's continuation bullet (`and`, `or` inside the list). |
| Examples | 63 lines in 53 declarations move: ch3 1, ch4 1, ch6 1, ch7 2, ch8 22, ch10 3, ch11 1, ch13 3, ch15 4, ch19 1, ch22 20, ch24 3, F 1. Rewrite the demonstrations whose point disappears (§7.5 and F.8 `pick`, §19.4 `process`); give §10.1's `keep` a body; keep §22.3.6's `offload` as the split example (this plan's §2.3). 76 lines stay: `Self` 43, conditional conformances 9, pairs 11, projections 6, effect-bearing Callable 5, conditional members 2. |

- **If the integration instruction includes it (recommended):** [SETTLED](../../docs/SETTLED.md)'s examples at L18-20
  and L200-203, which would otherwise show a rejected form (AGENTS: SETTLED changes need an explicit revision). The
  entry "`is` has several meanings" keeps its reason: its rejected fixes (separate spellings and a region introducer)
  are not adopted, and every requirement, `<T is R>` included, still uses `is`.
- **Explicit only:** [STYLE](../../docs/STYLE.md) (a rule for wrapping long generic parameter lists; examples and order
  rules at L89, L103-105, L138-140, L261-276, L287-288, L397-398). [LIBRARY](../../docs/LIBRARY.md) shows requirements
  as table suffixes (about 46 rows on own parameters); its display may stay. [GUIDE](../../docs/GUIDE.md) and
  [SPECJ](../../docs/SPECJ.md) keep the old form until an instructed update; since they would then teach a rejected
  form, the integration instruction should include them. SPECJ also embeds the repair-kind enumeration.
- **Implementation records** follow §4 (its Records bullet and the unit work); HOVER's update also fixes its stale
  §23.4.11 link.

## 7. Owner decisions and open issues

**Decisions requested.**

- **Additions.** Approve the rules this plan adds to the draft: Contracts' own associated Types and families (§2.6); the
  generic-closer extension (§2.1); the Constraint unit with its two whole-clause exceptions and report order (§2.4),
  which changes how today's implementation judges an existing `and` requirement (GR1b) so that grouping stays notation;
  effect lists only on an unparenthesized `Callable<…>` (§2.4); roles per conjunct, which corrects the role cases of
  GR1b, conditional members, name and Origin scope, the identity error location and fragments (§2.5); directives (§2.7);
  the single report and its repair (§2.8); layout and bodies (§2.9).
- **Effect-bearing parameters.** Confirm option (ii) (§2.3) or choose (i). Under (ii), a parameter with an
  effect-bearing Callable clause has its requirements in two places. Under (i), §2.2 gains a parameter-level rule, §2.8
  a second report and repair, and §3.2's classifier needs every clause of the parameter before judging one.
- **`length` keyword.** Follow F.3.2 as written (recommended), which rejects `<length>`; today's parser accepts it as a
  Type slot named `length`, reading SPEC §4.4.1's "a plain `<N>` is a Type slot", and the corpus row
  `length-as-generic-parameter-name` asserts that. The alternative clarifies F.3.2 to "when a Name follows", under which
  `<length>` stays a Type slot and `<length is R>` becomes a slot named `length` with a requirement; §2.1, GR3, §5 and
  §6 would change with it.
- **Vocabulary.** One SyntaxForm row and one repair kind (§2.8); the plan recommends both.
- **Scope of the integration instruction.** Whether it includes SETTLED's two examples (recommended), STYLE, LIBRARY's
  display, GUIDE and SPECJ (§6).

**Answers to the draft's questions.** (1) Several Callable Constraints joined by `and`: §2.4 (equal candidates; three
whole-clause reports differ until GR1). (2) Line continuation inside `<…>`: §2.9. (3) A Contract's own associated Types:
§2.6. (4) Effect-bearing parameters: §2.3. (5) Directives: §2.7. (6) Unchanged forms and placement-independent rules: §1
and §2.5.

**Open questions, not decided here.**

- **Inherited associated Types and other qualifier spellings.** Both a region clause and `associate Parent.X is R`
  constrain an inherited associated Type today, and a region clause may still reach an own associated Type through
  another spelling of the Contract's own qualifier (§2.6); a separate proposal should choose one form for each.
- **Identity between two introduced names.** An identity's subject is the writer's choice, so `T is U` and `U is T` go
  to different sites (`<T is U, U>` or `<T, U is T>`; in a Contract `contract C<Key is Element>` or `associate Element
  is Key`). Choosing a canonical side is outside this plan; for a Contract the associated declaration is the candidate,
  since associated-Type inference reads only that side today (below).
- **Representation.** One `IsKoto` serves the runtime `is` test and compile-time clauses, and `associate X is R` is both
  a Contract declaration and a conformance specification; a dedicated compile-time clause node, or one node for every
  `associate` form, needs its own design.

**Open issues found by this plan's research.** They are not part of this plan. Probes ran on the Release build of
2026-10-17 (the last Parser and Lexing changes precede it; Binding has changed since), so each needs a check on the
current tree before it is recorded in PLAN.

| Issue | Evidence |
| --- | --- |
| SPEC §8.2.3 identity cycles and conflicts are not detected; calling the cyclic example overflows the stack (an internal fault). | `Binding.ContractType` recursion; no cycle check in source. |
| A parenthesized requirement with a leading `not` is read as negating the whole parenthesized requirement, so `(not A or B)` means `not (A or B)`, contrary to F.6.2's RequirementUnary. | `Parser.cs:2334-2342` calls `ParseCondition` |
| A function requirement's region accepts a clause on its Contract's parameter or own associated Type, contrary to SPEC §8.4.2; the clause becomes an obligation at calls through the requirement. | `Binding.Constraints.cs:482` (`validSubject`) |
| Inside Contract `C`, `associate C.X is R` is accepted for a non-generic `C` and reports `PrerequisiteUnavailable_Kd` for a generic one, contrary to SPEC §8.5.1. | probes |
| Associated-Type inference collects identity atoms only by subject: `Key is Element` does not bind `Element`, `Element is Key` does. | `Binding.AssociatedTypes.cs:468-469` |
| A recovered misplaced clause also reports `InvalidConstraint_Kd` at calls, not a derived record. | `Binding.Constraints.cs:420-427`; DIAGNOSTICS §4.3 |
| An identity requirement carrying an Origin (`T is ref/i32 during y`) cannot be proven at a call. | probe |
| Tokenizer: a non-adjacent declaration `<` continues only when its line ends after it, and `contract` is missing from the declaration contexts; expression Type arguments may span lines (F.9.3); a one-level over-indented `->` line inside a delimiter is accepted. | `Tokenizer.cs:1075-1086`, `:1245`, `:861-865` |
| `FunctionKoto`'s child slots visit the base initializer, clauses and effect bounds before the generic parameters, contrary to the source-order contract; fixing it renumbers every golden. | `FunctionKoto.cs:506-511`; `ChildSlots.cs:13` |
| The Kimi library's compiler-signature files accept a bodyless header with an indented region, a form F.6.3 does not describe. | `KimiLibrarySources.cs:162` |

## 8. Principles

| Principle | Application |
| --- | --- |
| One Concept, One Canonical Form | A requirement on an introduced Type name has one canonical site, shared by Type slots and Contract associated Types; every clause has exactly one place, decided by its own shape; several requirements on a slot are one `and` requirement in one canonical form. |
| Local Reasoning | A slot's requirements are beside the slot, except an effect-bearing Callable clause, which stays in the region with its bounds; scope, roles and Origins are those of the region, stated once; a Constraint is judged per conjunct, so grouping is notation except for §2.4's two whole-clause rules. |
| Explicit Semantics | Requirements stay written; effect bounds stay attached to their Callable clause; a configuration-dependent contract is written as separate declarations. |
| Compiler Server Protocol | One located report with a related parameter; a repair whose edits, absence of conditions and withholding cases follow from the rules, and whose result is rechecked; writers reproduce each requirement where it was written. |
