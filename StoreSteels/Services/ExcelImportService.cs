using ClosedXML.Excel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Data;
using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;

namespace CIMS.Services
{
    public class ExcelImportService
    {
        // -----------------------------------------------------------------
        // [1] นำเข้าไฟล์ FORECAST & ORDER (เริ่มอ่านที่ A3, หัวตารางอยู่ที่บรรทัด 2)
        // -----------------------------------------------------------------
        public List<ExcelForecastOrderModel> ReadForecastOrderExcel(string filePath)
        {
            var list = new List<ExcelForecastOrderModel>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException(
                    $"ไม่พบไฟล์เทมเพลตที่ระบบระบุ: {filePath}");

            using (var workbook = new XLWorkbook(filePath))
            {
                var worksheet = workbook.Worksheet(1);
                var rows = worksheet.RowsUsed();

                foreach (var row in rows)
                {
                    if (row.RowNumber() < 3)
                        continue;

                    if (string.IsNullOrWhiteSpace(row.Cell(1).GetString()) &&
                        string.IsNullOrWhiteSpace(row.Cell(2).GetString()))
                        break;

                    var item = new ExcelForecastOrderModel
                    {
                        Customer = row.Cell(1).GetString().Trim(),
                        PartA = row.Cell(2).GetString().Trim(),

                        Forecast = SafeInt(row.Cell(3)),
                        Order = SafeInt(row.Cell(4)),
                        Delivery = SafeInt(row.Cell(5)),
                        Workday = SafeInt(row.Cell(6))
                    };

                    list.Add(item);
                }
            }

            return list;
        }

        private int SafeInt(IXLCell cell)
        {
            try
            {
                if (cell == null || cell.IsEmpty())
                    return 0;

                string value = cell.GetString().Trim();

                if (string.IsNullOrWhiteSpace(value))
                    return 0;

                if (int.TryParse(value, out int result))
                    return result;

                if (double.TryParse(value, out double dbl))
                    return Convert.ToInt32(dbl);

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // -----------------------------------------------------------------
        // [2] นำเข้าไฟล์ SET MAX MIN (เริ่มอ่านที่ A4, หัวตารางอยู่ที่บรรทัด 3)
        // -----------------------------------------------------------------
        public List<ExcelMaxMinModel> ReadMaxMinExcel(string filePath)
        {
            var list = new List<ExcelMaxMinModel>();

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"ไม่พบไฟล์เทมเพลตที่ระบบระบุ: {filePath}");

            using (var workbook = new XLWorkbook(filePath))
            {
                var worksheet = workbook.Worksheet(1); // หรือชีท "MAX&MIN"
                var rows = worksheet.RowsUsed();

                foreach (var row in rows)
                {
                    // ข้ามบรรทัด 1, 2, 3 (เริ่มอ่านจริงบรรทัดที่ 4)
                    if (row.RowNumber() < 4) continue;

                    if (string.IsNullOrWhiteSpace(row.Cell(1).GetString()) &&
                        string.IsNullOrWhiteSpace(row.Cell(2).GetString()))
                        break;

                    var item = new ExcelMaxMinModel
                    {
                        Customer = row.Cell(1).GetString().Trim(),      // Col A
                        PartNo = row.Cell(2).GetString().Trim(),        // Col B
                        PartName = row.Cell(5).GetString().Trim(),      // Col E
                        DayMax = row.Cell(8).GetValue<int>(),           // Col H
                        DayMin = row.Cell(9).GetValue<int>(),           // Col I
                        WorkDay = row.Cell(10).GetValue<int>()          // Col J
                    };
                    list.Add(item);
                }
            }
            return list;
        }
    }
}