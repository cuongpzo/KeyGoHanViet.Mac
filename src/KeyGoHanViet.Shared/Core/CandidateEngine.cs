using System.Text;

namespace KeyGoHanViet.Core;

/// <summary>
/// Bộ sinh gợi ý: nhận chuỗi Latin thô đang gõ và trả về danh sách chữ Hán Nôm
/// đã xếp hạng. Hỗ trợ đồng thời bốn kiểu nhập liệu của đặc tả:
/// gõ đủ dấu TELEX, gõ bỏ thanh, gõ không dấu, và gõ tắt.
/// </summary>
public sealed class CandidateEngine
{
    private readonly HanNomDictionary _dict;
    private readonly MacroStore _macros;
    private readonly UsageMemory _usage;
    private readonly AppSettings _settings;

    private readonly Segmenter _fullSeg;
    private readonly Segmenter _bareSeg;
    private readonly Segmenter _plainSeg;
    private readonly FreeOrderTelex _freeOrder;

    public CandidateEngine(HanNomDictionary dict, MacroStore macros, UsageMemory usage, AppSettings settings)
    {
        _dict = dict;
        _macros = macros;
        _usage = usage;
        _settings = settings;

        _fullSeg = new Segmenter(dict.FullSyllables, dict.MaxSyllableLength);
        _bareSeg = new Segmenter(dict.BareSyllables, dict.MaxSyllableLength);
        _plainSeg = new Segmenter(dict.PlainSyllables, dict.MaxSyllableLength);
        _freeOrder = new FreeOrderTelex(dict);
    }

    public HanNomDictionary Dictionary => _dict;
    public MacroStore Macros => _macros;
    public UsageMemory Usage => _usage;
    public FreeOrderTelex FreeOrder => _freeOrder;

    /// <summary>
    /// Mã TELEX chuẩn của chuỗi gõ tự do ("dofod" -> "ddoof"), hoặc null nếu
    /// chuỗi đã chuẩn sẵn hay tính năng đang tắt.
    /// </summary>
    public string? CanonicalTelex(string sanitized)
    {
        if (!_settings.EnableFreeOrderTelex || sanitized.Length == 0) return null;

        // Đã là một khoá có thật trong từ điển thì người dùng gõ đúng chuẩn rồi,
        // không đoán thêm cách hiểu nào khác.
        if (_dict.LookupFull(sanitized).Count > 0 || _dict.LookupBare(sanitized).Count > 0 ||
            _dict.LookupPlain(sanitized).Count > 0)
            return null;

        return _freeOrder.Canonicalize(sanitized);
    }

    // ------------------------------------------------------------------ API

    /// <summary>
    /// Sinh danh sách gợi ý cho chuỗi đang gõ.
    ///
    /// Chuỗi vào có hai dạng và được tách nhánh ngay từ đây:
    ///
    ///   TELEX thô     "hans vieetj tuwf ddieenr trichs daanx"
    ///   Quốc ngữ      "hán việt từ điển trích dẫn"
    ///
    /// Dạng thứ hai đến từ việc dán clipboard, chuyển đoạn văn bản đang bôi đen,
    /// hoặc gõ qua một bộ gõ Việt khác đang chạy sẵn. Cả hai đều phải cho ra
    /// 漢越辭典摘引.
    /// </summary>
    public List<Candidate> GetCandidates(string rawInput)
    {
        if (_settings.EnableVietInput && VietText.ContainsDiacritics(rawInput))
            return GetCandidatesFromViet(rawInput);

        return GetCandidatesFromTelex(TelexCodec.SanitizeInput(rawInput));
    }

    /// <summary>
    /// Nhánh TELEX. Tra nguyên văn trước như xưa nay; nếu chuỗi gõ không theo
    /// thứ tự chuẩn ("dofod") thì tra thêm bản đã sắp lại ("ddoof") rồi gộp.
    ///
    /// Bên nào đứng trước tuỳ nguyên văn có tra ra chữ khớp chính xác không:
    /// có thì người dùng gõ đúng chuẩn, bản sắp lại chỉ là dự phòng; không thì
    /// ngược lại - nguyên văn "ddofo" chỉ ghép vụn được "đò + o" nên nhường chỗ
    /// cho 圖 của "ddoof".
    /// </summary>
    private List<Candidate> GetCandidatesFromTelex(string input)
    {
        var result = new List<Candidate>();
        if (input.Length == 0) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectTelex(input, result, seen);

        string? canonical = CanonicalTelex(input);
        if (canonical is null) return Finish(input, result);

        bool rawHasExact = result.Exists(c => c.Tier <= CandidateTier.ExactPlain);
        int split = result.Count;

        CollectTelex(canonical, result, seen);

        for (int i = 0; i < result.Count; i++)
            result[i].Fallback = rawHasExact ? i >= split : i < split;

        // Lịch sử chọn chữ ghi theo mã chuẩn, để mọi thứ tự gõ dùng chung.
        return Finish(canonical, result);
    }

