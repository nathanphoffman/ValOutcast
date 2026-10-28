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
        private const float MinObjectHeight = 0.3f;    // lower than this counts as part of the ground (the player walks over it)
        private const float StepHeight = 0.55f;        // up to this high the player steps onto things (Outcast's step is 0.56 m)
        // A taller object only blocks if it is solid at body height. Branches, roofs and arches higher up are walked under.
        private const float BodyLow = 0.6f, BodyHigh = 1.8f;
        private const float CheckHalfWidth = 0.2f;     // a little less than half a cell, so thin things at a cell's edge do not block it

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
            for (int offsetZ = -Radius; offsetZ <= Radius; offsetZ++)

                for (int offsetX = -Radius; offsetX <= Radius; offsetX++)
                    _offsets.Add(new Vector2Int(offsetX, offsetZ));
            _offsets.Sort((first, second) => (first.x * first.x + first.y * first.y).CompareTo(second.x * second.x + second.y * second.y));
        }

        private static long ChunkKey(int chunkX, int chunkZ)
        {
            // Pack the two chunk coordinates into one long key: x in the high 32 bits, z in the low 32.
            return ((long)chunkX << 32) | (uint)chunkZ;
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
            int playerChunkX = Mathf.FloorToInt(wire.x / (VoLink.ChunkCells * VoLink.CellSizeM));
            int playerChunkZ = Mathf.FloorToInt(wire.z / (VoLink.ChunkCells * VoLink.CellSizeM));

            _clock.Restart();

            while (_clock.Elapsed.TotalMilliseconds < BudgetMs)
            {
                if (!_working && !StartNextChunk(playerChunkX, playerChunkZ)) break;

                if (_working) Measure(link);
            }

            ForgetFarChunks(playerChunkX, playerChunkZ);
        }

        /// <summary>Picks what to measure next: the nearest chunk never measured, else the oldest one now and then.</summary>
        private bool StartNextChunk(int playerChunkX, int playerChunkZ)
        {
            foreach (Vector2Int offset in _offsets)
            {
                int chunkX = playerChunkX + offset.x, chunkZ = playerChunkZ + offset.y;

                if (_chunks.ContainsKey(ChunkKey(chunkX, chunkZ))) continue;
                Begin(chunkX, chunkZ, true);

                return true;
            }

            float currentSeconds = Time.realtimeSinceStartup;

            if (currentSeconds < _nextRefresh) return false;
            _nextRefresh = currentSeconds + RefreshEverySeconds;

            long oldestKey = 0;
            float oldest = float.MaxValue;

            foreach (var entry in _chunks)
            {
                if (entry.Value.LastSample < oldest) { oldest = entry.Value.LastSample; oldestKey = entry.Key; }
            }

            if (oldest == float.MaxValue) return false;

            // Unpack the key: high 32 bits are x, low 32 bits are z.
            Begin((int)(oldestKey >> 32), (int)(uint)(oldestKey & 0xFFFFFFFF), false);

            return true;
        }

        private void Begin(int chunkX, int chunkZ, bool isNew)
        {
            _working = true;
            _workCx = chunkX;
            _workCz = chunkZ;
            _workCell = 0;
            _workHash = 17;
            _workIsNew = isNew;

            if (_objectMask < 0)
            {
                _objectMask = BuildObjectMask();
            }
        }

        /// <summary>
        /// The layers that really stop the player, taken from Valheim's own collision rules (so bushes, pickables and other
        /// things the player walks through do not become walls). Falls back to a fixed list if that finds nothing.
        /// </summary>
        private static int BuildObjectMask()
        {
            int character = LayerMask.NameToLayer("character");
            int mask = 0;
            var names = new List<string>();

            if (character >= 0)
            {
                for (int layer = 0; layer < 32; layer++)
                {
                    string name = LayerMask.LayerToName(layer);

                    if (string.IsNullOrEmpty(name)) continue;

                    if (name.StartsWith("character") || name == "terrain" || name == "Water" || name == "WaterVolume" || name == "UI" || name == "ghost") continue;

                    if (Physics.GetIgnoreLayerCollision(character, layer)) continue;

                    // Set the bit for this layer in the layer mask.
                    mask |= 1 << layer;
                    names.Add(name);
                }
            }

            if (mask == 0)
            {
                mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
                names.Add("(fixed list)");
            }

            Plugin.LogMessage("terrain: objects that block the player are on layers " + string.Join(", ", names));

            return mask;
        }

        /// <summary>Measures cells of the current chunk until the time budget runs out or the chunk is done.</summary>
        private void Measure(VoLink link)
        {
            int cells = VoLink.ChunkCells * VoLink.ChunkCells;

            while (_workCell < cells)
            {
                if (_clock.Elapsed.TotalMilliseconds >= BudgetMs) return;

                int cellX = _workCell % VoLink.ChunkCells;
                int cellZ = _workCell / VoLink.ChunkCells;
                float wireX = (_workCx * VoLink.ChunkCells + cellX + 0.5f) * VoLink.CellSizeM;
                float wireZ = (_workCz * VoLink.ChunkCells + cellZ + 0.5f) * VoLink.CellSizeM;
                Vector3 worldPosition = PlayerFollow.WireToWorldXZ(wireX, wireZ);

                short height = VoLink.NoHeight;

                if (ZoneSystem.instance.GetGroundHeight(worldPosition, out float ground))
                {
                    float surfaceTop = ground;

                    // Something solid standing on the ground (tree, rock, wall)? Then the column is as tall as it really is.
                    // (Measured from 30 m above the ground, so a roof higher than that is ignored.)
                    var from = new Vector3(worldPosition.x, ground + ObjectSearchHeight, worldPosition.z);

                    if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, ObjectSearchHeight, _objectMask, QueryTriggerInteraction.Ignore)
                        && hit.point.y > ground + MinObjectHeight)
                    {
                        // Low things are stepped onto. Taller ones only count if something solid is at body height here.
                        bool isLow = hit.point.y <= ground + StepHeight;

                        if (isLow || SolidAtBodyHeight(worldPosition, ground)) surfaceTop = hit.point.y;
                    }

                    float wireY = PlayerFollow.WorldYToWireY(surfaceTop);
                    height = (short)Mathf.Clamp(Mathf.RoundToInt(wireY * VoLink.HeightUnitsPerM), -32767, 32767);
                }

                _heights[_workCell] = height;
                _workHash = _workHash * 31 + height;
                _workCell++;
            }

            // Chunk finished.
            long chunkKey = ChunkKey(_workCx, _workCz);
            float currentSeconds = Time.realtimeSinceStartup;

            if (!_chunks.TryGetValue(chunkKey, out Sent sent))
            {
                sent = new Sent { LastSent = -1000f };
                _chunks[chunkKey] = sent;
            }

            sent.LastSample = currentSeconds;

            if (_workIsNew || _workHash != sent.Hash || currentSeconds - sent.LastSent > ResendSeconds)
            {
                link.SendCollisionSection(_workCx, _workCz, _heights);
                sent.LastSent = currentSeconds;
                sent.Hash = _workHash;
            }

            _working = false;
        }

        private bool SolidAtBodyHeight(Vector3 worldPosition, float ground)
        {
            float half = (BodyHigh - BodyLow) * 0.5f;
            var center = new Vector3(worldPosition.x, ground + BodyLow + half, worldPosition.z);

            return Physics.CheckBox(center, new Vector3(CheckHalfWidth, half, CheckHalfWidth), Quaternion.identity, _objectMask, QueryTriggerInteraction.Ignore);
        }

        private void ForgetFarChunks(int playerChunkX, int playerChunkZ)
        {
            List<long> farChunks = null;

            foreach (long chunkKey in _chunks.Keys)
            {
                // Unpack the key: high 32 bits are x, low 32 bits are z.
                int chunkX = (int)(chunkKey >> 32), chunkZ = (int)(uint)(chunkKey & 0xFFFFFFFF);

                if (Mathf.Abs(chunkX - playerChunkX) > Radius + 2 || Mathf.Abs(chunkZ - playerChunkZ) > Radius + 2)
                {
                    (farChunks ?? (farChunks = new List<long>())).Add(chunkKey);
                }
            }

            if (farChunks != null) foreach (long chunkKey in farChunks) _chunks.Remove(chunkKey);
        }
    }
}
