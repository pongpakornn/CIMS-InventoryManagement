using CIMS.Helpers;
    using CIMS.Models;
    using CIMS.Services;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;
    using System.Windows;

    namespace CIMS.ViewModels
    {
        public class PRViewModel : INotifyPropertyChanged
        {
            private readonly PRService _prService = new PRService();
            private readonly ExportService _exportService = new ExportService();
            public event PropertyChangedEventHandler PropertyChanged;

            public ObservableCollection<PRModel> PRHistory { get; set; } = new ObservableCollection<PRModel>();
            public ObservableCollection<string> ProductSuggestions { get; set; } = new ObservableCollection<string>();

            // ช่องที่พิมพ์เองได้ + แนะนำค่าที่เคยใช้ (MST_PR_LOOKUP)
            public ObservableCollection<string> RequesterSuggestions { get; } = new ObservableCollection<string>();
            public ObservableCollection<string> DeptSuggestions { get; } = new ObservableCollection<string>();
            public ObservableCollection<string> RemarkSuggestions { get; } = new ObservableCollection<string>();
            public ObservableCollection<string> TargetSuggestions { get; } = new ObservableCollection<string>();

            public ObservableCollection<string> LookupCollection(string field) => field switch
            {
                "REQUESTER" => RequesterSuggestions,
                "DEPT" => DeptSuggestions,
                "REMARK" => RemarkSuggestions,
                _ => TargetSuggestions
            };

            // กรองรายการแนะนำตามที่พิมพ์ (คำที่อยู่ตรงไหนของข้อความก็เจอ)
            public async Task UpdateLookup(string field, string text)
            {
                try
                {
                    var data = await Task.Run(() => _prService.GetLookupValues(field, text));
                    var target = LookupCollection(field);
                    if (target.SequenceEqual(data)) return;
                    target.Clear();
                    foreach (var v in data) target.Add(v);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PR lookup error: {ex.Message}");
                }
            }

            public async Task LoadAllLookups()
            {
                foreach (var f in new[] { "REQUESTER", "DEPT", "REMARK", "TARGET" }) await UpdateLookup(f, "");
            }

            // รีเฟรชตารางเบื้องหลัง (รายการแสกนออกเข้ามาใหม่) - วาดใหม่เฉพาะตอนข้อมูลเปลี่ยน และคงรายการที่ติ๊กไว้
            private string _lastSignature;
            public async Task RefreshIfChanged(string search)
            {
                var data = await Task.Run(() => _prService.GetPRList(search ?? ""));
                string sig = string.Join("|", data.Select(x => $"{x.PR_NO}:{x.Status}:{x.QTY}"));
                if (sig == _lastSignature) return;
                _lastSignature = sig;

                var selected = new HashSet<string>(PRHistory.Where(x => x.IsSelected).Select(x => x.PR_NO));
                PRHistory.Clear();
                foreach (var item in data)
                {
                    item.IsSelected = selected.Contains(item.PR_NO);
                    PRHistory.Add(item);
                }
            }

            private UserSession _currentUser;
            public UserSession CurrentUser
            {
                get => _currentUser;
                set { _currentUser = value; OnPropertyChanged(); OnPropertyChanged(nameof(LoginUserName)); }
            }

            public string LoginUserName => CurrentUser?.UserName ?? "Unknown User";
            public string CurrentDateDisplay => DateTime.Now.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

            // ค่าคงที่ตามที่นนท์กำหนด
            public string FixedDept => "41304134-บำรุงรักษาแม่พิมพ์";
            public string FixedRemark => "ซ่อมแม่พิมพ์/ประตู2B";
            public string FixedTarget => "ชนนิกานต์";

        #region === [ Load Data Function ] ===

        protected void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            public async Task LoadAllPR(string search = "")
            {
                try
                {
                    var data = await Task.Run(() => _prService.GetPRList(search));
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PRHistory.Clear();
                        foreach (var item in data) PRHistory.Add(item);
                    });
                    _lastSignature = string.Join("|", data.Select(x => $"{x.PR_NO}:{x.Status}:{x.QTY}"));
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError("โหลดข้อมูล PR ไม่สำเร็จ: " + ex.Message);
                }
            }

        #endregion

        #region === [ Save Function ] ===

        // requester / dept / remark / target = ค่าที่ผู้ใช้พิมพ์หรือเลือก (ว่าง = ใช้ค่าเริ่มต้นเดิม)
        public async Task<bool> SavePRToDb(PRModel item, string requester = null, string dept = null, string remark = null, string target = null)
            {
                string reqName = string.IsNullOrWhiteSpace(requester) ? LoginUserName : requester.Trim();
                string useDept = string.IsNullOrWhiteSpace(dept) ? FixedDept : dept.Trim();
                string useRemark = string.IsNullOrWhiteSpace(remark) ? FixedRemark : remark.Trim();
                string useTarget = string.IsNullOrWhiteSpace(target) ? FixedTarget : target.Trim();

                // 1. เช็คว่า Login หรือยัง
                if (CurrentUser == null || string.IsNullOrEmpty(CurrentUser.UserId))
                {
                    DialogHelper.ShowWarning("กรุณาล็อกอินก่อนทำรายการ");
                    return false;
                }

                // 2. เช็ค Level (ถ้า Level > 2 คือไม่มีสิทธิ์สร้าง PR)
                if (!CurrentUser.CanSubmitPR)
                {
                    DialogHelper.ShowWarning("คุณไม่มีสิทธิ์สร้างรายการ PR");
                    return false;
                }

                try
                {
                    string finalPRNo = "";
                    bool success = await Task.Run(() =>
                    {
                        finalPRNo = _prService.GetNextPRNo();
                        item.PR_DATE = DateTime.Now;
                        bool ok = _prService.InsertPR(finalPRNo, CurrentUser.UserId, useDept, item.PartName, item.QTY, useRemark, useTarget, reqName);
                        if (ok)
                        {
                            // จำค่าที่พิมพ์ไว้แนะนำครั้งต่อไป
                            _prService.SaveLookupValues(new Dictionary<string, string>
                            {
                                ["REQUESTER"] = reqName, ["DEPT"] = useDept, ["REMARK"] = useRemark, ["TARGET"] = useTarget
                            });
                        }
                        return ok;
                    });

                    if (success)
                    {
                        await LoadAllLookups();
                        LogService.WriteLog(CurrentUser.UserId, "CREATE_PR", $"Issued PR | NO: {finalPRNo}", finalPRNo);
                        await LoadAllPR();
                    }
                    return success;
                }
                catch (Exception ex)
                {
                    DialogHelper.ShowError("บันทึก PR ไม่สำเร็จ: " + ex.Message);
                    return false;
                }
            }

        #endregion

        #region === [ Approve Function ] ===

        // qty = ยอดที่อนุมัติ / keepRemainder = ยอดที่เหลือสร้างเป็น PR ใหม่ (Waiting)
        public async Task ApprovePR(PRModel item, int qty, bool keepRemainder)
            {
                if (item == null || CurrentUser == null) return;
                if (!CurrentUser.CanApprovePR)
                {
                    DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ในการอนุมัติรายการนี้");
                    return;
                }

                try
                {
                    string uid = CurrentUser.UserId;
                    string newPr = await Task.Run(() => _prService.ApprovePR(item.PR_NO, qty, keepRemainder, uid));
                    string detail = qty == item.QTY ? $"Approved PR No: {item.PR_NO} | QTY: {qty}"
                                  : $"Approved PR No: {item.PR_NO} | QTY: {item.QTY}->{qty}" + (newPr != null ? $" | Remaining {item.QTY - qty} -> new PR {newPr} (Waiting)" : $" | Remaining {item.QTY - qty} not kept");
                    LogService.WritePRLog(uid, "APPROVE_PR", detail, item.PR_NO);
                    NotificationManager.Show("Approved", $"อนุมัติ {item.PR_NO} จำนวน {qty:N0} แล้ว" + (newPr != null ? $"\nยอดที่เหลือ {item.QTY - qty:N0} -> PR ใหม่ {newPr}" : ""), true);
                    await LoadAllPR();
                }
                catch (Exception ex) { DialogHelper.ShowError("อนุมัติไม่สำเร็จ: " + ex.Message); }
            }

        #endregion

        #region === [ Reject Function ] ===

        public async Task RejectPR(PRModel item)
            {
                if (item == null || CurrentUser == null) return;
                if (!CurrentUser.CanApprovePR)
                {
                    DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ปฏิเสธรายการนี้");
                    return;
                }

                // คืนค่าการ Confirm ก่อนลบ/ปฏิเสธ
                bool isConfirm = DialogHelper.ShowConfirm($"คุณต้องการ Reject รายการ {item.PR_NO} ใช่หรือไม่?", "ยืนยัน");
                if (!isConfirm) return;

                try
                {
                    bool success = await Task.Run(() => _prService.UpdatePRStatus(item.PR_NO, "Rejected", CurrentUser.UserId));
                    if (success)
                    {
                        // ใช้ WritePRLog เหมือนของเดิม
                        LogService.WritePRLog(CurrentUser.UserId, "REJECT_PR", $"Rejected PR No: {item.PR_NO}", item.PR_NO);
                        await LoadAllPR();
                    }
                }
                catch (Exception ex) { DialogHelper.ShowError("Reject ไม่สำเร็จ: " + ex.Message); }
            }

        #endregion

        #region === [ Export Function ] ===
        public async Task<bool> ProcessExportAsync(IEnumerable<PRModel> items)
            {
                if (CurrentUser == null || items == null || !items.Any()) return false;
                if (!CurrentUser.CanApprovePR)
                {
                    DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ส่งออกข้อมูล");
                    return false;
                }

                try
                {
                    // 1. Export File
                    await Task.Run(() => _exportService.ExportPRToExcel(items));

                    // 2. Update DB & Log
                    await Task.Run(() =>
                    {
                        foreach (var item in items)
                        {
                            if (_prService.UpdateAfterExport(item.PR_NO, CurrentUser.UserId))
                            {
                                LogService.WriteLog(CurrentUser.UserId, "EXPORT_PR", $"Exported PR No: {item.PR_NO}", item.PR_NO);
                            }
                        }
                    });

                    await LoadAllPR();
                    return true;
                }
                catch (Exception ex)
                {
                    string msg = ex.Message.Contains("being used") ? "กรุณาปิดไฟล์ Excel ก่อน Export รายการ" : ex.Message;
                    DialogHelper.ShowError("Export ไม่สำเร็จ: " + msg);
                    return false;
                }
            }

        #endregion

        #region === [ Search & Suggestions ] ===

        public async Task UpdateSuggestions(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    Application.Current.Dispatcher.Invoke(() => ProductSuggestions.Clear());
                    return;
                }

                var data = await Task.Run(() => _prService.GetProductSuggestions(text));

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // ป้องกัน UI กระพริบ: ถ้าได้รายการเดิมไม่ต้องวาดใหม่
                    if (data.SequenceEqual(ProductSuggestions)) return;

                    ProductSuggestions.Clear();
                    foreach (var item in data) ProductSuggestions.Add(item);
                });
            }

        #endregion

        #region === [ Select All Function ] ===

        public void ToggleSelectAll(bool isSelected)
            {
                foreach (var item in PRHistory) item.IsSelected = isSelected;
            }
        }

        #endregion
}