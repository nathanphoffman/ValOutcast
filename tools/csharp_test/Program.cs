using System;
using System.Diagnostics;
using System.Threading;
using ValOutcast;

class Program
{
    static void Check(bool condition, string what)
    {
        if (!condition) { Console.WriteLine("FAIL: " + what); Environment.Exit(1); }
    }

    // Look up the expected bytes for a message by name in the shared golden file.
    static byte[] Golden(string name)
    {
        foreach (var line in System.IO.File.ReadAllLines("protocol/golden.txt"))
        {
            var parts = line.Split(' ');

            if (parts.Length == 2 && parts[0] == name) return Convert.FromHexString(parts[1]);
        }

        throw new Exception("no golden packet " + name);
    }

    static readonly byte[] HelloBytes = { 0x56, 0x4F, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0xD2, 0x04, 0, 0 };

    // Hello, heartbeat and player state are the first packets any new world must get right.
    static void TestHelloHeartbeatAndState()
    {
        var beat = new byte[] { 0x56, 0x4F, 1, 2, 2, 0, 0, 0, 0x88, 0x13, 0, 0 };
        Check(VoLink.EncodeHello(1, VoLink.RolePlayer, 1234).AsSpan().SequenceEqual(HelloBytes), "hello bytes");
        Check(VoLink.EncodeHeartbeat(2, 5000).AsSpan().SequenceEqual(beat), "heartbeat bytes");
        var state = new byte[] { 0x56, 0x4F, 1, 3, 3, 0, 0, 0, 0, 0, 0x80, 0x3F, 0, 0, 0, 0x40, 0, 0, 0x40, 0x40,
                                 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 0xBF };
        Check(VoLink.EncodePlayerState(3, new float[] { 1, 2, 3 }, new float[] { 0, 0, -1 }).AsSpan().SequenceEqual(state), "player state bytes");
        var readPosition = new float[3]; var readLook = new float[3];
        VoLink.DecodePlayerState(state, readPosition, readLook);
        Check(readPosition[0] == 1 && readPosition[1] == 2 && readPosition[2] == 3 && readLook[2] == -1, "decode player state");
    }

    // A terrain chunk has an exact header, size and height layout.
    static void TestCollisionSection()
    {
        var heights = new short[256];

        for (int index = 0; index < 256; index++) heights[index] = (short)(index * 100 - 12000);
        heights[0] = VoLink.NoHeight;
        var collisionPacket = VoLink.EncodeCollisionSection(4, -1, 2, heights);
        Check(collisionPacket.Length == 528, "collision size");
        Check(collisionPacket.AsSpan(0, 16).SequenceEqual(new byte[] { 0x56, 0x4F, 1, 4, 4, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 2, 0, 0, 0 }), "collision header bytes");
        Check(BitConverter.ToInt16(collisionPacket, 16) == -32768 && BitConverter.ToInt16(collisionPacket, 16 + 510) == 255 * 100 - 12000, "collision heights");
        Check(VoLink.EncodeCollisionReset(5).AsSpan().SequenceEqual(new byte[] { 0x56, 0x4F, 1, 5, 5, 0, 0, 0 }), "reset bytes");
    }

