# Kimigayo の LLVM IR 最適化 — Developer notes

## 1. 目的と読み方

仕様が保証する情報を LLVM に伝え、意味を変えずに最適化するための調査・実装・検証台帳。**本書は非規範の開発ノートであり、言語仕様・実装仕様・正式な作業計画を変更しない。** 着手する範囲と順序は [PLAN.md](PLAN.md)、製品の対応範囲は [STATUS.md](../STATUS.md) に従う。

- 対象：windows-x64-v1、LLVM **22.1.8**（[profile.json](../../src/backend/windows-x64/profile.json)）。
- 初回コード確認：**2026-10-01 19:46 JST**。参照時 HEAD は `cdc62ddad4a4e6229e468e1a68b7146fe529595a`。未コミット変更を含む作業ツリーを確認した。
- 初回の範囲：仕様・生成コード・既存テストの静的確認。属性を追加した実行検証、ツールチェーンの実体確認、性能測定は行っていない。
- 「現在の状態」は主に **Kimigayo が明示する最適化前 IR**。LLVM の O2 が後から推論する属性の有無・効果は、別に確認する。
- 「Ryu」は Clang が生成した組み込み浮動小数点整形コード。そこでの使用は、通常の Kimigayo コード生成が対応済みであることを意味しない。

### 状態の表記

| 軸 | 表記と意味 |
| --- | --- |
| 適用の見込み | **有望**：既存の言語契約・layout から導ける範囲が明確。**条件付き**：個別の範囲・効果・寿命などの証明が必要。**保留**：仕様整理または効果測定を先に行う。**原則不採用**：一般適用が仕様と合わない。 |
| 実装状態 | **実装あり**：記載範囲で明示的な使用を確認。**限定実装**：intrinsic、特定型、Ryu などに限定。**未付与**：調査した emitter/runtime に一般的な付与処理を確認できない。 |
| 日時・証拠 | 一覧の全項目に上記の初回コード確認日時が適用される。**実行検証日時は全項目「—：本書では未記録」**。過去の製品テストの成功を、各属性の正しさ・効果の証明として転記しない。以後は §5.3 に項目 ID ごとの日時と証拠を追記する。 |

「未付与」は LLVM 自身が推論しないという意味ではない。「今後」は候補と次の確認事項であり、実装開始・採用・高速化を確約しない。

### 共通の判断基準

1. `noalias` などの保証を誤ると、必要な処理が消えるなどの誤コンパイルを招く。属性は実行時チェックではない。
2. 引数・戻り値・関数全体・個々の命令を区別する。`readonly` 引数から関数全体の純粋性は導けない。
3. Abort、評価順序、cleanup、合法な非停止、ポインタの由来、浮動小数点の結果を保存する。未定義動作で必要な Abort を代用しない。
4. 借用契約は直接指す格納領域に適用する。ハンドルから読み出したポインタの先へ自動的に広げない。
5. 必須の Loan/effect 検査不足は生成前の診断対象。任意の最適化の証明不足なら、その属性を省略する。
6. メリットは期待効果であり、速度向上の実測結果ではない。属性数を増やすことを目標にしない。

## 2. キーワード一覧

各 ID は §3 の詳細と §5 の検証記録を結ぶ。関連する表記は同じ項目にまとめ、適用対象の違いを詳細に記す。

### 2.1. 借用・メモリ効果・値の妥当性

