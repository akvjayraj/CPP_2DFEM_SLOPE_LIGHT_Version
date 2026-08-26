# RS2 Example 16 (Greco 1996 / Chen & Shao 1988) GUI Automation Summary

Generated: `2026-07-19`
RS2 SSR reference FoS: `1.02`
Bishop (circular) reference: `1.087`
Spencer (circular) reference: `1.093`
Spencer (non-circular) reference: `1.007`
Yield gate: `max_yield_ratio <= 25`

| Mesh | FoS | Deviation | Deviation % | Nodes | Elements | GW |
|---|---:|---:|---:|---:|---:|:---:|
| T3 | 0.950 | -0.070 | -6.863 | 8618 | 14725 | yes |

Critical FoS ≈ 0.950 (yield-gate bracket F ∈ [0.9, 1], gate=25)

Bracket: F ∈ [0.9, 1]

See also:

- `FINDINGS_SRF_MISMATCH.md` — root cause
- `geometry_overlay_rs2_example16.html` — geometry vs RS2 §16
- `T6_STABILIZATION_NOTES.md` — when to prefer T6
- `dry_baseline_summary.md` — dry F=1 status
