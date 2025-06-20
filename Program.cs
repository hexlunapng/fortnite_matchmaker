using System;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
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
            var message = System.Text.Json.JsonSerializer.Serialize(new { payload, name });
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
    static void Main()
    {
        var wssv = new WebSocketServer(System.Net.IPAddress.Any, 80);
        wssv.AddWebSocketService<MatchmakerBehavior>("/");
        wssv.Start();

        Console.WriteLine("Matchmaker started listening on port 80");
        Console.ReadLine();

        wssv.Stop();
    }
}
