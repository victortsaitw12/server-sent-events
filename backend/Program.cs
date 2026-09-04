using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<BroadcastHub>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

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
        // ReadAllAsync 會一直等待這個客戶端專屬 channel 裡的新訊息，
        // 有新訊息就寫回這個連線；不同客戶端各自讀自己的 channel，互不影響。
        await foreach (var message in reader.ReadAllAsync(cancellationToken))
        {
            await WriteSseMessageAsync(
                context.Response, cancellationToken,
                id: message.Id.ToString(), eventName: message.EventName, data: message.Data);
        }
    }
    catch (OperationCanceledException)
    {
        // 使用者關閉分頁或斷線，屬於正常結束。
    }
    finally
    {
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

    // 這一個 API 呼叫，會讓所有正在連線的瀏覽器分頁「同時」收到這則訊息。
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

record ChatMessageRequest(string Text);

record BroadcastMessage(long Id, string EventName, string Data);

// 一對多廣播的核心：每個連線的客戶端各自擁有一個 Channel<T>，
// Publish 時把同一則訊息「複製」進每一個 channel，
// 每個連線的 while 迴圈只需要讀自己的 channel，彼此完全獨立、互不阻塞。
class BroadcastHub
{
    private readonly ConcurrentDictionary<Guid, Channel<BroadcastMessage>> _subscribers = new();
    private long _nextId;

    public int SubscriberCount => _subscribers.Count;

    public (Guid ClientId, ChannelReader<BroadcastMessage> Reader) Subscribe()
    {
        var clientId = Guid.NewGuid();

        // 有界 channel + DropOldest：萬一某個客戶端消化訊息的速度太慢
        // （例如網路很差），寧可讓它漏掉比較舊的訊息，也不要拖慢或塞爆整個伺服器。
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
