# Compilation 仕様改訂の判断

添付案の番号3–22について、既存の段階的選択・名前解決・ジェネリック仕様との整合性を確認した。

| 番号 | 判断と適用内容 |
| --- | --- |
| 3 | 採用。Binding、型・オーバーロード解決、特殊化、所有権・Origin解析、Loweringを含む論理工程図に変更。ただし単一の全体パスとはせず、スコープごとの選択を明記。未実装の工程をダミーの実装で補わない。 |
| 4 | 採用。字句・インデント・構造検査と通常の文法検査を区別。早期Falseの `#if` は本文の通常文法を検査しない。既に解析した本文や `#match` のarmの構文診断は、後から選択されなくなっても取り消さない。既存の除外規則を維持して明文化した。 |
| 5 | 採用。Conditionをbool/i64/string、設定名、論理・等値演算、限定された `is`、括弧に限定。符号付き整数リテラルはi64範囲を検査する。呼出し・算術・大小比較などは短絡されてもエラー。通常の実行時式の演算子は変更しない。 |
| 6 | 採用。言語結果Deferredと内部のPendingを分離。未解決名や `is` のBinding待ちはPending、許可しない構文はErrorとする。 |
| 7 | 修正して採用。値の参照は組み込み値・Project設定だけに限定。Type/Contract名までその環境に限定すると既存の `T is Comparable` と外部Contract参照が壊れるため、既に確立した環境で通常のType Name Selectionとaliasを使う。 |
| 8 | 修正して採用。最終評価時にTrueのarmがある場合だけcatch-allを省略できる。`#case _` の一律必須化やType列挙・新しい定理証明機構は不要。 |
| 9 | 採用。値の型・正規化・不変条件と設定衝突を規定。`arch`、型付きProject設定、準備済み環境の読み取り専用公開を実装。 |
| 10 | 採用。完全な入力の分類を規定し、OS/architectureだけをキャッシュキーにしない。解析後の同一Compilationの再準備を禁止。今回のbuild metadataは完全なキャッシュキーではない。 |
| 11 | 修正して採用。言語が要求するlayout/ABIの事実と現在のLLVM表現を区別。存在しない第二backendや抽象layout APIは追加しない。 |
| 12 | 修正して採用。`.kimiproj` のLangVersionを有効化し、Project→Solution→compilerの選択順と未対応版のエラーを実装。現在の言語版だけを受け入れ、版とcompiler build identityを準備・build metadataに記録。複数言語版対応を装わず、再現性にはcompiler自体の固定も必要と明記。 |
| 13–15 | 採用。`[]`、`=`、PascalCaseの説明を用途・対象に合わせて修正。既存の式・宣言の実装を変更する必要はない。 |
| 16 | 採用。導入例のmainをrunExampleに改称。未定義のentry point ABIを新設しない。 |
| 17 | 採用。SourceDocument、Koto tree、Compilation root、project rootを表に追加し、Kotonohaをアプリにも使えるmodule単位として説明。 |
| 18 | 修正して採用。新しいrevision番号体系を追加せず、既存の不変SourceDocumentをsnapshot identityとして使用。解析ごとにsource由来CodeContextを生成し、遅延診断も元snapshotを参照する。完全なincremental compilationを実装したという意味ではない。 |
| 19 | 採用。source artifactに用語を統一し、必要情報を分類。完全なartifact形式・互換性要件は別仕様へ委譲。 |
| 20 | 修正して採用。re-exportは構文が決まるまで予定機能とする。当面、外部Symbolをソースから名前で使うconsumerには元ライブラリの直接参照とアクセス可能な経路を要求。外部公開Typeを含むSignatureの全面禁止は不要。 |
| 21 | 採用。未定義のexplicit import pathを明示的なalias宣言に置換。 |
| 22 | 採用。言語規則とCurrent implementation statusを分離。実装上の保留状態を言語規則に混ぜない。 |

末尾の重点項目について、`#match` / `#case` の構文関係は前回の変更を維持した。`#if` のnarrowingは、選択対象にのみ正の条件事実を与える規則を追加した。`#match` の事実も対象arm内に限定し、後続の文や別グループへ漏れないことを明記した。一般のDirective Binding、Contractの証明、narrowingの意味解析、所有権解析、code generationは引き続き予定工程である。

検証では、禁止構文の短絡時診断、整数境界、除外本文の検査範囲、target値の整合性、設定の直列化・固定、言語版の継承と拒否、source snapshotと遅延診断を追加した。Debug・Releaseの全1,133件が成功。build metadataは現在メモリ上のAPIで提供し、未実装のbinary artifactへの保存を意味しない。
