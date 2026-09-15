# Design Change: Coreの用語定義と分類

日付: 2026-09-10

## Current Specification — 現在の仕様

Typeの基本形は `Semantics/CoreType from Origin`。Core Typeは「値が何であるか」、Semanticsは表現・所有・アクセス方法、Originは借用の有効範囲を表す。

Core Typeの種類はSPEC.mdの各節で定義されている。Scalarに相当する既存の分類名はPrimitive scalarであり、string・Unit・Neverを含まない。

## Proposed Specification — 新しい仕様

基本形を `Type = Semantics/Core from Origin` と表記し、各用語を次のように定義する。

| 用語 | 定義 |
| --- | --- |
| Type | Core、Semantics、および必要なOriginの情報を組み合わせた完全な型。 |
| Semantics | 値の表現、所有、借用、アクセス方法、安全性を規定する構成要素。 |
| Core | 値の種類・構造・同一性を規定する構成要素。従来のCore Type。 |
| Origin | 借用の有効性が保証されるプログラム上の地点の集合。借用や、借用を保持する値の有効性を制約する。 |

以下は継承関係ではなく、構成要素とその分類を表す。

```text
Type
├─ Semantics
│  ├─ Value: owner
│  ├─ Value Borrow: ref, uniq
│  ├─ Object: obj, rc, arc
│  ├─ Object Borrow: objref, objuniq
│  └─ Unsafe: unsafe
├─ Core
│  ├─ Scalar: Integer, Floating-point, Boolean, Character
│  ├─ String
│  ├─ Unit
│  ├─ Never
│  ├─ Struct
│  ├─ Enum
│  ├─ Tuple
│  ├─ Fixed Array: [N of T]
│  ├─ Callable
│  │  ├─ Function Item
│  │  ├─ Concrete Closure
│  │  └─ Common Function
│  └─ その他の名前付きCoreの例: Array<T>, Dictionary<K, V>, Slice<T>
└─ Origin
   ├─ 借用元から得られるOrigin
   ├─ 宣言された抽象Origin
   ├─ 複数のOriginの共通部分
   └─ static
```

Scalarは既存のPrimitive scalarの短称とする。Arrayなどの名前付きCoreの例は、新しい宣言形式や相互排他的な分類を追加するものではない。

Coreは外側のSemanticsとOriginから区別されるが、要素や型引数には完全なTypeが含まれ得る。基本形には次の既存規則を併記する。

- `ref`・`uniq`・`unsafe` は完全なTypeを内側に取り、再帰的に構成できる。
- Object Semanticsの対象にはCoreのほか、拡張仕様で認められるruntime Contract View Targetがある。Contract自体をCoreに分類しない。
- SemanticsとOriginの省略は既存規則に従う。すべての層がOriginを持つわけではなく、複数のOriginへの依存も保持する。

## Changes — 変更点

- 仕様用語Core TypeをCoreに短縮する。
- 基本形の表記を `Semantics/Core from Origin` に統一する。
- Type・Semantics・Core・Originの定義と文字ベースの分類図をまとめて示す。
- Primitive scalarの短称としてScalarを用い、その対象範囲を維持する。

## Rationale — 変更する理由

完全な型を表すTypeと、その構成要素であるCoreの呼び分けを簡潔にする。構成要素の役割とCoreの種類をまとめることで、所有・借用の違いと値そのものの種類を理解しやすくする。

## Impact on SPEC.md — SPEC.md への影響

- §3の基本形、定義表、分類説明を更新する。
- §3.1のPrimitive scalarとScalarの対応を明記し、§3.2・§4・§6の型分類と整合させる。
- §3.3.5–§3.3.6のView Targetと再帰的構成、§15のOrigin規則を維持する。
- 本文・見出し・用語集のCore Type表記を見直し、見出し変更時は内部リンクも更新する。
- §22などの基盤機能を指す既存のCoreと、今回の型構成要素Coreを文脈上区別する。
- 文法上の識別子`CoreType`や実装名の変更は別途判断する。ソース構文、型の同一性、所有・借用規則、実行時表現は変更しない。

このDesign Changeでは方針を記録し、SPEC.md自体の更新は行わない。

## Decision — 最終決定

Core TypeをCoreと呼び、基本形を `Type = Semantics/Core from Origin` とする。上記の定義と分類を採用し、既存の型システムの意味と制約を維持する。
