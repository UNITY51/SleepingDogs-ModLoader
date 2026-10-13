using System.Text;

namespace SDModLoader.Core;

/// <summary>Запись центрального каталога zip (нужна для методов, которые не умеет System.IO.Compression).</summary>
internal sealed class RawZipEntry
{
    public string Name = "";
    public ushort Flags, Method;
    public uint Crc;
    public long CompressedSize, Size, LocalOffset;
}

/// <summary>
/// Распаковка zip-записей, сжатых LZMA (метод 14) — так упакован, например, релиз FileRedirector, а также
/// архивы 7-Zip в режиме «zip + LZMA». Встроенный ZipArchive такие записи не читает.
/// Декодер LZMA — компактная реализация по спецификации (формат такой же, как в LZMA SDK).
/// </summary>
internal static class LzmaZip
{
    public const ushort MethodLzma = 14;

    // ---------- чтение центрального каталога ----------
    public static List<RawZipEntry> ReadCentralDirectory(Stream s)
    {
        var len = s.Length;
        var tailLen = (int)Math.Min(len, 22 + 65535 + 64);
        var tail = new byte[tailLen];
        s.Position = len - tailLen; ReadExactly(s, tail, tailLen);
        var eocd = -1;
        for (var i = tailLen - 22; i >= 0; i--)
            if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 5 && tail[i + 3] == 6) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("ZIP: end of central directory not found.");

        long total = BitConverter.ToUInt16(tail, eocd + 10);
        long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
        if ((total == 0xFFFF || cdOffset == 0xFFFFFFFF) && eocd >= 20 &&
            tail[eocd - 20] == 0x50 && tail[eocd - 19] == 0x4b && tail[eocd - 18] == 6 && tail[eocd - 17] == 7)
        {
            var off64 = BitConverter.ToInt64(tail, eocd - 20 + 8);
            var h = new byte[56];
            s.Position = off64; ReadExactly(s, h, 56);
            total = BitConverter.ToInt64(h, 32);
            cdOffset = BitConverter.ToInt64(h, 48);
        }

        var list = new List<RawZipEntry>();
        s.Position = cdOffset;
        var hdr = new byte[46];
        for (long i = 0; i < total; i++)
        {
            ReadExactly(s, hdr, 46);
            if (BitConverter.ToUInt32(hdr, 0) != 0x02014b50) throw new InvalidDataException("ZIP: bad central directory.");
            var e = new RawZipEntry
            {
                Flags = BitConverter.ToUInt16(hdr, 8), Method = BitConverter.ToUInt16(hdr, 10), Crc = BitConverter.ToUInt32(hdr, 16),
                CompressedSize = BitConverter.ToUInt32(hdr, 20), Size = BitConverter.ToUInt32(hdr, 24), LocalOffset = BitConverter.ToUInt32(hdr, 42),
            };
            int nameLen = BitConverter.ToUInt16(hdr, 28), extraLen = BitConverter.ToUInt16(hdr, 30), commentLen = BitConverter.ToUInt16(hdr, 32);
            var name = new byte[nameLen]; ReadExactly(s, name, nameLen);
            var extra = new byte[extraLen]; ReadExactly(s, extra, extraLen);
            s.Seek(commentLen, SeekOrigin.Current);
            e.Name = ((e.Flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.Latin1).GetString(name);

            // zip64: поля, равные 0xFFFFFFFF, лежат в extra (id 0x0001) в порядке: размер, сжатый размер, смещение
            for (int p = 0; p + 4 <= extra.Length;)
            {
                int id = BitConverter.ToUInt16(extra, p), sz = BitConverter.ToUInt16(extra, p + 2), q = p + 4;
                if (id == 1)
                {
                    if (e.Size == 0xFFFFFFFF && q + 8 <= p + 4 + sz) { e.Size = BitConverter.ToInt64(extra, q); q += 8; }
                    if (e.CompressedSize == 0xFFFFFFFF && q + 8 <= p + 4 + sz) { e.CompressedSize = BitConverter.ToInt64(extra, q); q += 8; }
                    if (e.LocalOffset == 0xFFFFFFFF && q + 8 <= p + 4 + sz) { e.LocalOffset = BitConverter.ToInt64(extra, q); }
                }
                p += 4 + sz;
            }
            list.Add(e);
        }
        return list;
    }

