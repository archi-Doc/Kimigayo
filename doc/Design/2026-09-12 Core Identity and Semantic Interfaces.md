# Core の意味識別・生成物の互換性・実行契約の明確化

2026-09-12。提示された提案 4–11 の評価と改訂提案。現行 SPEC.md と実装を確認した設計文書であり、仕様への採用・コンパイラへの実装は別途行う。

## 1. 評価と推奨

全体の方向は良い。主な効果は、言語機能の追加よりも、既存機能の意味と実装境界を一貫した規則で説明できることにある。外部ライブラリの安全な再利用や再現可能な出力は実用上の強さを増す。一方、厳格な provenance は低水準操作の自由度を狭めるため、全案を単純な表現力の向上とは評価しない。

| 元の提案 | 判断 | 改善する点と修正方針 |
| --- | --- | --- |
| 4. Required declaration と intrinsic の分離 | 採用を推奨 | ソース宣言と意味操作の台帳を分離する。未命名操作を Missing declaration に数えない |
| 5. SemanticInterface と互換性判定 | 修正して採用を推奨 | 形式に先立って必須情報・拒否条件を定義する。判定には依存解決結果と利用目的も必要 |
| 6. Kotonoha Identity と version の分離 | 修正して採用を推奨 | 公開 interface hash だけでは実装の差を識別できない。モジュール改訂・Symbol・生成物内容を分ける |
| 7. 初期 target の厳格な provenance | 採用を推奨 | 整数からの再構成ではアクセスの根拠を得ないと明記。元ポインターの権限は失われない |
| 8. FFI 契約違反の UB 明記 | 範囲を限定して採用を推奨 | 実行時の unsafe 境界違反を UB とする。構文・ABI エラー、Abort、通常の外国関数の失敗は区別 |
| 9. float Stringify の canonical 化 | 採用を推奨 | 最短有効桁・候補選択・表記・特殊値を固定する。アルゴリズムは自由 |
| 10. Type alias の用語整理 | 採用を推奨 | Container alias、透明参照の正規化、将来の Type alias を区別する |
| 11. Core shape と language semantic の分類 | 4 と統合して採用を推奨 | 四つの独立した意味機構を作らず、二つの台帳と宣言側の分類で表す |

優先順位は **4＋11、5＋6、7＋8、10、9**。9 も公開ランタイムの出力が定着する前に採用したい。5＋6 は外部 Kotonoha の受理開始前、7＋8 は該当操作の Emit 開始前に確定する。

## 2. 現状から確認できること

