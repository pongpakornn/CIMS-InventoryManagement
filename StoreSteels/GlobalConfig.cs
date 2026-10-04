using CIMS.Models;

namespace CIMS.Core
{
    // ⚙️ ค่าการเชื่อมต่อฐานข้อมูล
    // 🔒 ค่าจริง (ชื่อ Server / User / รหัสผ่าน) อยู่ในไฟล์ GlobalConfig.Local.cs ซึ่งไม่ขึ้น GitHub (.gitignore)
    //    เครื่องใหม่: คัดลอก GlobalConfig.Local.example.txt เป็น GlobalConfig.Local.cs แล้วใส่ค่าจริง
    //    ไม่มีไฟล์ Local = ใช้ค่าตัวอย่างด้านล่าง (Build ได้ แต่ต่อฐานข้อมูลไม่ได้)
    public static partial class GlobalConfig
    {
        // ฐานข้อมูล CIMS (Stock)
        public static readonly string ConnStr;

        // 🏭 ERP (CHR) - ใช้เฉพาะหน้า Pick List (ดึงรายการมาปริ้นอย่างเดียว ไม่เขียนกลับ)
        public static readonly string ErpConnStr;

        public static UserSession CurrentUser { get; set; }

        static GlobalConfig()
        {
            string conn = @"Server=<DB-SERVER>; Database=<DATABASE>; User ID=<USER>; Password=<PASSWORD>; TrustServerCertificate=True;";
            string erp = @"Server=<ERP-SERVER>; Database=<ERP-DATABASE>; User ID=<USER>; Password=<PASSWORD>; TrustServerCertificate=True; Encrypt=False; ApplicationIntent=ReadOnly;";

            ApplyLocal(ref conn, ref erp);

            ConnStr = conn;
            ErpConnStr = erp;
        }

        // ใส่ค่าจริงใน GlobalConfig.Local.cs
        static partial void ApplyLocal(ref string connStr, ref string erpConnStr);
    }
}
