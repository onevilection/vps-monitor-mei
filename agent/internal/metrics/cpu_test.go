package metrics

import "testing"

func TestParseStatCPU(t *testing.T) {
	// /proc/stat: aggregate "cpu " line (trailing space distinguishes it from
	// per-core "cpu0"). Fields: user nice system idle iowait irq softirq steal...
	in := []byte("cpu  100 0 100 1000 0 0 0 0 0 0\ncpu0 50 0 50 500 0 0 0 0 0 0\nintr 12345\n")
	got, err := ParseStatCPU(in)
	if err != nil {
		t.Fatalf("ParseStatCPU: %v", err)
	}
	if got.Total() != 1200 { // 100+0+100+1000
		t.Errorf("Total = %d, want 1200", got.Total())
	}
	if got.Idle != 1000 || got.IOWait != 0 {
		t.Errorf("idle=%d iowait=%d, want 1000/0", got.Idle, got.IOWait)
	}
}

func TestComputeCPUNormal(t *testing.T) {
	prev := CPUTimes{User: 100, System: 100, Idle: 1000}
	cur := CPUTimes{User: 110, System: 110, Idle: 1080}
	// idle_d=80, total_d=100 -> (1-80/100)*100 = 20.0
	got := ComputeCPU(prev, cur)
	if got == nil {
		t.Fatal("want 20.0, got nil")
	}
	if *got != 20.0 {
		t.Errorf("cpu_pct = %v, want 20.0", *got)
	}
}

func TestComputeCPUZeroTotalDeltaIsNull(t *testing.T) {
	// total_d <= 0 -> null (付録A). Identical snapshots produce zero delta.
	same := CPUTimes{User: 100, Idle: 1000}
	if got := ComputeCPU(same, same); got != nil {
		t.Errorf("want nil for zero total delta, got %v", *got)
	}
}

func TestComputeCPURounded(t *testing.T) {
	prev := CPUTimes{Idle: 0}
	cur := CPUTimes{User: 1, Idle: 2} // total_d=3, idle_d=2 -> (1-2/3)*100=33.33..
	got := ComputeCPU(prev, cur)
	if got == nil || *got != 33.3 {
		t.Errorf("cpu_pct = %v, want 33.3 (rounded 1dp)", got)
	}
}
