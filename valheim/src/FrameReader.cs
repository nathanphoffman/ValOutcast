using System;
using System.IO;

namespace ValOutcast
{
    /// <summary>Reads the overlay frame file described in protocol/FRAME.md. No Unity types, so it can be tested alone.</summary>
    public class FrameReader
    {
        public const uint Magic = 0x52464F56;
        public const int HeaderSize = 32;
        public const int RequestOffset = 20;

        private readonly string _path;
        private FileStream _file;
        private readonly byte[] _header = new byte[HeaderSize];
        private uint _lastCounter;
        private FileStream _requestFile;
        private uint _requests;
        private bool _requestsKnown;
        private readonly byte[] _four = new byte[4];

        public FrameReader(string path)
        {
            _path = path;
        }

        /// <summary>
        /// Reads the newest complete frame into pixels (resized if needed). Returns false if there is no new frame
        /// (file missing, frame being written, nothing changed since the last read, or the file changed while reading).
        /// </summary>
        public bool TryRead(ref byte[] pixels, out int width, out int height)
        {
            width = height = 0;
            if (_file == null)
            {
                if (!File.Exists(_path)) return false;
                // bufferSize 1 turns buffering off: a buffer could keep showing an old header after the other process rewrote it.
                _file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1);
            }

            _file.Position = 0;
            if (_file.Read(_header, 0, HeaderSize) < HeaderSize) return false;
            uint magic = BitConverter.ToUInt32(_header, 0);
            uint version = BitConverter.ToUInt32(_header, 4);
            int w = (int)BitConverter.ToUInt32(_header, 8);
            int h = (int)BitConverter.ToUInt32(_header, 12);
            uint counter = BitConverter.ToUInt32(_header, 16);
            if (magic != Magic || version != 1 || w <= 0 || h <= 0 || w > 8192 || h > 8192) return false;
            if ((counter & 1) != 0) return false;          // being written right now
            if (counter == _lastCounter) return false;     // nothing new

            int size = w * h * 4;
            if (pixels == null || pixels.Length != size) pixels = new byte[size];

            _file.Position = HeaderSize;
            int got = 0;
            while (got < size)
            {
                int n = _file.Read(pixels, got, size - got);
                if (n <= 0) return false; // file shorter than the header says (Outcast is resizing it)
                got += n;
            }

            _file.Position = 16;
            if (_file.Read(_header, 0, 4) < 4 || BitConverter.ToUInt32(_header, 0) != counter) return false; // changed while reading

            _lastCounter = counter;
            width = w;
            height = h;
            return true;
        }

        /// <summary>
        /// Asks the other game for one new picture (it draws one per request, so its pictures keep step with ours).
        /// Call once per frame. Does nothing if the frame file does not exist yet.
        /// </summary>
        public void RequestFrame()
        {
            if (_requestFile == null)
            {
                if (!File.Exists(_path)) return;
                _requestFile = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1);
            }
            if (!_requestsKnown)
            {
                // Carry on from whatever count is in the file, so it never goes back.
                _requestFile.Position = RequestOffset;
                _requests = _requestFile.Read(_four, 0, 4) == 4 ? BitConverter.ToUInt32(_four, 0) : 0;
                _requestsKnown = true;
            }
            _requests++;
            if (_requests == 0) _requests = 1; // zero means "nobody is asking"
            BitConverter.GetBytes(_requests).CopyTo(_four, 0);
            _requestFile.Position = RequestOffset;
            _requestFile.Write(_four, 0, 4);
        }

        public void Close()
        {
            _file?.Dispose();
            _file = null;
            _requestFile?.Dispose();
            _requestFile = null;
            _requestsKnown = false;
        }
    }
}
