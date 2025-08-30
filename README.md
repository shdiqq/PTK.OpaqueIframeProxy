# PTK.OpaqueIframeProxy

Library internal untuk melakukan **proxy konten HTML di `<iframe>`** dan **proxy gambar** dengan aman.  
Mendukung tokenized URL, whitelist host, slug HMAC, serta TagHelper Razor untuk memudahkan integrasi.

---

## Table of Contents
- [✨ Features](#-features)
- [A) Installation (Step-by-step)](#a-installation-step-by-step)
- [B) Configuration (General)](#b-configuration-general)
- [C) Usage Scenarios (Pick what fits your app)](#c-usage-scenarios-pick-what-fits-your-app)
- [D) Iframe & Security Recommendations](#d-iframe--security-recommendations)
- [E) How it works](#e-how-it-works)
- [F) Performance & Caching](#f-performance--caching)
- [G) Diagnostics & Logging](#g-diagnostics--logging)
- [H) Compatibility Matrix](#h-compatibility-matrix)
- [I) Publishing](#i-publishing)
- [J) Troubleshooting](#j-troubleshooting)
- [K) FAQ](#k-faq)
- [L) Build](#l-build)

---

## ✨ Features
- 🔒 **Security-first**: token HTML dengan DataProtection, slug HMAC-SHA256 untuk gambar.
- 🛡️ **Host whitelisting**: hanya origin tertentu yang bisa di-proxy.
- 🔄 **Automatic rewriting**: `<img src>` diubah agar lewat proxy internal.
- ⚙️ **Configurable**: TTL token, route template, size limits.
- 🖼️ **Razor TagHelper**: `<opaque-iframe>` siap pakai.
- 📦 **NuGet-ready**: paket internal dengan struktur rapi.
- 🧩 **Multi-TFM**: mendukung .NET Core 3.1, 6, 7, 8.

---

## A) Installation (Step-by-step)

1. Tambahkan package ke project:
   ```powershell
   dotnet add package PTK.OpaqueIframeProxy
   ```

2. **Jika aplikasi .NET 6+ (Program.cs minimal hosting):**
   ```csharp
   builder.Services.AddOpaqueContentProxy(builder.Configuration);
   var app = builder.Build();
   app.MapOpaqueContentProxy();
   app.Run();
   ```

3. **Jika aplikasi .NET Core 3.1 (Startup.cs):**
   ```csharp
   public void ConfigureServices(IServiceCollection services)
   {
       services.AddControllersWithViews();
       services.AddOpaqueContentProxy(Configuration);
   }

   public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
   {
       if (env.IsDevelopment()) app.UseDeveloperExceptionPage();
       app.UseRouting();
       app.UseEndpoints(endpoints =>
       {
           endpoints.MapOpaqueContentProxy();
           endpoints.MapDefaultControllerRoute();
       });
   }
   ```

---

## B) Configuration (General)
Tambahkan di `appsettings.json`:

```json
"OpaqueProxy": {
  "AllowedHtmlHosts": [ "reports.internal.corp" ],
  "AllowedImageHosts": [ "cdn.internal.corp" ],
  "BasePath": "proxy",
  "HtmlTokenLifetime": "01:00:00",
  "MaxHtmlSizeBytes": 2097152,
  "MaxImgRewrite": 300,
  "Routes": {
    "HtmlTokenTemplate": "/{basePath}/t/{token}",
    "ImageSlugTemplate": "/{basePath}/img/s/{slug}",
    "AutoMapEndpoints": true
  }
},
"OpaqueProxyMap": {
  "MapRoot": "App_Data/img-map",
  "SlugSecret": "super-secret-key-change-me",
  "MaxFileSizeBytes": 10485760
}
```

---

## C) Usage Scenarios (Pick what fits your app)

- **Razor View**:
  ```razor
  <opaque-iframe src="https://reports.internal.corp/dashboard?id=42"
                 width="100%" height="800"
                 style="border:0"
                 sandbox="allow-scripts allow-same-origin"
                 referrerpolicy="no-referrer">
  </opaque-iframe>
  ```

- **Manual URL building**:
  ```csharp
  var builder = sp.GetRequiredService<IOpaqueIframeUrlBuilder>();
  var url = builder.Build("https://reports.internal.corp/dashboard?id=42");
  ```

---

## D) Iframe & Security Recommendations
- Gunakan atribut `sandbox` dan `referrerpolicy`.
- Batasi host melalui whitelist `AllowedHtmlHosts` dan `AllowedImageHosts`.
- Pertimbangkan menambahkan header keamanan tambahan via middleware (CSP, frame-ancestors).

---

## E) How it works
1. `IOpaqueIframeUrlBuilder` membuat token (dengan TTL).
2. Endpoint `/proxy/t/{token}` memvalidasi token, fetch HTML origin, lalu rewrite `<img>`.
3. `<img>` dialihkan ke endpoint `/proxy/img/s/{slug}`.
4. Gambar difetch hanya jika host diizinkan & ukuran <= limit.

---

## F) Performance & Caching
- HTML response default `no-store, no-cache`.
- Image response pakai `ETag` & `Cache-Control: immutable, max-age=...`.
- Batas ukuran HTML default 2 MB, gambar default 10 MB.

---

## G) Diagnostics & Logging
- Logging aktif saat token invalid/expired, host ditolak, konten oversize, dll.
- Fallback transparan: jika gambar tidak valid → diganti 1×1 PNG.

---

## H) Compatibility Matrix
- ✅ .NET 8.0 (direkomendasikan)
- ✅ .NET 7.0 (uji dasar)
- ✅ .NET 6.0 (LTS)
- ✅ .NET Core 3.1 (EOL, supported dengan catatan:  
  - menggunakan C# 9 record dengan shim `IsExternalInit`  
  - endpoint mapping memakai `RequestDelegate` lama & header diakses via indexer)
- ❌ .NET Framework (tidak didukung)

---

## I) Publishing
Untuk NuGet internal (Azure Artifacts, GitHub Packages, Nexus/Artifactory):
```bash
dotnet pack -c Release
dotnet nuget push bin/Release/PTK.OpaqueIframeProxy.*.nupkg -s <FEED_URL> -k <TOKEN>
```

---

## J) Troubleshooting
- **Token expired** → periksa `HtmlTokenLifetime` atau override `ttl`.
- **Host not allowed** → tambahkan host ke whitelist config.
- **Image exceeds MaxFileSizeBytes** → naikkan limit atau fallback 1×1 PNG.
- **HTML too large** → adjust `MaxHtmlSizeBytes`.

---

## K) FAQ
**Q: Apakah library ini bisa dipakai untuk host eksternal (misal YouTube)?**  
A: Tidak. Hanya host yang ada di `AllowedHtmlHosts` & `AllowedImageHosts`.  

**Q: Apa beda TagHelper vs manual URL?**  
A: TagHelper memudahkan Razor; manual URL builder untuk backend code.  

**Q: Kenapa masih support .NET Core 3.1 padahal EOL?**  
A: Untuk kompatibilitas sistem lama. Direkomendasikan upgrade ke .NET 6/8 LTS.

---

## L) Build
```bash
dotnet build
dotnet pack   # menghasilkan nupkg
```
