package metrics

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

// fixtureDir resolves testdata/ at the repository root from this package dir
// (agent/internal/metrics → repo root is three levels up).
func fixtureDir(t *testing.T) string {
	t.Helper()
	return filepath.Join("..", "..", "..", "testdata")
}

func fp(v float64) *float64 { return &v }

// sampleForFixture returns a Sample whose values mirror the named golden
// fixture. The fixture-conformance test marshals it and asserts the output has
// the same key set / JSON type / null positions as the fixture itself.
func sampleForFixture(name string) Sample {
	switch name {
	case "sample.ndjson": // 正常: レート系も値あり・disk 1要素
		return Sample{
			V: 1, ID: "vps-example-1", TS: 1717300000,
			CPUPct:    fp(12.4),
			Mem:       Mem{UsedPct: 63.2, UsedMB: 2521, TotalMB: 3989},
			Swap:      Swap{UsedPct: 0.0},
			Disk:      []Disk{{Mount: "/", UsedPct: 48.1, UsedGB: 24.0, TotalGB: 50.0}},
			Net:       Net{Iface: "eth0", RxBps: fp(102400), TxBps: fp(51200)},
			Load:      []float64{0.12, 0.08, 0.05},
			UptimeSec: 864000,
		}
	case "sample_measuring.ndjson": // 測定中: cpu_pct/rx_bps/tx_bps が null
		return Sample{
			V: 1, ID: "vps-example-1", TS: 1717299999,
			CPUPct:    nil,
			Mem:       Mem{UsedPct: 63.0, UsedMB: 2513, TotalMB: 3989},
			Swap:      Swap{UsedPct: 0.0},
			Disk:      []Disk{{Mount: "/", UsedPct: 48.1, UsedGB: 24.0, TotalGB: 50.0}},
			Net:       Net{Iface: "eth0", RxBps: nil, TxBps: nil},
			Load:      []float64{0.10, 0.07, 0.05},
			UptimeSec: 864000,
		}
	case "sample_multidisk.ndjson": // 複数 disk: disk 2要素
		return Sample{
			V: 1, ID: "vps-example-2", TS: 1717300001,
			CPUPct: fp(23.7),
			Mem:    Mem{UsedPct: 71.5, UsedMB: 2853, TotalMB: 3989},
			Swap:   Swap{UsedPct: 12.5},
			Disk: []Disk{
				{Mount: "/", UsedPct: 48.1, UsedGB: 24.0, TotalGB: 50.0},
				{Mount: "/var/www", UsedPct: 67.3, UsedGB: 134.6, TotalGB: 200.0},
			},
			Net:       Net{Iface: "eth0", RxBps: fp(204800), TxBps: fp(81920)},
			Load:      []float64{0.45, 0.30, 0.18},
			UptimeSec: 864002,
		}
	default:
		panic("unknown fixture " + name)
	}
}

// jsonKind reduces a decoded JSON value to its structural category. This is the
// granularity the schema contract cares about (§3 null許容, §4 型厳守):
// null vs number vs string vs bool vs object vs array.
func jsonKind(v any) string {
	switch v.(type) {
	case nil:
		return "null"
	case float64:
		return "number"
	case string:
		return "string"
	case bool:
		return "bool"
	case map[string]any:
		return "object"
	case []any:
		return "array"
	default:
		return "unknown"
	}
}

// assertSameSchema fails if got's structure diverges from ref: differing key
// sets, differing JSON types (incl. null position), or differing array lengths.
func assertSameSchema(t *testing.T, path string, ref, got any) {
	t.Helper()
	rk, gk := jsonKind(ref), jsonKind(got)
	if rk != gk {
		t.Errorf("%s: type mismatch: fixture=%s output=%s", path, rk, gk)
		return
	}
	switch rk {
	case "object":
		rm := ref.(map[string]any)
		gm := got.(map[string]any)
		for k := range rm {
			gv, ok := gm[k]
			if !ok {
				t.Errorf("%s: output missing key %q present in fixture", path, k)
				continue
			}
			assertSameSchema(t, path+"."+k, rm[k], gv)
		}
		for k := range gm {
			if _, ok := rm[k]; !ok {
				t.Errorf("%s: output has extra key %q not in fixture", path, k)
			}
		}
	case "array":
		ra := ref.([]any)
		ga := got.([]any)
		if len(ra) != len(ga) {
			t.Errorf("%s: array length mismatch: fixture=%d output=%d", path, len(ra), len(ga))
			return
		}
		for i := range ra {
			assertSameSchema(t, path+"["+itoa(i)+"]", ra[i], ga[i])
		}
	}
}

func itoa(i int) string {
	if i == 0 {
		return "0"
	}
	var b []byte
	for i > 0 {
		b = append([]byte{byte('0' + i%10)}, b...)
		i /= 10
	}
	return string(b)
}

// TestOutputMatchesFixtureSchema is the contract guard: the agent's Sample must
// marshal to exactly the schema of each of the 3 golden fixtures (same keys,
// types, null positions). Both sides reference the same testdata/ fixtures.
func TestOutputMatchesFixtureSchema(t *testing.T) {
	fixtures := []string{
		"sample.ndjson",
		"sample_measuring.ndjson",
		"sample_multidisk.ndjson",
	}
	for _, name := range fixtures {
		t.Run(name, func(t *testing.T) {
			raw, err := os.ReadFile(filepath.Join(fixtureDir(t), name))
			if err != nil {
				t.Fatalf("read fixture: %v", err)
			}
			var ref any
			if err := json.Unmarshal(raw, &ref); err != nil {
				t.Fatalf("unmarshal fixture: %v", err)
			}

			out, err := json.Marshal(sampleForFixture(name))
			if err != nil {
				t.Fatalf("marshal sample: %v", err)
			}
			var got any
			if err := json.Unmarshal(out, &got); err != nil {
				t.Fatalf("unmarshal sample output: %v", err)
			}

			assertSameSchema(t, name, ref, got)
		})
	}
}
