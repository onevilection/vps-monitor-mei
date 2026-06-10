# NDJSON スキーマ契約（単一の真実 / Single Source of Truth）

このドキュメントは **VPS監視-Mei** の agent（Go）と client（WPF）が交換するデータ形式の**正典**である。
設計書 `docs/design.md` §3.5 を契約として確定したもの。

> **契約の運用ルール（CLAUDE.md より）**
> - agent と client は別マシン・別セッションで開発されるため、**出力スキーマとパーサのズレ**が最大の事故源になる。
> - スキーマの正典は **この `docs/ndjson-schema.md` ＋ `testdata/sample.ndjson`** の2点。
> - agent 側は「`sample.ndjson` に準拠した出力を吐く」テストを持ち、client 側は「`sample.ndjson` を正しくパースする」テストを持つ。**両側が同じ fixture を参照**する。
> - スキーマを変えるときは **契約ファイル（本書 + sample.ndjson）→ 両側のテスト → 実装** の順で更新する。契約を更新せずに片側だけ実装を変えてはならない。
> - スキーマ変更は人間の承認を要する操作（CLAUDE.md「権限方針」）。整合確認にはサブエージェント `consistency-checker` を使う。

---

## 1. 転送形式

- **NDJSON**（Newline Delimited JSON）。**1行 = 1サンプル = 1個の JSON オブジェクト**。
- agent は 1Hz（毎秒1行）で stdout へ出力し、**行ごとに即時フラッシュ**する（バッファリングしない。design §3.4）。
- 文字エンコーディングは **UTF-8**。改行は `\n`（LF）。行末以外に改行・整形を含めない（1サンプルは厳密に1行）。
- client は SSH exec の stdout を**行単位**で読み、1行ごとに JSON デシリアライズする。

---

## 2. トップレベルのフィールド

| フィールド | 型 | null許容 | 説明 |
|---|---|---|---|
| `v` | integer | 不可 | スキーマバージョン。現在 **1**。互換性を壊す変更時のみ増やす（§5）。 |
| `id` | string | 不可 | サーバ識別子。N台を区別するためペイロードに**必須**。`servers.json` の `id` と一致させる。 |
| `ts` | integer | 不可 | サンプル取得時刻。**Unix epoch 秒**（UTC）。 |
| `cpu_pct` | number \| null | **可** | CPU使用率 [%], 0–100。**測定不能時 null**（§3）。 |
| `mem` | object | 不可 | メモリ。§2.1。 |
| `swap` | object | 不可 | スワップ。§2.2。 |
| `disk` | array\<object\> | 不可（空配列可） | マウントポイントごとのストレージ使用量。§2.3。 |
| `net` | object | 不可 | ネットワーク。§2.4。 |
| `load` | array\<number\> | 不可 | ロードアベレージ `[1分, 5分, 15分]`。要素数は常に3。 |
| `uptime_sec` | integer | 不可 | 稼働秒数。`/proc/uptime` 由来。 |

### 2.1 `mem`（object）

| フィールド | 型 | null許容 | 説明 |
|---|---|---|---|
| `used_pct` | number | 不可 | 使用率 [%]。`(MemTotal − MemAvailable) / MemTotal × 100`。**MemAvailable 基準**（design §3.2）。 |
| `used_mb` | integer | 不可 | 使用量 [MiB]。`(MemTotal − MemAvailable)` を MiB 換算。 |
| `total_mb` | integer | 不可 | 総量 [MiB]。`MemTotal` を MiB 換算。 |

### 2.2 `swap`（object）

| フィールド | 型 | null許容 | 説明 |
|---|---|---|---|
| `used_pct` | number | 不可 | スワップ使用率 [%]。`(SwapTotal − SwapFree) / SwapTotal × 100`。**SwapTotal が 0 のときは 0.0** を出力する（ゼロ除算回避）。 |

### 2.3 `disk`（array of object）

`mounts` 設定で指定された各マウントポイントにつき1要素。順序は設定順。

| フィールド | 型 | null許容 | 説明 |
|---|---|---|---|
| `mount` | string | 不可 | マウントポイント（例 `/`, `/var/www`）。 |
| `used_pct` | number | 不可 | 使用率 [%]。`statfs(2)` の `(f_blocks − f_bfree) / f_blocks × 100`（**実使用＝全体−空き**）。表示・しきい値判定にはこの値を用いる。 |
| `used_gb` | number | 不可 | 使用量 [GiB]。`(f_blocks − f_bavail) × f_bsize` を GiB 換算（**ユーザ視点の使用量＝全体−ユーザ利用可能**）。 |
| `total_gb` | number | 不可 | 総量 [GiB]。`f_blocks × f_bsize` を GiB 換算。 |