    private void CollectTelex(string input, List<Candidate> result, HashSet<string> seen)
    {
        AddMacros(input, result, seen);
        AddExact(input, MatchMode.Full, CandidateTier.ExactFull, result, seen);
        AddExact(input, MatchMode.Bare, CandidateTier.ExactBare, result, seen);

        if (_settings.EnablePlainInput)
            AddExact(input, MatchMode.Plain, CandidateTier.ExactPlain, result, seen);

        if (_settings.EnableComposite)
            AddComposite(input, TelexCompositeModes(), result, seen);

        if (_settings.EnableAbbrevFromDictionary)
            AddExact(input, MatchMode.Abbrev, CandidateTier.Abbrev, result, seen);

        if (_settings.EnablePrefixSuggestions && input.Length >= _settings.MinPrefixLength)
            AddPrefix(input, [MatchMode.Full, MatchMode.Bare], result, seen);
    }

    /// <summary>
    /// Nhánh tra theo âm đọc Quốc ngữ có dấu.
    ///
    /// Chạy song song hai đường rồi gộp lại, vì không phải mục từ nào cũng có
    /// cột âm đọc:
    ///
    ///   1. Khoá Quốc ngữ đã chuẩn hoá  "hánviệttừđiểntríchdẫn"  → chỉ mục Viet
    ///   2. Cùng chuỗi đó nhưng bỏ dấu  "hanviettudientrichdan"  → chỉ mục Plain
    ///
    /// Đường 2 rộng hơn nên cũng lắm chữ đồng âm hơn; nhờ xếp hạng theo bậc mà
    /// kết quả khớp đúng dấu vẫn luôn đứng trước.
    /// </summary>
    private List<Candidate> GetCandidatesFromViet(string rawInput)
    {
        var result = new List<Candidate>();

        string vietKey = VietText.NormalizeKey(rawInput);
        if (vietKey.Length == 0) return result;

        string plainKey = VietText.ToPlainKey(rawInput);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddExact(vietKey, MatchMode.Viet, CandidateTier.ExactViet, result, seen);
        AddExact(plainKey, MatchMode.Plain, CandidateTier.ExactPlain, result, seen);

        // Chuỗi bỏ dấu trùng khoá Bare ở những âm không có mũ/móc ("an", "tam"),
        // nên vẫn phải hỏi cả chỉ mục đó mới đủ.
        AddExact(plainKey, MatchMode.Bare, CandidateTier.ExactBare, result, seen);

        if (_settings.EnableComposite)
        {
            AddComposite(vietKey, [MatchMode.Viet], result, seen);
            AddComposite(plainKey, [MatchMode.Plain, MatchMode.Bare], result, seen);
        }

        if (_settings.EnablePrefixSuggestions && vietKey.Length >= _settings.MinPrefixLength)
            AddPrefix(vietKey, [MatchMode.Viet], result, seen);

        return Finish(vietKey, result);
    }

    /// <summary>
    /// Xếp hạng rồi cắt bớt cho vừa kho ứng viên.
    ///
    /// Cắt theo <see cref="AppSettings.CandidatePoolSize"/> chứ không theo số
    /// dòng của khung gợi ý: khung chỉ hiện một trang, còn phần sau vẫn phải còn
    /// đó để người dùng lật tiếp bằng phím Tab. Cắt ở đây theo cỡ trang thì
    /// những chữ đồng âm hiếm - đúng thứ mà người ta phải lật đi tìm - bị vứt
    /// ngay từ trong bộ máy.
    /// </summary>
    private List<Candidate> Finish(string usageKey, List<Candidate> result)
    {
        Rank(usageKey, result);

        int max = PoolSize;
        if (result.Count > max) result.RemoveRange(max, result.Count - max);
        return result;
    }

