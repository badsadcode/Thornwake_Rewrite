# Cloud blend check on dump 182351 (HANDOFF "Next" step 1)

Captured on Tom's PC (RTX 3080) from `main` at 5c45cda, 2026-10-12. Tom's world and settings from the Reproduce
line of dump 182351, without `--at --view --fly-height --day --hour sky.clouds time.paused fx.dof`, plus:

```
--at=-5669.182,5738.657 --fly-height=400 --view=<yaw>,-0.1,8 --day=1 --hour=<hour>
--set=sky.clouds=0.5 --set=time.paused=1 --set=fx.dof=0 --clouds=0.8,0,0,0 --set=sky.cloud_blend=<0|1>
```

One process per image (no `--extra-yaws`, so no volumetric fog history from a previous view). Originals are
1440 × 900, downscaled here to 1280 px wide.

File names: `<hour>-<direction>-<blend>.jpg`

| Part | Values |
|---|---|
| hour | `1500` = 15:00 · `1615` = 16:15, winter sunset (the dump's season is winter, 9 h of daylight, sunset 16:30; the handoff's 19:30 is full night there) |
| direction | `north` = yaw 0 · `east` = yaw 1.57 · `south` = yaw 3.14 · `west` = yaw 4.71 |
| blend | `build52` = `sky.cloud_blend=0` (`CloudsNear52.gdshader`) · `matched` = `sky.cloud_blend=1` (v2, the build 53 default) |

| | 15:00 | 16:15 sunset |
|---|---|---|
| north | `1500-north-build52.jpg` / `1500-north-matched.jpg` | `1615-north-build52.jpg` / `1615-north-matched.jpg` |
| east | `1500-east-build52.jpg` / `1500-east-matched.jpg` | `1615-east-build52.jpg` / `1615-east-matched.jpg` |
| south | `1500-south-build52.jpg` / `1500-south-matched.jpg` | `1615-south-build52.jpg` / `1615-south-matched.jpg` |
| west | `1500-west-build52.jpg` / `1500-west-matched.jpg` | `1615-west-build52.jpg` / `1615-west-matched.jpg` |

## Findings
* The only cloud in front of land in this set is the cap on the peak (north). Build 52: a pale, near-white blob.
  Matched: the same tone and haze as the clouds behind it in the sky, at 15:00 and at sunset.
* Elsewhere, 400 m up and looking nearly level, the clouds stand against the sky only, so both blends look almost
  the same. Tom's own case (a cloud tower seen from a mountain, the deck below eye level) is not covered yet.
* No cloudless band under the horizon, no mountain outline through clouds, no `SHADER ERROR` in any run.
* Unrelated to the blend, in both variants: ~1,020 `Too many instances using shader instance variables` errors
  per run with Tom's settings; a thin bright vertical streak on the sea in `1615-west-*`.
