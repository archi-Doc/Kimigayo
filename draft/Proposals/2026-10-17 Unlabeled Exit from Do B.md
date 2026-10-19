# `do` のラベルなし `exit`（案B）— 仕様変更案・実装計画

2026-10-19 作成。[案A](<../Obsolete/2026-10-17 Unlabeled Exit from Do.md>)の改訂案で、2026-10-20 に所有者の指示で受付し、案Aを置き換えた。
正式仕様とコンパイラーは変更していない。実装は案Aの決定どおり、**R9完了後**に始める。

## 1. 概要

### 1.1. 目的

値を返す `do` から、ラベルなしの `exit` で抜けられるようにする。値を返すためだけのラベルを不要にする。

```kimi
let size = do
    if value > 100 => exit 100
    exit value
```

ただし、結果を捨てる `do`（スコープを区切るための `do`）が、外側宛ての `exit` を黙って受け取らないようにする。

### 1.2. 規則

1. **対象の決定**: ラベルなし `exit` は、最も内側の `for`・`while`・`loop`・`do`・Deferred Block を対象とする。変更点は `do` の追加だけである。
2. **可否の検査**: 対象が Discard Context の `do` なら、その `exit` はエラーとする。外側を探し直すことはしない。

2は、ラベルなし `yield` の既存規則（[§14.5.2](../../docs/spec/14-control-flow.md#1452-targets-and-barriers)）と同じ規則である。
選択（`if`・`match`）と `do` がラベルなし転送を受け取れるのは、その結果が使われる場合に限る。

### 1.3. 案Aとの違い

| 項目 | 案A | 案B |
| --- | --- | --- |
| 対象の決定 | ラベル・オペランド・型・文脈によらない | 同じ |
| 結果を捨てる `do` へのラベルなし `exit` | Unit の `exit` なら受理 | エラー |
| 結果を捨てる `do` の中の既存コード | 対象が変わっても、Unit 同士なので型検査で検出できない | すべてエラーとして検出できる |
| 値を使う `do` の中の既存コード | 対象が変わる | 対象が変わる（U0で列挙する。§3.2） |
| `yield` の規則との関係 | 食い違う | 同じ規則になる |

文脈によらず対象を決める点は、案Aから変えない。案Bでは、文脈は転送が有効かどうかだけを決める。

### 1.4. 原則との対応

| 原則 | 本書での適用 |
| --- | --- |
| One Concept, One Canonical Form | 既存の `exit` を使う。結果を捨てる構文へのラベルなし転送を、`yield` と同じ一つの規則で扱う。 |
| Local Reasoning | 対象は構文の入れ子だけで決まる。スコープ用の `do` で囲んでも、中の `exit` の行き先は黙って変わらない。 |
| Explicit Semantics | 早期終了は `exit`、外側の対象は `exit to`、Non-Copy値の転送は `@move` で示す。 |
| Compiler Server Protocol | 解決した対象をHIRの対象行で一度だけ公開する。無効な転送の診断は、同じ可否判定から既存の診断規則で出す。 |

## 2. 仕様

例は独立した断片である。エラーと記した例は、不受理であることを要求する。

### 2.1. 対象の決定

| 制御転送 | ラベルなしの対象 | 名前付きの対象 |
| --- | --- | --- |
| `exit` | `for`・`while`・`loop`・**`do`**・Deferred Block | `for`・`while`・`loop`・`do` |
| `continue` | `for`・`while`・`loop` | 同左 |
| `yield` | `if`・`match` | 同左 |
| `return` | 関数 | なし |

- 次の条件は対象の選択を変えない。
  - `do` のラベルの有無、および本体が単一項目（`=> item`）かインデント本体か。
  - `exit` のオペランドの有無・型、期待型、到達可能性。
  - `do` が Value Context にあるか Discard Context にあるか。
- 先に対象を決め、その後で可否（§2.2）と結果型（§2.3）を検査する。検査に失敗しても、外側を探し直さない。`exit` は `exit ()` と同じである。
- 構文が対象になるのは、その本体の中だけである。条件・列挙元・ガードの中からは、その構文を対象にできない。
  ただし、それらの位置に書かれた `do` は、その `do` 自身の本体から対象にできる。
- `if`・`match`・`unsafe`・`require` は `exit` の対象にならない。
- 次の境界は、既存の規則を変えない。
  - Function Boundary（関数・クロージャーなど）を越えて転送することはできない。
  - 既定引数の中から、宣言の外側へ転送することはできない。
  - `$expect` / `$require` の `message:` の中から、外側へ転送することはできない（[§17.6.3](../../docs/spec/17-failure-handling.md#1763-message-and-observed-values)）。
  - Deferred Block については §2.5 を参照する。

### 2.2. 結果を捨てる `do` への制限

ラベルなし `exit` の対象が Discard Context の `do` なら、エラーとする。`do` のラベル、本体の形、オペランドの有無は問わない。

- Discard Context は [§14.2.2](../../docs/spec/14-control-flow.md#1422-evaluation-contexts-and-body-results) の定義に従い、本体を検査する前に決まる。
  - たとえば、単独の式、インデント本体の直下の式、反復や Deferred Block の単一項目本体がこれにあたる。
  - 関数の単一項目本体では、戻り値型が Unit に固定されているかどうかにもよる。
  - 括弧やラベルを付けても、文脈は変わらない。
  - 明示的な破棄 `_ = do …` のオペランドは Value Context である（[§14.2.4](../../docs/spec/14-control-flow.md#1424-explicit-discard)）。
    したがって、その中のラベルなし `exit` は受理される。`_ = if …` の中のラベルなし `yield` と同じ扱いである。
- 結果を捨てる `do` を途中で終えるには、ラベルを付けて `exit to` を使う。結果を捨てる選択を `yield to` で終えるのと同じ形である（[§14.7](../../docs/spec/14-control-flow.md#147-if-and-yield)）。
- この制限は、結果を届ける構文（選択と `do`）にだけ課す。反復と Deferred Block には課さない。
  これらをラベルなし `exit` で終えることは、その本来の用途だからである。

```kimi
loop
    do
        let guard = registry.lock()
        if guard.isEmpty() => exit       // エラー: 結果を捨てる do へのラベルなし exit
        guard.flushPending()
    sleep(interval)

label worker: loop
    do
        let guard = registry.lock()      // ロックは do を出るときに解放される
        if guard.isEmpty() => exit to worker
        guard.flushPending()
    sleep(interval)

label scope: do
    if skip => exit to scope             // 有効: 名前付きなら、結果を捨てる do も終えられる
    work()

do => exit                               // エラー
func f() => do => exit                   // エラー: 戻り値型が Unit に固定された関数の単一項目本体は結果を捨てる
let g = func () => do => exit            // 有効: 戻り値型を推論する無名関数の本体は Value Context
```

### 2.3. 結果・所有権・後始末

既存の [§14.2.2](../../docs/spec/14-control-flow.md#1422-evaluation-contexts-and-body-results)・
[§14.9](../../docs/spec/14-control-flow.md#149-result-validation)・第15章・
[§16.2](../../docs/spec/16-scope-exit-and-destruction.md#162-scope-exit) の規則を、新しい対象にもそのまま適用する。

- **結果源**
  - `do` 自身を対象とする `exit` のオペランドが、その `do` の結果源になる。オペランドを省略した場合は Unit が結果源になる。
  - インデント本体の末尾の式は、これまでどおり結果を捨てる。末尾まで通常どおり到達する経路があれば、Unit が結果源になる。
- **型**: Target Result Type・共通型・Never の規則、および到達不能な結果源も検査する規則は変えない。
- **配送**: オペランドを評価して結果を確保し、去るスコープを内側から後始末したうえで、対象へ届ける。
  Non-Copy値の Move、Loan、および後始末が配送を阻止する場合の扱いは、既存の規則に従う。

```kimi
func choose(ready: bool) -> i32
    return do
        if ready => exit 42
        exit 0

let unit: () = do => exit              // 有効。exit () と同じ。
let value = do => exit 42              // 有効。型は i32。

func incomplete(ready: bool) -> i32
    return do
        if ready => exit 42
        ()                             // エラー: 末尾に到達したときの Unit が i32 に合わない。
```

### 2.4. 入れ子とラベル

内側の反復構文や `do` が、ラベルなし `exit` を受け取る。外側の対象を終えるにはラベルを使う。
ラベルを追加・削除しても、ラベルなし転送の対象は変わらない。

```kimi
let first = do
    for value in values
        if value > 0 => exit value     // エラー: 対象は for で、その Target Result Type は Unit。
    exit 0

func findPositive(values: ref/Array<i32>) -> i32
    return label search: do
        for value in values
            if value > 0 => exit to search value
        exit 0                         // 対象は search の do。
```

### 2.5. Deferred Block

- Deferred Block は、Unit を結果とする終了対象であり、外向き転送の境界である。これは変えない。
  名前付き転送で境界を越えることはできず、`defer` にラベルを付けることもできない。
- 本体の中のラベルなし `exit` は、§2.1 に従って最も内側の対象へ向かう。
  - 最も内側の対象が Value Context の `do` なら、その `do` を終える。
  - 最も内側の対象が Discard Context の `do` なら、§2.2 によりエラーとする。
  - 内側に対象がなければ、Deferred Block の本体を終える。
- 内側の `do` から本体全体を終えたい場合は、ラベル付きの `do` で包む。
  `exit to` で抜けた後も、そのスコープの後始末は実行する。保留中の結果や転送が置き換わることはない。

```kimi
defer
    do
        let handle = acquire()
        if handle.closed => exit         // エラー: 結果を捨てる do へのラベルなし exit
        handle.flush()
    log("closed")

defer
    label cleanup: do
        do
            let handle = acquire()
            if handle.closed => exit to cleanup   // 有効: log("closed") を実行せずに終える
            handle.flush()
        log("closed")
```

## 3. 互換性と採用理由

### 3.1. エラーになる既存コード

Discard Context の `do` の中にあり、現在は外側の反復や Deferred Block を終えているラベルなし `exit` は、エラーになる（§2.2・§2.5の例）。
修正方法は二つある。

- 外側の反復にラベルを付けて、`exit to` を使う。
- Deferred Block の中では、ラベル付きの `do` で包む。

既存のテストでは、`ControlFlowConformanceTest.AcceptsSpecifiedResults` の `loop / do / exit` がこれに当たる（§4.3）。

逆に、現在は対象がなくてエラーになる Value Context の `do` の中の `exit`（例: `let v = do => exit 42`）は、受理されるようになる。

### 3.2. 黙って意味が変わる既存コード

黙って意味が変わりうるのは、Value Context の `do`（ラベルの有無を問わず、`_ = do …` を含む）の中にあるラベルなし `exit` が、現在は外側の反復か Deferred Block を対象にしている場合である。
その `exit` のオペランド（省略時は Unit）が `do` の結果型に合えば、型検査でも検出できない。
`do` の推論された型が Never からオペランドの型に変わる場合も、値の使い方によっては検出されない。

```kimi
let code = loop
    let step: i32 = label calc: do
        if failed() => exit -1        // 現行: loop を -1 で終える。案B: calc を -1 で終え、反復を続ける。
        exit to calc next()
    total += step

for item in items
    let skipped: () = do
        if item.last => exit          // 現行: for を終える。案B: do を終え、反復を続ける。
        record(item)
```

U0では、この形を型によらず構文だけで列挙する。具体的には、現行の `ResolveTransferTarget` の対象が、Value Context（`IsValueContext`）の `do` より外側にあるラベルなし `exit` をすべて挙げる。
このプローブは一時的なもので、コミットしない。

- コミット `7f7038df` での簡易調査では、`.kimi`（ライブラリ・milestone・MIR fixture）とSPEC・IMPLの例に該当箇所はなかった。ラベル付きの `do` も調べた。
- C#テストの文字列でも、該当箇所はなかった。見つかったのは結果を捨てる `do` の2件だけで、§3.1のテストと、既に不受理のテストである。
  この調査は近似なので、U0のプローブで確かめる。

凍結済みの過去文書は書き換えない。互換モード、型による再探索、旧規則の恒久的な複製は追加しない。

### 3.3. 不採用案

| 案 | 理由 |
| --- | --- |
| 案A（すべての `do` が受け取る） | 結果を捨てる `do` が外側宛ての `exit` を黙って受け取る。`do` で囲む編集だけで意味が変わり、`yield` の規則とも食い違う。 |
| 現状維持 | 安全だが、値を返す `do` ごとにラベル名が要る。案Bは、結果を捨てる `do` については同じ安全性を保ったまま、ラベルを不要にできる。 |
| Value Context の `do` だけを対象にする | 文脈によって対象が変わる。案Bは対象を固定し、可否だけを文脈で検査する。 |
| 型が合う対象を外側へ探す | 対象の決定が型推論に依存し、失敗後の再探索を持ち込む。 |
| 値付き `exit` だけ `do` を対象にする | `exit` と `exit ()` の同値性を壊す。 |
| `do` を `yield` の対象にする | 内側の `if` が `yield` を受け取ってしまい、条件付きの早期終了が書きにくくなる。 |
| `do` の本体の末尾式を結果にする | §14.2.2 の「インデント本体の式は結果を捨てる」規則の例外になる。 |
| `yield` を廃止して `exit` に統合する | `if condition => exit` でループや `do` を抜けられなくなる。 |
| 反復構文にも §2.2 の制限を設ける | 反復を終えることが `exit` の本来の用途であり、既存の正しいコードを大量に壊す。 |

## 4. 実装計画 — R9完了後

### 4.1. 設計

- **必要な意味**: §2の対象表と可否の規則。
- **表現に欠けているもの**
  - 対象解決が、`do` の本体をラベルなし `exit` の対象として扱わない。
  - 可否判定の「捨てる文脈」の条件が、選択へのラベルなし `yield` だけを見ている。
- **所有コンポーネント**: `KotoHelper` の `ResolveTransferTarget` と `IsValidTransfer`。DECISIONS Q11 で、転送の可否を決める唯一の規則と定められている。
- **不要になるもの**
  - SPEC の「`do` だけはラベルが必要」という規則と、その説明文。
  - コードには `do` を除外する分岐がもともとないので、コードの削除はない。

比較した案は二つで、1を採用する。

1. 所有コンポーネントの規則に、`do` の節を一つ加え、可否の条件を広げる。
2. 共有構造に、対象種別ごとの能力表や列を加える。結果対象としての `do` は既に表現されており、一つの形のために表を足すことになるので採用しない。

所在はコミット `7f7038df` での調査結果である。R9後は [CODEMAP](../../docs/dev/CODEMAP.md) と
[COMPILER_ARCHITECTURE](../../docs/dev/COMPILER_ARCHITECTURE.md) で後継の所有者を確かめ、旧経路は復活させない。

| 責任 | 所在 | 変更 |
| --- | --- | --- |
| 対象解決 | `KotoHelper.ResolveTransferTarget`（`Parsing/Koto/Common/ControlFlowContext.cs`） | `do` の本体を、ラベルなし `exit` の対象に加える。 |
| 可否 | `KotoHelper.IsValidTransfer`・`IsValueContext`（同じファイル） | 条件を「選択へのラベルなし `yield`」から「選択または `do` へのラベルなし転送」へ広げる。 |
| 診断 | `Binding.Lints` の行10（`LintRow.UnlabeledYieldTarget`）、`UnlabeledYieldTarget_Kd`（`DiagnosticCode.cs`、メッセージは `DiagnosticCode.tinyhand`） | 名前（案: `DiscardedTransferTarget_Kd`）、メッセージ、行名、説明コメントを一般化する。語彙の変更なので所有者の承認が要る。DECISIONS Q11 の規則文を置き換える新しいエントリーを加える。 |
| 対象の公開・MIR | `Binding.ControlFlow.BindTransferTarget`（HIRの対象行）、`MirBuilder.Transfers` | 変更しない見込み。可否を通った転送だけが対象行を持ち、MIRはその行を読む。 |
| 結果源・完了 | `Binding.ResultSurvey`、`StructuralCompletion`、旧経路の `ControlFlowAnalysis`（R9後に残っていれば） | 変更しない見込み。`ResolveTransferTarget` を直接呼ぶので、解決の変更に従う。可否は見ないため、無効な転送も結果源・完了として数える。これはラベルなし `yield` と同じ扱いである。 |

- HIRの契約（対象行が null なら、Bindingが行9か行10を記録している）は保つ。
- 新しいモード引数、機能専用の表、転送種別は追加しない。
- R9後の表現がこの前提と異なる場合、実装者は局所的な回避を加えず、設計に差し戻す。
- 規模は数行の追加と名前の変更を見込む。可否検査が文脈を調べるのは、`do` を対象とするラベルなし `exit` に限られる。
  生成コードと実行時のメモリー割当ては、同じ対象へ向かう名前付きの形と同じになることを期待し、§4.3で確かめる。
- レビューの重み: 一つの所有コンポーネント内の規則変更で、共有表現は変えない。AGENTSに従い、完了前に一回の独立レビューを受ける。

### 4.2. 作業単位

| 順序 | 作業 | 完了条件 |
| --- | --- | --- |
| U0 | R9の完了、本書の正式仕様への取り込み、関連するDECISIONS/SETTLEDを確認する。§4.1の所在を確かめ、§3.2の監査を行う。 | 移行対象と検証対象が明確になっている。 |
| U1 | 対象解決・可否・診断名を変更し、テストと既存コードの移行を同じ単位で行う。構造変更が要ると分かったら、振る舞いを変えないコミットを先に分ける。 | §2を満たし、後続処理に重複した規則がない。期待を変えた理由を本書に結び付けている。 |
| U2 | Session、独立レビュー、STATUSの該当欄の更新を行う。 | 必須検証が通り、証跡とコミットが対応し、originへpush済みである。 |

### 4.3. 検証

規則は、それを観測できる最も低い公開層で一度だけ検査する。名前付き形で既に検査している規則の例は増やさない。

- **受理と診断**（`ControlFlowConformanceTest`）
  - 対象: ラベルの有無、本体の形、入れ子の `do`、`do` と反復の両方向の入れ子、型が合わなくても再探索しないこと。
  - 可否: Discard Context の各位置ではエラーになること。単独の式、本体の直下、単一項目本体、括弧・ラベル付き、Unit に固定された関数本体を含む。
    Value Context の各位置では受理されること。初期化子、引数、`return`、`_ =`、無名関数本体を含む。
  - 境界: 条件・列挙元・ガードの中の `do`。関数・クロージャー・既定引数・検証メッセージ・Deferred Block の境界。
  - 回帰: 新しく受理される例（`let v = do => exit 42` など）は、変更前に失敗し、変更後に通ること。
- **対象行**（`HirFormsTest.AnInvalidTransferHasANullTargetRow`）: 結果を捨てる `do` へのラベルなし `exit` の行を一つ加える。
- **MIR**（`MirBuildTest`）: 同じ対象へ向かう名前付き形とラベルなし形で、MIRが等しいこと。
  - 結果・所有権・後始末（Move、Loan、破棄の順序、配送の阻止）は、この同等性で担保する。既存の名前付き形のテストに重ねて例を加えない。

既存のテストは次のように扱う。

- `ControlFlowConformanceTest.AcceptsSpecifiedResults` の `func f() / loop / do / exit / ()` は、§3.1によりエラーになる。
  `RejectsInvalidResultsAndTargets` へ移すか、ループにラベルを付けた形に書き換える。期待を変える理由は、SPECの改訂としてコミットに記す。
- `ControlFlowConformanceTest.RejectsInvalidResultsAndTargets` の `func f() / do => exit` は、不受理のまま残る。理由は「対象なし」から §2.2 に変わる。
- `HirFormsTest`・`ContextualLabelTest`・`HirColumnsTest` の診断コード名を、改名に合わせて更新する。
  診断スナップショットの基準が変わる場合は、所有者の決定を理由としてコミットに記す。

native fixture は、MIRが名前付き形と異なる場合に限って追加する。NativeAOT は明示の指示がなければ実行しない。
[VERIFICATION](../../docs/dev/VERIFICATION.md) に従って Unit検証と最後の Session を行い、`scripts/verify-commit.ps1` で証跡を対応付けてから push する。

## 5. 正式仕様への取り込み対応

正式取り込みを指示されたときに、次の箇所をまとめて整合させる。今回はこれらのファイルを変更しない。

| 本書 | 取り込み先 | 変更 |
| --- | --- | --- |
| §2.1 | SPEC §14.5.2 の表 | ラベルなし `exit` の対象に `do` を加える。 |
| §2.2 | SPEC §14.5.2 の本文、§14.3 | 「Discard Context の選択へのラベルなし `yield` はエラー」を、選択または `do` へのラベルなし転送に一般化する。§14.3 の「unlabeled `exit` skips `do`」を置き換え、新しい規則を示す例を一つ加える。 |
| §2.3 | SPEC §14.2.2 の表 | `do` の結果転送を「named `exit`」から「`exit`」に改める。 |
| §2.5 | SPEC §16.1.1 | 「入れ子の `do` からも Deferred Block を終える」から `do` を除く。 |
| — | SPEC §14.1・§14.9・第15章・§16.2・付録F | 変更しない（文法・結果・所有権・後始末）。 |
| §4 | CODEMAP、COMPILER_ARCHITECTURE（診断コード名）、DECISIONS、STATUS、PLAN | 各文書の更新条件に当たる事項だけを記録する。 |

- SPEC.md は索引のままとし、規則を重複して定義しない。
- SPECJ・GUIDE・履歴との同期は、別途の明示の指示に従う。
- 案Aは、本書による置き換えで `draft/Obsolete` へ移した。
  本書の受付、案Aの処理、取り込み先は [INTEGRATED](../INTEGRATED.md) に記録する。正式取り込みと実装完了は、それぞれ区別して記録する。
