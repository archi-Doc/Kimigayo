# Kimigayo の依存設定・配布・検証済み成果物

2026-09-13 改訂。[初回レビュー](../Decisions/2026-09-13%20Dependencies%20and%20Artifacts%20Review.md)と[追加修正案の採否](../Decisions/2026-09-13%20Dependencies%20and%20Artifacts%20Revision.md)を統合した。**本書の対象では本書が SPEC と先行設計文書に優先する。SPEC.md 本体は変更しない。**

これは設計仕様であり、設定・コマンド・コード例は実装済みであることを示さない。実装状況は [STATUS](../../STATUS.md)、変更しない言語規則は [SPEC](../../SPEC.md) に従う。

## 1. 基本方針

1. **依存先の意味を定義側で確定する。** 名前、アクセス、型、generic、所有権の境界を配布方法によって変えない。
2. **入力を固定してから検証する。** 検証、生成、pack は同じ不変の入力を使う。
3. **解決・検証・生成の記録を分ける。** 版やファイル名だけで互換性を判断しない。
4. **製品を確定してからテストを追加する。** この境界を依存解決から生成予算まで維持する。

| 初期対応 | 後続の機能 |
| --- | --- |
| Project 参照、ソースパッケージ、完全一致の版、複数版の併存 | レジストリ、版範囲の自動解決 |
| ソースからの検証・最終生成、意味計画の任意キャッシュ | 機械語キャッシュ、ソース非公開バイナリ |
| 既存の native 接続契約 | 安定した外部 Kimigayo ABI、DLL・動的ロード、re-export |

一つの配布パッケージは一つの Library Kotonoha を持つ。現在の検査用 Library `.ll` は配布形式にしない。必要な型・Loan・効果・cleanup・生成経路が未対応なら診断する。

## 2. 依存と名前

### 2.1 識別の役割

| 識別 | 用途 |
| --- | --- |
| ReferenceName | 利用側のソースから直接依存を参照する名前 |
| PackageId／PackageVersion | 定義元のモジュールとリリース |
| SourceId | ソースパッケージの正確な内容（§4） |
| ProjectSnapshotId | ローカル Project の製品ソースと正規化した設定 |
| ModuleInputId | 実効環境と解決済み依存を含む、モジュール全体の検証入力 |
| DeclarationKey | 内容の世代をまたいで宣言を対応付けるキー（§5） |

PackageId は ASCII の小文字英数字を `.` または `-` で区切る名前とし、最初は小文字、空区画は禁止する。PackageVersion は ASCII 英数字で始まり、以後に英数字・`.`・`-`・`+` を使う。比較は大小文字を区別する完全一致とし、版番号の大小関係や互換性を推測しない。

ReferenceName は通常の言語識別子であり、project root の宣言や予約された Core の参照名と衝突してはならない。同じ依存を別名や別パスで指しても、別の型や static を作らない。異なる版の同名型は別の型である。

### 2.2 グラフと定義環境

依存グラフは循環のない有向グラフ（DAG）とする。自己参照・循環は依存経路付きで診断する。相互依存する宣言は一つの Kotonoha に置ける。

一つの target のグラフ全体で、同じ PackageId／Version には一つの入力と意味設定だけを認める。テスト拡張にもこの規則を適用する。同一入力への複数経路は統合し、異なる内容・設定は両経路を示して拒否する。入力の種別も照合し、発行済み Package と Project の暗黙置換は行わない。

直接依存だけが名前で参照できる。推移依存は型検査・生成のために読み込むが、名前を自動公開しない。公開署名中の外部型を名前で指定する場合も、その定義元への直接参照が必要である。source alias は再公開しない。

各モジュールは自身の alias、default alias、アクセス文脈、宣言、閉じた specialization 集合を使う。利用側からの宣言追加や再束縛は認めない。Core は compiler が選ぶ一つの identity と契約を全体で使う。

### 2.3 プロジェクト設定

参照される Library と配布対象では PackageId／PackageVersion を必須にする。Application が依存を使うために、自身の配布 ID を持つ必要はない。

```text
OutputKind="Library"
PackageId="example.geometry"
PackageVersion="1.0.0"
Targets=
  "x86_64-pc-windows-msvc"
Dependencies=
  Math={
    PackageId="example.math"
    PackageVersion="1.2.0"
    Project="../Math/Math.kimiproj"
  }
```

Dependencies は ReferenceName をキーとする map。値は ID／Version と、Project（`.kimiproj`）または Package（`.kimipkg`）のどちらか一つを持つ。相対パスは記述したプロジェクト基準とし、読み込んだ ID／Version を照合する。

PackageSources は取得元の配列であり、各要素は ID／Version／Package、または Store（ディレクトリ）のどちらかを持つ。Package 指定を初回の ID／Version 解決に使い、manifest／lock に SourceId がある依存は `<Store>/<SourceId>.kimipkg` を直接開ける。Store から版や内容を推測しない。

