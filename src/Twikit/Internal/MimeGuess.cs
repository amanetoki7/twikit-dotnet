namespace Twikit.Internal;

/// <summary>マジックナンバーから MIME タイプを推定します（Python 版の filetype 相当）。</summary>
internal static class MimeGuess
{
    public static string? Guess(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) return "image/png";
        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8'
            && (data[4] == '7' || data[4] == '9') && data[5] == 'a') return "image/gif";
        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
            && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') return "image/webp";
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return "image/bmp";
        if (data.Length >= 4 && ((data[0] == 'I' && data[1] == 'I' && data[2] == 0x2A && data[3] == 0)
                                 || (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && data[3] == 0x2A))) return "image/tiff";
        if (data.Length >= 4 && data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3) return "video/webm";
        if (data.Length >= 12 && data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(data.Slice(8, 4));
            if (brand.StartsWith("qt", StringComparison.Ordinal)) return "video/quicktime";
            if (brand is "M4V " or "M4VH" or "M4VP") return "video/x-m4v";
            if (brand is "M4A " or "M4B ") return "audio/mp4";
            if (brand.StartsWith("heic", StringComparison.Ordinal) || brand.StartsWith("heix", StringComparison.Ordinal)
                || brand.StartsWith("mif1", StringComparison.Ordinal)) return "image/heic";
            if (brand.StartsWith("avif", StringComparison.Ordinal)) return "image/avif";
            return "video/mp4";
        }
        if (data.Length >= 3 && data[0] == 'I' && data[1] == 'D' && data[2] == '3') return "audio/mpeg";
        if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return "audio/mpeg";
        if (data.Length >= 4 && data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S') return "audio/ogg";
        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
            && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E') return "audio/x-wav";
        if (data.Length >= 4 && data[0] == 'f' && data[1] == 'L' && data[2] == 'a' && data[3] == 'C') return "audio/x-flac";
        if (data.Length >= 4 && data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46) return "application/pdf";
        return null;
    }
}
