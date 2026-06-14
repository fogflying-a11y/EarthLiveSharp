# EarthLiveSharp-CDN 项目规则

## 硬性规则（必须遵守）

### 构建与部署
- 使用 Visual Studio 编译，不要尝试命令行 MSBuild（需要完整 Windows SDK）
- 修改 `.resx` 文件后必须重新编译才能看到效果
- `git push` 前必须先 `git pull` 同步远程变更

### 代码规范
- PictureBox 图片加载使用 `FileStream` + `MemoryStream` + `Bitmap`，避免文件锁定
- 图片缩放优先在代码中完成（`Graphics.DrawImage`），SizeMode 作为兜底
- 日期格式化使用转义斜杠：`ToString("yyyy\\/MM\\/dd\\/HH0000")`，避免文化敏感分隔符
- URL 编码时只编码冒号 `:` → `%3A`，保留斜杠 `/` 为字面量

### 配置管理
- 版本号在三处同步：`App.config`、`AssemblyInfo.cs`、`EarthLiveSharp.csproj`
- `wallpaper.bmp` 是运行时动态生成，不要手动修改或提交到 git

## 核心架构（快速参考）

### 文件职责
- `mainForm.cs` + `mainForm.Designer.cs` — 主界面（含 PictureBox 实时预览）
- `mainForm.resx` — 布局资源（通过 `ApplyResources()` 运行时加载）
- `Program.cs` — CDN fetch 代理 + 源站直连逻辑
- `CloudinaryUpload.cs` — Cloudinary fetch URL 构建（仅 57 行）
- `Cfg.cs` — 配置读取（从 App.config）

### 数据流
1. `GetQuantizedImageId()` 生成量化时间戳（本地计算，无需网络）
2. CDN 模式：构建 Cloudinary fetch URL → 下载拼接瓦片
3. 源站模式：直接构造 Himawari-8 URL → 下载
4. 拼接为 `wallpaper.bmp` → PictureBox 加载预览 → 设置为桌面壁纸

### 关键参数
- 支持分辨率：550/1100/2200/4400（1×1, 2×2, 4×4, 8×8 瓦片）
- CDN 代理：Cloudinary fetch（需 cloud_name 配置）
- 当前版本：v3.12

## 常见陷阱（避免重复踩坑）

1. **resx 修改不生效** → 忘记重新编译
2. **git push 被拒** → 忘记先 pull
3. **日期格式错误** → 未转义 `/` 和 `:` 分隔符
4. **URL 过度编码** → 使用 `UrlEncode()` 而非手动替换
5. **图片显示不全** → SizeMode 设置不当