直接依存の Package 指定と、到達した Project の取得元も候補に加える。必要区分の Project 設定を先に集め、同版の異なる内容を診断してから解決する。読込順で選択を変えず、テスト側の候補を製品の解決に使わない。候補登録だけでは名前で参照できない。総当たり検索・版の自動選択はせず、要求された内容がなければ診断する。

```text
PackageSources=
  { Store="packages" }
```

未実装の旧 KotonohaArray は Dependencies に置き換える。非空の旧設定には移行診断を出し、取得元や参照名を推測しない。

### 2.4 実効環境と使用例

target と debug/release はグラフ全体で統一し、target は各依存の対応対象でもなければならない。CompileTimeSettings と default alias は定義側のものを使う。依存辺での設定上書き、暗黙の feature 統合、条件付き依存は初期版に導入しない。別構成を別モジュールとして提供する場合は別 PackageId を使う。

未指定の LangVersion はルートと同じ solution／compiler の既定値で解決し、別 solution を暗黙探索しない。配布 manifest には確定した LangVersion を保存する。初期グラフは一つの実効言語版と現在の compiler build で検証する。発行者と同じ compiler build は要求しない。

各内容 ID は、その対象に含まれる設定の確定した実効値を使う。省略と明示が同じ値なら区別しない。ただし配布内容に含まれない利用側の target／mode／compiler は SourceId に加えず、ModuleInputId に記録する。設定の既定値が変わって配布内容が変われば、発行済みの版は再使用できない（§4.4）。

```kimi
// Math/Arithmetic.kimi
public group Arithmetic
    public func twice(value: i32) -> i32 => value + value
```

```kimi
// Geometry/Measure.kimi。Dependencies の Math を使う。
alias Math.Arithmetic

public group Measure
    public func doubled(value: i32) -> i32 => twice(value)
```

## 3. 解決とビルド入力

### 3.1 コマンドの責任

| コマンド | 責任 |
| --- | --- |
| `restore <project>` | 製品を先に、ルートのテスト拡張を次に解決し、区分ごとの結果を一括保存 |
| check／build／emit-llvm／test | lock を読み、今回の入力を記録。lock は更新しない |
| `pack <project>` | lock を基に配布グラフを作り、検証して公開（§4） |
| run | 既存成果物を実行。依存解決・再構築はしない |

restore は宣言されたローカル取得元を読み、ネットワーク・Mod・対象ソースの実行は行わない。製品の解決失敗は非ゼロ終了とする。製品が解決できれば、テストだけの失敗は理由を表示して記録し、製品の restore は成功させる。設定全体を解釈できないエラーは区分へ隔離せず失敗する。

入力を使うコマンドは必要区分の有効な lock を要求する。必要区分の依存宣言が空なら解決結果は一意なので、lock がなくても空の解決結果として扱う。製品コマンドは TestDependencies の有無に左右されず、test は製品とテストの両方を必要区分とする。既存 lock の破損を空扱いで隠さない。

`--locked` は「restore が必要な差分があれば失敗する」の明示指定とする。通常の入力コマンドも自動 restore は行わないため同じ検査を行い、Project のソース編集を禁止する追加条件は持たない。check や test の `--list` にコード生成は要求しない。

```powershell
kimi restore Geometry.kimiproj
kimi check Geometry.kimiproj
# Math の本体だけを編集した後も、現在の入力で検証できる。
kimi check Geometry.kimiproj
# Project の現在の内容を検証。依存構造の変更には restore が必要。
kimi check Geometry.kimiproj --locked
```

### 3.2 lock と並行更新

`Name.kimiproj` に対して `Name.kimi.lock.json` を置く。schemaVersion は 1。各区分には解決済み／未解決と理由コード、パスを除いた依存構造、ノードの ID／Version／入力種別、参照名付きの辺、Package の SourceId を保存する。ルートは専用ノードで識別する。パスを含む詳細診断は別の処理記録へ置く。

ルートと Project は常に現在のソース・意味設定を使い、今回の snapshot は入力記録へ保存する。ID／Version、参照名・辺・入力種別、固定 Package の内容が変われば restore を要求する。ソースや依存構造以外の設定の編集では lock を書き換えないが、必要な再検証は行う。

取得場所は lock に保存せず、現在の `.kimiproj` から求める。移動先でも ID／Version／固定 SourceId を照合する。同じ解決結果なら場所の変更だけでは restore を要求しない。

restore は同じ設定 snapshot から両区分を作り、古いテスト区分を引き継がない。製品解決に失敗したときはテストも未解決とする。test は両区分の解決成功と、現在の製品・テスト両方の依存構造との一致を検査する。未解決・不一致なら restore を案内する。テスト区分に別の製品世代 digest は持たせない。

