# Kimigayo の依存設定・配布・検証済み成果物

2026-09-13 改訂、2026-09-15 本文同期。依存・成果物の規則と追加案 M1–M8、S1–S4 の採否は [SPEC 第18章](../../spec/18-modules-and-dependencies.md)、第20・21章と付録へ反映済み。現行の規範は SPEC 本文とし、本書は設計の説明を保持する。取り下げられた Composition Root の Entry／Provider 選択は本書から導入しない。

これは設計仕様であり、設定・コマンド・コード例は実装済みであることを示さない。実装状況は [STATUS](../../STATUS.md)、変更しない言語規則は [SPEC](../../SPEC.md) に従う。

## 1. 基本方針

1. **依存先の意味を定義側で確定する。** 名前、アクセス、型、generic、所有権の境界を配布方法によって変えない。
2. **入力を固定してから検証する。** 検証、生成、pack は同じ不変の入力を使う。
3. **解決・検証・生成の記録を分ける。** 版やファイル名だけで互換性を判断しない。
4. **製品を確定してからテストを追加する。** この境界を依存解決から生成予算まで維持する。

| 初期対応 | 後続の機能 |
| --- | --- |
| Project 参照、ソースパッケージ、ローカル発行ストア、完全一致の版、複数版の併存 | ネットワークレジストリ、版範囲の自動解決 |
| ソースからの検証・最終生成、意味計画の任意キャッシュ | 機械語キャッシュ、ソース非公開バイナリ |
| 既存の native 接続契約 | 安定した外部 Kimigayo ABI、DLL・動的ロード、re-export |

一つの配布パッケージは一つの Library Kotonoha を持つ。現在の検査用 Library `.ll` は配布形式にしない。必要な型・Loan・効果・cleanup・生成経路が未対応なら診断する。

既知の制約として、発行済み Package の Project override はない。子の配布内容を変えて同じストアへ再発行する場合は、依存辺が変わる発行済みの祖先も版の更新が必要になる。試作の pack にはこの制限を課さない。

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
LangVersion="0.0.1"
Targets=
  "x86_64-pc-windows-msvc"
Dependencies=
  Math={
    PackageId="example.math"
    PackageVersion="1.2.0"
    Project="../Math/Math.kimiproj"
  }
```

Dependencies は ReferenceName をキーとする map。ID／Version は必須とし、Project（`.kimiproj`）と Package（`.kimipkg`）は併記しない。どちらも省略した参照は PackageSources から Package を解決する。相対パスは記述したプロジェクト基準とする。

PackageSources は取得元の配列であり、各要素は ID／Version／Package、または Store（ディレクトリ）のどちらかを持つ。SourceId が未固定なら、明示 Package と発行ストアの対応表を候補として完全一致の ID／Version を解決する。候補の SourceId が一意でなければ取得元を示して拒否し、先勝ちにしない。対応表は取得の索引であり、採用時に manifest の ID／Version と内容を照合する。

manifest に SourceId がある参照と、build 等が有効な lock を読む場合は、その内容だけを候補とし、`<Store>/<SourceId>.kimipkg` を直接取得する。対応表が違う内容を示しても固定を更新せず、その取得元の不整合として診断する。対応表を持たない pack 出力も、SourceId 指定時の取得元には使える。利用者単位の管理キャッシュは版の選択候補にせず、決まった SourceId の取得を高速化するだけとする。

直接依存の Package 指定と、到達した Project の取得元も候補に加える。必要区分の Project 設定を先に集め、読込順で選択を変えない。テスト側の候補を製品の解決に使わず、候補登録だけでは名前で参照できない。総当たり検索・版の自動選択はせず、要求された内容がなければ診断する。

```text
PackageSources=
  { Store="packages" }
Dependencies=
  Math={ PackageId="example.math" PackageVersion="1.2.0" }
