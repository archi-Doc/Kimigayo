# Kimigayo 仕様ツアー

2026-09-13 時点の [SPEC.md](../../SPEC.md) にある、ソースで表現可能な主要機能を広く使う読み物用サンプルです。**現在のコンパイラーでビルド・実行できることを目的としていません。** 仕様の全規則を網羅する適合性テストでもありません。

[Main.kimi](Main.kimi) の `public main` から各分野の `run` を読む構成です。全 `.kimi` は同じ Kotonoha に属し、`SpecTour` の各 fragment が合流します。補助型・関数はこのディレクトリー内で定義し、標準機能は SPEC の Core 宣言・操作だけを使っています。

## ファイルと仕様の対応

| ファイル | 主な例 | SPEC |
| --- | --- | --- |
| [Main.kimi](Main.kimi) | 明示 main、文書単位 alias、ルート修飾、defer | §9、18、22.2 |
| [Basics.kimi](Basics.kimi) | 数値・char・raw string・補間、Tuple、名前付き/省略可能引数、オーバーロード、静的 Property、環境分岐 | §2、3、6、7、10、12、13、19 |
| 同上の Reading / Gauge | Copy、Comparable / Stringify、constructor、computed / stored Property、custom setter、storage、合成 init | §3.5、6.2、8.4、11 |
| [Sequences.kimi](Sequences.kimi) | 固定長・多次元・動的配列、length 引数と推論、Index / Range / Slice、範囲検査の Option、Dictionary、所有/借用反復、部分 Move | §4、12.3、14.6、15.1 |
| [Generics.kimi](Generics.kimi) | 静的 Contract、多重 refinement、関連型と projection、Property witness、条件付き適合、完全 Type / Semantics-Core パラメーター、明示完全特殊化 | §8–10 |
| [Lifetimes.kimi](Lifetimes.kimi) | ref / uniq、named Origin、Origin の交差、結果借用、借用の格納、再借用、消費による借用移譲、部分 Move と再初期化、deinit | §3、15、16 |
| [Callables.kimi](Callables.kimi) | Function Item / 共通 Function Type、暗黙/明示 capture、ref / uniq capture、可変 capture、Shared / Exclusive / Consuming 呼び出し、Callable、Owned、環境借用結果 | §7.6、8.6、15.8 |
| [ControlFlow.kimi](ControlFlow.kimi) | enum payload、guard、借用/所有 match、Tuple pattern、Option / Result、require、if / yield、ラベルと exit / continue、do / loop / while、独自 Iterable / Iterator、Abort | §6.3、14、16、17、22.1 |
| [Objects.kimi](Objects.kimi) | open struct、base constructor、protected Field、静的メソッド選択、obj / rc / arc / objref / objuniq、上方変換、is / is not、refinement | §3.3、6.2、12.4、13.5–13.6、14.10 |
| [Native.kimi](Native.kimi)、[Native.Methods.kimi](Native.Methods.kimi) | C layout、struct fragment、rootgroup、raw pointer、null、読み書き・算術・変換、unsafe、LibraryImport | §5、6.1、21.1、22.3 |
| [native/observer.c](native/observer.c) | NativeRecord を読み取る最小の外部 C 実装 | §21.1、22.3 |
| [SpecTour.kimiproj](SpecTour.kimiproj) | Application、target、O2、LlvmBin、native library 指定 | §20.8 |

## 読み方と期待結果

