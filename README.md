# VPS監視-Mei

N台の Ubuntu VPS の CPU / メモリ / ストレージ / ネットワークを、Windows デスクトップに常駐するガジェットで **1Hz リアルタイム表示**する監視アプリ。状態をキャラクターの表情で示し、しきい値超過時に VOICEPEAK 音声で警告する。

- 監視される側に負担をかけない **低負荷最優先**設計（アイドル時 CPU ほぼ 0%）。
- 公開ポートを増やさず、既存 **SSH（鍵認証・forced-command）**のみで到達。
- 監視対象は設定追加だけで増やせ（N台可変）、パネルはドラッグで並べ替え可能。

> 詳細仕様は [`docs/design.md`](docs/design.md)（設計書・正典）を参照。

---

## 構成（2コンポーネント）

| ディレクトリ | 内容 | 技術 | 配布形態 |
|---|---|---|---|
| [`agent/`](agent/) | サーバ側エージェント。`/proc`・`statfs` を直接読み、NDJSON を 1行/秒で stdout へ流すステートレスなストリーム。 | **Go 静的バイナリ**（`CGO_ENABLED=0`） | GitHub Releases（amd64 / arm64） |
| [`client/`](client/) | Windows クライアント。SSH exec で各 VPS の stdout を受け、数値・バー・キャラ表情・音声で表示。 | **C# / .NET LTS / WPF** | GitHub Releases（self-contained 単一 exe） |

両者が交換するデータ形式は [`docs/ndjson-schema.md`](docs/ndjson-schema.md) ＋ [`testdata/sample.ndjson`](testdata/sample.ndjson) を**単一の真実**として固定する。

---

## 導入（概要・追って充実）

> ⚠️ 本セクションは骨子。実バイナリ公開（GitHub Releases）後に手順を確定する。

### サーバ側エージェント（Go）

GitHub Releases から取得し、SHA256 で改ざん検証してから配置する:

まず自分の VPS のアーキテクチャを確認する（`uname -m` の結果が `x86_64` なら amd64、`aarch64` なら arm64）。以下は **amd64** の例。arm64 の場合は `agent-linux-amd64` を `agent-linux-arm64` に読み替える（`.sha256` も同様）。

```sh
cd /tmp
# バイナリと SHA256 を「元のファイル名のまま」両方ダウンロード
curl -fsSLO https://github.com/onevilection/vps-monitor-mei/releases/latest/download/agent-linux-amd64
curl -fsSLO https://github.com/onevilection/vps-monitor-mei/releases/latest/download/agent-linux-amd64.sha256

# 改ざん検証（.sha256 内のファイル名と一致するので検証が通る）
sha256sum -c agent-linux-amd64.sha256

# 検証が OK になってから配置（実行権限付与も同時）
sudo install -m 755 agent-linux-amd64 /opt/vpswatcher/agent
```

arm64（aarch64）の場合は、上記の `agent-linux-amd64` を `agent-linux-arm64` に読み替えて同じ手順を実行する。

- エージェントは **listen しない**。到達経路は SSH のみ。
- 監視用公開鍵は `authorized_keys` の **forced-command** でエージェント起動のみに束縛する（鍵が漏れてもシェルを取らせない）。詳細は設計書 §4。

### Windows クライアント（WPF）

GitHub Releases から self-contained 単一 exe をダウンロードして実行する。

- ⚠️ **SmartScreen 警告**: 署名なし exe のため初回起動時に保護警告が出る。自分用／少人数なら「詳細情報 → 実行」で続行可。
- ⚠️ **サイズ**: .NET ランタイム同梱のため 100MB 超になる（ゼロインストールとのトレードオフ）。

---

## 設定

- サーバ定義: `%APPDATA%\VpsWatcher\servers.json`（テンプレートは [`servers.example.json`](servers.example.json)）。
- アプリ設定・UI状態（最前面ON/OFF・並べ替え順・ウィンドウ位置）: `%APPDATA%\VpsWatcher\state.json`。
- **実サーバの IP・SSH 鍵・ホスト鍵・実 `servers.json` はリポジトリにコミットしない**（公開リポジトリ）。リポジトリにはダミー値の `servers.example.json` のみ置く。

---

## 開発

- ランタイムで分業: `agent/` は Linux（dev VPS）で、`client/` は Windows で開発・テスト（設計書 §15）。
- ブランチモデルは **GitHub Flow**。`main` は常にビルド可能な統合ブランチ。公開物は `main` の良コミットに付けた SemVer タグ（`vX.Y.Z`）から CI が生成する Release。
- スキーマ契約に触れる変更は `docs/ndjson-schema.md` と `testdata/sample.ndjson` を**同時更新**し、両側テストを通すこと。

---

## ライセンス

本体 MIT（予定）。依存 `gong-wpf-dragdrop`（BSD-3-Clause）・`OxyPlot`（MIT）と両立。詳細は [`LICENSE`](LICENSE)。
