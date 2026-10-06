using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CIMS.Services
{
    // 🕘 การเคลื่อนไหวของผู้ใช้งานทั้งระบบ (CIMS.SystemLogs) สำหรับหน้า ACTIVITY LOG
    public class ActivityLogRow
    {
        public DateTime LogDate { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Action { get; set; }
        public string Detail { get; set; }
        public string RefCode { get; set; }
        public string Computer { get; set; }
        public string DateText => LogDate.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        public string UserText => string.IsNullOrWhiteSpace(UserName) ? (UserId ?? "-") : $"{UserId} - {UserName}";
    }

    public class ActivityLogFilter
    {
        public string Keyword { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }      // รวมทั้งวัน
        public string UserId { get; set; }    // null = ทุกคน
        public string Action { get; set; }    // null = ทุกประเภท
    }

    public class ActivityLogService
    {
        public const int PageSize = 50;
        private static bool? _hasPc;

        private static bool HasPc(SqlConnection conn)
        {
            if (_hasPc.HasValue) return _hasPc.Value;
            using (var cmd = new SqlCommand("SELECT COL_LENGTH('CIMS.SystemLogs', 'ComputerName')", conn))
                _hasPc = cmd.ExecuteScalar() != DBNull.Value;
            return _hasPc.Value;
        }

        private static string FromWhere => @"
            FROM CIMS.SystemLogs l
            LEFT JOIN CIMS.Users u ON u.UserID = l.UserID
            WHERE l.LogDate >= @from AND l.LogDate < @to
              AND (@usr IS NULL OR l.UserID = @usr)
              AND (@act IS NULL OR l.ActionType = @act)
              AND (@key = '' OR l.ActionType LIKE '%' + @key + '%' OR l.Description LIKE '%' + @key + '%' OR ISNULL(l.Reference, '') LIKE '%' + @key + '%'
                   OR ISNULL(l.UserID, '') LIKE '%' + @key + '%' OR ISNULL(u.FullName, '') LIKE '%' + @key + '%' {PC})";

        private static string Sql(bool pc, string select, string tail) =>
            select + FromWhere.Replace("{PC}", pc ? "OR ISNULL(l.ComputerName, '') LIKE '%' + @key + '%'" : "") + tail;

        private static string Cols(bool pc) =>
            "SELECT l.LogDate, l.UserID, ISNULL(u.FullName, '') AS FullName, l.ActionType, ISNULL(l.Description, '') AS Description, ISNULL(l.Reference, '') AS Reference, "
            + (pc ? "ISNULL(l.ComputerName, '') AS ComputerName " : "'' AS ComputerName ");

        private static void AddParams(SqlCommand cmd, ActivityLogFilter f)
        {
            cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = f.From.Date;
            cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = f.To.Date.AddDays(1);
            cmd.Parameters.Add("@usr", SqlDbType.VarChar, 50).Value = (object)f.UserId ?? DBNull.Value;
            cmd.Parameters.Add("@act", SqlDbType.VarChar, 100).Value = (object)f.Action ?? DBNull.Value;
            cmd.Parameters.Add("@key", SqlDbType.NVarChar, 200).Value = (f.Keyword ?? "").Trim();
        }

        private static ActivityLogRow Read(SqlDataReader r) => new ActivityLogRow
        {
            LogDate = Convert.ToDateTime(r["LogDate"]),
            UserId = r["UserID"] == DBNull.Value ? null : r["UserID"].ToString(),
            UserName = r["FullName"].ToString(),
            Action = r["ActionType"].ToString(),
            Detail = r["Description"].ToString(),
            RefCode = r["Reference"].ToString(),
            Computer = r["ComputerName"].ToString()
        };

        public async Task<(List<ActivityLogRow> Rows, int Total)> QueryAsync(ActivityLogFilter f, int page, CancellationToken ct)
        {
            var rows = new List<ActivityLogRow>();
            int total;
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                bool pc = HasPc(conn);
                using (var cmd = new SqlCommand(Sql(pc, "SELECT COUNT(*) ", ""), conn))
                {
                    AddParams(cmd, f);
                    total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
                }
                using (var cmd = new SqlCommand(Sql(pc, Cols(pc), " ORDER BY l.LogDate DESC, l.LogID DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY"), conn))
                {
                    AddParams(cmd, f);
                    cmd.Parameters.AddWithValue("@skip", Math.Max(0, page) * PageSize);
                    cmd.Parameters.AddWithValue("@take", PageSize);
                    using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                        while (await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(Read(r));
                }
            }
            return (rows, total);
        }

        // ตัวเลือก USER / ACTION ในตัวกรอง
        public (List<(string Id, string Name)> Users, List<string> Actions) GetLookups()
        {
            var users = new List<(string, string)>();
            var acts = new List<string>();
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT UserID, ISNULL(FullName, '') FROM CIMS.Users ORDER BY UserID", conn))
                using (var r = cmd.ExecuteReader()) while (r.Read()) users.Add((r.GetValue(0).ToString(), r.GetValue(1).ToString()));
                using (var cmd = new SqlCommand("SELECT DISTINCT ActionType FROM CIMS.SystemLogs WHERE ISNULL(ActionType, '') <> '' ORDER BY ActionType", conn))
                using (var r = cmd.ExecuteReader()) while (r.Read()) acts.Add(r.GetValue(0).ToString());
            }
            return (users, acts);
        }

        // 📤 Export ทุกรายการตามตัวกรอง
        public int Export(ActivityLogFilter f, string path)
        {
            var rows = new List<ActivityLogRow>();
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                conn.Open();
                bool pc = HasPc(conn);
                using (var cmd = new SqlCommand(Sql(pc, Cols(pc), " ORDER BY l.LogDate DESC, l.LogID DESC"), conn) { CommandTimeout = 300 })
                {
                    AddParams(cmd, f);
                    using (var r = cmd.ExecuteReader()) while (r.Read()) rows.Add(Read(r));
                }
            }
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Activity Log");
                // รูปแบบเดียวกับไฟล์ Export / Template ทั้งระบบ
                string[] head = { "NO", "DATE TIME", "USER", "ACTION", "DETAIL", "REF", "COMPUTER" };
                ImportTemplateService.Header(ws, head, new double[] { 7, 20, 26, 24, 90, 18, 18 });
                int i = 2;
                foreach (var x in rows)
                {
                    ws.Cell(i, 1).Value = i - 1;
                    ws.Cell(i, 2).Value = x.LogDate; ws.Cell(i, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    ws.Cell(i, 3).Value = x.UserText;
                    ws.Cell(i, 4).Value = x.Action;
                    ws.Cell(i, 5).Value = x.Detail;
                    ws.Cell(i, 6).Value = x.RefCode;
                    ws.Cell(i, 7).Value = x.Computer;
                    i++;
                }
                ImportTemplateService.Body(ws, i - 1, head.Length);
                ImportTemplateService.LeftAlign(ws, i - 1, 3, 5);
                if (i > 2) { ws.Range(2, 1, i - 1, 1).Style.Font.Bold = true; ws.Range(2, 5, i - 1, 5).Style.Alignment.WrapText = true; }
                wb.SaveAs(path);
            }
            return rows.Count;
        }
    }
}