restore は読み始めた lock の内容を、書き込み直前に排他の下で再照合する。不一致なら競合として失敗する。staging から一括置換し、部分更新や古い内容による上書きを防ぐ。保存は決定的に行い、同じ結果では書き換えない。失敗した解決も未解決状態として保存できるが、前回の成功を今回の成功として流用しない。

### 3.3 不変の入力記録

ファイルを snapshot として確定し、その byte 列を検証・生成・pack で共有する。検査後に同じパスから内容を読み直して使わない。

ProjectSnapshotId は、製品ソースの論理パス・順序・byte 列と、正規化した意味設定を内容で識別する。`.kimiproj` 原文全体ではなく、ソース所属を反映した結果を使う。TestDependencies、テスト専用入力、取得場所、出力先、生成だけに影響する最適化設定は除外し、必要な別の記録へ保存する。依存の参照名と ID／Version、意味に関係する設定は残す。対応済みの Mod がある場合は、既存の再生成契約が要求する登録・実装・追加入力も含める。配布 SourceId に代用してはならない。

各処理はルートを含む全入力、実効環境、解決済みの辺、検証規則、実際に必要とした native 入力を build input record に保存する。native 未解決の段階はその状態を明示し、完成した link 入力の記録と区別する。出力は入力記録と自身の内容 hash に結び付け、未完了の処理を成功扱いしない。

入力記録は内容 ID ごとの不変データとし、入力 byte 列を複製せず、内容ストアの ID／hash と必要な構造・設定を保存する。依存 lock の一致だけで、ルート、native、toolchain、実行時環境まで固定されたとは扱わない。

管理キャッシュの回収では、最新ポインター、保存指定した記録・lock、実行・検証中の処理を起点に参照先を残す。処理は利用前に入力・成果物を pin し、回収と競合させない。参照されない記録と内容は回収できる。hash だけでは消えた入力を再現できず、再現を要求する記録は入力の閉包も保存する。利用者の配布ディレクトリは自動回収せず、発行済み版の対応表も回収しない（§4.4）。

## 4. ソース配布

### 4.1 pack の確定順序

pack は Library を対象とし、次の順に処理する。

1. 製品区分の lock を照合し、ルートと必要な Project の現在の入力を固定する。
2. Project 依存を末端から配布用の論理入力へ変換し、最終 SourceId を計算する。既存 Package は内容を照合して再利用する。
3. 親の依存辺を、子の PackageId／Version／SourceId へ固定する。全体と既知の発行済み版の一意性を確認する（§4.4）。
4. 通常の package loader と意味検証器で、最終グラフを検証する。
5. 既存 Package も含む依存の閉包を出力ストアへ保存する。子を先に、親を最後に公開し、各 ID と保存先を報告する。

変換ではソースの論理パス・順序、製品所属、実効 LangVersion、CompileTimeSettings、default alias、native 要求を保存し、取得パスを取り除く。環境で選択した枝だけを残したソースに書き換えず、元の条件付きソースを配布する。ProjectSnapshotId や ID／Version だけを根拠に、別の pack 済み成果物を選ばない。既存 Package を Project で置換する一般機能にはしない。

```text
Geometry -> Project Math
  1. Math の現在の配布入力を確定       -> SourceId = h_math
  2. Geometry の Math 参照を h_math に固定 -> SourceId = h_geometry
  3. 確定したグラフを検証
  4. h_math.kimipkg、h_geometry.kimipkg の順に公開
```

上の h_math／h_geometry は実際には64桁の内容 ID である。出力ディレクトリにはソースパッケージの閉包を揃え、同じ SourceId は一つにまとめる。native ファイル・実行時 DLL・toolchain はこの閉包に含めない。親の公開時点で、その参照する子が同じストアから取得可能でなければならない。

検証には archive 書き出し前の論理 manifest／file view を使える。検証済みの同じ byte 列を writer へ渡し、ZIP の作成・再展開を挟まない。通常は選択した target／mode を意味検証する。`pack --verify-all` は宣言した target × {debug, release} のすべてを要求し、未対応環境や一つでも検証失敗があれば公開しない。コード生成・native link・実行は要求しない。

manifest の targets は利用を許す対象であり、検証済み一覧ではない。実際に成功した組合せは SourceId・compiler build・実効設定とともに別の検証記録へ保存し、未検証を成功と表示しない。利用側は自分の環境で必ず検証する。upload や実行は行わない。

```powershell
# 依存一式を packages に保存。宣言した全 target・両 mode の検証も要求する。
kimi pack Geometry.kimiproj --output packages --verify-all
# Math の配布内容を編集した再発行では、Math と Geometry の版を更新して restore する。
```

