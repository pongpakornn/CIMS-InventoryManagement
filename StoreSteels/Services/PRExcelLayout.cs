using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CIMS.Services
{
    // 📊 SETUP EXCEL PATH (User Management): ไฟล์ Excel ของคลัง + ข้อมูล PR ช่องไหนลงเซลล์ / คอลัมน์ไหน
    //   CIMS.Stocks.PRExportPath / PRExcelMap / PRExcelStartRow (Update_20261017.sql)
    //   PRExcelMap = "H:DATE=C1;H:DEPARTMENT=C2;T:CODE=B;..."  H = ค่าเดียวลงเซลล์ที่กำหนด / T = ทุกรายการ แถวละ 1 รายการ ลงคอลัมน์ที่กำหนด เริ่มแถว StartRow
    public class PRExcelEntry
    {
        public string Field { get; set; }      // คีย์ใน PRExcelFields.All
        public bool IsTable { get; set; }      // true = คอลัมน์ของตาราง (แถวละรายการ) / false = เซลล์เดียว
        public string Target { get; set; }     // เซลล์ (C1) หรือคอลัมน์ (B)
    }

    public class PRExcelLayout
    {
        public int StockId { get; set; }
        public string Path { get; set; }
        public int StartRow { get; set; } = 4;
        public List<PRExcelEntry> Entries { get; set; } = new List<PRExcelEntry>();
        public bool HasPath => !string.IsNullOrWhiteSpace(Path);

        public string MapText => string.Join(";", Entries.Where(e => !string.IsNullOrWhiteSpace(e.Field) && !string.IsNullOrWhiteSpace(e.Target))
                                                          .Select(e => $"{(e.IsTable ? "T" : "H")}:{e.Field}={e.Target.Trim().ToUpperInvariant()}"));

        public static List<PRExcelEntry> Parse(string map)
        {
            var list = new List<PRExcelEntry>();
            foreach (var part in (map ?? "").Split(';'))
            {
                var m = Regex.Match(part.Trim(), @"^([HT]):([A-Z0-9_]+)=([A-Z]{1,3}[0-9]*)$", RegexOptions.IgnoreCase);
                if (m.Success) list.Add(new PRExcelEntry { IsTable = m.Groups[1].Value.ToUpperInvariant() == "T", Field = m.Groups[2].Value.ToUpperInvariant(), Target = m.Groups[3].Value.ToUpperInvariant() });
            }
            return list;
        }

        // แบบเริ่มต้น = ไฟล์ RPA - PR ของ STORE-SP
        public static List<PRExcelEntry> DefaultEntries() => Parse("H:DATE=C1;H:DEPARTMENT=C2;H:REMARK=E1;H:TARGET=E2;T:NO=A;T:CODE=B;T:NAME=C;T:QTY=D");

        public static bool ValidCell(string t) => Regex.IsMatch((t ?? "").Trim(), @"^[A-Za-z]{1,3}[1-9][0-9]{0,6}$");
        public static bool ValidColumn(string t) => Regex.IsMatch((t ?? "").Trim(), @"^[A-Za-z]{1,3}$");
    }

    // ตัวเลือกใน Dropdown FIELD (ComboBox แสดงตาม ToString)
    public class PRExcelFieldOption
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public override string ToString() => Label;
    }

    // ช่องข้อมูลของ PR ที่ลง Excel ได้ (เพิ่มช่องใหม่ = เพิ่มบรรทัดที่นี่ + ให้ PRService อ่านค่ามา)
    public static class PRExcelFields
    {
        public static readonly (string Key, string Label)[] All =
        {
            ("DATE", "DATE (today)"), ("NO", "NO (running)"), ("PRNO", "PR NO"), ("PRDATE", "PR DATE"), ("APPROVED", "APPROVED DATE"),
            ("REQUESTER", "REQUESTER"), ("DEPARTMENT", "DEPARTMENT"), ("REMARK", "REMARK"), ("TARGET", "TARGET"), ("STOCK", "STOCK"),
            ("CODE", "PRODUCT CODE"), ("NAME", "PRODUCT NAME"), ("QTY", "QTY"), ("UNIT", "UNIT"),
            ("BIN", "BIN"), ("MOTHERCOIL", "MOTHER COIL"), ("COILNO", "COIL NO"), ("PARTA", "PART A"), ("PARTB", "PART B"),
            ("PARTNO", "PART NO"), ("MODEL", "MODEL"), ("SUPPLIER", "SUPPLIER"), ("CUSTOMER", "CUSTOMER"), ("CATEGORY", "CATEGORY")
        };

        public static string Label(string key) => All.FirstOrDefault(f => f.Key == key).Label ?? key;

        // ค่าของ 1 รายการ (no = ลำดับในไฟล์) - DATE = วันนี้ / ตัวเลข = number
        public static object Value(string key, PRModel x, int no)
        {
            switch (key)
            {
                case "DATE": return DateTime.Today;
                case "NO": return no;
                case "PRNO": return x.PRNumber ?? "";
                case "PRDATE": return x.PRDate;
                case "APPROVED": return x.AppDate.HasValue ? (object)x.AppDate.Value : "";
                case "REQUESTER": return x.Requester ?? "";
                case "DEPARTMENT": return x.Department ?? "";
                case "REMARK": return x.Remark ?? "";
                case "TARGET": return x.TargetDept ?? "";
                case "STOCK": return x.StockCode ?? "";
                case "CODE": return x.PartCode == "N/A" ? "" : x.PartCode ?? "";
                case "NAME": return x.PartName ?? "";
                case "QTY": return x.QTY;
                case "UNIT": return x.Unit ?? "";
                case "BIN": return x.Bin ?? "";
                case "MOTHERCOIL": return x.MotherCoil ?? "";
                case "COILNO": return x.CoilNo ?? "";
                case "PARTA": return x.PartA ?? "";
                case "PARTB": return x.PartB ?? "";
                case "PARTNO": return x.PartNo ?? "";
                case "MODEL": return x.Model ?? "";
                case "SUPPLIER": return x.Supplier ?? "";
                case "CUSTOMER": return x.Customer ?? "";
                case "CATEGORY": return x.Category ?? "";
                default: return "";
            }
        }

        // ช่องเซลล์เดียวจากหลายรายการ = ค่าที่ใช้มากที่สุด (DATE = วันนี้)
        public static object HeaderValue(string key, IList<PRModel> items)
        {
            if (key == "DATE") return DateTime.Today;
            if (key == "NO") return items.Count;
            if (key == "QTY") return items.Sum(i => i.QTY);
            var vals = items.Select((x, i) => Value(key, x, i + 1)).Where(v => v != null && !(v is string s && s.Trim().Length == 0)).ToList();
            return vals.GroupBy(v => Convert.ToString(v, CultureInfo.InvariantCulture)).OrderByDescending(g => g.Count()).Select(g => g.First()).FirstOrDefault() ?? "";
        }
    }
}
