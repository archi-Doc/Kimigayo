# Composition Root 原案レビュー

2026-09-13。検討記録・提案であり、採用済みの言語仕様ではない。

対象は指定文書 `C:/Users/bwff1/OneDrive/Dev/Documents/Kimigayo/Design/Composition Root 仕様.md`、現在の SPEC、優先設計文書、コンパイラー実装。実装は HEAD `ff3a21f` に未コミット変更を含む作業ツリーを読んだ。既存の変更は編集していない。設計レビューのため、コンパイラー・実行・NativeAOT テスト、性能測定は行っていない。

ユーザーの指定を前提とし、`$abort`・`$expect`・`$require` など、言語が定義する root function はすべて置換不能な compiler intrinsic として検討した。原案の DefaultExpect / DefaultRequire はこの前提では採用しない。添付文書の記述は評価対象であり、仕様採用や実装変更の指示として扱っていない。

## 1. 判断

**中心設計は実現可能であり、採用を勧める。ただし、現在の原案をそのまま実装するには意味規則が不足している。**

有効なのは、API の宣言、Application による実装選択、利用側の Contract による検査を分離する点である。動的 DI container、runtime Contract View、reflection を導入せず、既存の静的 Contract と直接呼び出しを土台にできる。

最も重要な追加原則は次の一文になる。

> Composition は、Contract によって確定した操作を、検証済みの実装へ接続する。Provider の選択によって、利用側の名前解決・オーバーロード選択・公開された型と所有権契約をやり直さない。

推奨する構成は次のとおり。

| 要素 | 意味 | Application による差し替え |
| --- | --- | --- |
| `$abort`・`$expect`・`$require` 等 | 言語が意味・評価・制御フローを定める組み込み操作 | 不可 |
| `$Std` 等の Entry | 固定 Contract を公開するコンパイル時の接続点 | Provider の選択だけ可能 |
| Provider | その Contract に明示的に適合する具体型の型関数群 | `compose $` で選択 |
| Provider が利用する状態 | 通常の値・引数・静的保存域 | 通常の所有権・初期化・破棄規則 |
| Core の型 Identity、Copy / Owned、配置、Abort 保証 | 言語の基盤 | Composition で弱めない |

構文変更自体より、**Provider に依存しない意味解析、最終接続、別コンパイルの境界**の整備が大きな仕事になる。

## 2. 原案の矛盾と修正優先順位

| 優先度 | 箇所 | 問題 | 推奨修正 |
| --- | --- | --- | --- |
| 必須 | 原案 §2・3・5・6・14・15 | expect / require が通常 API・Default Binding に見える | root function 全体を sealed とし、Binding 表から除外する |
| 必須 | 原案 §14 | 通常の `func run` で `$expect`・`$require` を使用 | 優先テスト仕様に合わせ、`#Test` 内の例へ変更する |
| 必須 | 原案 §3 | `public Std: StdComposition` が通常 Property のように見える | 専用の Entry 宣言であり、値の型注釈・保存域ではないと規定する |
| 必須 | 原案 §4・9 | Provider の種類、適合宣言、receiver が未定義 | 初期設計では具体型と既存の明示的静的適合を使う |
| 必須 | 原案 §9 | 名前・Signature の確認だけでは所有権等を保証できない | 既存の適合照合、Origins、Constraints、access、Effects を再利用する |
| 必須 | 原案 §9・10 | Contract に実装依存 Self / 関連型を許すと Provider が API の型を変える | 公開する型と呼び出し条件を Provider 非依存に制限する |
| 必須 | 原案 §7・10 | Library が Provider を選ばない一方、全処理で Final Composition を Binding より前に要求 | 利用側の意味確定と実装への接続を分ける |
| 必須 | 原案 §3・7・8 | Kotonoha を跨ぐ Entry の Identity、宣言権限、可視性が未定義 | 所有モジュールと固定 Contract による Identity を導入する |
| 必須 | 既存 SPEC §22.1・22.4 | `Core.writeLine` と `$Std.writeLine` が別系統になる | 標準出力の公開入口と低水準実装の責務を決めてから移行する |
| 必須 | 原案 §5・11 | Default 不在、壊れた明示 Binding、未使用 Entry の扱いが未定義 | 選択規則・検証範囲を固定し、最適化で診断が変わらないようにする |
| 重要 | 原案全体 | Entry が値でないことから状態の管理方法までは決まらない | 状態と初期化を通常の仕組みに戻す |
| 重要 | 原案 §10・11 | 分離成果物の再接続方法と ABI 検証が未定義 | 初期は再生成できる source/意味情報、後に固定 ABI の接続を検討する |

以下で、矛盾の修正と追加設計の提案を分けて詳述する。

## 3. root function の扱い

既存の [テスト仕様 §3](../Design/2026-09-13%20Testing.md) は、すでに `$expect` と `$require` を差し替え可能な関数やマクロではないと規定している。単に両者を Default Binding の上書き禁止にするだけでは不十分である。

