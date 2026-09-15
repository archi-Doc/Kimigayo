# Semantics Notation — Prefix Form

## Decision

Kimigayo の Type Semantics は、**Core Type の前に Semantics を記述する前置方式**を採用する。

```kimi
ref/T
uniq/T
obj/T
rc/T
arc/T
objref/T
objuniq/T
unsafe/T
```

`owner/T` は所有値を明示する完全形として扱い、通常は従来どおり `T` と省略できる。

Generic Type、Tuple、Union、Fixed Array、Function Type などにも同じ規則を適用する。

```kimi
ref/Array<obj/Node>
ref/(i32, string)
ref/(A or B)
ref/[4 of i32]
ref/((i32) -> string)
```

複数の Semantics layer が存在する場合も、外側から内側への順序で記述する。

```kimi
ref/uniq/T
```

これは概念的に、

```text
ref
└─ uniq
   └─ T
```

を表す。

Origin は Semantics と Type の後に記述する。

```kimi
ref/T from source
ref/Array<obj/Node> from source
```

---

## Context

Kimigayo では Type を Core Type だけではなく、Semantics や Origin を含む完全な型として扱う。

そのため、Semantics をどの位置に記述するかは、単なる表記上の問題ではなく、次の点に影響する。

- Type の読みやすさ
- Generic Type の視認性
- nested Semantics の理解しやすさ
- Tuple / Union など匿名型との一貫性
- Parser grammar の単純さ
- Type syntax と Type model の対応関係

当初の形式は、

```kimi
ref/T
obj/Node
ref/Array<obj/Node>
```

という前置方式であった。

その後、Core Type を先に読めることを重視して、後置方式や Type head 直後に Semantics を置く方式を検討した。

---

## Alternatives

### 1. Prefix Semantics

```kimi
ref/T
obj/Node
ref/Array<obj/Node>
ref/(A, B)
```

Semantics を対象となる Type expression の前に置く。

---

### 2. Postfix Semantics

```kimi
T/ref
Node/obj
Array<Node/obj>/ref
(A, B)/ref
```

まず Type expression を完成させ、その後ろに Semantics を付加する。

---

### 3. Type-head Semantics

```kimi
T/ref
Node/obj
Array/ref<Node/obj>
Dictionary/ref<String, Node/obj>
```

Named Type では、Type name の直後、Generic arguments の前に Semantics を置く。

長い Generic Type でも Semantics が早い位置に現れることを狙った方式である。

匿名型については別の記法が必要になる。

例:

```kimi
(i32, string)/ref
(A or B)/ref
```

---

## Pros / Cons

### Prefix Semantics

```kimi
ref/Array<obj/Node>
```

**Pros**

- Semantics を最初に認識できる。
- 長い Generic Type でも `ref`、`uniq`、`obj` などの重要な ownership / borrowing 情報がすぐ分かる。
- Named Type と anonymous Type で記法が変わらない。
- Tuple、Union、Fixed Array、Function Type などへ同じ規則を適用できる。
- nested Semantics を外側から内側へ自然に読める。
- 構文と意味構造が一致しやすい。

```text
ref/uniq/T

ref
└─ uniq
   └─ T
```

- Parser grammar が単純になる。
- Origin の `from` と自然につながる。

```kimi
ref/T from source
```

**Cons**

- Core Type の名前より Semantics が先に現れる。
- `Array<T>` や `Dictionary<K, V>` のような主要な型名を最初に見たい場合、Type-first 記法より視認性がやや低い。
- `ref/Array<...>` のように、Generic container の種類より借用状態を先に読むことになる。

---

### Postfix Semantics

```kimi
Array<Node/obj>/ref
```

**Pros**

- Core Type を先に読める。
- Semantics を Type expression 全体に対する postfix qualifier として統一できる。
- Named Type、Tuple、Union などに同じ後置規則を適用できる。

```kimi
Node/ref
Array<Node>/ref
(i32, string)/ref
(A or B)/ref
```

- `T = T/owner` という考え方とも相性がよい。

**Cons**

- 長い型では最も重要な Semantics が非常に遠くなる。

```kimi
Dictionary<String, Array<Pair<A, B>>>/ref
```

この場合、型全体をかなり読んだ後で初めて「shared borrow である」と分かる。

- nested Semantics では内側から外側へ読む必要がある。

```kimi
T/uniq/ref
```

は意味的には、