```

未実装の旧 KotonohaArray は Dependencies に置き換える。非空の旧設定には移行診断を出し、取得元や参照名を推測しない。

### 2.4 実効環境と使用例

target と debug/release はグラフ全体で統一し、target は各依存の対応対象でもなければならない。CompileTimeSettings と default alias は定義側のものを使う。依存辺での設定上書き、暗黙の feature 統合、条件付き依存は初期版に導入しない。別構成を別モジュールとして提供する場合は別 PackageId を使う。

通常の build では、未指定の LangVersion はルートと同じ solution／compiler の既定値で解決し、別 solution を暗黙探索しない。pack する各 Project には LangVersion と Targets を明示する。配布する他の意味設定も外側から継承せず、自身の明示値またはその言語版・成果物形式で固定した既定値だけを使う。省略された CompileTimeSettings は空の map とする。周囲や compiler build によって変わる既定値が必要なら、明示を要求する。

配布 manifest には確定した設定を保存する。初期グラフは一つの実効言語版と現在の compiler build で検証する。発行者と同じ compiler build は要求しない。

各内容 ID は、その対象に含まれる設定の確定した実効値を使う。省略と明示が同じ値なら区別しない。利用側の target／mode／compiler は SourceId に加えず、ModuleInputId に記録する。版の不変性を要求する範囲は発行先ストアとする（§4.4）。

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
| `pack <project>` | lock を基に配布グラフを作り、検証して内容を保存。版の対応表は更新しない |
| `publish <package> --store <directory>` | 作成済みパッケージの閉包をローカルストアへ発行し、版の対応表を更新 |
| `store verify` | 利用者単位の管理キャッシュの全内容と保存形式を再検査 |
| run | 既存成果物を実行。依存解決・再構築はしない |

restore は宣言されたローカル取得元を読み、ネットワーク・Mod・対象ソースの実行は行わない。製品の解決失敗は非ゼロ終了とする。製品が解決できれば、テストだけの失敗は理由を表示して記録し、製品の restore は成功させる。設定全体を解釈できないエラーは区分へ隔離せず失敗する。

restore は Project に宣言された Package 参照を現在の取得元から再解決する。既存 lock は更新前の状態であり、試作の Package パスを変更して restore すれば同版の別内容へ固定し直せる。ただし取り込む Package の manifest が固定した子の SourceId は変更しない。これ以外のコマンドは lock を固定したまま使う。

Project を入力とするコマンドは、lock があれば必要区分を必ず照合する。依存宣言を空にしても、古い非空の lock があれば restore を要求する。lock がない場合に限り、必要区分の依存宣言が空なら空の解決結果として扱う。製品コマンドは TestDependencies の有無に左右されず、test は両区分を必要とする。既存 lock の破損は空扱いしない。publish は確定済み Package の閉包を読み、Project の lock は使わない。

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

管理キャッシュの回収では、最新ポインター、保存指定した記録・lock、実行・検証中の処理を起点に参照先を残す。処理は利用前に入力・成果物を pin し、回収と競合させない。参照されない記録と内容は回収できる。hash だけでは消えた入力を再現できず、再現を要求する記録は入力の閉包も保存する。pack 出力と発行ストアは管理キャッシュの自動回収対象にせず、発行済み版の対応表も回収しない（§4.4）。

## 4. ソース配布

### 4.1 pack の確定順序

pack は Library を対象とし、次の順に処理する。

1. 製品区分の lock と配布設定の明示を照合し、ルートと必要な Project の現在の入力を固定する。設定不足は各ノードについてまとめて報告する。
2. Project 依存を末端から配布用の論理入力へ変換し、最終 SourceId を計算する。既存 Package は内容を照合して再利用する。
3. 親の依存辺を、子の PackageId／Version／SourceId へ固定し、今回のグラフ内の一意性を確認する。
4. 通常の package loader と意味検証器で、最終グラフを検証する。
5. 既存 Package も含む依存の閉包を出力ディレクトリへ保存する。子を先に、親を最後に配置し、各 ID と保存先を報告する。過去の pack と同版でも別の SourceId として保存できる。

変換ではソースの論理パス・順序、製品所属、実効 LangVersion、CompileTimeSettings、default alias、native 要求を保存し、取得パスを取り除く。環境で選択した枝だけを残したソースに書き換えず、元の条件付きソースを配布する。ProjectSnapshotId や ID／Version だけを根拠に、別の pack 済み成果物を選ばない。既存 Package を Project で置換する一般機能にはしない。

```text
Geometry -> Project Math
  1. Math の現在の配布入力を確定       -> SourceId = h_math
  2. Geometry の Math 参照を h_math に固定 -> SourceId = h_geometry
  3. 確定したグラフを検証
  4. h_math.kimipkg、h_geometry.kimipkg の順に保存