- `$expect` は条件を一度評価し、偽なら失敗を記録して継続する。
- `$require` は条件を一度評価し、偽なら失敗を記録して、その `#Test` 関数から通常 return する。退出時の cleanup を行う。
- 任意の `message:` は失敗時だけ評価し、条件の cleanup、メッセージ評価・cleanup、継続または return の順を維持する。
- `$abort` はメッセージ式の正常完了後に Abort し、残りの通常 cleanup を行わない。結果型は Never。

この違いは通常の eager 引数を持つ関数への接続だけでは表現できない。特に `$require` を helper 呼び出しにしても、呼び出し元のテスト関数からの return にはならない。

推奨規則は「compiler が root operation Identity ごとに構文、使用可能位置、型、評価手順、CFG と lowering を定義する」。同名のユーザー関数に組み込み性は付与しない。ユーザーの `group $` fragment による再定義・overload の追加、`compose $` による指定、関数値としての取得も認めない。将来の root function も個別に定義し、未知の `$foo` を自動的に組み込み扱いしない。

SDK の文書生成用に組み込みの宣言形を保持することはできるが、それは compiler 指定のカタログであり、ユーザーが実装する bodyless function 宣言ではない。

原案の基本例は、通常の出力とテストを分ける。

```kimi
group Example
    public func run()
        $Std.writeLine("Hello")

    #Test
    func verification()
        $expect(true)
        $require(true)
```

これは Composition 導入後の提案例であり、現在の compiler での実行対応を示さない。製品コード用の前提条件は既存の `require condition else ...` が担当し、今回 `$require` の意味を広げない。

## 4. Provider と Contract を既存の型体系へ接続する

### 4.1 Provider は具体型、Entry はそのインスタンスを持たない

現在の静的 Contract は具体型に対する適合であり、`Self is C` を明示する。同名メンバーがあるだけでは適合しない。group は型でも値でもなく、通常の適合対象にする規則はない。[SPEC §6.1・8.4](../../SPEC.md)

最小の一貫した拡張は、Provider を具体的な struct などとし、receiver のない型関数を既存の適合検証で選ぶことである。型名を指定してもインスタンスは生成しない。初期実装を nongeneric struct に絞り、後に閉じた generic 型などへ広げられる。

```kimi
public contract StdComposition
    func writeLine(text: string) -> ()

// SDK が所有する専用の Entry 宣言。
group $
    public Std: StdComposition

// 型として明示的に適合する、出力を捨てる検査用 Provider。
public struct DiscardStd
    Self is StdComposition
    public func writeLine(text: string) -> () => ()

compose $
    Std => DiscardStd
```

この例は構造的な適合と差し替えを示す。もし StdComposition の規範が「必ず OS の stdout へ出す」であれば DiscardStd はその動作規範を満たさない。テスト用 sink を許すなら「選択された標準出力先へ渡す」等の契約を明示し、Default Provider の stdout/LF 保証とは分ける必要がある。型検査だけでは I/O の動作法則を証明できない。

group Provider も実現はできるが、group への適合、Self の意味、access domain を追加設計することになる。インスタンス Provider は、構築、保存、共有・排他 receiver、借用、破棄、再入まで必要になる。どちらもこの機能の初期導入には不要である。

### 4.2 Composition 用に第二の適合規則を作らない

既存の [SPEC §8.4.5](../../SPEC.md) に従い、まず名前、関数種別、generic slot、パラメーター順序・外部ラベル、正規化した型で実装を識別し、その後に互換性を検証する。

入力 Origins の要求を強くしない、結果の寿命保証を弱くしない、safe な要求に unsafe な呼び出し文脈を要求しない、要求側の前提から実装の Constraints を証明する、必要な access を満たす、という規則を引き継ぐ。既存の正当な結果型互換性も使い、原案の「Signature 一致」を独自の文字列一致へ落とさない。

`string` と `ref/string` は違う取得契約である。`writeLine(text: string)` では既存 string local が Move する。借用版にするなら Contract 自体の改訂として扱い、Composition の接続時に都合のよい Borrow を追加しない。原案のラベル `message:` は現行 `Core.writeLine(text:)` と違うため、統合するなら意図的に揃える。

Contract の関数要件は既存仕様どおり default/optional 引数を持たない。Provider の default、追加 overload、追加 member は Entry 利用側へ漏らさない。

アクセスは三段階で見る。利用元が Entry/Contract を参照できること、Application が Provider を指定できること、Provider の適合が既存の公開範囲規則を満たすこと、である。Library のソース環境へ Application の Provider 名や private member を輸入しない。内部シンボルで接続することと、source の private access を迂回することを混同しない。

### 4.3 Self と関連型は最大の型体系上の落とし穴

次の Contract をそのまま Entry に使うと問題が現れる。

```kimi
contract FactoryComposition
    func create() -> Self
```

Provider が A なら結果が A、B なら B になる。結果の配置、Copy / Owned、破棄、overload、generic 推論まで変わり得る。裸の Entry は型ではないので、通常の `T is C` の T にそのまま代入することもできない。

Provider ごとの `associate Handle` も同様である。「Contract の API しか見せない」だけでは API の型は固定されない。実装を隠しながら実装依存の値を返すには、opaque 型、existential、Entry を基点にした抽象型などの追加機能が必要になる。

初期の Composition Contract には以下を勧める。

