# 検証の起動と証拠

これは自動実装用の起動ガイドです。現在のglobal.json、csproj、各スクリプトのparamを確認し、変更時は現行設定を優先してください。件数は固定せず、実際の成功・失敗・skip・0件を記録します。全検証を各段階で繰り返す指示ではありません。

## Managed

global.jsonはMicrosoft.Testing.Platformを選択しています。VSTest用の引数を無条件に流用しないでください。実装計画§2.2で成功した起動形は次です。

~~~powershell
dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore
dotnet build Kimigayo.slnx -c Release --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-build --no-restore
~~~

- --no-restoreは必要な依存が復元済みのときだけ使用する。依存変更・clean環境では先にrestoreし、失敗を製品回帰と混同しない。
- --no-buildは対象構成の現行ソースのビルド成功後だけ使用する。ビルド失敗後に古いDLLをテストしない。
- 近傍テストの選別・report引数は現行runnerのhelpで確認する。起動失敗や0件を「テスト成功」にしない。追加report引数でexit 5・0件になった既知例は計画§2.2にある。
- Debug/Releaseのsuiteは同時実行しない。scalar fixture出力先を共有するため、上書き競合が生じる。native検証とfixture生成も直列に行う。

## LLVM / 通常native / 統合

~~~powershell
pwsh -NoProfile -File ./backend/windows-x64/test-emission.ps1 -Configuration Release
pwsh -NoProfile -File ./backend/windows-x64/test-scalars.ps1
pwsh -NoProfile -File ./backend/windows-x64/test-cli.ps1 -Configuration Release
pwsh -NoProfile -File ./backend/windows-x64/test-lsp.ps1 -CompilerPath Kimi/bin/Release/net10.0/Kimi.dll
pwsh -NoProfile -File ./backend/windows-x64/test-artifact-paths.ps1
pwsh -NoProfile -File ./backend/windows-x64/test-toolchain.ps1
pwsh -NoProfile -File ./backend/windows-x64/test-kernel32.ps1
node --check KimiCode/extension.js
~~~

- toolchain/のLLVMと採用backendを使う。backend検証候補はbackend/windows-x64/bin/を使うスクリプトもあるので、前提不足なら既存setup/buildの手順を確認する。通常.NET buildはbackendを再生成しない。
- managed suiteが生成したbin/scalar-fixturesとbin/emission-fixtures/構成を使う。出力先には以前のfixtureが残り得る。今回生成された入力一覧とhash、期待stdout/stderr/exitを保存し、nativeで実行した入力と対応付ける。古いfixtureの混入を全件成功の根拠にしない。
- 全件の完成監査では、生成出力だけを安全な別名の退避先へ移すか隔離作業ツリーを用意して、空の出力先からmanaged suiteで再生成する。移動元・退避先の絶対パスが対象repoの生成領域内であることを検証し、元のソースや既存の未コミット変更を消さない。
- 近傍のscalar検証は既存の-FixturePatternで対象を絞れるが、一致0件を成功にしない。全件の完成監査では絞り込まない。
- node --checkは構文確認だけで、VS Code拡張の起動・通信・診断統合を証明しない。
- NativeAOTのpublish・テストは明示指定時だけ。上記のLLVM/native検証とは別物。

## 引継ぎ

対象計画ID、構成、コマンド、終了コード、実際の件数・skip・警告、入力hash、ログの絶対パスをSTATUSの現在の引継ぎとevidenceへ残す。実行前にCurrent evidence prefixに対応するログ名を決め、長いログの全文を会話へ貼らない。失敗時は最初の原因と周辺だけを取り出す。変更のない成功済み検証の再実行は、全体の完成監査または未解決の懸念に必要な場合に限る。
