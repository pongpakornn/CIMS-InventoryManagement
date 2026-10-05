using CIMS.Helpers;
using CIMS.Services;
using CIMS.Models;
using CIMS.Converters;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    public partial class MainView : Window
    {
        public UserSession CurrentUser { get; private set; }                                                                // ประกาศเรียกใช้งาน Models
        private AuthService _authService = new AuthService();                                                               // ประกาศเรียกใช้งาน Service
        public static readonly DependencyProperty IsSidebarOpenProperty =                                                   // ใช้ DependencyProperty เพื่อให้รองรับ Binding ใน XAML
            DependencyProperty.Register("IsSidebarOpen", typeof(bool), typeof(MainView), new PropertyMetadata(true));

        public bool IsSidebarOpen
        {
            get { return (bool)GetValue(IsSidebarOpenProperty); }
            set { SetValue(IsSidebarOpenProperty, value); }
        }

        #region === [ MainView ] ===
        public MainView(UserSession session)        // <-- รับ UserSession มาจาก LoginView
        {
            InitializeComponent();
            // หน้าใหม่ (ทั้งจากเมนูและจากในหน้า เช่น การ์ดคลัง -> ตาราง) เริ่มที่มุมซ้ายบนเสมอ (จอเล็กที่เลื่อนหน้าได้)
            MainFrame.Navigated += (s, e) => PageScroll.ScrollToHome();
            this.CurrentUser = session;             // เก็บข้อมูลคน Login
            this.DataContext = this;                // ทำให้ Binding {Binding CurrentUser.UserLevel} ทำงานได้
            this.Closing += MainView_Closing;       // [เพิ่ม] ดักจับการปิด Window ทุกกรณีรวมถึงกด X เพื่อให้ UserID อัพเดทสถานะเป็น Offline เสมอ

            #region === [ Initial Navigation Logic : ตัดสินใจนำทางไปยังหน้าแรกที่เหมาะสมตามสิทธิ์การใช้งาน ตัวเก่าแบบไม่มีเงื่อนไข ] ===
            // เริ่มต้นที่หน้าแรก
            //อันเก่าที่ไม่มีการกำหนดสิทธิ์การใช้งาน
            //NavigateToPage(new DashboardView(this.CurrentUser), "DASHBOARD"); 
            #endregion

            #region === [ Initial Navigation Logic : ตัดสินใจนำทางไปยังหน้าแรกที่เหมาะสมตามสิทธิ์การใช้งาน ตัวใหม่แบบมีเงื่อนไข ] ===

            //// 1. ถ้าเป็น Admin (Level 1, 2) ให้ไปหน้า Dashboard ตามปกติ
            //if (CurrentUser.UserLevel == 1 || CurrentUser.UserLevel == 2)
            //{
            //    NavigateToPage(new DashboardView(this.CurrentUser), "DASHBOARD");
            //}
            //// 2. ถ้าเป็นพนักงาน ScanIn (และมีสิทธิ์) ให้ไปหน้า ScanIn เลย
            //else if (CurrentUser.CanViewScanIn && !CurrentUser.CanViewScanOut)
            //{
            //    NavigateToPage(new ScanInView(this.CurrentUser), "MULTI-SCANNER ( IN )");
            //}
            //// 3. ถ้าเป็นพนักงาน ScanOut (และมีสิทธิ์) ให้ไปหน้า ScanOut เลย
            //else if (CurrentUser.CanViewScanOut && !CurrentUser.CanViewScanIn)
            //{
            //    NavigateToPage(new ScanOutView(this.CurrentUser), "MULTI-SCANNER ( OUT )");
            //}
            //// 4. กรณีอื่นๆ (เผื่อไว้)
            //else
            //{
            //    NavigateToPage(new DashboardView(this.CurrentUser), "DASHBOARD");
            //}
            if (CurrentUser.CanViewDashboard)
            {
                NavigateToPage(new DashboardView(this.CurrentUser), "DASHBOARD");
            }
            // มีสิทธิ์สแกน (IN และ/หรือ OUT) -> เปิดหน้า Multi-Scanner หน้าเดียว (หน้านี้จัดการเองว่าเริ่มที่
            // รับเข้า หรือบังคับสแกนออก ถ้าผู้ใช้มีสิทธิ์แค่ OUT)
            else if (CurrentUser.CanViewScannerMenu)
            {
                NavigateToPage(new ScanInView(this.CurrentUser), "MULTI-SCANNER");
            }
            // 🆕 มีสิทธิ์ ProductControl
            else if (CurrentUser.CanViewProductControl)
            {
                NavigateToPage(new ProductControlView(this.CurrentUser), "INVENTORY REGISTRATION");
            }
            else if (CurrentUser.CanViewStore)
            {
                NavigateToPage(new StockCardsView(this.CurrentUser), "STORE ( MAX-MIN )");
            }
            else if (CurrentUser.CanViewPackingCard)
            {
                NavigateToPage(new PackingCardView(this.CurrentUser), "PICK LIST");
            }
            else if (CurrentUser.CanViewPR)
            {
                NavigateToPage(new PRView(this.CurrentUser), "PURCHASE REQUEST");
            }
            else if (CurrentUser.CanViewMaxMinCalc)
            {
                NavigateToPage(new MaxMinCalculatorView(this.CurrentUser), "MAX-MIN CALCULATOR");
            }
            else if (CurrentUser.CanViewForecastOrder)
            {
                NavigateToPage(new ForecastOrderView(this.CurrentUser), "FORECAST / ORDER / DELIVERY");
            }
            else if (CurrentUser.CanViewActivityLog)
            {
                NavigateToPage(new ActivityLogView(this.CurrentUser), "ACTIVITY LOG");
            }
            else if (CurrentUser.CanViewUserMgmt)
            {
                NavigateToPage(new UserManagementView(this.CurrentUser), "USER MANAGEMENT");
            }
            // ไม่มีสิทธิ์อะไรเลย -> ไม่เปิดหน้าใด แจ้งเตือนให้ติดต่อผู้ดูแลระบบ
            else
            {
                txtPageTitle.Text = "NO PERMISSION";
                Loaded += (s, e) => DialogHelper.ShowWarning("บัญชีนี้ยังไม่ได้รับสิทธิ์ใช้งานระบบใดเลย กรุณาติดต่อผู้ดูแลระบบ (Level 1)");
            }

            #endregion

        }
        #endregion

        #region === [ Window Closing Event Handler : ออกสู่ระบบด้วย X Windows ] ===

        private void MainView_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // 1. เช็คว่ามี User ค้างอยู่จริงๆ ไหม
                if (this.CurrentUser != null && !string.IsNullOrEmpty(this.CurrentUser.UserId))
                {
                    // 2. เรียกใช้ Service อัปเดต (ตรวจสอบว่า UpdateLogoutStatus ไม่ได้เป็น async)
                    var auth = new AuthService();
                    auth.UpdateLogoutStatus(this.CurrentUser.UserId);

                    // 3. (Optional) เขียน Log ไว้หน่อยว่าปิดด้วยปุ่ม X
                    LogService.WriteLog(CurrentUser.UserId, "EXIT", "ผู้ใช้งานสั่งปิดโปรแกรมโดยตรง ( Window Closing )", "");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error on Closing: {ex.Message}");
            }
        }

        #endregion

        #region === [ Toggle Sidebar Animation : การหดเข้ากับการขยายออกของ Sidebar ] ===

        private void btnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _sidebarByUser = true;   // ผู้ใช้กดเอง -> ไม่พับ/กางอัตโนมัติตามขนาดจออีก
            ToggleSidebar();
        }

        private void ToggleSidebar()
        {
            // Responsive Logic: 75px สำหรับไอคอนอย่างเดียว, 260px สำหรับเมนูเต็ม
            double targetWidth = IsSidebarOpen ? 80 : 260;

            DoubleAnimation animation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            SidebarContainer.BeginAnimation(WidthProperty, animation);
            IsSidebarOpen = !IsSidebarOpen;
        }

        #endregion

        #region === [ Responsive : รองรับทุกขนาดหน้าจอ ] ===

        // พื้นที่ที่ทุกหน้าออกแบบไว้ (จอ Full HD ลบ Sidebar) - จอเล็กกว่านี้ย่อทั้งหน้าตามสัดส่วน
        private const double DesignWidth = 1600;
        private const double DesignHeight = 860;
        private const double MinScale = 0.72;          // ย่อได้ไม่ต่ำกว่านี้ (ตัวหนังสือยังอ่านได้) เล็กกว่านี้ให้เลื่อนแทน
        private const double SidebarAutoWidth = 1500;  // หน้าต่างแคบกว่านี้ -> พับ Sidebar ให้เหลือแต่ไอคอนเอง
        private bool _sidebarByUser;                   // ผู้ใช้กด ☰ เอง -> ไม่พับ/กางอัตโนมัติอีก
        private bool _sidebarAutoClosed;

        private void MainView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_sidebarByUser || !e.WidthChanged) return;
            bool narrow = ActualWidth < SidebarAutoWidth;
            if (narrow && IsSidebarOpen) { _sidebarAutoClosed = true; ToggleSidebar(); }
            else if (!narrow && !IsSidebarOpen && _sidebarAutoClosed) { _sidebarAutoClosed = false; ToggleSidebar(); }
        }

        private void PageScroll_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyPageScale();

        // จอเล็กที่ต้องเลื่อนหน้า: ไม่ให้หน้ากระโดดเลื่อนเองตอนช่องไหนได้ Focus / การ์ดที่เลื่อนวน (ผู้ใช้เลื่อนเอง)
        private void PageHost_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;

        private void ApplyPageScale()
        {
            double w = PageScroll.ActualWidth, h = PageScroll.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double s = Math.Min(1, Math.Max(MinScale, w / DesignWidth));
            // ต้องเลื่อนแนวตั้ง / แนวนอน -> เผื่อที่ของ Scrollbar ไม่ให้เกิดแถบเลื่อนซ้อนเพราะขาดไม่กี่ px
            bool needV = h / s < DesignHeight - 0.5;
            if (needV) { w -= SystemParameters.VerticalScrollBarWidth; s = Math.Min(1, Math.Max(MinScale, w / DesignWidth)); }
            bool needH = s < 1 && DesignWidth * s > w + 0.5;
            if (needH) { h -= SystemParameters.HorizontalScrollBarHeight; needV = h / s < DesignHeight - 0.5; }

            PageScale.ScaleX = PageScale.ScaleY = s;
            // แกนที่พอดีจอ: ปิดการเลื่อน หน้าขยายเต็มพื้นที่เอง (ไม่มี Scrollbar เกินมา)
            // แกนที่ไม่พอ: เลื่อนได้ และกำหนดขนาดหน้าตายตัว - ตารางในหน้าจึงยังสร้างแถวเฉพาะที่เห็น (Virtualization) ไม่ช้าลง
            PageScroll.HorizontalScrollBarVisibility = needH ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            PageScroll.VerticalScrollBarVisibility = needV ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            PageHost.Width = needH ? DesignWidth : double.NaN;
            PageHost.Height = needV ? DesignHeight : double.NaN;
        }

        #endregion

        #region === [ Navigation Helper : ฟังก์ชั่นกลางสำหรับการนำทางไปยังหน้าอื่นๆ ] ===

        public void NavigateToPage(object page, string title)
        {
            txtPageTitle.Text = title.ToUpper();
            MainFrame.Navigate(page);
            // 🕘 Activity Log: เปิดหน้าไหน (หน้าคลังบอกชื่อคลังด้วย)
            string detail = page is StoreMaxMinView sv && sv.StockCode != null ? $"{title.ToUpper()} | Stock: {sv.StockCode}" : title.ToUpper();
            LogService.WriteLog(CurrentUser?.UserId, "OPEN_PAGE", detail, "");
        }

        private void BtnUserMgmt_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewUserMgmt)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า User Management", false);
                return;
            }
            NavigateToPage(new UserManagementView(this.CurrentUser), "USER MANAGEMENT");
        }

        private void BtnActivityLog_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewActivityLog)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า Activity Log", false);
                return;
            }
            NavigateToPage(new ActivityLogView(this.CurrentUser), "ACTIVITY LOG");
        }

        #endregion

        #region Navigation Sidebar Events

        //private void BtnDashboard_Click(object sender, RoutedEventArgs e)
        // => NavigateToPage(new DashboardView(this.CurrentUser), "DASHBOARD");
        // ✅ เพิ่ม Guard Check สิทธิ์ ให้ Dashboard ด้วย (เดิมไม่มี)
        private void BtnDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewDashboard)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า Dashboard", false);
                return;
            }
            NavigateToPage(new DashboardView(this.CurrentUser), "DASHBOARD");
        }

        // Store ( Max - Min ) เริ่มที่หน้าการ์ดคลังทั้งหมด แล้วกดการ์ดเพื่อเข้าตารางของคลังนั้น
        private void BtnStore_Click(object sender, RoutedEventArgs e)
         => NavigateToPage(new StockCardsView(this.CurrentUser), "STORE ( MAX-MIN )");

        #region === [ ปุ่มสำหรับการใช้งานรูปแบบเก่า เเบบไม่กำหนดสิทธิ์หรือเงื่อนไขการใช้งาน ] ===

        //    private void BtnScanIn_Click(object sender, RoutedEventArgs e)
        //=> NavigateToPage(new ScanInView(this.CurrentUser), "MULTI-SCANNER ( IN )");

        //    private void BtnScanOut_Click(object sender, RoutedEventArgs e) 
        //=> NavigateToPage(new ScanOutView(this.CurrentUser), "MULTI-SCANNER ( OUT )");

        #endregion
        // Multi-Scanner หน้าเดียว: เข้ามาเป็นสแกนรับเข้า แล้วกดปุ่ม "สแกนออก" ในหน้าเพื่อสลับโหมด
        private void BtnMultiScanner_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewScannerMenu)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานระบบ Multi-Scanner", false);
                return;
            }
            NavigateToPage(new ScanInView(this.CurrentUser), "MULTI-SCANNER");
        }

        //private void BtnProductControl_Click(object sender, RoutedEventArgs e)
        // => NavigateToPage(new ProductControlView(this.CurrentUser), "INVENTORY REGISTRATION");
        // ✅ เพิ่ม Guard Check สิทธิ์
        private void BtnProductControl_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewProductControl)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า Inventory Registration", false);
                return;
            }
            NavigateToPage(new ProductControlView(this.CurrentUser), "INVENTORY REGISTRATION");
        }

        private void BtnPackingCard_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewPackingCard)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า Pick List", false);
                return;
            }
            NavigateToPage(new PackingCardView(this.CurrentUser), "PICK LIST");
        }

        #endregion

        #region === [ Reserved for Future Development : สำรองไว้เผื่อได้พัฒนาในอนาคต ] ===
        // สำรองไว้เผื่อได้พัฒนาในอนาคต
        private void BtnMaxMinCalc_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewMaxMinCalc)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า Max-Min Calculator", false);
                return;
            }
            NavigateToPage(new MaxMinCalculatorView(this.CurrentUser), "MAX-MIN CALCULATOR");
        }

        private void BtnForecastOrder_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewForecastOrder)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า Forecast / Order / Delivery", false);
                return;
            }
            NavigateToPage(new ForecastOrderView(this.CurrentUser), "FORECAST / ORDER / DELIVERY");
        }

        private void BtnPR_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentUser.CanViewPR)
            {
                NotificationManager.Show("Access Denied", "คุณไม่มีสิทธิ์เข้าใช้งานหน้า PR Management", false);
                return;
            }
            NavigateToPage(new PRView(this.CurrentUser), "PURCHASE REQUEST");
        }

        // สำรองไว้เผื่อได้พัฒนาในอนาคต
        //    private void BtnManageUser_Click(object sender, RoutedEventArgs e) 
        //=> NavigateToPage(new ManageUserView(), "USER ACCESS");

        // สำรองไว้เผื่อได้พัฒนาในอนาคต
        //private void btnProductControls_Click(object sender, RoutedEventArgs e)
        // => NavigateToPage(new ProductControlView(), "PRODUCT CONTROLS");

        #endregion

        #region === [ Logout Buttons : ปุ่มออกสู่ระบบเลิกใช้งาน ] ===

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            bool isConfirm = DialogHelper.ShowConfirm("คุณต้องการออกจากระบบใช่หรือไม่?", "CONFIRM LOGOUT");

            if (isConfirm)
            {
                _authService.UpdateLogoutStatus(CurrentUser.UserId);                          // 1. สั่ง Offline 
                LogService.WriteLog(CurrentUser.UserId, "LOGOUT", "ออกจากระบบสำเร็จ", "");       // 2. บันทึก Log การ Logout
                NotificationManager.Show("Logout", "ออกจากระบบสำเร็จ", true);                    // 3. แสดง Notification การ Logout
                this.IsHitTestVisible = false;
                await Task.Delay(800);                                                        // 4. กำหนด Delay เล็กน้อยเพื่อให้ Notification แสดงก่อนปิดหน้าต่าง
                new LoginView().Show();                                                       // 5. กลับไปหน้า Login
                this.Closing -= MainView_Closing;                                             // ถอด Event ออกชั่วคราวเพื่อไม่ให้รันซ้ำตอน Close()
                this.Close();
            }
        }

        #endregion
    }
}