```text
ref
└─ uniq
   └─ T
```

となり、記述順と構造の読み順が逆になる。

- ownership / borrow 情報を迅速に判断したいコードリーディングでは不利になる。

---

### Type-head Semantics

```kimi
Array/ref<Node/obj>
```

**Pros**

- Named Generic Type では非常に読みやすい。
- Type name と Semantics が近接する。

```kimi
Array/ref<T>
Dictionary/ref<K, V>
Node/obj
```

- 長い Generic arguments があっても Semantics を早い段階で確認できる。
- `Array<Node/obj>/ref` の「Semantics が遠い」という問題を解決できる。

**Cons**

- Tuple や Union のような anonymous Type には Type head が存在しない。

```kimi
(i32, string)
A or B
```

そのため、

```kimi
Array/ref<T>
(i32, string)/ref
(A or B)/ref
```

のように Type の種類によって Semantics の位置を変える必要がある。

- Named Type と anonymous Type で grammar が非対称になる。
- 「Semantics は Type name に付くのか、完成した Type に付くのか」という意味上の説明が複雑になる。
- Generic arguments より前に書かれていても、意味上は Generic arguments を含む Type 全体へ適用されるため、source syntax と semantic structure が一致しにくい。

---

## Rationale

最終的に Prefix Semantics を選択した最大の理由は、**一貫性と Semantics の視認性を同時に満たせるため**である。

Type-head Semantics の、

```kimi
Array/ref<Node/obj>
```

という形式は Named Generic Type に限れば非常に読みやすい。

しかし Kimigayo の Type system には Named Generic Type だけでなく、

```kimi
(i32, string)
A or B
[4 of i32]
(i32) -> string
```

などの anonymous / composite Type が存在する。

これらを含めて統一的に扱おうとすると、Type-head 方式では Semantics の配置規則に例外が必要になる。

一方、Postfix Semantics は匿名型を含めて統一できるが、

```kimi
Dictionary<String, Array<Pair<A, B>>>/ref
```

のような長い型で Semantics が遠くなり、ownership / borrowing の重要な情報を最後まで確認できない。

Kimigayo では `ref`、`uniq`、`obj`、`rc`、`arc` などの Semantics は、単なる装飾ではなく、ownership、borrowing、mutation、lifetime、Copy / Move、Loan に直接影響する重要な型情報である。

したがって、

```kimi
ref/Dictionary<String, Array<Pair<A, B>>>
```

のように、**Semantics を最初に確認できることを優先する**。

また Prefix Semantics は nested Semantics でも、

```kimi
ref/uniq/T
```

のように外側から内側へ記述でき、構文木および意味構造と自然に一致する。

さらに、

```kimi
ref/T from source
```

という Origin syntax とも相性がよく、

```text
Semantics → Core Type → Origin
```

という一定の読み順を維持できる。

以上から、Type-first の局所的な読みやすさよりも、**全Type formに対する一貫性、Semantics の即時視認性、nested Type の構造的な読みやすさ**を優先し、Prefix Semantics を採用する。

---

## Consequences

この決定により、Kimigayo の Semantics syntax は次の形式に統一される。

```kimi
T
ref/T
uniq/T
obj/T
rc/T
arc/T
objref/T
objuniq/T
unsafe/T
```

Generic Type にも同じ規則を適用する。

```kimi
ref/Array<T>
ref/Array<obj/Node>
uniq/Dictionary<String, T>
```

Tuple、Union、Fixed Array、Function Type なども例外を設けない。

```kimi
ref/(A, B)
ref/(A or B)
ref/[4 of T]
ref/((A) -> B)
```

nested Semantics は prefix の入れ子として解釈する。

```kimi
ref/uniq/T
```

Semantics を Type の後方へ配置する、

```kimi
T/ref
Array<T>/ref
Array/ref<T>
```

などの形式は採用しない。

Parser、Syntax Tree、Binder では、Semantics を対象 Type expression を包む外側の型構成要素として扱う。このため source syntax と semantic Type structure を同じ順序で保持できる。

また、長い Type expression でも ownership / borrowing semantics が先頭に現れるため、API signature や Generic code を読む際に、値の ownership category を早い段階で判断できる。

この決定は Semantics の**表記順序**を確定するものであり、各 Semantics の ownership、borrowing、conversion、Origin、Loan、Copy / Move に関する既存の意味規則そのものは変更しない。