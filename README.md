# EarthLiveSharp - CDN Fetch Edition

🌍 **实时地球卫星壁纸** — 从 [Himawari-8](https://himawari.asia/) 气象卫星获取实时地球图像，通过 Cloudinary CDN 代理加速，设置为桌面壁纸
<img width="1918" height="1080" alt="image" src="https://github.com/user-attachments/assets/225974e0-ec73-424c-a7b6-86ff956db1eb" />



> 本项目是 [bitdust/EarthLiveSharp](https://github.com/bitdust/EarthLiveSharp) 的简化重构分支，专注于 **CDN fetch 代理模式** 和 **极简架构**

---

## ✨ 相对于原项目的主要更新

### 🌐 图像源改用 himawari.asia 镜像站

- **原项目**：使用 NICT 官方源站 (`himawari8.nict.go.jp`)
- **本项目**：使用 `himawari.asia` 镜像站
- 镜像站在国内访问更稳定，速度更快

### 🚀 Cloudinary Fetch 代理模式（核心特性）

使用 Cloudinary 的 `fetch` API 作为 CDN 代理层，**无需上传、无需 API Key**：

```
用户请求 → Cloudinary CDN → himawari.asia 源站
           (自动缓存+优化)
```

**优势：**
- ✅ **无需 API Key** — 只需要 `cloud_name`
- ✅ **自动缓存** — Cloudinary CDN 边缘节点缓存图片
- ✅ **格式优化** — `f_auto,q_auto` 自动选择最佳格式和质量
- ✅ **全球加速** — Cloudinary CDN 节点覆盖全球

**工作原理：**
```
原始瓦片 URL: https://himawari.asia/img/D531106/4d/550/{timestamp}_{ii}_{jj}.png
      ↓ Cloudinary fetch 代理
CDN URL: https://res.cloudinary.com/{cloud_name}/image/fetch/f_auto,q_auto/{original_url}
```

### ⏱️ API 时间戳获取（精确到 10 分钟）

**原项目**：请求 NICT 官方源 `latest.json` 获取最新时间戳

**本项目**：请求 `himawari.asia` 镜像站的 `latest.json`（~3KB）获取最新时间戳：
- 源站发布即可获取，无需本地估算延迟
- 精确到 10 分钟帧级别（Himawari-8 实际更新频率）
- 格式：`yyyy/MM/dd/HHMM00`
- API 失败时保留当前壁纸，不盲目下载

### 🗑️ 删除了复杂的多设备协作机制

为了保持代码简洁，**移除了以下功能**：
- ❌ `UploadMode` — 不再需要 Cloudinary Upload API
- ❌ `api_key` / `api_secret` — 不再需要 API 密钥
- ❌ `ProbeExists()` — 不再需要 HTTP HEAD 探测
- ❌ `DeleteResource()` — 不再需要删除旧资源
- ❌ `lastNictRefreshUtc` — 不再需要复杂的状态管理

### 📦 极简代码架构

**CloudinaryUpload.cs**：
- `BuildFetchUrl()` — 构建 Cloudinary fetch URL
- `DownloadFile()` — 源站直连下载（单次尝试，失败返回 false）
- `DownloadFileWithRetry()` — CDN 下载（瞬时失败按指数退避重试）
- `DownloadFileCore()` — 下载核心实现（失败抛异常，供重试逻辑区分错误类型）
- `IsTransient()` — 判定是否为瞬时错误（超时/连接类/5xx）

**Program.cs** 核心逻辑：
- `GetLatestImageId()` — 请求 API 获取最新时间戳
- `BuildOriginUrl()` — 构建 himawari.asia 瓦片 URL
- `SaveImage()` — CDN 模式通过 Cloudinary 下载，Origin 模式直连下载
- `JoinImage()` — 拼接瓦片为完整壁纸

### 🔒 稳定性改进

- ✅ **TLS 1.2 强制启用**
- ✅ **显式超时控制** — 连接+响应 与 流读写 均为 30s（`Timeout=30s`，`ReadWriteTimeout=30s`）
- ✅ **CDN 下载重试机制** — 瞬时失败（超时/连接类错误/5xx）按指数退避 2s→4s→8s 最多重试 3 次
- ✅ **错误分类** — 4xx（400/401/403/404）视为非瞬时错误，不重试，直接放弃本轮
- ✅ **漏帧修复** — 仅在下载并拼图成功后更新 `last_imageID`，避免瞬时失败导致下一周期误判"unchanged"而跳过该帧

### 💬 UI 气球通知

| 状态 | 行为 |
|------|------|
| `success` | 提示"壁纸已更新" |
| `download_failed` | 错误提示"图像下载失败，请检查网络" |
| `api_failed` | 警告提示"无法获取最新卫星数据，请检查网络" |

### 🧹 配置简化

**App.config 保留项**：
- `source_selection` — 0: 源站直连 / 1: CDN 代理
- `cloud_name` — Cloudinary 云名称（CDN 模式必需）

**已移除项**：
- `api_key` / `api_secret` — 不再需要
- `upload_mode` — 不再需要
- 多语言支持文件（简化界面）

---

## 📥 下载

[Releases 页面](https://github.com/fogflying-a11y/EarthLiveSharp/releases) 下载最新版本。

## 🔧 配置说明

### 两种模式对比

| 模式 | 数据源 | 是否需要 Cloudinary | 说明 |
|------|--------|---------------------|------|
| **源站直连 (Origin)** | himawari.asia | ❌ 不需要 | 直接从镜像站下载瓦片 |
| **CDN 代理 (CDN)** | himawari.asia → Cloudinary | ✅ 需要 cloud_name | **推荐**：通过 Cloudinary 加速 |

### Cloudinary 配置（CDN 模式）

1. 注册 [Cloudinary](https://cloudinary.com/) 账户（免费版即可）
2. 获取 `cloud_name`（在 Dashboard 首页可见）
3. 在设置界面：
   - 选择 **CDN** 模式
   - 填写 `cloud_name`
4. 保存配置

> **注意**：CDN 模式只需要 `cloud_name`，**不需要 `api_key` 和 `api_secret`**

---

## 📸 截图

### 原项目截图

![screenshot1](https://cloud.githubusercontent.com/assets/6072743/23821657/7d53554e-0674-11e7-8ccc-260070261967.png)

![sample](https://cloud.githubusercontent.com/assets/6072743/11613290/6af013a8-9c56-11e5-8d7e-553cc8226d5a.png)

---

## 🏗️ 技术架构

### 控制流

```
UpdateImage()
  │
  ├─ 1. GetLatestImageId() → 请求 himawari.asia/img/D531106/latest.json
  │     获取源站最新时间戳（精确到 10 分钟）
  │     格式: "2026/06/26/062000"
  │     失败 → api_failed，保留当前壁纸
  │
  ├─ 2. 检查是否与上次相同
  │     相同 → 跳过（same_image）
  │     不同 → 继续
  │
  ├─ 3. SaveImage() → 下载瓦片
  │     │
  │     ├─ CDN 模式:
  │     │   BuildOriginUrl() → himawari.asia URL
  │     │   BuildFetchUrl() → Cloudinary fetch URL
  │     │   DownloadFileWithRetry() → 下载（瞬时失败重试 3 次，退避 2/4/8s）
  │     │
  │     └─ Origin 模式:
  │         BuildOriginUrl() → himawari.asia URL
  │         DownloadFile() → 直接下载（单次尝试，维持原行为）
  │
  ├─ 4. JoinImage() → 拼接瓦片为完整壁纸
  │
  └─ 5. 设置壁纸 + 气球通知
```

### 文件结构

```
EarthLiveSharp/
├── CloudinaryUpload.cs     # Cloudinary fetch 代理 + 下载/重试
│   ├── BuildFetchUrl()         # 构建 CDN URL
│   ├── DownloadFile()          # 源站直连（单次尝试）
│   ├── DownloadFileWithRetry() # CDN 下载（瞬时失败重试）
│   ├── DownloadFileCore()      # 下载核心实现（失败抛异常）
│   └── IsTransient()           # 判定瞬时错误
├── Program.cs              # 主逻辑
│   ├── GetLatestImageId()  # API 获取最新时间戳
│   ├── BuildOriginUrl()    # 构建源站 URL
│   ├── SaveImage()         # 下载瓦片
│   └── JoinImage()         # 拼接图片
├── Cfg.cs                  # 配置管理
├── mainForm.cs             # 主界面
├── settingsForm.cs         # 设置界面
├── Wallpaper.cs            # 壁纸设置
└── App.config              # 配置文件
```

---

## 📋 更新日志

### 2026-07-28 — CDN 下载重试机制 + 漏帧修复

完全恢复从（主分支 EarthLiveSharp）CDN 更新的逻辑，并增加重试机制。

**1. CloudinaryUpload.cs 重构（下载 + 重试）**
- 新增 `DownloadFileCore()`：下载核心实现，显式设置 `Timeout` 与 `ReadWriteTimeout` 均为 30s，失败时抛出异常，供上层区分错误类型。
- 新增 `DownloadFileWithRetry()`：CDN 下载专用，瞬时失败按**指数退避 2s→4s→8s 最多重试 3 次**；非瞬时错误或重试耗尽返回 false。
- 新增 `IsTransient()`：判定瞬时错误——超时/连接类错误（Timeout、ConnectFailure、ConnectionClosed、KeepAliveFailure、SendFailure、ReceiveFailure）或 5xx；4xx（400/401/403/404）视为非瞬时，不重试。
- 保留 `DownloadFile()`：源站直连单次尝试（吞掉异常返回 bool），维持原有行为。

**2. Program.cs 接入重试**
- `SaveImage()` 的 CDN 分支由 `DownloadFile` 改为 `CloudinaryUpload.DownloadFileWithRetry(fetchUrl, image_path)`，以单个瓦片为粒度重试；任一瓦片重试耗尽仍失败则整轮放弃（返回 -1）。
- Origin 直连分支与 Bing 壁纸逻辑均不受影响。

**3. 漏帧修复（`last_imageID`）**
- 原逻辑无论下载成功与否都会更新 `last_imageID`，瞬时失败后下一周期会误判为"same_image"而**跳过该帧**。
- 现仅在 `SaveImage()` 成功并完成 `JoinImage()` 后才更新 `last_imageID`。



### 2026-06-27 — 时间戳获取改为 API 模式

- 🔄 `GetQuantizedImageId()` → `GetLatestImageId()`：从本地计算改为请求 `himawari.asia/img/D531106/latest.json`
- ⏱️ 时间精度从整小时提升到 10 分钟帧级别
- 🛡️ API 失败时保留当前壁纸 + 气球警告通知（`api_failed`）
- 📉 数据延迟从 1.5~2.5 小时缩短至约 20~30 分钟

### 2026-06-09 — UI 重构：实时壁纸预览

- 🖼️ 主界面图片区域改为实时显示 wallpaper.bmp 预览图
- 📐 窗口尺寸调整为 660×760，图片区域 576×576
- ⚡ 下载成功后自动刷新预览（550/1100/2200/4400 均自动缩放至 640×640）
- 🎨 状态标签 (Running/Not Running) 加粗，字号与标题一致
-  移除 Banner2.png 嵌入资源，改用文件流加载避免锁定
- 🗑️ 清理 .resx 中约 5000+ 行旧 base64 图片数据

### 2026-06-09 — CDN Fetch 重构

- 🌐 图像源改为 `himawari.asia` 镜像站
- 🚀 使用 Cloudinary fetch 代理模式
- 🗑️ 删除 Upload Mode 和多设备协作机制
- 📦 精简代码架构
- 🔧 TLS 1.2 强制支持
- 💬 简化气球通知

### 原项目最后更新 (2020-09-08)

- 修复上游 API 变更
- 多语言支持

---

## 🙏 致谢

感谢原项目作者 [bitdust](https://github.com/bitdust) 的贡献！

原项目：https://github.com/bitdust/EarthLiveSharp

卫星图像源：https://himawari.asia/

---

## 📄 许可证

继承原项目许可证，详见 [LICENSE](LICENSE)

---

## 🔗 其他平台

- **Android**: [Mantou Earth](https://github.com/oxoooo/earth)
- **Linux/macOS**: [社区脚本合集](https://www.v2ex.com/t/241563)
