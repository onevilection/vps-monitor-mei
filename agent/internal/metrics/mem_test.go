package metrics

import "testing"

const meminfoSample = `MemTotal:        4084736 kB
MemFree:          500000 kB
MemAvailable:    1503232 kB
Buffers:           10000 kB
Cached:           800000 kB
SwapTotal:             0 kB
SwapFree:              0 kB
`

func TestParseMeminfo(t *testing.T) {
	mi, err := ParseMeminfo([]byte(meminfoSample))
	if err != nil {
		t.Fatalf("ParseMeminfo: %v", err)
	}
	if mi.MemTotalKB != 4084736 || mi.MemAvailableKB != 1503232 {
		t.Errorf("got total=%d avail=%d", mi.MemTotalKB, mi.MemAvailableKB)
	}
	if mi.SwapTotalKB != 0 || mi.SwapFreeKB != 0 {
		t.Errorf("got swaptotal=%d swapfree=%d", mi.SwapTotalKB, mi.SwapFreeKB)
	}
}

func TestComputeMem(t *testing.T) {
	mi := MemInfo{MemTotalKB: 4084736, MemAvailableKB: 1503232}
	got := ComputeMem(mi)
	// used = 2581504 kB -> used_mb = 2521, total_mb = 3989, used_pct = 63.2
	if got.UsedPct != 63.2 {
		t.Errorf("used_pct = %v, want 63.2", got.UsedPct)
	}
	if got.UsedMB != 2521 {
		t.Errorf("used_mb = %d, want 2521", got.UsedMB)
	}
	if got.TotalMB != 3989 {
		t.Errorf("total_mb = %d, want 3989", got.TotalMB)
	}
}

func TestComputeSwapZeroTotalIsZero(t *testing.T) {
	// SwapTotal == 0 must yield 0.0, not a divide-by-zero (§2.2).
	got := ComputeSwap(MemInfo{SwapTotalKB: 0, SwapFreeKB: 0})
	if got.UsedPct != 0.0 {
		t.Errorf("used_pct = %v, want 0.0", got.UsedPct)
	}
}

func TestComputeSwapNonzero(t *testing.T) {
	// (1000-875)/1000*100 = 12.5
	got := ComputeSwap(MemInfo{SwapTotalKB: 1000, SwapFreeKB: 875})
	if got.UsedPct != 12.5 {
		t.Errorf("used_pct = %v, want 12.5", got.UsedPct)
	}
}
