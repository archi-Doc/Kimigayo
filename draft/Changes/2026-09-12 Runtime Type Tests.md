# 実行時 is / is not のBinding

状態：SPEC §13.6.1の具体的なstruct Coreを対象とする型検査と、制御フロー解析への接続を実装。Flow Type refinement、オブジェクトのLoan・破棄検証、Lowering、実行は対象外。

## 表現と解析の接続

Parserは構文文脈で選んだ実行時テストにIsRuntimeTestを設定する。is notの否定はIsNegatedとして保持し、右辺には型構文だけを置く。制約のRequirement Testは従来のNotKotoを含む要件式を維持する。BinaryKotoの共通writerはInfixText経由でis notを書き戻す。

Bindingは左辺を通常の式、右辺を既存BindTypeで解決する。型名、修飾名、alias、型引数とそのアクセス検査は既存処理を使い、左辺から対象型の引数を推論しない。BoundRuntimeTestはIsKoto上のnullable値型として、元の完全なOperandType、TargetTypeと共有アクセスの要件を保持する。型と構文のIdentityは共有し、別の木、依存追跡、Binding世代は追加しない。IndexVisitorが既存のBoundConstraintと同じ時点で無効化する。両結果は同じノードに共存しない。

実行時テストの式型はbool。BindingのResultEvidence、構文段階の型情報、ControlFlowAnalysisの型推論でも同じ型を供給する。型が分かることと対象型の検証完了は別であり、Bindingの証拠がなければ制御フロー解析はPendingBindingを残す。

ControlFlowAnalysis、StructuralCompletion、結果転送の事前収集は左辺だけを実行式として走査する。右辺には値の評価・bool制約・転送元を作らない。左辺が非継続なら真偽の経路も作らず、転送とcleanupによる非継続を保持する。静的な型関係から条件を定数化して経路を削らない。

IsEffectFreeは解決済みテストのローカル／引数名の共有読取りを扱う。呼出しやPropertyの評価を無効果と推測しない。型の右辺は走査しない。

## 境界に関する判断

| ケース | 今回の扱いと理由 |
| --- | --- |
| obj/S、rc/S、arc/S、objref/S、objuniq/S | Sが具体的struct Coreなら受理。Semantics、Originを保持し、暗黙の参照外しはしない。 |
| 普通の所有値、値借用、ポインター、数値、Tuple、enum | 実行時テストの左辺として拒否。ref越しのハンドルも含む。 |
| objref/Contract | 現行のオブジェクト型形成自体が未対応。テストによって受理範囲を拡張しない。 |
| obj/T、Dog<T>など依存型が残るもの | Unsupported。再帰的に型引数も調べる。言語全体の総称型規則を変更するものではなく、今回の具体型サブセットの境界。 |
| 非総称struct内のSelf | 既存SelfTypeにより具体的structへ解決できれば受理。総称Selfの未解決引数はUnsupported。 |
| Dog<i32>など具体的な型引数 | 受理。型引数も通常の型Bindingとアクセス検査を通す。 |
| 引数なしの総称型名 | 既存の不完全型の診断で拒否。左辺から推論しない。 |
| T.Itemなど関連型 | 修飾されたstruct名とは扱わない。未解決projectionと関連型Symbolを検出してUnsupported。 |
| Semantics／Origin付き対象、非struct対象 | 拒否。右辺の文法は拡張せず、既存の構文または型診断を用いる。 |
| x is obj/Dog | 現行優先順位では(x is obj) / Dog。型テストの対象はobjという名前で、Semantics付き型として吸収しない。Bindingで拒否する。i32やTupleの直接記述は既存の構文診断。 |
| Neverの左辺 | §3.8のNever fittingに従って受理。対象型と転送オペランドの型検査は省略せず、仮のオブジェクト値や真偽経路は作らない。 |
| not x is Dog | §13.1どおり(not x) is Dog。全体の否定はx is not Dogまたはnot (x is Dog)で書く。 |
| 総称関数の先頭制約位置のx is Dog | ParserがRequirement Testとして扱い、主語が不正なら構文診断。非総称関数の通常本文では実行時テスト。 |

自明なテストや矛盾に対する任意の警告は追加しない。require後のメンバー検索やandの右辺での型の絞り込みはまだ行わない。SPECのrefinementを要する例を、この単位で受理したとは扱わない。以上は実装範囲の記録であり、SPECの規範文言は変更しない。

## 所有権の安全網

OwnershipAnalysisにはis専用の入口を置く。左辺をBorrow用途で一度だけ評価し、テスト自身をSourceとするUnsupportedを記録する。右辺をLocalで検索しない。左辺に含まれる呼出し・引数のMove・一時値cleanupは保持する。共有アクセスが必要であるというBinding結果は、Loanの検証完了を意味しない。

SupportsTypeのオブジェクト制限は維持する。実行時テストを含む関数に、所有権のIsVerifiedや実行許可を与えない。既存の到達不能継続、実行辺、GetInputState、Placement、実行用Finalizeの分離も変更しない。

## 検証と性能

RuntimeTypeTestに56ケースを追加。5種類のオブジェクトSemantics、否定とround-trip、bool推論、アクセス、具体／依存型、Self、関連型、構文文脈、alias変更・AST置換後の再Bind、Never、cleanupによる非継続、短絡、到達不能な結果制約、左辺の単一評価、明示的な所有権Unsupportedを確認する。旧NotKotoを期待した実行時テストの既存アサートを更新し、制約のNotKoto検査とdirectiveのis拒否テストは維持する。

1・32・128個のテスト式について、warm Binding、再利用した制御フロー解析、両者の組合せが0 bytesであることを要求する。結果は値型で保持し、既存の型interning、scratch、ノードと解析状態の再利用を使う。BindingBenchmarkにRuntimeTypeTestsシナリオを追加したが、throughputの測定値や改善率は主張しない。

検証結果：Debug・Releaseとも全2,762件が成功。上記warm測定はすべて0 bytes。Releaseソリューションビルドは警告0・エラー0。git diff --checkも成功。
