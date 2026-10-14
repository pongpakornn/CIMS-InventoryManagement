using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace CIMS.Services
{
    // อ่านข้อมูลจาก ERP (CHR) โดยตรงแบบ Read-only เพื่อเอามาแสดง/ปริ้น Packing Card เฉยๆ
    // ไม่มีการเขียน/แก้ไขข้อมูลกลับเข้า ERP เด็ดขาด (SELECT อย่างเดียว + WITH (NOLOCK) ไม่ล็อกตารางของ ERP)
    public class PackingCardErpService
    {
        private readonly string _erpConnStr = GlobalConfig.ErpConnStr;
        private readonly PackingPrintLogService _printLogService;

        // ค้นหาประวัติย้อนหลังได้ไม่เกิน 2 เดือน
        public const int HistoryMonths = 2;

        public PackingCardErpService(PackingPrintLogService printLogService = null)
        {
            _printLogService = printLogService ?? new PackingPrintLogService();
        }

        // หน้า Pick List ปกติ = วันนี้เท่านั้น ที่ยังไม่ได้พิมพ์
        public List<PackingCardModel> GetPendingPackingCards()
        {
            var today = DateTime.Today;
            var list = Query(today, today);
            var printed = _printLogService.GetPrintedKeys();
            return list.Where(x => !printed.Contains(PackingPrintLogService.MakeKey(x.TicketNo, x.ItemNo))).ToList();
        }

        // 🕘 ประวัติย้อนหลัง (from-to ไม่เกิน 2 เดือน) = ทุกรายการทั้งที่พิมพ์แล้วและยังไม่พิมพ์ (บอกว่าใครพิมพ์ เมื่อไหร่)
        public List<PackingCardModel> GetHistory(DateTime from, DateTime to)
        {
            var list = Query(from.Date, to.Date);
            var printed = _printLogService.GetPrintedInfo();
            foreach (var x in list)
                if (printed.TryGetValue(PackingPrintLogService.MakeKey(x.TicketNo, x.ItemNo), out var p))
                {
                    x.PrintedBy = p.By;
                    x.PrintedDate = p.Date;
                }
            return list;
        }

        // ที่มาข้อมูลอ้างอิงรายงาน sp26issf_CH.rpt ของ ERP - ใช้เฉพาะที่ต้องแสดง:
        //   SD05PDDS (FPDCODE)            -> FPDNAME = PRODUCT NAME, FUNITM = หน่วย
        //   PD20WOI1 (FWONO) -> SD05PDDS  -> FG_FPDNAME = ชื่อสินค้าสำเร็จรูปของ Work Order (PART NAME)
        // ใช้ OUTER APPLY TOP 1 แทน JOIN: ไม่ตัดแถวที่ไม่มีข้อมูลคู่ และไม่ทำให้แถวเบิกซ้ำถ้าตารางคู่มีหลายแถว
        private List<PackingCardModel> Query(DateTime from, DateTime to)
        {
            var list = new List<PackingCardModel>();

            const string sql = @"
                SELECT
                    src.FTRNNO       AS TicketNo,
                    src.FITEMNO      AS ItemNo,
                    src.FGDCODE      AS Warehouse,
                    src.FLOTNO       AS LotNo,
                    src.FPDCODE      AS MaterialCode,
                    src.FWONO        AS WorkOrder,
                    src.FMDATE       AS TicketDate,
                    src.FREMARK      AS JobName,
                    src.FQTY         AS Qty,
                    pds.FPDNAME      AS ProductName,
                    pds.FUNITM       AS Unit,
                    fg.FPDNAME       AS FgName
                FROM dbo.SD11ICTR src WITH (NOLOCK)
                OUTER APPLY (SELECT TOP 1 p.FPDNAME, p.FUNITM
                             FROM dbo.SD05PDDS p WITH (NOLOCK)
                             WHERE p.FPDCODE = src.FPDCODE) pds
                OUTER APPLY (SELECT TOP 1 f.FPDNAME
                             FROM dbo.PD20WOI1 w WITH (NOLOCK)
                             JOIN dbo.SD05PDDS f WITH (NOLOCK) ON f.FPDCODE = w.FPDCODE
                             WHERE w.FWONO = src.FWONO) fg
                WHERE src.FTRNYEAR BETWEEN @YearFrom AND @YearTo          -- FTRNYEAR เป็น varchar(4)
                  AND src.FMOVECODE = 42
                  AND src.FMDATE >= @From AND src.FMDATE < @ToNext
                  AND src.FPDCODE NOT LIKE 'B%'
                ORDER BY src.FMDATE DESC, src.FTRNNO, src.FITEMNO";

            // หมายเหตุ: เดิมสเปกให้ทำ NOT EXISTS (...dbo.PackingPrintLog...) ในคิวรีนี้เลย แต่ทำไม่ได้จริง
            // เพราะ ERP (192.168.10.10) กับ Stock DB คนละเครื่องกัน การ query ข้ามเซิร์ฟเวอร์แบบนี้ต้อง
            // ตั้ง Linked Server ก่อนซึ่งยังไม่มี จึงกรองรายการที่พิมพ์ไปแล้วออกด้วยโค้ดฝั่งแอปแทน (ผลลัพธ์
            // เหมือนกันทุกประการ) ผ่าน PackingPrintLogService ด้านบน
            using (var conn = new SqlConnection(_erpConnStr))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@YearFrom", SqlDbType.VarChar, 4).Value = from.Year.ToString();
                cmd.Parameters.Add("@YearTo", SqlDbType.VarChar, 4).Value = to.Year.ToString();
                cmd.Parameters.Add("@From", SqlDbType.DateTime).Value = from.Date;
                cmd.Parameters.Add("@ToNext", SqlDbType.DateTime).Value = to.Date.AddDays(1);
                cmd.CommandTimeout = 120;

                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    // ภายใต้ SequentialAccess ต้องอ่านแต่ละคอลัมน์ "ครั้งเดียว" และเรียงตาม ordinal จากน้อยไปมาก
                    // (เช็ค IsDBNull ก่อน ค่อยอ่านค่า - ห้ามอ่านคอลัมน์เดิมซ้ำสองครั้งแบบ rdr["Col"] ในทั้งเงื่อนไขและค่า)
                    string Str(int i) => rdr.IsDBNull(i) ? "" : rdr.GetString(i).Trim();
                    while (rdr.Read())
                    {
                        var m = new PackingCardModel
                        {
                            TicketNo = Str(0),
                            ItemNo = Str(1),
                            GroupCode = Str(2),
                            LotNo = Str(3),
                            MaterialCode = Str(4),
                            WorkOrder = Str(5),
                            TicketDate = rdr.IsDBNull(6) ? DateTime.MinValue : rdr.GetDateTime(6),
                            JobName = Str(7),
                        };
                        // FQTY ฝั่ง ERP เป็น float/real ไม่ใช่ decimal/numeric - GetDecimal() cast ตรงๆ ไม่ได้
                        // (SqlDataReader typed getters ต้องตรงชนิดคอลัมน์เป๊ะ) ใช้ GetValue + Convert แทนให้รองรับทั้งสองแบบ
                        m.Qty = rdr.IsDBNull(8) ? 0 : Convert.ToDecimal(rdr.GetValue(8));
                        m.ProductName = Str(9);
                        m.Unit = Str(10);
                        m.FgName = Str(11);
                        list.Add(m);
                    }
                }
            }
            return list;
        }
    }
}
