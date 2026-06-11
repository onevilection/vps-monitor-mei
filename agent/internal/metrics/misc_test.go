package metrics

import "testing"

func TestParseLoadavg(t *testing.T) {
	got, err := ParseLoadavg([]byte("0.12 0.08 0.05 1/234 5678\n"))
	if err != nil {
		t.Fatalf("ParseLoadavg: %v", err)
	}
	want := []float64{0.12, 0.08, 0.05}
	if len(got) != 3 {
		t.Fatalf("len = %d, want 3", len(got))
	}
	for i := range want {
		if got[i] != want[i] {
			t.Errorf("load[%d] = %v, want %v", i, got[i], want[i])
		}
	}
}

func TestParseUptimeTruncates(t *testing.T) {
	got, err := ParseUptime([]byte("864000.99 1234567.89\n"))
	if err != nil {
		t.Fatalf("ParseUptime: %v", err)
	}
	if got != 864000 {
		t.Errorf("uptime_sec = %d, want 864000 (truncated)", got)
	}
}

const routeSample = `Iface	Destination	Gateway 	Flags	RefCnt	Use	Metric	Mask	MTU	Window	IRTT
eth0	00000000	0102A8C0	0003	0	0	0	00000000	0	0	0
eth0	0002A8C0	00000000	0001	0	0	0	00FFFFFF	0	0	0
`

func TestDefaultIface(t *testing.T) {
	// The default route is the entry whose Destination is 00000000.
	got, err := DefaultIface([]byte(routeSample))
	if err != nil {
		t.Fatalf("DefaultIface: %v", err)
	}
	if got != "eth0" {
		t.Errorf("iface = %q, want eth0", got)
	}
}

func TestDefaultIfaceNoneFound(t *testing.T) {
	only := "Iface\tDestination\tGateway\neth0\t0002A8C0\t00000000\n"
	if _, err := DefaultIface([]byte(only)); err == nil {
		t.Error("want error when no default route, got nil")
	}
}
