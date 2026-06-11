package metrics

import "strings"

// NetCounters holds cumulative byte counters for one interface from
// /proc/net/dev.
type NetCounters struct {
	RxBytes uint64
	TxBytes uint64
}

// ParseNetDev extracts the rx/tx byte counters for iface from /proc/net/dev.
// Each data line is "  iface: rxbytes rxpackets ... txbytes txpackets ...":
// rx bytes is the 1st post-colon field, tx bytes the 9th (index 8).
func ParseNetDev(b []byte, iface string) (NetCounters, error) {
	var nc NetCounters
	found := false
	forEachLine(b, func(line string) {
		if found {
			return
		}
		name, rest, ok := cutColon(line)
		if !ok {
			return
		}
		if strings.TrimSpace(name) != iface {
			return
		}
		f := fields(rest)
		if len(f) < 9 {
			return
		}
		nc.RxBytes = parseUint(f[0])
		nc.TxBytes = parseUint(f[8])
		found = true
	})
	if !found {
		return nc, errNoField("net iface " + iface)
	}
	return nc, nil
}

// ComputeNet returns rx/tx bytes-per-second over interval seconds. Each
// direction is independent: a field whose counter went backwards (rollback /
// reset) yields nil for that field only (§3).
func ComputeNet(prev, cur NetCounters, interval float64) (rx, tx *float64) {
	return rate(prev.RxBytes, cur.RxBytes, interval), rate(prev.TxBytes, cur.TxBytes, interval)
}

func rate(prev, cur uint64, interval float64) *float64 {
	if cur < prev || interval <= 0 {
		return nil // counter rollback/reset -> null this sample
	}
	return round1Ptr(float64(cur-prev) / interval)
}
