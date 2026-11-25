using System.IO;
using Microsoft.Data.SqlClient;

namespace auto_scan_crack.Database
{
    /// <summary>
    /// Truy vấn số lượng hàng tốt (GOODCNT) của PO trong MANUFASPCPD.
    /// Chuỗi kết nối đọc từ file connection.txt đặt cạnh file exe (không ghi trong code, không commit lên git).
    /// </summary>
    public static class PoRepository
    {
        public const string ConnectionFileName = "connection.txt";

        public static string ConnectionFilePath => Path.Combine(AppContext.BaseDirectory, ConnectionFileName);

        private const string GoodCountSql = """
            SELECT  SUM(ISNULL(mfpda.AMNT, 0) - ISNULL(mfpda.BADCNT, 0)) AS GOODCNT,
                    COUNT(*) AS ROWCNT
            FROM MANUFASPCPD.dbo.MANUFA_F_PD_DT_ASSIST AS mfpda
            WHERE mfpda.AUFNR = @po;
            """;

        /// <summary>
        /// GOODCNT = AMNT - BADCNT của PO (cộng dồn nếu PO có nhiều dòng).
        /// Trả về null nếu không có dòng nào cho PO này.
        /// </summary>
        public static async Task<int?> GetGoodCountAsync(string po, CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(LoadConnectionString());
            await conn.OpenAsync(ct);

            await using var cmd = new SqlCommand(GoodCountSql, conn) { CommandTimeout = 15 };
            cmd.Parameters.Add("@po", System.Data.SqlDbType.NVarChar, 50).Value = po;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetInt32(1) == 0)
                return null;
            return reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
        }

        private static string LoadConnectionString()
        {
            var path = ConnectionFilePath;
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Chưa có file cấu hình kết nối DB: {path}. Tạo file theo mẫu connection.example.txt.");

            // Bỏ các dòng trống và dòng ghi chú bắt đầu bằng #
            var cs = string.Join("", File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith('#')));
            if (cs.Length == 0)
                throw new InvalidOperationException($"File {path} chưa có chuỗi kết nối.");
            return cs;
        }
    }
}
