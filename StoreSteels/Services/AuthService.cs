// LoginView.xaml
using Microsoft.Data.SqlClient;
using CIMS.Models;
using CIMS.Core;
using System;

namespace CIMS.Services
{
    public class AuthService
    {
        // BackUp Authenticate function ไว้ก่อนเผื่อมีปัญหาอะไรจะได้ย้อนกลับไปแก้ไขได้ง่ายๆ
        #region === [ Function Authenticate User : Before ] ===
        //public UserSession Authenticate(string username, string password)
        //{
        //    try
        //    {
        //        using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
        //        {
        //            conn.Open(); // เปิด Connection แค่ครั้งเดียวที่นี่

        //            // 1. เช็คก่อนว่า User นี้ Online ค้างอยู่หรือไม่
        //            // --- [ แก้ไขใน AuthService.cs ] ---

        //            // 1. ปรับ SQL แรกให้ดึงค่า Master Admin มาด้วยเพื่อใช้เช็ค
        //            // 1. เช็คสถานะ Online (ห้ามทุกคนเข้าซ้อน)
        //            string checkSql = "SELECT IS_ONLINE FROM MST_USER WHERE USR_ID = @user";
        //            using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
        //            {
        //                checkCmd.Parameters.AddWithValue("@user", username);
        //                using (SqlDataReader rdr = checkCmd.ExecuteReader())
        //                {
        //                    if (rdr.Read())
        //                    {
        //                        // ใช้ Convert.ToBoolean จะปลอดภัยกว่า (bool) ตรงๆ เพราะรองรับ NULL และเลข 0,1
        //                        bool isOnline = rdr["IS_ONLINE"] != DBNull.Value && Convert.ToBoolean(rdr["IS_ONLINE"]);

        //                        if (isOnline)
        //                        {
        //                            // ตรงนี้แหละที่มันจะไปโชว์ใน NotificationManager.Show ของหน้า Login
        //                            throw new Exception("บัญชีนี้กำลังใช้งานอยู่ในเครื่องอื่น");
        //                        }
        //                    }
        //                }
        //            }

        //            // 2. ถ้าไม่ Online ถึงค่อยเช็ค Password
        //            string sql = @"SELECT USR_ID, USR_NAME, USR_LVL, IS_MASTER_ADMIN, USR_DEPT 
        //                   FROM MST_USER 
        //                   WHERE USR_ID = @user AND USR_PWD = @pass AND IS_LOCKED = 0";

        //            using (SqlCommand cmd = new SqlCommand(sql, conn)) // ใช้ Connection เดิมที่เปิดอยู่ (ไม่ต้อง conn.Open() ซ้ำ)
        //            {
        //                cmd.Parameters.AddWithValue("@user", username);
        //                cmd.Parameters.AddWithValue("@pass", password);

        //                using (SqlDataReader reader = cmd.ExecuteReader())
        //                {
        //                    if (reader.Read())
        //                    {
        //                        // สร้าง Object session ขึ้นมาพักไว้ก่อน
        //                        var session = new UserSession
        //                        {
        //                            UserId = reader["USR_ID"].ToString(),
        //                            UserName = reader["USR_NAME"].ToString(),
        //                            UserLevel = Convert.ToInt32(reader["USR_LVL"]),
        //                            IsMasterAdmin = Convert.ToBoolean(reader["IS_MASTER_ADMIN"]),
        //                            Department = reader["USR_DEPT"]?.ToString() ?? ""
        //                        };

        //                        // *** สำคัญ: ต้องปิด reader ก่อนจะไป Query Permissions ต่อ ***
        //                        reader.Close();

        //                        // 3. โหลด Permissions มาใส่ใน session
        //                        session.Permissions = GetUserPermissions(session.UserId, conn);

        //                        // ส่ง session ที่มีข้อมูลครบถ้วนกลับไป
        //                        return session;
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw ex;
        //    }
        //    return null;
        //}
        #endregion

        // After Authenticate function: เพิ่มการเช็คชื่อเครื่องคอมพิวเตอร์ล่าสุดที่ใช้งานอยู่ด้วย เพื่อให้สามารถ Bypass ได้ถ้าเป็นเครื่องเดียวกัน (กรณีที่โปรแกรมปิดผิดวิธีหรือค้างจนไม่ได้อัพเดตสถานะ Online)
        #region === [ Function Authenticate User : After ] ===
        public UserSession Authenticate(string username, string password)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                {
                    conn.Open();

                    // 1. ปรับ SQL ให้ดึง LAST_SESSION (ชื่อเครื่องคอมพิวเตอร์ล่าสุด) มาตรวจสอบด้วย
                    string checkSql = "SELECT IS_ONLINE, LAST_SESSION FROM MST_USER WHERE USR_ID = @user";
                    using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@user", username);
                        using (SqlDataReader rdr = checkCmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                bool isOnline = rdr["IS_ONLINE"] != DBNull.Value && Convert.ToBoolean(rdr["IS_ONLINE"]);
                                string lastSession = rdr["LAST_SESSION"]?.ToString() ?? "";

                                // ดึงชื่อเครื่องคอมพิวเตอร์ปัจจุบันที่กำลังรันโปรแกรมอยู่
                                string currentMachine = Environment.MachineName;

                                // ถ้าสถานะเป็น Online แต่อยู่บนคอมพิวเตอร์เครื่องเดิม ให้ข้ามการเช็ค (Bypass) ไปได้เลย
                                if (isOnline && lastSession != currentMachine)
                                {
                                    throw new Exception("บัญชีนี้กำลังใช้งานอยู่ในเครื่องอื่น (เครื่อง: " + lastSession + ")");
                                }
                            }
                        }
                    }

