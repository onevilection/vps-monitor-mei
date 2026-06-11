package metrics

import "math"

// CPUTimes holds the cumulative jiffy counters from the aggregate "cpu " line
// of /proc/stat. CPU% is a delta ratio so it is USER_HZ-independent (§3.7).
type CPUTimes struct {
	User    uint64
	Nice    uint64
	System  uint64
	Idle    uint64
	IOWait  uint64
	IRQ     uint64
	SoftIRQ uint64
	Steal   uint64
}

// Total is the sum of all counters (busy + idle).
func (c CPUTimes) Total() uint64 {
	return c.User + c.Nice + c.System + c.Idle + c.IOWait + c.IRQ + c.SoftIRQ + c.Steal
}

// ParseStatCPU extracts the aggregate "cpu " line from /proc/stat content.
// It tolerates extra trailing fields (guest, guest_nice) by ignoring them.
func ParseStatCPU(b []byte) (CPUTimes, error) {
	line, ok := firstLineWithPrefix(b, "cpu ")
	if !ok {
		return CPUTimes{}, errNoField("cpu")
	}
	f := fields(line)
	// f[0] == "cpu"; counters start at f[1].
	get := func(i int) uint64 {
		if 1+i < len(f) {
			return parseUint(f[1+i])
		}
		return 0
	}
	return CPUTimes{
		User:    get(0),
		Nice:    get(1),
		System:  get(2),
		Idle:    get(3),
		IOWait:  get(4),
		IRQ:     get(5),
		SoftIRQ: get(6),
		Steal:   get(7),
	}, nil
}

// ComputeCPU implements 付録A: cpu_pct = total_d>0 ? (1-idle_d/total_d)*100 :
// null. Returns nil when the total delta is non-positive (e.g. identical reads
// or counter wrap) — the caller also passes nil on the first loop iteration.
func ComputeCPU(prev, cur CPUTimes) *float64 {
	totalPrev, totalCur := prev.Total(), cur.Total()
	if totalCur <= totalPrev {
		return nil // total_d <= 0
	}
	totalD := float64(totalCur - totalPrev)
	idleD := float64((cur.Idle + cur.IOWait) - (prev.Idle + prev.IOWait))
	pct := (1 - idleD/totalD) * 100
	return round1Ptr(pct)
}

// round1 rounds to one decimal place.
func round1(x float64) float64 { return math.Round(x*10) / 10 }

func round1Ptr(x float64) *float64 { v := round1(x); return &v }
