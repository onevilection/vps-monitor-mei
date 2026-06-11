package metrics

import "math"

// MemInfo holds the /proc/meminfo values we use, in kB (kibibytes) as the file
// reports them.
type MemInfo struct {
	MemTotalKB     uint64
	MemAvailableKB uint64
	SwapTotalKB    uint64
	SwapFreeKB     uint64
}

// ParseMeminfo reads the few keys we need from /proc/meminfo. Lines look like
// "MemTotal:        4084736 kB". Unwanted keys are skipped.
func ParseMeminfo(b []byte) (MemInfo, error) {
	var mi MemInfo
	seenTotal := false
	forEachLine(b, func(line string) {
		key, rest, ok := cutColon(line)
		if !ok {
			return
		}
		f := fields(rest)
		if len(f) == 0 {
			return
		}
		val := parseUint(f[0]) // first token is the number; "kB" follows
		switch key {
		case "MemTotal":
			mi.MemTotalKB = val
			seenTotal = true
		case "MemAvailable":
			mi.MemAvailableKB = val
		case "SwapTotal":
			mi.SwapTotalKB = val
		case "SwapFree":
			mi.SwapFreeKB = val
		}
	})
	if !seenTotal {
		return mi, errNoField("MemTotal")
	}
	return mi, nil
}

// ComputeMem implements schema §2.1: MemAvailable-based usage, MiB amounts.
func ComputeMem(mi MemInfo) Mem {
	usedKB := int64(mi.MemTotalKB) - int64(mi.MemAvailableKB)
	if usedKB < 0 {
		usedKB = 0
	}
	var usedPct float64
	if mi.MemTotalKB > 0 {
		usedPct = round1(float64(usedKB) / float64(mi.MemTotalKB) * 100)
	}
	return Mem{
		UsedPct: usedPct,
		UsedMB:  kbToMiB(usedKB),
		TotalMB: kbToMiB(int64(mi.MemTotalKB)),
	}
}

// ComputeSwap implements schema §2.2: 0.0 when SwapTotal is 0 (no divide).
func ComputeSwap(mi MemInfo) Swap {
	if mi.SwapTotalKB == 0 {
		return Swap{UsedPct: 0.0}
	}
	used := int64(mi.SwapTotalKB) - int64(mi.SwapFreeKB)
	if used < 0 {
		used = 0
	}
	return Swap{UsedPct: round1(float64(used) / float64(mi.SwapTotalKB) * 100)}
}

// kbToMiB converts kibibytes to mebibytes, rounded to nearest.
func kbToMiB(kb int64) int64 { return int64(math.Round(float64(kb) / 1024)) }
