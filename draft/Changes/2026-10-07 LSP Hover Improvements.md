# LSP Hover の表示と説明の改善

状態：提案。合意した表示方針と追加の改善を整理した文書であり、正式仕様への取り込み・実装は未実施。

## 1. 目的

Hover の冒頭で「どこに宣言され、何が宣言されているか」を把握できるようにする。変数には型の構成を、組み込みの `@` 操作には一般的な意味を説明する。

表示ラベルと組み込み説明は英語、宣言コメントは原文の言語を使う。説明はコンパイラが確定した事実と正式仕様に基づき、新しい言語上の保証を加えない。

## 2. 宣言の表示

### 表示順序

1. 以前の解析結果である場合の既存の通知。
2. Declaration container。
3. Target の宣言。
4. 使用位置で確定した型・引数などの補足、既存の Copy 判定・effect 情報。
5. 宣言のドキュメント。
6. 宣言元ファイル。

container と宣言は通常サイズで上下に並べる。container はコード表記、宣言は `kimi` のコードブロックとし、横2列の表や大きな宣言見出しは使わない。`Type`、`Property`、`Function`、`Built-in type` など、種類だけを示すラベルは削除する。

container は宣言の所属を識別できる修飾名とし、トップレベルでは所属モジュールを示す。実在しない container は作らない。宣言は既存の契約を保持し、制約、Origins、アクセサーなどを複数行のまま表示する。本文・初期化式・全メンバーは展開しない。

表示例（所属名とパスは例示）：

```text
Example
func borrowCounter(counter: ref/Counter) -> ref/Counter during counter

〈既存の補足情報・ドキュメント〉

(tests/milestones/Milestone7.kimi)
```

### 宣言元ファイル

- 最下部に `(tests/milestones/Milestone7.kimi)` の形で表示する。基準は宣言元プロジェクト、区切りは `/` とする。
- 部分的な文字サイズの指定は必須にしない。通常サイズの括弧書きで控えめに表示し、HTML・CSSによる縮小には依存しない。
- 同じ宣言元への重複経路はまとめる。分割宣言は全ファイルを安定した順序で表示し、主ファイルを選ばない。
- 複数のプロジェクト・宣言・コメント断片がある場合は、所属名や番号で本文と末尾の出典を対応させる。異なる宣言の同文コメントは統合しない。
- 変数自身と、その Core の宣言元も区別する。組み込み・生成宣言に実ファイルがなければ省略し、埋め込みソースを実ファイルに見せかけない。
- 確定した論理パスを使い、対応が不明な相対パスや物理パスを推測しない。クリック可能な宣言移動機能は今回追加しない。

ドキュメント内の見出し階層は保持する。宣言見出しの削除後も本文と混同しないように描画規則を更新する。

## 3. 変数・引数の Hover

### 対象と内容

ローカルの `let`・`var`、通常の関数引数を、その宣言名と解決済みの参照名で対象にする。ループ・パターン束縛も、通常のローカル束縛として確定した名前は同じ規則で扱う。メンバー Property は既存の対象と表示規則を維持する。

冒頭に所属関数などの文脈と、変数名・完全な型を表示する。その下に次の2つを置く。

- **Semantics**：外側の Semantics と、短い英語説明。
- **Core**：Core の名前、宣言、関連する制約と宣言コメント。

次の `counter` の宣言名と `return counter` の参照名で利用できるようにする。

```kimi
func borrowCounter(counter: ref/Counter) -> ref/Counter during counter
    return counter
```

主要部分の表示例（型の Origins は省略した説明用の例）：

```text
Example.borrowCounter
counter: ref/Counter

Semantics: ref — Shared, non-owning access to a value.

Core: Counter
〈Counter の container・宣言・ドキュメント〉

〈変数と Core の宣言元ファイル〉
```

### Semantics の共通説明

| Semantics | 英語説明 |
| --- | --- |
| `owner` | Direct ownership of a value. |
| `ref` | Shared, non-owning access to a value. |
| `uniq` | Exclusive, mutable, non-owning access to a value. |
| `obj` | Exclusive ownership of an object. |
| `rc` | Shared object ownership with non-atomic reference counting. |
| `arc` | Shared object ownership with atomic reference counting. |
| `objref` | Shared, non-owning access to an object. |
| `objuniq` | Exclusive, mutable, non-owning access to an object. |
| `raw` | A raw pointer without safe-borrow guarantees. |

`arc` の原子性は参照カウントの管理についての説明であり、内容の並行変更の安全性を意味しない。Semantics の説明だけで、その位置での書込み・Move・借用の可否を保証しない。

### 型と説明の正確さ

