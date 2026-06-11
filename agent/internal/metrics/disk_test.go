package metrics

import "testing"

func TestComputeDisk(t *testing.T) {
	// bsize=4096, blocks=13107200 -> total = 50 GiB.
	// bfree=6553600  -> used_pct = (blocks-bfree)/blocks*100 = 50.0 (physical).
	// bavail=6291456 -> used_gb  = (blocks-bavail)*bsize     = 26.0 GiB (user).
	got := ComputeDisk("/", 13107200, 6553600, 6291456, 4096)
	if got.Mount != "/" {
		t.Errorf("mount = %q, want /", got.Mount)
	}
	if got.UsedPct != 50.0 {
		t.Errorf("used_pct = %v, want 50.0 (f_bfree based)", got.UsedPct)
	}
	if got.UsedGB != 26.0 {
		t.Errorf("used_gb = %v, want 26.0 (f_bavail based)", got.UsedGB)
	}
	if got.TotalGB != 50.0 {
		t.Errorf("total_gb = %v, want 50.0", got.TotalGB)
	}
}

func TestComputeDiskZeroBlocksNoPanic(t *testing.T) {
	// A pseudo/empty filesystem (f_blocks==0) must not divide by zero.
	got := ComputeDisk("/none", 0, 0, 0, 4096)
	if got.UsedPct != 0.0 || got.TotalGB != 0.0 || got.UsedGB != 0.0 {
		t.Errorf("zero-block fs = %+v, want all 0.0", got)
	}
}
