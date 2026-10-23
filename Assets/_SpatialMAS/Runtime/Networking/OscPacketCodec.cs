using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpatialMAS.Networking
{
    /// <summary>
    /// Represents a decoded or outgoing OSC 1.0 message.
    /// </summary>
    public readonly struct OscMessage
    {
        public readonly string Address;
        public readonly object[] Arguments;

        public OscMessage(string address, params object[] arguments)
        {
            Address = address ?? string.Empty;
            Arguments = arguments ?? Array.Empty<object>();
        }

        public int ArgCount => Arguments != null ? Arguments.Length : 0;

        public int GetInt(int index, int fallback = 0)
        {
            if (Arguments == null || index < 0 || index >= Arguments.Length) return fallback;
            object val = Arguments[index];
            if (val is int i) return i;
            if (val is float f) return (int)f;
            if (val is double d) return (int)d;
            if (val is long l) return (int)l;
            if (val is bool b) return b ? 1 : 0;
            if (val is string s && int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int parsed)) return parsed;
            return fallback;
        }

        public float GetFloat(int index, float fallback = 0f)
        {
            if (Arguments == null || index < 0 || index >= Arguments.Length) return fallback;
            object val = Arguments[index];
            if (val is float f) return f;
            if (val is int i) return i;
            if (val is double d) return (float)d;
            if (val is long l) return l;
            if (val is string s && float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float parsed)) return parsed;
            return fallback;
        }

        public string GetString(int index, string fallback = "")
        {
            if (Arguments == null || index < 0 || index >= Arguments.Length || Arguments[index] == null) return fallback;
            if (Arguments[index] is string s) return s;
            return Convert.ToString(Arguments[index], CultureInfo.InvariantCulture);
        }

        public string FormatArgumentsCsv()
        {
            if (Arguments == null || Arguments.Length == 0) return string.Empty;
            var sb = new StringBuilder(Arguments.Length * 8);
            for (int i = 0; i < Arguments.Length; i++)
            {
                if (i > 0) sb.Append(',');
                object arg = Arguments[i];
                if (arg is float f) sb.Append(f.ToString("G6", CultureInfo.InvariantCulture));
                else if (arg is double d) sb.Append(d.ToString("G6", CultureInfo.InvariantCulture));
                else sb.Append(Convert.ToString(arg, CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Clean-room, high-performance OSC 1.0 packet encoder and decoder supporting single messages
    /// and recursive #bundle packets with big-endian network byte order and 4-byte boundary padding.
    /// </summary>
    public static class OscPacketCodec
    {
        private const string BundlePrefix = "#bundle";

        /// <summary>
        /// Decodes a raw UDP datagram (either a single OSC Message or a recursive #bundle)
        /// and appends all extracted OscMessages into the target list.
        /// </summary>
        public static void DecodePacket(byte[] buffer, int offset, int length, List<OscMessage> outputMessages)
        {
            if (buffer == null || length <= 0 || offset < 0 || offset + length > buffer.Length)
                return;

            // Check if this is an OSC #bundle (starts with "#bundle\0" = 8 bytes + 8 bytes timetag = 16 bytes header)
            if (length >= 16 && IsBundleHeader(buffer, offset))
            {
                int pos = offset + 16; // Skip "#bundle\0" (8) + 64-bit timetag (8)
                int end = offset + length;
                while (pos + 4 <= end)
                {
                    int elementSize = ReadInt32BigEndian(buffer, pos);
                    pos += 4;
                    if (elementSize <= 0 || pos + elementSize > end)
                        break;

                    DecodePacket(buffer, pos, elementSize, outputMessages);
                    pos += elementSize;
                }
            }
            else
            {
                if (TryDecodeSingleMessage(buffer, offset, length, out OscMessage msg))
                {
                    outputMessages.Add(msg);
                }
            }
        }

        public static bool TryDecodeSingleMessage(byte[] buffer, int offset, int length, out OscMessage message)
        {
            message = default;
            int end = offset + length;
            int pos = offset;

            string address = ReadPaddedString(buffer, ref pos, end);
            if (string.IsNullOrEmpty(address) || address[0] != '/')
                return false;

            // If no type tag string is present, message has 0 arguments
            if (pos >= end)
            {
                message = new OscMessage(address, Array.Empty<object>());
                return true;
            }

            string typeTags = ReadPaddedString(buffer, ref pos, end);
            if (string.IsNullOrEmpty(typeTags) || typeTags[0] != ',')
            {
                message = new OscMessage(address, Array.Empty<object>());
                return true;
            }

            int argCount = typeTags.Length - 1;
            if (argCount == 0)
            {
                message = new OscMessage(address, Array.Empty<object>());
                return true;
            }

            var args = new List<object>(argCount);
            for (int i = 1; i < typeTags.Length; i++)
            {
                char tag = typeTags[i];
                switch (tag)
                {
                    case 'i':
                        if (pos + 4 > end) break;
                        args.Add(ReadInt32BigEndian(buffer, pos));
                        pos += 4;
                        break;
                    case 'f':
                        if (pos + 4 > end) break;
                        args.Add(ReadFloat32BigEndian(buffer, pos));
                        pos += 4;
                        break;
                    case 's':
                        args.Add(ReadPaddedString(buffer, ref pos, end));
                        break;
                    case 'd':
                        if (pos + 8 > end) break;
                        args.Add(ReadDouble64BigEndian(buffer, pos));
                        pos += 8;
                        break;
                    case 'h':
                        if (pos + 8 > end) break;
                        args.Add(ReadInt64BigEndian(buffer, pos));
                        pos += 8;
                        break;
                    case 'T':
                        args.Add(1);
                        break;
                    case 'F':
                        args.Add(0);
                        break;
                    case 'b':
                        if (pos + 4 > end) break;
                        int blobLen = ReadInt32BigEndian(buffer, pos);
                        pos += 4;
                        if (blobLen >= 0 && pos + blobLen <= end)
                        {
                            byte[] blob = new byte[blobLen];
                            Buffer.BlockCopy(buffer, pos, blob, 0, blobLen);
                            args.Add(blob);
                            pos += Align4(blobLen);
                        }
                        break;
                }
            }

            message = new OscMessage(address, args.ToArray());
            return true;
        }

        /// <summary>
        /// Encodes a single OscMessage into a byte array ready for UDP transmission.
        /// </summary>
        public static byte[] EncodeMessage(OscMessage message)
        {
            using (var ms = new System.IO.MemoryStream(128))
            {
                WriteMessageToStream(ms, message);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Encodes a batch of OscMessages into a single OSC #bundle packet.
        /// </summary>
        public static byte[] EncodeBundle(IReadOnlyList<OscMessage> messages, ulong timetag = 1UL)
        {
            using (var ms = new System.IO.MemoryStream(256))
            {
                // Write "#bundle\0" (8 bytes)
                byte[] header = Encoding.ASCII.GetBytes(BundlePrefix);
                ms.Write(header, 0, header.Length);
                ms.WriteByte(0);

                // Write 64-bit timetag (8 bytes big-endian)
                WriteInt64BigEndian(ms, (long)timetag);

                using (var elemStream = new System.IO.MemoryStream(128))
                {
                    for (int i = 0; i < messages.Count; i++)
                    {
                        elemStream.SetLength(0);
                        WriteMessageToStream(elemStream, messages[i]);
                        int len = (int)elemStream.Length;
                        WriteInt32BigEndian(ms, len);
                        elemStream.WriteTo(ms);
                    }
                }
                return ms.ToArray();
            }
        }

        private static void WriteMessageToStream(System.IO.Stream stream, OscMessage message)
        {
            WritePaddedString(stream, message.Address);

            object[] args = message.Arguments ?? Array.Empty<object>();
            var tagBuilder = new StringBuilder(args.Length + 2);
            tagBuilder.Append(',');

            for (int i = 0; i < args.Length; i++)
            {
                object a = args[i];
                if (a is int || a is short || a is byte) tagBuilder.Append('i');
                else if (a is float) tagBuilder.Append('f');
                else if (a is double) tagBuilder.Append('f'); // Downcast double to float32 for standard OSC compatibility
                else if (a is string) tagBuilder.Append('s');
                else if (a is bool b) tagBuilder.Append(b ? 'T' : 'F');
                else if (a is long) tagBuilder.Append('h');
                else if (a is byte[]) tagBuilder.Append('b');
                else tagBuilder.Append('s');
            }

            WritePaddedString(stream, tagBuilder.ToString());

            for (int i = 0; i < args.Length; i++)
            {
                object a = args[i];
                if (a is int iv) WriteInt32BigEndian(stream, iv);
                else if (a is short sv) WriteInt32BigEndian(stream, sv);
                else if (a is byte bv) WriteInt32BigEndian(stream, bv);
                else if (a is float fv) WriteFloat32BigEndian(stream, fv);
                else if (a is double dv) WriteFloat32BigEndian(stream, (float)dv);
                else if (a is string str) WritePaddedString(stream, str);
                else if (a is bool) { /* T/F have no payload bytes */ }
                else if (a is long lv) WriteInt64BigEndian(stream, lv);
                else if (a is byte[] blob)
                {
                    WriteInt32BigEndian(stream, blob.Length);
                    stream.Write(blob, 0, blob.Length);
                    int pad = Align4(blob.Length) - blob.Length;
                    for (int p = 0; p < pad; p++) stream.WriteByte(0);
                }
                else
                {
                    WritePaddedString(stream, Convert.ToString(a, CultureInfo.InvariantCulture) ?? string.Empty);
                }
            }
        }

        private static bool IsBundleHeader(byte[] buffer, int offset)
        {
            return buffer[offset] == (byte)'#' &&
                   buffer[offset + 1] == (byte)'b' &&
                   buffer[offset + 2] == (byte)'u' &&
                   buffer[offset + 3] == (byte)'n' &&
                   buffer[offset + 4] == (byte)'d' &&
                   buffer[offset + 5] == (byte)'l' &&
                   buffer[offset + 6] == (byte)'e' &&
                   buffer[offset + 7] == 0;
        }

        private static string ReadPaddedString(byte[] buffer, ref int pos, int end)
        {
            if (pos >= end) return string.Empty;
            int nullIdx = pos;
            while (nullIdx < end && buffer[nullIdx] != 0)
            {
                nullIdx++;
            }
            string result = Encoding.UTF8.GetString(buffer, pos, nullIdx - pos);
            int totalBytes = (nullIdx - pos) + 1; // Include at least 1 null terminator
            pos += Align4(totalBytes);
            if (pos > end) pos = end;
            return result;
        }

        private static void WritePaddedString(System.IO.Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            stream.Write(bytes, 0, bytes.Length);
            int totalWithNull = bytes.Length + 1;
            int paddedLen = Align4(totalWithNull);
            int zerosToWrite = paddedLen - bytes.Length;
            for (int i = 0; i < zerosToWrite; i++)
            {
                stream.WriteByte(0);
            }
        }

        private static int Align4(int length)
        {
            return (length + 3) & ~3;
        }

        private static int ReadInt32BigEndian(byte[] buffer, int pos)
        {
            return (buffer[pos] << 24) |
                   (buffer[pos + 1] << 16) |
                   (buffer[pos + 2] << 8) |
                   buffer[pos + 3];
        }

        private static long ReadInt64BigEndian(byte[] buffer, int pos)
        {
            uint hi = (uint)ReadInt32BigEndian(buffer, pos);
            uint lo = (uint)ReadInt32BigEndian(buffer, pos + 4);
            return ((long)hi << 32) | lo;
        }

        private static float ReadFloat32BigEndian(byte[] buffer, int pos)
        {
            byte[] temp = new byte[4];
            if (BitConverter.IsLittleEndian)
            {
                temp[0] = buffer[pos + 3];
                temp[1] = buffer[pos + 2];
                temp[2] = buffer[pos + 1];
                temp[3] = buffer[pos];
            }
            else
            {
                Buffer.BlockCopy(buffer, pos, temp, 0, 4);
            }
            return BitConverter.ToSingle(temp, 0);
        }

        private static double ReadDouble64BigEndian(byte[] buffer, int pos)
        {
            byte[] temp = new byte[8];
            if (BitConverter.IsLittleEndian)
            {
                for (int i = 0; i < 8; i++) temp[i] = buffer[pos + 7 - i];
            }
            else
            {
                Buffer.BlockCopy(buffer, pos, temp, 0, 8);
            }
            return BitConverter.ToDouble(temp, 0);
        }

        private static void WriteInt32BigEndian(System.IO.Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 24) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteInt64BigEndian(System.IO.Stream stream, long value)
        {
            WriteInt32BigEndian(stream, (int)((value >> 32) & 0xFFFFFFFF));
            WriteInt32BigEndian(stream, (int)(value & 0xFFFFFFFF));
        }

        private static void WriteFloat32BigEndian(System.IO.Stream stream, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
            {
                stream.WriteByte(bytes[3]);
                stream.WriteByte(bytes[2]);
                stream.WriteByte(bytes[1]);
                stream.WriteByte(bytes[0]);
            }
            else
            {
                stream.Write(bytes, 0, 4);
            }
        }
    }
}
