# Multi-Device CDN Collaboration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable multiple devices sharing Cloudinary credentials to collaborate — one device fetches from NICT and uploads, others detect the CDN resource and skip NICT entirely.

**Architecture:** Replace fixed `public_id` with dynamic timestamp-based IDs (`earthlivesharp/{size}x{size}/{yyyyMMdd_HHmm}`). Before downloading, probe the CDN with HTTP HEAD. On 200, download from CDN; on 404, fetch from NICT and upload. On probe error, stop updates.

**Tech Stack:** C# WinForms, .NET Framework 4.8, Cloudinary Upload/Admin APIs

---

### Task 1: Add CDN Probe Method to CloudinaryUpload

**Files:**
- Modify: `CloudinaryUpload.cs` (after `DownloadFromCloudinary`, around line 59)

- [ ] **Step 1: Add `ProbeExists` method**

Add this method to the `CloudinaryUpload` class, right after `DownloadFromCloudinary` (after line 59, before `UploadImage`):

```csharp
/// <summary>
/// HTTP HEAD probe to check if a CDN resource exists.
/// Returns true on 200, false on 404, throws on other errors.
/// </summary>
public static bool ProbeExists(string publicId, string cloudName)
{
    string url = string.Format("{0}/{1}/image/upload/{2}.png", FETCH_BASE, cloudName, publicId);
    HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
    request.Method = "HEAD";
    request.Timeout = 10000;
    try
    {
        using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Trace.WriteLine("[upload_mode] HEAD probe 200: " + publicId);
                return true;
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Trace.WriteLine("[upload_mode] HEAD probe 404: " + publicId);
                return false;
            }
            throw new Exception("HTTP " + (int)response.StatusCode);
        }
    }
    catch (WebException) when (request.HaveResponse && ((HttpWebResponse)((WebException)request.HaveResponse ? null : null)) != null)
    {
        throw;
    }
    catch (Exception e)
    {
        Trace.WriteLine("[upload_mode] HEAD probe error: " + e.Message);
        throw;
    }
}
```

Wait, that WebException handling is overly complex. Simplify:

```csharp
/// <summary>
/// HTTP HEAD probe to check if a CDN resource exists.
/// Returns true on 200, false on 404, throws on other errors.
/// </summary>
public static bool ProbeExists(string publicId, string cloudName)
{
    string url = string.Format("{0}/{1}/image/upload/{2}.png", FETCH_BASE, cloudName, publicId);
    HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
    request.Method = "HEAD";
    request.Timeout = 10000;
    try
    {
        using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Trace.WriteLine("[upload_mode] HEAD probe 200: " + publicId);
                return true;
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Trace.WriteLine("[upload_mode] HEAD probe 404: " + publicId);
                return false;
            }
            throw new Exception("HTTP " + (int)response.StatusCode);
        }
    }
    catch (WebException we)
    {
        if (we.Response is HttpWebResponse errorResponse)
        {
            if (errorResponse.StatusCode == HttpStatusCode.NotFound)
            {
                Trace.WriteLine("[upload_mode] HEAD probe 404: " + publicId);
                errorResponse.Close();
                return false;
            }
            errorResponse.Close();
        }
        Trace.WriteLine("[upload_mode] HEAD probe error: " + we.Message);
        throw;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build` or compile via Visual Studio/msbuild
Expected: Build succeeds with no new errors

- [ ] **Step 3: Commit**

```bash
git add CloudinaryUpload.cs
git commit -m "feat: add CloudinaryUpload.ProbeExists for CDN resource detection"
```

---

### Task 2: Add Delete Resource Method to CloudinaryUpload

**Files:**
- Modify: `CloudinaryUpload.cs` (after `ProbeExists`, before `SignParams`)

- [ ] **Step 1: Add `DeleteResource` method**

Add this method to `CloudinaryUpload`, right after `ProbeExists`:

