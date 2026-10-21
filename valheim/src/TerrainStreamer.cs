using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace ValOutcast
{
    /// <summary>
    /// Sends the ground around the player to the player side as a grid of columns
    /// (see protocol/PROTOCOL.md, "Collision field"). Terrain comes from Valheim's heightmap.
    /// Trees, rocks and buildings in the way make the column taller so they can't be walked through.
    /// The measuring is spread over many frames: each frame it only works for a small time budget.
    /// </summary>
    public class TerrainStreamer
    {
        private const int Radius = 4;               // chunks around the player (8 m each)
        private const float BudgetMs = 1.0f;        // most time spent on measuring in one frame
        private const float RefreshEverySeconds = 0.5f;
        private const float ResendSeconds = 5f;     // resend unchanged chunks now and then, in case a packet was lost
        private const float ObjectSearchHeight = 30f;  // how far above the ground to look for objects
        private const float MinObjectHeight = 0.1f;    // lower than this counts as part of the ground

        private class Sent
        {
            public float LastSample;
            public float LastSent;
            public int Hash;
        }

        private readonly Dictionary<long, Sent> _chunks = new Dictionary<long, Sent>();
        private readonly List<Vector2Int> _offsets = new List<Vector2Int>();
        private readonly short[] _heights = new short[VoLink.ChunkCells * VoLink.ChunkCells];
        private readonly Stopwatch _clock = new Stopwatch();
        private int _generation = -1;
        private bool _wasConnected;
        private int _objectMask = -1;

        // The chunk being measured right now (it can take several frames).
        private bool _working;
        private int _workCx, _workCz, _workCell, _workHash;
        private bool _workIsNew;
        private float _nextRefresh;

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
                _working = false;
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
                _working = false;
                link.SendCollisionReset();
            }

            Vector3 wire = PlayerFollow.WorldToWire(player.transform.position);
            int pcx = Mathf.FloorToInt(wire.x / (VoLink.ChunkCells * VoLink.CellSizeM));
            int pcz = Mathf.FloorToInt(wire.z / (VoLink.ChunkCells * VoLink.CellSizeM));

            _clock.Restart();
            while (_clock.Elapsed.TotalMilliseconds < BudgetMs)
            {
                if (!_working && !StartNextChunk(pcx, pcz)) break;
                if (_working) Measure(link);
            }

            ForgetFarChunks(pcx, pcz);
        }

        /// <summary>Picks what to measure next: the nearest chunk never measured, else the oldest one now and then.</summary>
        private bool StartNextChunk(int pcx, int pcz)
        {
            foreach (Vector2Int o in _offsets)
            {
                int cx = pcx + o.x, cz = pcz + o.y;
                if (_chunks.ContainsKey(Key(cx, cz))) continue;
                Begin(cx, cz, true);
                return true;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextRefresh) return false;
            _nextRefresh = now + RefreshEverySeconds;

            long oldestKey = 0;
            float oldest = float.MaxValue;
            foreach (var kv in _chunks)
            {
                if (kv.Value.LastSample < oldest) { oldest = kv.Value.LastSample; oldestKey = kv.Key; }
            }
            if (oldest == float.MaxValue) return false;
            Begin((int)(oldestKey >> 32), (int)(uint)(oldestKey & 0xFFFFFFFF), false);
            return true;
        }

        private void Begin(int cx, int cz, bool isNew)
        {
            _working = true;
            _workCx = cx;
            _workCz = cz;
            _workCell = 0;
            _workHash = 17;
            _workIsNew = isNew;
            if (_objectMask < 0)
            {
                _objectMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
            }
        }

        /// <summary>Measures cells of the current chunk until the time budget runs out or the chunk is done.</summary>
        private void Measure(VoLink link)
        {
            int cells = VoLink.ChunkCells * VoLink.ChunkCells;
            while (_workCell < cells)
            {
                if (_clock.Elapsed.TotalMilliseconds >= BudgetMs) return;

                int ix = _workCell % VoLink.ChunkCells;
                int iz = _workCell / VoLink.ChunkCells;
                float wireX = (_workCx * VoLink.ChunkCells + ix + 0.5f) * VoLink.CellSizeM;
                float wireZ = (_workCz * VoLink.ChunkCells + iz + 0.5f) * VoLink.CellSizeM;
                Vector3 w = PlayerFollow.WireToWorldXZ(wireX, wireZ);

                short h = VoLink.NoHeight;
                if (ZoneSystem.instance.GetGroundHeight(w, out float ground))
                {
                    float top = ground;
                    // Something solid standing on the ground (tree, rock, wall)? Then the column is as tall as it really is.
                    // (Measured from 30 m above the ground, so a roof higher than that is ignored.)
                    var from = new Vector3(w.x, ground + ObjectSearchHeight, w.z);
                    if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, ObjectSearchHeight, _objectMask, QueryTriggerInteraction.Ignore)
                        && hit.point.y > ground + MinObjectHeight)
                    {
                        top = hit.point.y;
                    }
                    float wireY = PlayerFollow.WorldYToWireY(top);
                    h = (short)Mathf.Clamp(Mathf.RoundToInt(wireY * VoLink.HeightUnitsPerM), -32767, 32767);
                }
                _heights[_workCell] = h;
                _workHash = _workHash * 31 + h;
                _workCell++;
            }

            // Chunk finished.
            long key = Key(_workCx, _workCz);
            float now = Time.realtimeSinceStartup;
            if (!_chunks.TryGetValue(key, out Sent sent))
            {
                sent = new Sent { LastSent = -1000f };
                _chunks[key] = sent;
            }
            sent.LastSample = now;
            if (_workIsNew || _workHash != sent.Hash || now - sent.LastSent > ResendSeconds)
            {
                link.SendCollisionSection(_workCx, _workCz, _heights);
                sent.LastSent = now;
                sent.Hash = _workHash;
            }
            _working = false;
        }

        private void ForgetFarChunks(int pcx, int pcz)
        {
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
    }
}
