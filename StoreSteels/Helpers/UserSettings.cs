using CIMS.Core;
using Microsoft.Data.SqlClient;
using System;

namespace CIMS.Helpers
{
    // ค่าที่จำไว้ต่อผู้ใช้ (ตาม UserID - ใช้เครื่องไหนก็ได้ค่าเดิม) เก็บที่ CIMS.UserSettings
    // ฐานยังไม่รัน Update_20261013b.sql -> จำไว้ในเครื่องนี้แทน (UiPrefs แยกตาม UserID) / อ่าน-เขียนไม่ได้ = ใช้ค่าเริ่มต้น ไม่แจ้ง Error
    public static class UserSettings
    {
        private static bool? _hasTable;
        private static bool HasTable
        {
            get
            {
                if (_hasTable.HasValue) return _hasTable.Value;
                try
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID('CIMS.UserSettings', 'U') IS NOT NULL THEN 1 ELSE 0 END", conn))
                    {
                        conn.Open();
                        _hasTable = Convert.ToInt32(cmd.ExecuteScalar()) == 1;
                    }
                }
                catch { return false; }
                return _hasTable.Value;
            }
        }

        public static string Get(string userId, string key, string fallback = null)
        {
            if (string.IsNullOrWhiteSpace(userId)) return fallback;
            try
            {
                if (!HasTable) return UiPrefs.Get($"{userId}.{key}", fallback);
                using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                using (var cmd = new SqlCommand("SELECT SettingValue FROM CIMS.UserSettings WHERE UserID = @u AND SettingKey = @k", conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@k", key);
                    conn.Open();
                    var v = cmd.ExecuteScalar();
                    return v == null || v is DBNull ? fallback : (string)v;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UserSettings.Get {key}: {ex.Message}"); return fallback; }
        }

        public static void Set(string userId, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            try
            {
                if (!HasTable) { UiPrefs.Set($"{userId}.{key}", value ?? ""); return; }
                using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                using (var cmd = new SqlCommand(@"IF EXISTS (SELECT 1 FROM CIMS.UserSettings WHERE UserID = @u AND SettingKey = @k)
                                                      UPDATE CIMS.UserSettings SET SettingValue = @v, UpdatedDate = GETDATE() WHERE UserID = @u AND SettingKey = @k
                                                  ELSE
                                                      INSERT INTO CIMS.UserSettings (UserID, SettingKey, SettingValue) VALUES (@u, @k, @v)", conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@k", key);
                    cmd.Parameters.AddWithValue("@v", (object)value ?? DBNull.Value);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UserSettings.Set {key}: {ex.Message}"); }
        }
    }
}
