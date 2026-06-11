package metrics

import (
	"errors"
	"strconv"
	"strings"
)

// errNoField reports a required line/key missing from a /proc file.
func errNoField(name string) error {
	return errors.New("metrics: required field not found: " + name)
}

// firstLineWithPrefix returns the first line of b that begins with prefix
// (prefix is matched verbatim, so "cpu " excludes "cpu0").
func firstLineWithPrefix(b []byte, prefix string) (string, bool) {
	s := string(b)
	for len(s) > 0 {
		nl := strings.IndexByte(s, '\n')
		var line string
		if nl < 0 {
			line, s = s, ""
		} else {
			line, s = s[:nl], s[nl+1:]
		}
		if strings.HasPrefix(line, prefix) {
			return line, true
		}
	}
	return "", false
}

// fields splits on runs of whitespace (like strings.Fields).
func fields(s string) []string { return strings.Fields(s) }

// forEachLine calls fn for each newline-delimited line of b (excluding the
// newline). A trailing empty segment after the final newline is skipped.
func forEachLine(b []byte, fn func(line string)) {
	s := string(b)
	for len(s) > 0 {
		nl := strings.IndexByte(s, '\n')
		if nl < 0 {
			fn(s)
			return
		}
		fn(s[:nl])
		s = s[nl+1:]
	}
}

// cutColon splits "Key: value" into ("Key", "value", true).
func cutColon(line string) (key, rest string, ok bool) {
	i := strings.IndexByte(line, ':')
	if i < 0 {
		return "", "", false
	}
	return line[:i], line[i+1:], true
}

// parseUint parses a base-10 unsigned integer, returning 0 on malformed input.
func parseUint(s string) uint64 {
	v, err := strconv.ParseUint(s, 10, 64)
	if err != nil {
		return 0
	}
	return v
}

// parseFloat parses a float, returning 0 on malformed input.
func parseFloat(s string) float64 {
	v, err := strconv.ParseFloat(s, 64)
	if err != nil {
		return 0
	}
	return v
}
