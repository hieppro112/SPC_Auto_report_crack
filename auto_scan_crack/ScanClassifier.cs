using System.Text.RegularExpressions;

namespace auto_scan_crack
{
    public enum ScanKind
    {
        Unknown,
        /// <summary>Mã máy: bắt đầu bằng "TRTB" (vd TRTB23333).</summary>
        Terminal,
        /// <summary>MSNV: chỉ gồm 4 hoặc 5 chữ số.</summary>
        Msnv,
        /// <summary>PO: chỉ gồm 12 chữ số.</summary>
        Po,
    }

    /// <summary>Phân loại chuỗi vừa quét để biết đó là mã máy, MSNV hay PO.</summary>
    public static partial class ScanClassifier
    {
        public static ScanKind Classify(string code)
        {
            var c = code.Trim();
            if (TerminalRegex().IsMatch(c)) return ScanKind.Terminal;
            if (MsnvRegex().IsMatch(c)) return ScanKind.Msnv;
            if (PoRegex().IsMatch(c)) return ScanKind.Po;
            return ScanKind.Unknown;
        }

        [GeneratedRegex(@"^TRTB\S+$", RegexOptions.IgnoreCase)]
        private static partial Regex TerminalRegex();

        [GeneratedRegex(@"^\d{4,5}$")]
        private static partial Regex MsnvRegex();

        [GeneratedRegex(@"^\d{12}$")]
        private static partial Regex PoRegex();
    }
}
