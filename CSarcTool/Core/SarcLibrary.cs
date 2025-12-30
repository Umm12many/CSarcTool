using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CSarcTool.Core
{
    public enum Endianness
    {
        Big,
        Little
    }

    public static class EndianIO
    {
        public static ushort ReadUInt16(this BinaryReader reader, Endianness endianness)
        {
            var bytes = reader.ReadBytes(2);
            if (bytes.Length < 2) throw new EndOfStreamException("Unexpected end of stream while reading UInt16.");

            if ((endianness == Endianness.Big && BitConverter.IsLittleEndian) ||
                (endianness == Endianness.Little && !BitConverter.IsLittleEndian))
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt16(bytes, 0);
        }

        public static uint ReadUInt32(this BinaryReader reader, Endianness endianness)
        {
            var bytes = reader.ReadBytes(4);
            if (bytes.Length < 4) throw new EndOfStreamException("Unexpected end of stream while reading UInt32.");

            if ((endianness == Endianness.Big && BitConverter.IsLittleEndian) ||
                (endianness == Endianness.Little && !BitConverter.IsLittleEndian))
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt32(bytes, 0);
        }

        public static void Write(this BinaryWriter writer, ushort value, Endianness endianness)
        {
            var bytes = BitConverter.GetBytes(value);
            if ((endianness == Endianness.Big && BitConverter.IsLittleEndian) ||
                (endianness == Endianness.Little && !BitConverter.IsLittleEndian))
            {
                Array.Reverse(bytes);
            }
            writer.Write(bytes);
        }

        public static void Write(this BinaryWriter writer, uint value, Endianness endianness)
        {
            var bytes = BitConverter.GetBytes(value);
            if ((endianness == Endianness.Big && BitConverter.IsLittleEndian) ||
                (endianness == Endianness.Little && !BitConverter.IsLittleEndian))
            {
                Array.Reverse(bytes);
            }
            writer.Write(bytes);
        }
    }

    public abstract class SarcEntry
    {
        public string Name { get; set; }
    }

    public class SarcFile : SarcEntry
    {
        public byte[] Data { get; set; }
        public bool HasFilename { get; set; }

        public SarcFile(string name, byte[] data, bool hasFilename = true)
        {
            Name = name;
            Data = data;
            HasFilename = hasFilename;
        }
    }

    public class SarcFolder : SarcEntry
    {
        public List<SarcEntry> Contents { get; set; }

        public SarcFolder(string name)
        {
            Name = name;
            Contents = new List<SarcEntry>();
        }

        public void Add(SarcEntry entry)
        {
            Contents.Add(entry);
        }

        public SarcFolder GetFolder(string name)
        {
            return Contents.OfType<SarcFolder>().FirstOrDefault(f => f.Name == name);
        }
    }

    public class SarcArchive
    {
        public SarcFolder Root { get; private set; }
        public Endianness Endian { get; set; } = Endianness.Big;
        public uint HashKey { get; set; } = 0x65;

        public SarcArchive()
        {
            Root = new SarcFolder("");
        }

        public SarcArchive(byte[] data) : this()
        {
            Load(data);
        }

        public void AddFile(SarcFile file)
        {
            Root.Add(file);
        }

        public void AddFolder(SarcFolder folder)
        {
            Root.Add(folder);
        }

        public static string GuessFileExtension(byte[] data)
        {
            if (data.Length < 4) return ".bin";

            string Magic(int len) => Encoding.ASCII.GetString(data, 0, len);
            string MagicAt(int offset, int len) => Encoding.ASCII.GetString(data, offset, len);

            if (data.Length >= 8)
            {
                if (Magic(8) == "BNTX\0\0\0\0") return ".bntx";
                if (Magic(8) == "BNSH\0\0\0\0") return ".bnsh";
                if (Magic(8) == "MsgStdBn") return ".msbt";
                if (Magic(8) == "MsgPrjBn") return ".msbp";
            }

            string head4 = Magic(4);
            if (head4 == "SARC") return ".sarc";
            if (head4 == "Yaz0" || head4 == "Yaz1") return ".szs";
            if (head4 == "FFNT") return ".bffnt";
            if (head4 == "CFNT") return ".bcfnt";
            if (head4 == "CSTM") return ".bcstm";
            if (head4 == "FSTM") return ".bfstm";
            if (head4 == "FSTP") return ".bfstp";
            if (head4 == "CWAV") return ".bcwav";
            if (head4 == "FWAV") return ".bfwav";
            if (head4 == "Gfx2") return ".gtx";
            if (head4 == "FRES") return ".bfres";
            if (head4 == "AAHS") return ".sharc";
            if (head4 == "BAHS") return ".sharcfb";
            if (head4 == "FSHA") return ".bfsha";
            if (head4 == "FLAN") return ".bflan";
            if (head4 == "FLYT") return ".bflyt";
            if (head4 == "CLAN") return ".bclan";
            if (head4 == "CLYT") return ".bclyt";
            if (head4 == "CTPK") return ".ctpk";
            if (head4 == "CGFX") return ".bcres";
            if (head4 == "AAMP") return ".aamp";

            if (data.Length > 0x28)
            {
                string tail = Encoding.ASCII.GetString(data, data.Length - 0x28, 4);
                if (tail == "FLIM") return ".bflim";
                if (tail == "CLIM") return ".bclim";
            }

            if (data.Length >= 2)
            {
                string head2 = Magic(2);
                if (head2 == "YB" || head2 == "BY") return ".byml";
            }

            if (data.Length >= 0x10)
            {
                if (MagicAt(0xC, 4) == "SCDL") return ".bcd";
            }

            return ".bin";
        }

        public void Load(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                ushort nodeCountDebug = 0;
                try
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "SARC")
                        throw new Exception("Invalid Magic");

                    byte[] headLenBytes = reader.ReadBytes(2);
                    byte[] bomBytes = reader.ReadBytes(2);

                    if (bomBytes[0] == 0xFE && bomBytes[1] == 0xFF)
                    {
                        Endian = Endianness.Big;
                    }
                    else if (bomBytes[0] == 0xFF && bomBytes[1] == 0xFE)
                    {
                        Endian = Endianness.Little;
                    }
                    else
                    {
                        throw new Exception($"Invalid BOM: {bomBytes[0]:X2} {bomBytes[1]:X2}");
                    }

                    ms.Position = 4;
                    ushort headerLen = reader.ReadUInt16(Endian);
                    reader.ReadUInt16(Endian);

                    uint fileSize = reader.ReadUInt32(Endian);
                    uint dataStartOffset = reader.ReadUInt32(Endian);
                    reader.ReadUInt32(Endian);

                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "SFAT")
                        throw new Exception("Invalid SFAT Magic");

                    ushort sfatHeaderLen = reader.ReadUInt16(Endian);
                    ushort nodeCount = reader.ReadUInt16(Endian);
                    nodeCountDebug = nodeCount;
                    uint hashKey = reader.ReadUInt32(Endian);
                    this.HashKey = hashKey;

                    var nodes = new List<(uint nameHash, bool hasName, uint nameOffset, uint dataStart, uint dataLen)>();

                    for (int i = 0; i < nodeCount; i++)
                    {
                        uint nameHash = reader.ReadUInt32(Endian);
                        uint nameTableEntry = reader.ReadUInt32(Endian);
                        uint nodeDataStart = reader.ReadUInt32(Endian);
                        uint nodeDataEnd = reader.ReadUInt32(Endian);

                        bool hasName = (nameTableEntry >> 24) != 0;
                        uint nameOffset = nameTableEntry & 0xFFFFFF;
                        uint len = nodeDataEnd - nodeDataStart;

                        nodes.Add((nameHash, hasName, nameOffset, nodeDataStart, len));
                    }

                    long sfntStart = ms.Position;
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "SFNT")
                        throw new Exception("Invalid SFNT Magic");

                    ushort sfntHeaderLen = reader.ReadUInt16(Endian);
                    reader.ReadUInt16(Endian);

                    Root = new SarcFolder("");

                    foreach (var node in nodes)
                    {
                        byte[] fileData = new byte[node.dataLen];
                        long savedPos = ms.Position;

                        ms.Position = dataStartOffset + node.dataStart;
                        if (ms.Position + node.dataLen > ms.Length)
                            throw new Exception($"File data offset out of bounds for node {node.nameHash:X}");

                        ms.Read(fileData, 0, (int)node.dataLen);

                        string name;
                        if (node.hasName)
                        {
                            ms.Position = sfntStart + 8 + (node.nameOffset * 4);
                            name = ReadNullTerminatedString(reader);
                        }
                        else
                        {
                            name = "hash_" + node.nameHash.ToString("X") + GuessFileExtension(fileData);
                        }

                        ms.Position = savedPos;
                        AddFileToTree(name, fileData, node.hasName);
                    }
                }
                catch (EndOfStreamException ex)
                {
                    throw new Exception($"SARC Parsing Error: Unexpected End of Stream. (Stream Pos: {ms.Position}, Stream Length: {ms.Length}, NodeCount: {nodeCountDebug})", ex);
                }
                catch (Exception ex)
                {
                    if (!ex.Message.StartsWith("SARC Parsing Error"))
                        throw new Exception($"SARC Parsing Error: {ex.Message} (Stream Pos: {ms.Position}, Stream Length: {ms.Length})", ex);
                    throw;
                }
            }
        }

        private string ReadNullTerminatedString(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            while (true)
            {
                byte b = reader.ReadByte();
                if (b == 0) break;
                bytes.Add(b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private void AddFileToTree(string fullPath, byte[] data, bool hasFilename)
        {
            string[] parts = fullPath.Split('/');
            SarcFolder current = Root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                var next = current.GetFolder(part);
                if (next == null)
                {
                    next = new SarcFolder(part);
                    current.Add(next);
                }
                current = next;
            }

            current.Add(new SarcFile(parts.Last(), data, hasFilename));
        }

        public static uint CalcHash(string name, uint key)
        {
            uint result = 0;
            foreach (char c in name)
            {
                result = (result * key + (uint)c) & 0xFFFFFFFF;
            }
            return result;
        }

        public static int GetDataAlignment(byte[] data)
        {
            if (data.Length < 4) return 4;
            string magic = Encoding.ASCII.GetString(data, 0, 4);

            if (magic == "SARC") return 0x2000;
            if (magic == "Yaz0" || magic == "Yaz1") return 0x80;
            if (magic == "FFNT") return 0x2000;
            if (magic == "CFNT") return 0x80;
            if (magic == "CSTM" || magic == "FSTM" || magic == "FSTP" || magic == "CWAV" || magic == "FWAV") return 0x20;
            if (data.Length >= 8 && (Encoding.ASCII.GetString(data, 0, 8) == "BNTX\0\0\0\0" || Encoding.ASCII.GetString(data, 0, 8) == "BNSH\0\0\0\0" || Encoding.ASCII.GetString(data, 0, 8) == "FSHA    ")) return 0x1000;
            if (magic == "Gfx2" || magic == "FRES" || magic == "AAHS" || magic == "BAHS") return 0x2000;
            if (data.Length > 0x28)
            {
                string tail = Encoding.ASCII.GetString(data, data.Length - 0x28, 4);
                if (tail == "FLIM") return 0x2000;
                if (tail == "CLIM") return 0x80;
            }
            if (magic == "CTPK") return 0x10;
            if (magic == "CGFX") return 0x80;
            if (magic == "AAMP") return 8;

            if (data.Length >= 2)
            {
                string head2 = Encoding.ASCII.GetString(data, 0, 2);
                if (head2 == "YB" || head2 == "BY") return 0x80;
            }

            if (data.Length >= 8 && (Encoding.ASCII.GetString(data, 0, 8) == "MsgStdBn" || Encoding.ASCII.GetString(data, 0, 8) == "MsgPrjBn")) return 0x80;

            if (data.Length >= 0x10 && Encoding.ASCII.GetString(data, 0xC, 4) == "SCDL") return 0x100;

            return 4;
        }

        public (byte[] data, int maxAlign) Save()
        {
            var files = new List<(string path, SarcFile file)>();
            Flatten(Root, "", files);

            files.Sort((a, b) => {
                uint GetHashVal(string name, bool hasName)
                {
                    if (!hasName)
                    {
                        var hex = name.Substring(5).Split('.')[0];
                        return Convert.ToUInt32(hex, 16);
                    }
                    return CalcHash(name, HashKey);
                }

                return GetHashVal(a.path, a.file.HasFilename).CompareTo(GetHashVal(b.path, b.file.HasFilename));
            });

            var packEntries = new List<(SarcFile file, string fullPath, int nameOffset, int dataOffset)>();
            List<byte> nameTableBytes = new List<byte>();

            foreach (var f in files)
            {
                int nameOffset = 0;
                if (f.file.HasFilename)
                {
                    nameOffset = nameTableBytes.Count;
                    nameTableBytes.AddRange(Encoding.UTF8.GetBytes(f.path));
                    nameTableBytes.Add(0);
                    while (nameTableBytes.Count % 4 != 0) nameTableBytes.Add(0);
                }
                packEntries.Add((f.file, f.path, nameOffset, 0));
            }

            int sfatNodesLen = 0x10 * files.Count;
            int fileNamesLen = nameTableBytes.Count;

            int RoundUp(int x, int y) => ((x - 1) | (y - 1)) + 1;

            uint dataStartOffset = (uint)RoundUp(0x20 + sfatNodesLen + 0x08 + fileNamesLen, 4);

            int maxAlignment = 0;
            var dataTable = new MemoryStream();

            for (int i = 0; i < packEntries.Count; i++)
            {
                var entry = packEntries[i];
                int align = GetDataAlignment(entry.file.Data);
                maxAlignment = Math.Max(maxAlignment, align);

                long currentLen = dataTable.Length;
                long targetLen = RoundUp((int)currentLen, align);

                while (dataTable.Length < targetLen) dataTable.WriteByte(0);

                entry.dataOffset = (int)dataTable.Length;
                packEntries[i] = entry;

                dataTable.Write(entry.file.Data, 0, entry.file.Data.Length);
            }

            dataStartOffset = (uint)RoundUp((int)dataStartOffset, maxAlignment);

            var ms = new MemoryStream();
            var writer = new BinaryWriter(ms);

            writer.Write(Encoding.ASCII.GetBytes("SARC"));
            writer.Write((ushort)0x14, Endian);
            writer.Write((ushort)(Endian == Endianness.Big ? 0xFEFF : 0xFFFE), Endian);
            uint totalSize = dataStartOffset + (uint)dataTable.Length;
            writer.Write(totalSize, Endian);
            writer.Write(dataStartOffset, Endian);
            writer.Write((uint)0x01000000, Endian);

            writer.Write(Encoding.ASCII.GetBytes("SFAT"));
            writer.Write((ushort)0x0C, Endian);
            writer.Write((ushort)files.Count, Endian);
            writer.Write(HashKey, Endian);

            foreach (var entry in packEntries)
            {
                uint hash;
                if (!entry.file.HasFilename)
                {
                    hash = Convert.ToUInt32(entry.file.Name.Substring(5).Split('.')[0], 16);
                    writer.Write(hash, Endian);
                    writer.Write((uint)0, Endian);
                }
                else
                {
                    hash = CalcHash(entry.fullPath, HashKey);
                    writer.Write(hash, Endian);
                    uint offsetVal = (uint)(entry.nameOffset / 4) | 0x01000000;
                    writer.Write(offsetVal, Endian);
                }

                writer.Write((uint)entry.dataOffset, Endian);
                writer.Write((uint)(entry.dataOffset + entry.file.Data.Length), Endian);
            }

            writer.Write(Encoding.ASCII.GetBytes("SFNT"));
            writer.Write((ushort)0x08, Endian);
            writer.Write((ushort)0, Endian);
            writer.Write(nameTableBytes.ToArray());

            long currentHeaderSize = ms.Length;
            if (dataStartOffset > currentHeaderSize)
            {
                byte[] padding = new byte[dataStartOffset - currentHeaderSize];
                writer.Write(padding);
            }

            dataTable.Position = 0;
            dataTable.CopyTo(ms);

            return (ms.ToArray(), maxAlignment);
        }

        private void Flatten(SarcFolder folder, string path, List<(string, SarcFile)> result)
        {
            if (path.Contains("\\")) path = path.Replace("\\", "/");

            foreach (var item in folder.Contents)
            {
                if (item is SarcFile f)
                {
                    result.Add((path + f.Name, f));
                }
                else if (item is SarcFolder sub)
                {
                    Flatten(sub, path + sub.Name + "/", result);
                }
            }
        }
    }
}