```

上の h_math／h_geometry は実際には64桁の内容 ID である。出力ディレクトリにはソースパッケージの閉包を揃え、同じ SourceId は一つにまとめる。native ファイル・実行時 DLL・toolchain はこの閉包に含めない。親の配置時点で、その参照する子が同じディレクトリから取得可能でなければならない。

検証には archive 書き出し前の論理 manifest／file view を使える。検証済みの同じ byte 列を writer へ渡し、ZIP の作成・再展開を挟まない。通常は選択した target／mode を意味検証する。単一環境の選択は build と共通とし、`--Target` を優先、未指定で Targets が一つならそれを使い、複数なら指定を要求する。mode は release が既定で、`--Debug` 指定時は debug とする。

`pack --verify-all` は宣言した target × {debug, release} のすべてを検証し、`--Target`／`--Debug` と併用しない。未対応環境や一つでも検証失敗があれば出力を確定しない。コード生成・native link・実行は要求しない。

manifest の targets は利用を許す対象であり、検証済み一覧ではない。実際に成功した組合せは SourceId・compiler build・実効設定とともに別の検証記録へ保存し、未検証を成功と表示しない。利用側は自分の環境で必ず検証する。

```powershell
# 配布設定を各 Project に明示した後、試作品と依存一式を作る。
kimi pack Geometry.kimiproj --output trial --verify-all
# 内容を編集しても、試作中は同じ版で繰り返せる。
kimi pack Geometry.kimiproj --output trial --verify-all
# 発行時は、pack が報告したルートのパスを指定する。
kimi publish "trial/<SourceId>.kimipkg" --store packages
```

`<SourceId>` は pack が報告した64桁の ID に置き換える。Project 依存の Math 側にも LangVersion／Targets を明示する。

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

外部入力を管理キャッシュへ登録する際は、全 entry の形式・長さ・実 hash を検証し、同じ snapshot をコピーする。完全性検証と意味検証の記録は別にする。キャッシュは利用者単位とし、Windows の既定位置は `%LOCALAPPDATA%/Kimigayo/Cache/v1`。内容は SourceId 等の内容 ID で保存し、版の対応表を持たない。

再検査を省く前提は「登録後の内容を compiler 以外が書き換えない」ことであり、変更を自動検出できる保証ではない。読み込み時の構造不正は破損として扱い、`store verify` は全内容の形式・実 hash と参照を再検査する。破損は隔離し、依存するキャッシュ結果を失効させ、元入力の再取得・再検証を要求する。外部の pack 出力や発行ストアはこの信頼範囲に含めず、read-only 属性・manifest hash だけで登録時の検証を省かない。

archive writer は形式版ごとに、entry の UTF-8 path 順、固定時刻（1980-01-01 00:00:00）、属性・extra field、圧縮方式・設定・実装を固定し、同じ入力から同じ byte 列を作る。host 固有の属性や時刻を入れず、形式版と archive 全体の SHA-256 は SourceId と分けて記録する。

既存の外部 archive は、検証済みコピーまたは今回の writer 出力から得た信頼できる全体 hash と、実 byte 列の順次 hash が一致すれば展開を省ける。形式版や外部の hash 申告だけでは省略しない。不一致なら entry ごとの完全性検査へ進み、同じ SourceId の合法な別圧縮表現は再利用できる。比較に使った snapshot を後続にも使う。

### 4.4 発行とストア

| 状態 | 更新する操作 | 範囲 |
| --- | --- | --- |
| 依存 lock | restore | プロジェクトの解決結果 |
| 発行済み ID／Version → SourceId | publish | 指定した発行先ストア |
| 内容ファイル | pack／取得・登録／publish | 内容 ID ごとの不変データ |

pack と登録は版を予約しない。同版の別内容が一つの利用グラフへ入る場合だけ §2.2 と lock で拒否し、無関係なプロジェクトの過去の内容には拘束されない。ProjectSnapshotId を SourceId とみなしたり Package と暗黙統合したりしない。

pack の既定の出力先は `bin/packages/<SourceId>.kimipkg`。`--output <directory>` は出力ディレクトリを指定する。ルートも子も内容 ID 名で保存する。一時ファイルを完成・検証した後、上書きしない atomic rename で配置し、既存・競合ファイルは §4.3 で内容を確認する。

publish は指定したルート Package と、同じ入力ディレクトリ内の SourceId 名の依存閉包を読み、完全性・manifest・グラフを検査する。現在の Project を再 pack せず、確定済み内容だけを発行する。意味検証の実績や native link 成功を新しく主張する操作ではない。

発行先の `releases.json` は schemaVersion=1 と entries（packageId／packageVersion／sourceId の配列）を持つ。同じ ID／Version の重複を拒否し、順序は ID、Version の UTF-8 順に固定する。一つの発行先では既存の対応を変更せず、同じ対応の再発行は成功扱いとする。別の発行先との対応を世界共通にはしない。

発行前に閉包全体を照合し、同版の別内容がある全ノード・旧新 SourceId・依存経路を一度に報告する。版や Project 設定を自動編集しない。子の更新で内容が変わる発行済みの祖先も対象に含む。衝突があれば対応表を一件も更新しない。

内容ファイルを先に揃え、発行先の排他下で対応を再照合し、対応表を一括で atomic replace する。読者は表の一つの snapshot を使う。中断しても表が未完成の閉包を指さないようにし、配置済みの正しい内容を巻き戻す必要はない。表が壊れている場合は空のストアとして初期化せず診断する。発行済みの対応は内容の回収後も保持する。初期 publish は明示したローカルディレクトリだけを対象とし、ネットワークへ送信しない。

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

対応したノード内の DeclarationKey は Container、宣言種別、正規化した宣言識別情報から作り、版・内容 hash・ソース位置を含めない。これは再利用候補を探すキーであり、実際の型・symbol は引き続き定義元の版を含む identity を持つ。完全な編集追跡は要求しない。Composition Entry の identity は本機構から定義しない。

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

意味・生成処理の初期永続キャッシュは、compiler 専用の検証済み意味計画までとする。同じ形式版・compiler build・検証規則で使い、ABI、context、frame、予算選択、IR、機械語は現在の方式で作り直す。生成だけに影響する設定変更で意味計画を失効させない。内容の完全性記録や native の入力要約（§7.4）は生成結果ではなく、これと別に保存できる。

外部パッケージに同梱された「検証済み」の申告は採用しない。hash は内容の一致を示すもので、証明の正しさや発行者を証明しない。Koto の object graph、host pointer、runtime context graph を保存形式にしない。

古い・壊れた自前キャッシュは破棄して元入力から再検証する。必要な情報がなければ再構築要求を出す。配布パッケージ自体の破損を隠して古い入力を使い続けてはならない。

### 5.5 ソース位置

SourceId／ProjectSnapshotId は原文の変更を記録する。細粒度の比較から除けるのは、その判断を使う compiler・Mod・生成物のいずれからも観測されない情報だけとする。製品の意味だけを比較する場合は token 列・インデント構造を使えるが、literal、意味に関係する改行・所属・ソース順は残し、§5.2 の束縛・環境・依存も照合する。

Mod がコメントや位置を読める場合は、そのモジュールの Mod 入力を原文 byte 列で照合し、変更時は再実行する。観測する範囲を完全に記録できるまでは、Mod の結果を token の一致だけで再利用しない。再生成後の製品の意味が同じなら、通常の内容依存規則で後続の再利用を判断できる。

意味計画内の位置は、論理ファイルと宣言に属する token の参照で持つ。現在の原文への対応表から行・桁を得て、テストの token が増えても製品の参照をずらさない。必要な字句・構文検査は現在の入力に対して行い、対応が確定できなければ位置情報を作り直す。Abort の位置や Testing の SiteId テーブルに載せる式の文字列は、生成段階で現在の原文へ結び付ける。これらが変われば該当生成物・診断テーブルとその成果物 ID を更新し、古い表示を流用しない。

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

製品計画を確定した後、テスト実行入口から必要なコードだけを出力する。到達性は直接呼び出しだけでなく、選択した実装、initializer／destructor、cleanup、runtime helper、entry/context、metadata などの生成依存を閉包まで辿る。出力を省いた製品計画の予算を再配分しない。

テスト実行対象の入力・設定を固定し、製品計画は上記の適合条件で再利用する。入力・設定が異なる部分では影響する製品側の生成計画を先に検証・確定してからテストの追加要求を処理する。異なる入力の生成物を無条件には再利用しない。Entry／Provider による Composition Root の接続は未確定であり、本書の生成規則には含めない。

二度の全面コンパイルや別ファイルへの生成は要求しない。一つの最終 LLVM module に格納できる。テスト用入口・到達可能性や後段最適化は異なり得るため、最終バイナリ全体の byte 一致は要求しない。

## 7. native 接続と最終生成

### 7.1 定義側の要求

native の論理名は宣言元の Kotonoha に属し、大小文字を区別する Ordinal 文字列で比較する。プロジェクトの NativeRequirements と配布 manifest の nativeRequirements は、target から論理名への二段の map とする。

各要求は Kind（static／import）を必須とし、任意の ContractId、Sha256 を持つ。JSON では kind／contractId／sha256 と表記する。契約 ID は空でない識別文字列として完全一致で比較する。要求 hash は追加の受入条件であり、省略できる。異なる target の要求は別に照合する。

その処理で選択されたソースの非予約 `#LibraryImport` は、必ず同名の要求を持つ。要求は NativeRequirements に明示するか、自身向けの NativeLibraries にまとめて記述できる。後者の Kind／ContractId／Sha256 は、同じ定義者の要求と供給の両方へ展開する。両方に記述した項目は一致を要求し、欠落した Kind や呼出条件との不一致は診断する。他モジュール向け供給から定義側の要求を補わない。

