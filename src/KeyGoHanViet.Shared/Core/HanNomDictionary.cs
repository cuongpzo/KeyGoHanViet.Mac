using System.Diagnostics;
using System.Text;

namespace KeyGoHanViet.Core;

/// <summary>
/// Nạp từ điển Rime (<c>hannomPS.dict_v2.yaml</c>) và dựng các chỉ mục tra cứu.
///
/// Hai định dạng dòng dữ liệu đều nạp được, nhận diện theo từng dòng ở
/// <see cref="ParseTail"/>:
///
///   v1  <c>漢字 \t mã telex \t trọng số \t ghi chú</c>
///   v2  <c>漢字 \t mã telex \t âm đọc Quốc ngữ \t trọng số \t ghi chú</c>
///
/// trong đó cụm nhiều chữ có các âm tiết cách nhau bằng khoảng trắng.
/// </summary>
public sealed class HanNomDictionary
{
    private readonly List<HanEntry> _entries = [];

    private readonly Dictionary<string, List<HanEntry>> _byFull = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HanEntry>> _byBare = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HanEntry>> _byPlain = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HanEntry>> _byAbbrev = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HanEntry>> _byViet = new(StringComparer.Ordinal);

    private string[] _sortedFullKeys = [];
    private string[] _sortedBareKeys = [];
    private string[] _sortedPlainKeys = [];
    private string[] _sortedVietKeys = [];

    private readonly HashSet<string> _fullSyllables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bareSyllables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _plainSyllables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _vietSyllables = new(StringComparer.Ordinal);

    private Dictionary<string, List<HanEntry>>.AlternateLookup<ReadOnlySpan<char>> _fullSpan;
    private Dictionary<string, List<HanEntry>>.AlternateLookup<ReadOnlySpan<char>> _bareSpan;
    private Dictionary<string, List<HanEntry>>.AlternateLookup<ReadOnlySpan<char>> _plainSpan;
    private Dictionary<string, List<HanEntry>>.AlternateLookup<ReadOnlySpan<char>> _vietSpan;

    /// <summary>Độ dài âm tiết dài nhất, dùng để giới hạn vòng lặp khi tách từ.</summary>
    public int MaxSyllableLength { get; private set; } = 1;

    /// <summary>Độ dài khoá tra cứu dài nhất, dùng để giới hạn vòng lặp khi ghép cụm.</summary>
    public int MaxKeyLength { get; private set; } = 1;

    public int EntryCount => _entries.Count;
    public int PhraseCount { get; private set; }
    public int UserEntryCount { get; private set; }
    public long LoadMilliseconds { get; private set; }
    public IReadOnlyList<HanEntry> Entries => _entries;

    public HashSet<string> FullSyllables => _fullSyllables;
    public HashSet<string> BareSyllables => _bareSyllables;
    public HashSet<string> PlainSyllables => _plainSyllables;
    public HashSet<string> VietSyllables => _vietSyllables;

    /// <summary>Số mục có âm đọc Quốc ngữ ghi thẳng trong từ điển (cột thứ ba của bản v2).</summary>
    public int VietEntryCount { get; private set; }

    // ------------------------------------------------------------ nạp dữ liệu

    /// <summary>Số mục lấy thêm từ bảng âm đọc bổ sung (<c>ChinesePhienAmWords.txt</c>).</summary>
    public int SupplementEntryCount { get; private set; }

    /// <summary>
    /// Nạp từ điển chính, (tuỳ chọn) từ điển người dùng và bảng âm đọc bổ sung,
    /// rồi dựng chỉ mục.
    /// </summary>
    public static HanNomDictionary Load(string mainDictPath, string? userDictPath = null,
                                        string? supplementPath = null)
    {
        var sw = Stopwatch.StartNew();
        var dict = new HanNomDictionary();

        dict.LoadFile(mainDictPath, fromUser: false);

        if (!string.IsNullOrWhiteSpace(userDictPath) && File.Exists(userDictPath))
        {
            int before = dict._entries.Count;
            dict.LoadFile(userDictPath, fromUser: true);
            dict.UserEntryCount = dict._entries.Count - before;
        }

        if (!string.IsNullOrWhiteSpace(supplementPath) && File.Exists(supplementPath))
            dict.SupplementEntryCount = dict.LoadSupplement(supplementPath);

        dict.BuildIndexes();
        sw.Stop();
        dict.LoadMilliseconds = sw.ElapsedMilliseconds;
        return dict;
    }

    private void LoadFile(string path, bool fromUser)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Không tìm thấy tệp từ điển: {path}", path);

        bool inData = false;

