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
        var state = new byte[] { 0x56, 0x4F, 1, 3, 3, 0, 0, 0, 0, 0, 0x80, 0x3F, 0, 0, 0, 0x40, 0, 0, 0x40, 0x40,
                                 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0xBF };
        Check(VoLink.EncodePlayerState(3, new float[] { 1, 2, 3 }, new float[] { 0, 0, -1 }).AsSpan().SequenceEqual(state), "player state bytes");
        var rp = new float[3]; var rl = new float[3];
        VoLink.DecodePlayerState(state, rp, rl);
        Check(rp[0] == 1 && rp[1] == 2 && rp[2] == 3 && rl[2] == -1, "decode player state");
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