native 内部の外部参照を満たす補助ライブラリの要求も明示できるため、直接 import されない要求を一律に誤りとはしない。配布時には確定した要求だけを残し、Input の host パスを含めない。

```text
NativeRequirements=
  x86_64-pc-windows-msvc=
    codec={ Kind="static" ContractId="example.codec.v1" }
```

```text
// 自分の Application で使う場合は、要求と供給を一度に記述できる。
NativeLibraries=
  x86_64-pc-windows-msvc=
    observer={ Kind="static" Input="native/observer.lib" }
```

### 7.2 target ごとの供給

供給は NativeLibraries に統一し、target ごとに Name／Input の配列を持つ。任意の Package={PackageId, PackageVersion} で供給先を指定し、省略時は記述した Project 自身とする。Name はそのモジュールの native 論理名であり、利用側の ReferenceName ではない。ContractId と Sha256 も指定できる。Kind は定義側の要求なので、自身向けの統合記述だけに認め、他モジュール向けの供給には書かない。

```text
NativeLibraries=
  x86_64-pc-windows-msvc=
    {
      Package={ PackageId="example.codec" PackageVersion="1.0.0" }
      Name="codec"
      ContractId="example.codec.v1"
      Input="native/codec.lib"
    }
```

既存の「論理名 → Kind／Input」map は Package 省略・Name=キーの短縮表記として、同じ要求・供給表へ展開する。旧設計の NativeBindings は採用せず、設定されていれば移行を案内する。