        foreach (string rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            string line = rawLine.TrimEnd('\r', '\n');

            // Bỏ qua phần đầu YAML cho tới dấu kết thúc "..." hoặc "---".
            if (!inData)
            {
                string t = line.Trim();
                if (t == "..." || t == "---") { inData = t == "..."; continue; }
                if (t.Length == 0 || t.StartsWith('#')) continue;
                // Một số tệp không có dấu "..."; nếu gặp dòng có tab thì coi như đã vào phần dữ liệu.
                if (!line.Contains('\t')) continue;
                inData = true;
            }

            if (line.Length == 0 || line[0] == '#') continue;

            var entry = ParseLine(line, fromUser);
            if (entry is not null) _entries.Add(entry);
        }
    }

    /// <summary>
    /// Nạp bảng âm đọc dạng <c>漢=hán</c> mỗi dòng một chữ (ChinesePhienAmWords.txt).
    ///
    /// Bảng này phủ thêm vài nghìn chữ và âm đọc mà hannomPS chưa có. Chỉ lấy
    /// những gì từ điển chính còn thiếu, và mọi mục lấy thêm đều mang trọng số
    /// 0, không cờ chữ chuẩn - nên luôn xếp sau âm chuẩn của cùng một mã, chỉ
    /// lấp chỗ trống chứ không đảo thứ tự quen thuộc.
    ///
    /// Bỏ qua: chữ không phải chữ Hán (kana, Hangul, ký hiệu, chữ vùng riêng
    /// PUA mà phông thường không hiện được), âm đọc nhiều chữ (đó là lời giải
    /// nghĩa như "đổ xuống" chứ không phải âm), và âm có ký tự lạ ("thi5").
    /// Một ô có nhiều âm cách nhau dấu phẩy ("ơ,o") thì tách thành nhiều mục.
    /// </summary>
    private int LoadSupplement(string path)
    {
        var existing = new HashSet<(string, string)>();
        foreach (var e in _entries) existing.Add((e.Text, e.FullKey));

        int added = 0;
        IEnumerable<string> lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch (IOException) { return 0; }

        foreach (string rawLine in lines)
        {
            int eq = rawLine.IndexOf('=');
            if (eq <= 0) continue;

            string text = rawLine[..eq].Trim().TrimStart('﻿');
            if (!IsSingleHanChar(text)) continue;

            foreach (string part in rawLine[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string reading = part.Trim();
                if (reading.Length == 0 || reading.Contains(' ')) continue;

                string code = TelexCodec.FromVietSyllable(reading);
                if (code.Length == 0 || !existing.Add((text, code))) continue;

                string viet = reading.ToLowerInvariant();
                string bare = TelexCodec.StripTone(code);
                _entries.Add(new HanEntry
                {
                    Text = text,
                    Syllables = [code],
                    Weight = 0,
                    IsStandard = false,
                    Comment = "#bổ sung",
                    FullKey = code,
                    BareKey = bare,
                    PlainKey = TelexCodec.ToPlain(bare),
                    AbbrevKey = code[..1],
                    Viet = viet,
                    VietKey = VietText.NormalizeKey(viet),
                    HasVietColumn = true,
                });
                added++;
            }
        }
        return added;
    }

    /// <summary>true nếu chuỗi là đúng một chữ Hán (kể cả chữ ngoài mặt phẳng cơ bản).</summary>
    private static bool IsSingleHanChar(string s)
    {
        if (s.Length is 0 or > 2) return false;
        if (s.Length == 2 && !char.IsSurrogatePair(s[0], s[1])) return false;

        int cp = char.ConvertToUtf32(s, 0);
        return cp is >= 0x2E80 and <= 0x2FDF      // bộ thủ
            or >= 0x3400 and <= 0x4DBF            // CJK mở rộng A
            or >= 0x4E00 and <= 0x9FFF            // CJK cơ bản
            or >= 0xF900 and <= 0xFAFF            // CJK tương thích
            or >= 0x20000 and <= 0x323AF;         // CJK mở rộng B trở đi (phần lớn chữ Nôm)
    }

    private static HanEntry? ParseLine(string line, bool fromUser)
    {
        string[] cols = line.Split('\t');
        if (cols.Length < 2) return null;

        string text = CleanText(cols[0]);
        string code = cols[1].Trim();
        if (text.Length == 0 || code.Length == 0) return null;

        string[] syllables = code.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (syllables.Length == 0) return null;

        // Mã hợp lệ chỉ gồm a-z; loại bỏ các dòng rác trong tệp gốc.
        foreach (string syl in syllables)
            foreach (char c in syl)
                if (c is < 'a' or > 'z') return null;

        var (viet, weight, comment) = ParseTail(cols);

        var full = new StringBuilder();
        var bare = new StringBuilder();
        var plain = new StringBuilder();
        var abbrev = new StringBuilder(syllables.Length);

        foreach (string syl in syllables)
        {
            string b = TelexCodec.StripTone(syl);
            full.Append(syl);
            bare.Append(b);
            plain.Append(TelexCodec.ToPlain(b));
            abbrev.Append(syl[0]);
        }

        // Dòng có trọng số đúng bằng 10000 là cờ "chữ chuẩn" của hannomPS,
        // không phải tần suất - tách riêng để không che mất tần suất thật.
        bool standard = weight >= StandardMarker;
        int frequency = weight == StandardMarker ? 0 : weight;

        // Từ điển v1 không có cột âm đọc; dựng lại từ mã TELEX để chỉ mục Quốc
        // ngữ vẫn dùng được với cả hai bản. Cột có sẵn thì luôn ưu tiên vì nó
        // là chữ do người biên soạn viết, chính xác hơn phép dựng máy móc.
        string reading = viet.Length > 0 ? viet : TelexCodec.RenderPhrase(syllables);

        return new HanEntry
        {
            Text = text,
            Syllables = syllables,
            Weight = frequency,
            IsStandard = standard,
            Comment = comment,
            FromUserDict = fromUser,
            FullKey = full.ToString(),
            BareKey = bare.ToString(),
            PlainKey = plain.ToString(),
            AbbrevKey = abbrev.ToString(),
            Viet = reading,
            VietKey = VietText.NormalizeKey(reading),
            HasVietColumn = viet.Length > 0,
        };
    }

    /// <summary>
    /// Đọc phần đuôi của dòng, tự nhận ra đang là định dạng nào:
    ///
    ///   v1  漢越辭典摘引 ⇥ hans vieetj… ⇥ 10000 ⇥ #ghi chú
    ///   v2  漢越辭典摘引 ⇥ hans vieetj… ⇥ hán việt… ⇥ 10000 ⇥ #ghi chú
    ///
    /// Phân biệt bằng cột thứ ba: v1 luôn là số, v2 luôn là chữ. Xét theo từng
    /// dòng chứ không theo cả tệp, nhờ vậy từ điển người dùng trộn lẫn hai kiểu
    /// vẫn nạp đúng và không phải thêm tuỳ chọn nào cho người dùng phải nhớ.
    /// </summary>
    private static (string Viet, int Weight, string? Comment) ParseTail(string[] cols)
    {
        if (cols.Length < 3) return ("", 0, null);

        string third = cols[2].Trim();

        // Cột thứ ba rỗng: không có âm đọc, mà cũng không có trọng số.
        if (third.Length == 0) return ("", 0, Comment(cols, 3));

        if (int.TryParse(third, out int w1)) return ("", w1, Comment(cols, 3));

        int weight = 0;
        if (cols.Length >= 4 && int.TryParse(cols[3].Trim(), out int w2)) weight = w2;

        return (third, weight, Comment(cols, 4));
    }

    /// <summary>Ghi chú là cột đầu tiên không rỗng kể từ <paramref name="from"/>.</summary>
    private static string? Comment(string[] cols, int from)
    {
        for (int i = from; i < cols.Length; i++)
        {
            string c = cols[i].Trim();
            if (c.Length > 0) return c;
        }
        return null;
    }

    private const int StandardMarker = 10000;

    /// <summary>
    /// Cột chữ Hán trong tệp gốc có vài dòng lẫn rác ASCII ("Am han viet", "Other",
    /// hay chữ 'z' dính vào đuôi 𡗶𡄰). Loại bỏ mọi ký tự ASCII và khoảng trắng;
    /// dòng nào không còn chữ Hán Nôm nào thì bỏ hẳn.
    /// </summary>
    private static string CleanText(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
            if (!char.IsAscii(c) && !char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    // -------------------------------------------------------------- chỉ mục

    private void BuildIndexes()
    {
        // Gộp trùng: cùng chữ + cùng mã thường xuất hiện nhiều dòng, mỗi dòng mang
        // một mẩu thông tin (một dòng đánh dấu chữ chuẩn, một dòng ghi tần suất).
        // Phải hợp nhất chứ không được bỏ bớt, nếu không sẽ mất tần suất.
        var slotOf = new Dictionary<(string, string), int>(_entries.Count);
        var deduped = new List<HanEntry>(_entries.Count);

        foreach (var e in _entries)
        {
            var key = (e.Text, e.FullKey);
            if (slotOf.TryGetValue(key, out int slot))
            {
                var old = deduped[slot];
                deduped[slot] = new HanEntry
                {
                    Text = old.Text,
                    Syllables = old.Syllables,
                    Weight = Math.Max(old.Weight, e.Weight),
                    IsStandard = old.IsStandard || e.IsStandard,
                    Comment = old.Comment ?? e.Comment,
                    FromUserDict = old.FromUserDict || e.FromUserDict,
                    FullKey = old.FullKey,
                    BareKey = old.BareKey,
                    PlainKey = old.PlainKey,
                    AbbrevKey = old.AbbrevKey,
                    // Giữ âm đọc ghi thẳng trong từ điển, đừng để bản dựng máy
                    // móc của dòng kia đè lên.
                    Viet = old.HasVietColumn ? old.Viet : e.Viet,
                    VietKey = old.HasVietColumn ? old.VietKey : e.VietKey,
                    HasVietColumn = old.HasVietColumn || e.HasVietColumn,
                };
                continue;
            }
            slotOf[key] = deduped.Count;
            deduped.Add(e);
        }

        _entries.Clear();
        _entries.AddRange(deduped);

        foreach (var e in _entries)
        {
            Add(_byFull, e.FullKey, e);
            Add(_byBare, e.BareKey, e);
            if (e.PlainKey != e.BareKey) Add(_byPlain, e.PlainKey, e);
            Add(_byAbbrev, e.AbbrevKey, e);

            // Khoá Quốc ngữ trùng khoá không dấu ở những âm vốn không mang dấu
            // ("an", "am"); khi đó chỉ mục Plain đã lo rồi, thêm vào chỉ tốn chỗ.
            if (e.VietKey.Length > 0 && e.VietKey != e.PlainKey) Add(_byViet, e.VietKey, e);
            if (e.HasVietColumn) VietEntryCount++;

            if (e.Length > 1) PhraseCount++;

            foreach (string syl in e.Syllables)
            {
                _fullSyllables.Add(syl);
                string b = TelexCodec.StripTone(syl);
                _bareSyllables.Add(b);
                _plainSyllables.Add(TelexCodec.ToPlain(b));
                if (syl.Length > MaxSyllableLength) MaxSyllableLength = syl.Length;
            }

            foreach (string word in e.Viet.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string w = VietText.NormalizeKey(word);
                if (w.Length > 0) _vietSyllables.Add(w);
            }
        }

        // Sắp xếp danh sách ứng viên trong mỗi khoá: trọng số giảm dần, ưu tiên từ điển người dùng.
        foreach (var bucket in _byFull.Values) SortBucket(bucket);
        foreach (var bucket in _byBare.Values) SortBucket(bucket);
        foreach (var bucket in _byPlain.Values) SortBucket(bucket);
        foreach (var bucket in _byAbbrev.Values) SortBucket(bucket);
        foreach (var bucket in _byViet.Values) SortBucket(bucket);

        _sortedFullKeys = SortedKeys(_byFull);
        _sortedBareKeys = SortedKeys(_byBare);
        _sortedPlainKeys = SortedKeys(_byPlain);
        _sortedVietKeys = SortedKeys(_byViet);

        foreach (string k in _sortedFullKeys) if (k.Length > MaxKeyLength) MaxKeyLength = k.Length;
        foreach (string k in _sortedBareKeys) if (k.Length > MaxKeyLength) MaxKeyLength = k.Length;
        foreach (string k in _sortedPlainKeys) if (k.Length > MaxKeyLength) MaxKeyLength = k.Length;
        foreach (string k in _sortedVietKeys) if (k.Length > MaxKeyLength) MaxKeyLength = k.Length;

        _fullSpan = _byFull.GetAlternateLookup<ReadOnlySpan<char>>();
        _bareSpan = _byBare.GetAlternateLookup<ReadOnlySpan<char>>();
        _plainSpan = _byPlain.GetAlternateLookup<ReadOnlySpan<char>>();
        _vietSpan = _byViet.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    private static void SortBucket(List<HanEntry> bucket) =>
        bucket.Sort(static (a, b) =>
        {
            if (a.FromUserDict != b.FromUserDict) return a.FromUserDict ? -1 : 1;
            int c = b.Rank.CompareTo(a.Rank);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Text, b.Text);
        });

    private static string[] SortedKeys(Dictionary<string, List<HanEntry>> map)
    {
        var keys = new string[map.Count];
        map.Keys.CopyTo(keys, 0);
        Array.Sort(keys, StringComparer.Ordinal);
        return keys;
    }

    private static void Add(Dictionary<string, List<HanEntry>> map, string key, HanEntry entry)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(entry);
    }

    // -------------------------------------------------------------- tra cứu

    public IReadOnlyList<HanEntry> LookupFull(string key) => Get(_byFull, key);
    public IReadOnlyList<HanEntry> LookupBare(string key) => Get(_byBare, key);
    public IReadOnlyList<HanEntry> LookupPlain(string key) => Get(_byPlain, key);
    public IReadOnlyList<HanEntry> LookupAbbrev(string key) => Get(_byAbbrev, key);

    /// <summary>Tra theo âm đọc Quốc ngữ. <paramref name="key"/> phải đã qua <see cref="VietText.NormalizeKey"/>.</summary>
    public IReadOnlyList<HanEntry> LookupViet(string key) => Get(_byViet, key);

    private static IReadOnlyList<HanEntry> Get(Dictionary<string, List<HanEntry>> map, string key) =>
        map.TryGetValue(key, out var list) ? list : Array.Empty<HanEntry>();

    /// <summary>
    /// Tra cứu theo lát cắt chuỗi, không cấp phát chuỗi tạm. Dùng trong vòng lặp
    /// quy hoạch động của bộ ghép cụm nên rất nhạy về hiệu năng.
    /// </summary>
    public List<HanEntry>? LookupSpan(MatchMode mode, ReadOnlySpan<char> key)
    {
        var lookup = mode switch
        {
            MatchMode.Full => _fullSpan,
            MatchMode.Bare => _bareSpan,
            MatchMode.Plain => _plainSpan,
            MatchMode.Viet => _vietSpan,
            _ => _fullSpan,
        };
        return lookup.TryGetValue(key, out var list) ? list : null;
    }

    /// <summary>true nếu tồn tại ít nhất một mục có khoá bắt đầu bằng <paramref name="prefix"/>.</summary>
    public bool HasPrefix(MatchMode mode, string prefix)
    {
        string[] keys = KeysFor(mode);
        int i = LowerBound(keys, prefix);
        return i < keys.Length && keys[i].StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Liệt kê các khoá bắt đầu bằng <paramref name="prefix"/> (dùng cho gợi ý khi đang gõ dở).
    /// </summary>
    public IEnumerable<KeyValuePair<string, List<HanEntry>>> EnumeratePrefix(MatchMode mode, string prefix, int maxKeys)
    {
        string[] keys = KeysFor(mode);
        var map = MapFor(mode);

        int i = LowerBound(keys, prefix);
        int produced = 0;

        for (; i < keys.Length && produced < maxKeys; i++)
        {
            string k = keys[i];
            if (!k.StartsWith(prefix, StringComparison.Ordinal)) yield break;
            if (k.Length == prefix.Length) continue;   // khớp chính xác đã xử lý riêng
            yield return new KeyValuePair<string, List<HanEntry>>(k, map[k]);
            produced++;
        }
    }

    private string[] KeysFor(MatchMode mode) => mode switch
    {
        MatchMode.Full => _sortedFullKeys,
        MatchMode.Bare => _sortedBareKeys,
        MatchMode.Plain => _sortedPlainKeys,
        MatchMode.Viet => _sortedVietKeys,
        _ => _sortedFullKeys,
    };

    private Dictionary<string, List<HanEntry>> MapFor(MatchMode mode) => mode switch
    {
        MatchMode.Full => _byFull,
        MatchMode.Bare => _byBare,
        MatchMode.Plain => _byPlain,
        MatchMode.Abbrev => _byAbbrev,
        MatchMode.Viet => _byViet,
        _ => _byFull,
    };

    /// <summary>Chỉ số phần tử đầu tiên >= <paramref name="value"/>.</summary>
    private static int LowerBound(string[] sorted, string value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (string.CompareOrdinal(sorted[mid], value) < 0) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Tra ngược: tìm các mã TELEX của một chữ Hán.</summary>
    public IEnumerable<HanEntry> ReverseLookup(string hanText) =>
        _entries.Where(e => e.Text == hanText).OrderByDescending(e => e.Weight);
}

/// <summary>Kiểu khớp khoá tra cứu.</summary>
public enum MatchMode
{
    /// <summary>Gõ đủ dấu thanh: vieetjnamz</summary>
    Full,
    /// <summary>Bỏ thanh điệu, giữ dấu mũ/móc: vieetnam</summary>
    Bare,
    /// <summary>Hoàn toàn không dấu: vietnam</summary>
    Plain,
    /// <summary>Viết tắt chữ cái đầu: vn</summary>
    Abbrev,
    /// <summary>Âm đọc Quốc ngữ có dấu, đã chuẩn hoá: việtnam</summary>
    Viet,
}
