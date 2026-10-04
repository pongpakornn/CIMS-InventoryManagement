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
    // ไม่มีการเขียน/แก้ไขข้อมูลกลับเข้า ERP เด็ดขาด
    public class PackingCardErpService
    {
        private readonly string _erpConnStr = GlobalConfig.ErpConnStr;
        private readonly PackingPrintLogService _printLogService;

        public PackingCardErpService(PackingPrintLogService printLogService = null)
        {
            _printLogService = printLogService ?? new PackingPrintLogService();
        }

        public List<PackingCardModel> GetPendingPackingCards()
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
                    src.FQTY         AS Qty
                FROM dbo.SD11ICTR src
                WHERE src.FTRNYEAR IN (YEAR(GETDATE()), YEAR(DATEADD(DAY, -1, GETDATE())))
                  AND src.FMOVECODE = 42
                  AND src.FMDATE >= DATEADD(DAY, -1, CAST(GETDATE() AS DATE))   -- Pick List: ย้อนหลัง 1 วัน (เมื่อวาน + วันนี้)
                  AND src.FPDCODE NOT LIKE 'B%'
                ORDER BY src.FMDATE DESC, src.FTRNNO, src.FITEMNO";

            // หมายเหตุ: เดิมสเปกให้ทำ NOT EXISTS (...dbo.PackingPrintLog...) ในคิวรีนี้เลย แต่ทำไม่ได้จริง
            // เพราะ ERP (192.168.10.10) กับ Stock DB คนละเครื่องกัน การ query ข้ามเซิร์ฟเวอร์แบบนี้ต้อง
            // ตั้ง Linked Server ก่อนซึ่งยังไม่มี จึงกรองรายการที่พิมพ์ไปแล้วออกด้วยโค้ดฝั่งแอปแทน (ผลลัพธ์
            // เหมือนกันทุกประการ) ผ่าน PackingPrintLogService.GetPrintedKeys() ด้านล่าง
            using (var conn = new SqlConnection(_erpConnStr))
            using (var cmd = new SqlCommand(sql, conn))
            {
                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    int ordTicketNo = rdr.GetOrdinal("TicketNo");
                    int ordItemNo = rdr.GetOrdinal("ItemNo");
                    int ordWarehouse = rdr.GetOrdinal("Warehouse");
                    int ordLotNo = rdr.GetOrdinal("LotNo");
                    int ordMaterialCode = rdr.GetOrdinal("MaterialCode");
                    int ordWorkOrder = rdr.GetOrdinal("WorkOrder");
                    int ordTicketDate = rdr.GetOrdinal("TicketDate");
                    int ordJobName = rdr.GetOrdinal("JobName");
                    int ordQty = rdr.GetOrdinal("Qty");

                    // ภายใต้ SequentialAccess ต้องอ่านแต่ละคอลัมน์ "ครั้งเดียว" และเรียงตาม ordinal จากน้อยไปมาก
                    // (เช็ค IsDBNull ก่อน ค่อยอ่านค่า - ห้ามอ่านคอลัมน์เดิมซ้ำสองครั้งแบบ rdr["Col"] ในทั้งเงื่อนไขและค่า)
                    while (rdr.Read())
                    {
                        list.Add(new PackingCardModel
                        {
                            TicketNo = rdr.IsDBNull(ordTicketNo) ? "" : rdr.GetString(ordTicketNo).Trim(),
                            ItemNo = rdr.IsDBNull(ordItemNo) ? "" : rdr.GetString(ordItemNo).Trim(),
                            GroupCode = rdr.IsDBNull(ordWarehouse) ? "" : rdr.GetString(ordWarehouse).Trim(),
                            LotNo = rdr.IsDBNull(ordLotNo) ? "" : rdr.GetString(ordLotNo).Trim(),
                            MaterialCode = rdr.IsDBNull(ordMaterialCode) ? "" : rdr.GetString(ordMaterialCode).Trim(),
                            WorkOrder = rdr.IsDBNull(ordWorkOrder) ? "" : rdr.GetString(ordWorkOrder).Trim(),
                            TicketDate = rdr.IsDBNull(ordTicketDate) ? DateTime.MinValue : rdr.GetDateTime(ordTicketDate),
                            JobName = rdr.IsDBNull(ordJobName) ? "" : rdr.GetString(ordJobName).Trim(),
                            // FQTY ฝั่ง ERP เป็น float/real ไม่ใช่ decimal/numeric - GetDecimal() cast ตรงๆ ไม่ได้
                            // (SqlDataReader typed getters ต้องตรงชนิดคอลัมน์เป๊ะ) ใช้ GetValue + Convert แทนให้รองรับทั้งสองแบบ
                            Qty = rdr.IsDBNull(ordQty) ? 0 : Convert.ToDecimal(rdr.GetValue(ordQty))
                        });
                    }
                }
            }

            var printedKeys = _printLogService.GetPrintedKeys();
            return list.Where(x => !printedKeys.Contains(PackingPrintLogService.MakeKey(x.TicketNo, x.ItemNo))).ToList();
        }
    }
}
