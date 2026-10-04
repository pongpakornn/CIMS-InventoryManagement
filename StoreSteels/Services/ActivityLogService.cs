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
    // 🕘 การเคลื่อนไหวของผู้ใช้งานทั้งระบบ (SYS_LOGS) สำหรับหน้า ACTIVITY LOG
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
            using (var cmd = new SqlCommand("SELECT COL_LENGTH('dbo.SYS_LOGS', 'LOG_PC')", conn))
                _hasPc = cmd.ExecuteScalar() != DBNull.Value;
            return _hasPc.Value;
        }

        private static string FromWhere => @"
            FROM SYS_LOGS l
            LEFT JOIN MST_USER u ON u.USR_ID = l.USR_ID
            WHERE l.LOG_DATE >= @from AND l.LOG_DATE < @to
              AND (@usr IS NULL OR l.USR_ID = @usr)
              AND (@act IS NULL OR l.ACT_TYPE = @act)
              AND (@key = '' OR l.ACT_TYPE LIKE '%' + @key + '%' OR l.LOG_DESC LIKE '%' + @key + '%' OR ISNULL(l.LOG_REF, '') LIKE '%' + @key + '%'
                   OR ISNULL(l.USR_ID, '') LIKE '%' + @key + '%' OR ISNULL(u.USR_NAME, '') LIKE '%' + @key + '%' {PC})";

        private static string Sql(bool pc, string select, string tail) =>
            select + FromWhere.Replace("{PC}", pc ? "OR ISNULL(l.LOG_PC, '') LIKE '%' + @key + '%'" : "") + tail;

        private static string Cols(bool pc) =>
            "SELECT l.LOG_DATE, l.USR_ID, ISNULL(u.USR_NAME, '') AS USR_NAME, l.ACT_TYPE, ISNULL(l.LOG_DESC, '') AS LOG_DESC, ISNULL(l.LOG_REF, '') AS LOG_REF, "
            + (pc ? "ISNULL(l.LOG_PC, '') AS LOG_PC " : "'' AS LOG_PC ");

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
            LogDate = Convert.ToDateTime(r["LOG_DATE"]),
            UserId = r["USR_ID"] == DBNull.Value ? null : r["USR_ID"].ToString(),
            UserName = r["USR_NAME"].ToString(),
            Action = r["ACT_TYPE"].ToString(),
            Detail = r["LOG_DESC"].ToString(),
            RefCode = r["LOG_REF"].ToString(),
            Computer = r["LOG_PC"].ToString()
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
                using (var cmd = new SqlCommand(Sql(pc, Cols(pc), " ORDER BY l.LOG_DATE DESC, l.LOG_ID DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY"), conn))
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
                using (var cmd = new SqlCommand("SELECT USR_ID, ISNULL(USR_NAME, '') FROM MST_USER ORDER BY USR_ID", conn))
                using (var r = cmd.ExecuteReader()) while (r.Read()) users.Add((r.GetValue(0).ToString(), r.GetValue(1).ToString()));
                using (var cmd = new SqlCommand("SELECT DISTINCT ACT_TYPE FROM SYS_LOGS WHERE ISNULL(ACT_TYPE, '') <> '' ORDER BY ACT_TYPE", conn))
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
                using (var cmd = new SqlCommand(Sql(pc, Cols(pc), " ORDER BY l.LOG_DATE DESC, l.LOG_ID DESC"), conn) { CommandTimeout = 300 })
                {
                    AddParams(cmd, f);
                    using (var r = cmd.ExecuteReader()) while (r.Read()) rows.Add(Read(r));
                }
            }
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Activity Log");
                string[] head = { "DATE TIME", "USER", "ACTION", "DETAIL", "REF", "COMPUTER" };
                for (int c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
                var h = ws.Range(1, 1, 1, head.Length);
                h.Style.Font.Bold = true;
                h.Style.Fill.BackgroundColor = XLColor.FromHtml("#002060");
                h.Style.Font.FontColor = XLColor.White;
                int i = 2;
                foreach (var x in rows)
                {
                    ws.Cell(i, 1).Value = x.LogDate; ws.Cell(i, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    ws.Cell(i, 2).Value = x.UserText;
                    ws.Cell(i, 3).Value = x.Action;
                    ws.Cell(i, 4).Value = x.Detail;
                    ws.Cell(i, 5).Value = x.RefCode;
                    ws.Cell(i, 6).Value = x.Computer;
                    i++;
                }
                ws.Columns(1, 3).AdjustToContents();
                ws.Column(4).Width = 90;
                ws.Columns(5, 6).AdjustToContents();
                ws.SheetView.FreezeRows(1);
                wb.SaveAs(path);
            }
            return rows.Count;
        }
    }
}
