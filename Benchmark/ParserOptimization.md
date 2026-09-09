# Parser optimization (2026-09-05)

対象は `ParseBenchmark.Test1()`。入力とベンチマークメソッドの処理内容は変更していない。

## 結果

| 測定 | 変更前 | 変更後 |
| --- | ---: | ---: |
| アロケーション / 回 | 19,688 B | 18,368 B |
| 同一プロセスでの交互計測・平均 | 10.054 µs | 9.681 µs |

アロケーションは **1,320 B / 回（6.7%）削減**。補助の交互計測では実行時間が **3.7%短縮**した。12組すべてで変更後が速かった。

交互計測では、変更前のビルドを別の AssemblyLoadContext に読み込み、両方の `Test1()` を同じ形式のデリゲートで呼び出した。CPU affinity は 1、両方を各200,000回ウォームアップし、32,768回ずつ12組測定。前後の測定順を組ごとに反転した。

BenchmarkDotNet 0.15.8 / .NET 10.0.11 / Windows 11 / Intel Core Ultra X7 368H でも測定した。共通条件は `Job.MediumRun.WithAffinity((IntPtr)1).WithInvocationCount(65536)`、WarmupCount=10、IterationCount=15、LaunchCount=2。

| BenchmarkDotNet | Mean | Error (99.9% CI half-width) | Allocated |
| --- | ---: | ---: | ---: |
| 変更前 | 10.03 µs | 1.158 µs | 19.23 KB |
| 変更後 | 11.16 µs | 1.962 µs | 17.94 KB |

BenchmarkDotNetの時間は実行間の変動が大きく、この結果だけでは高速化を確認できない。上記3.7%は交互計測の観測値であり、一般的な高速化率を保証するものではない。通常設定でも初期ウォームアップ中に大きな時間変化が見られた。計測専用のJob設定は比較後に戻してある。

生データは `BenchmarkDotNet.Artifacts/parser-baseline-stable/`、`parser-final/`、`parser-paired-final.csv` に保存した。

## 実装

- `ApplicationKoto` に呼び出し式とジェネリック適用式の格納・出力・親子関係・置換処理を集約。シリアライズのキー番号は維持。
- 引数列・型引数列・ブロックの構築に `TemporaryList` を使用。4要素までは作業用Listを確保せず、必要な長さの配列を構文木に保持する。
- 引数を読み取る場合は `ArgumentNodes` / `TypeArguments` を使うとListの実体化が不要。従来の可変 `Arguments` はアクセス時にList化する。
- 関数パラメーター名の一時Identifierノードを廃止。宣言ヘッダーのリストを直接引き継ぎ、既に公開済みの可変リストは同じインスタンスを維持する。
- 識別子の検証結果を共有文字列とともにキャッシュ。無効な綴りだけ別途記録し、通常のテーブル要素サイズを増やさない。Unicode検証と並行挿入時の公開順序を維持。
- `TokenReader.TryConsume` の通常経路をエラー回復から分離し、インライン化する。式解析では後置演算子に該当するトークンだけ後置解析を呼び出す。
- 短い10進整数は浮動小数点マーカーの走査を省いて変換する。
- 属性の重複したキャッシュと復元処理を削除。属性の参照ビューは置換後のオペランドにも追従する。

Tokenizerの分岐統合も試したが、速度改善を確認できなかったため採用していない。構文木ノードの共有・使い回しによる親子関係や寿命の変更は行っていない。

## 検証

`dotnet test xUnitTest/xUnitTest.csproj -c Release --no-restore`：432件成功。

追加の回帰テストでは、空・1・4・5・20要素の引数列、ラベルの対応、シリアライズ、配列とListの子ノード置換、属性参照の更新、既存宣言リストの参照維持、Unicode識別子、識別子テーブルの並行挿入と拡張を検証した。

# Parser optimization, round 2 (2026-09-09)

対象は引き続き `ParseBenchmark.Test1()`。入力とベンチマークメソッドの処理内容は変更していない。

## 結果

| 測定 | 変更前 | 変更後 |
| --- | ---: | ---: |
| アロケーション / 回 | 21,384 B | 18,824 B |
| 交互計測（1コア固定）・最速値 | 9.85–10.15 µs | 8.19–8.53 µs |