相対 Input は記述した Project 基準とし、単純ファイル名の既存 linker 検索も使用前に実ファイルへ解決する。ContractId は供給者の実装契約の申告であり、要求されていれば欠落・不一致を拒否する。別モジュールの要求を供給側の申告で書き換えない。同じ供給先への複数指定は、実内容・契約が一致する場合だけ統合する。単なる他モジュール向け供給候補の存在は要求や生成を増やさない。

Kind の実照合は native を使う段階で、接続する symbol の static 定義／import 経路を解析して行う。short import header だけでなく long import object と補助 record を区別する。archive 全体を一つの Kind に推測分類せず、異種 member の共存だけでは拒否しない。必要な接続が定義側の Kind と合わない場合や、形式未対応で判定できない場合は診断する。意味検証・pack は実ファイルに依存せず定義側の要求で行える。

native ファイルを実際に使う段階で解決し、実内容の SHA-256 を入力記録へ保存する。指定された要求 hash と供給側 hash は両方照合する。同じ内容の検証済みコピーが管理ストアにあれば再利用し、なければ不変の staging へコピーする。元ファイルとの hard link は作らない。外部 linker はこの内容を使い、元パスを読み直さない。Input の移動だけでは意味を変えず、同じパスでも内容が変われば link 入力を更新する。

