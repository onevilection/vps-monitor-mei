package metrics

import "testing"

const netdevSample = `Inter-|   Receive                                                |  Transmit
 face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed
    lo:  100      10    0    0    0     0          0         0      100      10    0    0    0     0       0          0
  eth0: 102400   200    0    0    0     0          0         0    51200     180    0    0    0     0       0          0
`

func TestParseNetDev(t *testing.T) {
	got, err := ParseNetDev([]byte(netdevSample), "eth0")
	if err != nil {
		t.Fatalf("ParseNetDev: %v", err)
	}
	if got.RxBytes != 102400 || got.TxBytes != 51200 {
		t.Errorf("got rx=%d tx=%d, want 102400/51200", got.RxBytes, got.TxBytes)
	}
}

func TestParseNetDevMissingIface(t *testing.T) {
	if _, err := ParseNetDev([]byte(netdevSample), "wlan0"); err == nil {
		t.Error("want error for missing interface, got nil")
	}
}

func TestComputeNetNormal(t *testing.T) {
	prev := NetCounters{RxBytes: 102400, TxBytes: 51200}
	cur := NetCounters{RxBytes: 204800, TxBytes: 102400}
	rx, tx := ComputeNet(prev, cur, 1.0)
	if rx == nil || *rx != 102400 {
		t.Errorf("rx_bps = %v, want 102400", rx)
	}
	if tx == nil || *tx != 51200 {
		t.Errorf("tx_bps = %v, want 51200", tx)
	}
}

func TestComputeNetInterval(t *testing.T) {
	// 102400 bytes over 2 seconds = 51200 bytes/sec.
	prev := NetCounters{RxBytes: 0}
	cur := NetCounters{RxBytes: 102400}
	rx, _ := ComputeNet(prev, cur, 2.0)
	if rx == nil || *rx != 51200 {
		t.Errorf("rx_bps = %v, want 51200", rx)
	}
}

func TestComputeNetRollbackIsNull(t *testing.T) {
	// Counter went backwards (NIC reset/reboot): that field's rate is null (§3).
	prev := NetCounters{RxBytes: 200, TxBytes: 100}
	cur := NetCounters{RxBytes: 100, TxBytes: 300} // rx rolled back, tx did not
	rx, tx := ComputeNet(prev, cur, 1.0)
	if rx != nil {
		t.Errorf("rx_bps = %v, want nil on rollback", *rx)
	}
	if tx == nil || *tx != 200 {
		t.Errorf("tx_bps = %v, want 200 (tx not rolled back)", tx)
	}
}