    // Every newer message must encode to the shared golden bytes.
    static void TestEncodersMatchGolden()
    {
        Check(VoLink.EncodeActorUpsert(6, 7, new float[] { 1, 2, 3 }, 0.5f, 2f, 200, 1).AsSpan().SequenceEqual(Golden("actor_upsert")), "actor_upsert bytes");
        Check(VoLink.EncodeActorRemove(7, 7).AsSpan().SequenceEqual(Golden("actor_remove")), "actor_remove bytes");
        Check(VoLink.EncodeHitActor(8, 7, 25f, new float[] { 0, 0, -1 }).AsSpan().SequenceEqual(Golden("hit_actor")), "hit_actor bytes");
        Check(VoLink.EncodePlayerHurt(9, 10f, new float[] { 1, 0, 0 }, 42).AsSpan().SequenceEqual(Golden("player_hurt")), "player_hurt bytes");
        Check(VoLink.EncodeInput(10, 3, 1f, -1f, 0f, 0.5f, -0.5f, 4).AsSpan().SequenceEqual(Golden("input")), "input bytes");
        Check(VoLink.EncodeWorldContext(11, new float[] { 0f, 0.8f, 0.6f }, new float[] { 1f, 0.9f, 0.5f }, new float[] { 0.25f, 0.5f, 0.75f }).AsSpan().SequenceEqual(Golden("world_context")), "world_context bytes");
        Check(VoLink.EncodeLoadout(14, 0x2004).AsSpan().SequenceEqual(Golden("loadout")), "loadout bytes");
        Check(VoLink.EncodePlayerLight(13, new float[] { 0.25f, 0.5f, 1f }, 0.75f).AsSpan().SequenceEqual(Golden("player_light")), "player_light bytes");
    }

    // Decoders read the golden bytes back and reject packets cut short.
    static void TestDecoders()
    {
        Check(VoLink.DecodePlayerLight(Golden("player_light"), 24, out var lightMsg) && lightMsg.Blue == 1f && lightMsg.Intensity == 0.75f, "player_light decode");
        Check(!VoLink.DecodePlayerLight(Golden("player_light"), 12, out _), "player_light short");
        Check(VoLink.EncodeHitWorld(12, new float[] { 1, 2, 3 }, new float[] { 0, 0, -1 }, 25f, 1).AsSpan().SequenceEqual(Golden("hit_world")), "hit_world bytes");
        var worldBytes = Golden("hit_world");
        Check(VoLink.DecodeHitWorld(worldBytes, worldBytes.Length, out var worldMsg) && worldMsg.PosZ == 3f && worldMsg.DirZ == -1f && worldMsg.Damage == 25f && worldMsg.Kind == 1, "hit_world decode");
        Check(!VoLink.DecodeHitWorld(worldBytes, 20, out _), "hit_world short");
        Check(VoLink.EncodeActorPush(15, 7, 0.5f, new float[] { 0, 0, -1 }).AsSpan().SequenceEqual(Golden("actor_push")), "actor_push bytes");
        Check(VoLink.EncodeActorCharm(16, 7, 1.0f, new float[] { 0, 0, -1 }).AsSpan().SequenceEqual(Golden("actor_charm")), "actor_charm bytes");
        Check(VoLink.EncodeActorGrip(17, 7, 1.0f, new float[] { 0, 0, -1 }).AsSpan().SequenceEqual(Golden("actor_grip")), "actor_grip bytes");
        Check(VoLink.EncodeActorHeal(18, 7, 1.0f, new float[] { 0, 0, -1 }).AsSpan().SequenceEqual(Golden("actor_heal")), "actor_heal bytes");
        var hitBytes = Golden("hit_actor");
        Check(VoLink.DecodeHitActor(hitBytes, hitBytes.Length, out var hitMsg) && hitMsg.ActorId == 7 && hitMsg.Damage == 25f && hitMsg.DirZ == -1f, "hit_actor decode");
        Check(!VoLink.DecodeHitActor(hitBytes, 10, out _), "hit_actor short");
        Check(VoLink.Decode(HelloBytes, 16, out uint seq) == VoLink.MsgHello && seq == 1, "decode hello");
        Check(VoLink.Decode(HelloBytes, 4, out _) == -1, "decode short");
    }

