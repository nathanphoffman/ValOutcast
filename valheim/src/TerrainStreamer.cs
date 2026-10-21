using System.Collections.Generic;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Sends the ground around the player to the player side as a grid of columns
    /// (see protocol/PROTOCOL.md, "Collision field"). Terrain comes from Valheim's heightmap.
    /// Trees, rocks and buildings in the way make the column taller so they can't be walked through.
    /// </summary>
    public class TerrainStreamer
    {
        private const int Radius = 5;               // chunks around the player (8 m each)
        private const int NewChunksPerUpdate = 3;
        private const float ResendSeconds = 5f;     // resend unchanged chunks now and then, in case a packet was lost
        private const float ObjectBlockHeight = 3f; // columns with an object in the way are this much taller than the ground

        private class Sent
        {
            public float LastSample;
            public float LastSent;
            public int Hash;
        }

        private readonly Dictionary<long, Sent> _chunks = new Dictionary<long, Sent>();
        private readonly List<Vector2Int> _offsets = new List<Vector2Int>();
        private readonly short[] _heights = new short[VoLink.ChunkCells * VoLink.ChunkCells];
        private int _generation = -1;
        private bool _wasConnected;
        private int _objectMask = -1;

        public TerrainStreamer()
        {
            for (int dz = -Radius; dz <= Radius; dz++)
                for (int dx = -Radius; dx <= Radius; dx++)
                    _offsets.Add(new Vector2Int(dx, dz));
            _offsets.Sort((a, b) => (a.x * a.x + a.y * a.y).CompareTo(b.x * b.x + b.y * b.y));
        }

        private static long Key(int cx, int cz)
        {
            return ((long)cx << 32) | (uint)cz;
        }

        public void Update(VoLink link)
        {
            if (link == null) return;
            bool connected = link.Connected;
            if (connected && !_wasConnected)
            {
                _chunks.Clear(); // the player side may have restarted and knows nothing
            }
            _wasConnected = connected;

            Player player = Player.m_localPlayer;
            if (!connected || !PlayerFollow.Active || !PlayerFollow.Anchored || player == null || ZoneSystem.instance == null)
            {
                return;
            }
            if (_generation != PlayerFollow.AnchorGeneration)
            {
                _generation = PlayerFollow.AnchorGeneration;
                _chunks.Clear();
                link.SendCollisionReset();
            }

            Vector3 wire = PlayerFollow.WorldToWire(player.transform.position);
            int pcx = Mathf.FloorToInt(wire.x / (VoLink.ChunkCells * VoLink.CellSizeM));
            int pcz = Mathf.FloorToInt(wire.z / (VoLink.ChunkCells * VoLink.CellSizeM));

            int budget = NewChunksPerUpdate;
            foreach (Vector2Int o in _offsets)
            {
                if (budget == 0) break;
                int cx = pcx + o.x, cz = pcz + o.y;
                if (_chunks.ContainsKey(Key(cx, cz))) continue;
                SampleAndSend(link, cx, cz, true);
                budget--;
            }

            if (budget > 0)
            {
                // Everything near is already sent: re-check the oldest chunk, in case the world changed.
                long oldestKey = 0;
                float oldest = float.MaxValue;
                foreach (var kv in _chunks)
                {
                    if (kv.Value.LastSample < oldest) { oldest = kv.Value.LastSample; oldestKey = kv.Key; }
                }
                if (oldest < float.MaxValue)
                {
                    SampleAndSend(link, (int)(oldestKey >> 32), (int)(uint)(oldestKey & 0xFFFFFFFF), false);
                }
            }

            // Forget chunks far behind us.
            List<long> far = null;
            foreach (long key in _chunks.Keys)
            {
                int cx = (int)(key >> 32), cz = (int)(uint)(key & 0xFFFFFFFF);
                if (Mathf.Abs(cx - pcx) > Radius + 2 || Mathf.Abs(cz - pcz) > Radius + 2)
                {
                    (far ?? (far = new List<long>())).Add(key);
                }
            }
            if (far != null) foreach (long key in far) _chunks.Remove(key);
        }

        private void SampleAndSend(VoLink link, int cx, int cz, bool isNew)
        {
            if (_objectMask < 0)
            {
                _objectMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            }

            int hash = 17;
            for (int iz = 0; iz < VoLink.ChunkCells; iz++)
            {
                for (int ix = 0; ix < VoLink.ChunkCells; ix++)
                {
                    float wireX = (cx * VoLink.ChunkCells + ix + 0.5f) * VoLink.CellSizeM;
                    float wireZ = (cz * VoLink.ChunkCells + iz + 0.5f) * VoLink.CellSizeM;
                    Vector3 w = PlayerFollow.WireToWorldXZ(wireX, wireZ);

                    short h = VoLink.NoHeight;
                    if (ZoneSystem.instance.GetGroundHeight(w, out float ground))
                    {
                        float top = ground;
                        var center = new Vector3(w.x, ground + 1.2f, w.z);
                        if (Physics.CheckBox(center, new Vector3(0.25f, 0.9f, 0.25f), Quaternion.identity, _objectMask, QueryTriggerInteraction.Ignore))
                        {
                            top = ground + ObjectBlockHeight;
                        }
                        float wireY = PlayerFollow.WorldYToWireY(top);
                        h = (short)Mathf.Clamp(Mathf.RoundToInt(wireY * VoLink.HeightUnitsPerM), -32767, 32767);
                    }
                    _heights[iz * VoLink.ChunkCells + ix] = h;
                    hash = hash * 31 + h;
                }
            }

            long key = Key(cx, cz);
            float now = Time.realtimeSinceStartup;
            if (!_chunks.TryGetValue(key, out Sent sent))
            {
                sent = new Sent { LastSent = -1000f };
                _chunks[key] = sent;
            }
            sent.LastSample = now;
            if (isNew || hash != sent.Hash || now - sent.LastSent > ResendSeconds)
            {
                link.SendCollisionSection(cx, cz, _heights);
                sent.LastSent = now;
                sent.Hash = hash;
            }
        }
    }
}