- `Sequences.sum` は固定長配列を借用して i64 で集計します。配列そのものを複製する必要はありません。`[10,20,30,40]` を増分した合計は `104` です。
- `Generics.forward(i32 の借用)` は元のジェネリック定義を経由して `classify<i32>` の特殊化を選び、`1` を返します。
- `Lifetimes` の `MutView` は既存ストレージへの借用を保持します。`get` の子借用を使い終えてから `take` で View を消費し、元の排他的借用を移譲します。Gauge の custom setter による最終値は `50` です。
- `Callables.applyTwice` は capture 内の count を更新し、`10 → 11 → 13` を計算します。元の count は `0` のままです。
- `ControlFlow` は `Result` を明示的な match で伝播します。`?` や例外構文は使いません。`required` の `.None` は Abort の例ですが、main からは `.Some(42)` を渡します。
- `Lifetimes` の最後のブロックは `defer B → nested defer → audit 2 → defer A → audit 1` の順で片付けます。`securedResult` は defer の前に結果を確保するので `1` を返します。

以上は仕様から導く期待結果で、実行による確認結果ではありません。配列処理は固定長ストレージと借用を中心にし、動的配列・Dictionary・所有文字列は、それぞれの機能を示す箇所で使っています。補間は独立した所有文字列を生成し得るため、表示処理まで含めた無割り当てを主張する例ではありません。

## API や構文が未確定の領域

「仕様をフルに使う」場合でも、設計上の意味と利用可能なソース構文を区別します。

| 領域 | このサンプルでの扱い |
| --- | --- |
| obj / rc / arc の生成、強参照の複製 | §13.5.8 は意味を定義していますが最終 API 名は未確定です。Objects の関数は取得済みの handle を受け取ります。`Sensor.init(...)@obj` のような生成構文を捏造していません。 |
| Exchange / Swap | §15.7 の初期化を保つ交換は、最終 API 名・解決規則が未確定です。通常の Move / 再初期化と混同せず、呼び出し例は含めていません。 |
| Mods | §20.7 の生成・順序・再検証の規則はありますが、具体的ホスト API / 登録方法は設計中です。ソース追加後の形は NativeRecord の fragment で示し、存在しない Mod 実装は加えていません。 |
| Runtime Contract、virtual / override、checked cast | active な構文に含まれません。静的 Contract と具象 Core の object view / `is` を使っています。 |
| 外部 Kotonoha の設定、re-export、binary interface | 設定・形式の未確定部分があるため、同一 Kotonoha の named Container と文書単位 alias で構成しています。 |
| 可変 Slice、固定配列の fill、独自算術、文字列連結の所有規則 | 未導入・未確定の部分は使わず、所有配列の排他的借用・リテラル・数値演算・補間を使っています。 |
| 並行処理 | スレッド・task・async・Send/Sync 相当の機能は仕様にありません。arc の存在から推測して追加していません。 |

LLVM のレイアウト・ABI・最適化・メタデータ・キャッシュの規則はコンパイラーの責務です。ソース例と `.kimiproj` にその入力を示していますが、キャッシュや最適化そのものを言語機能として記述してはいません。

## ビルド設定と検証範囲

`SpecTour.kimiproj` は構成例です。LLVM の場所と backend archive は環境に合わせる必要があり、`native/observer.lib` は同梱していません。FFI の相手側ソースは `native/observer.c` にあります。一般的なコード生成が未実装のため、この設定を調整しても現在の `kimi build` で動作するサンプルにはなりません。

`NativeDemo.inspectForeign` と raw pointer 関数は、有効なポインターを呼び出し側から受け取る例です。null は型付けと比較にだけ使い、dereference の例には渡しません。ポインター取得・所有権管理 API は仮定していません。

検証では、既存のローカル Debug ビルドのパーサーで全 10 ファイルを個別に読み、`x86_64-pc-windows-msvc` と `x86_64-unknown-linux-gnu` の環境条件で構文診断が 0 件であることを確認しました。Linux は条件選択の構文確認だけで、Linux 向け native backend の対応を示しません。ソースのリンク・Binding・寿命解析・native build・実行は検証していません。

型・寿命・所有権は SPEC と照合して記述していますが、未実装の意味解析や実行の適合性を保証するものではありません。現在実行可能な小さなサンプルは [Hello](../Hello/README.md)、コンパイラーの実装範囲は [STATUS.md](../../STATUS.md) を参照してください。
