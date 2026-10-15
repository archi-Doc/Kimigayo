# SPECJ 用語表記一覧

前回提案した「英語のまま」「カタカナ推奨」「日本語推奨」の一覧を保存する。[SPECJ.md](SPECJ.md)は日本語簡約版、[SPECJ2.md](SPECJ2.md)はこの一覧の**全分類の用語を英語表記にした版**である。以下の推奨分類は比較用に残し、SPECJ2では分類によらず原語を使う。

SPECJ2は説明文を日本語のまま保ち、専門用語だけを英語にする。既存の英語識別子・API名・キーワード・構文・固定出力文字列・リンク先を変更しない。複合語と文脈を見て対応させ、例えば「形式」の「式」をexpressionへ置き換えない。一般名詞は小文字、ContractやOriginなど固有の概念名は原文の大文字・小文字を尊重する。

## 1. 英語のまま残すと提案した用語

初出に必要な日本語説明を添えても、用語の表記自体は英語にする。

| 原語 | 前回の推奨表記 | 理由 |
| --- | --- | --- |
| Contract | Contract | 言語機能の名前。「契約」では一般的な約束事と区別しにくい |
| Semantics | Semantics | 所有・借用・表現などを決める、Kimigayoの型の構成要素 |
| Core | Core | 「核」「本体」では型の構成要素という意味が伝わりにくい |
| Origin | Origin | 記号的な寿命を表す概念。「起源」では意味が異なる |
| Place | Place | 型やアクセス能力を伴う記憶場所。「場所」「左辺値」では不十分 |
| Loan | Loan | 借用操作そのものとは区別する必要がある |
| Owned | Owned | 「所有されている」ではなく、non-static Originに依存しない性質 |
| Copy / Move | Copy / Move | 規定された操作・能力としての名前を保てる |
| Callable | Callable | 呼出し可能性を表す組込みContract名 |
| Function Item | Function Item | 関数宣言を指す具体的な値の種類。一般の関数値と区別できる |
| View Target | View Target | オブジェクトビューが公開する型の対象という固有概念 |
| Adaptation | Adaptation | 「適応」は直感的でなく、「変換」では借用などを含む範囲を表せない |
| Unit / Never | Unit / Never | 型名として固定。単位などとは訳さない |
| ObjectPayload / Sealed / Transferableなど | 原語のまま | 組込みContract・判定名はコードとの対応を優先 |
| Read / Write / Take | Read / Write / Take | Placeの正式な能力名として用いる場合 |
| Shared / Exclusive / Consuming | Shared / Exclusive / Consuming | 呼出しモードの正式名として用いる場合 |
| Proven / Refuted / Unknown / Error | 原語のまま | 証明判定の状態名として用いる場合 |
| `confined` / `preserves results` | 構文のまま | 効果の保証を表す正確な綴りを保つ |

## 2. カタカナを推奨した用語

SPECJ2では、以下のカタカナ・日本語も左欄の英語へ変更する。

| 原語 | 前回の推奨表記 | 避けたい表記・補足 |
| --- | --- | --- |
| receiver | レシーバ | 受信者 |
| constructor | コンストラクター | 構築子 |
| accessor | アクセサー | アクセス関数への言い換えを繰り返さない |
| getter / setter | ゲッター／セッター | 取得者／設定者 |
| property / field | プロパティ／フィールド | 属性／項目との混同を避ける |
| member | メンバー | 構成員 |
| container | コンテナー | 容器。Declaration Containerは「宣言コンテナー」 |
| scope / block | スコープ／ブロック | 範囲／塊 |
| closure / capture | クロージャー／キャプチャー | 閉包／捕捉より、この仕様では読みやすい |
| payload | ペイロード | 積荷。enumやobjectの保持データを指す |
| view | ビュー | 見方／表示 |
| handle | ハンドル | 取っ手 |
| slot | スロット | 枠／欄。型引数やtaskの位置を表す |
| binder | バインダー | 束縛子。pair binderは「ペアバインダー」 |
| generic / generics | ジェネリック／ジェネリクス | 汎用。型引数を扱う機能名として |
| overload / override | オーバーロード／オーバーライド | 多重定義／上書きだけでは違いが不明瞭 |
| dispatch | ディスパッチ | 配送 |
| cast | キャスト | 投射 |
| literal / token | リテラル／トークン | 字面／字句単位への言い換えは不要 |
| tuple / slice | タプル／スライス | 組／切片 |
| iterator | イテレーター | 反復器。ただしContract名のIteratorは英語 |
| task / thread / frame | タスク／スレッド／フレーム | 仕事／糸／枠 |
| buffer / layout / alignment | バッファー／レイアウト／アラインメント | 緩衝器などの直訳を避ける |
| schema / protocol | スキーマ／プロトコル | 図式／規約だけでは対象が曖昧 |

## 3. 日本語を推奨した用語

SPECJ2では、以下の日本語も左欄の英語へ変更する。

| 原語 | 前回の推奨表記 |
| --- | --- |
| type / value | 型／値 |
| expression / statement | 式／文 |
| declaration / definition | 宣言／定義 |
| function body | 関数本体 |
| ownership / lifetime | 所有権／寿命 |
| borrow / reborrow | 借用／再借用 |
| shared borrow / exclusive borrow | 共有借用／排他借用 |
| binding | 束縛 |
| constraint / requirement | 制約／要件 |
| conformance | 適合 |
| inference / name resolution | 推論／名前解決 |
| acquisition / conversion | 取得／変換 |
| associated type | 関連型 |
| type erasure | 型消去 |
| specialization | 特殊化 |
| projection | 射影 |
| identity / equality | 同一性／等価性 |
| initialization / destruction | 初期化／破棄 |
| allocation / deallocation | メモリー割当て／解放 |
| effect / effect bound | 効果／効果境界 |
| premise / proof / obligation | 前提／証明／証明義務 |
| suspension / cancellation | 中断／取消し |
| control transfer | 制御転送 |
| diagnostic / repair candidate | 診断／修復候補 |
| undefined behavior | 未定義動作 |
| implementation-defined / unspecified | 実装定義／未規定 |

## 4. 表記を変えても保つ区別

| 区別 | 意味とSPECJ2の表記 |
| --- | --- |
| Contractと一般的な契約 | 言語機能はContract。APIの保証など一般の契約はcontract |
| Loanと借用 | Loanは解析で追跡する依存。借用操作はborrow |
| Ownedと所有権 | Ownedは型の性質。所有権はownership、Semanticsのownerとも別 |
| 破棄と解放 | 値の破棄はdestruction、メモリーの解放はdeallocation。`_ =`等で結果を捨てる意味ではdiscard |
| レシーバとReceiver | 関数の受信者はreceiver。型名の`Async.Receiver<T>`はそのまま |

同じ日本語でも、一覧の概念を指さない語は機械的に置換しない。例えば数値の「値」、文法の「文」、形式の「式」は、それぞれnumeric・grammar・formに属する語である。また、channelで値を受信する動作は関数のreceiverとは区別する。
