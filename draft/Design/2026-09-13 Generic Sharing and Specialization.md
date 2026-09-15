# ジェネリック共有コードと自動特殊化の仕様

2026-09-13 改訂。[決定記録](../Decisions/2026-09-13%20Generic%20Sharing%20Review.md)の精査結果を、2026-09-14 に [SPEC §21.3・§21.4.6](../../spec/21-layout-runtime-and-code-generation.md#213-generic-code-generation)、付録 A.14・B.7・D と関連節へ統合した。2026-09-15 に本文との同期と Composition Root 案取り下げに伴う参照整理を行った。現行の規範は SPEC 本文、実装状況は [STATUS.md](../../STATUS.md) に従う。仕様の統合はコンパイラー実装の完了を示さない。

第I部は生成の正しさを定める規範、第II部は初期コンパイラーの実装指針、第III部は将来の検討範囲である。内部名は説明用であり、新しい構文、reflection、JIT、安定した外部ABIを追加しない。kimiの例は言語仕様上の例、textの例は内部の疑似コードである。

## 第I部　規範

### 1. 意味の確定

1. 宣言した制約の下で、generic bodyの型・操作・所有権・Loan・cleanupを普遍的に検証する。
2. 使用時の引数と契約を検証し、閉じた明示specialization集合から実装を選ぶ。
3. 検証済み計画へ代入し、具体的な配置・取得・効果・cleanupを確定する。
4. 意味を保存する基準共有計画を作り、その上で有限の任意最適化を行う。

この順序は論理的な依存であり、pass分割を固定しない。共有・特殊化・予算変更で、意味上の受理、実装選択、結果、効果順、cleanupを変えない。lookupや制約・Loanの検証を実行時へ移さず、生成キーで消去するOriginも意味の検証と依存には保持する。

変更しない言語規則はSPECの[汎用body検証](../../spec/08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations)、[長さ引数](../../spec/04-arrays-indexing-and-slices.md#44-function-length-parameters)、[取得・cleanupと内部ABI](../../spec/21-layout-runtime-and-code-generation.md#214-checked-lowering-and-internal-abi)に従う。

~~~kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func forward<T>(value: ref/T) -> i32 => classify<T>(value)
~~~

forwardのbodyを共有しても、T=i32では選択済みの明示specializationを呼ぶ。自動特殊化の予算0でもこの選択は変わらない。

### 2. 代入計画と共通lowering

#### 2.1 要求と束縛

共有・全固定・部分固定は同じloweringを使う。型付き操作が必要とする事実を、検証済み代入計画から取得する。

| 束縛 | 処理 |
| --- | --- |
| `Fixed(value)` | 定数・直接命令・既知entryとして使う |
| `FromContext(slot)` | schemaが定める型で読む |
| 要求なし | slotを作らない |

要求は型引数と一対一ではなく、size、offset、長さ、選択済みcallee等を個別に表せる。符号・幅、浮動小数点規則、Semantics、SharedReadResult、所有権・部分状態は型付き計画に残す。具体型を見て意味を選び直さない。

| 操作 | 基準の共有経路 |
| --- | --- |
| 借用の単純転送 | 参照先情報は不要 |
| 値の転送・配置 | size・offset等の整数と、検証済み格納領域 |
| 破棄・Contract操作・選択済みcall | 操作ごとのentryと専用contextの組 |
| スカラー演算・型付きload/store | 型付きhelperと必要なABI適応 |

helperは元の演算の検査、失敗順、source locationを保存する。共有表現・操作・ABI・格納経路で意味を保存できない場合だけ基準を分離する。スカラー演算であることだけを理由に大型bodyを型別複製しない。未対応経路は診断し、意味を変える代用品を生成しない。

#### 2.2 固定事実の整合性

size、alignment、offset、型identity、取得条件、操作の組は同じ代入から導出する。bodyに埋め込む命令・属性の前提も固定事実として記録し、接続するすべての代入で成立することを静的に証明する。slotの物理型が同じだけでは接続しない。

~~~text
固定事実:
    size(T) = 16
    転送元・転送先は16 bytesの有効な範囲で、互いに重ならない
    転送元・転送先のalignment >= 8

許可: memcpy 16 bytes, align 8
不可: sizeだけを根拠にalign 16を付ける
~~~

sizeの一致から派生型のoffset、破棄、符号付き演算を固定してはならない。LLVMのalign・dereferenceable等には証明済みの定数を使う。共通の保証が弱ければ属性を弱めるか省略し、runtime slotの値をそのまま定数属性へ指定しない。より強い属性はその前提を証明できる分岐・helper・特殊化で使う。noaliasやinboundsもそれぞれの契約から証明する。

~~~kimi
func transfer<T>(value: T) -> T => value

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func keepBorrow<T>(value: ref/T) -> ref/T from value => value
~~~

transferは代入に対応するCopy／Move計画を実行する。duplicateには静的Copy証明が必要で、runtimeのHasCopyはsourceの合法性を決めない。keepBorrowは参照先を読まないため、そのmetadataを省ける。

### 3. 入口ABIと呼び出し責任

#### 3.1 予算に依存しない入口

入口ABIは呼び出しの契約から決める。

| 呼び出し | 入口ABI |
| --- | --- |
| calleeがFixedで、代入後の署名の表現を確定できる | 非generic関数と同じFunctionAbi規則 |
| contextのcallee pair経由、または署名の表現が未確定 | generic宣言の署名に基づく共有ABI |
| 共通関数値 | Function Typeの既存契約（SPEC §21.2.5） |

共有ABIでは、代入で表現が変わる値を取得済み領域へのpointer、結果をcallerの未初期化領域へのpointerで渡す。宣言から共通表現が分かる値はその表現を使い、ref・uniqの借用は既存の1 pointerを維持する。entryがFixedでも、それだけでは未確定の署名をscalar ABIにしない。

予算が変えるのは入口の実装先と内部適応であり、callerが使う入口ABIは変えない。必要な入口を呼び出し契約ごとに生成し、内部の共有body・特殊化bodyへ接続する。同じ契約と実装を満たせば入口を統合できる。

~~~text
具体caller → direct_i32_entry(i32, i32) -> i32
                          ├ 共有body向けに領域へ格納 → sharedBody
                          └ 特殊化body

共有caller → {erased_entry, 専用context}
                          → sharedBody または適応後の特殊化body
~~~

具体callerのscalar受け渡しは予算0でも維持する。ただし入口内部のメモリ転送や追加callまでなくなる保証ではない。候補を取り消す場合は入口の実装先を基準へ戻せるため、再帰するcaller群を一括特殊化する必要はない。

#### 3.2 Entryと専用context

呼び出しの論理単位は`{entry, context}`である。entryと、そのentryが読むcontextは一緒に生成・検証する。callerはcalleeのcontextを不透明なpointerとして渡し、そのschemaを解釈しない。schema変更は読み手とcontextの内部で完結し、不要slotを削除できる。schemaだけの違いを適応するadapterは作らない。

最終生成単位の内部生成方式identityは、compiler build、target/profile、ABI・metadata・contextの生成規則とそれらを変える設定から決める。接続ごとに、この方式と入口ABIの一致をコンパイル時に照合する。入口ABIには論理引数、結果、所有権、context引数と物理型・位置・呼び出し規約・属性の対応を含める。間接callの型付き契約は必要であり、slotごとのruntime ABI tagは不要である。

同じ方式内でもscalar返却と結果領域返却等にはABI adapterが必要になる。異なる方式の生成物は再生成または拒否し、方式間adapterやruntime照合は導入しない。OS入口、FFI、外部backend helperは既存の外部契約を維持する。

内部の引数除去・calling convention変更は全使用との互換性を証明した後段最適化として許す。引数の物理位置、symbol名、未使用入口の出力は公開仕様へ固定しない。

#### 3.3 取得・結果・cleanup

receiverと引数は言語規則の順に一度だけ評価・取得し、論理callee入口で責任を渡す。入口・adapter・bodyの分割は追加のCopy／Moveや二重cleanupを生まない。所有引数・ref・uniq・結果領域は、同じpointer型でも異なる契約である。

結果は必要なcleanupの正常完了後にcallerへ渡す。Abort・非終了では後続cleanupや結果受け取りを行わない。calleeはcaller所有の格納領域を解放しない。

### 4. 情報の供給

#### 4.1 整数と操作の組

GenericContextには、読み手が必要とする事実だけを直接置く。Windows x64ではschema付きの8-byte slot列とし、論理種別を三つにする。

| slot種別 | 内容 |
| --- | --- |
| 64-bit整数 | 長さ、size、alignment、offsetはisize、型token等はu64、真偽値は0／1 |
| entry ptr | 選択済み操作の入口 |
| context ptr | その入口専用のcontext、操作が要求するmetadata、またはnull |

長さ・配置の整数は非負で、profileの表現上限をコンパイル時に検査する。整数の符号と用途はschema・型付き計画で定め、LLVMでは共通のi64に格納する。整数とpointerを区別し、pointerの整数化やruntime tagは使わない。

呼ぶ操作はentry・contextの隣接2 slotにする。ContractのwitnessとFrameLayoutはコンパイル時の対応計画とし、GenericContextから辿る実行時tableにはしない。関連型とrequirement・実装の対応は意味計画に保持する。Fixedの成分や不要な要求は省ける。

通常の共有bodyはsize・offset・長さを直接読み、TypeContextを辿らない。calleeだけが必要とする情報はcalleeの専用contextに残す。同じ意味の要求は一つにまとめるが、異なる要求を現在の値が偶然同じという理由だけで統合しない。

~~~text
比較操作を使う共有bodyのcontext例（T=i32）:
    [0] isize  size(T)                  = 4
    [1] entry  選択済みcompare入口      = compare_i32
    [2] ctx    compare_i32専用context   = null
    [3] isize  offset(temp1)            = 4

offsetがbodyの全代入で一定ならFixedにして[3]を省く。
callでは[1]と[2]を読み、compareの入口ABIに従って引数を渡す。
~~~

直接の整数取得は1 slot、操作の組の取得は最大2 slotのloadで済む。操作内部のmetadata参照や値の読み取りは別であり、実際のload命令数・速度を保証するものではない。平坦化でcontextが大きくなる可能性も計測する。

#### 4.2 Metadataと派生型

ValueMetadataの既存48-byte形式を維持する。完全な値のArgKeyに対応するtypeKey、size、alignment、flags、destroyValues、TypeContextを保持し、strideはsizeと同じとする。値のmetadataとobject payloadのmetadataを混同しない。

TypeContextは型操作のentryが読む。Field offset、要素／Case metadata等を持ち、`[N of T]`では`{elementMetadata, N}`を含む。派生型の配置は閉じた代入から全体のTypeLayoutで計算する。body用の整数slotもこの同じ計画から導出し、runtimeで配置を組み直さない。

型identityが必要なら完全幅のu64として整数slotまたはFixedで供給し、isizeの非負値へ縮めない。metadata固有の処理には操作が必要とする不透明なmetadataを渡す。検証済み条件分岐に真偽値を供給できるが、SharedReadResultを単なるHasCopy判定に置き換えない。instanceの初期化状態・Loan・動的collectionの長さは共有metadataへ入れない。

#### 4.3 Schemaと循環

schemaは読み手の要求、定義側の束縛、正規化した型／長さ式、操作とslot型の対応を表す。要求キーの安定した構造順で重複排除・配列化し、操作の2 slotは組として並べる。出現順・並列処理の完了順に依存させない。calleeの内部schemaはcallerのschemaへ含めない。

metadata/contextは閉じた代入に対応する不変定数であり、有限の循環を許す。相互再帰をDAGへ制限しない。選択後にすべてのentry・専用contextの組と固定事実を検証し、未完成の定数を実行可能な出力へ公開しない。候補変更でcalleeのcontextが変われば最終参照を解決し直すが、callerのABIや最適化選択を変更する必要はない。

contextとmetadataは全使用・cleanupまで有効とし、callerのstackへ依存させない。呼び出しごとのcontext構築やruntimeの型検索は要求しない。

### 5. 作業領域と固定フレーム

#### 5.1 再利用と生存期間

結果領域への直接構築、取得済み所有引数領域の利用、中間転送の除去を優先する。評価順、別名、Loan、格納identity、生存期間、部分初期化、cleanupを保存する。

Copy sourceとcalleeの取得値を同居させず、生きた再代入対象へ先に書かない。所有引数をMoveした後も、旧値の責任と残存参照がなく、容量・alignmentが適合すれば領域を再利用できる。変更不能なsource引数への代入を許す規則ではない。

cleanup・最終参照まで含む生存期間が重なる値には別領域を使う。反復間で同時生存しないloop一時値は同じ領域を再利用できるが、破棄を最後の読み取りへ繰り上げない。

#### 5.2 正確な容量と共有body

bodyでsize・alignmentが固定された一時値はbody自身の静的フレームへ置ける。残る領域は閉じた代入ごとに配置し、その入口が必要な正確な容量を最大alignmentで丸めて固定サイズのentry allocaで確保する。容量を他のinstanceの最大値やbucketで決めない。

共有bodyには呼び出し固有の`scratch` pointerを別の内部引数で渡す。offsetはFixedまたは整数slotで供給する。scalar引数の領域化等、ABI適応に必要な領域も計画へ含める。

~~~text
directEntry(x: i32, ...) -> i32:
    引数・結果のABI適応用領域を必要な分だけ固定確保
    scratch = 固定alloca(capacity, alignment)  // 容量0なら省略
    sharedBody(適応済み引数, 結果領域, 専用context, scratch)
    cleanup完了後の結果を返す

sharedBody(..., ctx, scratch):
    temporary = scratch + load(ctx[offsetSlot])
    // 検証済み計画に従って構築・使用・cleanup
~~~

各領域のoffset・容量・alignmentを入口の予約範囲と静的に照合する。共有bodyは作業領域をruntimeに配置計算・動的確保せず、sourceが要求する通常のheap確保は既存規則に従う。loop内で反復ごとにstackを積み増さず、再帰呼び出しごとに必要な独立領域を持つ。

入口はABI、容量・alignment、接続先body、適応処理、埋め込む定数・context参照がすべて適合する場合だけ共有できる。容量0ならフレーム確保を省くが、入口とbodyのABIやcontext渡しが異なればadapterは残す。

scratchを共有contextへ保存せず、そこへの借用を結果・Closureへescapeさせない。格納値のMoveや、その値が持つ外部への合法な参照の返却は維持する。正常returnでframeを戻し、入口で二重破棄しない。scratch使用中にそのframeを破棄するtail callは許さない。

#### 5.3 初期profileの範囲

初期profileはalignment<=16の固定stack経路を対象とする。型のalignmentを切り下げず、超過時は対応経路がなければ表現未対応を診断する。固定frameにも必要なWindows stack probeとunwind情報を生成する。

ゼロサイズには既存のalignment付き代替格納を使い、論理状態・Loan・破棄回数を維持する。代替領域の存在から正のdereferenceable範囲やnoaliasを導かない。正確な容量は計画上の予約量であり、spill・call領域等を含む最終machine frame全体のサイズではない。

動的alloca、heap退避、新しいstack枯渇・確保失敗契約は導入しない。既存のLibrary出力範囲を維持し、未知の代入は今回の生成対象に含めない。

### 6. 長さ、転送、破棄

#### 6.1 長さの供給

| 用途 | 供給元 |
| --- | --- |
| 明示specialization選択 | 静的LengthKey |
| bodyのN、境界検査、Slice長、要素loop | 整数slotまたはFixed |
| 配列の転送size | 同じTypeLayoutから計算した整数slotまたはFixed |
| 固定配列の破棄 | 配列TypeContextのelementMetadataとN |
| calleeだけが使う長さ | callee自身のcontext |

長さ要求はSPEC §4.4の正規化済み式と定義側束縛で識別する。型形成の合法性は宣言上の前提から証明し、具体代入では検査付き評価を行う。body-onlyの型形成を都合のよい代入まで保留しない。

~~~kimi
func keepArray<length N, T>(value: [N of T]) -> [N of T] => value

func lengthAfterOffset<length N>(value: ref/[(N + 4) of u8]) -> isize
    return N + 4

func twiceLength<length N>() -> isize => N * 2
~~~

keepArrayの転送はsizeだけで足りる。lengthAfterOffsetは署名のValidLengthの証明を使ってN+4の計算を省ける。twiceLengthのN*2は通常のchecked isize算術であり、runtime Abortを型形成エラーへ変えない。

size / strideからNを逆算せず、bodyが読むNをTypeContext経由で供給しない。body用整数と破棄用Nの必要な重複は許す。

#### 6.2 転送と検査除去

合法な非重複の完全値Copy／Moveはsize bytesのmemcpy相当で実行できる。許可された重複範囲の再配置はmemmove相当とする。転送そのものにallocation、count増加、ユーザーCopy関数を加えない。paddingを意味のある値として読み取らず、ゼロbyteでも責任の遷移を消さない。

~~~kimi
func sum<length N>(values: ref/[N of i32]) -> i64
    var total: i64 = 0
    for i in values.indices
        total += values[i]@i64
    return total
~~~

小さいNの固定は検査除去・loop展開の候補となるが、Nを固定しただけでtotalのoverflow検査を除いてはならない。

byte offsetの乗算にnuw等を付けるには、同じ配列の0<=i<Nが乗算を支配し、strideが非負、N*strideが演算幅・配置上限内、値がdefinedであることを証明する。pointer加算、provenance、inboundsは別途証明する。stride=0でも論理的な境界検査を維持する。

#### 6.3 破棄の組

破棄の組は`{destroyValues, metadata}`とする。既存の`destroyValues(first, count, metadata, location)`においてmetadata引数が専用contextに当たる。通常関数と物理引数順まで同一にせず、操作の入口ABIに従う。

count=0またはentry=nullならcallと範囲走査のアドレス計算を省く。null entryは破棄不要の証明としてのみ使う。完全にInitializedの範囲を逆論理順に破棄し、Moved・部分状態・予備領域はCleanupPlanで除外する。source locationと失敗順を保存する。

固定配列のentryは、自身のmetadataのTypeContextからelementMetadataとNを読む。外側count個の配列を逆順に走査し、各配列について内側N個の要素を破棄する。破棄後の格納領域解放は別操作とする。

~~~text
{arrayDestroyEntry, arrayMetadata}で外側count個を破棄:
    elementMetadata, N = arrayMetadata.TypeContext
    配列を逆順に:
        elementMetadata.destroyValues(arrayFirst, N, elementMetadata, location)
        // 要素entry=nullまたはN=0なら省略
~~~

完全な連続固定配列では、metadata・範囲・逆論理順・失敗位置が一致すればcount*N個の要素への一回の範囲破棄にまとめられる。積がcount幅を超えれば元のloopを保ち、新しいAbortを加えない。ゼロstrideでも効果の回数を保つ。

### 7. 有限生成・予算・cacheの契約

必須生成の資源上限と任意最適化の増加予算を分ける。同じ入力・compiler/profile・設定では、列挙順・並列完了順・cache有無によらず論理計画と予算配分を再現する。OSの実資源不足まで同じ結果を保証しない。

必須生成の閉じた代入、metadata/context、配置・計画は有限でなければならない。有限の循環、T→Box<T>等の異なるキーの増殖、無限のinline値配置を区別する。コード共有だけではmetadata生成の有限性を保証できない。

任意探索の上限・不成立では基準計画を維持し、最適化の失敗だけを理由にsourceを拒否しない。必要な基準生成も上限を超える場合は資源不足を診断する。機械語を出さない定義も必須の意味検証・未対応診断を省略しない。

予算の数値と見積り方法は言語仕様の固定値にしない。最終binaryサイズやコンパイル時間の厳密な上限を、推定命令数から保証しない。

意味・生成処理の初期永続cacheは検証済みの意味計画までとする。[依存・成果物仕様 §5](2026-09-13%20Dependencies%20and%20Artifacts.md#5-検証と再利用)に従い、版をまたぐ宣言の対応付けと、実際の型 identity・内容・検証結果の有効性を区別する。再利用する判断が実際に読んだ全依存内容を照合し、定義・選択集合・環境の変更、追加・削除も検出する。証明、所有権、効果、cleanup、token への位置参照、正当な未解決の表現義務を保持する。診断・生成物の位置や表示する式は現在の原文へ結び直し、Mod が観測する原文は別の入力依存として照合する。使用時のLoan・初期化検証はその使用環境で行う。

ABI、schema、context、frame、予算選択、IR・機械語は現在の生成方式で作り直す。生成だけに影響する倍率変更は意味計画を不要に失効させず、target依存の証明や意味を変える設定は検証条件に含める。native の内容解析要約は入力の索引として別に保存できる。製品の生成選択結果の永続化は、候補探索の費用を測定し、基準計画・候補集合・予算・全生成依存の照合方法を定めるまで導入しない。

生成物が読む操作・選択された実装・schema・埋め込む定数へ依存を記録し、変更で影響する witness・inline body・entry/context は確実に再検証・再生成する。精密な依存が未整備の段階では保守的な失効を許す。取り下げられた Composition Root 案の Entry／Provider 選択と CompositionId は、本書の共有キーや生成規則から導入しない。

## 第II部　初期コンパイラーの実装指針

### 8. 生成計画と特殊化予算

#### 8.1 キーと生成順

| キーの分類 | 内容 |
| --- | --- |
| 意味計画 | compiler build・検証規則、定義・束縛・意味環境、選択集合と依存 |
| body | 選択済み実装、型付き操作・cleanup、内部ABI、読むschema、埋め込む固定事実 |
| 入口 | 入口ABI、接続先body、容量・alignment、適応処理、埋め込む定数・参照 |
| context内容 | 読み手schemaに対応する整数・操作の組・metadata内容と参照先 |

これらはキーの役割の分類であり、四つのhashだけですべてを判定する指定ではない。方式identityは生成域の前提とする。bodyに不要な具体型・容量をキーへ入れず、直接破棄等へ埋め込むentry・contextは省略しない。

循環する定数は論理nodeを先に登録し、内容と辺を後で確定する。参照先の内容を再帰的にキーへ展開しない。hash衝突は構造比較で判別し、メモリアドレス・登録順IDに意味を依存させない。

1. 必要な閉じた代入と依存を登録・検証し、必須資源を確認する。
2. 共通の型付き操作と内部ABIで表現できる代入を基準共有クラスへまとめる。軽い簡約後の基準body、入口契約、基準資源量を固定する。
3. 各クラス内で、操作の依存を満たす少数の固定集合から選択肢を作る。選択肢は全メンバーをどのbodyへ割り当てるかを示し、未特殊化メンバーも含める。
4. §8.2–8.3の予算内でクラスごとに一つを選ぶ。入口ABIを維持して接続し、entry専用schemaとcontextを確定する。
5. 資源・固定事実・全接続を再確認し、必要な生成物だけを出力する。任意選択による超過は安定した逆採用順で基準へ戻し、同じ選択肢を再採用しない。

選択前後でクラス間の呼び出しは安定した入口契約を通す。calleeの内部body・schemaをcallerの候補へ取り込む最適化はこの選択段階では行わない。再帰の有限性確認は必要だが、再帰SCC全体を一つの特殊化候補にはしない。

#### 8.2 クラス内の排他的な会計

費用の単位は共通計画の推定命令数だけとする。候補評価のために後段loweringやLLVMを試行しない。各選択肢の費用を次で固定する。

~~~text
追加費用 = max(0, 選択肢が必要とするbody群の命令数 − 基準クラスの命令数)

基準共有body 100命令、特殊化body 60命令:
    未特殊化メンバーが残る → 100 + 60 − 100 = 60
    全メンバーを置き換える → max(0, 60 − 100) = 0
~~~

クラス内の同一bodyは一度だけ数える。共有bodyを差し引けるのは全用途が置き換わった場合だけで、選択肢から別の選択肢への差分会計はしない。削減量を他クラスの予算へ加算しない。

入口・adapterのABI適応と固定確保、contextのデータは増加予算から外し、基準を含め生成資源として数える。ただし本体の複製・展開を入口やhelperへ移しても、その命令はbody群の費用に含める。除外対象は呼び出し境界の定型処理に限る。

helperを含むbody群はクラス内で完結する計画として見積もる。クラスをまたぐ生成物の重複排除は選択後に行い、その削減を費用・予算枠へ還元しない。これにより他クラスの採用に伴う費用再評価をなくす。入口・contextの件数だけでなく命令数・bytesも資源として制限し、instance数への単純比例は仮定しない。

利益は基準に対して消せるcontext依存操作を、軽い簡約後に数える。残るloop内の操作には上限付きの深さ重みを掛け、loop外へ移したloadは一回分とする。外部の候補採用や将来のLLVMインライン化による利益を先取りせず、負の利益・変化のない候補は採らない。正の費用には正の利益を要求する。

#### 8.3 二つの上限と決定的な選択

任意選択前に元generic関数ごとの基準命令量Bfと生成単位の基準命令量Bを固定する。複数クラスのBfを同じ元関数へ集計し、Bは非genericの内部コードも含める。これらは§8.2の会計に従うクラス間重複排除前の推定量である。外部ソース依存から生成するbodyも計上し、別途リンクする既存nativeコードは含めない。

本節の生成単位は計画と予算を確定する単位であり、最終LLVM moduleと同一である必要はない。[依存・成果物仕様 §6.2](2026-09-13%20Dependencies%20and%20Artifacts.md#62-生成要求と予算)に従い、製品とテストの生成要求・共有クラス・予算を分ける。検証済みの入力・設定で製品を先に確定し、テスト専用の追加代入で製品の入口・schema・frame・予算選択を作り直さない。再利用する製品コードをテストのB・Bfへ加算せず、候補や余剰予算も相互に移さない。確定後にテストから不要なコードの出力を省けるが、生成依存の閉包を維持し、製品予算を再配分しない。

| 増加上限 | 計算形 |
| --- | --- |
| 元generic関数ごと | 倍率m × 内部係数k × Bf |
| 生成単位全体 | 倍率m × 内部増加率r × B |

利用者向け設定は有限・非負の倍率m一つとする。係数・丸めは決定的にし、BfまたはBが0なら対応する増加枠も0。body数の独立した最適化予算は作らない。

有限の選択肢を、まず費用0の有効な候補、次に利益÷正の費用の降順に並べる。同順位は意味計画・クラス・選択肢の安定キーで決める。費用0同士は利益の降順とする。上から両上限と資源条件を満たすものを採用し、採用済みクラスの残りの選択肢は捨てる。未採用クラスは基準を使う。比はoverflowを避けて比較し、費用0で除算しない。

クラスは一度だけ確定するため、採用後の再評価は不要である。この貪欲法は最適な配分を保証しない。生成済み選択肢数nに対するsort・選択はO(n log n)にできるが、候補の構築、全メンバーの評価、資源・接続検証は別の費用である。候補数、割当記述量、固定集合、探索作業量を制限し、全直積を列挙しない。

| profile方針 | 倍率と探索 |
| --- | --- |
| O0 | m=0。任意探索を省き、必須生成と軽い簡約を行う |
| O2 | 実測で定める既定倍率 |
| サイズ優先 | m=0を既定とし、非増加の簡約・置換を優先 |

倍率0でも具体callerの入口ABI、必須分離、再利用は維持する。LLVMの後段最適化は別の方針で制御され、この倍率だけで最終binaryの複製を全面禁止しない。数値、設定名、レポート形式は実装時の計測で決める。

### 9. 性能改善と検証

新規複製より先に、Fixed情報の定数伝播、直接call、不要分岐・転送・slotの除去を試す。context loadは有効な範囲で共通化・loop外移動し、null経路への無条件移動とregister圧力の増大を避ける。

固定サイズの一時値をbodyへ分離し、scratch先頭を必要な最大alignmentへそろえると、先頭の正サイズ領域のoffsetを0にできる。領域の再利用には安定した貪欲彩色等を使い、最適彩色を要求しない。

小さな入口はインライン化しやすいIRにし、一律alwaysinlineにはしない。融合によりcallとframeを減らせるか、生成物で確認する。融合後もloop反復ごとのstack増加、scratchの寿命違反、費用計上からの本体複製漏れを生じさせない。

要求・TypeLayout・FunctionAbi・CleanupPlanを再利用し、候補は基準の複製でなく差分として保持する。必要nodeをworklistで作り、配列・探索用領域の容量を再利用する。循環比較では訪問済みnode対を記録する。

| 確認対象 | 代表例 |
| --- | --- |
| 意味・ABI | 明示specialization、予算0のscalar入口、部分固定で未確定の署名、関数値、外部ABI、取消し |
| Context | slot削除、隣接pair、異なる専用schema、相互再帰、型増殖、完全幅の型identity |
| 固定事実 | 同sizeで異なるalignment・破棄・派生配置、SharedReadResult、属性の前提 |
| 格納 | Loan・部分Move・Copy source、容量0でも必要なadapter、再帰、loop、非escape、ゼロサイズ |
| 配置・破棄 | 他instance追加時の容量独立性、N=0、stride=0、負のi、通常算術overflow、count×Nのoverflow |
| 予算・cache | 排他的な割当、小さい特殊化と共有版の併存、入口内への本体移動、クラス間重複排除、有限探索、列挙順・cache有無、倍率変更、使用時Loan |
| 生成物 | 直接化、load数、context bytes、固定frameのprobe・unwind、入口融合、後段最適化後のコード量 |

生成レポートでは共有・固定・必須分離の理由、候補の費用・利益と非採用理由、予算消費、入口／body／adapter数、context・metadata bytes、frame量を示す。資源不足は上限種別・設定値・消費量・定義位置・増殖経路を有限長で示す。

実行時間、コード量、frame量、コンパイル時間・最大メモリを測り、結果・効果順・cleanupの一致を確認する。平坦化によるデータ増加、具体入口のABI適応コスト、意味計画cacheの効果も実測する。現時点でこれらの実装・性能検証は完了していない。

## 第III部　将来導入時に決める事項

### 10. 未導入の機能

- **Object cache:** 機械語再生成の費用を実測して必要になった段階で、保存形式・生成物キー・依存検証を定義する。今回の意味計画cacheに、永続contextグラフやCode Cache Keyの保存規則を持ち込まない。
- **未知代入の動的格納:** 別コンパイル等で必要になった段階で、配置計算、動的stack／heap、上限、確保失敗と本文の効果順序を一緒に定義する。失敗順を無条件に未規定としない。
- **局所的な最適化ヒント:** ホットな箇所の特殊化不足・不要複製を実測し、全体倍率では他の箇所のコード量・コンパイル時間が許容範囲を外れる場合に再検討する。追加する場合も無視して正しいヒントとし、意味や資源上限を変えない。

今回は特殊化を要求・禁止する構文を追加しない。倍率と生成レポートを使い、既存の`specialize func`は静的実装選択として維持する。

backend上の証明条件は[LLVMの引数属性](https://releases.llvm.org/22.1.0/docs/LangRef.html#parameter-attributes)、[メモリ転送](https://releases.llvm.org/22.1.0/docs/LangRef.html#llvm-memcpy-intrinsic)、[整数乗算](https://releases.llvm.org/22.1.0/docs/LangRef.html#mul-instruction)に従う。固定frameの生成は[Windows prologとstack probe](https://learn.microsoft.com/en-us/cpp/build/prolog-and-epilog?view=msvc-170)も確認する。