初期 pack は Mod を必要としない入力に限る。生成結果だけを原ソースへ偽装しない。Mod 対応を追加するときは、登録、実装、host API、追加入力、target、順序、provenance を含む既存の再生成契約を適用する。

### 4.2 manifest とファイル

拡張子は `.kimipkg`。通常ファイルだけを収める ZIP とし、ルートに UTF-8 の `manifest.json` を置く。schemaVersion は 1、kind は `source`。JSON key は lowerCamelCase、プロジェクト設定は PascalCase とする。

| 必須 key | 内容 |
| --- | --- |
| schemaVersion、kind | 形式版と成果物種別 |
| packageId、packageVersion、langVersion | 定義元と確定した言語版 |
| targets | 利用を許す target の集合。検証記録とは区別する |
| compileTimeSettings、aliases | 定義側の設定と default alias |
| files | manifest 自身を除く全ファイルの path／size／sha256 |
| sources | 製品ソースの論理パスを並べた順序付き配列 |
| dependencies | 参照名から packageId／packageVersion／sourceId への map |
| requiredFeatures | 必須 compiler 機能の集合 |
| nativeRequirements | target ごとの native 要求（§7） |

空の map／配列も省略しない。compileTimeSettings の各値は bool／integer／string のうち一つだけを持つ。sources は重複せず、すべて files の要素を指す。files は archive 内の通常 entry と一対一に対応させる。テスト専用ファイル・依存は収めず、通常ソース中の `#Test` は原文のまま保持して所属規則で除外する。

論理パスは `/` 区切りの相対パスとし、空区画、`.`、`..`、絶対パス、drive、NUL、backslash、link entry を拒否する。Unicode 15.0.0（SPEC §2.5 と同じ版）で NFC 済みであることを要求し、自動正規化しない。重複検査には同版の default simple case folding（C／S、F／T は不使用）後に NFC 化したキーを使い、衝突を拒否する。SourceId には元の NFC パスを使う。このパス検査は言語の大文字小文字を区別する名前解決を変えない。

host パスへ展開することは要求しない。展開する場合は host の予約名等も検査し、別名への上書きを許さない。元ソースの改行や Unicode は書き換えない。

reader は未知の schema／key／必須機能、重複 key、不正な参照、範囲外の整数、不足・余分な entry、長さ・hash の不一致を拒否する。必須機能は compiler の機能 catalog で識別する。entry 数・展開 bytes・文字列長には有限の実装上限を置き、不正形式と資源不足を区別する。

### 4.3 内容 ID と完全性

SourceId は圧縮結果から独立した SHA-256 とする。manifest 自身に SourceId は含めない。

```text
SHA-256(
  ASCII "Kimigayo.Source.v1" + NUL
  + manifest.json の実 byte 列
)
```

manifest は全ファイルの path／size／sha256 を持つため、この ID が内容一式を間接的に識別する。ZIP の時刻や圧縮率は影響しない。ID の文字列表記は小文字の16進64桁に統一する。ただし SourceId の一致だけでは、実際の entry が manifest と一致する証拠にはならない。

writer は JSON の map を UTF-8 key 順、files を path 順、集合の配列を要素順に並べる。sources の意味上の順序は保存する。整数は10進数、空白・BOM なし、末尾 LF 一つとする。文字列は quote・backslash・制御文字だけを escape し、制御文字には小文字の `\u00xx` を使う。reader は他の合法 JSON 表記も読めるが、manifest bytes が違えば別 SourceId である。

外部入力を compiler 管理ストアへ登録する際は、全 entry の形式・長さ・実 hash を検証し、同じ snapshot をコピーして不変に保管する。検証済みの管理領域内では内容 ID で再利用できる。外部ファイルの名前・read-only 属性・manifest hash だけではこの検証を省かない。不変性を保証できなくなった内容は再検証する。完全性検証と意味検証の記録は別にする。

### 4.4 発行とストア

発行済み PackageId／Version は一つの SourceId にだけ結び付く。restore・登録・pack は、関連 lock、指定された取得元、管理ストア・出力ストアで確認できる対応を照合し、同版の別内容を拒否する。pack の診断は変更したパッケージの版の更新を案内する。子の SourceId 更新で親の配布入力も変われば、発行済みの親の版も更新する。未確認の別ストアまで世界全体の一意性を保証する規則ではない。

ストアにはこの対応表を永続保存する。内容ファイルを回収しても対応を忘れず、登録・発行の排他区間で再照合する。外部ストアの表を信頼の根拠にせず、採用するパッケージの manifest・内容を照合する。Project は発行前の編集対象なので、ProjectSnapshotId を SourceId とみなしたり Package と暗黙統合したりしない。

既定の出力先は `bin/packages/<SourceId>.kimipkg`。`pack --output <directory>` は出力ストアを指定する。ルートも子も内容 ID 名で保存し、ID／Version をそのままファイル名にしない。

