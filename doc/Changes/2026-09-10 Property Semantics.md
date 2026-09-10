# Design Change: Property の分類とアクセス契約

日付: 2026-09-10

最終仕様: [プロパティ仕様](../Design/2026-09-10%20Properties.md)

## Current Specification — 現在の仕様

SPEC.md 第11章は、storage を持つ Field と、操作を提供する computed Property を分けている。

- `let / var` は immutable / mutable Field。Accessor を持たず、文脈上の `storage` もない。
- Field の取得・借用・Move・更新には、同じ宣言のアクセス修飾子を適用する。
- Computed は見出し型 P、getter 結果型 R を持ち、setter は P を受け取る。Receiver は get が共有、set が排他に固定され、暗黙に与えられる。
- Contract は `property ... has get, set` で要求する。適合には実装の Field 型または P と要求の P の構造的一致を要求し、その上で操作を対応付ける。
- `@move` は Copy 型も明示的に Move できる。

通常取得の Copy/Move、部分 Move、Loan、確実な初期化、右辺先行の単純代入、一時値の寿命は既に規定されている。

## Proposed Specification — 新しい仕様

具体的なメンバーを次の Property に統一する。Contract の要求には引き続き `property` を用いる。

| 宣言 | Storage | Accessor | Initializer |
| --- | --- | --- | --- |
| `let` | Immutable | get | instance は任意 |
| `var` | Mutable | get / set | instance は任意 |
| `computed` | なし | get、任意の set | 不可 |

Static の let/var は initializer 必須。Stored の accessor 内では自身の slot を `storage` で参照でき、computed と Contract では使用できない。

**標準 get は Place のアクセス契約、カスタム get は値を返す関数**とする。標準の値取得結果は常に storage 型 T であり、Copy 能力は取得方法だけを決める。

- 標準 get/set は Copy・非 Copy の両方に対応する。
- Stored のカスタム getter は T が Copy の場合だけ許可し、結果型を T に揃える。
- Stored のカスタム setter は非 Copy も許可し、入力型を T、結果を Unit に揃える。
- カスタム契約を明記し、stored instance の get は `ref/Self`、set は `uniq/Self` とする。
- Computed は見出し型を getter 結果型に揃える。非 Copy 結果、異なる setter 入力型、明示的な所有 receiver も通常の関数規則で扱う。

```kimi
struct Holder
    public var item: Resource
        get
        set(self: uniq/Self, value: Resource) -> ()
            storage = normalize(value)

// Resource は非 Copy。生成・検査用関数は宣言済みとする。
holder.item = makeResource()       // 入力の所有権を setter へ渡す
let view = holder.item@ref/Resource // 標準 get で共有借用
let taken = holder.item            // Error：カスタム set の storage は直接 Move 不可
```

## Changes — 変更点

### 宣言とアクセス権

- Field を stored Property として位置付け、標準・カスタム accessor と個別のアクセス制限を導入する。
- Slot の直接 Copy・共有借用には標準 get、排他借用には標準 get/set の両方を要求する。
- `var` の直接 Move は標準 get/set の両方がアクセス可能でなければならない。`let` の消費には標準 get を要求する。Move は setter を呼ばず、通常の書き込み権限を追加で要求しない。
- カスタム setter は直接 Move・排他借用を公開しない。子 Place や暗黙の receiver 適応でも親の境界を迂回できない。
- カスタム getter の借用は戻り値に作用する。保存済み参照の返却と、storage slot の直接借用を区別する。
- Getter の所有一時領域と子領域への更新・排他借用を禁止する。制限はその領域に適用し、値を取得した local や通常の関数の所有結果へは引き継がない。返された参照・handle の別領域へのアクセスは通常規則に従う。
- 単純代入で呼ばないのは最終対象の get であり、アクセス経路に必要な getter は右辺の確保後に一度だけ評価する。

### 型・所有権・初期化

