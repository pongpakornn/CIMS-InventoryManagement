using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CIMS.Models
{
    public class UserSession
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public int UserLevel { get; set; }    // UserLevel (1, 2, 3)
        public bool IsMasterAdmin { get; set; } // สำหรับสิทธิ์พิเศษของคุณเอง
        public string Department { get; set; }

        #region === [ เพิ่มส่วนนี้เพื่อเช็คสิทธิ์แบบละเอียด (Permission) ] ===
        public List<UserPermission> Permissions { get; set; } = new List<UserPermission>();
        #endregion

        // =========================================================================
        // 🔐 สิทธิ์ใหม่ (ต.ค. 2026): Level 1 = Admin ทำได้ทุกอย่าง
        //    Level 2 / 3 ไม่มีสิทธิ์อัตโนมัติ ได้เฉพาะที่ติ๊กใน CIMS.Permissions (VIEW / ADD / EDIT / DEL / APPROVE)
        //    ไม่มีสิทธิ์ = ไม่แสดงเมนู / ปุ่ม / แก้ไขไม่ได้
        // =========================================================================
        private bool P(string sysId, Func<UserPermission, bool> check)
        {
            if (UserLevel == 1) return true;
            return !string.IsNullOrEmpty(sysId) &&
                   Permissions.Any(p => string.Equals(p.SystemId, sysId, StringComparison.OrdinalIgnoreCase) && check(p));
        }

        // 📊 Dashboard: VIEW
        public bool CanViewDashboard => P("DASHBOARD", p => p.CanView);

        // 📷 Multi-Scanner (SystemID MultiScanner): VIEW = เมนู + ดูหน้า / HISTORY / TEST MODE
        //    ADD = สแกนเข้า + คืน (RETURN) / EDIT = สแกนออก
        public bool CanViewScannerMenu => P("MultiScanner", p => p.CanView);
        public bool CanViewScanIn => CanViewScannerMenu && P("MultiScanner", p => p.CanAdd);
        public bool CanViewScanOut => CanViewScannerMenu && P("MultiScanner", p => p.CanEdit);
        public bool CanScanReturn => CanViewScanIn;

        // 🏷️ Inventory Registration (ProductControl): VIEW = เมนู + ดู + Export QR / ADD = ลงทะเบียน + IMPORT
        //    EDIT = แก้ไข + SHOW/HIDE + รูปภาพ / DEL = ลบสินค้า
        public bool CanViewProductControl => P("ProductControl", p => p.CanView);
        public bool CanAddProduct => CanViewProductControl && P("ProductControl", p => p.CanAdd);
        public bool CanEditProduct => CanViewProductControl && P("ProductControl", p => p.CanEdit);
        public bool CanDeleteProduct => CanViewProductControl && P("ProductControl", p => p.CanDelete);

        // 📦 Pick List: VIEW
        public bool CanViewPackingCard => P("PackingCard", p => p.CanView);

        // 🧾 PR: VIEW = เมนู + รายการ + APPROVED LIST / ADD = SUBMIT PR
        //    EDIT = EDIT ใน APPROVED LIST + PR SETTINGS / DEL = DEL ใน APPROVED LIST / APPROVE = APPROVE + REJECT + EXPORT
        public bool CanViewPR => P("PR", p => p.CanView);
        public bool CanSubmitPR => CanViewPR && P("PR", p => p.CanAdd);
        public bool CanEditPR => CanViewPR && P("PR", p => p.CanEdit);
        public bool CanDeletePR => CanViewPR && P("PR", p => p.CanDelete);
        public bool CanApprovePR => CanViewPR && P("PR", p => p.CanApprove);
        public bool CanManagePrSettings => CanEditPR;

        // 📐 Max-Min Calculator: VIEW / EDIT (แก้ไข / Import / คำนวณ / สูตร)
        public bool CanViewMaxMinCalc => P("MAXMINCALC", p => p.CanView);
        public bool CanEditMaxMinCalc => CanViewMaxMinCalc && P("MAXMINCALC", p => p.CanEdit);

        // 📊 Forecast / Order / Delivery: VIEW = เมนู + ดู + Export / ADD = Import
        public bool CanViewForecastOrder => P("ForecastOrder", p => p.CanView);
        public bool CanImportForecastOrder => CanViewForecastOrder && P("ForecastOrder", p => p.CanAdd);

        // 🕘 Activity Log: VIEW
        public bool CanViewActivityLog => P("ACTIVITYLOG", p => p.CanView);

        // 👥 User Management: Level 1 / Master admin = ทำได้ทุกอย่าง / คนอื่นตามสิทธิ์ UserManagement
        //    VIEW = เปิดหน้า, ADD = เพิ่ม, EDIT = แก้ไข, DEL = ลบ, APPROVE = กำหนดสิทธิ์
        private bool UserMgmt(Func<UserPermission, bool> check) => IsMasterAdmin || P("UserManagement", check);
        public bool CanViewUserMgmt => UserMgmt(p => p.CanView);
        public bool CanAddUser => CanViewUserMgmt && UserMgmt(p => p.CanAdd);
        public bool CanEditUser => CanViewUserMgmt && UserMgmt(p => p.CanEdit);
        public bool CanDeleteUser => CanViewUserMgmt && UserMgmt(p => p.CanDelete);
        public bool CanSetPermissions => CanViewUserMgmt && UserMgmt(p => p.CanApprove);

        // =========================================================================
        // 🏬 หน้า Store (Max-Min) (SystemID StoreMaxMin)
        //   VIEW = เมนู + เข้าดูคลัง / EDIT = แก้รูปแบบบาร์โค้ด + ตั้งค่าคลัง
        //   ADD  = เพิ่มรูปแบบบาร์โค้ด + เพิ่มคลัง / DEL = ลบคลัง
        // ภายในแต่ละคลัง (SystemID = รหัสคลัง รวมคลังหลัก)
        //   VIEW = เห็นคลัง (ดูอย่างเดียว พิมพ์ REMARK ได้) / EDIT = แก้ MAX / MIN / QTY / STOCK(BOX) / STOCK(PCS)
        //   ADD  = IMPORT EXCEL
        // =========================================================================
        public bool CanViewStore => P("StoreMaxMin", p => p.CanView);
        public bool CanAddStore => CanViewStore && P("StoreMaxMin", p => p.CanAdd);
        public bool CanEditStore => CanViewStore && P("StoreMaxMin", p => p.CanEdit);
        public bool CanDeleteStore => CanViewStore && P("StoreMaxMin", p => p.CanDelete);

        // ปุ่ม BARCODE FORMATS เห็นเมื่อแก้ไขหรือเพิ่มได้
        public bool CanManageFormats => CanEditStore || CanAddStore;
        public bool CanCreateStock => CanAddStore;

        public bool CanViewStock(StockModel s) => s != null && P(s.PermSysId, p => p.CanView);
        // แก้ตัวเลขในตารางของคลัง (MAX / MIN / QTY / STOCK BOX / STOCK PCS)
        public bool CanEditStock(StockModel s) => s != null && CanViewStock(s) && P(s.PermSysId, p => p.CanEdit);
        // ลบคลัง (คลังหลักลบไม่ได้)
        public bool CanDeleteStock(StockModel s) => s != null && !s.IsMain && CanDeleteStore;
        public bool CanImportStock(StockModel s) => s != null && s.InExcel && CanViewStock(s) && P(s.PermSysId, p => p.CanAdd);

        // แก้รหัสคลังแล้ว ชื่อสิทธิ์ในฐานข้อมูลเปลี่ยนตาม -> เปลี่ยนในข้อมูลผู้ใช้ที่ Login อยู่ด้วย ไม่ต้อง Login ใหม่
        public void RenameStockPermission(string oldSysId, string newSysId)
        {
            if (string.IsNullOrEmpty(oldSysId) || oldSysId == newSysId) return;
            foreach (var p in Permissions.Where(p => string.Equals(p.SystemId, oldSysId, StringComparison.OrdinalIgnoreCase))) p.SystemId = newSysId;
        }

        // หลังสร้างคลังใหม่ ให้สิทธิ์ของผู้ใช้ที่ Login อยู่ตรงกับที่ฐานข้อมูลให้ทันที (CIMS.sp_Stock_GrantViewAll: ทุกคนได้ VIEW อย่างเดียว)
        public void GrantViewInSession(StockModel s)
        {
            if (s == null || Permissions.Any(p => string.Equals(p.SystemId, s.PermSysId, StringComparison.OrdinalIgnoreCase))) return;
            Permissions.Add(new UserPermission { SystemId = s.PermSysId, CanView = true });
        }
    }

}
