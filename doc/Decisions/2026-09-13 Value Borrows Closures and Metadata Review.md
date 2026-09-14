# 値の借用・Closure・metadata 修正案の採否

2026-09-13。[設計仕様](../Design/2026-09-13%20Value%20Borrows%20Closures%20and%20Metadata.md)に採用分を反映した。§1 は最初の修正案、§2 は追加レビューに対応し、後の決定を優先する。SPEC.md と STATUS.md は今回の編集対象に含めない。

## 1. 最初の修正案

初回の判断を記録する。P6 の slot receiver と性能案1の noalias 見送りは、§2 で改訂した。

| 提案 | 判断・補正 |
| --- | --- |
| P1 | 採用。転送 entry・ByteMove/ByteCopy を削除。memmove も移動量に比例し、O(1) にはならない |
| P2 | 採用。NoDestroy と導出可能な allocationSize を削除。処理不要は null、HasCopy は保持 |
| P3 | 採用。範囲破棄を一種類に統一。部分状態・非連続の論理順は caller が分解 |
| P4 | 採用。破棄・解放へ location を伝達。call body は自身の位置、adapter の後始末も元操作へ対応 |
| P5 | 配置対象の統一を採用。全 offset は TypeLayout に必須、runtime 表の常時生成は不要な容量・再配置情報を増やすため不採用 |
| P6 | inline 条件と slot receiver を採用。操作表は E に加えて call 契約・ABI も区別。一般のゼロサイズ値借用の backing は引き続き必要 |
| P7 | 採用。先頭を entry/context に統一。異なる receiver 契約まで同一視しない |
| P8 | 直接の Closure 式は採用。一般の source 式・代入先の評価をまたぐ確保前倒しは不採用 |
| P9 | 採用。hash 照合は compile 時、allocator は16-byte alignment、増大再帰は既知の生成制限と明記 |
| 性能案1 | 共通証明による属性付与を採用。ref/uniq への noalias の一律付与は LLVM の期間・alias 条件の証明にならないため不採用 |
| 性能案2 | 条件付き採用。異なる destructor を有無だけで統合しない。残る metadata か、直接化した操作の identity を保持 |
| 性能案3 | 採用。内部 record のアドレスを非観測とし、private unnamed_addr constant を使用 |

配置例 `(u8, u64, u8)` は、並べ替え後16 bytes・記述順24 bytes。設計仕様 §3.1 の4要素例は24 bytes・32 bytesであり、両者を区別する。

## 2. 追加レビュー

| 提案 | 判断・理由 |
| --- | --- |
| noalias の共通証明 | 採用。引数・capture の Loan と呼び出し全体の static 効果要約を合わせれば、通常の ref/uniq に必要な保証を共通化できる。前回は callee 内の Loan の使用期間だけで評価していたため、見送りを撤回した。unsafe/FFI の義務と call 全体の保護を設計仕様 §6.2.2 に明記 |
| 要素ループの性能 | handle の属性だけでバッファーの非重複まで証明できる、という一般化は不採用。load した pointer の由来・範囲・要素 Loan も使って最適化する |
| entry への環境語の値渡し | 採用。F のメモリー配置を必須にせず、必要な entry だけが inline 環境を local に配置する。Shared の非所有ビューと、破棄 entry に渡す一度限りの責任を区別。具体 Closure の receiver・借用結果の規則は変更しない |
| allocationSize | 採用。必要な consumer だけが計算し、具体的な D は生成時に検査済みの定数。Free に不要な size 計算を課さない |
| null の破棄 entry | 採用。最後の strong release の疑似コードも non-null の場合だけ呼ぶ |
| 決定記録の分離 | 採用。採否表・旧配置例の訂正・同期先を本書へ移し、設計本文には現在の契約だけを置く |
| SPEC §21 の注記 | 同期対象として記録。設計の対象では設計仕様を優先するため、将来の統合時に not yet normative の注記を更新する。SPEC 本体は今回変更しない |

## 3. SPEC に反映する際の対応先

| 対応先 | 同期内容 |
| --- | --- |
| §21 冒頭 | 未採用 draft という注記を、採用済みの設計範囲と実装状況を区別する記述へ更新 |
| §21.1.1・§21.1.3・§21.1.5 | byte 転送・範囲再配置と集約配置の共通規則、base・tag・C layout の固定部分、型代入後の offset |
| §21.2・§21.3 | 型 token、48-byte ValueMetadata、24-byte ObjectDescriptor、範囲破棄、context schema・生成制限 |
| §21.4.2 | 小環境を inline 格納する16-byte 共通関数値、24-byte FunctionOperations、環境語を受け取る entry。物理 ABI は引き続き compiler が選ぶ |
| §15.6.4・§21.5.5、unsafe/FFI の契約 | call 全体の借用保護と効果要約に基づく属性の共通証明、独立経路のアクセス禁止、対象外の内部 pointer との区別 |
| §22.5 の診断位置 | metadata 経由の破棄・解放へ元操作の location を引き継ぐ |

§7.6 の capture 取得順・呼び出し条件と、共通関数値の Owned 環境・Shared 呼び出し・Non-Copy は維持する。object header の統一は [Weak・object runtime 設計](../Design/2026-09-13%20Weak%20References%20and%20Object%20Runtime.md)を正本とし、同書 §6 は descriptor 形式を値借用設計へ委ねているため追加のサイズ訂正は不要。