- receiver のない関数要件だけを公開する。Property 要件は既存では instance の意味を持つため初期対象にしない。
- 継承した要件も含め、公開パラメーター・結果・Origins・Constraints・呼び出し条件を Provider 非依存で確定できること。
- 実装依存の Self、未固定の関連型を公開契約に残さない。完全に固定された関連型を後で許す場合は、綴りではなく正規化後の依存で判定する。
- 通常の関数 generic は、この非依存性を満たせば言語設計上は扱える。初期 executable subset の制限とは区別する。

複雑な状態を返したい場合は、まず全 Provider が共有する公開型や、明示的な値・引数の API を使う。実装ごとに違うハンドル型を隠す機能は別設計にする。

Contract の多重継承にも注意が必要である。共通祖先の同じ Requirement Identity は既存どおり重複排除できる。一方、異なる要件の候補統合を「今回の Provider では同じ関数に接続されたから」で行うと、Provider が overload を変える。Provider 非依存に統合を証明できなければ別候補のまま扱う。

### 4.4 関数値の境界も明文化する

`let service = $Std` は Entry の値化なので禁止できる。しかし `$Std.writeLine` の関数参照まで禁止することは、Entry が値でないことからは導けない。通常も、型は値でなくても型関数を値として取得できる。

長期的には通常の Function Item 規則を利用する方が一貫する。その場合、論理 Identity を Entry・Operation・束縛済み引数で定め、Library 成果物にも未接続の関数参照を保持する。同じ Provider 関数へ接続されたという理由だけで別 Entry の Function Item を同一化しない。呼び出し可能性、unsafe、Origins、関数共通型への変換は既存規則で検査する。

初期実装で直接呼び出しだけに限定することは妥当である。その際は「関数参照は未対応」と明示診断し、暗黙に runtime container を作らない。特殊な評価・制御意味を持つ root function の値取得禁止とは別の理由である。

## 5. Root の所有、名前、Default を決める

### 5.1 `group $` は通常 group の全機能を引き継がない

`public Std: StdComposition` のコロンは「Entry が公開する Contract」を指定する。裸の Contract を runtime Property の型に使えるという意味ではない。

初期の `group $` は SourceDocument の宣言位置だけに置き、Entry 宣言とそれを選ぶ compile-time directive に絞ることを勧める。通常の Field、getter/setter、初期化式、実行文、root function の実装を許す必要はない。`compose $` も宣言であり、トップレベル実行項目や startup 候補には数えない。

原案の「通常の group と同様の宣言構造を利用できる」は広すぎる。「インデント付き宣言リストの構文を利用するが、許可する項目は専用規則で定める」へ変更する。

構文については `compose $` を維持する。宣言と選択の意味が異なるため独立した構文にする理由は成立している。`compose` はその宣言位置で `$` が続く場合だけの contextual keyword とし、通常の関数名・変数名としての使用を奪わない。`=>` の追加用途を SPEC の記法表と文法へ追記する。

### 5.2 「ひとつの Root」と「全ライブラリの名前を merge」は違う

通常の group fragment は Kotonoha を跨いで merge しない。同じ原則を捨てて、依存 Library が同名 Entry を共同で宣言できるようにすると、依存追加で API が変わる。[SPEC §6.1.2・18](../../SPEC.md)

推奨する規則は以下。

- Entry は宣言元 Kotonoha の Identity、宣言位置の論理パス、Entry 名に所属する。異なるバージョン・同じ綴りを自動的に同一視しない。
- `$Std` と root function は SDK/compiler が指定する canonical Identity を持ち、ユーザーが別宣言で横取りできない。
- 同じ所有モジュール内の fragment は異なる Entry を追加できる。同一 Entry の再宣言は同じ Contract を書いてもエラー。
- Application が所有するのは最終 Binding であり、依存 Library の Entry 宣言や Contract を変更する権限ではない。
- Library が独自 Entry を公開できる拡張では、所有元を示す限定参照を定義する。単純名の全依存スキャン、先勝ち、同名 merge を行わない。

初期実装では SDK 管理 Entry と Application 内 Entry に範囲を絞り、Library 独自 Entry の限定構文を後で決めてもよい。ただし、全依存の集約を完成させる仕様にはこの限定参照が必要であり、省略したまま完成扱いにはできない。

通常 Name lookup と独立させるのは `$` を起点とする参照である。Contract の名前と Binding 右辺の Provider 型は、それぞれの宣言サイトにある通常の source environment で解決する。別ファイルの alias を流用せず、Binding 左辺は Entry registry だけを見る。Root が通常 lookup と独立していても、任意の依存モジュールが source から自動的に到達可能になるわけではない。

### 5.3 Default は選択の入力、Library の確定結果ではない

各 Entry の選択規則は小さく固定できる。

```text
明示 Application Binding が複数 → エラー
明示 Application Binding が1つ → その Provider を検証
明示 Binding がなく、一意な指定済み SDK Default がある → それを検証
必要な Entry にどちらもない → エラー
```

無効な明示 Binding を Default で救済しない。Default も compiler 指定の意味を除けば通常の適合検証を受ける。source/dependency の列挙順、installed package の探索順、リンク時の weak symbol 選択で決定しない。SDK/profile/target と Default の対応を固定する。Library が自分のビルド環境の Default を焼き付けて、Application の指定を無視することも禁止する。