    /// <summary>Số ứng viên tối đa giữ lại cho một chuỗi gõ, không dưới một trang.</summary>
    private int PoolSize => Math.Max(Math.Max(1, _settings.MaxCandidates), _settings.CandidatePoolSize);

    /// <summary>
    /// Khoá ghi nhớ lựa chọn. Phải khớp với khoá mà <see cref="GetCandidates"/>
    /// dùng để xếp hạng, nếu không thì chọn xong lần sau vẫn không lên đầu.
    /// </summary>
    private string UsageKey(string rawInput)
    {
        if (_settings.EnableVietInput && VietText.ContainsDiacritics(rawInput))
            return VietText.NormalizeKey(rawInput);

        string telex = TelexCodec.SanitizeInput(rawInput);
        return CanonicalTelex(telex) ?? telex;
    }

    /// <summary>
    /// Chuỗi Quốc ngữ có dấu hiển thị trên vùng preedit.
    /// Ưu tiên âm đọc thật của ứng viên đang chọn (gõ "vietnam" vẫn hiện "việt nam"),
    /// chỉ khi không có ứng viên nào mới dựng thẳng từ chuỗi TELEX.
    /// </summary>
    public string RenderPreedit(string rawInput, Candidate? selected = null)
    {
        if (selected is { Reading.Length: > 0 }) return selected.Reading;

        // Chuỗi vào đã là Quốc ngữ rồi thì không dựng lại gì cả, hiện nguyên văn.
        if (VietText.ContainsDiacritics(rawInput)) return rawInput;

        string input = TelexCodec.SanitizeInput(rawInput);
        if (input.Length == 0) return rawInput;

        return RenderRaw(CanonicalTelex(input) ?? input);
    }

    /// <summary>Dựng Quốc ngữ trực tiếp từ chuỗi TELEX, không dựa vào từ điển.</summary>
    public string RenderRaw(string input)
    {
        var seg = _fullSeg.Segment(input) ?? _bareSeg.Segment(input) ?? _plainSeg.Segment(input);
        if (seg is not null)
            return TelexCodec.RenderPhrase(seg, _settings.ModernTonePlacement);

        var (head, rest) = _fullSeg.SegmentPartial(input);
        if (head.Length > 0)
        {
            string rendered = TelexCodec.RenderPhrase(head, _settings.ModernTonePlacement);
            return rest.Length > 0 ? rendered + " " + rest : rendered;
        }

        return TelexCodec.RenderSyllable(input, _settings.ModernTonePlacement);
    }

    /// <summary>Ghi nhận người dùng đã chọn ứng viên nào cho chuỗi nào.</summary>
    public void RecordChoice(string rawInput, string text)
    {
        if (!_settings.LearnUsage) return;
        _usage.Record(UsageKey(rawInput), text);
    }

    // ------------------------------------------------------------ thu thập

    private void AddMacros(string input, List<Candidate> result, HashSet<string> seen)
    {
        if (!_settings.EnableMacros) return;
        if (!_macros.TryGet(input, out var item)) return;

        foreach (string v in item.Values)
            if (seen.Add(v))
                result.Add(new Candidate
                {
                    Text = v,
                    // Ghi chú của mục gõ tắt thường chính là âm đọc Quốc ngữ.
                    Reading = item.Note ?? "",
                    Hint = item.Note is { Length: > 0 } ? $"gõ tắt · {item.Note}" : "gõ tắt",
                    Tier = CandidateTier.Macro,
                    Weight = int.MaxValue,
                });
    }

    private void AddExact(string input, MatchMode mode, CandidateTier tier,
                          List<Candidate> result, HashSet<string> seen)
    {
        if (input.Length == 0) return;

        var hits = mode switch
        {
            MatchMode.Full => _dict.LookupFull(input),
            MatchMode.Bare => _dict.LookupBare(input),
            MatchMode.Plain => _dict.LookupPlain(input),
            MatchMode.Abbrev => _dict.LookupAbbrev(input),
            MatchMode.Viet => _dict.LookupViet(input),
            _ => Array.Empty<HanEntry>(),
        };

        foreach (var e in hits)
        {
            if (!seen.Add(e.Text)) continue;
            result.Add(new Candidate
            {
                Text = e.Text,
                Reading = ReadingOf(e),
                Hint = HintFor(e),
                Tier = tier,
                Weight = e.Rank,
            });
        }
    }

