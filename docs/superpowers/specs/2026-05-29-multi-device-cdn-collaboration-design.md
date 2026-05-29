# Multi-Device CDN Collaboration Design

**Date:** 2026-05-29
**Status:** Approved

## Problem

Multiple devices with the same Cloudinary credentials each independently fetch from the Himawari NICT source station and upload to CDN. No mechanism exists for Device B to benefit from Device A's already-uploaded work, resulting in redundant NICT requests.

## Design Decision: Dynamic Timestamp + CDN Probe

### Time Slot Calculation

Himawari satellite images are published every 10 minutes (e.g., 0720, 0730, 0740). The dynamic `public_id` uses a floor-to-nearest-10-minutes strategy:

```
Current time 07:24 → timestamp 0720
Current time 07:31 → timestamp 0730
Current time 15:59 → timestamp 1550
```

Format: `earthlivesharp/{size}x{size}/{yyyyMMdd_HHmm}`
Example: `earthlivesharp/4x4/20260529_0720`

Different `Cfg.size` values get distinct IDs to avoid conflicts.

### Core Control Flow (UploadMode)

```
Each UpdateImage_UploadMode():
  1. GetImageID() → fetch NICT latest.json (~3KB) to confirm official latest timestamp
  2. Calculate dynamic_id from confirmed timestamp
  3. HTTP HEAD probe on CDN URL for this dynamic_id
     ├─ HTTP 200 (CDN hit)
     │   → Download from CDN → set wallpaper
     │   → status = "cdn_hit" (no balloon, unless first)
     │
     └─ HTTP 404 (CDN miss)
         → Download tiles from NICT → stitch → set wallpaper
         → Upload to CDN (dynamic_id)
         ├─ Upload success
         │   → Delete previous time slot's public_id from CDN
         │   → status = "nict_upload_ok" (show balloon)
         └─ Upload failure
             → status = "upload_failed_stop" (show balloon + stop updates)
```

### Key Guarantee

Step 1 (NICT JSON) confirms "what is the official latest image time slot".
Step 3 (CDN HEAD probe) confirms "has any device already uploaded this time slot's image".
Combined: a CDN hit is guaranteed to be the current fresh image.

Cost: one ~3KB JSON request per update cycle (vs. ~800KB of tile downloads previously).

### Old Resource Cleanup

The `Scraper_himawari8` class tracks `previousPublicId` — the last public_id that was successfully uploaded. When a new upload succeeds, it calls `DeleteResource(previousPublicId)`. The first upload has `previousPublicId = ""` so no deletion occurs. Deletion failures are non-fatal (logged only).

### Error Handling

| Scenario | Behavior |
|----------|----------|
| HEAD probe returns non-200/404 or timeout | Show balloon + stop updates |
| CDN hit download fails | Show balloon "all_sources_failed" |
| NICT GetImageID() fails | Show balloon "all_sources_failed", continue next cycle |
| NICT tile download fails | Show balloon "all_sources_failed", continue next cycle |
| Upload fails | Show balloon "upload_failed_stop" + stop updates |
| Delete old resource fails | Log only, continue |

### Mode Compatibility

| source_selection | upload_mode | api_keys | Behavior |
|---|---|---|---|
| 0 (Origin) | - | - | Direct from NICT, unchanged |
| 1 (CDN) | 0 | - | Cloudinary fetch proxy from NICT, unchanged |
| 1 (CDN) | 1 | incomplete | CDN Only download (fixed ID), unchanged |
| 1 (CDN) | 1 | complete | **New Scheme A: dynamic ID + probe** |

### Balloon Notifications

| Status | Icon | Message |
|--------|------|---------|
| `cdn_hit` | None | No balloon (silent) |
| `nict_upload_ok` | Info | "壁纸已从源站更新并上传CDN" |
| `all_sources_failed` | Error | "所有图像源均不可用，请检查网络" |
| `upload_failed_stop` | Error | "CDN上传失败，程序已停止自动更新" |

Balloons only shown on first occurrence per status change (deduplicated via `lastNotifiedStatus` in mainForm.cs).

### Files Changed

| File | Changes |
|------|---------|
| `CloudinaryUpload.cs` | Add `ProbeExists(publicId, cloudName)` (HTTP HEAD); Add `DeleteResource(publicId, cloudName, apiKey, apiSecret)` (Admin API DELETE) |
| `Program.cs` | Rewrite `UpdateImage_UploadMode()`: GetImageID → dynamic ID → HEAD probe → CDN download / NICT fetch+upload; Remove `lastNictRefreshUtc` field; Add `GetCurrentPublicId()` method; Simplify `ResetState()` |
| `mainForm.cs` | Update balloon handling: add `cdn_hit` (no-op), replace old `cdn_cache` |
