# Inherited receiver Binding

SPEC §7.3、§8.4.5、§9.5〜9.5.1、§10.1〜10.4に沿って、継承memberのlookup、receiver対応、関数・Propertyの適合対応を拡張した。

## Lookupとreceiver

- 通常callと関数・Property要求で同じ`LookupTypeMember`を使う。名前空間のroleとアクセス権を満たす最初の層を選択し、その層の候補だけを検証する。アクセス不能な宣言やType側の名前だけではValue lookupを停止しない。
- instance/type関数は同じValue roleで停止する。receiver不適合、引数不適合、条件不成立、accessorのアクセス違反によって基底へ戻らない。実装適合の実効アクセス範囲も、選択後に検証する。
- protectedは投影前の静的receiverと利用箇所の派生Typeで判定する。基底subobjectのTypeに置き換えてアクセスを再判定しない。囲む宣言のアクセス範囲も確認する。
- struct／enum／Contract関数の`self`の位置をSymbolへ保持する。receiver宣言の完全Typeと、名前・省略・defaultの制限を検証する。group関数の同名引数は通常引数として扱う。
- 値からのmember callはreceiverを独立した先行評価源として一つ保持し、宣言上の`self`位置へ対応付ける。Type-qualified callは非束縛callとして、`self`も含めた明示引数を通常の順序で対応付ける。非修飾instance callにreceiverを補わない。

## 操作記録と候補選択

- `BoundCall`にlookupのbase path、宣言元の構築済みType、Type／Origin代入、receiver操作、ソース引数順の操作配列を保持する。各操作は元の式とType、代入済みparameter Type、parameter slotを保持する。非束縛callのlookup経路と、値receiverを投影する操作は区別する。
- 引数adaptationは通常の型代入・Origin対応を使い、Exact、literal、同Semanticsのreborrow、異Semanticsのborrow/reborrowを記録する。継承の投影はmember receiverに限定し、通常引数にderived/base変換やowningのslicingを追加しない。
- Best Candidateは同じソース式ごとにadaptationを比較する。引数間で優劣が逆転した場合は比較不能とする。同順位では操作不要のType関係、関数自身のgeneric有無、実際に使用するdefault数の順で比較する。継承の深さや条件の強さは順位に使わない。
- standard Propertyの基底storage投影は、whole-base borrowと別の操作として保持する。共有receiver経由の更新を拒否し、元receiverのSemanticsとOriginを保持する。
- borrowed method／custom accessorの基底投影はwhole-base borrowとして記録する。`TryGetReceiverOperation`は選択済みだが証明保留の操作も返すため、呼び出し可能であることの証拠には使えない。

## 適合と証明の境界

- 関数要求の実装特定では通常callの順位付けを使わない。基底実装の`Self = Base`を維持し、要求receiverの対応だけを別に検証する。通常のparameterや結果の`Base`を派生Typeへ書き換えない。
- `BoundFunctionWitness`に宣言元Type、要求／実装receiver、base path、Origin対応、ObjectCompatibleの証明状態を保持する。祖先Contractへの転送でも対応全体を保持する。Propertyは既存の操作witnessを拡張する。
- 複数の適合経路は、member Identityに加えて代入、receiver対応、Origin対応、正規化した各base pathのTypeを比較する。member条件は宣言元のbase引数で代入し、要求を検証する環境で証明する。
- **ObjectCompatibleの本体・callee・返却Loanの効果解析は未実装。** 今回は共通の証明入口を用意し、signatureや空のbodyを証拠にしない。基底全体をborrowするcall/accessorと適合はUnknownを保持し、最終Bindではエラーになる。通常callでは勝者決定後にこの義務を確認するため、証明失敗による候補の選び直しも行わない。
- standard storageによるwitnessにはwhole-base借用の証明を要求しない。未検証の適合経路から実装を公開しない既存の利用時APIを維持する。

## 再利用と検証

- 各候補の適用可能性と操作をBinding専用バッファへ保存し、Best Candidate比較中に再探索・再証明しない。確定した意味情報は再利用配列へコピーし、一時バッファを保持しない。base pathと関数witnessもIdentityをキーに再利用する。
- 再Bindでreceiver位置と現在の操作表をリセットし、callと適合の有効性を再構築する。アクセス権・receiver Type・base引数の変更と復元を検証した。
- 追加56ケースで、lookupの停止、Type/Valueの曖昧性、任意のself位置、非束縛call、候補順序、adaptationの優劣、多段の完全Type／Origin代入、protected判定、storageとwhole-base borrowの区別、継承witness、経路の整合性、再Bindを検証した。
- 1・32・512型、条件付き適合、継承関数／Property、overload、入れ子callを含むwarm Bindを8回測定し、追加割当0バイトを確認した。Benchmarkに同じ構成の`InheritedReceivers`シナリオを追加した。スループットの比較測定は行っていない。
- 既存テスト2件は目的を維持して入力を修正した。type関数はType-qualified callとし、overload追加後の曖昧性テストはexpected resultによって片方が除外されない結果Typeへ揃えた。
- 最終差分でRelease／Debugとも全2,027テスト成功。BenchmarkのReleaseビルドは警告・エラー0件。`git diff --check`も成功した。

## 後続単位

Propertyを含む一般式の取得・更新・呼び出し計画、関数参照の実体化、Access Effect／ObjectCompatibleの証明、CFGのownership／lifetime・cleanup解析を進める。今回の`Value`操作は、後続のCopy／MoveやLoan・初期化の合法性を証明するものではない。witnessの宣言適合もbodyのownership検証完了を意味しない。
