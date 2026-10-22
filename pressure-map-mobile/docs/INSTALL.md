# 安装说明

## Android

安装包：`dist/PressureMap-android-1.0.0.apk`（92KB，minSdk 26 / Android 8.0 及以上）

### 方式一：手机直接装

1. 用微信/QQ/数据线/网盘把 APK 传到手机。
2. 在文件管理器里点开 APK。
3. 系统提示「禁止安装未知应用」时，点「设置」→ 允许该来源（文件管理器 / 浏览器）安装应用。
4. 返回继续安装，完成后桌面出现「学期压力地图」。
5. 部分国产 ROM（MIUI、ColorOS 等）还会弹「风险提示」，选「继续安装」，必要时关闭「纯净模式」。

### 方式二：adb

```bash
adb install -r dist/PressureMap-android-1.0.0.apk
```

### 说明

- 应用没有申请网络权限，装上后不会联网、不会上传数据。
- 首次打开会载入示例学期；不想要就在「设置 → 数据 → 清空全部数据」。
- 覆盖安装（同签名）不会丢数据；换签名安装需要先卸载。

---

## iOS

### 方式一：Xcode 装到自己手机（免费 Apple ID 即可）

需要 macOS + Xcode 15 或更新。

```bash
bash scripts/prepare-ios.sh
open ios/PressureMap.xcodeproj
```

1. Xcode 左侧选中工程 → TARGETS `PressureMap` → **Signing & Capabilities**。
2. **Team** 选你的个人团队（没有就点 Add an Account，用 Apple ID 登录，免费）。
3. 如果提示 bundle id 冲突，把 `com.pressuremap.mobile` 改成 `com.你的名字.pressuremap`。
4. iPhone 用数据线连电脑，顶部设备选择你的手机 → 点 ▶️ Run。
5. 手机上首次运行会提示「不受信任的开发者」，去 **设置 → 通用 → VPN 与设备管理** 里信任该开发者。
6. 7 天后应用会打不开（免费签名有效期），重新 Run 一次即可。

### 方式二：打包 IPA

```bash
TEAM_ID=你的团队ID bash ios/build-ios.sh
```

产物在 `ios/build/ipa/PressureMap.ipa`。安装用 Xcode 的 Devices and Simulators 窗口，或 Apple Configurator 拖入。

- `ios/ExportOptions.plist` 里的 `method` 决定分发方式：`development`（自己的设备）/ `ad-hoc`（需登记 UDID）/ `app-store`（TestFlight 或上架）。
- 上架 App Store 需要付费开发者帐号（$99/年）。

### 方式三：没有 Mac 时先「添加到主屏幕」

把 `www/` 目录部署到任意 **HTTPS** 静态空间（例如现有官网服务器 `../pressure-map-website` 的 dist 旁边新建一个目录），然后：

1. iPhone 用 **Safari** 打开该地址。
2. 点底部「分享」→「添加到主屏幕」。
3. 桌面会出现图标，全屏运行，数据存在 Safari 本地。

限制：iOS 清理 Safari 数据会清掉本地数据，也没有原生备份入口（只能先在网页里「导出备份」）。要长期用、要离线可靠，还是走方式一/二。

---

## 数据迁移

- 导出：设置 → 数据 → 导出备份（JSON）。Android 存到 `下载/PressureMap/pressuremap-backup-YYYY-MM-DD.json`；iOS 存到「文件」App 的 `我的 iPhone/PressureMap/`。
- 导入：设置 → 数据 → 导入备份，把 JSON 内容粘贴进去。两台手机之间搬数据就走这条。
