- [ ] csharpier設定変更
- [ ] テストの整理(名前変更、不要なものの削除)
- [ ]

## 初回の調査・修正

GPT-6 Luna のサブエージェント2体が初回の調査、修正、計測を実施した。各ファイルに原因、修正方針、実施結果、残課題を記録した。

| 記録 | 実施結果 |
|---|---|
| [ランタイム](performance-runtime-initial.md) | null検索時の例外を修正。CopyToを型付き一時配列とArray.Copyに変更し、wrap失敗時に宛先を変更しない挙動を維持 |
| [ジェネレーター](performance-generator-initial.md) | 出力分割時の分類を1回に集約。アロケーションは微減。生成ファイル数・コード量・ハッシュは変化なし |

初回は両変更を含むReleaseビルドで関連テスト10件が成功した。CopyToの短時間計測では平均処理時間が減少したが、割当量は変わらなかった。この時点では生成コード量を削減しておらず、ジェネレーター全体の速度向上も確認できなかった。

`todo/` はローカルの `.git/info/exclude` で除外されているため、これらの記録は作業ディレクトリに保存されるがGitの変更一覧には表示されない。01〜11のリンク先は開始時点では存在せず、追加作業で現行コードを確認して作成した。

## 生成コードとランタイムの性能整理

追加作業では11項目すべてを対象に、GPT-6 Luna のサブエージェント3体が調査、実装、個別検証を実施した。4項目を変更し、残る7項目は既存対策または互換性の根拠から現状を維持した。全変更を組み合わせたLinux上のmanagedテスト2,882件とNativeAOTテスト51件、フォーマット、パッケージ利用側の検証が成功した。実行条件とWindows未実行の範囲は[統合検証記録](performance-all-validation.md)に記載した。

各項目では原因と契約を確認し、修正可能な問題を実装し、回帰確認と計測結果を記録した。既に対処済みの候補や互換性を壊す変更についても、その判断を支えるコードと計測結果を残した。

| 項目 | 優先度 | 対応結果 |
|---|---|---|
| [01 Keyed Patch の構築と変換](performance-01-keyed-patch-construction.md) | 高 | 既に線形。TryAdd案はnetstandard2.0互換性と評価順序のため不採用。重複検証の共通化は07で実施 |
| [02 EditSession のスナップショット共有](performance-02-session-snapshots.md) | 高 | 既存の共有と未通知変更の検出を確認。変更なし |
| [03 適用処理の中間生成](performance-03-apply-materialisation.md) | 高 | 既存の直接書き込みとリスト再利用を確認。新規モデル・スナップショットの契約を維持 |
| [04 差分 traversal の用途別出力](performance-04-change-traversal.md) | 高 | path専用sinkと早期終了が実装済み。変更なし |
| [05 collection transition projection](performance-05-transition-projections.md) | 高 | granular投影のキャッシュが実装済み。whole投影の独立性を維持 |
| [06 ValidationStore の照会](performance-06-validation-queries.md) | 中 | 型付き照会・名前照会・全体ソートのキャッシュを実装。パス変更で無効化 |
| [07 モデル非依存処理の共有](performance-07-generated-code-sharing.md) | 中 | 重複キーの検証を生成済み共通ファイルへ集約。値の評価順序と古いTFMへの対応を維持 |
| [08 Mixed composition の最終圧縮](performance-08-mixed-compaction.md) | 中 | 既にbitmapによる線形圧縮。順序を維持して変更なし |
| [09 ObservableList の所属確認](performance-09-observable-membership.md) | 中 | 一括削除時のremoved-key setを除去。重複参照とrawアクセス後の検証を維持 |
| [10 descriptor のキー索引](performance-10-descriptor-key-index.md) | 中 | 未通知のキー変更を見落とす永続索引は不採用。live scanの契約を維持 |
| [11 SparsePath の prefix 保持](performance-11-path-prefix-storage.md) | 中 | 未読パスのprefix共有、遅延展開、展開後の配列切り離し、bulk appendを実装 |

性能値は各記録のワークロードに限定する。短時間計測の差をライブラリ全体の高速化率として扱わない。07は16 keyedモデルで4,652バイトを削減する一方、1モデルでは共通処理の固定費により463バイト増える。生成ファイル数は変わらない。

## 実装時の共通条件

- 生成物だけを手修正せず、対応する emitter と runtime を変更する。必要に応じて生成コード例を更新する。
- 既存の比較、presence、順序、例外、所有権、未通知変更の契約を維持する。速度のために検証を省略しない。
- Shared は product-neutral のまま、既存の config／dialect を通して製品固有の型名を渡す。公開生成 API の変更は downstream への影響を確認し、public コードの XML docs を維持する。
- Shared の変更では SparseTypes runtime を参照しない isolated consumer probe を実施する。consumer-facing requirements が変わる場合は Shared README を更新する。
- C# の変更は CSharpier で整形し、`dotnet csharpier check` と変更範囲に対応する既存の検証を実行する。
- 時間と allocation に加え、入力サイズに対する増え方を確認する。共通化では生成ソース、追加共通実装、必要に応じて IL とビルド時間も比較する。