using PKHeX.Core;
using System.Buffers.Binary;

/// <summary>
/// Makes files in each game's real layout. PKHeX's blank saves are meant to be filled in memory, so what
/// a console writes around the data (GBA sectors, DS footers, the 3DS footer mark) is added here.
/// </summary>
static class TestSaves
{
    const int Gen3Size = 0x20000, Gen3Sector = 0x1000, Gen3Sectors = 14, Gen3Signature = 0x08012025;
    const int Gen4Size = 0x80000, Gen4Partition = 0x40000;
    const uint Gen4Magic = 0x20060623, FooterBeef = 0x42454546;
    const int BeefOffset = 0x1F0;

    public static SaveFile Start(GameVersion version) => version switch {
        GameVersion.R or GameVersion.S or GameVersion.E or GameVersion.FR or GameVersion.LG => Named(SaveUtil.GetSaveFile(Gen3Image(version))!),
        GameVersion.D or GameVersion.P or GameVersion.Pt or GameVersion.HG or GameVersion.SS => Named(SaveUtil.GetSaveFile(Gen4Image(version))!),
        _ => Typed(BlankSaveFile.Get(version, "TRAINER", LanguageID.Spanish)),
    };

    // A hand-built image starts with no trainer, unlike a played save.
    static SaveFile Named(SaveFile sav)
    {
        sav.OT = "TRAINER";
        sav.Language = (int)LanguageID.Spanish;
        return sav;
    }

    public static byte[] Finish(SaveFile sav)
    {
        var bytes = sav.Write().ToArray();
        if (sav.Generation is 6 or 7) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - BeefOffset), FooterBeef);
        if (sav is ISCBlockArray or SAV7b) BlockReader.Register(bytes.Length, sav.GetType());
        return bytes;
    }

    // Gen 8+ blanks have untyped blocks, which can't be written out; a real save's are all typed.
    static SaveFile Typed(SaveFile sav)
    {
        if (sav is not ISCBlockArray array) return sav;
        var type = typeof(SCBlock).GetProperty(nameof(SCBlock.Type))!;
        foreach (var block in array.AllBlocks.Where(b => b.Type == SCTypeCode.None))
            type.SetValue(block, block.Data.Length switch { 0 => SCTypeCode.Bool1, 4 => SCTypeCode.UInt32, _ => SCTypeCode.Object });
        return sav;
    }

    // Detection knows the exact sizes of retail Gen 8+ saves; a test file is smaller, so its size is told here.
    sealed class BlockReader : ISaveReader
    {
        static readonly Dictionary<long, Type> known = [];
        static BlockReader() => SaveUtil.CustomSaveReaders.Add(new BlockReader());
        public static void Register(long size, Type type) => known[size] = type;
        public bool IsRecognized(long size) => known.ContainsKey(size);
        public bool TryRead(Memory<byte> data, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SaveFile? save, string? path = null)
        {
            save = known.TryGetValue(data.Length, out var type) ? (SaveFile)Activator.CreateInstance(type, data)! : null;
            return save != null;
        }
    }

    static byte[] Gen3Image(GameVersion version)
    {
        var data = new byte[Gen3Size];
        for (short id = 0; id < Gen3Sectors; id++)
        {
            var sector = data.AsSpan(id * Gen3Sector);
            BinaryPrimitives.WriteInt16LittleEndian(sector[0xFF4..], id);
            BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFF8..], Gen3Signature);
            BinaryPrimitives.WriteUInt32LittleEndian(sector[0xFFC..], 1);
        }
        if (version is GameVersion.FR or GameVersion.LG) data[0xAC] = 1;
        else if (version == GameVersion.E)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xAC), 0x3C1D5E2F);
            data[0x900] = 1;
        }
        return data;
    }

    static byte[] Gen4Image(GameVersion version)
    {
        var (general, storage, storageStart) = version switch {
            GameVersion.D or GameVersion.P => (0xC100, 0x121E0, 0xC100),
            GameVersion.Pt => (0xCF2C, 0x121E4, 0xCF2C),
            _ => (0xF628, 0x12310, 0xF700),
        };
        var data = new byte[Gen4Size];
        void Footer(int end, int size, uint revision)
        {
            var span = data.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span[(end - 0x14)..], revision);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(end - 0x10)..], revision);
            BinaryPrimitives.WriteInt32LittleEndian(span[(end - 0xC)..], size);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(end - 0x8)..], Gen4Magic);
        }
        foreach (var (partition, revision) in new[] { (0, 2u), (Gen4Partition, 1u) })
        {
            Footer(partition + general, general, revision);
            Footer(partition + storageStart + storage, storage, revision);
        }
        return data;
    }
}