```csharp
/// <summary>
/// Delete a resource from Cloudinary via Admin API (Basic Auth).
/// Non-fatal — returns false on error, caller should log.
/// </summary>
public static bool DeleteResource(string publicId, string cloudName, string apiKey, string apiSecret)
{
    string deleteUrl = string.Format("https://api.cloudinary.com/v1_1/{0}/resources/image/upload/{1}", cloudName, publicId);
    HttpWebRequest request = WebRequest.Create(deleteUrl) as HttpWebRequest;
    request.Method = "DELETE";
    request.Timeout = 10000;
    string svcCredentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(apiKey + ":" + apiSecret));
    request.Headers.Add("Authorization", "Basic " + svcCredentials);
    try
    {
        using (HttpWebResponse response = request.GetResponse() as HttpWebResponse)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Trace.WriteLine("[upload_mode] deleted old resource: " + publicId);
                return true;
            }
            Trace.WriteLine("[upload_mode] delete returned HTTP " + (int)response.StatusCode + " for: " + publicId);
            return false;
        }
    }
    catch (Exception e)
    {
        Trace.WriteLine("[upload_mode] delete error (non-fatal): " + e.Message);
        return false;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build` or compile via Visual Studio/msbuild
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add CloudinaryUpload.cs
git commit -m "feat: add CloudinaryUpload.DeleteResource for old asset cleanup"
```

---

### Task 3: Add Dynamic Public ID Method

**Files:**
- Modify: `CloudinaryUpload.cs` (replace `PublicIdForSize`)
- Modify: `Program.cs` (add `GetCurrentPublicId` to `Scraper_himawari8`)

- [ ] **Step 1: Replace `PublicIdForSize` in CloudinaryUpload.cs**

Replace the existing `PublicIdForSize` method (lines 17-20) with:

```csharp
/// <summary>
/// Legacy fixed public_id — kept for backward compatibility with CDN Only mode.
/// UploadMode now uses dynamic timestamp-based IDs via Scraper_himawari8.GetCurrentPublicId().
/// </summary>
public static string PublicIdForSize(int size)
{
    return "earthlivesharp/latest_" + size + "x" + size;
}

/// <summary>
/// Build dynamic public_id from NICT image ID and size.
/// imageID format: "2026/05/29/072000"
/// Returns: "earthlivesharp/4x4/20260529_0720"
/// </summary>
public static string PublicIdFromImageId(string imageId, int size)
{
    string[] parts = imageId.Split('/');
    string timestamp = parts[0] + parts[1] + parts[2] + "_" + parts[3].Substring(0, 4);
    return string.Format("earthlivesharp/{0}x{0}/{1}", size, timestamp);
}
```

- [ ] **Step 2: Add `GetCurrentPublicId` to Scraper_himawari8 in Program.cs**

Add this private method to the `Scraper_himawari8` class, after `JoinImage` (around line 242):

```csharp
private string GetCurrentPublicId()
{
    return CloudinaryUpload.PublicIdFromImageId(imageID, Cfg.size);
}
```

- [ ] **Step 3: Verify compilation**

Run: `dotnet build` or compile via Visual Studio/msbuild
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add CloudinaryUpload.cs Program.cs
git commit -m "feat: add dynamic public_id generation from NICT timestamp"
```

---

### Task 4: Rewrite UpdateImage_UploadMode

**Files:**
- Modify: `Program.cs` — `Scraper_himawari8.UpdateImage_UploadMode()` (lines 286-354)

- [ ] **Step 1: Add `previousPublicId` field to Scraper_himawari8**

Add this field alongside existing fields (after line 110, after `stopUpdates`):

```csharp
private string previousPublicId = ""; // tracks last uploaded public_id for cleanup
```

- [ ] **Step 2: Replace `UpdateImage_UploadMode` entirely**

Replace the entire `UpdateImage_UploadMode` method (lines 286-354) with:

