using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ValOutcast
{
    /// <summary>
    /// Times the plugin's own work and logs a summary every 10 seconds, so slowness can be traced to a part.
    /// </summary>
    public class PerfStats
    {
        private class Entry
        {
            public double TotalMs;
            public double MaxMs;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly Action<string> _logger;
        private readonly Stopwatch _since = Stopwatch.StartNew();
        private int _frames;

        public PerfStats(Action<string> logger)
        {
            _logger = logger;
        }

        public void Measure(string name, Action action)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

            if (!_entries.TryGetValue(name, out Entry e)) _entries[name] = e = new Entry();
            e.TotalMs += milliseconds;

            if (milliseconds > e.MaxMs) e.MaxMs = milliseconds;
        }

        /// <summary>Call once per frame.</summary>
        public void EndFrame(bool active)
        {
            _frames++;

            if (_since.Elapsed.TotalSeconds < 10) return;

            if (active && _frames > 0)
            {
                var parts = new List<string>();

                foreach (var entry in _entries)
                {
                    parts.Add(entry.Key + " " + (entry.Value.TotalMs / _frames).ToString("0.00") + " ms (worst " + entry.Value.MaxMs.ToString("0.0") + ")");
                }

                _logger("perf: " + (_frames / _since.Elapsed.TotalSeconds).ToString("0") + " frames/s; per frame: " + string.Join(", ", parts));
            }

            _entries.Clear();
            _frames = 0;
            _since.Restart();
        }
    }
}
