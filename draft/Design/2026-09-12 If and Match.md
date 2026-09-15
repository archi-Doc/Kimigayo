# ifとmatchの共通仕様

2026-09-15 同期。2026-09-12 の先行案を、採用済みの [Control flow](2026-09-12%20Control%20flow.md) と [SPEC 第14章](../../spec/14-control-flow.md) に合わせて整理した。現行の規範は SPEC 本文とする。先行案の「インデント本体の末尾式を暗黙yieldする」「使用位置によりmatchの網羅性を緩める」規則は採用しない。実装状況は [STATUS](../../STATUS.md) を参照する。

## 1. 本体の構文

ifの各節とmatchの各armは、同じ共通Bodyを使う。

```text
Body := "=>" (Expression | Statement)
      | NEWLINE INDENT ItemList DEDENT
```

単一本体は `=>` のある物理行で1個の式または文を開始する。通常の式継続は許可するが、`=>` だけを書いて次行からインデント本体を始めない。インデント本体には `=>` を付けず、1個以上の項目を置く。宣言・指令はインデント本体に限る。空白とコメントだけでは本体にならず、何もしない場合は `()` とする。

```kimi
let result = match value
    .A => calculate()
    .B
        prepare()
        yield calculateOther()
    _ => 0
```

## 2. ifの動作

条件はbool。各条件を一度評価し、boolを確保して条件の一時値をcleanupした後、その値で分岐する。else-ifは未選択の場合だけ次の条件を評価する。elseがなければ未選択経路はUnitを供給するため、値を使う場合も共通結果型に適合しなければならない。詳細は SPEC §14.2.3・§14.7 に従う。

## 3. matchの動作と網羅性

対象を一度取得し、ソース順でPattern・guardを調べて最初に成功したarmを実行する。対象・候補束縛・armでの取得とcleanupは SPEC §14.8・§15.1.6 に従う。

**本体形式と使用位置にかかわらず、matchは常に網羅的でなければならない。** 意図的に無視する場合も `_ => ()` などを明記する。guard付きarmは `if true` でも網羅性の証明に使わない。選択後のarm一覧は非空とし、保守的な証明と包含警告は SPEC §14.8.4 を使う。

## 4. 結果を使う場合・捨てる場合

初期化式、引数、条件、演算子・転送のオペランドはValue Context、独立した式とインデント本体の直接の式はDiscard Contextである。括弧は使用位置を維持する。期待型Unitや後からの未使用はDiscard Contextへの変更理由にならない。

| 使用位置 | 単一本体の式 | 選択式の結果と明示yield |
| --- | --- | --- |
| Value Context | 値を使って選択式へ供給 | 全結果源を対象結果型に適合させる |
| Discard Context | 値を捨て、正常完了時はUnit | 対象結果型はUnit。明示yieldの値もUnitに適合させる |

```kimi
if ready => check() // checkの非Unit結果を捨てられる。
else => log()

if ready => yield 123 // Error: 捨てる選択式への明示結果はUnitに適合しない。
```

## 5. 末尾式と暗黙yield

**インデント本体の直接の式は、末尾を含めて捨てる。** 構造上の末尾到達はUnitを供給する。値を返す場合は明示yieldを使う。暗黙の結果供給はValue Contextの単一本体の式に適用し、インデント本体へ一般化しない。

```kimi
let price1: i32 = if member => 80 else => 100

let price2: i32 = if member
    yield 80
else
    yield 100

let invalid: i32 = if member
    80 // Error: 捨てた後のUnitはi32に適合しない。
else
    100
```

本体形式の変更は意味を変え得る。formatterが暗黙に変換せず、結果・転送先・スコープ・破棄順序を保存するリファクタリングとして扱う。必要なyield等を追加する。

## 6. ラベル付き選択式と明示yield

ラベルは構文の前に `name:` と置く。`yield value` は探索規則に従う選択式へ、`yield to name: value` は指定した外側の選択式へ値を供給する。オペランド省略は `()`。解決した対象の結果型に検査し、別の対象へ送る値を現在の選択式の結果源として数えない。

```kimi
let result: i32 = outer: if ready
    loop
        yield to outer: 42
else => 0
```

yieldの探索は反復を通過する。関数・defer等の探索境界とラベルなしyieldの使用位置は SPEC §14.4・§14.5 に従う。転送の結果を確保してから退出スコープをcleanupし、正常に到着した場合だけ対象へ届ける。

## 7. 改行・括弧・スコープ

共通Bodyのレイアウト、elseの接続、区切り領域は SPEC §2.2 に従う。括弧は独自の本体スコープ・転送先・探索障壁を作らない。Pattern束縛と直後のarm本体は同じ宣言空間を使い、入れ子の本体は通常のスコープ規則に従う。

## 8. Never・型検査・Scope Exit

結果型は全結果源と構造的完了から決める。boolリテラル、定数伝播、Pattern包含、実行時の非到達だけでは検査経路を除外しない。結果源の推論順と期待型の境界は SPEC §14.9・§10.5 に従う。

Neverは型、制御転送はCompletion、Abort・非終了はEvaluation Outcomeであり、同一視しない。cleanupによる配送阻止で既に検査した結果型を置き換えない。実行到達性・初期化・Move・Loan・cleanupは共通の経路で検査し、Abort後に通常のcleanupを再開しない。

## 9. 他の本体構文との関係

関数、反復、do、unsafe、defer、require、accessor、init、deinitも共通Bodyを使うが、結果規則は所属構文に従う。例えば反復の末尾到達は次の反復へ進み、関数の結果はreturnで、doの明示結果は名前付きexitで供給する。SPEC §14.2の表を正本とする。

## 10. 診断とSPEC.mdへの反映

共通Body・使用位置・常時網羅性・明示転送・末尾Unitは SPEC §2・§7・§10・§14–17 と付録Fに反映済み。意図しないUnit推論と作用のない式の破棄は SPEC §17.4 の条件で警告する。先行案の暗黙yieldを再導入する根拠として本書を使わない。