                    // 2. ตรวจสอบ Password ต่อตามปกติ
                    string sql = @"SELECT USR_ID, USR_NAME, USR_LVL, IS_MASTER_ADMIN, USR_DEPT 
                           FROM MST_USER 
                           WHERE USR_ID = @user AND USR_PWD = @pass AND IS_LOCKED = 0";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@user", username);
                        cmd.Parameters.AddWithValue("@pass", password);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var session = new UserSession
                                {
                                    UserId = reader["USR_ID"].ToString(),
                                    UserName = reader["USR_NAME"].ToString(),
                                    UserLevel = Convert.ToInt32(reader["USR_LVL"]),
                                    IsMasterAdmin = Convert.ToBoolean(reader["IS_MASTER_ADMIN"]),
                                    Department = reader["USR_DEPT"]?.ToString() ?? ""
                                };

                                reader.Close();

                                session.Permissions = GetUserPermissions(session.UserId, conn);
                                return session;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return null;
        }
        #endregion

        // ฟังก์ชันนี้จะถูกเรียกจากภายใน Authenticate หลังจากที่ตรวจสอบ User ได้แล้ว เพื่ออัพเดตสถานะของ User ว่าออนไลน์แล้ว (IS_ONLINE = 1) และบันทึกเวลาที่ Login เข้ามา (LAST_LOGIN) รวมถึงชื่อเครื่องคอมพิวเตอร์ล่าสุดที่ใช้งาน (LAST_SESSION) เพื่อใช้ในการเช็คในครั้งถัดไป
        #region === [ Function Update Login Stats ] ===
        public void UpdateLoginStats(string userId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                {
                    string sql = @"UPDATE MST_USER SET 
                                   IS_ONLINE = 1, 
                                   LAST_LOGIN = GETDATE(), 
                                   LAST_SESSION = @pc 
                                   WHERE USR_ID = @uid";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@pc", Environment.MachineName);
                    cmd.Parameters.AddWithValue("@uid", userId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }
        #endregion

        // ฟังก์ชันนี้จะถูกเรียกจากภายใน Logout เพื่ออัพเดตสถานะของ User ว่าไม่ได้ออนไลน์แล้ว (IS_ONLINE = 0) และล้างชื่อเครื่องคอมพิวเตอร์ล่าสุด (LAST_SESSION = NULL) เพื่อให้พร้อมสำหรับการ Login ครั้งถัดไป
        #region === [ Function Update Logout Status ] ===
        public void UpdateLogoutStatus(string userId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                {
                    string sql = "UPDATE MST_USER SET IS_ONLINE = 0 WHERE USR_ID = @uid";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@uid", userId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Logout Error: {ex.Message}");
            }
        }
        #endregion

        // ฟังก์ชันนี้จะถูกเรียกจากภายใน Authenticate หลังจากที่ตรวจสอบ User ได้แล้ว เพื่อโหลด Permissions ของ User นั้นๆ มาเก็บไว้ใน Session
        #region === [ Function Get User Permissions ] ===
        private List<UserPermission> GetUserPermissions(string userId, SqlConnection conn)
        {
            List<UserPermission> perms = new List<UserPermission>();
            string sql = "SELECT SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP FROM MST_PERM WHERE USR_ID = @uid";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@uid", userId);
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        perms.Add(new UserPermission
                        {
                            SystemId = rdr["SYS_ID"].ToString(),
                            // เช็คเงื่อนไขถ้าเป็น 'Y' ให้เป็น true
                            CanView = rdr["PERM_VIEW"].ToString() == "Y",
                            CanAdd = rdr["PERM_ADD"].ToString() == "Y",
                            CanEdit = rdr["PERM_EDIT"].ToString() == "Y",
                            CanDelete = rdr["PERM_DEL"].ToString() == "Y",
                            CanApprove = rdr["PERM_APP"].ToString() == "Y"
                        });
                    }
                }
            }
            return perms;
        }
        #endregion
    }
}