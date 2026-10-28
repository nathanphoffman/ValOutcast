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
        public const int FovOffset = 24;

        // Where each field sits in the 32-byte header (see protocol/FRAME.md).
        private const int VersionAt = 4;
        private const int WidthAt = 8;
        private const int HeightAt = 12;
        private const int CounterAt = 16;
        private const uint SupportedVersion = 1;
        private const int MaxFrameSide = 8192;
        private const int BytesPerPixel = 4;
        private const int ValueBytes = sizeof(uint);

        private readonly string _path;
        private FileStream _file;
        private readonly byte[] _header = new byte[HeaderSize];
        private uint _lastCounter;
        private FileStream _requestFile;
        private uint _requests;
        private bool _requestsKnown;
        private readonly byte[] _four = new byte[ValueBytes];

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
            uint version = BitConverter.ToUInt32(_header, VersionAt);
            int frameWidth = (int)BitConverter.ToUInt32(_header, WidthAt);
            int frameHeight = (int)BitConverter.ToUInt32(_header, HeightAt);
            uint counter = BitConverter.ToUInt32(_header, CounterAt);

            if (magic != Magic || version != SupportedVersion || frameWidth <= 0 || frameHeight <= 0 || frameWidth > MaxFrameSide || frameHeight > MaxFrameSide) return false;

            if ((counter & 1) != 0) return false;          // being written right now

            if (counter == _lastCounter) return false;     // nothing new

            int size = frameWidth * frameHeight * BytesPerPixel;

            if (pixels == null || pixels.Length != size) pixels = new byte[size];

            _file.Position = HeaderSize;
            int filled = 0;

            while (filled < size)
            {
                int bytesRead = _file.Read(pixels, filled, size - filled);

                if (bytesRead <= 0) return false; // file shorter than the header says (Outcast is resizing it)
                filled += bytesRead;
            }

            _file.Position = CounterAt;

            if (_file.Read(_header, 0, ValueBytes) < ValueBytes || BitConverter.ToUInt32(_header, 0) != counter) return false; // changed while reading

            _lastCounter = counter;
            width = frameWidth;
            height = frameHeight;

            return true;
        }

        /// <summary>
        /// Asks the other game for one new picture (it draws one per request, so its pictures keep step with ours).
        /// Call once per frame. Does nothing if the frame file does not exist yet.
        /// fovDegrees is Valheim's horizontal field of view, so the other game can draw with the same one (0 = not given).
        /// </summary>
        public void RequestFrame(float fovDegrees = 0f)
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
                _requests = _requestFile.Read(_four, 0, ValueBytes) == ValueBytes ? BitConverter.ToUInt32(_four, 0) : 0;
                _requestsKnown = true;
            }

            _requests++;

            if (_requests == 0) _requests = 1; // zero means "nobody is asking"
            BitConverter.GetBytes(_requests).CopyTo(_four, 0);
            _requestFile.Position = RequestOffset;
            _requestFile.Write(_four, 0, ValueBytes);
            BitConverter.GetBytes(fovDegrees).CopyTo(_four, 0);
            _requestFile.Position = FovOffset;
            _requestFile.Write(_four, 0, ValueBytes);
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