| ID | キーワード | 簡単な説明 | 見込み | 現在の付与範囲 |
| --- | --- | --- | --- | --- |
| [A01](#a01) | `noalias`（引数） | 独立した経路との競合アクセスを制限 | 有望 | 限定実装：`memcpy`。通常の借用引数は未付与 |
| [A02](#a02) | `readonly noalias` | 読み取り専用と借用契約を併記 | 有望 | 限定実装：`memcpy` の source。通常の `ref/T` は未付与 |
| [A03](#a03) | `readonly` / `memory(read)` | 引数経由／関数全体の読み取り効果 | 有望／条件付き | 引数は intrinsic に限定。関数全体は未付与 |
| [A04](#a04) | `readnone` / `memory(none)` | 引数経由／関数全体のメモリ効果がない | 条件付き | 未付与 |
| [A05](#a05) | `nocapture` / `captures(...)` | ポインタの捕捉を制限 | 条件付き | intrinsic に `nocapture`、Ryu に `captures(none)` |
| [A06](#a06) | `writeonly` | 元の内容を観測しない出力 | 条件付き | 限定実装：intrinsic と Ryu の出力引数 |
| [A07](#a07) | `noundef` | 値のビットに undef/poison がない | 有望 | 限定実装：`ref/string` 引数、Ryu |
| [A08](#a08) | `nonnull` | ポインタが null ではない | 有望 | 限定実装：`ref/string` 引数 |
| [A09](#a09) | `align` | 保証された alignment | 有望 | 実装あり：load/store、alloca、定数、`ref/string` 引数など |
| [A10](#a10) | `dereferenceable(N)` / `dereferenceable_or_null(N)` | 固定 byte 数へのアクセス可能性 | 有望／条件付き | 前者は `ref/string` の 24 byte。後者は未付与 |
| [A11](#a11) | `range` / `!range` | 値の取り得る範囲 | 有望 | 未付与 |
| [A12](#a12) | `returned` | 戻り値が特定の引数と同じ値 | 条件付き | 未付与 |

### 2.2. 算術・アドレス計算

| ID | キーワード | 簡単な説明 | 見込み | 現在の付与範囲 |
| --- | --- | --- | --- | --- |
| [B01](#b01) | `nuw` | unsigned overflow がない | 条件付き | 限定実装：Ryu。通常演算は未付与 |
| [B02](#b02) | `nsw` | signed overflow がない | 条件付き | 限定実装：Ryu。通常演算は未付与 |
| [B03](#b03) | `inbounds` / GEP の `nusw`, `nuw` | allocation 内のアドレス・非 wrap 計算 | 有望／条件付き | Ryu に `inbounds` / `nuw`。通常 GEP は未付与 |
| [B04](#b04) | `exact` | 除算・右シフトで値を捨てない | 条件付き | 未付与 |
| [B05](#b05) | `trunc nuw/nsw` | 切り詰め後も該当する整数値を保存 | 条件付き | 限定実装：Ryu |
| [B06](#b06) | `nneg` | 変換元の符号ビットが立たない | 条件付き | 限定実装：Ryu の `zext` |
| [B07](#b07) | `or disjoint` | 入力の 1 bit が重ならない | 条件付き | 限定実装：Ryu |
| [B08](#b08) | `icmp samesign` | 両入力の符号が同じ | 条件付き | 限定実装：Ryu |

### 2.3. 確保・寿命・局所的な別名情報

| ID | キーワード | 簡単な説明 | 見込み | 現在の付与範囲 |
| --- | --- | --- | --- | --- |
| [C01](#c01) | 戻り値 `noalias`, `allocsize`, `allockind`, `alloc-family` | allocator の独立領域・サイズ・種類 | 有望／保留 | allocator への明示付与は未実装 |
| [C02](#c02) | `nofree` | 対象メモリの解放を制限 | 条件付き | 未付与 |
| [C03](#c03) | `llvm.lifetime.start/end` | 物理 storage の有効期間 | 有望 | 未付与 |
| [C04](#c04) | `constant`, `invariant.load`, `invariant.start/end` | メモリの不変性 | 有望／条件付き | 定数は実装あり。invariant 系は未付与 |
| [C05](#c05) | `alias.scope` / `noalias` metadata | 個々のメモリアクセス集合の非重複 | 条件付き | 未付与 |
| [C06](#c06) | TBAA / `!tbaa` | 型に対応する別名関係 | 保留 | 未付与 |
| [C07](#c07) | `writable` | entry 時の追加書き戻しを許可 | 保留 | 未付与 |
| [C08](#c08) | `initializes` | 指定 byte 範囲の初期化を保証 | 保留 | 未付与 |
| [C09](#c09) | `dead_on_return` | 関数終了後の対象メモリを観測しない | 保留 | 未付与 |

### 2.4. 呼び出し・制御フロー

| ID | キーワード | 簡単な説明 | 見込み | 現在の付与範囲 |
| --- | --- | --- | --- | --- |
| [D01](#d01) | `noreturn` | 正常には戻らない | 有望 | 実装あり：Never、Abort/Exit など |
| [D02](#d02) | `nounwind` | unwind しない | 条件付き | 明示的な一般付与なし |
| [D03](#d03) | `willreturn` | 呼び出しを含む既存 stack へ制御が戻る | 条件付き | 未付与 |
| [D04](#d04) | `mustprogress` / `llvm.loop.mustprogress` | 戻るか観測可能な進行を行う | 条件付き | 一般付与なし。一律付与は仕様上不可 |
| [D05](#d05) | `nosync` | 他 thread と同期しない | 条件付き | 未付与 |
| [D06](#d06) | `norecurse` | 再帰的な呼び出しに参加しない | 条件付き | 未付与 |
| [D07](#d07) | `speculatable` | 結果計算だけを安全に先行実行できる | 条件付き | 未付与 |

### 2.5. 性能ヒント・最適化情報

| ID | キーワード | 簡単な説明 | 見込み | 現在の付与範囲 |
| --- | --- | --- | --- | --- |
| [E01](#e01) | `cold` / 分岐重み `!prof` | 低頻度経路と分岐頻度を伝達 | 有望 | 未付与 |
| [E02](#e02) | `noinline`, `alwaysinline`, `inlinehint` | インライン化方針 | 条件付き | 明示的な一般付与なし |
| [E03](#e03) | `optsize` / `minsize` | コードサイズを重視 | 条件付き | 未付与 |
| [E04](#e04) | `unnamed_addr` / `local_unnamed_addr` | アドレス identity が重要でない | 有望 | 定数に前者、Ryu 関数に後者を確認 |
| [E05](#e05) | `llvm.loop.vectorize.*` / `llvm.loop.unroll.*` | ベクトル化・展開の方針 | 条件付き | 明示 metadata は未付与 |
| [E06](#e06) | `llvm.access.group` / `llvm.loop.parallel_accesses` | 反復間のメモリ依存がないアクセス群 | 条件付き | 未付与 |
| [E07](#e07) | `!nontemporal` | 近い将来に再利用しないアクセス | 条件付き | 未付与 |
| [E08](#e08) | `llvm.assume` | 成立済みの条件を追加伝達 | 条件付き | 未付与 |

### 2.6. 適用を制限する項目

| ID | キーワード | 簡単な説明 | 見込み | 現在の付与範囲 |
| --- | --- | --- | --- | --- |
| [F01](#f01) | `undef` | 使用ごとに異なり得る未定義値 | 原則不採用 | 明示的な生成を確認せず |
| [F02](#f02) | `poison` | 特定の使用へ伝播すると UB を生む値 | 条件付き | 限定実装：整形用 aggregate の構築開始値 |
| [F03](#f03) | `freeze` | undef/poison を一つの任意値に固定 | 保留 | 未付与 |
| [F04](#f04) | `fast`, `reassoc`, `contract`, `arcp`, `afn`, `nnan`, `ninf`, `nsz`, `nofpclass` | FP の変換許可・値域制限 | 原則不採用／条件付き | 一般の fast-math と `nofpclass` は未付与 |
| [F05](#f05) | `sret`, `byval`, `signext`, `zeroext`, `fastcc` | 物理 ABI の設計 | 別途検討 | 現行の通常 ABI は ccc。これらの一般付与なし |

## 3. 項目別の判断と次の作業

以下の「状態」は初回静的確認時点。各項目の実行検証日時・測定値は §5.3 の記録を正とする。

### 3.1. 借用・メモリ効果・値の妥当性

<a id="a01"></a>
#### A01. 引数の `noalias`

- **説明**：関数実行中に変更されるメモリについて、その引数由来でない経路とのアクセス競合を禁止する。
- **メリット**：再 load の削減、命令移動、別名解析を助ける。
- **デメリット**：借用検査の抜けが誤コンパイルへ直結する。
- **可否・注意**：通常の `uniq/T` と `ref/T` は有望。SPEC/IMPL §21.5.5 の call-wide 契約が前提。子 Reborrow は許される。ハンドルの先、object-borrow header、環境全体へ自動的に広げない。戻り値は C01。
- **状態**：`memcpy` の引数のみ。通常の借用 ABI には未反映。
- **今後**：ABI に借用モードと保証を保持。activation、capture、static、cleanup、Unsafe/FFI の契約を確認し、直接・間接 call と adapter を検証する。

<a id="a02"></a>
#### A02. `readonly noalias`

- **説明**：A01 に引数経由の書き込み禁止を組み合わせる。
- **メリット**：呼び出し中の読み取り値を保持・再利用しやすい。
- **デメリット**：共有ハンドルの先まで不変だと誤認しやすい。
- **可否・注意**：通常の `ref/T` に有望。同じ共有参照を複数引数に渡してよい。直接の T storage が対象であり、関数全体の無副作用を意味しない。
- **状態**：`memcpy` の source に使用。通常の `ref/T` は未付与。
- **今後**：A01 と共通実装にし、重複した共有入力・nested reference・handle と payload の区別を検証する。

<a id="a03"></a>
#### A03. `readonly` / `memory(read)`

- **説明**：`readonly` は引数経由の書き込みを制限し、`memory(read)` は関数全体のメモリ効果を制限する。
- **メリット**：呼び出し前後のメモリ値の再利用を助ける。
- **デメリット**：本体・callee・destructor の効果を取りこぼす危険がある。
- **可否・注意**：引数は `ref/T` の契約から導ける。`uniq/T`・raw pointer は追加証明が必要。関数全体は I/O、static 更新、count 更新、Abort の診断も解析する。
- **状態**：intrinsic の引数に限定。一般関数の効果属性はない。
- **今後**：引数属性と関数効果を別管理。既存の Iterator/BufferWriter 用 effect 検査を、純粋性の証明として流用しない。

<a id="a04"></a>
#### A04. `readnone` / `memory(none)`

- **説明**：`readnone` 引数はその経路でメモリを参照しない。`memory(none)` は外部に観測されるメモリ効果を持たない。
- **メリット**：条件が揃えば呼び出しの共通化・削除・移動が可能。
- **デメリット**：正常経路だけを見て判定すると、失敗経路や cleanup を消し得る。
- **可否・注意**：内容を読まない pointer helper や純粋な計算に限定。非脱出ローカル storage や不変定数の参照は別扱い。終了性・capture・先行実行の安全性は別証明。
- **状態**：明示付与なし。O2 の推論結果は未調査。
- **今後**：小さな helper と LLVM の推論結果を先に確認。チェック付き算術の Abort 経路を保持する。

<a id="a05"></a>
#### A05. `nocapture` / `captures(...)`

- **説明**：ポインタのアドレス・アクセス権限の捕捉を制限する。新規実装は LLVM 22 の `captures(none)` を基本候補とする。
- **メリット**：ローカル storage の最適化と call 前後の別名解析を助ける。
- **デメリット**：参照返却、外部への格納、capture、整数化などの追跡が必要。
- **可否・注意**：Origin の正しさだけでは非捕捉を証明できない。`captures(ret: address, provenance)` は LLVM の return 経由の制限であり、隠れた出力 slot へ書く aggregate return と同一ではない。
- **状態**：メモリ intrinsic は `nocapture`、Ryu の出力引数は `captures(none)`。一般推論なし。
- **今後**：明白な非捕捉 helper から開始。論理戻り値と物理的な脱出経路を区別して検証する。

<a id="a06"></a>
#### A06. `writeonly`

- **説明**：引数経由で元のメモリ内容を観測しない。自分で書いた後の内部読み取りとは区別する。
- **メリット**：不要な旧値への依存を減らし、store 最適化を助ける。
- **デメリット**：一部の旧値読み取りや destruction を見落とすと不正になる。
- **可否・注意**：新規 output slot や fill/copy helper が候補。既存値の replacement は destructor が旧値を読むため、一律には不可。
- **状態**：メモリ intrinsic と Ryu の output に使用。
- **今後**：初期化専用経路と replacement を分離し、部分初期化・早期終了も確認する。

<a id="a07"></a>
#### A07. `noundef`

- **説明**：引数・結果の値ビットに undef/poison がないと保証する。
- **メリット**：値の不確定性による最適化制約を減らす。
- **デメリット**：未定義ビットを含む ABI coercion に付けると不正になる。
- **可否・注意**：初期化済み scalar/safe pointer は有望。storage padding 自体と、それを整数・`environmentWord` の値ビットにした場合を区別する。NaN は poison ではない。
- **状態**：`ref/string` の pointer 引数と Ryu に限定。
- **今後**：physical value ごとの定義性を保持。scalar、borrow、結果、環境の順で確認し、不要な padding 初期化は増やさない。

<a id="a08"></a>
#### A08. `nonnull`

- **説明**：物理ポインタが null ではないと保証する。
- **メリット**：不要な null 分岐の削除を助ける。
- **デメリット**：nullable 表現との混同が誤コンパイルを招く。
- **可否・注意**：safe reference が有望。`Option`、raw pointer、空 buffer には一律付与しない。非 null は dereference 可能性を意味しない。
- **状態**：`ref/string` に限定。
- **今後**：型の正規化後に物理表現を確認し、nullable と zero-sized substitute の対照テストを用意する。

<a id="a09"></a>
#### A09. `align`

- **説明**：storage または pointer の最低 alignment を伝える。
- **メリット**：load/store と命令選択を改善する。
- **デメリット**：実際より強い alignment は不正。過剰な配置調整はメモリを増やす。
- **可否・注意**：既存 `TypeLayout` から導く。field offset と確保元を考慮し、基底の alignment を無条件に継承しない。動的 metadata の値は定数属性にしない。
- **状態**：load/store、alloca、定数、`ref/string` 引数などに実装あり。
- **今後**：一般の借用 ABI と結果へ拡張できる下限を確認。heap の現行上限 16 と高 alignment の拒否を維持する。

<a id="a10"></a>
#### A10. `dereferenceable(N)` / `dereferenceable_or_null(N)`

- **説明**：前者は正の定数 N byte、後者は非 null の場合の N byte へのアクセス可能性を示す。
- **メリット**：load の移動・先行実行を助ける。
- **デメリット**：寿命やサイズを過大に宣言すると、本来できないアクセスを許してしまう。
- **可否・注意**：即時参照先の有効範囲が必要。zero-sized 値に代替 byte のサイズを流用しない。動的長さ・部分初期化・nullable ABI を個別に扱う。
- **状態**：`ref/string` に `dereferenceable(24)`。後者は未付与。
- **今後**：layout と借用契約から定数下限を算出し、ゼロサイズ・Option・借用結果を検証する。

<a id="a11"></a>
#### A11. `range` / `!range`

- **説明**：引数・結果や load の値域を伝える。実行時に値を検査する仕組みではない。
- **メリット**：不可能な分岐と重複範囲チェックを減らす。
- **デメリット**：無効値を受け入れる境界に付けると必要な検査を消す。
- **可否・注意**：有効な格納 bool、Unicode scalar の char、enum tag、非負長さが候補。char の surrogate 除外、空 enum、nullable 表現を正確に扱う。
- **状態**：未付与。
- **今後**：validity が確立した load から開始。FFI 入力や runtime 検査前の値へ誤って付けないことを確認する。

<a id="a12"></a>
#### A12. `returned`

- **説明**：特定の引数と同じ値を LLVM の戻り値として返す。
- **メリット**：forwarding helper を越える値の伝播を助ける。
- **デメリット**：論理的な関連性を物理値の同一性と取り違えやすい。
- **可否・注意**：同じ Origin、同じ allocation、field projection だけでは不足。すべての正常 return が該当する必要がある。
- **状態**：未付与。
- **今後**：単純な forwarding helper の候補を抽出し、LLVM が既に除去できるか確認する。

### 3.2. 算術・アドレス計算

<a id="b01"></a>
#### B01. `nuw`

- **説明**：該当命令が unsigned の意味で overflow しないと保証する。
- **メリット**：長さ・容量・loop index・byte offset の推論を強める。
- **デメリット**：overflow 時に poison となり、必要な Abort を代用できない。
- **可否・注意**：事前の範囲証明が必要。配列なら `0 <= i < N`、非負 stride、`N * stride` の上限、入力の定義性を確認する。signed 型でも unsigned の証明は別。
- **状態**：Ryu に使用。通常算術は overflow intrinsic、Wrapping は plain 命令。
- **今後**：内部の長さ・offset 計算から限定導入。Wrapping の初期形は維持し、後段の LLVM 推論と区別する。

<a id="b02"></a>
#### B02. `nsw`

- **説明**：該当命令が signed の意味で overflow しないと保証する。
- **メリット**：大小比較、loop induction、符号拡張の簡略化を助ける。
- **デメリット**：通常整数の「overflow なら Abort」を、UB に弱める危険がある。
- **可否・注意**：入力範囲または独立した事前検査が必要。`nsw` 付き結果を使ってその演算の overflow を検出しない。左 shift の bit 切り捨ては仕様上合法であり、一律付与不可。
- **状態**：Ryu に使用。一般の checked 算術・Wrapping には未付与。
- **今後**：B01 と共通の証明表現を用意し、signed/unsigned 境界・最小値の否定・shift を別々に検証する。

<a id="b03"></a>
#### B03. `inbounds` / GEP の `nusw`, `nuw`

- **説明**：`inbounds` は allocation に対するアドレス範囲と計算条件を保証する。`nusw`/`nuw` は GEP 固有の非 wrap 条件。`inbounds` は `nusw` を含意する。
- **メリット**：pointer comparison、別名解析、loop address 計算を助ける。
- **デメリット**：論理的な添字範囲だけで付けると、基底領域・途中計算の誤りを隠す。
- **可否・注意**：安全な field/fixed-array address が有望。Array/Slice は buffer の由来・範囲も必要。末尾の一つ先を計算できても dereference は別。ゼロ変位、null、負変位、ゼロ stride を区別する。
- **状態**：Ryu に `inbounds`/`nuw`。通常 emitter は未付与。IMPL §21.5.3 は raw pointer の初期形にこれらを付けないと規定する。
- **今後**：storage の由来と証明済み GEP flags を lowering から渡す。raw pointer は SPEC §5.3 との対応を証明し、初期形の方針を変えるなら実装仕様も更新する。

<a id="b04"></a>
#### B04. `exact`

- **説明**：除算の余り、または右 shift で捨てる bit がゼロである。
- **メリット**：除算・shift・後続演算の簡略化。
- **デメリット**：通常の端数切り捨てを許さなくなる。
- **可否・注意**：定数倍や mask などから個別に証明した箇所のみ。ゼロ除算と signed 最小値/-1 の検査は別。
- **状態**：未付与。
- **今後**：内部 bit 操作の候補を探し、LLVM が既に推論する範囲を確認する。

<a id="b05"></a>
#### B05. `trunc nuw/nsw`

- **説明**：unsigned/signed の該当する解釈で、切り詰めても値が保存される。
- **メリット**：拡張・縮小の往復や比較を簡略化。
- **デメリット**：意図的な切り捨てに付けると正しい値を poison にする。
- **可否・注意**：range 検査後の numeric conversion が候補。`@wrap` の一般形は対象外。
- **状態**：Ryu に限定使用。
- **今後**：変換の成功分岐と定数の証明を再利用し、変換前の検査順序を保持する。

<a id="b06"></a>
#### B06. `nneg`

- **説明**：`zext`/`uitofp` などの変換元が、その LLVM bit 幅で非負である。
- **メリット**：signed/unsigned 変換の選択・簡略化を助ける。
- **デメリット**：ソースの unsigned 型と混同すると上位半分の値を失う。
- **可否・注意**：符号 bit がゼロと証明した値のみ。`i1 true` も signed 解釈では負なので自動付与しない。
- **状態**：Ryu の `zext` に使用。
- **今後**：検査済み index や小さい tag の変換で、既存推論との差を確認する。

<a id="b07"></a>
#### B07. `or disjoint`

- **説明**：両入力で同じ位置の bit が同時に 1 にならない。
- **メリット**：OR と加算の置換など、bit packing の最適化。
- **デメリット**：bit mask の証明が崩れると poison が発生する。
- **可否・注意**：互いに異なる bit 領域への packing のみ。メモリ領域の非重複とは無関係。
- **状態**：Ryu に使用。
- **今後**：mask/shift が定数である内部 helper から候補を選ぶ。

<a id="b08"></a>
#### B08. `icmp samesign`

- **説明**：整数比較の両入力の符号が等しい。
- **メリット**：signed/unsigned 比較の整理。
- **デメリット**：同じ型でも値の符号は異なり得る。
- **可否・注意**：両方が非負など、比較地点の range 証明が必要。
- **状態**：Ryu に使用。
- **今後**：長さ・容量の比較を候補とし、型名だけで付けないことを検証する。

### 3.3. 確保・寿命・局所的な別名情報

<a id="c01"></a>
#### C01. allocator の戻り値・関数属性

- **説明**：戻り値 `noalias` は新しい独立領域、`allocsize` は最低確保量、`allockind`/`alloc-family` は確保・解放操作の種類と対応を伝える。
- **メリット**：新旧領域の分離、確保サイズ推論、allocation 最適化。
- **デメリット**：強い allocator 認識は確保の削除・結合へ影響し、失敗や観測の契約確認が増える。
- **可否・注意**：`__kimi_alloc` の正常 return は `noalias nonnull align 16` と `allocsize(0)` の有望候補。借用結果は対象外。共有 substitute、解放 wrapper の特殊扱い、Abort 条件を確認する。
- **状態**：`__kimi_alloc` は最大サイズを検査し、`max(size, 1)` を確保、失敗時 Abort。対象属性は未付与。
- **今後**：まず戻り値とサイズの保証を検証。`allockind`/`alloc-family` は確保・解放の最適化と必要な失敗動作の関係を調べてから判断する。

<a id="c02"></a>
#### C02. `nofree`

- **説明**：引数属性は対象 pointer の解放、関数属性は呼び出し前からある allocation の解放を制限する。
- **メリット**：call を越える pointer validity の推論。
- **デメリット**：直接の storage と内部所有領域を混同しやすい。
- **可否・注意**：借用元 slot を解放しないことと、関数が buffer を解放しないことは別。Array の reserve/clear や destructor を含めて判断する。
- **状態**：未付与。
- **今後**：safe borrow の直接 storage と既知 helper を候補にし、関数全体への拡張は効果解析後に行う。

<a id="c03"></a>
#### C03. `llvm.lifetime.start/end`

- **説明**：物理 storage の有効期間を LLVM に示す。
- **メリット**：stack slot の再利用、不要 store の削除。
- **デメリット**：早い end は cleanup や残存参照を無効にする。
- **可否・注意**：IMPL §21.5.5 に導入余地あり。最後の read、Origin 表記、Move だけでは終点を決めない。zero-sized 値の論理的な destruction は残す。
- **状態**：未付与。初期化・cleanup の計画は存在する。
- **今後**：単純な local slot から開始し、branch、loop、defer、借用結果、部分 Move を順に確認する。

<a id="c04"></a>
#### C04. `constant` / invariant 系

- **説明**：`constant` は不変な global。`invariant.load` は対象 memory の不変性、`invariant.start/end` は不変期間を伝える。
- **メリット**：定数化、再 load の削減。
- **デメリット**：再初期化・storage 再利用を不変と誤認すると不正。
- **可否・注意**：文字列 backing、変更されない metadata が候補。`let` の binding 不変性だけでは永続的な memory 不変性を証明できない。
- **状態**：文字列・metadata・Ryu table などに `constant`。invariant 系は未付与。
- **今後**：真に不変な metadata の load を先に検討。一般 local への適用は lifetime と統合して判断する。

<a id="c05"></a>
#### C05. `alias.scope` / `noalias` metadata

- **説明**：load/store/call のアクセス集合間に局所的な非重複を示す。
- **メリット**：引数属性だけでは表せない分割領域・Loan の情報を利用できる。
- **デメリット**：scope の寿命、Reborrow、inlining、loop 複製の管理が難しい。
- **可否・注意**：Storage の分割や disjoint element が候補。親子 Loan や次の反復へ保証を無条件に延長しない。
- **状態**：未付与。
- **今後**：A01 の後に検討。必要なら `llvm.experimental.noalias.scope.decl` を用い、反復ごとの scope とアクセス範囲を検証する。

<a id="c06"></a>
#### C06. TBAA / `!tbaa`

- **説明**：言語の型・storage 規則に対応する別名関係を metadata にする。
- **メリット**：LLVM pointer 型だけでは分からない非重複を伝達できる。
- **デメリット**：不正な型分類は広い範囲の load/store を誤って独立化する。
- **可否・注意**：現行仕様から「異なる型名なら alias しない」とは結論できない。raw cast、byte access、基底部分、padding、storage 再利用を含む別名モデルが必要。
- **状態**：未付与。採用モデルは未設計。
- **今後**：合法な cross-type access を列挙し、A01/C05 で不足する効果があるか測定してから採否を決める。

<a id="c07"></a>
#### C07. `writable`

- **説明**：dereferenceable な範囲に、entry 時の読み取り・同値書き戻しを追加できると保証する。
- **メリット**：store の配置変更を助ける。
- **デメリット**：追加の書き込みが安全という、強い前提が必要。
- **可否・注意**：書き込み権限、範囲、競合不存在を証明した内部引数のみ。`readonly`/`readnone` と混在させない。
- **状態**：未付与。
- **今後**：`uniq` の通常属性より後に、効果のある具体例と必要な前提を確認する。

<a id="c08"></a>
#### C08. `initializes`

- **説明**：指定した byte 範囲が関数によって初期化されることを保証する。
- **メリット**：出力領域の旧値への依存を減らす。
- **デメリット**：部分初期化、失敗経路、padding の扱いが複雑。
- **可否・注意**：初期化専用 helper が候補。Kimigayo の論理的な Initialized 状態から、全 byte が定義済みとは推論しない。
- **状態**：未付与。
- **今後**：LLVM 22 の正確な範囲・終了経路の要件に合わせた最小例を作り、初期化計画との対応を確認する。

<a id="c09"></a>
#### C09. `dead_on_return`

- **説明**：関数終了後に対象メモリの旧内容を使用できないという契約を伝える。
- **メリット**：不要な終了前 store などを削減できる。
- **デメリット**：呼び出し後の参照や観測が残ると不正。
- **可否・注意**：内部の消費済み領域に限定して検討。Move だけで物理 storage 全体が死ぬとは判断しない。
- **状態**：未付与。
- **今後**：残存参照・cleanup・再利用を含む非観測を証明できる例が出るまで保留する。

### 3.4. 呼び出し・制御フロー

<a id="d01"></a>
#### D01. `noreturn`

- **説明**：関数が正常には戻らない。
- **メリット**：不要な後続経路・値の除去。
- **デメリット**：誤付与すると正常 return が不正になる。
- **可否・注意**：Never、Abort、Exit に適用可能。無副作用や即時終了を意味せず、診断出力も残す。
- **状態**：`FunctionAbi.NoReturn` と ExitProcess 宣言などで実装あり。
- **今後**：新たな entry/adapter の追加時に伝播を確認し、Abort と非停止の区別を維持する。

<a id="d02"></a>
#### D02. `nounwind`

- **説明**：例外による unwind を行わない。
- **メリット**：unwind を考慮した最適化制約を減らす。
- **デメリット**：外部 call の契約を誤認すると不正。
- **可否・注意**：本体と callee を確認した範囲で可能。IMPL §21.5.5 は LibraryImport の no-unwind 契約だけからの機械的推論をしない。OS stack walking 用の `uwtable(async)` は別に維持する。
- **状態**：一般付与なし。共通属性群には `uwtable(async)` がある。
- **今後**：閉じた helper から検討。全体方針を変える場合は実装仕様との整合も確認する。

<a id="d03"></a>
#### D03. `willreturn`

- **説明**：呼び出しを含む既存 call stack へ制御が戻ると保証する。
- **メリット**：不要 call の除去や終了性の推論を助ける。
- **デメリット**：合法な非停止や Abort を消す危険がある。
- **可否・注意**：有限処理と全 callee の性質を証明した関数のみ。`memory(none)` や非再帰だけでは十分でない。
- **状態**：未付与。一般付与しないことを確認する既存テストがある。
- **今後**：有限な scalar helper を候補とし、無限 loop・Abort・間接 call の対照例を残す。

<a id="d04"></a>
#### D04. `mustprogress` / `llvm.loop.mustprogress`

- **説明**：関数／loop に、終了または環境への観測可能な進行を要求する。
- **メリット**：副作用のない無用な loop の除去。
- **デメリット**：合法な非停止を不正に扱い得る。
- **可否・注意**：IMPL §21.5.5 により一般関数・loop への一律適用不可。個別に進行を証明できる範囲のみ。
- **状態**：未付与。
- **今後**：一般付与はしない。有限性などが確立した特定 loop で効果が必要な場合だけ検討する。

<a id="d05"></a>
#### D05. `nosync`

- **説明**：他 thread との同期を行わない。
- **メリット**：memory access の移動・解析を助ける。
- **デメリット**：runtime/OS 内の同期を取りこぼす危険がある。
- **可否・注意**：言語の thread 機能が未実装でも根拠にならない。現行 process heap は同期有効。順序を持つ atomic、FFI、将来の arc も確認対象。
- **状態**：未付与。
- **今後**：OS call を持たない helper から、LLVM の推論と必要な追加契約を確認する。

<a id="d06"></a>
#### D06. `norecurse`

- **説明**：直接・間接の再帰に参加しない。
- **メリット**：関数間解析や stack に関する最適化を助ける。
- **デメリット**：間接 call の追加で証明が無効になる。
- **可否・注意**：直接自己 call の不存在だけでは不足。call graph の循環と未知の entry を確認する。
- **状態**：未付与。
- **今後**：閉じた call graph に対する LLVM の既存推論を先に評価する。

<a id="d07"></a>
#### D07. `speculatable`

- **説明**：副作用や UB を生まずに結果だけを計算する関数である。
- **メリット**：分岐を越えた先行実行を可能にする。
- **デメリット**：本来未実行の経路で失敗やアクセスを起こし得る。
- **可否・注意**：通常の checked 算術、危険な load、I/O は対象外。終了性や call 回数の観測可能性も別に確認する。
- **状態**：未付与。
- **今後**：小さな全域計算だけを候補にし、short-circuit や未選択分岐の失敗が表面化しないことを検証する。

### 3.5. 性能ヒント・最適化情報

<a id="e01"></a>
#### E01. `cold` / 分岐重み `!prof`

- **説明**：`cold` は低頻度関数、`!prof` は分岐などの頻度情報を示す。
- **メリット**：Abort などを通常経路から離し、code layout を改善する。
- **デメリット**：頻度の見積もりが外れると遅くなる。
- **可否・注意**：Abort/診断経路が有望。低頻度は到達不能を意味せず、必要な処理を消す根拠にはしない。
- **状態**：未付与。
- **今後**：正常経路・失敗経路の実測を取り、固定の重みと profile のどちらが適切か比較する。

<a id="e02"></a>
#### E02. `noinline` / `alwaysinline` / `inlinehint`

- **説明**：それぞれ展開を禁止、強く要求、推奨する。
- **メリット**：大きい診断処理の分離や、小さい adapter の呼び出し負担削減。
- **デメリット**：展開禁止は最適化を阻害し、展開過多はコード量・compile time・register pressure を増やす。
- **可否・注意**：個別に測定して選択。一律 `alwaysinline` は不要という現行方針を維持する。
- **状態**：明示的な一般付与なし。O2 の inliner を使用。
- **今後**：adapter と Abort helper で O2 の既存判断との差を測る。

<a id="e03"></a>
#### E03. `optsize` / `minsize`

- **説明**：速度よりコードサイズを重視する。`minsize` はさらにサイズを優先する。
- **メリット**：binary と instruction cache の負担を減らせる。
- **デメリット**：throughput や latency が悪化し得る。
- **可否・注意**：補助処理や低頻度関数に限定して検討する。
- **状態**：未付与。
- **今後**：実行時間と native code size を同時に記録して採否を決める。

<a id="e04"></a>
#### E04. `unnamed_addr` / `local_unnamed_addr`

- **説明**：アドレスの identity が重要でないことを示す。後者は module 内の性質に限定する。
- **メリット**：定数・同内容実体の共有を促進する。
- **デメリット**：identity のある実体を統合すると意味が変わる。
- **可否・注意**：文字列 backing は仕様上適用可能。object、descriptor、function pointer のアドレス観測は個別に確認する。
- **状態**：文字列/Ryu table に `unnamed_addr`、Ryu 関数に `local_unnamed_addr`。
- **今後**：metadata の identity の根拠を整理してから対象を広げる。

<a id="e05"></a>
#### E05. loop の vectorize/unroll metadata

- **説明**：ベクトル化・interleave・展開などの変換方針を伝える。
- **メリット**：固定長 loop や大量処理の throughput 改善が期待できる。
- **デメリット**：コード肥大化、compile time、register pressure が増え得る。
- **可否・注意**：ヒントと変換の合法性は別。checked 算術の失敗順序、destructor、memory 依存を保存する。
- **状態**：明示 metadata なし。O2 の判断を使用。
- **今後**：最適化 remark と native code を確認し、拒否理由が情報不足か費用判断かを分けて対処する。

<a id="e06"></a>
#### E06. `llvm.access.group` / `llvm.loop.parallel_accesses`

- **説明**：access group で命令を分類し、parallel_accesses で対象 group の反復間 memory 依存がないと保証する。
- **メリット**：loop vectorizer に独立性を伝えられる。
- **デメリット**：誤った独立性は反復の順序を壊す。単なる性能ヒントではない。
- **可否・注意**：`uniq` でも `a[i] = a[i - 1] + 1` は反復間依存を持つ。独立な要素範囲と実際の read/write を証明する。
- **状態**：未付与。
- **今後**：Storage の分割証明とアクセス計画が揃った loop に限定する。loop 全体の他の副作用も別に確認する。

<a id="e07"></a>
#### E07. `!nontemporal`

- **説明**：対象データを近い将来に再利用しないことを示す cache 方針。
- **メリット**：大きな連続書き込みで cache 汚染を減らせる。
- **デメリット**：再利用する workload では性能を落とす。
- **可否・注意**：型や借用モードからは決めない。通常の初期値にはしない。
- **状態**：未付与。
- **今後**：十分大きい streaming workload が出たとき、サイズと再利用条件を固定して測定する。

<a id="e08"></a>
#### E08. `llvm.assume`

- **説明**：既に成立すると証明した条件を LLVM に伝える。
- **メリット**：通常の属性・CFG で伝わらない range や alignment を補足する。
- **デメリット**：偽の条件は不正。大量追加は解析時間や最適化を悪化させ得る。
- **可否・注意**：必須 runtime check の代用不可。成功分岐の情報を二重に記すだけなら効果を確認する。
- **状態**：未付与。
- **今後**：通常の型・属性・CFG では足りない具体的な missed optimization がある場合に限定する。

### 3.6. 適用を制限する項目

<a id="f01"></a>
#### F01. `undef`

- **説明**：各使用で異なる値になり得る LLVM の未定義値。
- **メリット**：非観測部分の値を固定しなくてよいが、通常の値生成での実益は小さい。
- **デメリット**：値の安定性を失い、未初期化検査の欠陥を隠す。
- **可否・注意**：未解決値・未初期化変数・Move 済み値の代用品には不可。LLVM も新たな用途の拡大を推奨していない。
- **状態**：明示的な生成を確認せず。
- **今後**：積極導入しない。例外が必要なら、非観測性と F02/F03 で代替できない理由を記録する。

<a id="f02"></a>
#### F02. `poison`

- **説明**：未定義な値が伝播し、特定の使用で UB となる LLVM の値。
- **メリット**：aggregate 構築で不要な初期値を定義せずに済む。
- **デメリット**：未設定 field が分岐・結果・`noundef` 境界へ漏れると不正になる。
- **可否・注意**：全ての観測 field を使用前に埋める構築用の値などに限定。Abort、未確定な言語の意味、初期化エラーの代用不可。
- **状態**：整数整形の `{ i128, i8 }` 構築で、全 field を `insertvalue` した後に return。
- **今後**：この限定用途を維持。分岐を伴う構築へ広げる際は全経路の定義性を検証する。

<a id="f03"></a>
#### F03. `freeze`

- **説明**：undef/poison を一つの任意値に固定し、その結果の各使用で同じ値を使う。
- **メリット**：LLVM 内の特定の変換で不定性・poison の伝播を制御できる。
- **デメリット**：必要な正解値や Abort を復元できず、コードや推論を複雑にする。
- **可否・注意**：誤った `nsw`、未初期化値、未実装 semantics の修復には不可。
- **状態**：未付与。
- **今後**：具体的な正当化された IR 変換が必要になるまで emitter への導入を保留する。

<a id="f04"></a>
#### F04. fast-math / `nofpclass`

- **説明**：`reassoc` は再結合、`contract` は融合、`arcp`/`afn` は逆数・関数近似、`nsz` はゼロの符号の緩和、`nnan`/`ninf` は NaN/無限大の排除。`fast` は複数の許可の集合。`nofpclass` は引数・結果の FP class を制限する。
- **メリット**：FP 演算・比較・vectorization の自由度が増える。
- **デメリット**：丸め、NaN、無限大、signed zero、`@bits` で観測する結果を変え得る。
- **可否・注意**：一般 fast-math は現行仕様に不適合。個別の値域保証は可能性があるが、finite な入力から finite な結果は導けない。`nofpclass` は有限性などを証明した境界だけを候補とする。
- **状態**：一般 emitter は通常の FP 命令。`denormal-fp-math=ieee,ieee` を使用。対象 flags/`nofpclass` は未付与。
- **今後**：一括導入しない。初期 emitter の方針変更は IMPL §21.5.4 と整理し、NaN・signed zero・subnormal・bit conversion を検証する。

<a id="f05"></a>
#### F05. ABI 関連：`sret`, `byval`, `signext`, `zeroext`, `fastcc`

- **説明**：それぞれ結果 slot、引数の隠れたコピー、整数の符号/ゼロ拡張、内部 calling convention を指定する。
- **メリット**：適切な ABI なら受け渡しや register 利用を改善できる。
- **デメリット**：caller/callee 不一致、不要なコピー、storage identity の変化を起こし得る。
- **可否・注意**：最適化 flag の追加ではなく ABI 変更として検討。`byval` は言語の Copy/Move を授けない。外部 Windows/C ABI と内部 ABI を分ける。
- **状態**：通常は ccc、aggregate は compiler 管理の slot。`ResultSlot` という内部分類は LLVM の `sret` 属性とは別。
- **今後**：属性導入の共通基盤ができた後、caller・adapter・runtime・cache を含む一つの ABI 単位として測定・検証する。

## 4. 段階的な進め方

これは技術的な依存順序。現在の milestone を変更せず、実際に着手するときに [PLAN.md](PLAN.md) へ必要な単位だけ反映する。

| 段階 | 範囲 | 完了条件 |
| --- | --- | --- |
| 0. 基準の確認 | 代表 workload の初期 IR・O2 IR・native code、既存属性、時間・サイズ・allocation | 同じ source/configuration の基準と未確認範囲を保存。LLVM が既に推論する属性を特定 |
| 1. 借用と ABI | A01–A03、A07–A10 | 通常 borrow の保証を ABI と cache に保持し、直接・間接 call、generic entry、adapter の整合と境界例を検証 |
| 2. 範囲と address | A11、B01–B03。必要な B04–B08 | 必須チェックを保存し、証明のある命令だけへ付与。zero size、整数境界、pointer provenance を検証 |
| 3. runtime と物理 lifetime | A05–A06、C01–C03、E01 | helper ごとの契約・失敗経路・cleanup・再利用を検証。性能効果を別途測定 |
| 4. 関数効果と局所別名 | A04、C04–C05、D02–D07、E06 | 未知の call を保守的に扱い、scope と反復間独立性を証明。一般属性の誤付与を防ぐ |
| 5. 測定に基づく調整 | C06–C09、E02–E05、E07–E08、F05 | missed optimization と費用を説明できる場合だけ採用。F01/F03/F04 は制限を維持 |

### 共通の実装構造

- **ABI**：物理型に加え、借用モード・属性 mask・定数 alignment/byte 範囲を保持する。引数、結果、関数効果は別管理。`ParameterShape` と cache の一致判定にも含める。
- **出力の統一**：定義と直接 call は既に `AbiParameter.Attributes` を利用する。一方、現在の closure adapter と間接 call には属性を出力しない経路がある。共通出力へ揃える。欠落だけで直ちに UB ではないが、強すぎる付与や ABI 不一致は避ける。
- **命令**：lowering が確定した証明を `EmissionInstruction` へ渡す。writer が Binding・syntax・ownership を読み直して推測しない。
- **効果**：既存 `EffectSummary` は主に Iterator/BufferWriter の公開制約用。LLVM の一般的な memory/capture/termination 属性と同一視しない。追加解析は未知を保守的に扱い、LLVM の既存推論と重複させすぎない。
- **費用**：属性の文字列化を命令ごとに再計算しない。既存の再利用可能な計画・cache を使い、warm allocation 回帰を維持する。
- **仕様更新**：raw pointer の初期 GEP、Wrapping の初期算術、FP 方針など、現行実装仕様が形を限定する場合は、採用時に所有章を更新する。本書だけで例外を追加しない。

## 5. 検証と台帳の更新

### 5.1. 必須の確認

| 分類 | 代表的な境界・既存テストの入口 |
| --- | --- |
| 借用・ABI | 同じ shared input、独立する uniq、親子 Reborrow、stored reference、capture/static、予約中の read、戻り値の依存、直接/間接/generic/adapter。`ReferenceEmissionTest`、`CallReservationTest`、`StoredReferenceLoanTest`、`AggregateValueCallTest` |
| 算術・pointer | 最小/最大値、signed/unsigned、Wrapping、左 shift、負変位、null+0、zero stride、one-past、未選択分岐。`ScalarEmissionTest`、`WrappingIntegerTest`、`BitwiseEmissionTest`、`ElementEmissionTest`、`ForeignEmissionTest` |
| lifetime・副作用 | 部分初期化/Move、loop での slot 再利用、cleanup が読む最後の値、Abort 時の非 cleanup、allocation failure、indirect call。`DeferredEmissionTest`、`AggregateResultEmissionTest`、`DynamicArrayCleanupTest` |
| FP | NaN、無限大、±0、subnormal、丸め、bit pattern、generic equality。`FloatEmissionTest`、`FloatConversionEmissionTest`、`BitConversionTest`、`Utf8FloatTest` |

既存クラスは選定の入口であり、全候補の検証完了を意味しない。属性を付ける例と、付けてはいけない正常な例を対にする。契約違反の IR を実行して「たまたま正しく動く」ことを証拠にしない。

### 5.2. 完了の判断

1. **意味の確認**：LLVM の同じ版の定義と、対応する SPEC/IMPL の保証を記録する。属性文字列の存在だけで完了にしない。
2. **IR と動作**：最適化前後の verifier、関係する native O0/O2、Abort の reason/location、評価・cleanup 順序を確認する。verifier は属性の意味の正しさを証明しない。
3. **正式検証**：実装 unit は [VERIFICATION.md](VERIFICATION.md) と AGENTS.md の Unit/Session 手順に従う。関係する allocation/reuse 回帰を含め、診断経路の変更は DIAGNOSTICS の workflow も適用する。NativeAOT は明示指示がない限り実行しない。
4. **測定**：同一 workload、固定 warm-up/反復条件で速度・code size・compile cost を必要に応じて測る。タイミング測定は `src/Benchmark`、証拠は `artifacts/benchmarks/`。一般に速くなったと推測だけで記さない。
5. **記録**：成功・失敗を `artifacts/verify/` に保存。対象 source/configuration、LLVM の実体、対象 ID、日時、検証範囲、残る不確実性を結び付ける。両 artifacts root は Git 管理外なので別途保持する。

### 5.3. 検証記録

以下へ追記し、対応する一覧・詳細の状態も同時に更新する。過去の成功・失敗を上書きしない。機能検証と性能測定は別の列に記録する。

| ID・範囲 | 確認/検証日時（JST） | source・LLVM・構成 | 実装/機能検証 | 性能測定 | 証拠・未確認事項 |
| --- | --- | --- | --- | --- | --- |
| A01–F05：初回調査 | 2026-10-01 19:46（静的確認） | 参照時 HEAD `cdc62ddad4a4e6229e468e1a68b7146fe529595a` の作業ツリー。設定 LLVM 22.1.8 | 仕様/コード/テスト読解のみ。実装変更なし。実行検証日時：— | 未測定 | §6 の参照先。属性追加後の正しさ、O2 の推論結果、toolchain 実体は未検証 |

新しい記録には、対象 ID、変更 commit（未コミットなら snapshot/hash）、実際の LLVM version/identity、Release/Debug、O0/O2、OS/CPU、実行した test/fixture、失敗した試行、artifact path、残る課題と次の作業を必要な範囲で記す。

## 6. 根拠と実装の入口

### 6.1. Kimigayo の仕様・実装

- [SPEC.md](../SPEC.md) / [IMPL.md](../IMPL.md)：規範の入口。[Chapter 21](../impl/21-layout-runtime-and-code-generation.md) の §21.2.4、§21.3.3.4、§21.4、§21.5 が主な根拠。
- [所有権・借用](../spec/15-ownership-and-lifetime-analysis.md)：§15.6.4 の call-wide 保護、§15.6.7 の reservation/activation。
- [raw pointer](../spec/05-raw-pointers-and-unsafe-memory.md)：アクセスの権限、provenance、変位の契約。
- [演算](../spec/13-operators-and-assignment.md) / [失敗](../spec/17-failure-handling.md)：checked/Wrapping、FP、Abort と評価順序。
- [runtime/FFI](../spec/22-core-execution-and-foreign-functions.md)：§22.3 の外部境界、§22.5 の allocation と失敗。
- [CODEMAP.md](CODEMAP.md)：変更箇所と代表テストの探索。機能や責務を変更した場合は対応項目も更新する。
- [WindowsLowering.cs](../../src/Kimi/Compiler/Emission/WindowsLowering.cs)、[FunctionAbi.cs](../../src/Kimi/Compiler/Emission/FunctionAbi.cs)、[FunctionAbiPool.cs](../../src/Kimi/Compiler/Emission/FunctionAbiPool.cs)：物理表現・属性・ABI cache。
- [EmissionModule.cs](../../src/Kimi/Compiler/Emission/EmissionModule.cs)、[BodyLowering.Graph.cs](../../src/Kimi/Compiler/Emission/BodyLowering.Graph.cs)、[BodyLowering.Pointers.cs](../../src/Kimi/Compiler/Emission/BodyLowering.Pointers.cs)：確定した命令計画と算術/pointer lowering。
- [LlvmModuleWriter.cs](../../src/Kimi/Compiler/Emission/LlvmModuleWriter.cs)、[Closures](../../src/Kimi/Compiler/Emission/LlvmModuleWriter.Closures.cs)、[Scalars](../../src/Kimi/Compiler/Emission/LlvmModuleWriter.Scalars.cs)、[Elements](../../src/Kimi/Compiler/Emission/LlvmModuleWriter.Elements.cs)：属性・call・命令の出力。
- [Binding.EffectBounds.cs](../../src/Kimi/Compiler/Binding/Binding.EffectBounds.cs)：既存の契約別 effect 検査。一般的な LLVM memory 属性の解析ではない。
- [WindowsRuntime.ll.in](../../src/Kimi/Compiler/Emission/WindowsRuntime.ll.in)、[Utf8BufferRuntime.ll.in](../../src/Kimi/Compiler/Emission/Utf8BufferRuntime.ll.in)、[Utf8FormatRuntime.ll.in](../../src/Kimi/Compiler/Emission/Utf8FormatRuntime.ll.in)：runtime、memory intrinsic、poison の限定使用。
- [Utf8FloatRyu.ll.in](../../src/Kimi/Compiler/Emission/Utf8FloatRyu.ll.in) / [generate-ryu.ps1](../../src/backend/windows-x64/generate-ryu.ps1)：Clang 生成の属性・算術 flags。生成ファイルを手編集しない。
- [LlvmConstantPool.cs](../../src/Kimi/Compiler/Emission/LlvmConstantPool.cs) / [NativeToolchain.cs](../../src/Kimi/Compiler/Emission/NativeToolchain.cs)：定数、O2 pipeline、前後の IR verification。
- [テスト一覧](../../tests/xUnitTest/Tests/)：§5.1 のクラスから関係するケースを選ぶ。

### 6.2. LLVM の一次資料

- [LLVM 22.1.8 LangRef](https://github.com/llvm/llvm-project/blob/llvmorg-22.1.8/llvm/docs/LangRef.rst)：属性・命令・metadata の正確な条件。版を固定して参照する。
- [Undefined Behavior](https://llvm.org/docs/UndefinedBehavior.html)：undef、poison、freeze、UB と変換の注意。
- [GetElementPtr](https://llvm.org/docs/GetElementPtr.html)：論理添字と allocation 境界、pointer 計算の区別。
- [Alias Analysis](https://releases.llvm.org/22.1.0/docs/AliasAnalysis.html)：別名と memory effects。
- [Frontend Performance Tips](https://releases.llvm.org/22.1.0/docs/Frontend/PerformanceTips.html)：情報の渡し方、assume、費用の注意。
- [Transform Metadata](https://releases.llvm.org/22.1.0/docs/TransformMetadata.html) / [Vectorizers](https://releases.llvm.org/22.1.0/docs/Vectorizers.html)：loop 方針と vectorization の条件。
- [LLVM Passes](https://releases.llvm.org/22.1.0/docs/Passes.html)：関数属性推論など。補助資料の表記と 22.1.8 の定義が異なる場合は、固定版 LangRef と実ツールを優先する。