一時ファイルを完成・検証した後、上書きしない atomic rename で公開する。既存ファイルや競合した書き手があれば完全性を検査し、同じ内容だけを再利用する。対応表の予約・公開は同じ排他と回復可能な記録で管理し、中断しても同版の別内容を通さない。親の公開前に閉包を揃えるが、途中で中断した場合に正しい子まで削除する必要はない。

## 5. 検証と再利用

### 5.1 三種類の一致

| 判定 | 確認内容 |
| --- | --- |
| 配布・入力の一致 | SourceId／ProjectSnapshotId、依存グラフ、実効環境 |
| 検証結果の有効性 | 宣言内容、前提、効果・適合・選択、不在依存、検証規則 |
| コード接続の一致 | target、配置、ABI、Core／runtime、native 供給と実 symbol |

版の一致、同じ型サイズ、LLVM verifier の成功を他の判定の代わりにしない。未検証・Unknown・Error・未実装は成功の証拠にならない。

ModuleInputId は元入力、実効言語版、compiler build、検証規則、Core、target／layout／意味に関係する profile、mode、設定、直接参照 mapping と依存先の ModuleInputId を含む。これは全体一致の高速経路であり、宣言の名前や生成候補の順序を決める ID ではない。

### 5.2 宣言・内容・検証結果

対応付けはノード、宣言の二段で行う。同じプロジェクトの旧新グラフで、ルートには専用キー root、依存には PackageId を使う。同じ ID の複数版などで対応が曖昧なら、その比較による再利用をせず再検証する。root は無関係な Application 間で共通の意味 identity を作る名前ではない。

対応したノード内の DeclarationKey は Container、宣言種別、正規化した宣言識別情報から作り、版・内容 hash・ソース位置を含めない。これは再利用候補を探すキーであり、実際の型・symbol・Composition Entry は引き続き定義元の版を含む identity を持つ。完全な編集追跡は要求しない。

対応付けの後に、検証が読んだ内容・環境と、その依存の現在の identity を照合する。署名、Origin、Constraint、access、body、効果、適合、選択集合は必要な範囲で別の内容事実として持つ。hash は比較の索引に使い、必要な構造・前提の検査を省かない。版をまたぐ参照は現在の型・宣言へ対応を検証して結び直し、確認できない判断は再検証する。キーや body の一致だけで古い型情報、証明、ABI を採用しない。

| 保存区分 | 主な内容 |
| --- | --- |
| 宣言・契約 | 所属、公開経路、open／base 関係、Field identity、完全な Type／Semantics／Origin、generic slot、Constraint、unsafe 条件、長さ条件 |
| 適合・公開保証 | witness・関連型 mapping、条件付き適合、閉じた specialization 集合、効果、返却 Loan の anchor、ObjectCompatible と原因 |
| 意味計画 | 検証済み body、取得・所有権・cleanup、定義側の束縛と private 依存、正当な表現義務 |
| 検証記録 | 対象、性質、前提、結果、内容依存、規則 identity、位置への参照と生成 provenance |

private 情報は生成・再検証に使えても、利用側の名前探索へ公開しない。公開契約の利用に private body の探索を要求しない。runtime で消去する Origin も意味情報には残す。

ObjectCompatible は検証を終えた Proven／NotProven とその原因だけを公開する。generic の正当な表現義務は対象・前提・依存・解決期限付きで保存できるが、完了した証明とは区別する。使用時の具体的な義務、Loan、初期化、cleanup の検査は省略しない。

### 5.3 変更の伝播

内容が変わったら、その内容を読んだ判断を未検証へ戻す。再検証した結論が同じなら、その結論だけに依存する利用側へ失効を広げない。body を直接使う generic 生成などは、結論が同じでも body の変更を反映する。

不在の判断も同じ内容依存として扱う。「同名宣言がない」「該当 specialization がない」という判断は、探索した Container や閉じた集合の内容へ依存する。Name・access・specialization の追加も検出し、空の集合と未取得を区別する。

再帰関数や効果の循環は、内部の循環成分をまとめて固定点を再計算する。完了した結果の内容ハッシュを取り、互いの hash を再帰的に展開しない。循環する未完了の証明は根拠にならない。モジュール DAG の禁止と、モジュール内部の合法な再帰を混同しない。

```text
private body を変更
  -> body と関連する効果を再検証
     ├─ 公開効果は同じ -> 効果だけに依存する呼び出し元は再利用
     └─ 公開効果が変化 -> その効果に依存する呼び出し元を再検証
  -> body に依存する生成物は更新
```

初期実装はモジュール全体の失効から始めてよい。細粒度の比較・対応付けを実装した範囲だけで再利用し、古い証明と新しい mapping を混在させない。失われた要件は利用箇所で診断し、発行側に未知の全利用者の受理を保証させない。

### 5.4 キャッシュの境界

