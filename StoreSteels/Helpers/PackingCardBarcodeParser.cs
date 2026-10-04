using System;

namespace CIMS.Helpers
{
    // ตรวจจับ QR ที่พิมพ์จากหน้า Packing Card เมื่อถูกสแกนเข้าหน้า MULTI-SCAN (IN)/(OUT) เดิม
    // รูปแบบ: TicketNo | MaterialCode | WorkOrder | LotNo | JobName | Qty
    public static class PackingCardBarcodeParser
    {
        public static bool TryParse(string raw, out string materialCode, out decimal qty)
        {
            materialCode = null;
            qty = 0;

            if (string.IsNullOrWhiteSpace(raw)) return false;

            var segments = raw.Split(new[] { " | " }, StringSplitOptions.None);
            if (segments.Length != 6) return false;

            materialCode = segments[1].Trim();
            return !string.IsNullOrEmpty(materialCode) && decimal.TryParse(segments[5].Trim(), out qty);
        }
    }
}
