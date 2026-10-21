using System;
using System.Diagnostics;
using System.Threading;
using ValOutcast;

class Program
{
    static void Check(bool ok, string what)
    {
        if (!ok) { Console.WriteLine("FAIL: " + what); Environment.Exit(1); }
    }

    static int Main()
    {
        var hello = new byte[] { 0x56, 0x4F, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0xD2, 0x04, 0, 0 };
        var beat = new byte[] { 0x56, 0x4F, 1, 2, 2, 0, 0, 0, 0x88, 0x13, 0, 0 };
        Check(VoLink.EncodeHello(1, VoLink.RolePlayer, 1234).AsSpan().SequenceEqual(hello), "hello bytes");
        Check(VoLink.EncodeHeartbeat(2, 5000).AsSpan().SequenceEqual(beat), "heartbeat bytes");
        Check(VoLink.Decode(hello, 16, out uint seq) == VoLink.MsgHello && seq == 1, "decode hello");
        Check(VoLink.Decode(hello, 4, out _) == -1, "decode short");

        // Connect to the fake player side (python), then notice it going away.
        var link = new VoLink(VoLink.RoleWorld, m => Console.WriteLine("[cs_test] " + m));
        bool connectedSeen = false, lostSeen = false;
        for (int i = 0; i < 600 && !lostSeen; i++)
        {
            link.Tick();
            if (link.Connected) connectedSeen = true;
            if (connectedSeen && !link.Connected) lostSeen = true;
            Thread.Sleep(10);
        }
        link.Close();
        Check(connectedSeen, "never connected");
        Check(lostSeen, "never noticed lost peer");
        Console.WriteLine("csharp protocol tests passed");
        return 0;
    }
}
