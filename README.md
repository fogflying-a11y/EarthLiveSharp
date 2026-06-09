# EarthLiveSharp - CDN Fetch Edition

🌍 **实时地球卫星壁纸** — 从 [Himawari-8](https://himawari.asia/) 气象卫星获取实时地球图像，通过 Cloudinary CDN 代理加速，设置为桌面壁纸
<img width="1920" height="1080" alt="image" src="https://github.com/user-attachments/assets/8ded6af7-8ac0-4f33-bf59-8a7e2808a0a4" />


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

### ⏱️ 本地时间戳量化（零网络请求）

**原项目**：每次更新需要请求 NICT `latest.json`（~3KB）获取最新时间戳

**本项目**：本地计算时间戳，**无需任何网络请求**：
- UTC 时间 - 90 分钟（卫星数据发布延迟）
- 向下取整到整小时
- 格式：`yyyy/MM/dd/HH0000`

**效果：** 每个更新周期节省 ~3KB 网络请求

### 🗑️ 删除了复杂的多设备协作机制

为了保持代码简洁，**移除了以下功能**：
- ❌ `UploadMode` — 不再需要 Cloudinary Upload API
- ❌ `api_key` / `api_secret` — 不再需要 API 密钥
- ❌ `ProbeExists()` — 不再需要 HTTP HEAD 探测
- ❌ `DeleteResource()` — 不再需要删除旧资源
- ❌ `lastNictRefreshUtc` — 不再需要复杂的状态管理
- ❌ NICT API 调用 — 不再需要 `GetImageID()` 等

### 📦 极简代码架构

**CloudinaryUpload.cs**（仅 57 行）：
- `BuildFetchUrl()` — 构建 Cloudinary fetch URL
- `DownloadFile()` — 下载文件（支持 fetch 代理和直连）

**Program.cs** 核心逻辑：
- `GetQuantizedImageId()` — 本地计算时间戳
- `BuildOriginUrl()` — 构建 himawari.asia 瓦片 URL
- `SaveImage()` — CDN 模式通过 Cloudinary 下载，Origin 模式直连下载
- `JoinImage()` — 拼接瓦片为完整壁纸

### 🔒 稳定性改进

- ✅ **TLS 1.2 强制启用**
- ✅ **读写超时控制** — `Timeout=15s`，`ReadWriteTimeout=30s`
- ✅ **简洁的错误处理** — 失败直接返回，不重试

### 💬 UI 气球通知

| 状态 | 行为 |
|------|------|
| `success` | 提示"壁纸已更新" |
| `download_failed` | 错误提示"图像下载失败，请检查网络" |

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
  ├─ 1. GetQuantizedImageId() → 本地计算时间戳
  │     UTC - 90分钟，向下取整到整小时
  │     格式: "2026/05/29/130000"
  │     无网络请求
  │
  ├─ 2. 检查是否与上次相同
  │     相同 → 跳过
  │     不同 → 继续
  │
  ├─ 3. SaveImage() → 下载瓦片
  │     │
  │     ├─ CDN 模式:
  │     │   BuildOriginUrl() → himawari.asia URL
  │     │   BuildFetchUrl() → Cloudinary fetch URL
  │     │   DownloadFile() → 下载
  │     │
  │     └─ Origin 模式:
  │         BuildOriginUrl() → himawari.asia URL
  │         DownloadFile() → 直接下载
  │
  ├─ 4. JoinImage() → 拼接瓦片为完整壁纸
  │
  └─ 5. 设置壁纸 + 气球通知
```

### 文件结构

```
EarthLiveSharp/
├── CloudinaryUpload.cs     # Cloudinary fetch 代理 (57行)
│   ├── BuildFetchUrl()     # 构建 CDN URL
│   └── DownloadFile()      # 下载文件
├── Program.cs              # 主逻辑
│   ├── GetQuantizedImageId() # 本地计算时间戳
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
- ⏱️ 本地时间戳量化（零网络请求）
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