```csharp
private void UpdateImage_UploadMode()
{
    int size = Cfg.size;
    string wallpaperPath = string.Format("{0}\\wallpaper.bmp", Cfg.image_folder);

    // Step 1: Get latest image ID from NICT (confirms official time slot)
    if (GetImageID() == -1)
    {
        Trace.WriteLine("[upload_mode] NICT GetImageID failed");
        lastUpdateStatus = "all_sources_failed";
        return;
    }

    // Step 2: Calculate dynamic public_id from confirmed NICT timestamp
    string dynamicId = GetCurrentPublicId();

    // Step 3: Probe CDN for this dynamic_id
    bool cdnHasResource;
    try
    {
        cdnHasResource = CloudinaryUpload.ProbeExists(dynamicId, Cfg.cloud_name);
    }
    catch (Exception ex)
    {
        Trace.WriteLine("[upload_mode] HEAD probe failed — stopping updates: " + ex.Message);
        stopUpdates = true;
        lastUpdateStatus = "upload_failed_stop";
        return;
    }

    if (cdnHasResource)
    {
        // CDN hit — download directly, skip NICT tiles
        try
        {
            if (CloudinaryUpload.DownloadFromCloudinary(dynamicId, Cfg.cloud_name, wallpaperPath))
            {
                Trace.WriteLine("[upload_mode] served from CDN cache: " + dynamicId);
                lastUpdateStatus = "cdn_hit";
                return;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[upload_mode] CDN download failed after HEAD hit: " + ex.Message);
        }
        lastUpdateStatus = "all_sources_failed";
        return;
    }

    // CDN miss — fetch from NICT source station
    if (!imageID.Equals(last_imageID))
    {
        int originalSource = Cfg.source_selection;
        Cfg.source_selection = 0;
        bool saveOk = (SaveImage() == 0);
        Cfg.source_selection = originalSource;
        if (saveOk) JoinImage();
        else
        {
            Trace.WriteLine("[upload_mode] NICT tile download failed");
            lastUpdateStatus = "all_sources_failed";
            return;
        }
    }

    // Upload to Cloudinary
    try
    {
        bool ok = CloudinaryUpload.UploadImage(
            wallpaperPath, dynamicId,
            Cfg.cloud_name, Cfg.api_key, Cfg.api_secret);
        if (ok)
        {
            Trace.WriteLine("[upload_mode] NICT refresh uploaded to Cloudinary: " + dynamicId);

            // Delete previous time slot's resource
            if (!string.IsNullOrEmpty(previousPublicId))
            {
                CloudinaryUpload.DeleteResource(previousPublicId, Cfg.cloud_name, Cfg.api_key, Cfg.api_secret);
            }
            previousPublicId = dynamicId;

            lastUpdateStatus = "nict_upload_ok";
        }
        else
        {
            Trace.WriteLine("[upload_mode] upload to Cloudinary failed — stopping updates");
            stopUpdates = true;
            lastUpdateStatus = "upload_failed_stop";
        }
    }
    catch (Exception ex)
    {
        Trace.WriteLine("[upload_mode] upload exception — stopping updates: " + ex.Message);
        stopUpdates = true;
        lastUpdateStatus = "upload_failed_stop";
    }
}
```

- [ ] **Step 3: Verify compilation**

Run: `dotnet build` or compile via Visual Studio/msbuild
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add Program.cs
git commit -m "refactor: rewrite UpdateImage_UploadMode with dynamic ID + CDN probe"
```

---

### Task 5: Simplify ResetState and Remove lastNictRefreshUtc

**Files:**
- Modify: `Program.cs` — field declaration (line 109) and `ResetState` (lines 428-433)

- [ ] **Step 1: Remove `lastNictRefreshUtc` field**

Remove line 109: `private DateTime lastNictRefreshUtc = DateTime.MinValue;`

- [ ] **Step 2: Simplify `ResetState`**

Replace the `ResetState` method (lines 428-433):

```csharp
public void ResetState()
{
    last_imageID = "0";
    previousPublicId = "";
    stopUpdates = false;
}
```

- [ ] **Step 3: Verify compilation**

Run: `dotnet build` or compile via Visual Studio/msbuild
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add Program.cs
git commit -m "chore: remove lastNictRefreshUtc, simplify ResetState"
```

---

### Task 6: Update Balloon Notification in mainForm

**Files:**
- Modify: `mainForm.cs` — `ShowBalloonForUpdateStatus` (lines 203-222)

- [ ] **Step 1: Update switch cases**

Replace the `ShowBalloonForUpdateStatus` method (lines 203-222) with:

```csharp
private void ShowBalloonForUpdateStatus()
{
    string status = Scrap_wrapper.LastUpdateStatus;
    if (string.IsNullOrEmpty(status)) return;
    if (status == lastNotifiedStatus) return; // already notified
    lastNotifiedStatus = status;

    switch (status)
    {
        case "cdn_hit":
            // Silent — no balloon for CDN cache hits
            break;
        case "nict_upload_ok":
            notifyIcon1.ShowBalloonTip(3000, "EarthLiveSharp", "壁纸已从源站更新并上传CDN", ToolTipIcon.Info);
            break;
        case "all_sources_failed":
            notifyIcon1.ShowBalloonTip(3000, "EarthLiveSharp", "所有图像源均不可用，请检查网络", ToolTipIcon.Error);
            break;
        case "upload_failed_stop":
            notifyIcon1.ShowBalloonTip(5000, "EarthLiveSharp", "CDN上传失败，程序已停止自动更新", ToolTipIcon.Error);
            break;
    }
}
```

- [ ] **Step 2: Verify compilation**

Run: `dotnet build` or compile via Visual Studio/msbuild
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add mainForm.cs
git commit -m "ui: update balloon notifications for cdn_hit status"
```

---

### Task 7: Build and Verify

**Files:**
- All modified files

- [ ] **Step 1: Full build**

Run: `dotnet build` or compile via Visual Studio
Expected: Build succeeds with zero errors

- [ ] **Step 2: Final commit**

```bash
git status
git diff --stat
```

Verify only these files changed: `CloudinaryUpload.cs`, `Program.cs`, `mainForm.cs`
