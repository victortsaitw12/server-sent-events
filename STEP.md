# Step 1：最基本的 SSE 推播

## 這一步要學什麼

Server-Sent Events 的本質其實很單純：**伺服器把 HTTP response 的 Content-Type
設成 `text/event-stream`，然後持續不斷地寫入資料、不關閉連線**。瀏覽器看到這個
Content-Type，就知道要用串流的方式讀取，而不是等整個 response 結束才處理。

這一步刻意不用任何框架包裝，直接手動組出最陽春的 SSE response，讓你看清楚
它底層到底在做什麼。

## 後端關鍵程式碼（`backend/Program.cs`）

```csharp
context.Response.Headers.ContentType = "text/event-stream";
context.Response.Headers.CacheControl = "no-cache";
```

- `text/event-stream` 是 SSE 的標準 MIME type，瀏覽器靠它判斷要用 `EventSource`
  的串流解析邏輯。
- `Cache-Control: no-cache` 避免中間的 proxy/瀏覽器快取這個一直在變動的串流。

```csharp
await context.Response.WriteAsync(message, cancellationToken);
await context.Response.Body.FlushAsync(cancellationToken);
```

- SSE 訊息最簡單的格式是 `data: <內容>\n\n`（**注意結尾一定要兩個換行**，這是
  一則訊息的結束標記）。
- ASP.NET Core 預設會緩衝 response，如果不手動 `FlushAsync`，資料會卡在伺服器
  端的緩衝區，瀏覽器完全收不到東西，直到 response 結束。

```csharp
var cancellationToken = context.RequestAborted;
```

- `HttpContext.RequestAborted` 會在使用者關閉分頁、瀏覽器主動斷線時被觸發，
  是我們判斷「該停止這個迴圈了」的依據。這一步先簡單處理，後面 Step 5 會更完整
  地講連線清理。

## 前端關鍵程式碼（`backend/wwwroot/app.js`）

```javascript
const source = new EventSource("/sse/time");
source.onmessage = (event) => { ... };
```

- `EventSource` 是瀏覽器原生 API，不需要安裝任何套件。
- 它只能發 **GET** 請求（這是後面 Step 3 要處理「認證」時會遇到的限制之一）。
- 沒有指定事件名稱的訊息（也就是純 `data: ...`）會觸發 `onmessage`。

## 動手試試看

```bash
cd backend
dotnet run
```

開瀏覽器到 `http://localhost:5080`，應該會看到每秒新增一行目前時間。

再試著：

1. 打開瀏覽器開發者工具的 **Network** 面板，點選 `/sse/time` 這個請求，
   觀察它的 Response 頁籤——你會看到資料是「持續增加」的，而不是一次性回來。
2. 直接用 curl 觀察最原始的資料格式：
   ```bash
   curl -N http://localhost:5080/sse/time
   ```
   `-N` 會關閉 curl 的緩衝，讓你即時看到每一則 `data: ...` 訊息。

## 下一步

Step 2 會加上具名事件（`event:`）、多行資料、`id:` 與 `retry:`，讓你完整認識
SSE 訊息的協定格式。
