# Appendix F. Syntax summary

[Specification index](../../SPEC.md)

**Non-normative syntax reference.** This appendix collects the specified forms; the linked language sections remain authoritative. It is not a standalone parser-generator grammar. Context-sensitive placement, token boundaries, and the open productions listed in F.9 remain part of the syntax definition; an open production does not accept arbitrary text.

In the EBNF below, quoted text is literal syntax, `|` is choice, parentheses group, and `?`, `*`, `+` mean optional, zero or more, and one or more. `? description ?` denotes a lexical class or a production whose definition is linked. `List<X>` abbreviates `X ("," X)*`; `TrailingList<X>` adds an optional final comma. These angle brackets are grammar notation, unlike quoted `"<"` and `">"`.

`NEWLINE`, `INDENT`, `DEDENT`, and `SEP` denote source-layout events under [source structure](../02-source-and-lexical-structure.md#22-lines-indentation-and-continuation), not required internal token kinds. Layout within delimiters and between branch clauses follows the linked constructs. `IndentedList<X>` abbreviates `NEWLINE INDENT ItemList<X> NEWLINE? DEDENT`; executable `Body<X>` additionally permits a single item after `=>` (F.5). `ItemList<X>` is a nonempty sequence separated where the surrounding grammar permits. Declaration Containers may additionally have empty bodies where their own rules permit them.

## F.1. Lexical grammar

[Source and layout](../02-source-and-lexical-structure.md#2-source-and-lexical-structure), [Names and keywords](../02-source-and-lexical-structure.md#25-names), [numeric literals](../02-source-and-lexical-structure.md#26-number-literals), [escapes](../02-source-and-lexical-structure.md#27-character-escapes), [character literals](../02-source-and-lexical-structure.md#28-character-literals), [strings](../02-source-and-lexical-structure.md#29-string-literals).

```ebnf
Name                 := NameStart NameContinue*

Punctuator           := "(" | ")" | "[" | "]" | "," | ";" | "." | ":" | "::"
                      | "->" | "=>" | "@" | "#" | "$" | "?"
                      | "+" | "-" | "*" | "/" | "%" | "++" | "--"
                      | "=" | "+=" | "-=" | "*=" | "/=" | "%="
                      | "==" | "!=" | "<" | "<=" | ">" | ">="
                      | "&" | "|" | "^" | "<<" | ">>" | "&=" | "|=" | "^="
                      | "<<=" | ">>=" | ".." | "..="
                      | "{" | "}" | "!" | "&&" | "||"
NameStart            := "A".."Z" | "a".."z" | "_"
                      | ? Unicode Lu, Ll, Lt, Lm, Lo, or Nl ?
NameContinue         := NameStart | "0".."9" | ? Unicode Mn, Mc, Nd, or Pc ?
PhysicalNewline      := LF | CR LF | CR
LineComment          := "//" ? text up to a physical newline or EOF ?
// Eligible whole-line /// comments carry documentation under §2.3.1–6;
// they add no executable tokens or declaration grammar productions.
BlockComment         := "/*" ? text up to the first closing delimiter ? "*/"
CharacterEscape      := "\0" | "\\" | "\e" | "\t" | "\n" | "\r"
                      | '\"' | "\'" | "\u(" HexDigits1To6 ")"
HexDigits1To6         := ? one to six ASCII hexadecimal digits ?
EscapedString        := '"' (StringText | CharacterEscape | Interpolation)* '"'
Interpolation        := "\(" Expression ")"
StringText           := ? literal text excluding unescaped quote and backslash ?
RawString            := QuoteRun RawText QuoteRun
QuoteRun             := ? matching run of N double quotes, N >= 3 ?
RawText              := ? raw content delimited by that QuoteRun ?
Literal              := number-literal | CharLiteral | EscapedString | RawString
                      | "true" | "false" | "null" | "(" ")"
```

```text
number-literal       := decimal-literal
                      | binary-literal
                      | octal-literal
                      | hexadecimal-literal

decimal-literal      := decimal-sequence fraction? exponent?
fraction             := '.' decimal-digit decimal-tail
exponent             := ('e' | 'E') ('+' | '-')? decimal-digit decimal-tail

binary-literal       := '0' ('b' | 'B') '_'* binary-digit binary-tail
octal-literal        := '0' ('o' | 'O') '_'* octal-digit octal-tail
hexadecimal-literal  := '0' ('x' | 'X') '_'* hexadecimal-digit hexadecimal-tail

decimal-sequence     := decimal-digit decimal-tail
decimal-tail         := (decimal-digit | '_')*
binary-tail          := (binary-digit | '_')*
octal-tail           := (octal-digit | '_')*
hexadecimal-tail     := (hexadecimal-digit | '_')*

decimal-digit        := '0' .. '9'
binary-digit         := '0' | '1'
octal-digit          := '0' .. '7'
hexadecimal-digit    := decimal-digit | 'a' .. 'f' | 'A' .. 'F'
```

```text
CharLiteral = "'" (DirectScalar | CharacterEscape) "'"
```

`DirectScalar` is the character class defined in [character content and validation](../02-source-and-lexical-structure.md#281-content-and-validation); reserved-word/standalone-underscore exclusions and contextual Name roles follow [Names](../02-source-and-lexical-structure.md#25-names).

## F.2. Type grammar

[Type composition](../03-types-and-values.md#3-types-and-values), [compound Types](../03-types-and-values.md#32-compound-types), [Semantics](../03-types-and-values.md#33-type-semantics), [generic application](../12-expressions.md#1242-invocation-and-generic-application), [Origins](../15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations).

```ebnf
Type                 := FunctionType | AnnotatedType
AnnotatedType        := SemanticsType ("?")* BorrowOrigin?
FunctionType         := FunctionParameters "->" (Type | PlaceResult)
FunctionParameters   := "(" TrailingList<Type>? ")"
SemanticsType        := Semantics "/" SemanticsType | TypeAtom
TypeAtom             := CoreType | "(" Type ")"
ObjectSemantics      := "obj" | "rc" | "arc" | "objref" | "objuniq"
RuntimeContractType  := ContractReference
CoreType             := NamedType | UnitType | TupleType | FixedArrayType
FixedArrayType       := "[" ArrayLength "of" ArrayElementType "]"
ArrayElementType     := Type | "_"
ArrayLength          := IntegerLiteral | ConstantName | "(" LengthExpression ")"
ConstantName         := "::"? Name ("." Name)*
LengthExpression     := LengthProduct (("+" | "-") LengthProduct)*
LengthProduct        := LengthUnary (("*" | "/" | "%") LengthUnary)*
LengthUnary          := ("+" | "-") LengthUnary | IntegerLiteral
                      | ConstantName | "(" LengthExpression ")"
UnitType             := "(" ")"
TupleType            := "(" Type "," TrailingList<Type>? ")"
PlainContainerPath   := "::"? TypeSegment ("." TypeSegment)*
BoundContainerQualifier := "(" ContainerPath OriginBindingSet ")"
PathSuffix           := "." TypeSegment | "." "(" ContractReference ")" "." Name
ContainerPath        := "::"? TypeSegment PathSuffix* | BoundContainerQualifier PathSuffix+
PlainNamedType       := ContainerPath
NamedReference       := PlainNamedType OriginBindingSet?
ContainerReference   := NamedReference
NamedType            := NamedReference OriginApplication?
OriginApplication    := "(" List<OriginAtom> ")" // Only after an associated-Type name, §8.4.3.1.
TypeSegment          := TypeName TypeArguments?
TypeName             := Name | PrimitiveType | "Self"
TypeArguments        := "<" TrailingList<GenericArgument> ">"
GenericArgument      := Type | ArrayLength
PrimitiveType        := "isize" | "usize" | "i8" | "i16" | "i32" | "i64" | "i128"
                      | "u8" | "u16" | "u32" | "u64" | "u128"
                      | "f32" | "f64" | "bool" | "char" | "string"
Semantics            := "owner" | "ref" | "uniq" | "obj" | "rc" | "arc"
                      | "objref" | "objuniq" | "unsafe" | Name
OriginHeader         := "{" TrailingList<Name>? "}" // struct/enum only, closed schema.
OriginBindingSet     := "{" Name ","? "}" // Introduces a fresh set name.
BorrowOrigin         := "during" OriginAtom
OriginExpression     := OriginAtom ("and" OriginAtom)*
OriginAtom           := Name ("." Name)? | "static" | "(" OriginExpression ")"
OriginRelation       := "origin" OriginExpression ("==" | "outlives") OriginExpression
OriginClauses        := IndentedList<OriginRelation>
GenericParameters    := "<" TrailingList<GenericParameter> ">"
GenericParameter     := NamedParameter | PairParameter | LengthParameter
NamedParameter       := Name
PairParameter        := Name "/" Name
LengthParameter      := "length" Name
```

`CoreType` and `NamedCoreType` remain grammar production names. They describe syntactic forms, not the full classification of Cores in §3; Function Types use a separate production, and generated callable Cores have no declaration spelling.

Object-target syntax uses the View Target lookup role; in the [runtime Contract extension](../08-generics-constraints-and-contracts.md#85-runtime-contracts), a named target may resolve to a valid `RuntimeContractType` instead of a Core. Runtime designation and View bindings are not supplied by this grammar. A bare Contract is not a value Type. This shared syntax permits no arbitrary Object Semantics around an already Semantics-applied Type. Layer legality and Origin attachment follow [nested Semantics](../03-types-and-values.md#336-nested-semantics-and-type-grouping). `NamedType` also preserves dotted associated-Type projection syntax; its Contract and Core roles are resolved under F.3. Callable signature syntax appears with requirements below; generated Closure and Function Item Types have no source declaration spelling.

GenericParameters and TypeArguments are nonempty and allow trailing commas. NamedParameter and PairParameter declare Type slots; only LengthParameter declares a function length slot. A pair consumes one complete Type argument and is recognized only by declaration-side `Name / Name`. Preserve syntactically ambiguous Name/grouped GenericArguments until Binding checks their declared slot kind under [length parameters](../04-arrays-indexing-and-slices.md#44-function-length-parameters); do not infer slot kinds from uses. `of` is contextual only after ArrayLength as the element delimiter. `_` as ArrayElementType is allowed only in a local binding annotation with an initializer. LengthParameter is forbidden on Type declarations. Standalone Semantics slots, general Const arguments, partial/default/variadic arguments, and other `_` Type arguments are not introduced; Origin binding sets and relations follow §15.3–4.

## F.3. Declaration grammar

[Containers](../06-declarations-and-containers.md#61-declaration-containers), [structures](../06-declarations-and-containers.md#62-structure-declarations), [enums](../06-declarations-and-containers.md#63-enums), [Constraints](../08-generics-constraints-and-contracts.md#82-constraints), [bindings](../06-declarations-and-containers.md#64-bindings), [functions](../07-functions-and-callable-values.md#7-functions-and-callable-values), [parameters](../07-functions-and-callable-values.md#72-parameters-and-defaults), [aliases](../18-modules-and-dependencies.md#181-external-references-and-aliases).

```ebnf
QualifiedName        := Name ("." Name)*
Access               := "private" | "internal" | "public" | "protected"
                      | "protected" "internal" | "private" "protected"
AliasDeclaration     := "alias" (Name "=>")? ContainerReference OriginClauses?
LocalBinding         := ("let" | "var") Name (":" Type)? ("=" Expression)? OriginClauses?
GroupDeclaration     := Access? "group" Name ContainerBody
RootGroupDeclaration := Access? "rootgroup" QualifiedName ContainerBody
StructureDeclaration := Access? "open"? "struct" Name GenericParameters?
                        OriginHeader? BaseClause? ContainerBody
BaseClause           := ":" NamedReference
EnumDeclaration      := Access? "enum" Name GenericParameters? OriginHeader?
                        IndentedList<EnumItem>
EnumItem             := EnumCase | AttributedFunctionDefinition | SpecializationDeclaration | ConstraintClause | OriginRelation
                      | AssociatedTypeSpecification | ConditionalConformance
                      | Directive<EnumItem>
EnumCase             := Name ("(" TrailingList<Type> ")")? OriginClauses?
ContractDeclaration  := Access? "contract" Name GenericParameters? ContractParentList? IndentedList<ContractItem>?
ContractParentList   := ":" ContractReference ("," ContractReference)*
ContractReference    := ContainerReference
ContractSelector     := ContainerPath | "(" ContractReference ")"
ContractItem         := ContractRequirement | AssociatedTypeDeclaration
                      | ConstraintClause | Directive<ContractItem>
ContractRequirement  := FunctionRequirement | PropertyRequirement
FunctionRequirement  := "unsafe"? "func" Name GenericParameters?
                        ParameterList<RequirementParameter> ("->" FunctionResult)?
                        RequirementConstraints?
FunctionResult       := Type | PlaceResult
PlaceResult          := "place" "(" ("ref" | "uniq") "," Type ")" BorrowOrigin?
RequirementParameter := ParameterName ":" Type | ReceiverShorthand
RequirementConstraints := IndentedList<ConstraintClause | OriginRelation>
PropertyRequirement  := "property" Name ":" Type
                        ("has" RequiredAccessor ("," RequiredAccessor)*
                         | IndentedList<RequiredSignature>)
RequiredAccessor     := "get" | "set"
RequiredSignature    := (GetterSignature | SetterSignature) OriginClauses?
AssociatedTypeDeclaration := "associate" Name OriginParameters? ("is" IsRequirement)?
                             IndentedList<WellformedClause | OriginRelation>?
OriginParameters     := "(" List<Name> ")"
WellformedClause     := "wellformed" Type
AssociatedTypeSpecification := "associate" AssociatedTypeName OriginParameters? "is" IsRequirement OriginClauses?
AssociatedTypeName   := Name | ContractSelector "." Name
AssociatedTypeReference := TypeQualifier "." Name OriginApplication?
                        | TypeQualifier "." "(" ContractReference ")" "." Name OriginApplication?
TypeQualifier        := ContainerPath
ConformanceClause    := "Self" "is" ContractReference
ConditionalConformance := "Self" "is" ContractReference "when" ConformanceConditions
                          IndentedList<ConditionalImplementationItem>?
ConformanceConditions := ConformanceCondition ("," ConformanceCondition)*
ConformanceCondition := ConstraintSubject "is" ConformanceRequirement
ConformanceRequirement := ConformanceRequirementAtom ("and" ConformanceRequirementAtom)*
ConformanceRequirementAtom := CallableRequirement | Type | Semantics | SemanticsCategory
                           | "(" ConformanceRequirement ")"
ConditionalImplementationItem := AttributedFunctionDefinition
                               | AttributePrefix? ComputedDeclaration
                               | AssociatedTypeSpecification
                               | Directive<ConditionalImplementationItem>
ConstructorDeclaration := Access? "init" ParameterList<Parameter>
                          BaseInitializer? ExecutableBody
BaseInitializer      := ":" "base" "(" TrailingList<Argument>? ")"
FunctionHeader       := Access? "unsafe"? "func" Name
                        GenericParameters?
                        ParameterList<Parameter> ("->" FunctionResult)?
FunctionDefinition   := FunctionHeader FunctionBody
AttributedFunctionDefinition := AttributePrefix? FunctionDefinition
ForeignFunctionDeclaration := FunctionHeader
// Only with the LibraryImport prefix and restricted header/placement in §22.3.
SpecializationDeclaration := "specialize" "func" Name TypeArguments
                            "(" TrailingList<SpecializationParameter>? ")"
                            ("->" FunctionResult)? SpecializationBody
SpecializationParameter := Name ("=>" Name)? ":" Type | ReceiverShorthand
SpecializationBody   := ExecutableBody
FunctionBody         := Body<FunctionItem>
Parameter            := Attribute* ParameterCore
ParameterCore        := ParameterName ":" Type ("=" Expression)?
                      | ReceiverShorthand
ParameterName        := Name ("=>" Name)?
ParameterList<P>      := "(" (List<P> ","? | List<P>? "!" List<P> ","?)? ")"
ReceiverShorthand    := "self"
ConstraintClause     := ConstraintSubject "is" IsRequirement
ConstraintSubject    := Name | "Self" | AssociatedTypeReference
IsRequirement        := "not" Requirement | PositiveRequirement
PositiveRequirement  := RequirementAtom ("and" RequirementUnary)*
                        ("or" RequirementAnd)*
Requirement          := RequirementAnd ("or" RequirementAnd)*
RequirementAnd       := RequirementUnary ("and" RequirementUnary)*
RequirementUnary     := "not" RequirementUnary | RequirementAtom
RequirementAtom      := CallableRequirement | Type | Semantics | SemanticsCategory
                      | "(" Requirement ")"
SemanticsCategory    := "value" | "valueborrow" | "object" | "objectborrow"
                      | "borrow" | "owning" | "reference"
CallableRequirement  := "Callable" "<" (CallableReceiver ",")? FunctionSignature ">"
CallableReceiver     := "ref" | "uniq" | "owner"
FunctionSignature    := FunctionParameters "->" Type
FunctionItem         := Expression | LocalBinding | AttributedFunctionDefinition
                      | Statement | ConstraintClause | OriginRelation | Directive<FunctionItem>
ContainerItem        := Declaration | ConstraintClause | OriginRelation | AssociatedTypeSpecification
                      | ConditionalConformance
                      | Directive<ContainerItem>
ContainerBody        := ? optional indented ContainerItem sequence for group/rootgroup/struct,
                         with omission and emptiness governed by §6.1.1 ?
Attribute            := "#" AttributeName ("(" TrailingList<Argument>? ")")?
AttributeName        := ? Name beginning with an uppercase Unicode letter, §6.5 ?
AttributePrefix      := Attribute (Attribute | NEWLINE)*
Declaration          := AttributePrefix? UnattributedDeclaration
UnattributedDeclaration := GroupDeclaration | RootGroupDeclaration
                      | StructureDeclaration | ContractDeclaration
                      | FunctionDefinition | SpecializationDeclaration | StoredPropertyDeclaration | ComputedDeclaration
                      | ConstructorDeclaration | DeinitDeclaration
                      | EnumDeclaration | ForeignFunctionDeclaration
```

Modifier placement and compound-access combinations are constrained by [accessibility](../09-names-signatures-and-access.md#93-accessibility-and-reachability), even where the shared grammar uses `Access`.

`ReceiverShorthand` is permitted only for an instance function receiver under [§7.3](../07-functions-and-callable-values.md#73-explicit-receivers), including Contract requirements and full specializations. It expands to `self: ref/Self` at its written position. The shared Parameter grammar does not permit it on constructors, local functions, or group/rootgroup functions. The functions with receivers in one member group share one receiver shape, and a Receiver Expression is acquired without a spelling; both are semantic rules of §7.3, not grammar.

AttributePrefix is allowed only on the declarations and parameters enumerated in §6.5; the shared Declaration wrapper does not authorize Attributes on constructors, deinit, or explicit specializations. Attribute-bearing functions in executable/enum lists use AttributedFunctionDefinition. ForeignFunctionDeclaration requires exactly the LibraryImport form of §22.3 and cannot appear in those lists. `open` applies only to structures. `BaseClause` has the semantic restrictions in [inheritance](../06-declarations-and-containers.md#622-inheritance-and-open-structures); unavailable declaration modifiers use §2.5.1's diagnostic recognition, not grammar productions.

`SpecializationDeclaration` is permitted only in the original generic function's declaration Container and Kotonoha. It adds no Type or Origin binder, Constraints, access/unsafe modifiers, attributes, defaults, or `!` boundaries. Its body uses ordinary executable syntax; the original function's contract is inherited under [full specialization](../08-generics-constraints-and-contracts.md#88-explicit-full-function-specialization). Written Type arguments must be closed after normalization, except for Origins governed by that inherited contract.

Omitting the result annotation in a named function declaration or Contract function requirement means Unit; the optional grammar does not authorize body-based return inference. Anonymous functions retain their own inference rules.

Ordinary parameter bindings are immutable under §7. A ParameterList permits at most one `!`, with no adjacent comma and at least one ordinary parameter on its right. Its external names are unique across both sections and the receiver. Receiver recognition uses the internal Name self, its position, and declaration context under §7.3; the shared Parameter production alone does not grant receiver defaults, renaming, or arbitrary Types. Default evaluation and cleanup follow §7.2. Empty group/rootgroup/struct/contract declarations follow §6.1.1; empty enums remain invalid.

Contract function requirements permit the `!` boundary but have no parameter defaults, access modifiers or executable bodies. Property requirements have no parameter defaults, `!` boundaries, initializers, access modifiers or executable bodies. `RequirementConstraints` is an optional nonempty indented list of Constraint Clauses; method generic parameters and implicit signature Origins follow ordinary function rules. Property Requirements are instance-only: get is mandatory and set optional, each at most once in either order. No accessor is implied beyond the written list or explicit signatures. Shared/exclusive defaults for has and explicit signature checks follow §11.4.

`AssociatedTypeDeclaration` introduces a name only inside a Contract, optionally with Origin parameters and attached `wellformed` or `origin` clauses (§8.4.3.1); `wellformed` is contextual only there. `AssociatedTypeSpecification` requires an existing associated Type of the enclosing Type's declared or implied conformances and repeats its Origin parameter count; a bare `associate Element` is not a specification. `OriginApplication` follows only an associated-Type name in Type context and is never a Type grouping, Tuple or call. `PlaceResult` is recognized only in result position when `place` is immediately followed by `(` (§7.1.1); anonymous functions use it only with an explicit result annotation, and Function Types accept it as their result. `ConformanceClause` is the Type-body interpretation of an unconditional Constraint Clause. `ConditionalConformance` instead occupies a member position only in generic struct/enum bodies (§8.4.8), has one target Contract, and introduces no namespace or generic binders. Its positive-only condition grammar does not change PositiveRequirement. Enum conditional blocks reject computed declarations; all blocks reject storage, Cases, constructors, deinit, nested Types, and nested conformances. Intrinsics retain their own rules.

Constraint subjects follow their declaration context. In a nested Contract, conditions on inherited arguments are reference inputs, conditions on conforming Self are implementation requirements, and closed conditions are declaration obligations (§6.1.3.1). Functions constrain their generic parameters and projections rooted in them; Types use their ordinary Constraints and conformance rules. These productions do not broaden `#if`/`#case` Conditions.

`ContractReference` resolves a user-defined Contract with all inherited bindings; a Contract may declare its own Type parameters (§8.4) but no Origin or length parameters. Built-in parameterized requirements have separate productions. `TypeQualifier` must resolve to a Core, parameter, `Self`, or associated-Type projection, not a value or Semantics-applied expression. The Parser preserves bound paths and distinguishes declarations, specifications, Type positions, and Constraints-only regions by context. Binding resolves the Contract/associated-Type roles and rejects distinct successful interpretations; expected results and value-member fallback cannot disambiguate them. Apply the same rules after ordinary compile-time selection.

`ConstructorDeclaration` and `DeinitDeclaration` are allowed only directly in structure bodies, subject to their merging and selection rules. A constructor has a Unit executable body but produces an owned structure through its dedicated construction operation. Neither declaration is an ordinary function declaration; constructors retain containing-Type Origins and may introduce implicit per-call scalar Origins in borrow annotations. See [constructors](../06-declarations-and-containers.md#623-constructors) and [destruction declarations](../16-scope-exit-and-destruction.md#163-aggregate-destruction-and-deinit).

Alias targets follow [§18.1](../18-modules-and-dependencies.md#181-external-references-and-aliases): `ContainerReference` is an optionally root-qualified reference valid under the Container rules, with their required arguments. A named target must be a Kotonoha or group. `=>` in this production introduces a reference, never an executable Body. There is no `alias Name = Type` production.


## F.4. Expression grammar

[Primary forms](../12-expressions.md#1232-names-literals-and-grouping), [calls](../12-expressions.md#1242-invocation-and-generic-application), [precedence](../13-operators-and-assignment.md#131-precedence-and-associativity), [runtime type tests](../13-operators-and-assignment.md#1361-runtime-is-tests), [explicit operations](../13-operators-and-assignment.md#135-explicit-operations), [assignment](../13-operators-and-assignment.md#137-assignment).

```ebnf
Expression           := Assignment
Assignment           := RangeExpression (AssignmentOperator Assignment)?
AssignmentOperator   := "=" | "+=" | "-=" | "*=" | "/=" | "%="
                      | "<<=" | ">>=" | "&=" | "^=" | "|="
RangeExpression      := OrExpression
                      | OrExpression? ".." OrExpression?
                      | OrExpression? "..=" OrExpression
OrExpression         := AndExpression ("or" AndExpression)*
AndExpression        := Comparison ("and" Comparison)*
Comparison           := BitOr (("<" | "<=" | ">" | ">=" | "==" | "!=") BitOr)?
                      | BitOr "is" "not"? NamedCoreType
OriginFreePath       := ? ContainerPath with no written direct borrow annotations at any layer ?
NamedCoreType        := OriginFreePath
BitOr                := BitXor ("|" BitXor)*
BitXor               := BitAnd ("^" BitAnd)*
BitAnd               := Shift ("&" Shift)*
Shift                := Additive (("<<" | ">>") Additive)*
Additive             := Multiplicative (("+" | "-") Multiplicative)*
Multiplicative       := Try (("*" | "/" | "%") Try)*
Try                  := "try" Try | Adapted
Adapted              := Prefix ("@" OperationTarget PostfixSuffix*)*
OperationTarget      := "move" | Semantics | AdaptationType
// "@" "deref" is a PostfixSuffix (level 1), never an OperationTarget.
AdaptationType       := AdaptationCore ("?")*
AdaptationCore       := Semantics "/" AdaptationCore | AdaptationAtom
AdaptationAtom       := ContainerPath | UnitType | "(" Type ")"
                      | "(" Type "," TrailingList<Type>? ")"
                      | "[" ArrayLength "of" Type "]"
Prefix               := ("+" | "-" | "not" | "*" | "^" | "++" | "--") Prefix
                      | Postfix
Postfix              := Primary PostfixSuffix*
PostfixSuffix        := "." (Name | DecimalTupleIndex)
                      | "." "(" ContractReference ")" "." Name
                      | "(" TrailingList<Argument>? ")"
                      | AdjacentTypeArguments | "[" Expression "]" | "++" | "--"
                      | "@" "deref"
AdjacentTypeArguments := ? TypeArguments adjacent to an eligible Name, §12.4.2 ?
DecimalTupleIndex    := ? decimal integer literal used as a Tuple member, §12.4.1 ?
ConstantIndexExpression := IntegerLiteral | "(" ConstantIndexExpression ")"
// Static fixed-array path recognition only, not a restriction on ordinary indexing (§15.1.3).
Argument             := (Name ":")? Expression
Primary              := "::"? Name | Literal | "(" Expression ")" | TupleExpression
                      | BoundContainerExpression
                      | ArrayExpression | DictionaryExpression | FunctionExpression
                      | IfExpression | MatchExpression | LabeledSelection | Iteration | DoExpression
                      | Transfer | CompositionRootExpression | ConstructionExpression
                      | InferredCaseExpression
BoundContainerExpression := BoundContainerQualifier
                           ("." Name | "." "(" ContractReference ")" "." Name)
ConstructionQualifier := PlainNamedType | BoundContainerQualifier
ConstructionExpression := ConstructionQualifier "." "init"
                          "(" TrailingList<Argument>? ")"
InferredCaseExpression := "." Name ("(" TrailingList<Expression> ")")?
TupleExpression      := "(" Expression "," TrailingList<Expression>? ")"
ArrayExpression      := "[" TrailingList<Expression>? "]"
                      | "[" ArrayLength "of" Expression "]"
DictionaryExpression := "[" ":" "]" | "[" TrailingList<DictionaryEntry> "]"
DictionaryEntry      := Expression ":" Expression
FunctionExpression   := "func" CaptureList? "(" TrailingList<AnonymousParameter>? ")"
                        ("->" FunctionResult)? AnonymousBody
AnonymousParameter   := Name (":" Type)?
AnonymousBody        := ExecutableBody
CaptureList          := "[" TrailingList<Capture>? "]"
Capture              := Name ("@" CaptureOperation)? | "var" Name ("@" "move")?
CaptureOperation     := "move" | "ref" | "uniq"
CompositionRootExpression := "$" "abort" "(" Expression ")"
                           | "$" "tryWrite" "(" Expression "," StringLiteral ")"
```

Ordinary `is` / `is not` accepts one named struct Core and does not consume outer `and` / `or`. The separate compile-time [requirement expressions](../08-generics-constraints-and-contracts.md#83-requirement-expressions) retain their existing extent in their dedicated contexts. Anonymous parameter/result omission and Capture Lists obey [function-expression rules](../07-functions-and-callable-values.md#76-function-expressions). Adaptation-target parsing and generic/comparison disambiguation follow [precedence](../13-operators-and-assignment.md#131-precedence-and-associativity); `@deref` binds as a postfix operation, takes no Type, `?` or `during`, and a following `/` is division. These boundaries are not alternative parses selected by conversion success.

Qualified enum Case expressions have no separate Primary production: ordinary Postfix syntax is classified during Binding under §6.3.2. Only InferredCaseExpression is a dedicated expression production; CaseReference belongs to the Pattern grammar in F.5.

The `.init(` suffix has construction priority under §6.2.3 and cannot use ordinary member `Name` in `PostfixSuffix` or `BoundContainerExpression`; its qualifier is checked in the Type role during Binding. Adaptation alternatives obey §13.5.1's syntactic prefix commitment and Origin restrictions: after optional expansion, the outer Semantics chain cannot write borrow Origins, while complete payload Types retain their annotations. Bound Container qualifiers remain unavailable in adaptation targets. `$abort` accepts exactly one positional Expression, without a label or trailing comma; it must fit `string`.

## F.5. Statements and Blocks

[Body forms](../14-control-flow.md#142-blocks-and-evaluation-contexts), [layout](../02-source-and-lexical-structure.md#22-lines-indentation-and-continuation), [labels](../14-control-flow.md#144-labels), [transfers](../14-control-flow.md#1451-syntax-and-operands), [patterns](../14-control-flow.md#1481-patterns), [defer](../16-scope-exit-and-destruction.md#161-deferred-blocks).

```ebnf
SourceUnit           := ? source-local declarations, aliases, and executable items, §6.1.1 ?
Body<Item>           := "=>" SingleItem | IndentedList<Item>
SingleItem           := Expression | Statement
Statement            := UnsafeStatement | DeferStatement | RequireStatement | DiscardStatement
                      | TestVerification
DiscardStatement     := "_" "=" Expression
TestVerification     := "$" ("expect" | "require") "(" Expression
                        ("," "message" ":" Expression)? ")"
ExecutableItem       := Expression | LocalBinding | AttributedFunctionDefinition
                      | Statement | Directive<ExecutableItem>
ExecutableBody       := Body<ExecutableItem>
UnsafeStatement      := "unsafe" ExecutableBody
DeferStatement       := "defer" ExecutableBody
RequireStatement     := "require" Expression RequireJoin "else" ExecutableBody
RequireJoin          := ? same-line or next effective aligned line, §2.2.1 ?
DoExpression         := (Name ":")? "do" ExecutableBody
LabeledSelection     := Name ":" (IfExpression | MatchExpression)
Iteration            := (Name ":")? (ForExpression | WhileExpression | LoopExpression)
ForExpression        := "for" ForBinding "in" Expression ExecutableBody
ForBinding           := ForSlot | "(" List<ForSlot> ")"
ForSlot              := Name | "var" Name | "_"
WhileExpression      := "while" Expression ExecutableBody
LoopExpression       := "loop" ExecutableBody
IfExpression         := "if" Expression ExecutableBody
                        (BranchJoin "else" "if" Expression ExecutableBody)*
                        (BranchJoin "else" ExecutableBody)?
BranchJoin           := ? same-line single-item join, or next effective aligned line
                          after body closure, §2.2.1 ?
MatchExpression      := "match" Expression IndentedList<MatchArm>
MatchArm             := Pattern ("if" Expression)? ExecutableBody
Pattern              := "_" | LiteralPattern | BindingPattern | CasePattern
                      | UnitPattern | TuplePattern | GroupedPattern
BindingPattern       := ("let" | "var") Name
CaseReference        := PlainContainerPath "." Name | "." Name
CasePattern          := CaseReference ("(" TrailingList<Pattern> ")")?
UnitPattern          := "(" ")"
TuplePattern         := "(" Pattern "," TrailingList<Pattern>? ")"
GroupedPattern       := "(" Pattern ")"
LiteralPattern       := BooleanLiteral | "-"? IntegerLiteral
                      | CharLiteral | NonInterpolatedStringLiteral
BooleanLiteral       := "true" | "false"
NonInterpolatedStringLiteral := ? ordinary or raw StringLiteral without interpolation ?
Transfer             := "return" Expression?
                      | "exit" (Expression? | "to" Name (":" Expression)?)
                      | "continue" ("to" Name)?
                      | "yield" (Expression? | "to" Name (":" Expression)?)
DeinitDeclaration    := "deinit" ExecutableBody
```

Conditions and guards must fit bool. Body headers, operand starts, delimiter regions, and required grouping follow §2.2 and §14.5. A label and its construct share a physical line. The keyword do is reserved; to is contextual immediately after exit/continue/yield. SingleItem does not include declarations or directives. Function-like bodies use the corresponding Body item category, not the declaration-container list grammar.

CaseReference qualifiers identify enum Cores without the enum's own Origin annotations; their generic argument Types retain complete Type information. Case existence, expected-Type resolution, payload presence/count, access, and Semantics follow §6.3.2. Payload Cases require parentheses; payload-free Cases prohibit them. Binding and acquisition follow §14.8. Match arm lists and enum bodies remain nonempty after selection.

## F.6. Property grammar

[Standard access](../11-properties.md#111-standard-access-and-acquisition), [accessor functions](../11-properties.md#112-accessor-functions), and [requirements](../11-properties.md#114-contract-property-requirements) define the semantic checks. PropertyRequirement is defined in F.3.

```ebnf
StoredPropertyDeclaration := Access? ("let" | "var") Name
                             (":" Type)? ("=" Expression)? IndentedList<OriginRelation | StoredAccessor>?
ComputedDeclaration  := Access? "computed" Name ":" Type IndentedList<OriginRelation | CustomAccessor>
StoredAccessor       := Access? ("get" | "set") | CustomAccessor
CustomAccessor       := Access? GetterSignature AccessorBody
                      | Access? SetterSignature AccessorBody
GetterSignature      := "get" "(" AccessorReceiver? ")" "->" Type
SetterSignature      := "set" "(" (AccessorReceiver ",")? "value" ":" Type ")" "->" UnitType
AccessorReceiver     := "self" ":" Type
AccessorBody         := Body<FunctionItem>
```

Each concrete accessor occurs at most once, in either order. Let forbids set; var supplies omitted standard get/set. A bodyless standard accessor has no explicit signature. Custom accessors require parameter lists and explicit input/result Types; stored receiver/Copy/Type restrictions follow §11.2. An omitted instance receiver expands to `self: ref/Self` for get or `self: uniq/Self` for set, including explicit requirement signatures. Static accessors omit self without inserting a receiver. Computed requires a custom get and optional custom set, with no initializer or inline has. Stored Type omission requires an initializer; static storage always requires one. Attributes/containers follow §6.5 and §11; unavailable modifiers follow §2.5.1.

## F.7. Origin grammar

The productions in F.2 distinguish schema/set braces from the postfix borrow annotation `during OriginAtom`. An intersection immediately after `during` needs parentheses; relation operands keep the full OriginExpression grammar. There are no function/accessor Origin lists or Origin mappings.

An `OriginRelation` is attached to its owning declaration, one indentation level below it. Type, function and accessor relations share the leading Constraint region and precede executable items or members. Field, enum Case, associated-Type specification, local and Container-alias relations are attached to that declaration. Relations are declaration metadata, permitted only in these leading or attached positions. A declaration-only signature may have only such clauses.

See [schemas, names and relations](../15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations) for ownership, scope and implicit binders, and [completion](../15-ownership-and-lifetime-analysis.md#154-origin-completion-and-elision) for inference and omission. Labels written within a local initializer's Type arguments, construction qualifier or Adaptation Target belong to that local declaration; no general expression constraint block is added.

## F.8. Compile-time directive grammar

[Directive syntax](../19-compile-time-directives.md#191-syntax-and-structural-selection), [closed Condition forms](../19-compile-time-directives.md#192-environment-condition-forms), [excluded-syntax parsing](../19-compile-time-directives.md#195-diagnostics-and-excluded-syntax).

```ebnf
Directive<Item>      := IfDirective<Item> | SwitchDirective<Item>
IfDirective<Item>    := "#" "if" CompileCondition (NEWLINE Item | IndentedList<Item>)
SwitchDirective<Item> := "#" "switch" NEWLINE INDENT CaseList<Item> DEDENT
CaseList<Item>       := CaseArm<Item>+ DefaultArm<Item>? | DefaultArm<Item>
CaseArm<Item>        := "#" "case" CompileCondition IndentedList<Item>
DefaultArm<Item>     := "#" "case" "_" IndentedList<Item>
CompileCondition    := CompileAnd ("or" CompileAnd)*
CompileAnd          := CompileComparison ("and" CompileComparison)*
CompileComparison   := CompileUnary (("==" | "!=") CompileUnary)?

CompileUnary        := "not" CompileUnary | CompileAtom
CompileAtom         := "true" | "false" | SignedInteger | PlainString | Name
                      | "(" CompileCondition ")"
SignedInteger       := ("+" | "-")? IntegerLiteral
IntegerLiteral      := ? integer alternatives of number-literal in F.1 ?
PlainString         := ? StringLiteral without interpolation, §19.2 ?

```

The hash forms use `#` followed by the reserved lowercase keywords `if`, `switch`, and `case`. Uppercase-initial `AttributeName` instead selects the Attribute grammar in F.3; other lowercase hash forms are errors. `Item` retains the surrounding syntax category; directives do not make an otherwise forbidden item legal there. Case layout and excluded-target grammar checking follow the linked sections.

## F.9. Syntax boundaries

These entries record the limits of a complete syntax summary for this revision. They are not wildcard productions or newly defined features.

| Form or production | Owning syntax and boundary |
| --- | --- |
| Extension declarations | Not introduced; no production or active extension candidate stage exists in this revision. See [Container boundary](../06-declarations-and-containers.md#61-declaration-containers). |
| Virtual/abstract/override declarations and ordinary base-member invocation | Not introduced; [extension design](../06-declarations-and-containers.md#624-virtual-members-and-overrides). Unavailable modifiers are diagnosed under §2.5.1. Base clauses and base-constructor initializers are defined in F.3. |
| Runtime-contract designations, View associated-type bindings, exact/contract tests and checked casts | The [runtime extension](../08-generics-constraints-and-contracts.md#85-runtime-contracts) and [object operations](../13-operators-and-assignment.md#1362-general-view-tests-and-checked-casts) preserve the design without final source spellings. Static associated-Type specifications/projections are defined in F.3. Ordinary struct `is` and explicit upcasts are defined. |
| Callable extensions | Borrowed or Exclusive/Consuming erased Types, public lending results, non-escaping declarations, capture aliases/initializers/parts, generic receiver Semantics, and `{environment}` remain unintroduced. |
| Additional Patterns | The [initial forms](../14-control-flow.md#1481-patterns) are defined; Struct, Type, OR, Range, Rest, and other [extensions](D-deferred-features.md#d1-enum-and-pattern-extensions) remain deferred. |
| Attributes | Syntax, placement, and Mod marker behavior follow [§6.5](../06-declarations-and-containers.md#65-attributes); Layout follows [§21.1.2](../21-layout-runtime-and-code-generation.md#2112-layout-attribute-and-fragments), LibraryImport follows [§22.3](../22-core-execution-and-foreign-functions.md#223-foreign-function-imports), and argument-free Test follows [§6.5.1](../06-declarations-and-containers.md#651-test-definitions). Concrete marker registration and other general semantics remain design boundaries. |
| Composition Root operations | `$abort(...)` is an expression. `$expect(...)` and `$require(...)` are standalone TestVerification items under [§17.5](../17-failure-handling.md#175-test-verification-operations), with the same test-only body restrictions and a message control boundary; they cannot be expression operands. Each evaluates its condition once; on false, expect records failure and continues, while require records failure and Aborts after condition/message temporary cleanup. Entry/Provider syntax remains unsettled under [§13.8](../13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax). |
| Additional type arguments | [Generic application](../12-expressions.md#1242-invocation-and-generic-application) does not define general constant type arguments. |
| Object ownership operations | [Kimi creation, strong-owner duplication and cyclic construction](../13-operators-and-assignment.md#1358-object-ownership-creation-and-sharing), and [Weak operations](../13-operators-and-assignment.md#1359-weak-reference-operations) define their source names, Types and acquisition contracts using ordinary call syntax. `Type.init` constructs an owner value, and `@obj`/`@rc`/`@arc` add no allocation or count increment. |
| Complete payload and whole-value updates | Sealed and ObjectPayload use ordinary requirement syntax; the opt-out `Self is not ObjectPayload` is an ordinary `ConstraintClause` whose placement §8.4.7.2 restricts. `@deref` selects a proven complete payload (§13.5.5.1); `@ref`/`@uniq` borrow the written slot. Kimi.Intrinsics.replace/exchange/swap use ordinary generic calls and named arguments (§15.7). |
| Places and iteration | `place(ref, T)`/`place(uniq, T)` results, Contract Type parameters, Origin-parameterized associated Types, `wellformed` clauses and `for var` slots are defined in F.2–F.5. Pattern-local acquisition selectors and Contract-owned Origin parameters remain unintroduced ([Appendix D](D-deferred-features.md)). |
| Function parameters | [§7.2](../07-functions-and-callable-values.md#72-parameters-and-defaults) defines the `!` boundary, external/internal names and independent defaults; F.3 summarizes their syntax. |
| Re-export and special FFI layouts | See [Re-exports](../18-modules-and-dependencies.md#182-re-exports) and [layout boundaries](../21-layout-runtime-and-code-generation.md#211-structure-layout-and-abi). |
| Failure propagation | Prefix `try` is defined in §17.2.4. User-defined propagation and try blocks are not introduced. |

Container placement follows §6.1.1, including nested groups/structs/enums/Contracts in structs, no children in enum/Contract bodies, and source-root-only rootgroup. Own arity excludes inherited slots; groups declare no own arguments, and Contracts declare only own Type parameters. BoundContainerQualifier is Type-side only and normalizes with ordinary grouped Type syntax. Contract and associated selectors use the same paths and full Origin bindings; each final role and intermediate qualifier is checked under §9.6.1. CaseReference remains a PlainContainerPath and excludes own/inherited explicit Origin annotations on its qualifier, while retaining Origins inside Type arguments.