Kind・契約・宣言した呼出条件は関連する検証・生成計画の入力に、実ファイル内容は少なくとも link 入力に結び付ける。pack や意味検証だけの段階は供給の要求を検査でき、native link の成功を主張しない。

### 7.3 共通の接続・実行規則

実 symbol の共有には、function／data 区分、物理型、calling convention、属性、provider の一致を要求する。異なるモジュールの同名 native 論理名を、名前だけで統合しない。

link 前に必要な供給を内容 hash でまとめ、全 member の定義・参照・import・再配置・directive を索引化する。取り込みは生成した object、entry、予約供給の必須入力から始め、未定義参照と有効な directive で必要になる member を固定点まで辿る。取り込まれない member 同士の未使用 symbol の重複だけでは拒否しない。

必要になった symbol は索引全体から候補を確認し、異なる供給に非等価な候補があれば先勝ちにせず診断する。取り込んだ member が持つ他の外部定義も、既に取り込んだ定義と照合する。各 Kimigayo import が指定した供給に結び付くことを確認し、生成 object と予約供給を除外しない。linker profile の選択規則を再現できない場合は、推測せず未対応として診断する。

import の公開 symbol と `__imp_` も同じ規則で扱う。weak／COMDAT／import 補助 record は、形式規則に従って解決先・再配置を含む同一供給または等価な定義と確認できる場合だけ統合する。「weak だから」「同じサイズだから」という理由では許可しない。供給内容の共有で呼び出し側の ABI・unsafe 契約を省略しない。

`.drectve` は、生成 object と取り込んだ member でだけ有効にし、採用した linker profile の規則で解釈する。