> 注: `used_pct` は管理者予約ブロックを含む物理使用率（`f_bfree`基準）、`used_gb` は一般ユーザの体感使用量（`f_bavail`基準）。`df` の表示流儀に合わせ、率は物理ベース・量はユーザベースとする。実装はこの定義に従うこと。

### 2.4 `net`（object）

| フィールド | 型 | null許容 | 説明 |
|---|---|---|---|
| `iface` | string | 不可 | 対象インターフェース名（例 `eth0`）。`lo` は対象外。設定指定 or デフォルトルートから自動判定（design §3.4）。 |
| `rx_bps` | number \| null | **可** | 受信レート [bytes/sec]。前回サンプルとの差分。**測定不能時 null**（§3）。 |
| `tx_bps` | number \| null | **可** | 送信レート [bytes/sec]。同上。 |

---

## 3. null の意味（最重要）

**レート系フィールド（`cpu_pct`・`net.rx_bps`・`net.tx_bps`）は「2回読んで差分」で算出するため、差分が取れないとき `null` を出力する。**

null になる条件:
1. **初回サンプル**（ループ1周目）：前回値が無く差分が取れない（design §3.4）。
2. **カウンタの巻き戻し／リセット**：差分が負になった場合（NIC再設定・再起動等）。その回のレートは `null`。
3. **CPU total 差分が 0**：`total_d <= 0` のとき `cpu_pct = null`（付録A）。

**client 側の扱い**: `null` は「測定中／不明」として表示する（0% とは区別する）。しきい値判定・グラフには `null` の点を含めない（欠測として扱う）。

> レート系以外（`mem`・`swap`・`disk`・`load`・`uptime_sec`・`ts`・`id`・`v`）は**常に値を持つ**。null を出力しない。

---

## 4. パースの約束（client 実装者向け）

- **未知のフィールドは無視**してよい（前方互換）。同一 `v` 内での**フィールド追加は互換変更**として扱う（§5）。
- 既知フィールドの**型は厳守**。数値は JSON number、整数フィールドも number として受理し、必要なら丸める。
- `disk` は**0個以上**の配列。要素数を固定で仮定しない（マウント設定で変わる）。
- 1行のパースに失敗しても**その行だけ捨てて次行へ**進む（ストリームを切らない）。
- `v` が想定外（未知のメジャー差）なら、その接続を**非対応として警告表示**にとどめ、クラッシュさせない。

---

## 5. バージョニング方針（`v`）

- `v` は**スキーマのメジャーバージョン**。最初から `1` を持たせ、将来の互換性管理に備える。
- **互換変更（`v` 据え置き）**: フィールドの**追加**、説明の明確化など。client は未知フィールドを無視するので壊れない。
- **非互換変更（`v` をインクリメント）**: 既存フィールドの**削除・改名・型変更・意味変更**。この場合のみ `v` を 2 に上げ、client は新旧両対応 or 非対応表示で安全に縮退する。
- 変更手順は本書冒頭の運用ルールに従う（**契約 → 両側テスト → 実装**、人間承認・`consistency-checker` 確認）。

---

## 6. 正典サンプル

`testdata/sample.ndjson` がゴールデンサンプル（1行）であり、**両側のテストの参照点**である。本書の表と `sample.ndjson` が食い違った場合は、**両者を一致させる修正を最優先**で行う（契約のドリフトを残さない）。

### 6.1 通常サンプル（= `testdata/sample.ndjson` の内容）

```json
{"v":1,"id":"vps-example-1","ts":1717300000,"cpu_pct":12.4,"mem":{"used_pct":63.2,"used_mb":2521,"total_mb":3989},"swap":{"used_pct":0.0},"disk":[{"mount":"/","used_pct":48.1,"used_gb":24.0,"total_gb":50.0}],"net":{"iface":"eth0","rx_bps":102400,"tx_bps":51200},"load":[0.12,0.08,0.05],"uptime_sec":864000}
```

### 6.2 初回サンプル（レート系が null）— 参考例

ループ1周目はレートが取れないため `cpu_pct`・`rx_bps`・`tx_bps` が `null` になる。**この形も契約に含む**（client は両方をパースできること）。

```json
{"v":1,"id":"vps-example-1","ts":1717299999,"cpu_pct":null,"mem":{"used_pct":63.0,"used_mb":2513,"total_mb":3989},"swap":{"used_pct":0.0},"disk":[{"mount":"/","used_pct":48.1,"used_gb":24.0,"total_gb":50.0}],"net":{"iface":"eth0","rx_bps":null,"tx_bps":null},"load":[0.10,0.07,0.05],"uptime_sec":864000}
```

> 申し送り: 6.2 の null ケースも fixture 化して両側テストに含めるかは次セッションで検討（現状の正典 fixture は 6.1 の1行）。