    static void ReadExactly(Stream s, byte[] buf, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = s.Read(buf, read, count - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
    }

    // ---------- распаковка записи ----------
    public static void Extract(Stream zip, RawZipEntry e, Stream output)
    {
        if (e.Method != MethodLzma) throw new NotSupportedException($"ZIP method {e.Method} is not supported.");
        if ((e.Flags & 1) != 0) throw new NotSupportedException("Encrypted ZIP entries are not supported.");

        var lh = new byte[30];
        zip.Position = e.LocalOffset; ReadExactly(zip, lh, 30);
        if (BitConverter.ToUInt32(lh, 0) != 0x04034b50) throw new InvalidDataException("ZIP: bad local header.");
        var dataStart = e.LocalOffset + 30 + BitConverter.ToUInt16(lh, 26) + BitConverter.ToUInt16(lh, 28);

        zip.Position = dataStart;
        var input = new ByteSource(zip, e.CompressedSize);
        input.ReadByte(); input.ReadByte();                       // версия LZMA SDK
        int propsLen = input.ReadByte() | (input.ReadByte() << 8);
        if (propsLen < 5) throw new InvalidDataException("LZMA: bad header.");
        var props = new byte[propsLen];
        for (var i = 0; i < propsLen; i++) props[i] = (byte)input.ReadByte();

        var crc = Crc32.Begin();
        new LzmaDecoder(props).Decode(input, output, e.Size, ref crc);
        if (Crc32.End(crc) != e.Crc) throw new InvalidDataException($"CRC mismatch for {e.Name}.");
    }

    /// <summary>Буферизованное чтение ограниченного участка потока.</summary>
    sealed class ByteSource
    {
        readonly Stream _s; long _left; readonly byte[] _buf = new byte[1 << 16]; int _pos, _len;
        public ByteSource(Stream s, long count) { _s = s; _left = count; }
        public int ReadByte()
        {
            if (_pos == _len)
            {
                if (_left <= 0) return 0; // допускаем чтение за концом: декодер доберёт нули, CRC всё проверит
                _len = _s.Read(_buf, 0, (int)Math.Min(_buf.Length, _left));
                _pos = 0;
                if (_len <= 0) throw new EndOfStreamException();
                _left -= _len;
            }
            return _buf[_pos++];
        }
    }

    // ---------- CRC32 ----------
    static class Crc32
    {
        static readonly uint[] Table = Make();
        static uint[] Make()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++) { var c = i; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[i] = c; }
            return t;
        }
        public static uint Begin() => 0xFFFFFFFF;
        public static uint Update(uint crc, byte[] b, int off, int count)
        {
            for (var i = off; i < off + count; i++) crc = Table[(crc ^ b[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }
        public static uint End(uint crc) => ~crc;
    }

    // ---------- LZMA ----------
    sealed class LzmaDecoder
    {
        const int NumStates = 12, PosStatesMax = 16, EndPosModelIndex = 14, NumFullDistances = 128, AlignBits = 4;
        readonly int _lc, _lp, _pb;
        readonly uint _dictSize;

        // вероятности
        readonly ushort[] _isMatch = new ushort[NumStates << 4], _isRep = new ushort[NumStates], _isRepG0 = new ushort[NumStates],
            _isRepG1 = new ushort[NumStates], _isRepG2 = new ushort[NumStates], _isRep0Long = new ushort[NumStates << 4];
        readonly ushort[] _posSlot = new ushort[4 << 6], _posSpecial = new ushort[1 + NumFullDistances - EndPosModelIndex], _align = new ushort[1 << AlignBits];
        readonly ushort[] _literal;
        readonly LenDecoder _len = new(), _repLen = new();

        // range decoder
        uint _range, _code;
        ByteSource _in = null!;

        public LzmaDecoder(byte[] props)
        {
            var d = props[0];
            if (d >= 9 * 5 * 5) throw new InvalidDataException("LZMA: bad properties.");
            _lc = d % 9; d = (byte)(d / 9); _lp = d % 5; _pb = d / 5;
            _dictSize = BitConverter.ToUInt32(props, 1);
            _literal = new ushort[0x300 << (_lc + _lp)];
        }

        static void Init(ushort[] a) => Array.Fill(a, (ushort)1024);

        uint Bit(ushort[] probs, int i)
        {
            uint p = probs[i], bound = (_range >> 11) * p, bit;
            if (_code < bound) { _range = bound; probs[i] = (ushort)(p + ((2048 - p) >> 5)); bit = 0; }
            else { _range -= bound; _code -= bound; probs[i] = (ushort)(p - (p >> 5)); bit = 1; }
            if (_range < (1u << 24)) { _range <<= 8; _code = (_code << 8) | (uint)_in.ReadByte(); }
            return bit;
        }

        uint Tree(ushort[] probs, int off, int bits)
        {
            uint m = 1;
            for (var i = 0; i < bits; i++) m = (m << 1) + Bit(probs, off + (int)m);
            return m - (1u << bits);
        }

        uint ReverseTree(ushort[] probs, int off, int bits)
        {
            uint m = 1, sym = 0;
            for (var i = 0; i < bits; i++) { var b = Bit(probs, off + (int)m); m = (m << 1) + b; sym |= b << i; }
            return sym;
        }

        uint Direct(int bits)
        {
            uint result = 0;
            for (var i = bits; i > 0; i--)
            {
                _range >>= 1; _code -= _range;
                var t = 0 - (_code >> 31);
                _code += _range & t;
                result = (result << 1) + (t + 1);
                if (_range < (1u << 24)) { _range <<= 8; _code = (_code << 8) | (uint)_in.ReadByte(); }
            }
            return result;
        }

        sealed class LenDecoder
        {
            public readonly ushort[] Choice = new ushort[2], Low = new ushort[PosStatesMax << 3], Mid = new ushort[PosStatesMax << 3], High = new ushort[256];
            public void Init() { LzmaDecoder.Init(Choice); LzmaDecoder.Init(Low); LzmaDecoder.Init(Mid); LzmaDecoder.Init(High); }
        }

        uint DecodeLen(LenDecoder l, int posState)
        {
            if (Bit(l.Choice, 0) == 0) return Tree(l.Low, posState << 3, 3);
            if (Bit(l.Choice, 1) == 0) return 8 + Tree(l.Mid, posState << 3, 3);
            return 16 + Tree(l.High, 0, 8);
        }

        public void Decode(ByteSource input, Stream output, long outSize, ref uint crc)
        {
            _in = input;
            var crcLocal = crc; // ref-параметр нельзя захватывать локальными функциями
            foreach (var a in new[] { _isMatch, _isRep, _isRepG0, _isRepG1, _isRepG2, _isRep0Long, _posSlot, _posSpecial, _align, _literal }) Init(a);
            _len.Init(); _repLen.Init();

            _range = 0xFFFFFFFF; _code = 0;
            input.ReadByte();
            for (var i = 0; i < 4; i++) _code = (_code << 8) | (uint)input.ReadByte();

            var winSize = (int)Math.Min(Math.Max(_dictSize, 4096u), (uint)Math.Max(Math.Min(outSize, int.MaxValue / 2), 4096));
            var win = new byte[winSize];
            int wpos = 0, flushed = 0;
            long total = 0;
            var posMask = (1 << _pb) - 1; var litMask = (1 << _lp) - 1;
            uint state = 0, rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;

            void Flush()
            {
                if (wpos > flushed)
                {
                    output.Write(win, flushed, wpos - flushed);
                    crcLocal = Crc32.Update(crcLocal, win, flushed, wpos - flushed);
                }
                if (wpos >= winSize) wpos = 0;
                flushed = wpos;
            }
            void Put(byte b) { win[wpos++] = b; total++; if (wpos >= winSize) Flush(); }
            byte Get(uint dist) { var p = wpos - (int)dist - 1; if (p < 0) p += winSize; return win[p]; }

            while (total < outSize)
            {
                var posState = (int)total & posMask;
                if (Bit(_isMatch, ((int)state << 4) + posState) == 0)
                {
                    uint prev = total == 0 ? 0u : Get(0);
                    var off = 0x300 * ((((int)total & litMask) << _lc) + (int)(prev >> (8 - _lc)));
                    uint sym = 1;
                    if (state >= 7)
                    {
                        uint match = Get(rep0);
                        do
                        {
                            var mb = (match >> 7) & 1; match <<= 1;
                            var b = Bit(_literal, off + (int)(((1 + mb) << 8) + sym));
                            sym = (sym << 1) | b;
                            if (mb != b) break;
                        } while (sym < 0x100);
                    }
                    while (sym < 0x100) sym = (sym << 1) | Bit(_literal, off + (int)sym);
                    Put((byte)sym);
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    continue;
                }

                uint len;
                if (Bit(_isRep, (int)state) == 1)
                {
                    if (total == 0) throw new InvalidDataException("LZMA: corrupted data.");
                    if (Bit(_isRepG0, (int)state) == 0)
                    {
                        if (Bit(_isRep0Long, ((int)state << 4) + posState) == 0)
                        {
                            state = state < 7 ? 9u : 11u;
                            Put(Get(rep0));
                            continue;
                        }
                    }
                    else
                    {
                        uint dist;
                        if (Bit(_isRepG1, (int)state) == 0) dist = rep1;
                        else
                        {
                            if (Bit(_isRepG2, (int)state) == 0) dist = rep2; else { dist = rep3; rep3 = rep2; }
                            rep2 = rep1;
                        }
                        rep1 = rep0; rep0 = dist;
                    }
                    len = DecodeLen(_repLen, posState);
                    state = state < 7 ? 8u : 11u;
                }
                else
                {
                    rep3 = rep2; rep2 = rep1; rep1 = rep0;
                    len = DecodeLen(_len, posState);
                    state = state < 7 ? 7u : 10u;

                    var lenState = (int)(len < 4 ? len : 3);
                    var slot = Tree(_posSlot, lenState << 6, 6);
                    if (slot < 4) rep0 = slot;
                    else
                    {
                        var numDirect = (int)(slot >> 1) - 1;
                        rep0 = (2 | (slot & 1)) << numDirect;
                        if (slot < EndPosModelIndex) rep0 += ReverseTree(_posSpecial, (int)(rep0 - slot), numDirect);
                        else
                        {
                            rep0 += Direct(numDirect - AlignBits) << AlignBits;
                            rep0 += ReverseTree(_align, 0, AlignBits);
                        }
                    }
                    if (rep0 == 0xFFFFFFFF) break; // маркер конца потока
                }

                if (rep0 >= total || rep0 >= winSize) throw new InvalidDataException("LZMA: corrupted data.");
                var copy = (long)len + 2;
                if (copy > outSize - total) copy = outSize - total;
                var src = wpos - (int)rep0 - 1; if (src < 0) src += winSize;
                for (; copy > 0; copy--) { if (src >= winSize) src = 0; Put(win[src++]); }
            }
            Flush();
            crc = crcLocal;
            if (total != outSize) throw new InvalidDataException("LZMA: unexpected end of stream.");
        }
    }
}
