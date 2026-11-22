using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using BatonE2E.Core.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BatonE2E.Core.Engine;

/// <summary>視聴用のHTTPサーバー（Kestrel）。GET / で視聴ページ、GET /ws でWebSocket。</summary>
internal sealed class ViewerServer : IAsyncDisposable
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private readonly WebApplication _app;
    private readonly EngineOptions _o;
    private readonly StreamHub _hub;
    private readonly AccessControl _ac;
    private string? _embedded;

    private ViewerServer(WebApplication app, EngineOptions o, StreamHub hub, AccessControl ac) { _app = app; _o = o; _hub = hub; _ac = ac; }

    public static async Task<ViewerServer> StartAsync(EngineOptions o, StreamHub hub)
    {
        var ip = IPAddress.Parse(o.ViewBind);
        if (!IPAddress.IsLoopback(ip) && !o.AllowExternalBind)
            throw new InvalidOperationException("external-bind-not-allowed");

        var b = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        b.Logging.ClearProviders();
        b.Services.Configure<HostOptions>(h => h.ShutdownTimeout = TimeSpan.FromSeconds(3));
        b.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = 1024;
            k.Listen(ip, o.ViewPort);
        });
        var app = b.Build();
        var ac = new AccessControl(o.Access);
        ac.Banned += ip => hub.KickRemote(ip, "rate");
        var me = new ViewerServer(app, o, hub, ac);
        app.Use(me.AccessMiddlewareAsync);
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        var vp = EngineOptions.NormalizeViewPath(o.ViewPath);
        var basePath = vp == "/" ? "/" : vp + "/";
        app.MapGet(vp, me.ServePageAsync);
        // 末尾の「/」あり・なしは同じ経路として扱われる（両方登録すると曖昧一致エラーになる）
        app.MapGet(basePath + "ws", me.HandleSocketAsync);
        await app.StartAsync();
        AppLog.Info(Loc.Get("log.view.listen", o.ViewBind, o.ViewPort));
        return me;
    }

    /// <summary>全リクエストの入口: 拒否リスト → 連打制限。接続元IPは ctx.Items["clientIp"] に入れる。</summary>
    private async Task AccessMiddlewareAsync(HttpContext ctx, Func<Task> next)
    {
        var ip = ClientIp.Resolve(ctx.Connection.RemoteIpAddress, ctx.Request.Headers["X-Forwarded-For"].ToString(), _o.Access.UseProxyHeader);
        ctx.Items["clientIp"] = ip;
        var r = _ac.Check(ip, ctx.Request.Headers.UserAgent.ToString(), ctx.Request.Host.Host);
        if (r.Decision == AccessDecision.Denied)
        {
            if (r.LogIt) AppLog.Warn(Loc.Get("log.access.denied", ip));
            ctx.Response.StatusCode = 403;
            return;
        }
        if (r.Decision == AccessDecision.RateLimited)
        {
            if (r.NewBan) AppLog.Warn(Loc.Get("log.access.rate", ip, _o.Access.RateWindowSec, _o.Access.RateMaxRequests, r.RetryAfterSec));
            ctx.Response.Headers["Retry-After"] = r.RetryAfterSec.ToString();
            ctx.Response.StatusCode = 429;
            return;
        }
        await next();
    }

    private async Task ServePageAsync(HttpContext c)
    {
        var r = c.Response;
        r.ContentType = "text/html; charset=utf-8";
        r.Headers.CacheControl = "no-store";
        r.Headers["X-Content-Type-Options"] = "nosniff";
        r.Headers["Referrer-Policy"] = "no-referrer";
        r.Headers["Content-Security-Policy"] =
            "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
            "connect-src 'self' ws: wss:; media-src blob: mediastream:; base-uri 'none'; form-action 'none'";
        await r.WriteAsync(LoadPage().Replace("{{TITLE}}", WebUtility.HtmlEncode(_o.PageTitle)));
    }

    private string LoadPage()
    {
        var file = _o.ViewerPageFile ?? Path.Combine(AppContext.BaseDirectory, "viewer.html");
        var html = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : (_embedded ??= ViewerTemplate.ReadEmbedded());
        // viewer.css（exeの横）があれば、ページの<head>末尾へ差し込む（CSPの都合で外部ファイルではなく埋め込み）
        var cssFile = Path.Combine(AppContext.BaseDirectory, "viewer.css");
        if (File.Exists(cssFile))
        {
            var css = File.ReadAllText(cssFile, Encoding.UTF8).Replace("</style", "<\\/style", StringComparison.OrdinalIgnoreCase);
            var i = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            var tag = "<style>\n" + css + "\n</style>\n";
            html = i >= 0 ? html.Insert(i, tag) : tag + html;
        }
        return html;
    }

    private async Task HandleSocketAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        var remote = ctx.Items["clientIp"] as string ?? "?";
        if (!_ac.TryAcquire(remote))
        {
            AppLog.Warn(Loc.Get("log.access.percli", remote, _o.Access.MaxConnPerClient));
            ctx.Response.StatusCode = 429;
            return;
        }
        try { await HandleSocketCoreAsync(ctx, remote); }
        finally { _ac.Release(remote); }
    }

    private async Task HandleSocketCoreAsync(HttpContext ctx, string remote)
    {
        var v = _hub.TryAddViewer(_o.ViewerQueue, remote);
        if (v == null)
        {
            AppLog.Warn(Loc.Get("log.viewer.full", _o.MaxViewers, remote));
            ctx.Response.StatusCode = 503;
            return;
        }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        AppLog.Info(Loc.Get("log.viewer.connect", remote, _hub.ViewerCount));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        var recv = Task.Run(() => DrainIncomingAsync(ws, cts));
        try
        {
            await SendAsync(ws, Encoding.UTF8.GetBytes(v.HeaderJson), WebSocketMessageType.Text, cts.Token);
            await SendAsync(ws, v.Confirm, WebSocketMessageType.Binary, cts.Token);
            await foreach (var frame in v.Reader.ReadAllAsync(cts.Token))
            {
                if (v.Kicked) break;
                await SendAsync(ws, frame, WebSocketMessageType.Binary, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            cts.Cancel();
            _hub.RemoveViewer(v);
            await recv;
            await CloseQuietlyAsync(ws, v.Kicked);
            AppLog.Info(Loc.Get("log.viewer.disconnect", remote, _hub.ViewerCount));
        }
    }

    /// <summary>送信に時間がかかる（遅い）視聴者は、タイムアウトで切断する。</summary>
    private static async Task SendAsync(WebSocket ws, byte[] data, WebSocketMessageType type, CancellationToken ct)
    {
        using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
        t.CancelAfter(SendTimeout);
        await ws.SendAsync(data.AsMemory(), type, true, t.Token);
    }

    /// <summary>視聴者からの受信は使わない。閉じ要求の検出だけ行う。</summary>
    private static async Task DrainIncomingAsync(WebSocket ws, CancellationTokenSource cts)
    {
        var buf = new byte[256];
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(buf.AsMemory(), cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        cts.Cancel();
    }

    private static async Task CloseQuietlyAsync(WebSocket ws, bool slow)
    {
        try
        {
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var t = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseAsync(slow ? WebSocketCloseStatus.PolicyViolation : WebSocketCloseStatus.NormalClosure, slow ? "slow" : "", t.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
