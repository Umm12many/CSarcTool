using System;
using System.Collections.Generic;
using System.Text;

namespace CSarcTool.Core
{
    public static class Yaz0
    {
        public static bool IsYazCompressed(byte[] data)
        {
            if (data.Length < 4) return false;
            string magic = Encoding.ASCII.GetString(data, 0, 4);
            return magic == "Yaz0" || magic == "Yaz1";
        }

        public static byte[] Decompress(byte[] data)
        {
            // Abort if too small to contain a header
            if (data.Length < 16) return data;

            // Header Parsing (Big Endian)
            uint destEnd = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);

            byte[] dest = new byte[destEnd];

            byte code = data[16];
            int srcPos = 17;
            int destPos = 0;

            while (srcPos < data.Length && destPos < destEnd)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (destPos >= destEnd || srcPos >= data.Length) goto Done;

                    if ((code & 0x80) != 0)
                    {
                        dest[destPos++] = data[srcPos++];
                    }
                    else
                    {
                        if (srcPos + 1 >= data.Length) goto Done;

                        byte b1 = data[srcPos++];
                        byte b2 = data[srcPos++];

                        int offset = ((b1 & 0x0f) << 8) | b2;
                        int copySrc = destPos - offset - 1;

                        int n = b1 >> 4;
                        if (n == 0)
                        {
                            if (srcPos >= data.Length) goto Done;
                            n = data[srcPos++] + 0x12;
                        }
                        else
                        {
                            n += 2;
                        }

                        if (copySrc < 0) copySrc += dest.Length;

                        for (int k = 0; k < n; k++)
                        {
                            if (destPos >= destEnd) break;
                            if (copySrc >= dest.Length) copySrc = 0;
                            dest[destPos++] = dest[copySrc++];
                        }
                    }
                    code <<= 1;
                }
                if (srcPos >= data.Length) break;
                code = data[srcPos++];
            }

        Done:
            return dest;
        }

        public static byte[] Compress(byte[] src, int level = 7)
        {
            int searchRange;
            if (level <= 0) searchRange = 0;
            else if (level < 9) searchRange = 0x10e0 * level / 9 - 0x0e0;
            else searchRange = 0x1000;

            int pos = 0;
            int srcEnd = src.Length;

            List<byte> dest = new List<byte>();

            // Yaz0 Header (16 bytes total)
            dest.AddRange(Encoding.ASCII.GetBytes("Yaz0"));

            // Uncompressed size (big endian)
            dest.Add((byte)((src.Length >> 24) & 0xFF));
            dest.Add((byte)((src.Length >> 16) & 0xFF));
            dest.Add((byte)((src.Length >> 8) & 0xFF));
            dest.Add((byte)(src.Length & 0xFF));

            // 8 bytes of padding/reserved space
            dest.AddRange(new byte[8]);

            int maxLen = 0x111;

            while (pos < srcEnd)
            {
                int codeBytePos = dest.Count;
                dest.Add(0);
                byte code = 0;

                for (int i = 0; i < 8; i++)
                {
                    if (pos >= srcEnd) break;

                    int foundLen = 1;
                    int foundOffset = 0;

                    if (searchRange > 0)
                    {
                        CompressionSearch(src, pos, maxLen, searchRange, srcEnd, out foundOffset, out foundLen);
                    }

                    if (foundLen > 2)
                    {
                        int delta = pos - foundOffset - 1;

                        if (foundLen < 0x12)
                        {
                            dest.Add((byte)((delta >> 8) | ((foundLen - 2) << 4)));
                            dest.Add((byte)(delta & 0xFF));
                        }
                        else
                        {
                            dest.Add((byte)(delta >> 8));
                            dest.Add((byte)(delta & 0xFF));
                            dest.Add((byte)((foundLen - 0x12) & 0xFF));
                        }

                        pos += foundLen;
                    }
                    else
                    {
                        code |= (byte)(1 << (7 - i));
                        dest.Add(src[pos]);
                        pos++;
                    }
                }

                dest[codeBytePos] = code;
            }

            return dest.ToArray();
        }

        private static void CompressionSearch(byte[] src, int pos, int maxLen, int searchRange, int srcEnd, out int found, out int foundLen)
        {
            found = 0;
            foundLen = 1;

            if (pos + 2 < srcEnd)
            {
                int search = pos - searchRange;
                if (search < 0) search = 0;

                int cmpEnd = pos + maxLen;
                if (cmpEnd > srcEnd) cmpEnd = srcEnd;

                byte searchByte = src[pos];

                while (search < pos)
                {
                    // Find match for first byte
                    int p = search;
                    bool matchStart = false;
                    while (p < pos)
                    {
                        if (src[p] == searchByte)
                        {
                            search = p;
                            matchStart = true;
                            break;
                        }
                        p++;
                    }

                    if (!matchStart)
                    {
                        break;
                    }

                    int cmp1 = search + 1;
                    int cmp2 = pos + 1;

                    while (cmp2 < cmpEnd && src[cmp1] == src[cmp2])
                    {
                        cmp1++;
                        cmp2++;
                    }

                    int len = cmp2 - pos;

                    if (foundLen < len)
                    {
                        foundLen = len;
                        found = search;
                        if (foundLen == maxLen) break;
                    }

                    search++;
                }
            }
        }
    }
}
