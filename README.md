# Server-Sent Events 課程（.NET 9 + 原生 JavaScript）

這是一個透過 Git branch 一步步學習 Server-Sent Events（SSE）的實作課程。
後端使用 **.NET 9 Minimal API**，前端使用**原生 HTML/JavaScript（EventSource API）**，
不依賴任何前端框架，讓你專注在 SSE 協定本身的機制。

## 專案結構

```
backend/
  Program.cs        後端進入點，所有 API 都寫在這裡（教學用途，刻意不拆檔案）
  wwwroot/           前端靜態檔案（index.html + app.js），由後端直接託管
```

後端與前端同一個專案託管（`app.UseStaticFiles()` + `app.UseDefaultFiles()`），
避免額外處理 CORS，讓你可以專心在 SSE 機制上。

## 如何使用這個課程

每個步驟都是一個獨立的 git branch，後一個步驟會在前一個步驟的程式碼基礎上疊加。

```bash
git checkout step-1-basic-sse
cd backend
dotnet run
# 開瀏覽器到 http://localhost:5080
```

想看某一步驟「新增了什麼」，可以直接 diff 相鄰兩個 branch：

```bash
git diff step-1-basic-sse step-2-event-format
```

## 課程大綱

| Branch | 主題 | 學習重點 |
|---|---|---|
| `step-1-basic-sse` | 最基本的 SSE 推播 | `text/event-stream`、手動寫入 Response、`EventSource` 基本用法 |
| `step-2-event-format` | SSE 協定格式 | `event:`、`id:`、多行 `data:`、`retry:`、具名事件監聽 |
| `step-3-reconnect` | 自動重連與 Last-Event-ID | 斷線自動重連、`Last-Event-ID` header、補送遺漏訊息 |
| `step-4-broadcast` | 多客戶端廣播 | `Channel<T>`、連線管理、一對多推播（多分頁同步收到訊息） |
| `step-5-heartbeat-cleanup` | 心跳與資源清理 | Keep-alive 心跳、偵測斷線、`CancellationToken` 清理連線資源 |

每個 branch 的根目錄都有一份 `STEP.md`，說明：
- 這一步要學什麼、為什麼重要
- 程式碼的關鍵改動與講解
- 怎麼動手測試（含瀏覽器操作步驟）

## 先備知識

- 熟悉 C# 與基本 ASP.NET Core（Minimal API）語法
- 熟悉 HTML/JavaScript 基礎（不需要框架經驗）
- 了解 HTTP 的基本觀念（header、streaming response）

## 環境需求

- .NET 9 SDK
- 任一現代瀏覽器（Chrome/Edge/Firefox 皆支援 `EventSource`）