| 指定 | 初期の扱い |
| --- | --- |
| `/INCLUDE` | 指定 symbol を必須参照へ加える |
| `/ALTERNATENAME` | weak／別名の解決として扱い、候補・循環・競合を検査する |
| `/FAILIFMISMATCH` | 同じキーの要求値を照合し、不一致を拒否する |
| `/DEFAULTLIB` | profile の `/NODEFAULTLIB` で無効なら取り込みを増やさない。有効な暗黙取得は初期未対応として、明示供給への移行を案内する |
| `/EXPORT` | 初期の export 範囲外として診断する |
| その他 | profile で意味を定義したものだけ受理し、未対応の指定・符号化は理由付きで拒否する |

最終 linker の入力・option も同じ profile と閉包に固定する。対応外の option や暗黙ライブラリで検査を迂回させない。

Core／backend／kernel32 の予約供給は compiler がグラフ全体で一度だけ行う。任意の package で置換しない。既存の明示 backend パス指定も採用済み内容との一致を要求し、kernel32 の独自設定は禁止する。契約 ID や hash は、初期化・FP・unwind・所有権などの供給契約を満たす証明にはならない。import library の固定は実行時 DLL・OS 全体の固定を意味しない。

各 Kotonoha の検証済み意味計画は、共通の最終生成へ渡せる。public 宣言を native export と解釈せず、Library の main を自動実行しない。トップレベル実行文の禁止、static の初回アクセス初期化・循環検出・実際の初期化順に基づく shutdown は既存規則を維持する。依存の読込順を実行順にしない。

最終ビルド記録はモジュール入力、実際の native 供給、toolchain、IR、link 結果を結び付ける。意味キャッシュと `.link.json` は別の役割であり、link manifest に必須情報を追加する実装では schema を改訂する。

### 7.4 native 入力要約

member 単位の定義・参照・import・weak／COMDAT・再配置・`.drectve` と索引を、native ファイルの内容 hash、要約形式版、parser／解釈規則版、対象 COFF profile をキーに管理キャッシュへ保存できる。未使用 member も索引化するが、解決結果や「衝突なし」の結論は別グラフへ流用しない。

再利用時も現在の root object・供給集合・呼出契約・linker option で閉包と競合を計算する。内容変更・形式不一致・破損時は再解析し、不明な情報を空の定義・参照集合として扱わない。供給ごとの索引と整数 ID を共有し、各 link で全 archive を解析し直さない。

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
| pack／publish | 同版の試作反復、発行先ごとの競合、閉包全体の一括診断、配布設定の明示、全環境検証の失敗、対応表の同時更新 |
| 保存形式 | Unicode 衝突、同内容の異なる圧縮、archive hash の高速確認と entry 検査への切替、破損・store verify、回収と pin の競合 |
| 証明 | 版更新時の対応と型の分離、効果が同じ／異なる private 編集、不在依存の変化、Proven 撤回、再帰群、位置だけの変更 |
| テスト | 無関係な追加、新しい generic 代入、同版の競合、実装依存の変更、cleanup を含む出力閉包。製品の共有・frame・予算を維持 |
| native | 自身向け統合記述、short／long import、混在 archive、未使用 member の重複と必要 symbol の曖昧性、directive の閉包、要約の失効 |
| 性能 | 多数経路からの共有、巨大 library の一部使用、生成倍率だけの変更、外部の小関数・generic の反復使用 |

所要時間は取得・字句解析・意味検証・生成・link に分ける。読込 bytes、hash 回数、再検証件数、body 展開数、allocation、最大メモリ、コード量、実行時間を測る。改善効果と上限値は未測定であり、数値保証は置かない。

### 8.4 後続の設計境界

ソース非公開バイナリにも、完全な型・Origin／Loan・公開保証・適合・specialization と、対応するコード／ABI 情報が必要である。generic の生成に必要な private な意味計画を含む場合があり、ソース非公開は実装の秘密性の保証ではない。

導入前に、外部の検証結果を信頼する条件、コードとの対応、再検査可能な証拠、再構築不能な依存更新の扱いを決める。独立した証明書や署名基盤は今回導入しない。

