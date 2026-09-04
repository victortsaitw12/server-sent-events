# SSE 課程 - Step 3：自動重連與 Last-Event-ID

> 這是 [Server-Sent Events 課程](https://github.com/victortsaitw12/server-sent-events)（.NET 9 + 原生 JavaScript）的第 3 步，共 5 步。
> 回到 [課程總覽](https://github.com/victortsaitw12/server-sent-events/blob/main/README.md) ・ 上一步：[`step-2-event-format`](https://github.com/victortsaitw12/server-sent-events/tree/step-2-event-format) ・ 下一步：[`step-4-broadcast`](https://github.com/victortsaitw12/server-sent-events/tree/step-4-broadcast)

## 這一步要學什麼

SSE 最強大的地方之一，是**瀏覽器內建自動重連機制**，而且重連時會自動帶上
`Last-Event-ID` header，讓伺服器有機會「補送」客戶端錯過的訊息。這一步會
讓你實際觀察到這整個流程，而不是只看文件描述。

## 教學用的設計：故意斷線

正式環境的伺服器不會沒事就把連線關掉，但為了讓你不用手動操作（例如拔網路線）
就能看到重連效果，這一步的 `/sse/counter` endpoint **故意每送出 8 則訊息就
主動關閉一次連線**（見 `Program.cs` 裡的 `ticksOnThisConnection >= 8`）。

搭配這件事的是 `CounterFeed`（一個 `BackgroundService`）：它背景持續每秒把
計數器加一，**跟任何一個 HTTP 連線的生命週期完全無關**。這很重要——如果計數
只在連線內產生，斷線期間就不會有「被錯過的訊息」可以補送，這個示範就沒意義了。

## 後端關鍵程式碼（`backend/Program.cs`）

判斷這是不是一個「重連」的請求：

```csharp
if (context.Request.Headers.TryGetValue("Last-Event-ID", out var header) &&
    long.TryParse(header, out var lastEventId))
{
    var missed = feed.GetMessagesAfter(lastEventId);
    // ...補送 missed 裡的每一則訊息
}
```

- `Last-Event-ID` 是瀏覽器自動加上的 header，值是它最後一次成功收到的 `id:`。
- 這一步用 `event: replay` 跟平常的 `event: tick` 區分「補送的舊訊息」跟
  「即時的新訊息」，方便你在畫面上一眼看出差異。

`CounterFeed` 用一個有上限的 List 當作簡易的歷史紀錄（ring buffer）：

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await Task.Delay(1000, stoppingToken);
        lock (_lock)
        {
            _currentId++;
            _buffer.Add((_currentId, DateTime.Now.ToString("HH:mm:ss")));
            if (_buffer.Count > MaxBufferSize) _buffer.RemoveAt(0);
        }
    }
}
```

> 注意：這裡的歷史紀錄存在記憶體裡，伺服器重啟就會消失，且多台伺服器實例
> 之間不會同步。真實專案如果需要更可靠的補送機制，會需要 Redis Stream、
> 訊息佇列等外部儲存，這超出這門課的範圍，但你可以帶著這個問題繼續深入。

## 前端關鍵程式碼（`backend/wwwroot/app.js`）

前端完全不用寫任何重連或補送邏輯——`Last-Event-ID` 的追蹤跟重送，
都是 `EventSource` 自動處理的：

```javascript
source.onerror = () => {
  statusEl.textContent = "連線中斷，等待自動重連...";
};

source.addEventListener("replay", (event) => { ... });
source.addEventListener("tick", (event) => { ... });
```

## 動手試試看

```bash
git checkout step-3-reconnect
cd backend
dotnet run
```

開瀏覽器到 `http://localhost:5080`，觀察：

1. 每 8 秒左右畫面會停頓一下（連線被伺服器主動關閉），接著自動恢復，
   並且「重連補送訊息」區塊會出現一則 `resume-info` 說明。
2. 打開 Network 面板觀察 `/sse/counter`，會看到它每隔一段時間就重新發送一次
   請求——這就是自動重連，且每次都帶著 `Last-Event-ID` header。

也可以用 curl 手動模擬「帶著舊的 Last-Event-ID 連線」：

```bash
# 先啟動伺服器一段時間讓計數器往上跑，再帶著較小的 id 連線，
# 應該會立刻看到一連串 event: replay 被補送回來。
curl -N -H "Last-Event-ID: 3" http://localhost:5080/sse/counter
```

## 下一步

Step 4 會處理「多個客戶端同時連線」的情境：目前這個做法是每個連線各自輪詢
`CounterFeed`，客戶端一多效能就會變差。下一步會改用 `Channel<T>` 做真正的
一對多廣播（pub/sub）。

```bash
git checkout step-4-broadcast
```
