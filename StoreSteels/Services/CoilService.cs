// ทะเบียน Coil (CIMS.Coils) - Coil ลูก / Coil แม่ / น้ำหนัก KG ของแต่ละสินค้าในคลังที่นับ Coil
using CIMS.Core;
using CIMS.Helpers;
using CIMS.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CIMS.Services
{
    public class CoilService
    {
        // Coil ที่อยู่ในคลัง (Status = IN) ของสินค้า 1 ตัว -> เรียงตาม Coil แม่ แล้วเลข Coil ลูก
        // คืนเป็นแถวพร้อมแสดง: หัวกลุ่ม Coil แม่ (IsMother) ตามด้วย Coil ลูกในกลุ่มนั้น
        public List<CoilRowModel> GetCoilRows(int stockId, int partId)
        {
            var coils = GetCoils(stockId, partId);
            var rows = new List<CoilRowModel>();
            foreach (var g in coils.GroupBy(c => string.IsNullOrWhiteSpace(c.MotherCoil) ? "-" : c.MotherCoil))
            {
                var list = g.ToList();
                rows.Add(new CoilRowModel
                {
                    IsMother = true,
                    MotherCoil = g.Key,
                    CoilNo = $"{list.Count} COIL",
                    WeightKG = list.Sum(c => c.WeightKG)
                });
                int no = 1;
                foreach (var c in list) { c.No = no++; rows.Add(c); }
            }
            return rows;
        }

        // partId = 0 -> ทุกสินค้าในคลัง (ใช้ตอน Export)
        public List<CoilRowModel> GetCoils(int stockId, int partId)
        {
            var list = new List<CoilRowModel>();
            if (!DbSchema.HasCoilRegister || stockId <= 0 || partId < 0) return list;

            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand(@"
                SELECT CoilID, PartID, CoilNo, MotherCoil, WeightKG, LabelDate, ReceivedDate
                FROM CIMS.Coils
                WHERE StockID = @stk AND (@pt = 0 OR PartID = @pt) AND Status = 'IN'
                ORDER BY PartID, ISNULL(MotherCoil, N''), CoilNo", conn))
            {
                cmd.Parameters.AddWithValue("@stk", stockId);
                cmd.Parameters.AddWithValue("@pt", partId);
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        list.Add(new CoilRowModel
                        {
                            CoilId = Convert.ToInt32(rdr["CoilID"]),
                            PartId = Convert.ToInt32(rdr["PartID"]),
                            CoilNo = rdr["CoilNo"].ToString(),
                            MotherCoil = rdr["MotherCoil"] == DBNull.Value ? null : rdr["MotherCoil"].ToString(),
                            WeightKG = rdr["WeightKG"] == DBNull.Value ? 0 : Convert.ToDecimal(rdr["WeightKG"]),
                            LabelDate = rdr["LabelDate"] == DBNull.Value ? null : rdr["LabelDate"].ToString(),
                            ReceivedDate = rdr["ReceivedDate"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rdr["ReceivedDate"])
                        });
                    }
                }
            }
            return list;
        }

        // จำนวน Coil ที่ลงทะเบียนไว้ต่อสินค้า (ใช้บอกว่าแถวไหนกดดู Coil ได้)
        public Dictionary<int, int> CountByPart(int stockId)
        {
            var map = new Dictionary<int, int>();
            if (!DbSchema.HasCoilRegister || stockId <= 0) return map;

            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand("SELECT PartID, COUNT(*) FROM CIMS.Coils WHERE StockID = @stk AND Status = 'IN' GROUP BY PartID", conn))
            {
                cmd.Parameters.AddWithValue("@stk", stockId);
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                    while (rdr.Read()) map[rdr.GetInt32(0)] = rdr.GetInt32(1);
            }
            return map;
        }
    }
}
