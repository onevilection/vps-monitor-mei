package metrics

const giB = 1024 * 1024 * 1024

// ComputeDisk implements schema §2.3 from raw statfs(2) values:
//   - used_pct is physical: (f_blocks-f_bfree)/f_blocks*100 (admin-reserved
//     blocks counted as used) — used for display/threshold.
//   - used_gb is user-facing: (f_blocks-f_bavail)*f_bsize in GiB.
//   - total_gb is f_blocks*f_bsize in GiB.
//
// A zero f_blocks (pseudo/empty filesystem) yields all zeros, no divide.
func ComputeDisk(mount string, blocks, bfree, bavail, bsize uint64) Disk {
	d := Disk{Mount: mount}
	if blocks == 0 {
		return d
	}
	usedBlocks := blocks - bfree      // physical used (incl. reserved)
	userUsedBlocks := blocks - bavail // user-visible used
	d.UsedPct = round1(float64(usedBlocks) / float64(blocks) * 100)
	d.UsedGB = round1(float64(userUsedBlocks) * float64(bsize) / giB)
	d.TotalGB = round1(float64(blocks) * float64(bsize) / giB)
	return d
}