アロケーションは **2,560 B / 回（12%）削減**。交互計測では実行時間が **約 16%短縮**した。

交互計測は、変更前と変更後の Benchmark アセンブリを別ディレクトリにビルドし、`Test1()` と同じ処理を 20,000 回 × 7 ラウンド行う Stopwatch ハーネスで交互に 3 組実行した。プロセス優先度を High、CPU affinity をコア 2 に固定した。この機種（ハイブリッドコア）では固定しない計測は ±5〜10% ぶれるため、固定なしの値は比較に使っていない。

BenchmarkDotNet 0.15.8 / .NET 10.0.11 / Windows 11 でも測定した。変更前のビルドは別ディレクトリの出力のため、両方を `--inProcess`（InProcessEmitToolchain、既定の反復設定）で同一条件にした。

| BenchmarkDotNet (in-process) | Mean | Error (99.9% CI half-width) | Allocated |
| --- | ---: | ---: | ---: |
| 変更前 | 10.24 µs | 0.136 µs | 20.88 KB |
| 変更後 | 8.52 µs | 0.170 µs | 18.38 KB |

変更後を通常の `Job.MediumRun`（別プロセス）で測ると 7.26 µs（Error 0.067 µs）、18.38 KB だった。

## プロファイル

.NET 10 の `dotnet-trace` サンプリングはこの環境ではほぼ全サンプルが `Array.Copy`/`PollGC` に付くため使えなかった。代わりに `SuspendThread` + `GetThreadContext` で RIP を採取し ClrMD で解決する小さなサンプラーを用意した。変更前の内訳はおおよそ、Tokenizer 30%、アロケーション・GC・メモリクリア（coreclr）15%、識別子の intern 5%、残りは Parser の各メソッドに薄く分散していた。アロケーション種別は `GCAllocationTick` から集計し、`TypeSemanticsKoto`・`IdentifierNameKoto`・`NumberLiteralKoto` で 40% を占めていた。

## 実装

- `Koto` 基底から `PendingDirectiveConditions` の格納フィールドを外し、スコープを持つ `DeclarationContainerKoto` と `CodeBlockKoto` だけが保持する（全ノード 8 B 削減）。それ以外のノードへ条件を渡すと `InvalidOperationException`。
- `Tokenizer.Read`: デリゲート表による分岐を `switch` に展開し、先頭文字クラス表（識別子/数値、単一文字トークン、その他）で識別子を最初に振り分ける。空白と字下げの計測をスカラーループにした（短い連続空白ではベクトル検索の準備コストが上回る）。
- 識別子の走査を `Vector128` で 8 文字ずつ分類。数値・Unicode 識別子・不正文字の経路は別メソッドに分離し、ホットパスのフレームを小さくした。
- `TokenHelper.TryGetSingleCharTokenKind` を表引きにし、`GetKeywordOrIdentifierKind` に「その長さのキーワードの先頭文字集合」による事前判定を追加。
- `IdentifierTable`: 4 文字ずつ混ぜるハッシュに変更し、`TryGetIdentifier` を `Intern` 経由ではなく 1 フレームで探索する。
- `TypeSemanticsKoto`: 型名と semantics パラメーターは同時に使われないため 1 スロットに統合し、Origin の 3 メンバーは Origin がある場合のみ生成する内部オブジェクトに移した（104 B → 80 B）。公開プロパティは維持。
- `NumberLiteralKoto`: 整数判定をリーダーのテキストから行い、短いリテラルはスカラー走査（`HasFloatMarker`）。
- 引数列・ブロック要素の一時リストを `Koto` 専用の `TemporaryKotoList` にし、配列格納時の共変性チェックを不要にした。
- 生成関数本体のリスト初期容量、`If`/`Match`/リテラル/宣言コンテナーの小さな List の初期容量を用途に合わせた。

`NumberLiteralKoto` の 128 bit キャッシュを外す案は、`KotoHelper.Replace` が `Span` を差し替えるため `ParserOptimizationTest` が失敗し、採用しなかった。

## 検証

`dotnet test xUnitTest/xUnitTest.csproj -c Release`：1348 件成功。変更前後のビルドでベンチマーク入力を解析し、`UnparseAll` の出力と診断（0 件）が一致することを確認した。
