package metrics

// ParseLoadavg returns the [1m,5m,15m] load averages from /proc/loadavg.
func ParseLoadavg(b []byte) ([]float64, error) {
	f := fields(string(b))
	if len(f) < 3 {
		return nil, errNoField("loadavg")
	}
	return []float64{parseFloat(f[0]), parseFloat(f[1]), parseFloat(f[2])}, nil
}

// ParseUptime returns whole seconds of uptime from /proc/uptime (first field,
// truncated toward zero).
func ParseUptime(b []byte) (int64, error) {
	f := fields(string(b))
	if len(f) < 1 {
		return 0, errNoField("uptime")
	}
	return int64(parseFloat(f[0])), nil
}

// DefaultIface returns the interface backing the default route from
// /proc/net/route (the entry whose hex Destination is all zeros). Used once at
// startup when --iface is not given (§3.4); excludes lo implicitly since lo has
// no default route.
func DefaultIface(b []byte) (string, error) {
	var iface string
	found := false
	forEachLine(b, func(line string) {
		if found {
			return
		}
		f := fields(line)
		if len(f) < 2 || f[0] == "Iface" { // skip header
			return
		}
		if f[1] == "00000000" {
			iface = f[0]
			found = true
		}
	})
	if !found {
		return "", errNoField("default route")
	}
	return iface, nil
}
