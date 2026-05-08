# KeyboardTaskRunner (KTR)

輕量級鍵盤/滑鼠巨集錄製與播放工具，使用硬體掃描碼模擬真實輸入，適用於遊戲自動化操作。

## 功能

- **錄製與播放** — 錄製鍵盤與滑鼠操作，精確重播
- **硬體掃描碼** — 使用 `SendInput` + `KEYEVENTF_SCANCODE`，模擬硬體層級輸入
- **視窗鎖定** — 可指定目標視窗，播放時自動切換至前景
- **速度控制** — 0.1x ~ 99x 倍速調整
- **重複播放** — 指定次數或無限循環
- **時間抖動** — 隨機偏移延遲時間（1%~50%），方向鍵與空白鍵不受影響
- **滑鼠錄製** — 可選擇是否錄製滑鼠移動與點擊（預設關閉）
- **高精度計時** — 使用 `QueryPerformanceCounter` 絕對時間戳 + `timeBeginPeriod(1)` 確保毫秒級精度
- **儲存/載入** — 自訂 `.ktr` 二進位格式

## 快捷鍵

| 按鍵 | 功能 |
|------|------|
| F8   | 開始/停止錄製 |
| F12  | 開始/停止播放 |

## 介面說明

```
[視窗選擇 ▼]                [↻] [💾] [📂] [模式 ▼] [🔍]
[⏺ F8] [▶ F12]  速度[1.0] 重複[100] [∞] [抖][8] [鼠]
```

- **視窗選擇** — 選擇要操控的目標視窗
- **↻** — 重新整理視窗列表
- **💾 / 📂** — 儲存/載入巨集檔案（.ktr）
- **模式** — 輸入注入方式（一般 / HID）
- **🔍** — 開啟診斷視窗，即時顯示按鍵/滑鼠事件的 INJECTED 旗標
- **速度** — 播放倍速（1.0 = 原速）
- **重複** — 播放次數
- **∞** — 勾選時無限重複
- **抖** — 啟用時間抖動，右側數字為抖動百分比
- **鼠** — 勾選時錄製滑鼠事件

### 按鈕顏色

| 顏色 | 狀態 |
|------|------|
| 灰色 | 閒置 |
| 紅色 | 錄製中 |
| 綠色 | 已錄製，可播放 |
| 橘色 | 播放中 |

## 建置與發布

需要 .NET 10 SDK。

```bash
# 建置
dotnet build -c Release

# 發布為單一執行檔
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

產出位於 `bin/Release/net10.0-windows/win-x64/publish/`，只需複製 `KeyboardTaskRunner.exe` 即可，不需要 `.pdb` 檔。

## 系統需求

- Windows 10/11
- 需以**系統管理員**身分執行（繞過 UIPI 限制）

## 輸入模式

KTR 提供兩種輸入注入方式：

### 一般模式（預設）
使用 `SendInput` API 注入輸入，自動安裝低階鉤子清除 `LLKHF_INJECTED` 旗標，讓其他 user-mode 程式看到的事件像真實鍵盤輸入。**對核心層反外掛無效。**

### HID 模式
透過 [Interception](https://github.com/oblitum/Interception) 核心驅動在裝置堆疊層注入輸入，事件帶有真實硬體輸入的特徵（無 INJECTED 旗標）。

**啟用 HID 模式步驟：**

1. 下載 [Interception 最新 release](https://github.com/oblitum/Interception/releases)
2. 解壓縮後以**系統管理員**身分執行：
   ```
   install-interception.exe /install
   ```
3. **重新開機**
4. 開啟 KTR，模式選單選擇 **HID**

> ⚠️ Interception 驅動的簽章已被多數核心級反外掛（EAC、BattlEye、Vanguard、Nexon NGS）列入黑名單。請僅在自有測試環境使用。

## 診斷視窗

點 🔍 開啟診斷視窗，可即時看到每個鍵盤/滑鼠事件的來源：

- **綠色 [REAL]** — 來自真實硬體（或 HID 模式）
- **紅色 [INJECTED]** — 來自 SendInput（一般模式且診斷視窗開啟時）

> 診斷視窗開啟時，一般模式不會自動清除 INJECTED 旗標，方便驗證兩種模式的差異。

## 技術細節

- 使用 `WH_KEYBOARD_LL` / `WH_MOUSE_LL` 低階掛鉤錄製輸入
- 播放使用絕對時間戳避免累積誤差
- 自動過濾 OS 產生的重複 KeyDown 事件（長壓時只錄首次按下與放開）
- 滑鼠移動節流：距離 <5px 且間隔 <8ms 時跳過
- 過濾 `LLKHF_INJECTED` 事件，不會錄到自身播放的輸入
- `.ktr` 格式：`KTR1` magic number + 版本號 + 事件陣列

## 已知限制

- `SendInput` 產生的輸入帶有 `LLKHF_INJECTED` 旗標，這是使用者模式無法避免的
- 不支援背景視窗操控（遊戲通常使用 DirectInput/Raw Input，`PostMessage` 無效）
- 播放時會將目標視窗切至前景