実装は、入力と解決 → モジュール境界と意味検証 → 配布・最終生成 → 永続意味キャッシュの順に進める。本書の採用と実装完了は区別し、SPEC 本体への統合は別の作業とする。

## 9. 今回の修正案の採否

規則と例は上記の各節へ統合した。以下は原案からの変更理由だけを記録する。

| 案 | 判定 | 反映先・理由 |
| --- | --- | --- |
| M1 pack と発行の分離 | 採用 | §3.1、§4.4。版の対応表は publish だけが発行先ごとに更新し、試作・共有キャッシュは版を予約しない |
| M2 配布設定の明示・版更新の一括診断 | 修正採用 | §1、§2.4、§4.4。外側からの既定値継承は禁止。固定した言語・形式の既定値は許す。衝突一覧は発行先が決まる publish で報告し、pack には版更新を要求しない |
| M3 ID／Version による取得 | 採用 | §2.3。発行ストアの対応表も候補に使い、manifest と照合する。複数候補は一意性を確認し、lock に固定した内容を先勝ちで更新しない |
| M4 native 設定の重複削減 | 修正採用 | §7.1–7.2。自身向け記述を要求・供給へ展開し、他モジュール向け供給には Kind を書かない。ヘッダーだけの種別推測と混在 archive の一律拒否は不採用 |
| M5 member の閉包と directive の検査 | 修正採用 | §7.3。必要な member の閉包で判定し、全 member は候補探索の索引に使う。未使用の重複は許すが、必要 symbol の曖昧な先勝ち選択は許さない。directive は対応範囲を明示 |
| M6 観測可能な原文の保持 | 採用 | §5.5。Mod 入力は保守的に原文を比較し、表示する式・位置は現在の生成入力へ結び付ける |
| M7 管理ストアの信頼と場所 | 採用 | §4.3。利用者単位の内容キャッシュとし、外部変更しない前提・store verify・破損時の失効を明記 |
| M8 lock と検証環境の既定 | 採用 | §3.1、§4.1。既存 lock は空の依存宣言でも照合。単一 target は自動選択、複数は指定必須、mode は release を既定とする |
| S1 決定的 archive と全体 hash | 採用 | §4.3。信頼できる完成済み byte 列の hash を使い、一致すれば展開を省略。不一致は entry 検査へ進む |
| S2 通知・USN による読み直し省略 | 保留 | 下記の正しさ検証が済むまでは導入しない。属性一致で内容確認を省く規則にも戻さない |
| S3 native symbol 要約の永続化 | 採用 | §7.4。内容・形式・解釈規則・対象ごとの入力要約を保存し、解決と競合は毎回現在の閉包で判断する |
| S4 製品の生成選択結果の永続化 | 保留 | 候補探索が支配的という測定がないため、初期の意味計画までの保存を維持する |

M4 の判定では、import library に short／long 形式と補助 record がある点を考慮した。archive 全体への二択の分類では必要な接続を十分に表せないため、symbol の実際の定義・import 経路を照合する。[Microsoft PE/COFF 仕様](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format#import-library-format)

S2 の cookie は通知の同期方法であり、すべての OS・ファイルシステムで変更通知の完全性を保証するものではない。[Watchman の説明](https://facebook.github.io/watchman/docs/cookies)にも適用条件と制限がある。導入前に監視開始時の全読込、通知順序、rename・別名経由の更新、開いた書込ハンドル、同期点と入力確定の競合を検証する。overflow・監視切断・cookie の失敗・journal 世代変更は全再読込へ戻す。[ファイル単位 USN](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_read_file_usn_data)も記録済み更新番号であり、単独で未変更の証拠にはしない。

S4 は測定で効果が見込めた場合に再検討する。基準計画だけでなく、候補集合・順序・見積り方式・予算・compiler・profile・実際に選択した実装と生成依存を照合する必要がある。保存する選択肢が小さくても、この検証ができなければ再利用せず再計画する。