独自 Entry には通常 Default がない。Default を許す拡張では、所有元が指定する一意な定義などを別途規定し、複数 Provider パッケージの自己登録による候補競争を避ける。初期は SDK Default だけでよい。

検証範囲は次を推奨する。

- 宣言された Contract と、明示された全 Binding は利用有無にかかわらず検証する。typo や無効 Binding を DCE で隠さない。
- Entry 宣言だけでは、未使用の独自 Entry に実装を要求しない。
- 選択済みソースで検査する本体の Entry 利用を要求として記録する。実行時の定数偽分岐や未呼び出しを理由に意味検査を省かず、最適化設定で欠落診断を変えない。
- 最終成果物へ取り込む Library の要求集合、選択 Provider の依存、生成される操作・cleanup の依存を閉じる。取り込む単位と要求形式は Library 成果物仕様に明記する。
- 選択された Provider は Contract 全体を満たす。Library が使った一操作だけを実装しても、完全な適合とはしない。

API 契約を将来拡張すると、全体適合が必要なため既存 Provider が壊れ得る。この性質を Default や未使用判定で隠さない。

## 6. 「すべての依存を集約する」の範囲

**Application 全体で選ぶ環境依存の実装を一か所で把握・決定できる**、という意味なら強い設計になる。集約対象が増えても、すべての依存を暗黙の global service にする必要はない。

| 依存 | 推奨する扱い |
| --- | --- |
| 標準出力先、時計、乱数源などの環境方針 | 小さい Composition Entry の候補 |
| Core.Option / Result、Copy / Owned、string の言語 Identity | 固定の言語基盤。入口表記の整理と実装差し替えを分ける |
| allocator / deallocator、object runtime、低水準 OS 接続 | 個別の ABI・安全・起動契約を定めて段階的に構成対象へ追加 |
| DB 接続、要求ごとの logger、複数 device、短命な arena | Entry から生成してもよいが、取得後は明示的な値・引数・所有権で扱う |
| 通常のライブラリ型やアルゴリズム、パッケージ参照 | Kotonoha の依存として保持。Composition は package resolver を兼ねない |

アプリ全体で同じ選択を共有することと、ライブラリの任意の処理が全サービスを自由に使えることは別である。`$` を許すだけでは純粋性・sandbox・権限分離の保証にはならない。外部 FFI や既存の直接呼び出しも存在する。

依存の見える化には、compiler が Entry 要求を集計して表示する方が先に役立つ。たとえば「この Library は Clock と Console に依存」「この Provider が FileSystem を追加要求する」を追えるようにする。利用者が指定する許可リストによる検査は将来の build policy として導入できる。今回、新しい効果型や各関数への重複した手書き requires 構文までは必要ない。

### 6.1 大きい Std をひとつ差し替える費用

StdComposition に I/O、ファイル、時刻、乱数、network、allocation をすべて積むと、stdout を変更するだけでも巨大な Provider が必要になる。API を一つ追加するたび、すべての Provider が影響を受ける。

初期は原案どおり Std に writeLine だけを置いて始められる。拡張時は Console / Clock / Random 等、独立して交換する単位へ分割することを勧める。名前の階層と Entry の選択単位を混同しない。複数の Entry が同じ Contract を持つことは許し、Contract 型だけを DI のキーにしない。

Default の一部を使う場合も、Contract ごとの明示的な Provider や通常の forwarding 関数で足りる。初期版で operation ごとの部分 override、優先度、decorator stack、`super` Default、Entry-to-Entry alias を導入すると、選択規則が急に複雑になる。

### 6.2 状態と循環

Entry は保存域を持たず、Provider 型の指定で constructor / initializer を走らせない。Provider が状態を必要とするなら、通常の引数、所有する戻り値、group の静的保存域を使う。

静的保存域の初期化は既存の初回アクセス、Initializing 検出、正常初期化の逆順破棄をそのまま適用する。Composition に別の起動順・singleton lifetime・自動トポロジカル初期化を追加しない。[SPEC §22.2.3](../../SPEC.md)

Provider A が Entry B を、Provider B が Entry A を利用すること自体は、有限の静的参照グラフとして成立する。通常の相互再帰まで一律にエラーにする必要はない。区別する対象は以下。

- Binding alias の循環：初期版では alias Binding 自体を導入しない。
- 適合証明の循環：自分の適合を未検証のまま根拠にせず、既存の Unknown / Refuted / Error と証明規則を使う。
- 実行時の関数再帰：通常の制御フローであり、完全な停止性検査は要求しない。
- 静的初期化の循環：既存の言語規則に従い Abort する。

特に `Std => MyStd` で MyStd.writeLine の実装が `$Std.writeLine` へ無条件に転送すると、自分に戻る。Default へ委譲したことにはならない。診断や IDE でこの経路を示せると有益だが、一般の再帰を禁止して解決しない。

### 6.3 allocator と Abort の最下層

将来 allocation まで集約するときは、allocate / free / resize の互換性、alignment、最大サイズ、失敗時の動作、実装が保持する所有者情報、FFI を跨いだ解放をセットで規定する。単に `$Memory.alloc` と `$Memory.free` の Signature が一致するだけでは安全でない。

