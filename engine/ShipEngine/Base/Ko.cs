namespace ShipEngine;

/// <summary>한국어 글 도우미: 받침에 따라 조사를 고른다 (이/가 · 을/를 · 은/는 · 과/와 · 으로/로).</summary>
public static class Ko
{
    /// <summary>마지막 글자에 받침이 있나 (한글이 아니면 없다고 본다 — 숫자는 읽는 소리로).</summary>
    public static bool HasFinal(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        char c = word[^1];
        if (c >= '0' && c <= '9') return "013678".Contains(c); // 영 일 삼 육 칠 팔
        if (c < 0xAC00 || c > 0xD7A3) return false;
        return (c - 0xAC00) % 28 != 0;
    }

    public static string I(string w) => w + (HasFinal(w) ? "이" : "가");
    public static string Eul(string w) => w + (HasFinal(w) ? "을" : "를");
    public static string Eun(string w) => w + (HasFinal(w) ? "은" : "는");
    public static string Gwa(string w) => w + (HasFinal(w) ? "과" : "와");
    public static string Euro(string w)
    {
        if (string.IsNullOrEmpty(w)) return w;
        char c = w[^1];
        bool rieul = c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 == 8;
        return w + (HasFinal(w) && !rieul ? "으로" : "로");
    }
}
