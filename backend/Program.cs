using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<BroadcastHub>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// 伺服器準備關閉時（例如部署新版本），先通知所有還連著的客戶端一聲，
// 讓前端可以顯示明確的訊息，而不是讓連線莫名其妙斷掉、觀察起來像個 bug。
var broadcastHub = app.Services.GetRequiredService<BroadcastHub>();
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("[SSE] 伺服器即將關閉，通知所有連線中的客戶端");
    broadcastHub.PublishAsync("server-shutdown", "伺服器即將關閉，稍後會自動重新連線").GetAwaiter().GetResult();
});

app.MapGet("/sse/chat", async (HttpContext context, BroadcastHub hub) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    var cancellationToken = context.RequestAborted;

    await WriteSseMessageAsync(context.Response, cancellationToken, retryMs: 2000);

    var (clientId, reader) = hub.Subscribe();
    Console.WriteLine($"[SSE] {clientId} 加入，目前在線 {hub.SubscriberCount} 人");
    await hub.PublishPresenceAsync();

    try
    {
        // 心跳的目的：反向代理（Nginx、雲端 LB）通常會把「一段時間沒有任何位元組
        // 傳輸」的連線當成閒置連線強制關閉，即使 TCP 連線本身還活著。
        // 用 PeriodicTimer 定期送出 SSE 註解行（": ..."），維持連線有資料在流動，
        // 同時不影響前端——EventSource 會直接忽略 ":" 開頭的註解行。
        using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        var readTask = reader.WaitToReadAsync(cancellationToken).AsTask();
        var heartbeatTask = heartbeatTimer.WaitForNextTickAsync(cancellationToken).AsTask();

        while (!cancellationToken.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(readTask, heartbeatTask);

            if (completed == readTask)
            {
                if (!await readTask)
                {
                    break; // channel 被關閉（理論上不會發生，除非伺服器主動移除訂閱）
                }

                while (reader.TryRead(out var message))
                {
                    await WriteSseMessageAsync(
                        context.Response, cancellationToken,
                        id: message.Id.ToString(), eventName: message.EventName, data: message.Data);
                }

                readTask = reader.WaitToReadAsync(cancellationToken).AsTask();
            }
            else
            {
                await heartbeatTask;
                await WriteSseCommentAsync(context.Response, cancellationToken, "heartbeat");
                heartbeatTask = heartbeatTimer.WaitForNextTickAsync(cancellationToken).AsTask();
            }
        }
    }
    catch (OperationCanceledException)
    {
        // 使用者關閉分頁、斷線，或伺服器關閉（RequestAborted 被觸發），屬於正常結束。
    }
    catch (IOException)
    {
        // 寫入時才發現對方已經斷線（例如網路被拔掉、沒有正常走 TCP FIN），
        // 這種情況不算伺服器的錯誤，只代表這個客戶端已經連不上了。
        Console.WriteLine($"[SSE] {clientId} 寫入失敗，視為已斷線");
    }
    finally
    {
        // 不管上面是哪一種方式結束，都一定要離開這裡才能釋放這個客戶端的 channel，
        // 否則反覆連線/斷線會讓 BroadcastHub 裡累積用不到的訂閱者，造成記憶體洩漏。
        hub.Unsubscribe(clientId);
        Console.WriteLine($"[SSE] {clientId} 離開，目前在線 {hub.SubscriberCount} 人");
        await hub.PublishPresenceAsync();
    }
});

app.MapPost("/sse/chat/messages", async (ChatMessageRequest request, BroadcastHub hub) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest();
    }

    await hub.PublishAsync("chat", request.Text.Trim());
    return Results.Accepted();
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

// SSE 規定用 ":" 開頭的行是註解，解析器會讀過去但完全不會觸發任何前端事件——
// 很適合拿來當作「只是要讓連線保持有資料流動」的心跳封包。
static async Task WriteSseCommentAsync(HttpResponse response, CancellationToken cancellationToken, string comment)
{
    await response.WriteAsync($": {comment}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

record ChatMessageRequest(string Text);

record BroadcastMessage(long Id, string EventName, string Data);

class BroadcastHub
{
    private readonly ConcurrentDictionary<Guid, Channel<BroadcastMessage>> _subscribers = new();
    private long _nextId;

    public int SubscriberCount => _subscribers.Count;

    public (Guid ClientId, ChannelReader<BroadcastMessage> Reader) Subscribe()
    {
        var clientId = Guid.NewGuid();

        var channel = Channel.CreateBounded<BroadcastMessage>(new BoundedChannelOptions(20)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _subscribers[clientId] = channel;
        return (clientId, channel.Reader);
    }

    public void Unsubscribe(Guid clientId)
    {
        if (_subscribers.TryRemove(clientId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public async Task PublishAsync(string eventName, string data)
    {
        var message = new BroadcastMessage(Interlocked.Increment(ref _nextId), eventName, data);

        foreach (var channel in _subscribers.Values)
        {
            await channel.Writer.WriteAsync(message);
        }
    }

    public Task PublishPresenceAsync() => PublishAsync("presence", SubscriberCount.ToString());
}
