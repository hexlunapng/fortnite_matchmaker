using System;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;

public class MatchmakerBehavior : WebSocketBehavior
{
    private static string GenerateHash(string prefix)
    {
        using var md5 = MD5.Create();
        var input = Encoding.UTF8.GetBytes(prefix + DateTimeOffset.Now.ToUnixTimeMilliseconds());
        var hash = md5.ComputeHash(input);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    protected override void OnOpen()
    {
        var protocolHeader = Context.Headers["Sec-WebSocket-Protocol"];
        if (!string.IsNullOrEmpty(protocolHeader) && protocolHeader.ToLower().Contains("xmpp"))
        {
            Context.WebSocket.Close();
            return;
        }

        var ticketId = GenerateHash("1");
        var matchId = GenerateHash("2");
        var sessionId = GenerateHash("3");

        void SendMsg(object payload, string name)
        {
            var message = JsonSerializer.Serialize(new { payload, name });
            Send(message);
        }

        Timer[] timers = new Timer[5];
        timers[0] = new Timer(_ => SendMsg(new { state = "Connecting" }, "StatusUpdate"), null, 200, Timeout.Infinite);
        timers[1] = new Timer(_ => SendMsg(new { state = "Waiting", totalPlayers = 1, connectedPlayers = 1 }, "StatusUpdate"), null, 1000, Timeout.Infinite);
        timers[2] = new Timer(_ => SendMsg(new { state = "Queued", ticketId, queuedPlayers = 0, estimatedWaitSec = 0, status = new { } }, "StatusUpdate"), null, 2000, Timeout.Infinite);
        timers[3] = new Timer(_ => SendMsg(new { state = "SessionAssignment", matchId }, "StatusUpdate"), null, 6000, Timeout.Infinite);
        timers[4] = new Timer(_ => SendMsg(new { matchId, sessionId, joinDelaySec = 1 }, "Play"), null, 8000, Timeout.Infinite);
    }
}

class Program
{
    static async Task HandleConnection(HttpListenerContext context)
    {
        WebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
        System.Net.WebSockets.WebSocket socket = wsContext.WebSocket;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ticketId = CreateMd5($"1{now}");
        var matchId = CreateMd5($"2{now}");
        var sessionId = CreateMd5($"3{now}");

        async Task SendJson(object obj)
        {
            string json = JsonSerializer.Serialize(obj);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            await SendJson(new { payload = new { state = "Connecting" }, name = "StatusUpdate" });

            await Task.Delay(800);
            await SendJson(new { payload = new { totalPlayers = 1, connectedPlayers = 1, state = "Waiting" }, name = "StatusUpdate" });

            await Task.Delay(1000);
            await SendJson(new { payload = new { ticketId, queuedPlayers = 0, estimatedWaitSec = 0, status = new { }, state = "Queued" }, name = "StatusUpdate" });

            await Task.Delay(4000);
            await SendJson(new { payload = new { matchId, state = "SessionAssignment" }, name = "StatusUpdate" });

            await Task.Delay(2000);
            await SendJson(new { payload = new { matchId, sessionId, joinDelaySec = 1 }, name = "Play" });

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
        });
    }

    static string CreateMd5(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    static void Main()
    {
        var wssv = new WebSocketServer(IPAddress.Any, 80);
        wssv.AddWebSocketService<MatchmakerBehavior>("/");
        wssv.Start();

        Console.WriteLine("Matchmaker started listening on port 80");
        Console.ReadLine();

        wssv.Stop();
    }
}
