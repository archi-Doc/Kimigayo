# ジェネリック共有コードと自動特殊化の設計案

2026-09-13。共有を基本とし、意味を保存する自動特殊化を有限の予算内で行う方針は採用済み。本書はその具体化案を記録する。**SPEC.md には未反映であり、本書による既存仕様の上書きはない。** 個別の追加提案と実装前の判断点は以下に区別する。実行対応を追加する変更ではない。

既存の契約は [SPEC §8.10](../../SPEC.md#810-generic-body-checking-and-deferred-obligations)、[§21.3](../../SPEC.md#213-generic-code-generation)、[§21.4](../../SPEC.md#214-checked-lowering-and-internal-abi)。ValueMetadata、TypeContext、GenericContext の骨格は既に仕様にある。本書では新たな source 構文、公開 reflection、安定した外部 ABI を追加しない。

## 1. 意味と生成を分ける

論理的な依存順を次のようにする。実際の pass 分割は固定しない。

1. 定義時に、宣言した制約の下で body の操作・型・所有権・cleanup を普遍的に検証する。
2. 使用時に引数と契約を検証し、閉じた明示 specialization 集合から実装を選ぶ。
3. 検証済みの計画へ代入し、具体的な配置・操作・効果・cleanup を確定する。
4. その意味を保存する共有／自動特殊化計画を選び、ABI と context を接続する。
5. 未解決の必須義務がない計画だけをコード生成する。

共有計画の早期解析・cache は許すが、未検証の executable body は生成しない。共有経路でも同じ明示 specialization を選び、予算不足を理由に通常実装へ戻さない。Source の lookup、overload 選択、Copy の合法性、Loan の検証を runtime metadata に委ねない。

## 2. 情報の担当と共有 ABI

| 情報 | 担当 |
| --- | --- |
| ValueMetadata | Origin のみを除いた完全な値の identity、size、alignment、flags、破棄 entry、TypeContext |
| TypeContext | 要素 metadata、論理 Field と offset の対応、型の操作・破棄に必要な追加情報 |
| witness | 静的に検証済みの Contract requirement と実装の対応 |
| callee record | 選択済み entry と、その entry 自身の context |
| GenericContext | 特定の共有 body／entry が必要とする上記レコードへの不変 pointer 列 |
| FunctionAbi | 論理 receiver・引数・結果・context と物理位置、passing mode、属性の対応 |

ValueMetadata は既存の48 bytesの形式を利用する。現仕様では stride は size と同じで、別の Copy/Move entry は置かない。型の違いだけを理由に新たな汎用操作表を増やさず、既存の値転送・破棄・callee record を使う。所有状態、部分初期化、Loan は共有 metadata の内容にしない。

**共有 ABI の初期案:** hidden context を一つ渡す。body から配置が未知の所有値は取得済みの格納領域への pointer、未知配置の結果は呼び出し側が用意した未初期化領域への pointer とする。既知の値の直接渡しは FunctionAbi が選択する。借用は既存の1 pointer表現を維持する。

同じ物理 pointer でも、所有値の引数領域、ref、uniq、結果領域は異なる論理契約として保持する。引数取得は source 順に一度だけ行い、callee entry で取得済み引数の責任を渡す。結果の責任は cleanup が正常完了して戻るときに渡す。caller の格納領域を callee が解放してはならない。Unknown-size の値を借用 pointer だけで所有値へ置き換えない。

物理順序・calling convention・slot省略・属性は言語保証にしない。定義・直接／間接 call・adapter は同じ FunctionAbi を使う。context が不要な場合の論理値は null。物理引数を省略する entry は、それを含む entry と自動的には ABI 互換にならない。

## 3. Context schema と呼び出し先

**追加提案:** 検証済み body の操作から必要情報を収集し、次の生成時 schema を作る。

- 各 slot の種別、意味を識別する静的 identity、型／長さ引数との対応。
- 参照先 metadata・witness・callee の契約。callee には FunctionAbi と参照先 context schema も含む。
- 固定した slot 順と schema identity。slot は Windows x64 では8 bytes。

情報の必要性は、body の直接操作だけでなく、呼び先・adapter・cleanup が必要とする依存も含めて求める。同一の要求は共有できるが、型や名前が同じという理由だけで別の操作を同一視しない。schema は生成側と使用側を静的に照合し、実行時の schema 探索を要求しない。

body が借用を転送するだけなら参照先 metadata は不要。callee record 経由の呼び出しだけなら、呼び先自身が必要情報を持つ限り、caller に同じ metadata を重複して渡す必要はない。未使用 slot の除去後も、producer・consumer・cache の schema を一致させる。

```text
共有 caller body:
    selected = context[0]
    selected.entry(selected.context, arguments...)

caller<i32> 用 context:
    slot 0 -> { classify_i32_entry, classify_i32_context }

caller<string> 用 context:
    slot 0 -> { classify_shared_entry, classify_string_context }
```

これは内部処理の疑似コードである。classify の entry は静的な実装選択の結果であり、引数の Dynamic Type から選ばない。接続先の schema や ABI が異なる場合は正しい record または検証済み adapter を生成する。caller の列を位置だけで流用しない。

閉じた代入の context は不変定数として生成・再利用し、呼び出しごとの context 確保や runtime の型検索を要求しない。TypeContext は呼び出し元の stack に依存せず、値の全使用・破棄まで有効でなければならない。この規則は値自身や関数環境に必要な格納領域の確保まで禁止するものではない。

## 4. 共有可能性と特殊化の単位

同じ entry を共有できるのは、選択済み実装の意味、ABI、評価順、配置アクセス、操作、所有権、失敗、cleanup のすべてを、body と渡す情報で保存できる場合である。同じ size・alignment は十分条件ではない。SharedReadResult の結果型・Semantics・Origin の相関も保持する。

生成キーは、body に埋め込む差を残す。metadata／callee record だけで扱える型の違いは body のキーから除けるが、context の内容と対応は失わない。意味の異なる選択済み実装を統合する場合は別途同値性の証明が必要であり、初期実装では実装 identity を区別する。

例えば f<P, T> で P の算術を直接生成し、T を転送・破棄にだけ使うなら、P=i32 に固定した body と T ごとの context を組み合わせられる。これは生成上の部分的な固定であり、source の部分 specialization を追加するものではない。型引数の全直積を列挙しない。

破棄が metadata 経由なら、実際の型の metadata/context を渡す。破棄を直接化するなら、正確な entry と埋め込む context を生成キーに含める。HasCopy、破棄の有無、size だけでは間接破棄を消せない。型 identity、Field offset、witness、callee にも同じ区別を適用する。

**初期の最適化優先案:** スカラーの算術・比較・変換、ループ内の間接 call／配置参照の除去、小関数のインライン化で転送を消せる箇所。これは性能の仮説であり、最終的な順位と費用式は計測で調整する。コードを増やさない直接化・定数伝播・既存計画の再利用を、新規 body 複製の予算と混同しない。

## 5. 生成予算と切り替え

| 制限 | 対象 | 到達時 |
| --- | --- | --- |
| 自動特殊化予算 | 最適化のための追加 body 数、推定コード増加量、再帰的な複製展開 | 新規の任意特殊化を止め、検証済み共有経路を使う |
| コンパイラ資源上限 | 必須 body、共有 body、metadata、context、helper、adapter、解析・生成計画全体 | 資源不足として診断する |

**追加提案:** 自動特殊化予算は元の generic 関数単位と最終生成単位全体の両方に設ける。前者は別の共有クラスを作っても同じ元関数へ課金する。型ごと・calleeごとに予算をリセットして無制限に増殖させない。共有経路の生成費用も全体資源上限へ含める。

```text
同じ有効な生成キーの計画がある      -> 再利用
新規特殊化に効果があり、予算内      -> 自動特殊化
意味を保存する共有経路がある        -> 共有 entry + context
共有では保存できず、分離が必須      -> 必須の分離生成
必要な生成が資源上限を超える        -> 資源不足の診断
```

切り替えはコンパイル時に行う。実行時の回数による tier 切り替えや JIT は追加しない。共有 fallback の成立を確認せずに依存生成を打ち切らない。共有可能性を示す計画は必要だが、特殊化で全用途を満たす場合に未使用の共有機械語まで必ず出力する必要はない。未実装の共有・操作を実装済みと扱うこともできない。

明示 specialization の選択と必須分離は任意最適化の予算では取り消せない。特殊化予算0でも、生成資源が足りる範囲で、同じ source 合法性と意味を守る。資源不足は型エラーや所有権エラーと区別する。

**再現性の追加提案:** 同じ入力・compiler/profile・設定では、列挙順や並列処理の完了順で予算配分を変えない。安定した候補順と費用モデルを記録し、cache の有無でも論理的な予算配分を変えない。具体的な費用単位や既定値は compiler の設定であり、言語仕様へ固定しない。

有限個の metadata/context キーによる再帰は、計画を先に登録して参照で閉じる。未完成の record を executable 出力として公開しない。T -> Box<T> -> Box<Box<T>> のように異なるキーが増える場合は、コードを共有できても別途生成上限が必要になる。共有に戻すだけで無限の metadata 生成が解決したとみなさない。無限の inline 値配置は既存の配置エラーである。

## 6. 観測と検証

生成レポートを compiler 機能として用意する案を推奨する。少なくとも元関数・選択済み実装、再利用／共有／特殊化／必須分離の理由、予算の費用と残量、context slot数、body・metadata・adapter の件数を確認できるようにする。通常成功時の大量出力は不要で、具体的な CLI 名やファイル形式は実装時に決める。

資源不足の診断は、上限の種類、設定値、消費量、元の定義、代表的な型／context の増殖経路を有限の長さで示す。未解決の証明・未対応機能はそれぞれ別の診断とする。

| 検証例 | 守る性質 |
| --- | --- |
| 借用の単純転送 | 不要な metadata/context を省ける |
| サイズ・破棄が異なる値の転送 | 転送・cleanup が正しく、型数だけ body が増えない |
| 同じ size で破棄の動作が異なる型 | metadata の取り違えや不正な直接化がない |
| 共有 caller と関数参照からの明示 specialization | 予算0／通常予算、最適化有無に関係なく選択が一致する |
| 複数 slot の一部だけを固定 | 全直積を作らず、未固定 slot の操作を保存する |
| SharedReadResult と独立した ref/uniq/所有引数 | 結果型・取得・Loan・ABI を混同しない |
| 有限再帰と増殖再帰 | 再利用、資源不足、無限 inline 配置を区別する |
| schema・specialization集合・cleanup依存の変更 | stale な cache と producer/consumer の混在を拒否する |
| 候補列挙順・cache有無を変えた生成 | 予算配分と生成計画が再現する |

性能計測は実行時間、生成コード量、コンパイル時間、コンパイラの最大メモリ使用量を対象とする。正しさと未実装境界の検証を先に行い、その後で予算の数値を決める。NativeAOT テストを要求する設計ではない。

## 7. 実装前に詰める事項と推奨

| 項目 | 推奨と残る判断 |
| --- | --- |
| 配置未知のローカル・一時値 | 呼び出し側の既存領域を再利用できるか先に調べる。残る領域の確保・alignment・生存期間・stack回収を具体化する。動的stack確保の上限やheapへの退避は、失敗契約も含めて別途決める。context の無確保とは別問題 |
| 長さ引数の runtime 表現 | LengthKey と選択は静的に維持。body に必要な長さは固定配列の TypeContext 等から明示的に供給する案。ゼロサイズ要素があるため size / elementStride から推定しない。専用 slot を足すかを含め、既存の pointer列schemaと整合する格納形式を決める |
| 自動特殊化の費用・既定値 | 関数ごとの追加body上限と推定コード増加量、全体上限を計測で定める。再帰の深さだけに依存しない。設定範囲とレポート形式は実装時に決める |
| 特殊化を要求／禁止する source 機能 | 今回は追加しない案。既存の明示 specialize は実装選択の機能として維持。最適化指定の必要性は計測後に判断する |
| 別コンパイル・cache の表現 | 閉じた実装集合・意味の依存・schema・ABI の照合は既存契約を守る。保存形式、symbol配置、再生成とlinkの担当は後で決める。安定した外部 generic ABI は今回追加しない |

最初の実装では共有 ABI、context schema と依存収集、検証済み共有経路を先に完成させる。その後、同じ計画からの直接化、一部slotの固定、新規body複製を順に追加する。共有コード生成を導入する前に、少なくとも未知配置の一時領域と必要な長さ情報の供給方法を具体化する。
