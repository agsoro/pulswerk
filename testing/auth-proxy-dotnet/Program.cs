// AuthProxy.cs – Minimal reverse proxy that injects Authelia-style admin headers.
// Listens on port 5002 and forwards to http://localhost:5000 with
// Remote-User/Groups/Name/Email headers set to an admin identity.
// Used by the knx-real and dashboard-interactions e2e tests.
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

var target = new Uri("http://localhost:5000/");
var listener = new HttpListener();
listener.Prefixes.Add("http://+:5002/");
listener.Start();
Console.WriteLine($"[auth-proxy] Admin proxy listening on http://localhost:5002 → {target}");

using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });

while (true)
{
    var ctx = await listener.GetContextAsync();
    _ = Task.Run(async () =>
    {
        try { await HandleAsync(ctx, client, target); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[proxy error] {ex.Message}");
            try { ctx.Response.StatusCode = 502; ctx.Response.Close(); } catch { }
        }
    });
}

static async Task HandleAsync(HttpListenerContext ctx, HttpClient client, Uri target)
{
    var req = ctx.Request;
    var url = new Uri(target, req.Url!.PathAndQuery);

    using var msg = new HttpRequestMessage(new HttpMethod(req.HttpMethod), url);
    // Copy original headers, then inject admin identity
    foreach (string? h in req.Headers.AllKeys)
    {
        if (h is null) continue;
        if (h.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
        msg.Headers.TryAddWithoutValidation(h, req.Headers[h]);
    }
    msg.Headers.TryAddWithoutValidation("Remote-User", "admin");
    msg.Headers.TryAddWithoutValidation("Remote-Name", "Administrator");
    msg.Headers.TryAddWithoutValidation("Remote-Email", "admin@pulswerk.lan");
    msg.Headers.TryAddWithoutValidation("Remote-Groups", "admins");

    if (req.HasEntityBody)
    {
        using var ms = new System.IO.MemoryStream();
        await req.InputStream.CopyToAsync(ms);
        msg.Content = new ByteArrayContent(ms.ToArray());
        var ct = req.ContentType;
        if (!string.IsNullOrEmpty(ct))
            msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ct.Split(';')[0]);
    }

    using var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead);
    ctx.Response.StatusCode = (int)resp.StatusCode;
    foreach (var h in resp.Headers)
        ctx.Response.Headers[h.Key] = string.Join(", ", h.Value);
    foreach (var h in resp.Content.Headers)
        ctx.Response.Headers[h.Key] = string.Join(", ", h.Value);
    ctx.Response.Headers["Server"] = "auth-proxy";

    using var stream = await resp.Content.ReadAsStreamAsync();
    await stream.CopyToAsync(ctx.Response.OutputStream);
    ctx.Response.Close();
}