初期化中の allocation が logger を呼び、その logger が allocation を呼ぶと循環する。Abort の診断が差し替え可能な Std や allocator に依存しても、失敗時に同じ循環が起きる。組み込みの Abort 保証には、通常のユーザー Provider を呼ばずに診断を試みて終了する最小経路が必要になる。現在の Windows runtime が持つ低水準の TryWriteStderr / Exit の分離を維持する。

比較例として Rust は global allocator の指定を依存閉包で一つに制限しつつ、内部の必要な runtime support が直接 System を使う余地を明記している。これは Kimigayo の仕様ではないが、全体での実装選択と基盤の循環回避を分ける参考になる。[Rust std::alloc](https://doc.rust-lang.org/std/alloc/index.html)

### 6.4 Core.writeLine との統合

現在は `Core.writeLine(text: string)` が compiler 指定の標準出力操作であり、emitter がその Symbol Identity を認識して Windows runtime に接続する。`$Std` だけ差し替え可能にしても、既存 Library の Core.writeLine はそこを通らない。

全出力を集約するなら、最終的には公開 API の選択を一系統にする。

```text
$Std.writeLine
  → 選択された Std Provider
      → Default Provider の場合だけ、compiler/SDK の低水準標準出力実装
```

移行方法は Core.writeLine を `$Std.writeLine` に forwarding するか、公開 API を新入口へ移すかである。Kimigayo が互換性より簡潔さを優先するなら、後者も合理的である。いずれも既存 SPEC の stdout 保証、Core カタログ、examples、テスト、runtime への接続点を同時に改訂する。

Default Provider が forwarding 化後の Core.writeLine を呼ぶと循環するため、その実装が使用する低水準操作には別の compiler/SDK 内部 Identity が必要である。公開 Core.writeLine をそのまま「迂回口」として残すなら、全出力の集約が成立しないことを明示する。

## 7. コンパイラーの処理と Library 成果物

### 7.1 原案の直列 pipeline を二つの責務に分ける

Provider の名前だけなら本体解析前に選べる。しかし Provider が Contract を満たす証明には、型、制約、アクセス、Origins、必要な効果情報が要る。現行 Binder 自体も header 検査と本体後の検証を分けている。

さらに、Library は Application を知らないので Final Composition を持てない。次の意味上の境界を設ける。

```text
入力・target・SDK・条件設定を固定
  → directive 選択、元ソースの解析、宣言収集
  → Mods の provisional Binding / 生成 / 再統合
  → Entry と Contract の宣言・型・公開契約を確定
  → 利用側を Contract で Binding、所有権・制御フロー検査
      → Library: 未接続の Operation と依存情報を出力
      → Application: 明示 Binding と SDK Default を選択
          → Provider 本体・適合・依存閉包を検証
          → 必要な効果等の義務を解消して Final Composition を固定
          → 検証済み Operation-to-implementation 対応から生成
```

これは実装の全 pass を完全な直列に強制する図ではない。候補収集や Provider header 検査は前倒しでき、循環する解析は既存の worklist で扱える。禁止するのは、未確定 Provider を使った利用側 overload の再選択と、未検証義務を残した executable の発行である。

Mods は既存仕様の生成境界に従う。生成された Entry / Binding も宣言元の権限と重複検査に従い、Library の生成ソースが Application の Binding を追加する権限は得ない。最終構成を利用してその最終構成自体を変える生成ループは導入しない。

### 7.2 呼び出し計画は二層にする

論理的には次を保持すればよい。専用の runtime object を作る指定ではない。

```text
利用側の意味計画:
  Entry Identity
  Contract / Requirement Identity
  選択済み generic・Origin 引数
  引数ラベルの対応、取得、結果型、Loan・Effects、source context

Application の接続計画:
  Entry Identity → Provider の具体型
  Requirement Identity → 検証済み Member / substitution / 必要な適応
```

`$Std.writeLine` を早期に `MyStd.writeLine` の普通の AST に書き換える方式は避ける。後者で通常 lookup を再実行すると、追加 overload、Provider の defaults、Provider 型の情報が利用側へ入り込む。

既存の BoundCall の引数取得と Origin 情報を保持し、静的 Contract の witness mapping から最終実装を取得する。Requirement と実装の論理型が合法に互換であっても、物理 ABI が同じとは限らない。必要な thunk/adapter は既存の checked lowering と ABI 計画に従う。call site の source location と、Provider 内の失敗位置を区別して保持する。

### 7.3 Library が保持する情報

原案の「Entry / Contract / Operation に必要な情報」は方向として正しいが、少なくとも以下を含める必要がある。

- Entry の宣言元 Identity・version/content と固定 Contract Identity。
- Requirement Identity、正規化した signature、generic schema、Origins、Constraints、access、calling conditions、必要な完了済み効果情報。
- 呼び出し・関数参照・生成操作・cleanup が持つ要求と定義元の source context。
- 再生成するならソース・設定または規定した意味表現。機械語接続なら、外部操作の symbol と ABI・配置・必要な適応情報。
- 接続済み成果物なら、それを固定した Composition Identity と依存内容。異なる構成への再利用を拒否できること。

現行 SPEC §18.3 は serialized Koto internals を portable interchange にしない。新機能のために Koto のメモリ配置を永続形式として保存しない。独立した成果物形式と検証が必要になる。

### 7.4 直接呼び出しと別コンパイルは両立できる

二つの方式がある。

| 方式 | 接続時の仕事 | 適性 |
| --- | --- | --- |
| source / 意味情報を保持し最終構成で生成 | Provider が分かった後に対象コードを生成 | 初期案。最適化・generic・可変の内部 ABI と整合させやすい |
| Library object が Entry 操作の外部 symbol を呼ぶ | 最終リンクで検証済み adapter / implementation を供給 | 再コンパイルを減らせるが、操作 ABI と artifact 検証が必要 |

LLVM は関数宣言と外部関数呼び出しを表現できるので、別 object に実装があるという理由だけで runtime DI が必要になるわけではない。ただし Kimigayo の Contract 適合や所有権は linker が証明しない。前段の接続検証が必須である。[LLVM Language Reference: Functions](https://www.llvm.org/docs/LangRef.html#functions)

一度 Default Provider を inline 済みの機械語へ固定しながら、後から別 Provider に自由に差し替えることはできない。再生成可能な情報か未接続の呼び出し境界を残す必要がある。

shared generic body で operation の entry/context pair を渡すことは既存の [generic 共有設計](../Design/2026-09-13%20Generic%20Sharing%20and%20Specialization.md) と両立する。間接 call が残る場合も、名前から runtime service を探索することとは異なる。「すべての呼び出しは常に direct」とは保証せず、普通の確定した呼び出しには runtime lookup が不要と規定する。

動的ロードや、一つのプロセスに異なる Composition を持つ複数の実行領域を載せる仕様は今回導入しない。将来、その境界で値を渡すなら allocator と解放を含む ABI の追加規定が必要になる。

## 8. テスト、再現性、性能

### 8.1 テストでも構成の単位を変えない

原案では最終所有者が Application だけなので、Library を `kimi test` する際に誰が構成するかを追記する必要がある。compiler の生成するテスト用実行対象を、構成を所有する host として扱うのが自然である。

一つのテスト成果物には一つの固定 Composition を持たせる。ケースごとの構成変更、実行中の Provider swap、フィルターによる再構成は導入しない。別 Provider の比較が必要なら、別構成の成果物として生成する。ケースごとのプロセス隔離は、固定された選択の下で通常の状態を隔離する。[テスト仕様 §2・4](../Design/2026-09-13%20Testing.md)

製品の意味確定へテスト宣言を混ぜないという優先仕様も維持する。テスト Provider を単なる source fragment の追加で製品 Binding より優先させてはならない。別構成を選ぶなら、テスト host の明示的な build 入力として選択・記録し、製品側 Contract で確定した呼び出しを再接続する。具体的な設定名・構文と既存 Application Binding との選択規則は、テスト側と同時に決める未確定事項である。

通常のテストは製品と同じ構成を基本にできる。Library 単体テストでは SDK Default または明示した host 構成を用いる。組み込み検証と結果通信は、差し替えられた `$Std` に依存させない。

### 8.2 キャッシュは段階に応じて無効化する

Composition がビルドの意味を変えるという原案は正しい。ただし全段階の cache key に構成全体を無条件に入れると、無関係な構成変更で Library の意味解析まで失効する。

| 計画 | 主な依存 |
| --- | --- |
| Library の Provider 非依存な意味計画 | ソース・生成結果・compiler 検証規則・Entry/Contract 内容・通常の型/設定依存 |
| 適合と最終接続の計画 | 上記に加え Provider Identity/内容・witness・選択表・依存閉包 |
| IR・コード・context・最終成果物 | 確定接続、target/profile、ABI、最適化、backend と実際の生成依存 |

Contract 内容や意味に使った情報が変われば利用側を再検証する。Provider 本体の変更も、その効果を根拠にした証明や生成物へ波及させる。再利用範囲を証明できない初期実装では、保守的に広く失効して正しさを優先する。

最終 manifest には Entry→Provider の確定表、SDK Default の出所、必要な Contract と Provider の内容 Identity を記録する。Provider 名の文字列や version label だけでは不足する。構造順で正規化し、別名・ファイル順・依存列挙順・並列完了順で選択結果を変えない。

実装は [generic 共有仕様 §7](../Design/2026-09-13%20Generic%20Sharing%20and%20Specialization.md) の意味計画 cache と整合させる。初期から永続 object cache まで必要とする設計にはしない。

### 8.3 allocation とコンパイル時間

Entry を名前文字列で呼び出しごとに解決せず、宣言収集で intern し、解析中は Entry / Requirement の参照か小さい ID を持たせる。最終接続表は compilation ごとに固定し、呼び出しごとに Dictionary や witness 配列を生成しない。

既存 BoundCall、Contract witness、型・Origin の intern、再利用する配列/list 容量を利用する。全 Entry と全 Provider の総当たりではなく、明示・Default で選ばれた組だけ適合検証する。依存閉包は既訪問集合と worklist で辿り、同一 Contract の継承・同一 conformance を重複検証しない。

通常の直接呼び出しなら、Composition 専用の heap allocation、hidden receiver、runtime lookup は必要ない。Provider 自身の allocation、I/O、静的初期化チェック、一般の ABI adapter までゼロにはならない。直接化による速度やコンパイル時間の改善は実装後に測定する。

明示的に渡す値も残す設計の比較例として、Zig の標準 allocator 指針では allocation の可能性がある関数に Allocator 引数を受けさせる。この方針を丸ごと導入する必要はないが、全体の Default 選択と局所的な寿命・複数インスタンスの表現を併用する参考になる。[Zig std/heap.zig](https://github.com/ziglang/zig/blob/master/lib/std/heap.zig)

## 9. 現在の実装に必要な変更

ここは確認した実装の状態と追加作業を区別している。解析段階の部分対応は、native 実行の対応を意味しない。

| 場所 | 現状 | 必要な変更 |
| --- | --- | --- |
| [Parser.cs](../../Kimi/Compiler/Parsing/Parser.cs) `ParsePrefixExpression`、[UnaryKoto.cs](../../Kimi/Compiler/Parsing/Koto/Expressions/UnaryKoto.cs) | `$` は unary/MacroKoto として作られ、Parser が `$abort` の一引数形だけを許可 | root reference / root intrinsic / compose / Entry の専用構文を表す。演算対象の普通の名前を先に解決しない |
| [TokenKind.cs](../../Kimi/Compiler/Lexing/TokenKind.cs)、Parser の宣言処理 | `$`、`=>` の字句は存在する | `compose $` の contextual 検出と、許可位置・directive・エラー回復を追加。一般の identifier を無用に予約しない |
| [Binding.cs](../../Kimi/Compiler/Binding/Binding.cs) | 通常宣言収集、Core の復元、schema/Contract/header、本体、最終適合の順 | 通常 scope と分けた Entry registry、所有権限・固定 Contract・重複検査、final composition を管理する |
| [Binding.Expressions.cs](../../Kimi/Compiler/Binding/Binding.Expressions.cs) | unary の通常 operand を Bind する。root 専用の意味経路はない | sealed root operation の専用検査と、Entry を Contract の操作に結び付ける専用 qualifier role |
| [Binding.ContractCalls.cs](../../Kimi/Compiler/Binding/Binding.ContractCalls.cs)、[Binding.ContractMatching.cs](../../Kimi/Compiler/Binding/Binding.ContractMatching.cs) | 要件候補と Identity による適合・witness を持つ | Provider 非依存 Contract の eligibility と Entry 経由の要件選択を加え、適合の本体は再利用 |
| [Binding.Calls.cs](../../Kimi/Compiler/Binding/Binding.Calls.cs) `BoundCall` | Target、ConformingType、取得・generic/Origin・引数対応を保持 | 論理 Entry/Operation と最終実装対応を保持。既存取得計画を再 lookup で捨てない |
| [ControlFlowAnalysis.cs](../../Kimi/Compiler/Analysis/ControlFlowAnalysis.cs)、[OwnershipAnalysis.cs](../../Kimi/Compiler/Analysis/OwnershipAnalysis.cs) | 通常 call、転送、cleanup の基盤がある | `$abort` の Never/Abort、expect の失敗分岐、require の test return、lazy message と制御境界を実装 |
| [BodyLowering.Calls.cs](../../Kimi/Compiler/Emission/BodyLowering.Calls.cs) | Core.WriteLine を参照 Identity で特別扱い。receiver/generic/Origin 等は executable subset 外 | 検証済み接続から callee ABI を選択し、通常 direct call と必要な adapter へ流す |
| [CoreIntrinsics.cs](../../Kimi/Compiler/Binding/CoreIntrinsics.cs)、Windows lowering/runtime | WriteLine が compiler function として固定 | sealed root operation と差し替え可能 Entry を区別。DefaultStd と低水準 output の接続を分離 |
| [Compilation.cs](../../Kimi/Compiler/Core/Compilation.cs) | 外部 Kotonoha の identifier は持つが、読み込み箇所は未実装のコメント | 外部宣言・Contract・Provider・要求を読み込む。Library 出力では Final Composition 不在を正当な状態として表す |
| [LlvmEmitter.cs](../../Kimi/Compiler/Emission/LlvmEmitter.cs) | native 生成は外部 Kotonoha と宣言 Container を含まない Application に制限 | 最初に Provider struct の型関数を収集・検証・生成する経路を追加する。後で依存モジュールの接続・生成を実装 |
| [CompilationBuildMetadata.cs](../../Kimi/Compiler/Core/CompilationBuildMetadata.cs)、[EmissionArtifacts.cs](../../Kimi/Compiler/Emission/EmissionArtifacts.cs) | prepared inputs と Windows 用 manifest。完全な cache key ではない | SDK/Entry/Contract/Provider と最終構成を段階ごとに識別し、成果物検証へ追加 |
| 診断・SourceDocument・startup・将来 Mods/test host | 既存の定義元 context と startup 規則 | Binding 原因位置・要求の経路・使用箇所を報告し、宣言を実行項目と誤認しない |

特に、現行 `$abort` は構文があることをもって end-to-end 実装済みとは言えない。STATUS でも explicit `$abort` の Binding は pending とされ、現在の unary Binding にも専用処理がない。今回の機能では sealed operation を単に既存完成機能として放置せず、必要な段階を実装・検証する。

言語仕様を採用する際は SPEC §1.2・6・7.6・8.4・9・13.8・17・18・19・20.7–8・21・22 と文法・用語を同期し、テスト仕様も同時に合わせる。現在のレビュー段階では SPEC / STATUS を「採用済み」「実装済み」へ変更しない。

## 10. 段階導入と検証項目

### 10.1 導入順

1. **意味規則の確定**：sealed root function、Provider の種類、Entry の所有と access、Provider 非依存 Contract、Default と要求集合、Core.writeLine の統合方針を決める。
2. **単一 compilation の Composition**：SDK の Std 宣言・Default、Application の compose、receiverless な nongeneric Provider、既存の scalar/owned string 呼び出しで実装する。現在 emitter が拒否する宣言 Container のうち、Provider 型と対象の型関数を扱えるようにする。契約検証と最終接続をこの段階で分離する。
3. **組み込み操作の整備**：root 構文基盤を共有しつつ、explicit Abort とテスト仕様の expect/require を各担当の CFG・cleanup 経路へ実装する。Std の差し替えだけでテスト機能完成とはしない。
4. **複数 Kotonoha**：宣言読み込み、Entry の限定参照、Library 要求出力、Provider を Application で選ぶ接続を実装する。初期は再生成できる source artifacts が適する。
5. **一般化と性能改善**：generic operation、関数値、分離 object 接続、細かい cache 再利用を必要性に応じて実装する。allocator や実装依存の抽象型は独立した設計確認を経る。

### 10.2 振る舞いで確認するテスト

| 観点 | 必須の確認例 |
| --- | --- |
| sealed operation | abort/expect/require の再宣言、overload 追加、compose 指定を拒否。同名 local や alias に影響されない |
| 構文・宣言位置 | `compose` を普通の名前に使用可能。group $ 内の runtime Field/実行文、関数内 compose を拒否。compose だけでは Application entry にならない |
| Contract の固定 | Provider に overload/default/member を追加しても利用側の結果が変わらない。未固定 Self/関連型を公開する Entry を拒否 |
| 適合と所有権 | ラベル違い、string/ref-string 違い、強すぎる Origin/Constraint、unsafe、不可視実装を拒否。正常呼び出しの Move と cleanup は通常 call と一致 |
| 選択 | 明示優先、重複、Default 不在、無効明示 Binding を確認。複数文書・生成入力の順を変えても同じ結果 |
| Library | Library を Provider 未選択で検査し、App A/B が別 Provider へ接続できる。Library は App の構成を変更できない |
| 依存閉包 | Provider が追加要求する Entry の欠落を原因経路付きで報告。生成/cleanup の依存を落とさない。未使用明示 Binding の誤りも検出 |
| 状態・再帰 | Provider 選択だけで初期化しない。通常の相互再帰を不当に禁止しない。静的初期化の循環は既存規則を維持 |
| Core/runtime | 選んだ出力 Provider を旧公開入口が迂回しない。Default が公開 forwarding に戻って再帰しない。Abort 経路は Std に依存しない |
| テスト | lazy message、単一評価、require return/cleanup、製品/テストの依存分離、同一成果物の全ケースで構成固定 |
| 生成・再利用 | 通常 call に root lookup/container を生成しない。Provider 変更で古い IR/context/inline body を流用しない。固定 Contract の意味計画は適切に再利用 |

最初の実装は通常の managed compiler テストと LLVM IR 検査から始め、必要な native 検証を段階に合わせて行える。NativeAOT テストはユーザーが明示指定した場合だけ行う。

## 11. 原案へ追加したい規範の骨子

原案の責務分離を維持したまま、次を追記・置換すれば仕様の中心がまとまる。

1. 言語定義の root function は置換不能であり Composition Binding の対象ではない。
2. Entry は宣言元 Identity と固定 Contract を持ち、runtime value・Type・Property・保存域ではない。
3. Composition Provider は具体型を指し、初期の公開 Contract は Provider 非依存の receiverless function requirements で構成する。
4. Provider は通常の静的適合規則で検証され、Entry 利用側は Contract のみで呼び出しを確定する。
5. Application は実装を選択するが、Entry の宣言・Contract・Library の定義元の意味を変更しない。
6. Library は Provider 未選択で解析でき、未接続 Operation と必要な意味・依存情報を成果物に保持する。
7. 明示 Binding、指定された Default、欠落エラーの順で選び、無効 Binding の fallback と列挙順による優先を認めない。
8. Provider の選択は通常のインスタンスや初期化を生成せず、実行時の状態は既存の所有権・静的初期化・破棄規則に従う。
9. 最終接続後、必要な ABI 適応と生成を行う。Composition 自体は runtime service lookup を要求しない。
10. 最終構成と各段階の依存を成果物・再利用判定に含め、意味検査の結果を DCE や実行ケース選択に依存させない。

この形なら、Composition Root は言語全体に別のオブジェクトモデルや適合体系を追加せず、既存の Contract・型・所有権・call 計画を、Application の実装選択へ拡張する機能として位置付けられる。