- 推論結果を含む、その位置で確定した完全な型を使う。Origins・型引数・入れ子を落とさず、宣言された型と使用位置の型が異なる場合は区別する。内部生成名は露出しない。
- `ref/obj/Counter` では `Referent: obj/Counter` を補い、オブジェクトのハンドル格納場所を借りていることを示す。`ref/Counter` と同じ意味に平坦化しない。
- Contract View は Core ではないため、`View target` と表示する。型パラメーターは具体型に置き換えて推測せず、その宣言と制約を示す。未確定の Semantics パラメーターも名前と確定した制約を示す。
- Tuple・Function・配列などは正規の型表記と必要な構造説明を使う。全メンバーや型引数のドキュメントを再帰的に展開しない。
- Core の説明は関連付け済みの宣言コメントから取得する。コメントがなければ宣言だけを表示し、内容を推測して生成しない。コメントの明示的な空・関連付け保留・処理中断は既存規則で区別する。
- 親関数のドキュメントに引数名と一意に対応する項目がある場合は、変数の直後に `Parameter` として表示する。Core の説明や別の引数の説明と混ぜない。
- 名前の束縛や型が確定しない場合は、推測した変数情報を返さない。既存の未解決・曖昧・回復構文の扱いに従う。
- Callable 変数の呼出し名では変数情報と確定した呼出し契約・effects を統合する。既存の呼出し情報を変数情報で置き換えない。Copy を表示する場合の対象も Core 単体ではなく完全な型とする。

## 4. 組み込みの `@` 操作の Hover

### 対象と位置

`@ref`、`@uniq`、`@obj`、`@rc`、`@arc`、`@objref`、`@objuniq`、`@move`、`@copy`、`@follow`、`@raw`、`@wrap<U>`、`@bits<U>` に、正式仕様に基づく短い一般説明を用意する。

- `@ref` では `@` と `ref` のどちらでも同じ操作説明を表示する。応答範囲は Hover した個々のトークンとし、空白まで対象に広げない。
- `@ref/Counter` では `@`・`ref` が操作説明、`Counter` が既存の Type 説明を担当する。型引数や連続する `@` 操作も、それぞれ自身の対象を保つ。
- 型記述 `ref/Counter` の `ref` では、既存の適用済み Type の Hover に Semantics の共通説明を添える。`@ref` の操作説明とは区別する。
- 組み込み操作として構文上確定していれば、型検査に失敗していても一般説明を表示できる。コメント、文字列、除外領域、構文上の推測や単なる同名識別子から説明を作らない。

### 説明例と注意点

```text
@ref
Creates a shared borrow of the operand's storage.
If the operand stores a reference, this borrows the reference slot.
```

```text
@obj
Uses exclusive object ownership, inferring the target from the operand.
Creates an object from an eligible owned value, or acquires an existing
object with the same ownership semantics.
Does not implicitly move a non-Copy value out of a place.
```

Semantics は値の性質、`@` は操作として説明し、同じ文章を流用しない。`@obj` を常に新規作成・ヒープ確保と説明せず、`@rc`・`@arc` も既存の強参照の複製と説明しない。完全なターゲットを指定する形式は、ターゲット省略形と区別する。

一般説明は、その式の操作が合法・検証済みであることを意味しない。確定した使用位置の情報を併記する場合は別の補足にし、型エラーを一般説明で隠さない。一般説明だけの項目には架空の container・宣言元を付けない。

## 5. 共通の制約と実装時の確認

既存の解析スナップショット、参加単位間の一致、以前の解析の明示、キャッシュ、処理量制限、診断からの分離を維持する。新しい対象も同じ仕組みに載せ、Hover の要求時に再解析しない。一般説明の追加を理由に別のスケジューラーを設けない。

必須の契約・Copy 判定・effect 根拠は表示整理で削らない。本文の切り詰め後も宣言元を末尾に残せるように領域を確保し、省略・保留の通知を保つ。Markdown と plaintext は同じ情報順序と意味を持たせる。

実装時には以下を確認する。

- 複数行の宣言、分割宣言、依存先・組み込み・生成宣言、コメントなしの出典。
- 変数と引数の宣言・参照、同名の別束縛、型推論、入れ子、Origins、Contract View、Callable 呼出し。
- `@` と操作名それぞれの範囲、型名との分担、連続する操作、エラー時の一般説明、非コード領域の除外。
- 更新待ち・設定間の不一致、長いコメント、Markdown/plaintext、反復要求の割当て・保持量・応答性能。
- VS Code での実際の表示。文字サイズ、折返し、見出しと出典の読みやすさは画面でも確認する。

## 6. 正式仕様への反映先

取り込み時は [Hover 仕様](../../docs/spec/lsp-hover.md) の対象・代表トークン・表示順・見出し・ドキュメント関連付け・一致判定を更新する。言語の意味は [Types and values](../../docs/spec/03-types-and-values.md)、[Operators and assignment](../../docs/spec/13-operators-and-assignment.md)、[Raw pointers](../../docs/spec/05-raw-pointers-and-unsafe-memory.md) に従う。

正式仕様を変更した時点で `draft/INTEGRATED.md` に取り込みを記録する。実装時に必要なテスト・実機確認を行い、確認済みの対応範囲に応じて `docs/STATUS.md` などを更新する。本提案の保存だけでは対応済みとしない。
