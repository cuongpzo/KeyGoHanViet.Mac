namespace KeyGoHanViet.Core;

/// <summary>
/// Tách một chuỗi Latin liền mạch thành dãy âm tiết hợp lệ.
/// Ví dụ "xahooichunghia" -> ["xa", "hooi", "chu", "nghia"].
/// </summary>
public sealed class Segmenter
{
    private readonly HashSet<string> _syllables;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;
    private readonly int _maxLen;

    public Segmenter(HashSet<string> syllables, int maxSyllableLength)
    {
        _syllables = syllables;
        _lookup = syllables.GetAlternateLookup<ReadOnlySpan<char>>();
        _maxLen = Math.Max(1, maxSyllableLength);
    }

    public bool IsSyllable(string s) => _syllables.Contains(s);

    /// <summary>
    /// Quy hoạch động: tìm cách tách có ít âm tiết nhất; khi hoà thì ưu tiên
    /// âm tiết đứng sau dài hơn (khớp dài nhất từ phải sang).
    /// Trả về null nếu không tách được toàn bộ chuỗi.
    /// </summary>
    public string[]? Segment(string input)
    {
        int n = input.Length;
        if (n == 0) return [];

        // best[i] = số âm tiết ít nhất để phủ input[i..n); int.MaxValue = không phủ được.
        var best = new int[n + 1];
        var cut = new int[n + 1];       // độ dài âm tiết được chọn tại vị trí i
        Array.Fill(best, int.MaxValue);
        best[n] = 0;

        for (int i = n - 1; i >= 0; i--)
        {
            int limit = Math.Min(_maxLen, n - i);
            for (int len = limit; len >= 1; len--)
            {
                if (best[i + len] == int.MaxValue) continue;
                if (!_lookup.Contains(input.AsSpan(i, len))) continue;

                int cost = best[i + len] + 1;
                // len duyệt từ dài xuống ngắn nên "<" giữ lại âm tiết dài nhất khi hoà.
                if (cost < best[i])
                {
                    best[i] = cost;
                    cut[i] = len;
                }
            }
        }

        if (best[0] == int.MaxValue) return null;

        var result = new string[best[0]];
        int pos = 0;
        for (int k = 0; k < result.Length; k++)
        {
            result[k] = input.Substring(pos, cut[pos]);
            pos += cut[pos];
        }
        return result;
    }

    /// <summary>
    /// Tách "tốt nhất có thể": nếu không tách trọn vẹn được thì trả về phần
    /// tách được ở đầu cùng với phần đuôi chưa khớp.
    /// </summary>
    public (string[] Syllables, string Remainder) SegmentPartial(string input)
    {
        for (int end = input.Length; end > 0; end--)
        {
            var seg = Segment(input[..end]);
            if (seg is not null) return (seg, input[end..]);
        }
        return ([], input);
    }
}
