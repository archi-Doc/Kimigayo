# SPECJ 用語表記一覧

日本語文書で使う推奨表記を定める。用語の意味は[仕様の用語集](spec/appendices/E-terminology.md)と各定義節に従う。

## 1. 英語のまま残す用語

初出に必要な日本語説明を添えても、用語の表記自体は英語にする。

| 原語 | 推奨表記 | 補足 |
| --- | --- | --- |
| Contract | Contract | 言語機能の名前。一般的な契約・保証とは区別する。[§8.4.1](spec/08-generics-constraints-and-contracts.md#841-contract-declarations) |
| Semantics | Semantics | 所有・借用・表現などを決める、Kimigayoの型の構成要素。[§3](spec/03-types-and-values.md#3-types-and-values) |
| Core | Core | 「核」「本体」では型の構成要素という意味が伝わりにくい。[§3](spec/03-types-and-values.md#3-types-and-values) |
| Origin | Origin | 記号的な寿命を表す概念。「起源」では意味が異なる。[§3.7](spec/03-types-and-values.md#37-origins-and-loans) |
| Place | Place | 型やアクセス能力を伴う記憶場所。「場所」「左辺値」では不十分。[§3.4](spec/03-types-and-values.md#34-places-and-storage) |
| Loan | Loan | 借用先のPlaceとアクセスモードを追跡する概念。借用操作とは区別する。[§15.6](spec/15-ownership-and-lifetime-analysis.md#156-borrow-checking) |
| Owned | Owned | non-static Originに依存しない型の性質。Semanticsの`owner`や所有権とは区別する。[§15.2.3](spec/15-ownership-and-lifetime-analysis.md#1523-static-and-owned) |
| Copy / Move | Copy / Move | 規定された操作・能力としての名前を保つ。[§3.5](spec/03-types-and-values.md#35-copy-and-move) |
| Callable | Callable | 呼出し可能性を表す組込みContract名 |
| Function Item | Function Item | 関数宣言を指す具体的な値の種類。一般の関数値と区別できる |
| View Target | View Target | オブジェクトビューが公開する型の対象という固有概念 |
| Adaptation | Adaptation | 「適応」は直感的でなく、「変換」では借用などを含む範囲を表せない |
| Unit / Never | Unit / Never | 型名として固定。単位などとは訳さない |
| ObjectPayload / Sealed / Transferableなど | 原語のまま | 組込みContract・判定名はコードとの対応を優先 |
| Read / Write / Take | Read / Write / Take | Placeの能力名。[§3.4](spec/03-types-and-values.md#34-places-and-storage) |
| ByValue / Shared / Exclusive | ByValue / Shared / Exclusive | パラメーターの取得モード。[§7.4.1](spec/07-functions-and-callable-values.md#741-acquisition-modes-and-admitted-cases)は[Subjectモード](spec/15-ownership-and-lifetime-analysis.md#1516-match-and-for-subjects)と同じ名称を使う |
| Shared / Exclusive / Consuming | Shared / Exclusive / Consuming | 呼出しレシーバの要件名。取得モードとは区別する。[§7.7.3](spec/07-functions-and-callable-values.md#773-call-receiver) |
| Proven / Refuted / Unknown / Error | 原語のまま | 4値の証明判定。Refutedは否定の証明、Unknownはどちらも未証明。[§8.3.1](spec/08-generics-constraints-and-contracts.md#831-propositions-and-outcomes) |
| Proven / NotProven | Proven / NotProven | ObjectCallCompatibleの公開状態。NotProvenをRefutedやUnknownに読み替えない。[§12.7](spec/12-expressions.md#127-object-receiver-compatibility) |
| `confined` / `preserves results` | 構文のまま | 効果の保証を表す正確な綴りを保つ |

## 2. カタカナを推奨する用語

| 原語 | 推奨表記 | 補足 |
| --- | --- | --- |
| receiver | レシーバ | 関数のreceiverを「受信者」とは訳さない |
| constructor | コンストラクター | 「構築子」とは訳さない |
| accessor | アクセサー | アクセス関数への言い換えを繰り返さない |
| getter / setter | ゲッター／セッター | 「取得者／設定者」とは訳さない |
| property / field | プロパティ／フィールド | 属性／項目との混同を避ける |
| member | メンバー | 「構成員」とは訳さない |
| container | コンテナー | 「容器」とは訳さない。Declaration Containerは「宣言コンテナー」 |
| scope / block | スコープ／ブロック | 「範囲／塊」とは訳さない |
| closure / capture | クロージャー／キャプチャー | 閉包／捕捉より、この仕様では読みやすい |
| payload | ペイロード | 「積荷」とは訳さない。enumやobjectの保持データを指す |
| view | ビュー | 「見方／表示」とは訳さない |
| handle | ハンドル | 「取っ手」とは訳さない |
| slot | スロット | 「枠／欄」とは訳さない。型引数やtaskの位置を表す |
| binder | バインダー | 「束縛子」とは訳さない。pair binderは「ペアバインダー」 |
| generic / generics | ジェネリック／ジェネリクス | 型引数を扱う機能名として使い、「汎用」とは訳さない |
| overload / override | オーバーロード／オーバーライド | 多重定義／上書きだけでは違いが不明瞭 |
| dispatch | ディスパッチ | 「配送」とは訳さない |
| cast | キャスト | 「投射」とは訳さない |
| literal / token | リテラル／トークン | 字面／字句単位への言い換えは不要 |
| tuple / slice | タプル／スライス | 「組／切片」とは訳さない |
| iterator | イテレーター | 「反復器」とは訳さない。ただしContract名の`Iterator`は英語 |
| task / thread / frame | タスク／スレッド／フレーム | 「仕事／糸／枠」とは訳さない |
| buffer / layout / alignment | バッファー／レイアウト／アラインメント | 緩衝器などの直訳を避ける |
| schema / protocol | スキーマ／プロトコル | 図式／規約だけでは対象が曖昧 |

## 3. 日本語を推奨する用語

| 原語 | 推奨表記 | 補足 |
| --- | --- | --- |
| type / value | 型／値 | |
| expression / statement | 式／文 | |
| declaration / definition | 宣言／定義 | |
| function body | 関数本体 | |
| contract | 契約 | APIなどの保証を表す一般名詞。言語機能のContractとは文脈で区別する |
| ownership / lifetime | 所有権／寿命 | 所有権とOwned・`owner`は別の概念 |
| borrow / reborrow | 借用／再借用 | 操作名。依存を追跡するLoanとは区別する |
| shared borrow / exclusive borrow | 共有借用／排他借用 | |
| binding | 束縛 | |
| constraint / requirement | 制約／要件 | |
| conformance | 適合 | 型のContractへの適合。[§8.6.1](spec/08-generics-constraints-and-contracts.md#861-conformance-declarations-and-identity) |
| fit | 期待型に合う | 値・型が期待型に合うこと。Contractへの適合とは区別する。[§3.8](spec/03-types-and-values.md#38-type-relations-and-operations)・[§10.2](spec/10-overload-resolution-and-inference.md#102-common-adaptation) |
| inference / name resolution | 推論／名前解決 | |
| acquisition / conversion | 取得／変換 | |
| associated type | 関連型 | |
| type erasure | 型消去 | |
| specialization | 特殊化 | |
| projection | 射影 | |
| identity / equality | 同一性／等価性 | |
| initialization | 初期化 | |
| initializer | 初期化子 | 初期化を指定する式など。initializationとは別項目として扱う。[§11.7](spec/11-properties.md#117-construction-and-destruction) |
| destruction | 破棄 | 値の寿命を終えること。[§3.4](spec/03-types-and-values.md#34-places-and-storage) |
| discard | 結果を捨てる | 式の結果を保持しないこと。結果のdestructionは通常の寿命に従う。[§14.2.2](spec/14-control-flow.md#1422-evaluation-contexts-and-body-results) |
| allocation | メモリー割当て | |
| deallocation | 記憶領域の解放 | 値のdestructionとは区別する。[§5.6](spec/05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations) |
| effect / effect bound | 効果／効果境界 | |
| premise / proof | 前提／証明 | |
| obligation | 義務 | 呼出し側の安全上の義務なども含む。[例：§5.6](spec/05-raw-pointers-and-unsafe-memory.md#56-raw-storage-operations) |
| proof obligation | 証明義務 | 証明を必要とする義務 |
| suspension | 一時停止 | 再開可能な実行の停止。[§24.3](spec/24-suspension-and-asynchronous-tasks.md#243-task-calls) |
| cancellation | 取消し | 一時停止とは区別する。[§22.3.3](spec/22-core-execution-and-foreign-functions.md#2233-cancellation-and-waits) |
| control transfer | 制御転送 | |
| diagnostic / repair candidate | 診断／修復候補 | |
| undefined behavior | 未定義動作 | |
| implementation-defined / unspecified | 実装定義／未規定 | |

## 4. 表記の適用

- 用語は概念と文脈で判断し、部分一致では置換しない。「形式」の「式」や「数値」の「値」は独立した用語ではない。
- 複合語は全体で対応させる。initializationは「初期化」、initializerは「初期化子」とする。
- コード中の識別子・キーワード・文字列リテラルは翻訳しない。説明文でもAPI名・型名は綴りを保つ。一般名詞のiteratorは「イテレーター」、Contract名の`Iterator`はそのままにする。
- 英語で残す正式名称は大文字・小文字を保つ。ただし訳語は字面だけで決めず、Contractと一般のcontract、Semanticsの`owner`と一般語のowner（所有者）を区別する。