初期の永続キャッシュは compiler 専用の検証済み意味計画だけとし、同じ形式版・compiler build・検証規則で使う。ABI、context、frame、予算選択、IR、機械語は現在の生成方式で作り直す。生成だけに影響する設定変更で意味計画を失効させない。

外部パッケージに同梱された「検証済み」の申告は採用しない。hash は内容の一致を示すもので、証明の正しさや発行者を証明しない。Koto の object graph、host pointer、runtime context graph を保存形式にしない。

古い・壊れた自前キャッシュは破棄して元入力から再検証する。必要な情報がなければ再構築要求を出す。配布パッケージ自体の破損を隠して古い入力を使い続けてはならない。

### 5.5 ソース位置

SourceId／ProjectSnapshotId は原文の変更を記録する。一方、細粒度の意味比較では製品所属の token 列・インデント構造を使い、意味を変えないコメントや空行を除ける。literal の内容、意味に関係する改行・所属・ソース順は保存する。この比較だけで再利用を確定せず、§5.2 の束縛・環境・内容依存も照合する。

意味計画内の位置は、論理ファイルと宣言に属する token の参照で持つ。現在の原文への対応表から行・桁を得て、テストの token が増えても製品の参照をずらさない。必要な字句・構文検査は現在の入力に対して行う。対応が確定できなければ位置情報を作り直し、古い診断位置を流用しない。Abort などに埋め込む位置は生成段階で結び付け、位置が変わった生成物を更新する。

## 6. 製品とテスト

### 6.1 入力の所属

TestDependencies は Dependencies と同じ形式、TestSources はプロジェクト相対の明示ファイル一覧とする。glob は導入せず、不正なパス・重複は設定エラーにする。通常のソース探索にも一致するファイルはテスト専用に分類し、製品として二重登録しない。分類を変えれば製品入力も変わる。テスト専用ファイルの読込・存在検査は test の責任とし、通常 build にテスト依存を要求しない。

```text
TestSources=
  "tests/MeasureTests.kimi"
```

```kimi
// tests/MeasureTests.kimi
group MeasureTests
    #Test
    func doubled()
        $expect(Measure.doubled(3) == 6)
```

テストは確定した製品の宣言・依存を参照できるが、製品を変更できない。同じ参照名を別の依存へ割り当てず、同一依存は統合する。依存ライブラリ自身のテスト・TestDependencies は推移しない。

テスト専用入力は製品の意味入力から除外する。通常ソース内の `#Test` 編集では原文の hash が変わるため解析をやり直してよいが、製品の意味・配置・共有・予算選択を変えない。行位置の変更に伴う診断位置や provenance は更新する。

### 6.2 生成要求と予算

コード生成を行う場合、製品入力と通常出力のルート集合から、製品の必要代入・共有クラス・呼出入口・frame・予算選択を先に確定する。test や `--list` の意味検証だけの段階では、この生成処理を実行する必要はない。

テストからの追加要求は、製品 generic の具体化も含めてテスト側へ所属させる。同じ代入の既存製品入口は再利用し、新しい入口から既存 body を使う場合も、既存契約へ接続できることを検証する。既存の共有クラス・context schema・frame を拡大せず、必要な追加計画をテスト側に作る。

製品とテストはそれぞれの生成量で予算を計算し、片方の候補・削減量・余剰を他方へ移さない。テスト側は全ケースをまとめた一つの区分とし、ケースごとに予算を増やさない。同順位の選択も製品の宣言・意味情報で決め、テストを含む SourceId や入力全体の hash を順序の種に使わない。

```text
製品の確定済み計画
  ├─ 製品だけの共有・特殊化予算
  └─ テストから既存入口を参照
       └─ テスト専用の追加代入・body・予算
```

製品計画を確定した後、テスト実行入口から必要なコードだけを出力する。到達性は直接呼び出しだけでなく、選択した Provider、initializer／destructor、cleanup、runtime helper、entry/context、metadata などの生成依存を閉包まで辿る。出力を省いた製品計画の予算を再配分しない。

[Composition Root 仕様](../Decisions/2026-09-13%20Composition%20Root%20Review.md)に従い、テスト実行対象の接続を一つに固定する。製品と同じ接続の計画は上記の条件で再利用する。接続が異なる部分では Provider 非依存の意味計画を保ち、影響する製品側の生成計画をその接続で先に検証・確定してからテストの追加要求を処理する。別接続の生成物を無条件には再利用しない。

二度の全面コンパイルや別ファイルへの生成は要求しない。一つの最終 LLVM module に格納できる。テスト用入口・到達可能性や後段最適化は異なり得るため、最終バイナリ全体の byte 一致は要求しない。

## 7. native 接続と最終生成

### 7.1 定義側の要求