    // The overlay frame file: skips half-written frames, follows resizes, counts requests.
    static void TestFrameFile()
    {
        // Overlay frame file: a made-up 4x2 frame.
        string framePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vo_test_frame_" + Environment.ProcessId);

        void WriteFrame(uint counter, int width, int height, byte fill, int extraBytes = 0)
        {
            var bytes = new byte[32 + width * height * 4 + extraBytes];
            BitConverter.GetBytes(FrameReader.Magic).CopyTo(bytes, 0);
            BitConverter.GetBytes(1u).CopyTo(bytes, 4);
            BitConverter.GetBytes((uint)width).CopyTo(bytes, 8);
            BitConverter.GetBytes((uint)height).CopyTo(bytes, 12);
            BitConverter.GetBytes(counter).CopyTo(bytes, 16);

            for (int index = 32; index < bytes.Length; index++) bytes[index] = fill;
            using var stream = new System.IO.FileStream(framePath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            stream.Write(bytes, 0, bytes.Length);
        }

        var reader = new FrameReader(framePath);
        byte[] pixels = null;
        Check(!reader.TryRead(ref pixels, out _, out _), "no frame file yet");
        WriteFrame(7, 4, 2, 9);                                   // odd counter: being written
        Check(!reader.TryRead(ref pixels, out _, out _), "odd counter is skipped");
        WriteFrame(8, 4, 2, 9);
        Check(reader.TryRead(ref pixels, out int fw, out int fh) && fw == 4 && fh == 2 && pixels.Length == 32 && pixels[0] == 9 && pixels[31] == 9, "reads a complete frame");
        Check(!reader.TryRead(ref pixels, out _, out _), "same frame is not read twice");
        WriteFrame(10, 2, 2, 5);                                  // new size, new frame
        Check(reader.TryRead(ref pixels, out fw, out fh) && fw == 2 && fh == 2 && pixels.Length == 16 && pixels[0] == 5, "reads a resized frame");

        // Frame requests: counts up in the file at offset 20, carrying on from what is there.
        reader.RequestFrame();
        reader.RequestFrame();
        Check(BitConverter.ToUInt32(System.IO.File.ReadAllBytes(framePath), FrameReader.RequestOffset) == 2, "two requests are counted");
        reader.Close();
        var reader2 = new FrameReader(framePath);
        reader2.RequestFrame();
        Check(BitConverter.ToUInt32(System.IO.File.ReadAllBytes(framePath), FrameReader.RequestOffset) == 3, "requests carry on after a restart");
        reader2.Close();
        System.IO.File.Delete(framePath);
    }

    // Optional: read a real frame written by Outcast (set VO_REAL_FRAME to the file path).
    static void TestRealFrame()
    {
        string real = Environment.GetEnvironmentVariable("VO_REAL_FRAME");

        if (!string.IsNullOrEmpty(real))
        {
            var realReader = new FrameReader(real);
            byte[] realPixels = null;
            Check(realReader.TryRead(ref realPixels, out int rw, out int rh) && realPixels.Length == rw * rh * 4, "reads the real Outcast frame");
            int covered = 0;

            for (int index = 3; index < realPixels.Length; index += 4) if (realPixels[index] > 8) covered++;
            Console.WriteLine("real frame: " + rw + "x" + rh + ", " + covered + " pixels with alpha");
            realReader.Close();
        }
    }

    // Connect to the fake player side (python), then notice it going away.
    static void TestPeerConnection()
    {
        var link = new VoLink(VoLink.RoleWorld, m => Console.WriteLine("[cs_test] " + m));
        bool connectedSeen = false, lostSeen = false;

        for (int index = 0; index < 600 && !lostSeen; index++)
        {
            link.Tick();

            if (link.Connected) connectedSeen = true;

            if (connectedSeen && !link.Connected) lostSeen = true;
            Thread.Sleep(10);
        }

        link.Close();
        Check(connectedSeen, "never connected");
        Check(lostSeen, "never noticed lost peer");
    }

    static int Main()
    {
        TestHelloHeartbeatAndState();
        TestCollisionSection();
        TestEncodersMatchGolden();
        TestDecoders();
        TestFrameFile();
        TestRealFrame();
        TestPeerConnection();
        Console.WriteLine("csharp protocol tests passed");

        return 0;
    }
}
