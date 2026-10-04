using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CIMS.Helpers
{
    // สแกนเนอร์ส่งบาร์โค้ดเป็น "ปุ่มกด" ถ้าเครื่องเปิดแป้นภาษาไทย (เกษมณี) อยู่ จะได้ตัวอักษรไทยแทน
    // ตัวนี้แปลงตัวอักษรไทยกลับเป็นปุ่มภาษาอังกฤษที่ถูกกด (เช่น "ฟ" -> "a", "ฅ" -> "|")
    // ช่องแสกนบังคับแป้นภาษาอังกฤษไว้แล้ว ตัวนี้เป็นตัวช่วยสำรองตอนแป้นยังเป็นไทย
    // (ปุ่มที่แป้นไทยให้ตัวอักษรอังกฤษอยู่แล้ว เช่น "/" "-" แปลงกลับไม่ได้เพราะแยกไม่ออกว่าตั้งใจพิมพ์)
    public static class ThaiKeyboard
    {
        private static readonly Dictionary<char, char> Map = Build();

        private static Dictionary<char, char> Build()
        {
            // แป้นเกษมณี: ตัวอักษรไทย -> ปุ่มอังกฤษ (ไม่กด Shift / กด Shift)
            const string thai = "ๅภถุึคตจขชๆไำพะัีรนยบลฃฟหกดเ้่าสวงผปแอิืทมใฝ" +
                                "๑๒๓๔ู฿๕๖๗๘๙๐ฎฑธํ๊ณฯญฐฅฤฆฏโฌ็๋ษศซฉฮฺ์ฒฬฦ";
            const string eng  = "14567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./" +
                                "@#$%^&*()_+QERTYUIOP{|ASDFGHJKL:CVBN<>?";
            var map = new Dictionary<char, char>();
            for (int i = 0; i < thai.Length && i < eng.Length; i++)
                if (!map.ContainsKey(thai[i])) map[thai[i]] = eng[i];
            return map;
        }

        // ปุ่มที่แป้นไทยพิมพ์ออกมาเป็นอักษรอังกฤษ/สัญลักษณ์ (ใช้เฉพาะตอนรู้แน่ว่าแป้นเป็นไทยอยู่)
        //   2 -> "/"  3 -> "-"  ` -> "_"  ~ -> "%"  ! -> "+"  W -> "\""  } -> ","  " -> "."  Z -> "("  X -> ")"  M -> "?"
        private static readonly Dictionary<char, char> AsciiMap = new Dictionary<char, char>
        {
            ['/'] = '2', ['-'] = '3', ['_'] = '`', ['%'] = '~', ['+'] = '!', ['"'] = 'W',
            [','] = '}', ['.'] = '"', ['('] = 'Z', [')'] = 'X', ['?'] = 'M'
        };

        public static bool HasThai(string s) => !string.IsNullOrEmpty(s) && s.Any(c => c >= '฀' && c <= '๿');

        // ข้อความที่พิมพ์ตอนแป้นเป็นไทยแน่ ๆ -> ปุ่มอังกฤษที่ถูกกดทุกตัว (รวม "/" = ปุ่ม 2, "-" = ปุ่ม 3)
        public static string FromThaiLayout(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(Map.TryGetValue(c, out char e) ? e : AsciiMap.TryGetValue(c, out char a) ? a : c);
            return sb.ToString();
        }

        public static string ToEnglish(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(Map.TryGetValue(c, out char e) ? e : c);
            return sb.ToString();
        }
    }
}