- [§22.1](../../SPEC.md#221-required-core-declarations) は public な required declarations の表に、名前・Signature 未定の object ownership operations を含める。[§13.5.8](../../SPEC.md#1358-object-ownership-creation-and-sharing) ではそれらを意味操作として定義している。現状は「必須宣言の何を検査するか」が未完であり、意味操作の設計自体が不可能という矛盾ではない。
- [CoreIntrinsics.cs](../../Kimi/Compiler/Binding/CoreIntrinsics.cs) と [CoreDeclaration.cs](../../Kimi/Compiler/Binding/CoreDeclaration.cs) にも `ObjectOwnership` が空の Name、Symbol なし、Missing として登録されている。`IsCompleteLibrary` は全項目の宣言検証を要求する。これは仕様上の分類の問題が実装に現れた例である。
- 実装はすでに Copy/Owned/Callable の `IntrinsicKind` と、writeLine の `CompilerFunctionKind` を持つ。全体を作り直すより、既存の意味 ID を正しい台帳へ整理する変更が適している。
- [§18.3](../../SPEC.md#183-source-artifacts-and-binary-interfaces) は情報カテゴリを列挙し、互換性の詳細を別仕様へ委ねる。ただし [§21.3.4](../../SPEC.md#2134-artifacts-verification-and-invalidation) は、specialization の集合・body・依存内容の変更による無効化をすでに要求している。互換性をゼロから設計するのではなく、この既存規則を一般化する。
- [KotonohaIdentifier.cs](../../Kimi/Compiler/Core/KotonohaIdentifier.cs) の Name/Version は依存の要求である。[Kotonoha.cs](../../Kimi/Compiler/Core/Kotonoha.cs) の `Id` は名前の hash を uint に切り詰めた値であり、永続的な生成物の同一性として使える契約ではない。[STATUS.md](../../STATUS.md#c2-builds-modules-and-source-artifacts) によれば外部 Kotonoha のロードは未実装で、現行の保存機構はソースの再構築用である。
- [§18.1](../../SPEC.md#181-external-references-and-aliases) は source Type alias を導入していない。一方 §8.1 の `Reject alias cycles` や §13.5.1 の `Type candidates include Type aliases` は、将来設計の注記を読まずに実装すると誤解しやすい。用語整理ではこの種の能動的な要件も修正する。

## 3. Core：二つの台帳と一つの検証境界

### 3.1. 意味の異なる分類を増やしすぎない

§22.1 を **Core declarations and semantic operations** に改め、次の二表へ分ける。本書の Core は Core Kotonoha を指す。型の構成要素を表す Core との区別は維持する。

| 台帳 | 識別対象 | ソース Name / Symbol |
| --- | --- | --- |
| Core Declaration Catalog | 言語が指定する Core 宣言 | 現行ソースから使える宣言には必須 |
| Semantic Operation Catalog | コンパイラが意味を認識する操作 | 意味 ID は必須。ソースへの公開は別の規則 |

宣言側には次の分類を付ける。新しい構文や型の種類ではなく、検証する契約を示す属性である。

| 宣言分類 | 対象 | 検証する境界 |
| --- | --- | --- |
| Designated nominal declarations | Option、Result、Array、Index、Range、ResolvedRange、Slice、Dictionary | 指定された nominal identity と各節の shape・能力・操作契約 |
| Protocol declarations | Stringify、Equatable、Comparable、Iterator、Iterable | requirement の shape と各節の特則。ユーザー型の適合は既存規則に従う |
| Compiler-derived requirements | Copy、Owned、Callable | 宣言の identity と shape に加え、言語が定める導出規則。通常のユーザー実装で代替しない |
| Required functions | writeLine | 通常の関数 Signature と指定された意味・実装契約 |

Option/Result を intrinsic nominal と呼ぶこと自体は可能だが、通常の enum の Copy/Owned 規則から外れる印象を避け、`designated` を推奨する。Iterator/Iterable の Element だけに認められた complete-Type の例外も、この分類を理由に他の Contract へ広げない。

primitive string、scalar、固定配列の構文由来 identity は、それぞれの言語規則で管理する。この整理を理由に架空の public Core 宣言を要求しない。

writeLine のように、宣言と実装操作の両方を持つものがあってよい。二表は対象の異なる台帳であり、機能を必ず片方だけへ分類する仕組みではない。

### 3.2. 意味操作 ID

次の識別子は仕様用の記号であり、ソース構文、公開 enum、数値 ABI を定めない。

| Operation ID | 意味上の入力 | 結果 |
| --- | --- | --- |
| `ObjectCreate` | by-value acquisition を行う `owner/T` | fresh `obj/T` |
| `RcCreate` | 同上 | strong count が 1 の fresh `rc/T` |
| `ArcCreate` | 同上 | strong count が 1 の fresh `arc/T` |
| `RcDuplicate` | Initialized な `rc/V` handle への shared access | 同一 object の追加の `rc/V` owner |
| `ArcDuplicate` | Initialized な `arc/V` handle への shared access | 同一 object の追加の `arc/V` owner |
| `WriteLine` | owned string を一度取得 | UTF-8 内容と LF を標準出力へ書き、Unit を返す |

`Clone` と `Duplicate` は併用せず、現行 §13.5.8 の表現に合わせて Duplicate を使う。意味契約の改訂は language/Core contract revision で識別し、同じ ID に互換性のない意味を黙って割り当てない。

各 operation の意味 schema は、完全な入力・結果 Type、generic/Origin の対応、取得方法、Loan と依存の保持、評価回数・順序、副作用、失敗、破棄責任を含む。物理 ABI は別に定める。特に以下を維持する。

- creation の T は object payload として許される exact concrete Core。enum や runtime Contract View を新たに payload として許さない。
- by-value acquisition は既存の Copy/Move 規則に従う。すべての入力を無条件 Move と定義しない。storage と metadata を完成してから handle を公開する。
- duplicate は count を一度増やす。payload の複製、view 変更、ユーザーコード呼出しを行わず、結果に元 handle の格納場所への新しい Loan を残さない。payload の依存は保持する。
- count overflow と必要な割当ての失敗は Abort。rc/arc の変換、obj の複製、weak reference、recoverable allocation は追加しない。

### 3.3. 宣言と操作の対応付け

指定された Core provider が公開宣言を提供した段階で、検証済みの **宣言 Symbol Identity → Operation ID と contract revision** を登録する。公開名や通常の Signature を解決してから、その Symbol の意味情報を使用する。

`CoreDeclarationId` は言語上の指定された役割であり、特定 provider の永続 SymbolIdentity とは区別する。合成 Core とロードした Core は同じ指定役割・意味契約を満たす必要がある。異なる provider revision の Symbol を役割 ID だけで混在させることは認めず、すべての依存を選択した一つの Core provider に整合させる。

対応付けでは宣言の種類、generic slots、ラベル、receiver、完全 Type、Origins、取得・結果契約、unsafe 性、意味契約との整合性を検証する。単に引数と戻り値のビット幅が同じでは不十分である。不明な ID、重複する競合登録、互換性のない Signature、一般ユーザー宣言による指定の偽装は拒否する。ユーザー向けの任意 `#Intrinsic` 属性は導入しない。

通常の lookup、overload selection、アクセス検査、Type/Origin/Loan 検査を経た後、Binding が意味 ID を保持し、Lowering がその ID を使う。同じ名前のユーザー関数、alias、shadowing に特別な意味を付与しない。意味操作の未対応を理由に別の overload へ戻らない。

公開 API の改名は利用ソースを壊し得る。ここで独立させるのは意味操作と Lowering であり、ソース互換性まで自動的に保証するものではない。

### 3.4. 必須性と未実装を分ける

規範文案：

> Every Compilation designates exactly one Core provider. Every declaration required by this language revision must have a unique, compatible designated Symbol before finalization, whether or not source code refers to it. Semantic operations are identified independently of public declaration names. An operation whose source binding is deferred does not impose a missing-declaration requirement and introduces no callable source spelling. A source binding, when specified, must validate against the operation's semantic contract before use.

宣言の状態は `Missing / Invalid / Validated` を維持する。operation は意味契約の認識・検証と、target 上の実装可否を別に持つ。source binding の状態 `Deferred` は、実装の `Unsupported` や宣言の `Missing` と混ぜない。

意味契約が定義済みでも、未指定の source API を仮の名前で呼べることにはならない。逆に、必要な操作の実装がない場合は §21.4 の supported-operation checks で Emit 前に拒否し、optimizer が未使用 body/branch を消すことを救済にしない。

`IsCompleteLibrary` の一つの bool ですべてを説明することをやめ、実装上は `AreRequiredDeclarationsValidated` と target/operation support を区別することを推奨する。完全な言語実装の適合性には別途すべての必須意味・実行契約の実装が必要であり、宣言の検証完了だけで full conformance を名乗らない。

## 4. Kotonoha・Symbol・生成物の同一性

### 4.1. 公開 interface hash だけでは不足する

同じ `Foo 1.2.0` が同じ public Signature で、一方の関数 body が 1、もう一方が 2 を返す場合、公開 interface hash は一致し得る。generic body、inline された body、specialization body、private helper についても同様である。等しい API shape は等しい実装やキャッシュの再利用を意味しない。

反対に、再圧縮や debug 情報だけが変わった binary を、別の nominal Type の世界として扱う必要もない。この二つを区別する。

### 4.2. 推奨する論理モデル

以下は内部クラス名や具体的な hash encoding を強制しない。意味上の区別を規定する。

| 概念 | 意味 |
| --- | --- |
| `KotonohaIdentity` | 発行元・名前空間によって一意になるモジュール系列の identity。同名の別提供元を区別する不透明な値 |
| `Version` | 人間向けの版名・依存解決の条件。同一性の証明ではない |
| `RevisionIdentity` | 一つの KotonohaIdentity に属する、不変の意味入力の改訂 identity |
| `ReferenceName` | 一つの Compilation の直接依存を指すローカルな Name |
| `SymbolIdentity` | `(RevisionIdentity, DeclarationKey)` |
| `SemanticFingerprint` | 正規化された意味インターフェースの内容を検証・比較するための値 |
| `ArtifactIdentity` | 配布・キャッシュ対象となる一つの生成物の内容 identity。意味 metadata、必要な body、code、target/ABI、依存記録等を覆う |

`RevisionIdentity` は少なくとも KotonohaIdentity、実装 body を含む意味入力、適用する言語意味の改訂、生成ソースとその意味に関わる設定、環境選択に使う設定、および解決済み依存の RevisionIdentity に対して不変でなければならない。これらのどれかを変更して同じ revision と称することを禁止する。

初版はこれらの入力を内容で固定する保守的な方式を推奨する。元ソース全体を含めれば、コメント変更でも revision が変わり得るが、意味上の同値性判定器を新設せずに済む。Version 文字列を付け替えただけでは意味入力は変わらない。最適化設定や debug/package encoding だけの変更は、意味入力を変えない限り ArtifactIdentity のみを変えてよい。

target が `#if` 等の宣言選択に使われた場合はその選択入力を revision に含める。選択と意味契約を変えない target 別の ABI/code 表現は別 artifact として表現できる。判断できない場合は同じ revision に統合しない。

`DeclarationKey` は当該 revision 内で一意で、外部から参照可能な宣言・必要な private 宣言・generic/Origin の binder に安定した対応を与える。ソース名だけやメモリアドレスだけを key にしない。ロード順や Symbol オブジェクトの配置にも依存させない。改訂をまたいだ DeclarationKey の一致から Type identity を推測しない。

意味データ内の自己参照は local DeclarationKey で表し、外部参照は既知の RevisionIdentity と DeclarationKey で表す。Symbol に自己 interface hash を埋め、その全体を再び hash する循環定義は採用しない。生成物自身の digest も digest 対象から除く。

hash のアルゴリズム、canonical encoding、領域識別、衝突・改竄検出手順は artifact 形式で定める。ただし、短い名前 hash、ファイルパス、timestamp、列挙順を永続 identity の代用にしないことは今決める。同じ identity に異なる内容が対応すると検出した場合は必ず拒否する。hash 一致だけでは提供元の信頼性や metadata の意味上の正しさを証明できない。

### 4.3. 同一性の具体的な結果

| 状況 | 結果 |
| --- | --- |
| FooA と FooB が同じ revision の同じ X を参照 | 同じ Symbol/nominal Type。reference name は変換を生まない |
| 同じ Name/Version、異なる source/body 内容 | 異なる revision。名前や shape で統合しない |
| 同じ revision、異なる最適化・debug packaging | artifact は異なり得るが Symbol identity は同じ。実行時には互換な一つの提供物へ一貫して解決する |
| 同じ ArtifactIdentity を名乗るが内容が違う | 検証エラー。先にロードした方を採用しない |
| 同じ Version、同じ layout、異なる revision の X | 別の nominal Type。暗黙の変換・witness 共有はしない |
| 依存 P が Foo の revision A、Q が revision B の X を公開 | 別 Type のまま検査する。相互に渡すコードは通常の Type 不一致 |

異なる revision の共存は、異なる reference name とそれぞれの依存閉包を用いて認める。同じ reference name を二つの revision へ解決してはならない。transitive metadata のロードは source Name Reachability を追加しない。Core provider については引き続き Compilation ごとに一つとし、異なる Core 契約をそのまま混在させない。

一つの最終プログラムで同じ revision の異なる binary provider を無秩序に二重登録しない。選択済み provider と全利用側の依存記録を検証し、必要なら利用側を再構築する。共存する別 revision の native symbol も区別できる ABI が必要であり、現在の inspection-only Library IR に cross-Kotonoha linking を追加したことにはならない。

この初版の厳密な revision identity は、private body 変更でも nominal identity と依存側の再構築範囲が変わり得るという代償がある。将来の hot replacement や互換 ABI による差替えは、明示的な互換性契約として追加する。SemVer だけによる自動的な同一視は採用しない。

## 5. SemanticInterface と受理判定

### 5.1. 必須となる意味情報

`SemanticInterface` は public API 一覧に限定しない。外部から意味検査・実装選択・必要な生成を再現できる論理モデルであり、private 情報を含むことと、その名前を公開することは別である。

| 情報 | 必須条件と内容 |
| --- | --- |
| Header / capabilities | schema revision、language/Core contract revision、必要な semantic features、producer identity。未知の必須 feature は拒否 |
| Module / Symbols | 上記 revision と宣言対応、access/enclosing domain、public paths、open/base 関係 |
| DeclarationSignature | 宣言種類、ラベル、receiver、parameter/result の完全 Type、取得・unsafe・default 等の利用に必要な契約 |
| GenericSchema | slot の種類・順序・束縛、長さ引数、associated projections、Constraints、正当な deferred obligations と検証期限 |
| OriginSchema | binder による対応、static、outlives、per-call quantification、借用・取得・結果の依存。名前の綴りだけで対応付けない |
| ConformanceMap | requirement/implementation の Symbol、associated-Type 対応、conditional premises、検証済み witness、必要な Property permissions と accessor 契約 |
| SpecializationSet | 定義側で閉じた集合、元宣言との対応、選択 key と必要な body/dependency 情報。空の集合も明示する |
| Verification dependencies | §15.6.4 の必要な effect summaries・返却 storage anchors、capture/environment、破棄・storage release、object metadata 等、当該機能の検査で使う情報 |
| Implementation material | 外部で instantiate/inline 等する場合の body または参照先と content identity。既存 code だけで利用する場合はその利用契約 |
| Layout / ABI | 利用目的が必要とする Type layout、calling convention、receiver adjustment、runtime descriptor の契約と target profile |
| Dependency requirements | 参照先 revision、必要な semantic fingerprint、body/content 依存、code 再利用時の artifact/ABI 依存と環境設定 |

「該当しない」と「不明・欠落」を区別する。たとえば generic 宣言がないことは空の GenericSchema で表せるが、generic 宣言があるのに schema がない状態を空と解釈しない。Layout が不要な意味検査専用 interface はその能力だけを宣言し、native linking に使えるとは扱わない。

§18.3 に列挙された将来機能の metadata は、その機能を提供する revision に限って必要となる。未導入の virtual や runtime Contract View の欄を必須にして、現行 artifact に存在しない機能を捏造しない。

正規化は binder の対応、透明参照、解決済み projection、grouping、冗長 owner 表記を処理し、全 Semantics 層・Origin・nominal identity を残す。制約の同値性は既存の proof system が認める範囲に限定する。一般の論理同値性を決定する新しい検査器は要求しない。set の順序は正規化できるが、parameter/slot 順序や意味のある宣言順は消さない。

### 5.2. 判定には利用目的と依存解決が必要

推奨する仕様上の関係は次とする。公開関数 API ではない。

```text
CanConsume(Artifact, ConsumerContract, TargetProfile,
           ResolvedDependencies, UsePurpose)
    -> Yes | Rebuild(Reason, RequiredInputs) | Error(Reason)
```

`UsePurpose` は意味検査、generic 等のコード生成、既存 native code の再利用を区別する。一つの利用で複数目的が必要なら、すべてを満たす必要がある。compiler の製品名や version だけから受理可否を判断しない。

規範的な判定順序：

1. 必要な形式を解釈でき、内容 identity と構造が有効であることを確認する。壊れた内容、競合 identity、必須 schema の欠落は Error。
2. 使用される言語意味・Core 契約・必須 feature を consumer が理解し、要求された意味検査を保存できることを確認する。未対応を ordinary operation や古い規則へ読み替えない。
3. 必要な依存閉包を exact revision に解決し、Symbol 参照、アクセス、Type、Origin、conformance、specialization と検証義務の整合性を確認する。ロード順による修復・選択は禁止。
4. 利用目的に応じて body、effect、layout、ABI、target、runtime support、code/dependency content を確認する。必要な意味検査は source 由来と同等の不変条件を満たす。
5. すべて満たせば Yes。再利用だけが不可能で、対応する元ソースまたは再生成可能な表現と完全な依存・設定が確保され、consumer がその入力を処理できる場合は Rebuild。必要な入力も実装能力もなければ Error。

Rebuild は「再構築を試みるための入力がそろっている」という判定であり、コンパイル成功の保証ではない。再構築結果には同じ検査を行い、成功前に古い生成物をリンクしない。壊れた binary をそのまま Rebuild として受理することもない。検証済みの別ソースを選び直す場合は、依存解決から別の入力としてやり直す。

意味入力・依存 revision の変更が必要な場合は、元 artifact の再生成と区別し、新 revision の解決と影響する consumer の再検査・再構築を行う。Rebuild を名目に既存 SymbolIdentity を別の宣言へ差し替えない。

### 5.3. 最小互換性規則

| 差・欠落 | 判定 |
| --- | --- |
| 異なる ReferenceName だけ | Yes。identity、契約、利用目的を満たすことが前提 |
| producer の build 番号だけが異なる | 意味 interface は明示された対応能力で検査。初版の生成済み code cache は exact compiler build/settings を要求し、不一致なら Rebuild または Error |
| 未知の任意 annotation | 形式が意味に無関係と明示する項目のみ無視可能。未知の必須意味情報は Error |
| Type/Origin/unsafe/ownership 契約の不整合 | 既存 binding のままの利用は Error。ソースからの新しい binding が必要 |
| effect summary や witness mapping の必要情報がない | interface として不完全なら Error。検証を省略して受理しない |
| 依存 version 条件は合うが resolved revision が違う | 現在の binding には利用不可。明示した依存解決の更新と consumer の再構築、または Error |
| API は同じで specialization 集合・body・private helper が変わった | exact content/revision の不一致として検出。旧 selection、inline、code cache を再利用しない |
| machine code が別 target/ABI 向け | 意味検査だけの利用可否と区別する。code 利用は正しい入力から Rebuild、なければ Error |
| generic instantiation に必要な body/生成表現がない | 該当用途は Rebuild または Error。既存の特定実体の code があることから任意実体を許可しない |
| 同じ意味契約・依存・用途、異なる格納圧縮方式 | decoder が対応し検証できれば Yes。圧縮方式から Type identity は変化しない |

初版は exact revision と保守的な依存一致を基準にする。互換性のないものを拒否する規則を先に確定し、依存の一部変更でも再利用できる細粒度化は、正しさを証明できる実装改善として後から加える。

依存 fingerprint は公開 API だけでなく、実装選択・検証・生成に実際に依存した情報を覆う。初版は依存 artifact 全体を記録して過剰に無効化してもよい。identity と意味参照を先に固定し、content fingerprint を後で計算する。循環依存を扱う形式には別途 group 単位の identity/検証規則が必要であり、それがない初版 decoder は循環した binary dependency graph を明示的に拒否する。ソース言語の依存循環全般を新しく禁止する提案ではない。

形式の encoding は延期できるが、外部 binary の受理を開始するまでには canonicalization、必須欄、整合性検証、body 表現、dependency graph、ABI の対応範囲を artifact-interface 仕様として完成させる。「SemanticInterface があるから任意の binary が読める」とはしない。

## 6. windows-x64-v1 の provenance 契約

### 6.1. 元の提案の修正点

`pointer -> usize` が provenance を失うという表現は、**結果の整数は provenance を運ばない**と書く。Copy な元ポインターの provenance・有効性を破壊する操作ではない。また address の取得を `expose provenance` と呼ばず、将来の exposed-provenance API と区別する。

§5.5 は、現在は documented Compiler/target guarantee による追加の根拠を許している。本案では windows-x64-v1 がその追加保証を与えないことを明記する。将来の別 profile の能力までここで固定しない。

### 6.2. 規範規則

> In windows-x64-v1, converting a raw pointer to usize yields its address without carrying or exposing access provenance; it does not change the original pointer. Every usize-to-raw-pointer conversion produces a pointer with no allocation access provenance, including an unchanged pointer–integer–pointer round trip. Address equality, retained integer data flow, serialization, and address reuse do not recover provenance. Forming a dereferenced place or performing nonzero pointer displacement with such a pointer violates the unsafe contract and is undefined behavior. No detection, trap, or runtime provenance representation is required.

null は整数 0 と相互変換する。整数から作った pointer も保持・Copy・Move・受渡し・破棄・同型の address equality・null test は可能である。cast の unsafe context 要件は変わらない。既存条件を満たす displacement 0 は元 pointer を保ち、権限を増やさない。

この初期契約ではゼロサイズ Type に対しても provenance なしの dereferenced place を認めない。§5.2 の place formation の前提に従う。`unsafe/()` の算術は、別途 positive stride 条件によって依然として不許可である。

```kimi
// p は live な i32 storage を指す有効な raw pointer とする。
unsafe
    let address = p@usize
    let q = address@unsafe/i32
    let equal = p == q // true。比較は有効。
    let original = *p // 元の access 条件が保たれていれば有効。
    // let reconstructed = *q // 実行すると UB。address equality は根拠にならない。
```

有効な pointer 値の Copy、通常の pointer 値の格納と読出し、同じ address space 内の pointer-Type cast、および許された pointer arithmetic は、既存の provenance を保存する。格納先が integer である場合や、pointer の bytes を整数として再構成する場合をこの保存規則へ含めない。任意の bytewise reconstruction API は未指定のままである。

provenance の起点は、個別に指定された割当て・pointer 取得操作、または foreign pointer の契約である。初期ランタイムの Alloc は §22.5.2 の割当てに対応する根拠を持つが、メモリはまだ未初期化である。pointer を返す FFI も、native 側がどの live storage をどの権限・寿命で返すかという契約に従う。型が pointer の foreign return というだけで任意の整数にアクセス権を付与しない。入力 pointer をそのまま返す foreign wrapper は、入力にない provenance を回復しない。

固定アドレスの MMIO、整数 handle からの mapping、provenance recovery は個別 API/target 契約を要する。必要になれば、保持した有効な基準 pointer と address を受ける操作を先に検討する。グローバルな expose/recover の仕組みや新 API spelling は今回導入しない。

### 6.3. LLVM との関係

元提案の「LLVM と衝突しにくい」は採用理由の補助にはなるが、「LLVM の inttoptr 自体が provenance を必ず消す」と解釈してはいけない。LLVM 22.1.0 の LangRef は inttoptr に、値の計算へ寄与した pointer との based-on 関係を定めている。[LLVM Pointer Aliasing Rules](https://releases.llvm.org/22.1.0/docs/LangRef.html#pointer-aliasing-rules)

したがって本案は **Kimigayo のソース契約としての選択**である。§21.5.3 の ptrtoint/inttoptr lowering をただちに変更する必要はないが、その LLVM 表現がソース契約を検出・強制するとは主張しない。定義済み動作を保ち、ソース側で UB のコードが偶然動くことから新しい保証を導かない。新しい noalias/inbounds 属性や強い最適化にはそれぞれの LLVM 条件の証明が必要である。固定 target の実装検証は SPEC 指定の LLVM 22.1.8 で別途行う。

参考として Rust には allocation provenance を持たない pointer を作る専用操作があるが、通常の整数 cast とは区別され、ゼロサイズ access の扱いも本案とは異なる。Rust の cast 規則をそのまま採用した提案ではない。[Rust without_provenance](https://doc.rust-lang.org/std/ptr/fn.without_provenance.html)

## 7. FFI：unsafe 契約違反と UB

### 7.1. 「保証が少ない」だけでは UB と断定しない

§1.3 は unspecified が UB を意味しないことを明記し、§7.1 の unsafe function は safety contract 違反を UB としている。FFI の実行時境界契約を後者へ接続する変更が適切である。一方、Abort にも cleanup 保証はないため、「cleanup を保証しない」すべてを UB へ置き換えるのは誤りである。

§1.3 の用語表には次を追加する。

> **undefined behavior:** Execution that violates a condition explicitly designated as an unsafe runtime contract. This specification imposes no requirements on that execution. Detection, diagnostics, Abort, and cleanup are not guaranteed. Unsafe context does not waive the contract.

### 7.2. FFI の置換文案

§22.3.1 の境界部分を次のようにする。

> A foreign call must return normally to its Kimigayo caller unless execution terminates under another explicitly permitted contract. C++ exceptions, SEH unwinding, and longjmp must not unwind across or bypass a Kimigayo frame. A foreign call must not call back into or otherwise reenter Kimigayo while the call is outstanding. Executing any of these forbidden boundary crossings is undefined behavior. Control transfers and exception handling contained entirely within foreign code are permitted when the call otherwise satisfies its contract. No automatic marshalling, cleanup adapter, exception translation, or environment repair is provided.

callback と reentry は `foreign → Kimigayo` の入口を禁止することで明確にし、通常の `Kimigayo → foreign → normal return` は許可する。foreign 内だけで完結する callback や例外処理まで禁止しない。export や新しい thread entry を許可する条文にはしない。

[§21.5.4](../../SPEC.md#2154-floating-point-environment) の FP 境界にも、ABI が要求する MXCSR control state で入ること・foreign code が正常復帰前に戻すことへの違反は unsafe runtime contract violation であり UB、と同じ用語を適用する。§13.5 の FP conversion の参照文もこれに合わせる。MXCSR status bits が観測可能になるわけではない。

### 7.3. 維持する区別

| 事象 | 扱い |
| --- | --- |
| 禁止された宣言形式、unsafe context 欠如、非対応 Signature | compile-time error |
| 未解決の native symbol | 既存の link/load failure。実行時 UB へ転嫁しない |
| foreign API が契約どおり失敗値を返す | 通常の API 結果 |
| 規定済み runtime の allocation/write failure | 規定どおり Abort |
| 禁止 unwind/reentry、メモリ safety 条件違反、FP 境界違反 | UB。必須 trap や診断を要求しない |
| 無効な操作を実行していない未実行 branch | UB は発生しない。通常の静的検査は省略しない |

`nounwind` を source の禁止規則だけから付けないという現行制約は維持する。UB の用語整理と、LLVM 属性・SEH・unwind metadata の適法な生成は別の問題である。これを契機に landingpad や外部例外の捕捉を導入しない。

## 8. f32/f64 Stringify：一意の最短有効桁と表記

### 8.1. 保証する対象

§12.3.3 の組込み f32/f64 mapping の出力を固定する。ユーザー型の Stringify へ同じ数値表記を強制せず、allocation・独立した owned string・shared borrow・一度だけの評価は現行どおりにする。

「最短 round-trip」だけでは、同じ桁数の候補、固定/指数表記、指数の正符号、末尾の `.0` 等が残る。次の二段階に分ける。

### 8.2. 数値候補の選択

有限・非ゼロの入力 x の絶対値を、元の Type F（f32 または f64）の正確な二進値として扱う。

1. 正規化された正の十進値 `d = m × 10^q` を考える。m は正の整数で 10 の倍数ではなく、q は整数。m の十進桁数を n とする。
2. d を正確な値から F へ roundTiesToEven・gradual underflow で一度丸めた結果が `abs(x)` と一致する候補を求める。f32 を f64 に拡張してから f64 用に最短化しない。
3. n が最小の候補だけを残し、その中で `abs(d - abs(x))` が最小のものを選ぶ。
4. 距離が等しければ m が偶数の候補を優先する。それでも同順位なら数値 d の小さい方を選ぶ。比較は正確な整数・有理数に基づく意味であり、host float の誤差を規則に含めない。

ここで最短とは有効桁数の最小化であり、最終文字列の byte 数の最小化ではない。実装に候補列挙を要求するものではなく、同じ結果を出すどのアルゴリズムも使える。

### 8.3. 表記

m の十進桁を `d1…dn`、正規化された十進指数を `e = q + n - 1` とする。

| 条件 | 出力規則 |
| --- | --- |
| `-6 <= e < 21` | 固定小数表記。必要な整数部/小数部の 0 を補い、小数点は小数部があるときだけ出す |
| それ以外 | `d1[.d2…dn]eE`。n が 1 なら小数点なし。E は指数 e の最小の十進表記 |
| 負の有限非ゼロ | 上記の前に `-` |
| 正のゼロ / 負のゼロ | `0` / `-0` |
| 正の無限大 / 負の無限大 | `Infinity` / `-Infinity` |
| 任意の NaN | `NaN`。符号・payload を表示しない |

ASCII の数字、`-`、`.`、小文字 `e` を使う。指数の `+` と先頭 0、桁区切り、余分な小数部末尾の 0、不要な `.0` を出さない。有限数の先頭 0 は `0.xxx` に必要なものだけとする。正規化された m が 10 の倍数でないため、末尾 0 を消す操作は値を変えずに一意に決まる。整数部分で値を表すために必要な 0 は消さない。

この固定/指数の閾値は本案の表示上の選択であり、IEEE 754 が要求する値ではない。f32/f64 で同じ規則を使い、読みやすさを保つため `1000000` のような表記も許す。

| 入力値または bit pattern | 期待出力 |
| --- | --- |
| f32/f64 の +0 / -0 | `0` / `-0` |
| f32/f64 の 1 / -1.5 | `1` / `-1.5` |
| `0.1` を各 Type に直接 fitting した値 | 両方とも `0.1` |
| f32 `0x3F800001` | `1.0000001` |
| f64 `0x3FF0000000000001` | `1.0000000000000002` |
| f64 に fitting した `0.000001` / `0.0000001` | `0.000001` / `1e-7` |
| f64 に fitting した `1e20` / `1e21` | `100000000000000000000` / `1e21` |
| f32 最小正 subnormal `0x00000001` | `1e-45` |
| f64 最小正 subnormal `0x0000000000000001` | `5e-324` |
| f32 最大有限 `0x7F7FFFFF` | `3.4028235e38` |
| f64 最大有限 `0x7FEFFFFFFFFFFFFF` | `1.7976931348623157e308` |

round-trip は出力十進値を **同じ F へ正確に一度丸め直す**性質である。新しい parse API や、出力が常に同型の source literal になるという保証は導入しない。たとえば `1` や `-0` は source で期待 Type がなければ float の復元表現ではない。NaN payload の保存や異なる float Type 間の bit 保存も保証しない。

符号付きゼロは文字列では区別し、NaN は一つに正規化する。これは float の Equatable が両ゼロや全 NaN を等しいとする既存規則と矛盾しない。Equatable と Stringify に一対一の対応は要求されていない。Stringify を型情報・NaN payload を保存する binary serialization として扱わない。

数値の選択と文字列化を分離する設計には既存の仕様例もある。ただし Java の Double.toString は最短 1 桁時の特則や独自の表示閾値を持つため、出力をそのまま互換仕様として採用するのではない。[Java SE 24 Double.toString](https://docs.oracle.com/en/java/javase/24/docs/api/java.base/java/lang/Double.html#toString(double))

## 9. Type alias 用語の整理

一括で `alias` を置換しない。memory aliasing、source Container alias、generic substitution、associated-Type projection は別の概念である。

| 現在の文脈 | 改訂表現・扱い |
| --- | --- |
| `alias ExternalLib.Group` と lookup | **Container alias**。現行の source-local opening 規則を維持 |
| Type identity 計算中の `expand aliases` | **normalize transparent Type references** |
| §3.8 の `Alias equivalence` | **Transparent Type reference equivalence**。同じ resolved target を示す透明参照の関係 |
| §8.1 の `Reject alias cycles` | 無効な循環する内部透明参照を受理しないという実装不変条件へ限定。source Type-alias cycle checker は要求しない |
| §13.5.1 の Type alias を含む candidate 説明 | 現行の named Type、generic binding、許可された resolved projection の候補と role 制限を記述。source Type alias の候補種別を削除 |
| future feature の説明 | **Type alias** を維持するが、Deferred design と明記 |
| Loan/最適化における alias | memory aliasing の意味のまま維持 |

正規化は解決済み identity を保ち、wrapper・conversion・Copy/Move・権限変更を導入しない。すべての Semantics 層、nested Types、generic/Origin binder の対応を保つ。associated projections の解決がまだ正当化できなければ、既存の obligation として保持する。

内部透明参照の循環、Contract/base/dependency の不正な循環と、許された recursive nominal Type は区別する。`struct Node` が別の Node への参照を持つことを、透明参照の循環として一律に拒否しない。

§18.1 の境界文案：

> A source alias opens a Container only; it does not declare or rename a Type. Transparent Type reference normalization elsewhere in this specification resolves internal references while preserving the complete Type and declaration identity. It introduces no source declaration form. Any future Type-alias feature must specify its own syntax, lookup, and cycle rules before it is admitted.

## 10. 採用時の編集・実装範囲

| 対象 | 変更 |
| --- | --- |
| §22.1、§13.5.8、Appendix D/E | 宣言台帳と操作台帳、分類、公開 binding と必須性、意味 ID、未実装の区別 |
| §18 の導入、§18.1、§18.3 | revision/Symbol/artifact identity、ReferenceName、SemanticInterface、受理規則 |
| §8.8、§21.2–21.3、Appendix A/B | specialization・runtime nominal identity・code/cache keys を新しい identity 区分へ接続。Origin 消去規則は保持 |
| §5.4–5.6、§21.5.3、Appendix A.5 | windows-x64-v1 の provenance 契約、起点と保存、UB と lowering の区別 |
| §1.3、unsafe function の §7.1、§13.5 の FP 参照、§21.5.4、§22.3.1 | UB の用語と unsafe 境界への参照を統一 |
| §12.3.3、§22.1、runtime 検証項目 | canonical float 出力を規範化し、Core contract revision に含める |
| §3.4/3.8、§5.4、§8.1/8.8、§9.3、§13.5/13.6、§15.2、§18.1、§21.2–21.3、Appendix A | Type alias と透明参照の用語を文脈ごとに整理 |

提案 4 と 11、5 と 6 はそれぞれ同時に採用する。部分編集で古い必須宣言の表や `originating Kotonoha/version` だけによる identity 説明を残さない。既存の SPEC 内リンクは見出し変更と合わせて更新する。

実装ではまず CoreDeclaration から未命名の ObjectOwnership 群を外して operation metadata を分ける。既存の指定 Symbol を維持し、空名の架空 Symbol で穴を埋めない。部分実装の残りの Missing 宣言も引き続き正しく報告する。次に外部 Binding の前提となる永続 identity と interface validator を導入し、現在の Koto/source snapshot serialization を binary interface へ転用しない。

本案は Result の conditional Copy、virtual の現行提供範囲、Array の Origin 設計など、別の設計提案を自動的に採用しない。採用された仕様改訂の契約を catalog/interface が正確に記録する仕組みにする。

## 11. 採用後に必要な検証

以下は実装時の受け入れ条件であり、本提案書を作成した時点で実行済みの runtime test ではない。

提案書の確認として、ローカル参照先・見出し anchor・code fence を検査した。§8 の有限数の出力例 12 件は、正確な有理数で二進浮動小数点の丸め区間を求める独立の確認コードで、最短有効桁と候補選択を含めて照合した。Kimigayo の Stringify 実装や LLVM による実行を検証したものではない。

| 分野 | 主な確認例 |
| --- | --- |
| Core | 同名ユーザー宣言には特別扱いなし。alias/qualified lookup 後も同じ意味 ID。誤った binding は拒否。未命名 operation は Missing declaration に数えず、未実装操作も Emit へ通さない |
| Ownership operations | Copy/Move 入力、payload dependencies、元 handle の Loan 非保持、count 一回、overflow/割当て失敗の Abort、完全な破棄責任 |
| Identity | 同じ revision の二つの reference name は同型。同名同版の別 body は別 revision。競合 digest、古い依存、diamond graph、二つの Core を拒否 |
| Interface | Origin/effect/witness の削除・改変、未知必須 feature、private generic dependency、specialization の追加・削除・body 変更、target/ABI 不一致、Rebuild 入力不足 |
| Provenance | 元 pointer の有効な access、integer round-trip の equality、null、zero displacement、保存した pointer 値の利用。違反例はモデル/診断器の検査対象とし、runtime crash を合格条件にしない |
| FFI | 正常 return、foreign 内だけの exception handling、正しい FP state。禁止 unwind/reentry の UB に、特定の終了コードや cleanup 結果を要求しない |
| Float | 正負ゼロ、NaN、無限大、隣接値、tie、subnormal、最大有限、指数閾値、locale 非依存、f32 の二重丸め回避。round-trip だけでなく最短性と候補選択も検証 |
| Terminology | source Type alias を受理しない。Container alias の visibility と source isolation を維持。recursive nominal Type を誤って alias cycle と扱わない |

得られる改善は、**名前から意味を推測しないこと、同一性と再利用可能性を混同しないこと、実行時の保証を用語と境界条件で確定すること**である。新構文を増やさずに一貫性と検証可能性を高められる一方、厳密な改訂管理による再構築コストと、整数由来 pointer の操作制限は明示的に受け入れる。
