# 到達不能コードの所有権検査継続

状態：明示的転送を起点とする、字句的な本文内の検査継続を実装。SPEC §14.10.3全体の完成ではない。

## 実装範囲

既存のwhole-Place、具体的enum構築・分解、Copy/Move、初期化、let代入履歴、deferの状態検査を、明示的なreturn/yield/exit/continue後のソースにも適用する。
Bindingの名前・結果型検査とresult-source収集は従来どおり行う。Flow Type refinement、Loan、借用match、一般Partial Move、Loweringはこの変更に含めない。

## 採用した経路と合流の判断

実行用EdgeStorageには検査継続の辺を追加しない。各operationに検査regionのIdentityを対応付け、別のregion表にSeedとEntryを保存する。構文木、Place Identity、宣言Identityは共有する。

| 場面 | 検査状態の扱い |
| --- | --- |
| 明示的転送の直後 | オペランド評価・取得後、当該転送のcleanup前の状態をSeedとする。返す値をMoveした事実は保持する。 |
| 連続する転送 | 直前のregionの収束状態を起点に、新しいregionを作る。operationを挟まない裸の転送は未使用のSeedを共有できる。 |
| 通常のif/match/短絡分岐 | 同じregionの通常辺だけを使い、初期化のmustは共通部分、may・Move・代入履歴は和集合で合流する。 |
| 分岐内で転送した後のソース | 子regionに属する。本文末尾まで検査しても、親の通常のjoinへ戻さない。 |
| 到達不能領域のrequire | 成功側は条件のtrue状態。failure内の転送後の子regionはsuccessに合流しない。転送を通常完了と解釈しない。 |
| 継続内に書かれたloop/while | そのregion内の通常backedge、continue、exitを使い固定点を解く。繰返しで消費済みになる値は拒否する。 |
| ループ本文の転送後ソース | 子regionの本文末尾から外側backedgeやexitへ状態を戻さない。 |
| 関数/deferの転送 | 既存の転送先解決と境界を維持する。deferの自己exitは通常の受領先へ進み、その後に書かれたソースは子regionで検査する。 |
| 検査regionの本文末尾 | 残りの字句的cleanupも検査するが、実行用の破棄計画は確定しない。本文を出ると親regionへ構築contextを戻す。子の状態自体は戻さない。 |

例えば、return後のrequire failureでxをMoveしてreturnした場合、require後のxは成功側の状態で検査する。failureのreturn後にもソースがあれば、それはfailureの子regionで引き続き検査する。この子regionからrequire後への経路は存在しない。

## 非継続の起点に関する判断

SPEC §14.10.3が明記する転送前cleanup状態を実装の出発点とした。次の箇所に、都合のよい状態や仮の通常辺を追加していない。

- Never呼出し後：呼出しには正常な復帰状態がない。引数取得前への巻戻しや、引数のMoveを消す処理はしない。
- exitのないloop後：反復の任意の状態を脱出状態に流用しない。
- cleanupで配送が止まるdoなどの後：実行されない結果配送・外側スコープへの復帰を作らない。ただし、その内部の明示的転送に続くソースはcleanup前のSeedから検査できる。
- すべての分岐が転送するif/matchの外側：複数の字句的regionを結合する一般的な非継続joinは今回実装していない。各分岐内の転送後は検査できるが、外側の後続には既定のSeedを与えない。

これらの後続に検査状態が届かない場合、従来と同じ範囲の操作をUnsupportedにする。対象はLocal/ParameterのRead・Consume・Borrowと、BinaryKotoをSourceとするWrite。対象を合成されたresult Placeなどへ拡張しない。これは安全側の実装制限であり、言語として無効と決める変更ではない。一般的な非継続joinを実装する際には、現行SPECとの対応とrefinementの合流も併せて検証する。

関数末尾の合成Produce/Deliverには新しい検査起点を作らない。Never呼出しやloopで終わる非Unit関数も、結果未初期化として拒否しない。検査経路の診断処理はDeliverを検査せず、架空の戻り値や暗黙のUnitをresult-sourceへ追加しない。

## solverと不変条件

1. 実行CFGを従来どおり分割して固定点を解き、診断と実行用planを確定する。
2. 未検査の到達不能ソース使用がなければ、検査用solverを省略する。
3. 同じregion内の辺を使って検査用blockを作る。SeedのEntryは必ずblock入口になる。EdgeStorage、IncomingEdges、実行用block境界は変更しない。
4. regionの作成順に解く。Seedは必ず先に作られたregionのoperationを指す。実行側または親regionの収束後、block入口からSeedまでを再生し、そのoperationの後状態を子の入口に渡す。
5. 検査region内のループが収束してから診断する。実行用Finalizeは呼ばない。
6. 検査状態が届かなかった対象操作には安全網を適用する。

状態遷移、合流演算、worklist、block再生、初期化・再代入の診断は共有する。保持する状態はblock入口の4 bit lanesであり、operation×Placeの状態行列は作らない。regionを解くたびに全operationを走査し直す方式も採用していない。配列とリストは再利用する。

GetInputStateは実行上到達不能ならNoneのまま。検査専用のHasCheckingState/GetCheckingInputStateを追加し、Bindで無効化する。検査済みでも実行上到達不能なWriteのPlacementはNoneを保つ。検査用の診断走査でcleanup planやstepを追記しない。

Debugでは、到達可能な入口を持つif/do/loop/matchについて、ControlFlowAnalysisのCanCompleteNormallyと所有権CFGの出口到達可能性を照合する。また、検査regionが実行到達可能なoperationを取り込まないことと、Seedの親子順をassertする。

defer展開に由来する複数のOwnershipIssueは従来同様にoperation単位で保持する。公開診断は既存DiagnosticCollectionのソース位置による重複抑制を使い、再度のReportDiagnosticsでも重複しない。

## 性能と検証

warmテストで、既存StructuralCompletionのHashSet.UnionWithに起因する1回40 bytesの列挙子boxingを検出した。具体的HashSetを直接列挙する共有UnionTransfersに置き換え、ControlFlowAnalysis側にも同じヘルパーを使う。

UnreachableOwnershipTestは正例、具体的診断、require/分岐/ループの隔離、連続転送、enum、defer、合成結果、安全網、状態API、再Bindと再解析、公開診断の重複抑制を検証する。1・32・128段の継続についてBinding単独・両解析・Bindingと両解析の組合せを測定し、warm時のゼロ割当を要求する。

OwnershipAnalysisBenchmarkに到達不能ソースの切替えを追加し、通常経路と検査経路の両方を測れるようにした。この変更だけからthroughputの改善は主張しない。

検証結果：63ケースを追加し、既存のUnsupported期待2ケースを具体的診断へ更新。Debug/Releaseとも全2,700件成功。1・32・128段のwarm測定はBinding単独・両解析・組合せのすべてで0 bytes。既存の割当回帰も成功。Releaseソリューションビルドは警告0・エラー0。