- `@move` を廃止する。非 Copy の所有値取得は Movable Place に限り許可し、失敗しても借用・複製に切り替えない。
- Generic でも結果型は T のまま。Copy 未証明の取得後状態は潜在的な Move として検証し、実行時には Copy 型を Copy する。
- Origin は通常の関数規則で補完後に型一致を検査する。Stored では storage の完全な型と一致しなければ明示を要求する。
- 初回配置は、全到達経路で own storage が未初期化かつ初回配置前の場合だけ accessor を通さず行う。Move・破棄で履歴をリセットせず、初回配置とカスタム setter を実行時に選び分けない。
- 構築中の self ではカスタム get/set・computed の呼び出しを禁止し、全 storage の初期化後も解除しない。カスタム set を持つ storage への二度目の代入や初期化状態の混在はエラーとする。標準 set の var は通常の状態別の配置・置換規則に従う。
- カスタム accessor は完全な receiver を要求する。標準操作は条件を満たす残存 Place にアクセスできる。Object receiver の呼び出しには既存の ObjectCompatible 検証も適用する。
- 非 Copy setter は入力を保存せず終了してもよい。未消費の入力は通常どおり破棄し、呼び出し側へ自動復元しない。

### Contract

- 見出し型を getter の結果型とし、明示形のシグネチャを追加する。Setter 入力型は個別に指定できる。
- `has get` は共有 receiver から指定型の値を返す要求であり、単なる「読み取り可能」ではない。
- 実装の storage 型との一律な一致要求をやめ、操作単位で適合を検査する。
- 型引数等の置換後に関数要求の適合規則を適用し、Origin は束縛の対応で検証する。標準の対応付けにない変換・再借用は合成しない。
- 公開された標準 Place 操作から要求を実現する対応付けを `witness adaptation` とする。Concrete なカスタム accessor を追加するものではない。
- 通常の名前検索で実装を確定してから適合を検査し、不適合でも別の同名候補へ戻らない。
- 呼び出し側の型・Origin・Loan と getter の一時領域制限は要求に基づく。Witness の直接操作への展開でも変えず、object の適合には既存の ObjectCompatible 条件も維持する。

```kimi
contract ResourceView
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource
// 標準 get が公開する Resource slot の共有借用で適合できる。
```

## Rationale — 変更する理由

- Storage と関数結果を区別し、既存の Place・RAII・Ownership 規則を維持する。
- Copy 能力に応じて T / ref/T が切り替わる型を導入せず、Generic の型と状態解析を分離する。
- 非 Copy getter の複雑さを制限しつつ、所有値の検証・正規化に必要なカスタム setter を提供する。
- Setter の検証とアクセス制限を、直接借用や子メンバー経由で迂回できないようにする。
- Getter の一時値だけを更新して捨てる操作を、Property の更新と誤認しにくくする。
- Contract を実装の storage 表現から分離し、固定した型・receiver・Origin の操作契約として扱う。

## Impact on SPEC.md — SPEC.md への影響

| 対象 | 更新内容 |
| --- | --- |
| 第11章 | Property の分類、accessor、storage、直接操作の権限を再構成 |
| 第3・15・16章 | Movable Place、`@move` 廃止、部分 Move と accessor 呼び出しの境界を整合 |
| 第6・7・9章 | 初回配置と構築中の呼び出し禁止、明示的 accessor 契約、個別アクセス権、子 Place・暗黙適応の検査を反映 |
| 第8・10章 | 操作単位の Contract 適合、witness adaptation、Generic の使用後状態を整合 |
| 第12・13章 | Getter の一時領域制限、ObjectCompatible との接続、経路上の getter の評価順、複合更新の権限を明記 |
| 第21・22章 | Stored storage に基づくレイアウト・初期化との接続を確認 |
| 用語集・構文概要・例・診断 | Field/Property 用語、新しい accessor 構文、`@move` 削除を反映 |

単純代入の右辺先行、複合代入の対象先行、一時値の借用による寿命延長なし、静的 storage の既存制限は維持する。ユーザー定義の算術演算子は追加しない。実装状況は STATUS.md に別途反映する。

## Decision — 最終決定

本変更を採用し、詳細はリンク先の設計仕様に従う。

Copy 能力で getter の公開型を変える案、stored custom getter の非 Copy 対応、全 accessor を同じ関数の省略形とする案は採用しない。借用を取る算術演算子の複合代入への適応は将来事項とする。

本書は設計決定の記録であり、SPEC.md 本体・コンパイラへの反映完了を意味しない。
