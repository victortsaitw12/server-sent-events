using System.Text;

var builder = WebApplication.CreateBuilder(args);

// CounterFeed 同時是背景服務（持續產生資料）也是可以被 endpoint 注入的服務
// （查詢/補送歷史資料），所以用同一個 singleton instance 註冊兩次。
builder.Services.AddSingleton<CounterFeed>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CounterFeed>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/sse/counter", async (HttpContext context, CounterFeed feed) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    var cancellationToken = context.RequestAborted;

    // retry 設短一點（2 秒），這樣斷線後很快就能觀察到重連。
    await WriteSseMessageAsync(context.Response, cancellationToken, retryMs: 2000);

    long lastSentId;

    // 瀏覽器的 EventSource 斷線自動重連時，會自動帶上 Last-Event-ID header，
    // 值就是它最後一次成功收到的 id。我們可以靠這個判斷要不要「補送」訊息。
    if (context.Request.Headers.TryGetValue("Last-Event-ID", out var header) &&
        long.TryParse(header, out var lastEventId))
    {
        var missed = feed.GetMessagesAfter(lastEventId);
        Console.WriteLine($"[SSE] 重新連線，Last-Event-ID={lastEventId}，補送 {missed.Count} 則訊息");

        await WriteSseMessageAsync(
            context.Response, cancellationToken,
            eventName: "resume-info",
            data: $"偵測到重新連線，從 id {lastEventId} 之後補送 {missed.Count} 則訊息");

        lastSentId = lastEventId;
        foreach (var item in missed)
        {
            await WriteSseMessageAsync(
                context.Response, cancellationToken,
                id: item.Id.ToString(), eventName: "replay", data: item.Data);
            lastSentId = item.Id;
        }
    }
    else
    {
        Console.WriteLine("[SSE] 新的客戶端連線");
        lastSentId = feed.CurrentId;
    }

    try
    {
        var ticksOnThisConnection = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(300, cancellationToken);

            foreach (var item in feed.GetMessagesAfter(lastSentId))
            {
                await WriteSseMessageAsync(
                    context.Response, cancellationToken,
                    id: item.Id.ToString(), eventName: "tick", data: item.Data);
                lastSentId = item.Id;
                ticksOnThisConnection++;
            }

            // 教學用：故意每送出 8 則訊息就主動斷線一次，讓你不用手動操作
            // 就能看到瀏覽器自動重連、並透過 Last-Event-ID 補送遺漏訊息的效果。
            // 正式環境「不會」也「不應該」故意這樣斷線。
            if (ticksOnThisConnection >= 8)
            {
                Console.WriteLine("[SSE] 故意中斷連線，模擬網路不穩 / 伺服器重啟");
                return;
            }
        }
    }
    catch (OperationCanceledException)
    {
        // 使用者關閉分頁或斷線，屬於正常結束。
    }
});

app.Run();

static async Task WriteSseMessageAsync(
    HttpResponse response,
    CancellationToken cancellationToken,
    string? id = null,
    string? eventName = null,
    string? data = null,
    int? retryMs = null)
{
    var builder = new StringBuilder();

    if (id is not null)
    {
        builder.Append("id: ").Append(id).Append('\n');
    }

    if (eventName is not null)
    {
        builder.Append("event: ").Append(eventName).Append('\n');
    }

    if (retryMs is not null)
    {
        builder.Append("retry: ").Append(retryMs.Value).Append('\n');
    }

    if (data is not null)
    {
        foreach (var line in data.Split('\n'))
        {
            builder.Append("data: ").Append(line).Append('\n');
        }
    }

    builder.Append('\n');

    await response.WriteAsync(builder.ToString(), cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

// 背景持續產生資料，跟任何一個 HTTP 連線的生命週期無關。
// 這樣即使所有客戶端都斷線，計數還是會繼續累加，
// 才能真實地示範「重連時已經有訊息被錯過」的情境。
class CounterFeed : BackgroundService
{
    private const int MaxBufferSize = 30;

    private readonly object _lock = new();
    private readonly List<(long Id, string Data)> _buffer = new();
    private long _currentId;

    public long CurrentId
    {
        get { lock (_lock) return _currentId; }
    }

    public List<(long Id, string Data)> GetMessagesAfter(long id)
    {
        lock (_lock)
        {
            return _buffer.Where(m => m.Id > id).ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);

            lock (_lock)
            {
                _currentId++;
                _buffer.Add((_currentId, DateTime.Now.ToString("HH:mm:ss")));

                if (_buffer.Count > MaxBufferSize)
                {
                    _buffer.RemoveAt(0);
                }
            }
        }
    }
}