native の論理名は宣言元の Kotonoha に属し、大小文字を区別する Ordinal 文字列で比較する。プロジェクトの NativeRequirements と配布 manifest の nativeRequirements は、target から論理名への二段の map とする。

各要求は Kind（static／import）を必須とし、任意の ContractId、Sha256 を持つ。JSON では kind／contractId／sha256 と表記する。契約 ID は空でない識別文字列として完全一致で比較する。要求 hash は追加の受入条件であり、省略できる。異なる target の要求は別に照合する。

その処理で選択されたソースの非予約 `#LibraryImport` は、必ず同名の要求を持つ。欠落や呼出条件との不一致は診断する。native 内部の外部参照を満たす補助ライブラリの要求も明示できるため、ソースから直接 import されない要求を一律に誤りとはしない。供給設定だけから要求を増やさない。配布時には要求だけを残し、Input の host パスを含めない。

```text
NativeRequirements=
  x86_64-pc-windows-msvc=
    codec={ Kind="static" ContractId="example.codec.v1" }
```

### 7.2 target ごとの供給

供給は NativeLibraries に統一し、target ごとに Name／Kind／Input の配列を持つ。任意の Package={PackageId, PackageVersion} で供給先を指定し、省略時は記述した Project 自身とする。Name はそのモジュールの native 論理名であり、利用側の ReferenceName ではない。ContractId と Sha256 も指定できる。

```text
NativeLibraries=
  x86_64-pc-windows-msvc=
    {
      Package={ PackageId="example.codec" PackageVersion="1.0.0" }
      Name="codec"
      Kind="static"
      ContractId="example.codec.v1"
      Input="native/codec.lib"
    }
```

既存の「論理名 → Kind／Input」map は Package 省略・Name=キーの短縮表記として同じ供給表へ展開する。旧設計の NativeBindings は採用せず、設定されていれば移行を案内する。非予約 import に NativeRequirements がなければ追加を案内し、Kind を供給側から推測しない。

相対 Input は記述した Project 基準とし、単純ファイル名の既存 linker 検索も使用前に実ファイルへ解決する。必要な ContractId は供給側にも明示し、欠落・不一致を拒否する。同じ供給先への複数指定は、実内容・Kind・契約が一致する場合だけ統合する。未使用の供給候補の存在は要求や生成を増やさない。

native ファイルを実際に使う段階で解決し、実内容の SHA-256 を入力記録へ保存する。指定された要求 hash と供給側 hash は両方照合する。同じ内容の検証済みコピーが管理ストアにあれば再利用し、なければ不変の staging へコピーする。元ファイルとの hard link は作らない。外部 linker はこの内容を使い、元パスを読み直さない。Input の移動だけでは意味を変えず、同じパスでも内容が変われば link 入力を更新する。

Kind・契約・宣言した呼出条件は関連する検証・生成計画の入力に、実ファイル内容は少なくとも link 入力に結び付ける。pack や意味検証だけの段階は供給の要求を検査でき、native link の成功を主張しない。

### 7.3 共通の接続・実行規則

実 symbol の共有には、function／data 区分、物理型、calling convention、属性、provider の一致を要求する。異なるモジュールの同名 native 論理名を、名前だけで統合しない。

link 前に必要な native 供給を内容 hash でまとめ、archive 内の全 member の外部定義・参照・import 情報を調べる。生成した object と予約供給も対象に含め、各 import が指定した供給に存在することと、同じ実 symbol の解決が一意であることを確認する。必要な補助供給も閉包へ加え、暗黙の未検査ライブラリを linker に追加させない。

異なる供給の定義衝突は linker の archive 選択順に任せず診断する。import の公開 symbol と `__imp_` も対象とする。ただし単なる symbol 名の重複と、COFF の正当な統合を混同しない。weak／COMDAT／import 補助 record は、対応する形式規則に従って解決先・再配置を含む同一供給または等価な定義と確認できる場合だけ統合する。「weak だから」「同じサイズだから」という理由で許可しない。未対応で一意性を判定できない場合は、その理由を診断する。供給内容の共有で呼び出し側の ABI・unsafe 契約を省略しない。

Core／backend／kernel32 の予約供給は compiler がグラフ全体で一度だけ行う。任意の package で置換しない。既存の明示 backend パス指定も採用済み内容との一致を要求し、kernel32 の独自設定は禁止する。契約 ID や hash は、初期化・FP・unwind・所有権などの供給契約を満たす証明にはならない。import library の固定は実行時 DLL・OS 全体の固定を意味しない。

各 Kotonoha の検証済み意味計画は、共通の最終生成へ渡せる。public 宣言を native export と解釈せず、Library の main を自動実行しない。トップレベル実行文の禁止、static の初回アクセス初期化・循環検出・実際の初期化順に基づく shutdown は既存規則を維持する。依存の読込順を実行順にしない。

