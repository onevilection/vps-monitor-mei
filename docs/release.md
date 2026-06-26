# リリース手順

公開物は **`main` の良コミットに付けた SemVer タグ（`vX.Y.Z`）** から作る（設計 §10.3 / §15.4）。
リリース成果物は 2 系統で、**生成・添付の方法が異なる**:

| 成果物 | ビルド | Release への添付 |
|---|---|---|
| `agent`（Go・amd64/arm64）＋ 各 `.sha256` | タグ push で **CI が自動**（[`.github/workflows/release.yml`](../.github/workflows/release.yml)） | **自動** |
| `VpsWatcher.App.exe`（WPF self-contained）＋ `.sha256` | **ローカルで手動発行** | **手動アップロード** |

> Windows クライアントの自動ビルドは CI に追加していない（**(b) 手動添付方式**）。クライアントは発行者がローカルで発行・検証し、Release に手動添付する。

---

## リリースの流れ（発行者が実行）

前提: リリースしたい変更が `main` にマージ済みで、`main` がビルド可能な状態。

### 1. タグを打って push（agent の自動ビルド＋Release 生成）

```sh
git switch main
git pull
git tag v0.1.0          # SemVer。既存タグと重複しないこと
git push origin v0.1.0
```

タグ push で `release.yml` が発火し、`agent-linux-amd64` / `agent-linux-arm64` と各 `.sha256` をビルドして、そのタグの GitHub Release を生成・添付する。Release は `generate_release_notes: true` により自動でノートが付く。

### 2. クライアント exe をローカル発行

Windows で（リポジトリのクライアントを発行）:

```sh
# ビルド & テストを通してから発行する
dotnet build  client/VpsWatcher.sln -c Release
dotnet test   client/VpsWatcher.sln

# self-contained 単一 exe（win-x64・.NET 同梱）を発行
dotnet publish client/VpsWatcher.App -p:PublishProfile=win-x64
```

発行物:

```
client/VpsWatcher.App/bin/Release/net8.0-windows/win-x64/publish/VpsWatcher.App.exe
```

> ⚠️ 発行物バイナリ（exe）はリポジトリにコミットしない（`bin/`・`publish/` は `.gitignore` 済み）。

### 3. SHA256 を生成

発行先ディレクトリで、agent 側（`sha256sum` 形式）と揃えてハッシュ値のみのファイルを作る。PowerShell で:

```powershell
cd client\VpsWatcher.App\bin\Release\net8.0-windows\win-x64\publish
(Get-FileHash VpsWatcher.App.exe -Algorithm SHA256).Hash.ToLower() | Out-File -Encoding ascii -NoNewline VpsWatcher.App.exe.sha256
```

これで `VpsWatcher.App.exe.sha256`（中身はハッシュ値）が同じフォルダに生成される。利用者は README の手順でこの値と照合する。

### 4. Release に手動添付

手順 1 で生成された **v0.1.0 の GitHub Release** に、手順 2–3 の 2 ファイルを手動アップロードする:

- `VpsWatcher.App.exe`
- `VpsWatcher.App.exe.sha256`

`gh` CLI を使う場合（`gh release upload` は承認が必要な操作）:

```sh
gh release upload v0.1.0 \
  "client/VpsWatcher.App/bin/Release/net8.0-windows/win-x64/publish/VpsWatcher.App.exe" \
  "client/VpsWatcher.App/bin/Release/net8.0-windows/win-x64/publish/VpsWatcher.App.exe.sha256"
```

または GitHub の Release 編集画面にドラッグ＆ドロップで添付する。

---

## チェックリスト

- [ ] `main` がビルド可能（`dotnet build` / `dotnet test` / `go test` が通る）
- [ ] csproj の `<Version>` がタグと一致（例 `0.1.0` ↔ `v0.1.0`）
- [ ] タグ push 後、CI が agent 4 ファイル（bin×2 + sha256×2）を添付した Release を生成
- [ ] クライアント exe を発行し、`.sha256` を生成
- [ ] exe ＋ `.sha256` を同じ Release に手動添付
- [ ] 実 `servers.json`・鍵・発行物バイナリをコミットしていない
