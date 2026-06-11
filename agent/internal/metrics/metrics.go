// Package metrics holds the NDJSON sample schema and the pure functions that
// compute each field from raw /proc and statfs inputs. The wire schema is the
// single source of truth defined by docs/ndjson-schema.md and the testdata/
// golden fixtures; this package must stay conformant to them.
//
// Everything here is deliberately I/O-free and allocation-light so it can be
// unit-tested against fixed inputs. The actual /proc reads, FD reuse and the
// 1Hz loop live in package main (the thin glue layer).
package metrics

// Sample is one NDJSON line: exactly one JSON object per sample (schema §1/§2).
// Nullable rate fields use pointers so a nil marshals to JSON null (§3).
type Sample struct {
	V         int       `json:"v"`          // schema version, currently 1
	ID        string    `json:"id"`         // server identifier (servers.json id)
	TS        int64     `json:"ts"`         // sample time, Unix epoch seconds UTC
	CPUPct    *float64  `json:"cpu_pct"`    // 0-100, null while measuring (§3)
	Mem       Mem       `json:"mem"`        // memory, always present
	Swap      Swap      `json:"swap"`       // swap, always present
	Disk      []Disk    `json:"disk"`       // per-mount; never null, empty array OK
	Net       Net       `json:"net"`        // network, always present
	Load      []float64 `json:"load"`       // [1m,5m,15m], always 3 elements
	UptimeSec int64     `json:"uptime_sec"` // uptime seconds (/proc/uptime)
}

// Mem mirrors schema §2.1 (MemAvailable-based usage).
type Mem struct {
	UsedPct float64 `json:"used_pct"` // (MemTotal-MemAvailable)/MemTotal*100
	UsedMB  int64   `json:"used_mb"`  // (MemTotal-MemAvailable) in MiB
	TotalMB int64   `json:"total_mb"` // MemTotal in MiB
}

// Swap mirrors schema §2.2. used_pct is 0.0 when SwapTotal is 0.
type Swap struct {
	UsedPct float64 `json:"used_pct"`
}

// Disk mirrors schema §2.3: rate (pct) is physical (f_bfree-based), amount
// (used_gb) is user-facing (f_bavail-based), matching df's conventions.
type Disk struct {
	Mount   string  `json:"mount"`
	UsedPct float64 `json:"used_pct"` // (f_blocks-f_bfree)/f_blocks*100
	UsedGB  float64 `json:"used_gb"`  // (f_blocks-f_bavail)*f_bsize in GiB
	TotalGB float64 `json:"total_gb"` // f_blocks*f_bsize in GiB
}

// Net mirrors schema §2.4. rx/tx are rates (delta-based) and null while
// measuring or on counter rollback (§3).
type Net struct {
	Iface string   `json:"iface"`
	RxBps *float64 `json:"rx_bps"`
	TxBps *float64 `json:"tx_bps"`
}
