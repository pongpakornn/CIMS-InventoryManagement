using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CIMS.Models
{
    // รูปแบบป้ายบาร์โค้ดของ Supplier ที่ผู้ใช้ตั้งเอง (CIMS.BarcodeFormats) เช่น Panta:
    // DCAE9031B-006;CHR; 26071606; -; DCAE9031B-006;SGACE 45/45; 1.000 X 175.00 X COIL; 1; 613.00; 21/08/2026; DCAE9031B;
    //   ตัวคั่น ";"  ช่องรหัสสินค้า 11 (DCAE9031B)  ช่องรหัสสำรอง 1 (DCAE9031B-006)  ช่องจำนวน 9 (613.00)
    // ตำแหน่งช่องนับจาก 1 (ช่องแรก = 1) ให้ตรงกับที่ผู้ใช้เห็นบนหน้าจอตั้งค่า
    public class BarcodeFormatModel
    {
        public int FmtId { get; set; }
        public string Name { get; set; }
        public string Delimiter { get; set; } = ";";
        public int CodePos { get; set; } = 1;
        public int? AltCodePos { get; set; }
        public int? QtyPos { get; set; }
        public int MinFields { get; set; } = 1;
        public string SampleText { get; set; }
        public bool IsActive { get; set; } = true;

        // แสกนป้ายรูปแบบนี้ "รับเข้าคลังหลัก" -> ตัดยอดจากคลังนี้อัตโนมัติ (เช่น Panta -> STOCK-PANTA) / null = ไม่ตัด
        public int? SourceStkId { get; set; }

        // คืน true เมื่อบาร์โค้ดเข้ารูปแบบนี้ - codes = รหัสที่ใช้ค้นหาตามลำดับ (รหัสหลักก่อน แล้วค่อยรหัสสำรอง)
        // qty = จำนวนจากป้าย (null ถ้ารูปแบบนี้ไม่ได้กำหนดช่องจำนวน -> ใช้ Pack Size ของสินค้าแทน)
        // ตัวกรองป้าย (สำหรับป้ายแบบ StorePC: |2A250-00017   0p37   ...   24.00   24.00   piece)
        public string DelimMode { get; set; } = "CHAR";     // CHAR = แยกด้วย Delimiter / SPACES = ช่องว่างกี่ตัวก็ได้นับเป็นตัวคั่นเดียว
        public string TrimChars { get; set; }               // ตัวอักษรที่ตัดทิ้งหัว-ท้ายป้ายก่อนแยกช่อง เช่น "|"
        public string CodePrefix { get; set; } = "NONE";    // NONE / DIGIT (ตัดเลขตัวแรกที่ตามด้วยตัวอักษร) / CUT (ตัดตามจำนวน CodeCut)
        public int? CodeCut { get; set; }
        public string MatchStart { get; set; }              // ป้ายต้องขึ้นต้นด้วยคำนี้ (ไม่สนตัวพิมพ์เล็ก/ใหญ่)
        public string MatchEnd { get; set; }                // ป้ายต้องลงท้ายด้วยคำนี้

        // ค้นหาสินค้าด้วยอะไร: CODE = รหัส (PRODUCT CODE / QR / PART A / PART NO) / NAME = ชื่อสินค้า (PRODUCT NAME)
        // NameFields = ช่องที่รวมกันเป็นชื่อสินค้า เช่น "6,7" (SGACE 45/45 + 1.000 X 175.00 X COIL) ว่าง = ใช้ CODE FIELD #
        public string MatchBy { get; set; } = "CODE";
        public string NameFields { get; set; }

        public bool MatchByName => string.Equals(MatchBy, "NAME", StringComparison.OrdinalIgnoreCase);

        public List<int> NamePositions()
        {
            var list = (NameFields ?? "").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.TryParse(t.Trim(), out int n) ? n : 0).Where(n => n > 0).Distinct().ToList();
            if (list.Count == 0 && CodePos > 0) list.Add(CodePos);
            return list;
        }

        // เทียบชื่อสินค้าแบบไม่สนรูปแบบการพิมพ์: ตัวพิมพ์ใหญ่-เล็ก / ช่องว่าง / จุลภาค / ศูนย์ท้ายทศนิยม / COIL = C
        //   "SGACE 45/45" + "1.000 X 1219.00 X COIL"  ->  SGACE45/451X1219XC
        //   "SGACE,45/45 1.0x1219xC"                  ->  SGACE45/451X1219XC
        public static string NormalizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string t = s.ToUpperInvariant();
            t = Regex.Replace(t, @"\bCOIL\b", "C");
            t = Regex.Replace(t, @"\d+\.\d+", m => decimal.Parse(m.Value, CultureInfo.InvariantCulture).ToString("0.######", CultureInfo.InvariantCulture));
            return Regex.Replace(t, @"[\s,]+", "");
        }

        public bool SplitBySpaces => string.Equals(DelimMode, "SPACES", StringComparison.OrdinalIgnoreCase);

        // ป้ายนี้ใช่รูปแบบนี้ไหม (ขึ้นต้น / ลงท้าย / มีตัวคั่น)
        public bool Matches(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            if (!string.IsNullOrEmpty(MatchStart) && !t.StartsWith(MatchStart.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(MatchEnd) && !t.EndsWith(MatchEnd.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (!SplitBySpaces && (string.IsNullOrEmpty(Delimiter) || raw.IndexOf(Delimiter, StringComparison.Ordinal) < 0)) return false;
            return true;
        }

        // แยกป้ายเป็นช่อง (ใช้ทั้งตอนแสกนจริงและตัวอย่างในหน้าตั้งค่า)
        public string[] SplitFields(string raw)
        {
            string t = (raw ?? "").Trim();
            if (!string.IsNullOrEmpty(TrimChars)) t = t.Trim(TrimChars.ToCharArray()).Trim();
            if (SplitBySpaces) return Regex.Split(t, @"\s+").Where(x => x.Length > 0).ToArray();
            return string.IsNullOrEmpty(Delimiter) ? new[] { t } : t.Split(new[] { Delimiter }, StringSplitOptions.None);
        }

        // ตัดหัวรหัสตามที่ตั้งไว้ เช่น 2A250-00017 -> A250-00017
        public string CleanCode(string code)
        {
            string c = (code ?? "").Trim();
            if (string.Equals(CodePrefix, "DIGIT", StringComparison.OrdinalIgnoreCase))
            {
                if (c.Length > 2 && char.IsDigit(c[0]) && char.IsLetter(c[1])) c = c.Substring(1);
            }
            else if (string.Equals(CodePrefix, "CUT", StringComparison.OrdinalIgnoreCase) && CodeCut.HasValue && CodeCut.Value > 0)
            {
                c = c.Length > CodeCut.Value ? c.Substring(CodeCut.Value) : "";
            }
            return c;
        }

        // คืน true เมื่อบาร์โค้ดเข้ารูปแบบนี้ - codes = รหัสที่ใช้ค้นหาตามลำดับ (รหัสหลักก่อน แล้วค่อยรหัสสำรอง)
        // qty = จำนวนจากป้าย (null ถ้ารูปแบบนี้ไม่ได้กำหนดช่องจำนวน -> ใช้ Pack Size ของสินค้าแทน)
        public bool TryParse(string raw, out List<string> codes, out decimal? qty)
        {
            codes = new List<string>();
            qty = null;

            if (!IsActive || !Matches(raw)) return false;

            var fields = SplitFields(raw);
            int required = Math.Max(MinFields, Math.Max(CodePos, Math.Max(AltCodePos ?? 0, QtyPos ?? 0)));
            if (MatchByName) required = Math.Max(required, NamePositions().DefaultIfEmpty(0).Max());
            if (fields.Length < required) return false;

            // ค้นด้วยชื่อ: codes[0] = ชื่อสินค้าจากป้าย (รวมช่องที่ตั้งไว้) แล้วค่อยรหัสสำรอง
            if (MatchByName)
            {
                string name = string.Join(" ", NamePositions().Select(n => fields[n - 1].Trim()).Where(x => x.Length > 0 && x != "-"));
                if (name.Length == 0) return false;
                codes.Add(name);
            }
            else AddCode(codes, fields, CodePos);
            if (AltCodePos.HasValue) AddCode(codes, fields, AltCodePos.Value);
            if (codes.Count == 0) return false;

            if (QtyPos.HasValue)
            {
                string q = fields[QtyPos.Value - 1].Trim();
                if (!decimal.TryParse(q, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) || parsed <= 0)
                    return false;
                qty = parsed;
            }
            return true;
        }

        // 🧲 ทะเบียน Coil: ช่องเลข Coil ลูก (เช่น #1 CWE0885B-006) / Coil แม่ (เช่น #11 CWE0885B) - null = รูปแบบนี้ไม่มีเลข Coil
        public int? CoilNoPos { get; set; }
        public int? MotherCoilPos { get; set; }
        public bool HasCoilNo => CoilNoPos.HasValue && CoilNoPos.Value > 0;

        // เลข Coil ลูก / แม่ จากป้าย (ไม่มี = null) - ใช้หลัง TryParse ผ่านแล้ว
        public void ReadCoil(string raw, out string coilNo, out string motherCoil)
        {
            coilNo = null; motherCoil = null;
            if (!HasCoilNo && !(MotherCoilPos > 0)) return;
            var fields = SplitFields(raw);
            string F(int? pos)
            {
                if (!(pos > 0) || pos.Value > fields.Length) return null;
                string v = fields[pos.Value - 1].Trim();
                return v.Length == 0 || v == "-" ? null : v.ToUpperInvariant();
            }
            coilNo = F(CoilNoPos);
            motherCoil = F(MotherCoilPos);
        }

        private void AddCode(List<string> codes, string[] fields, int pos)
        {
            if (pos < 1 || pos > fields.Length) return;
            string c = CleanCode(fields[pos - 1]);
            if (!string.IsNullOrEmpty(c) && c != "-" && !codes.Contains(c)) codes.Add(c);
        }
        public override string ToString() => Name;
    }
}
