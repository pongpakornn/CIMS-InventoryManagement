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
        //            string checkSql = "SELECT IsOnline FROM CIMS.Users WHERE UserID = @user";
        //            using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
        //            {
        //                checkCmd.Parameters.AddWithValue("@user", username);
        //                using (SqlDataReader rdr = checkCmd.ExecuteReader())
        //                {
        //                    if (rdr.Read())
        //                    {
        //                        // ใช้ Convert.ToBoolean จะปลอดภัยกว่า (bool) ตรงๆ เพราะรองรับ NULL และเลข 0,1
        //                        bool isOnline = rdr["IsOnline"] != DBNull.Value && Convert.ToBoolean(rdr["IsOnline"]);

        //                        if (isOnline)
        //                        {
        //                            // ตรงนี้แหละที่มันจะไปโชว์ใน NotificationManager.Show ของหน้า Login
        //                            throw new Exception("บัญชีนี้กำลังใช้งานอยู่ในเครื่องอื่น");
        //                        }
        //                    }
        //                }
        //            }

        //            // 2. ถ้าไม่ Online ถึงค่อยเช็ค Password
        //            string sql = @"SELECT UserID, FullName, UserLevel, IsMasterAdmin, Department 
        //                   FROM CIMS.Users 
        //                   WHERE UserID = @user AND Password = @pass AND IsLocked = 0";

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
        //                            UserId = reader["UserID"].ToString(),
        //                            UserName = reader["FullName"].ToString(),
        //                            UserLevel = Convert.ToInt32(reader["UserLevel"]),
        //                            IsMasterAdmin = Convert.ToBoolean(reader["IsMasterAdmin"]),
        //                            Department = reader["Department"]?.ToString() ?? ""
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

                    // 1. ปรับ SQL ให้ดึง LastSession (ชื่อเครื่องคอมพิวเตอร์ล่าสุด) มาตรวจสอบด้วย
                    string checkSql = "SELECT IsOnline, LastSession FROM CIMS.Users WHERE UserID = @user";
                    using (SqlCommand checkCmd = new SqlCommand(checkSql, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@user", username);
                        using (SqlDataReader rdr = checkCmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                bool isOnline = rdr["IsOnline"] != DBNull.Value && Convert.ToBoolean(rdr["IsOnline"]);
                                string lastSession = rdr["LastSession"]?.ToString() ?? "";

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
                    string sql = @"SELECT UserID, FullName, UserLevel, IsMasterAdmin, Department 
                           FROM CIMS.Users 
                           WHERE UserID = @user AND Password = @pass AND IsLocked = 0";

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
                                    UserId = reader["UserID"].ToString(),
                                    UserName = reader["FullName"].ToString(),
                                    UserLevel = Convert.ToInt32(reader["UserLevel"]),
                                    IsMasterAdmin = Convert.ToBoolean(reader["IsMasterAdmin"]),
                                    Department = reader["Department"]?.ToString() ?? ""
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

        // ฟังก์ชันนี้จะถูกเรียกจากภายใน Authenticate หลังจากที่ตรวจสอบ User ได้แล้ว เพื่ออัพเดตสถานะของ User ว่าออนไลน์แล้ว (IsOnline = 1) และบันทึกเวลาที่ Login เข้ามา (LastLogin) รวมถึงชื่อเครื่องคอมพิวเตอร์ล่าสุดที่ใช้งาน (LastSession) เพื่อใช้ในการเช็คในครั้งถัดไป
        #region === [ Function Update Login Stats ] ===
        public void UpdateLoginStats(string userId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                {
                    string sql = @"UPDATE CIMS.Users SET 
                                   IsOnline = 1, 
                                   LastLogin = GETDATE(), 
                                   LastSession = @pc 
                                   WHERE UserID = @uid";
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

        // ฟังก์ชันนี้จะถูกเรียกจากภายใน Logout เพื่ออัพเดตสถานะของ User ว่าไม่ได้ออนไลน์แล้ว (IsOnline = 0) และล้างชื่อเครื่องคอมพิวเตอร์ล่าสุด (LastSession = NULL) เพื่อให้พร้อมสำหรับการ Login ครั้งถัดไป
        #region === [ Function Update Logout Status ] ===
        public void UpdateLogoutStatus(string userId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                {
                    string sql = "UPDATE CIMS.Users SET IsOnline = 0 WHERE UserID = @uid";
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
            string sql = "SELECT SystemID, CanView, CanAdd, CanEdit, CanDelete, CanApprove FROM CIMS.Permissions WHERE UserID = @uid";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@uid", userId);
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        perms.Add(new UserPermission
                        {
                            SystemId = rdr["SystemID"].ToString(),
                            // เช็คเงื่อนไขถ้าเป็น 'Y' ให้เป็น true
                            CanView = rdr["CanView"].ToString() == "Y",
                            CanAdd = rdr["CanAdd"].ToString() == "Y",
                            CanEdit = rdr["CanEdit"].ToString() == "Y",
                            CanDelete = rdr["CanDelete"].ToString() == "Y",
                            CanApprove = rdr["CanApprove"].ToString() == "Y"
                        });
                    }
                }
            }
            return perms;
        }
        #endregion
    }
}