    /// <summary>
    /// Âm đọc hiển thị. Ưu tiên cột Quốc ngữ ghi thẳng trong từ điển v2 vì đó là
    /// chữ do người biên soạn viết; chỉ khi thiếu cột đó mới dựng từ mã TELEX,
    /// và khi ấy tuỳ chọn đặt dấu kiểu hiện đại mới có tác dụng.
    /// </summary>
    private string ReadingOf(HanEntry e) => e.HasVietColumn
        ? e.Viet
        : TelexCodec.RenderPhrase(e.Syllables, _settings.ModernTonePlacement);

    private void AddPrefix(string input, MatchMode[] modes, List<Candidate> result, HashSet<string> seen)
    {
        // Đủ rộng để lấp kín kho ứng viên, vì gợi ý theo tiền tố xếp bậc thấp
        // nhất nên gần như luôn nằm ở những trang cuối.
        int budget = PoolSize;

        foreach (var mode in modes)
        {
            foreach (var (key, entries) in _dict.EnumeratePrefix(mode, input, budget))
            {
                var e = entries[0];
                if (!seen.Add(e.Text)) continue;
                result.Add(new Candidate
                {
                    Text = e.Text,
                    Reading = ReadingOf(e),
                    Hint = HintFor(e),
                    Tier = CandidateTier.Prefix,
                    // Khoá càng ngắn (càng gần chuỗi đang gõ) thì càng ưu tiên.
                    Weight = e.Rank - (key.Length - input.Length) * 1000,
                });
                if (--budget <= 0) return;
            }
        }
    }

    /// <summary>
    /// Ghép kết quả từ nhiều mục từ điển bằng quy hoạch động trực tiếp trên
    /// chuỗi gõ: cắt chuỗi thành ít mảnh nhất mà mảnh nào cũng là một khoá có
    /// thật trong từ điển. Nhờ vậy "xahooichunghia" cho ra
    /// 社會(xahooi) + 主(chu) + 義(nghia) = 社會主義 thay vì cắt vụn từng chữ.
    /// </summary>
    private void AddComposite(string input, MatchMode[] modes, List<Candidate> result, HashSet<string> seen)
    {
        if (input.Length == 0) return;

        foreach (var mode in modes)
        {
            var comp = ComposeByKeys(input, mode);
            if (comp is null || comp.Pieces < 2) continue;   // 1 mảnh đã do nhánh khớp chính xác lo
            if (!seen.Add(comp.Text)) continue;

            result.Add(new Candidate
            {
                Text = comp.Text,
                Reading = comp.Reading,
                Hint = $"ghép · {comp.Reading}",
                Tier = CandidateTier.Composite,
                // Ít mảnh hơn = ghép gọn hơn = đáng tin hơn.
                Weight = 100_000 - comp.Pieces * 1000 + ModeBonus(mode),
            });
        }
    }

    /// <summary>
    /// Điểm cộng cho phép ghép dựng từ chỉ mục chặt chẽ hơn.
    ///
    /// Mọi phép ghép đều nằm chung một bậc ưu tiên, nên nếu không có điểm này
    /// thì hai cách ghép cùng số mảnh sẽ hoà nhau và thứ tự cuối cùng do phép so
    /// chuỗi quyết định - tức là gần như ngẫu nhiên. Khi đó gõ "hán việt từ điển"
    /// lại ra 恨越詞電 (hận việt từ điện, ghép theo khoá không dấu) đứng trên
    /// 漢越詞典 (hán việt từ điển, ghép theo đúng dấu).
    ///
    /// Khoảng cách giữ nhỏ hơn 1000 để số mảnh vẫn là tiêu chí mạnh hơn.
    /// </summary>
    private static int ModeBonus(MatchMode mode) => mode switch
    {
        MatchMode.Viet => 300,     // khớp đúng từng dấu thanh
        MatchMode.Full => 300,     // mã TELEX đủ dấu, chặt ngang chỉ mục trên
        MatchMode.Bare => 200,     // đúng mũ/móc, bỏ thanh điệu
        _ => 100,                  // không dấu: rộng nhất, nhiều chữ đồng âm nhất
    };

    private MatchMode[] TelexCompositeModes() => _settings.EnablePlainInput
        ? [MatchMode.Full, MatchMode.Bare, MatchMode.Plain]
        : [MatchMode.Full, MatchMode.Bare];

    /// <summary>Kết quả một phép ghép cụm.</summary>
    private sealed record Composition(string Text, string Reading, int Pieces);