最終ビルド記録はモジュール入力、実際の native 供給、toolchain、IR、link 結果を結び付ける。意味キャッシュと `.link.json` は別の役割であり、link manifest に必須情報を追加する実装では schema を改訂する。

## 8. 実装・性能・検証

### 8.1 再利用の実装方針

- 入力の byte 列を一度取得し、実内容の hash を stream で計算する。hash 用の巨大な連結 buffer は作らない。
- 同一依存の宣言・文字列・Type・Origin・検証結果は一組で持ち、辺は範囲検査付きの整数 ID で参照する。経路一覧を各ノードへ複製せず、診断時に復元する。
- 不変の字句情報は言語版などの条件が一致する場合に共有する。定義環境・条件選択に依存する Koto／Binding を別モジュールへ使い回さない。
- 検証済み自前キャッシュの body は必要時に展開できる。ただし未使用定義も含む必須検証の完了と依存の有効性を先に確認する。
- 逆依存表と処理待ちリストで §5.3 の再検証を進める。モジュールの深さを host stack の再帰に依存させず、ノード数・作業量にも有限の上限を置く。
- 意味キャッシュが有効で完全性検証済みの管理入力なら、元ソースの展開を省ける。必要な本文・表だけを読み、同じ native 内容のコピー・symbol 検査も共有する。
- 複数環境を検証する場合は、同じ snapshot の条件選択前の字句情報を共有する。環境ごとの束縛・検証状態は分離し、同時実行数と合計メモリに上限を設ける。

キャッシュなしでも同じ意味を検証できなければならない。細粒度再利用、読み込みの遅延、並列処理は段階的に実装し、処理順やキャッシュの有無で受理・実装選択を変えない。

管理外の可変ファイルでは、fileId・サイズ・更新時刻の一致を内容一致の証拠にしない。これらは読込順の最適化のヒントに限り、pack だけでなく build でも実内容を固定する。不変性を保証する管理ストアの再利用とは区別する。

### 8.2 コード量と実行性能

[generic 共有設計](2026-09-13%20Generic%20Sharing%20and%20Specialization.md)の共通生成を使い、静的に確定した呼び出しを直接化する。同じ生成キーの計画を重複排除し、定数伝播、不要な機械語・metadata の省略を行う。未使用宣言の必須意味検証は省略しない。

外部ソースから生成する body も、その生成要求が属する製品／テストの予算へ含める。別途リンクする既存 native コードの量は含めない。全型への特殊化や一律の強制 inline は要求しない。

意味計画を再利用した後の LLVM 処理・link が支配的かを測ってから、機械語キャッシュを検討する。初期版に別 object 間の安定接続契約を追加しない。

### 8.3 確認項目

| 対象 | 確認する例 |
| --- | --- |
| 解決・identity | 同一依存への複数経路・別名、同版の別内容、複数版、循環、推移名の非公開 |
| lock | 編集・移動で不変、依存変更、空の区分、テストだけの解決失敗、両区分の再照合、同時 restore |
| pack | 同版の再発行拒否、子更新に伴う親の版更新、継承設定の実効値、既存 Package を含む閉包、全環境検証の失敗 |
| 保存形式 | Unicode 衝突、大小文字が異なる版、manifest と実内容の不一致、同時公開・中断回復、回収と pin の競合 |
| 証明 | 版更新時の対応と型の分離、効果が同じ／異なる private 編集、不在依存の変化、Proven 撤回、再帰群、位置だけの変更 |
| テスト | 無関係な追加、新しい generic 代入、同版の競合、Provider 変更、cleanup を含む出力閉包。製品の共有・frame・予算を維持 |
| native | target 別供給、同パスの変更、同内容の移動、kind／契約不一致、archive 内衝突、weak／COMDAT／import 補助 record |
| 性能 | 多数経路からの共有、巨大 library の一部使用、生成倍率だけの変更、外部の小関数・generic の反復使用 |

所要時間は取得・字句解析・意味検証・生成・link に分ける。読込 bytes、hash 回数、再検証件数、body 展開数、allocation、最大メモリ、コード量、実行時間を測る。改善効果と上限値は未測定であり、数値保証は置かない。

### 8.4 後続の設計境界

ソース非公開バイナリにも、完全な型・Origin／Loan・公開保証・適合・specialization と、対応するコード／ABI 情報が必要である。generic の生成に必要な private な意味計画を含む場合があり、ソース非公開は実装の秘密性の保証ではない。

導入前に、外部の検証結果を信頼する条件、コードとの対応、再検査可能な証拠、再構築不能な依存更新の扱いを決める。独立した証明書や署名基盤は今回導入しない。

実装は、入力と解決 → モジュール境界と意味検証 → 配布・最終生成 → 永続意味キャッシュの順に進める。本書の採用と実装完了は区別し、SPEC 本体への統合は別の作業とする。