    /// <summary>
    /// Quy hoạch động trên chuỗi gõ. Tiêu chí chọn, theo thứ tự:
    /// (1) ít mảnh nhất, (2) tổng trọng số cao nhất.
    /// Trả về null nếu không cắt trọn vẹn được chuỗi.
    /// </summary>
    private Composition? ComposeByKeys(string input, MatchMode mode)
    {
        int n = input.Length;
        if (n == 0) return null;

        int maxKey = Math.Min(_dict.MaxKeyLength, n);

        var pieces = new int[n + 1];        // số mảnh ít nhất để phủ input[i..n)
        var score = new long[n + 1];        // tổng trọng số của cách phủ tốt nhất
        var take = new int[n + 1];          // độ dài mảnh được chọn tại i

        Array.Fill(pieces, int.MaxValue);
        pieces[n] = 0;

        for (int i = n - 1; i >= 0; i--)
        {
            int limit = Math.Min(maxKey, n - i);
            for (int len = 1; len <= limit; len++)
            {
                if (pieces[i + len] == int.MaxValue) continue;

                var hits = _dict.LookupSpan(mode, input.AsSpan(i, len));
                if (hits is null || hits.Count == 0) continue;

                int p = pieces[i + len] + 1;
                long s = score[i + len] + hits[0].Rank;

                if (p < pieces[i] || (p == pieces[i] && s > score[i]))
                {
                    pieces[i] = p;
                    score[i] = s;
                    take[i] = len;
                }
            }
        }

        if (pieces[0] == int.MaxValue) return null;

        var text = new StringBuilder();
        var reading = new StringBuilder();

        for (int pos = 0; pos < n;)
        {
            int len = take[pos];
            var hits = _dict.LookupSpan(mode, input.AsSpan(pos, len))!;
            var best = hits[0];

            text.Append(best.Text);
            if (reading.Length > 0) reading.Append(' ');
            reading.Append(ReadingOf(best));

            pos += len;
        }

        return new Composition(text.ToString(), reading.ToString(), pieces[0]);
    }

    private static string HintFor(HanEntry e)
    {
        string code = e.CodeDisplay;
        if (e.FromUserDict) return code + " · người dùng";
        if (e.Comment is { Length: > 0 } c && c.StartsWith('#')) return code + " · " + c[1..].Trim();
        return code;
    }

    // ------------------------------------------------------------ xếp hạng

    private void Rank(string input, List<Candidate> result)
    {
        if (_settings.LearnUsage)
            foreach (var c in result)
            {
                c.UsageCount = _usage.Get(input, c.Text);
                c.TextUsage = _usage.GetTotal(c.Text);
            }

        result.Sort((a, b) =>
        {
            // 1. Chữ người dùng hay chọn nhất luôn lên đầu (trừ gõ tắt vẫn ưu tiên tuyệt đối).
            if (a.Tier != CandidateTier.Macro || b.Tier != CandidateTier.Macro)
            {
                int u = b.UsageCount.CompareTo(a.UsageCount);
                if (u != 0 && (b.UsageCount > 0 || a.UsageCount > 0))
                {
                    bool aMacro = a.Tier == CandidateTier.Macro;
                    bool bMacro = b.Tier == CandidateTier.Macro;
                    if (aMacro != bMacro) return aMacro ? -1 : 1;
                    return u;
                }
            }

            // 2. Theo bậc ưu tiên kiểu khớp.
            int t = ((int)a.Tier).CompareTo((int)b.Tier);
            if (t != 0) return t;

            // 2b. Cùng bậc thì cách hiểu chính của chuỗi gõ đứng trước cách hiểu dự phòng.
            if (a.Fallback != b.Fallback) return a.Fallback ? 1 : -1;

            // 2c. Rồi tới chữ hay được chọn nói chung, dù trước đó gõ bằng chuỗi nào.
            int tu = b.TextUsage.CompareTo(a.TextUsage);
            if (tu != 0) return tu;

            // 3. Theo trọng số từ điển (10000 = chữ chuẩn).
            int w = b.Weight.CompareTo(a.Weight);
            if (w != 0) return w;

            // 4. Cụm dài hơn thường là lựa chọn người dùng muốn.
            int len = b.Text.Length.CompareTo(a.Text.Length);
            if (len != 0) return len;

            return string.CompareOrdinal(a.Text, b.Text);
        });
    }
}
