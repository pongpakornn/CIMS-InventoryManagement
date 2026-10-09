//using System;
//using System.ComponentModel;
//using System.Collections.ObjectModel;
//using System.Runtime.CompilerServices;
//using CIMS.Models;
//using CIMS.Services;
//using LiveCharts;
//using LiveCharts.Wpf;
//using System.Windows.Media;

//namespace CIMS.ViewModels
//{
//    public class DashboardViewModel : INotifyPropertyChanged
//    {
//        private readonly DashboardService _dashboardService;

//        // ข้อมูลหลักสำหรับ Binding เข้าคุม Control
//        public ObservableCollection<DashboardSummaryCard> Cards { get; set; }

//        // ตัวแปรสำหรับคุมระบบชาร์ตของ LiveCharts
//        public SeriesCollection PieSeriesCollection { get; set; }
//        public SeriesCollection BarSeriesCollection { get; set; }
//        public string[] ChartLabels { get; set; }
//        public Func<double, string> ValuesFormatter { get; set; }

//        private string _currentMonthYearLabel;
//        public string CurrentMonthYearLabel
//        {
//            get => _currentMonthYearLabel;
//            set { _currentMonthYearLabel = value; OnPropertyChanged(); }
//        }

//        public DashboardViewModel()
//        {
//            _dashboardService = new DashboardService();
//            CurrentMonthYearLabel = DateTime.Now.ToString("MMMM yyyy");

//            // 1. โหลดข้อมูลลงการ์ดสรุป
//            Cards = new ObservableCollection<DashboardSummaryCard>(_dashboardService.GetSummaryCards());

//            // 2. ประมวลผลและสร้างชาร์ตวงกลม (Pie Chart)
//            LoadPieChartData();

//            // 3. ประมวลผลและสร้างชาร์ตแท่งเปรียบเทียบ (Bar Chart)
//            LoadBarChartData();
//        }

//        //private void LoadPieChartData()
//        //{
//        //    PieSeriesCollection = new SeriesCollection();
//        //    var customerOrders = _dashboardService.GetMonthlyCustomerOrders();

//        //    foreach (var order in customerOrders)
//        //    {
//        //        PieSeriesCollection.Add(new PieSeries
//        //        {
//        //            Title = order.CustomerName,
//        //            Values = new ChartValues<double> { order.OrderAmount },
//        //            DataLabels = true,
//        //            LabelPoint = chartPoint => $"{chartPoint.Y} ({chartPoint.Participation:P0})",
//        //            Fill = (Brush)new BrushConverter().ConvertFromString(order.HexColor)
//        //        });
//        //    }
//        //}
//        private void LoadPieChartData()
//        {
//            PieSeriesCollection = new SeriesCollection();

//            // 🚀 เรียกดึงข้อมูลจาก Service ปกติ (กรองเดือนปัจจุบันจาก SQL View แล้ว)
//            var customerOrders = _dashboardService.GetMonthlyCustomerOrders();

//            foreach (var order in customerOrders)
//            {
//                PieSeriesCollection.Add(new PieSeries
//                {
//                    Title = order.CustomerName,
//                    Values = new ChartValues<double> { order.OrderAmount },
//                    DataLabels = true,

//                    // 🍰 ตัด InnerRadius ออกแล้ว เหลือแค่ PushOut เพื่อดันชิ้นเค้กให้แยกกันสวย ๆ
//                    PushOut = 2,

//                    LabelPoint = chartPoint => $"{chartPoint.Y:N0} ({chartPoint.Participation:P0})",
//                    Fill = (Brush)new BrushConverter().ConvertFromString(order.HexColor)
//                });
//            }
//        }

//        // แสดงตามเดือนที่ import Forecast Order Automation
//        //private void LoadBarChartData()
//        //{
//        //    var forecastOrders = _dashboardService.GetYearlyForecastOrder();

//        //    var forecastValues = new ChartValues<double>();
//        //    var orderValues = new ChartValues<double>();
//        //    var labels = new string[forecastOrders.Count];

//        //    for (int i = 0; i < forecastOrders.Count; i++)
//        //    {
//        //        forecastValues.Add(forecastOrders[i].ForecastValue);
//        //        orderValues.Add(forecastOrders[i].OrderValue);
//        //        labels[i] = forecastOrders[i].MonthName;
//        //    }

//        //    BarSeriesCollection = new SeriesCollection
//        //    {
//        //        new ColumnSeries
//        //        {
//        //            Title = "Forecast",
//        //            Values = forecastValues,
//        //            // #D1C4E9 = ชอบสีนี้สีม่วงลาเวนเดอร์พาสเทล #B39DDB = เข้มกว่าเดิมสีม่วงสว่างแนวโมเดิร์น
//        //            // #90CAF9 = สีฟ้าอ่อนพาสเทล
//        //            Fill = (Brush)new BrushConverter().ConvertFromString("#90CAF9")
//        //        },
//        //        new ColumnSeries
//        //        {
//        //            Title = "Order",
//        //            Values = orderValues,
//        //            Fill = (Brush)new BrushConverter().ConvertFromString("#6A1B9A")
//        //        }
//        //    };

//        //    ChartLabels = labels;
//        //    ValuesFormatter = value => value.ToString("N0"); // ฟอร์แมตแสดงคอมม่าเช่น 1,000,000
//        //}
//        private void LoadBarChartData()
//        {
//            var forecastOrders = _dashboardService.GetYearlyForecastOrder();

//            var forecastValues = new ChartValues<double>();
//            var orderValues = new ChartValues<double>();
//            var deliveryValues = new ChartValues<double>(); // 🚀 1. สร้างชุดข้อมูลแท่งใหม่
//            var labels = new string[forecastOrders.Count];

//            for (int i = 0; i < forecastOrders.Count; i++)
//            {
//                forecastValues.Add(forecastOrders[i].ForecastValue);
//                orderValues.Add(forecastOrders[i].OrderValue);
//                deliveryValues.Add(forecastOrders[i].DeliveryValue); // 🚀 2. แอดข้อมูลจริงใส่ List
//                labels[i] = forecastOrders[i].MonthName;
//            }

//            BarSeriesCollection = new SeriesCollection
//    {
//        new ColumnSeries
//        {
//            Title = "Forecast",
//            Values = forecastValues,
//            Fill = (Brush)new BrushConverter().ConvertFromString("#90CAF9") // สีฟ้าอ่อนพาสเทลเดิม
//        },
//        new ColumnSeries
//        {
//            Title = "Order",
//            Values = orderValues,
//            Fill = (Brush)new BrushConverter().ConvertFromString("#6A1B9A") // สีม่วงสว่างเดิม
//        },
//        new ColumnSeries
//        {
//            Title = "Delivery", // 🚀 3. เพิ่มแท่งจัดส่งจริงเข้าไปในกราฟ
//            Values = deliveryValues,
//            // เลือกใช้สีเขียวโมเดิร์นพาสเทล (#4DB6AC) ให้ดูเป็นยอดที่เสร็จสิ้นสมบูรณ์ สบายตาและตัดกับสีเดิมได้ดีครับ
//            Fill = (Brush)new BrushConverter().ConvertFromString("#B39DDB")
//        }
//    };

//            ChartLabels = labels;
//            ValuesFormatter = value => value.ToString("N0");
//        }


//        // แบบแสดงกราฟพร้อมกัน 12 แท่ง
//        //    private void LoadBarChartData()
//        //    {
//        //        // ดึงข้อมูลจาก Service (ซึ่งจะได้รับข้อมูลครบ 12 แถวเสมอ)
//        //        var forecastOrders = _dashboardService.GetYearlyForecastOrder();

//        //        var forecastValues = new ChartValues<double>();
//        //        var orderValues = new ChartValues<double>();
//        //        var labels = new string[forecastOrders.Count];

//        //        for (int i = 0; i < forecastOrders.Count; i++)
//        //        {
//        //            forecastValues.Add(forecastOrders[i].ForecastValue);
//        //            orderValues.Add(forecastOrders[i].OrderValue);
//        //            labels[i] = forecastOrders[i].MonthName; // จะได้ค่า 'Jan', 'Feb', 'Mar' ตามลำดับจาก DB
//        //        }

//        //        BarSeriesCollection = new SeriesCollection
//        //{
//        //    new ColumnSeries
//        //    {
//        //        Title = "Forecast",
//        //        Values = forecastValues,
//        //        Fill = (Brush)new BrushConverter().ConvertFromString("#CBD5E0")
//        //    },
//        //    new ColumnSeries
//        //    {
//        //        Title = "Order",
//        //        Values = orderValues,
//        //        Fill = (Brush)new BrushConverter().ConvertFromString("#6A1B9A")
//        //    }
//        //};

//        //        ChartLabels = labels; // ส่งแกน X ขนาด 12 เดือนไปให้ UI
//        //        ValuesFormatter = value => value.ToString("N0");
//        //    }


//        public event PropertyChangedEventHandler PropertyChanged;
//        protected void OnPropertyChanged([CallerMemberName] string name = null)
//        {
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
//        }
//    }
//}


using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Windows;
using CIMS.Models;
using CIMS.Services;
using System.Collections.Generic;
using LiveCharts;
using LiveCharts.Wpf;
using System.Windows.Media;
using System.Linq; // เพิ่มเข้ามาเพื่อช่วยค้นหาข้อมูลได้ง่ายขึ้น

namespace CIMS.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly DashboardService _dashboardService;
        private DispatcherTimer _refreshTimer;

        public ObservableCollection<DashboardSummaryCard> Cards { get; set; }
        public SeriesCollection PieSeriesCollection { get; set; }
        public SeriesCollection BarSeriesCollection { get; set; }
        public string[] ChartLabels { get; set; }
        public Func<double, string> ValuesFormatter { get; set; }

        private string _currentMonthYearLabel;
        public string CurrentMonthYearLabel
        {
            get => _currentMonthYearLabel;
            set { _currentMonthYearLabel = value; OnPropertyChanged(); }
        }

        public DashboardViewModel()
        {
            _dashboardService = new DashboardService();
            CurrentMonthYearLabel = DateTime.Now.ToString("MMMM yyyy");

            // 🏬 การ์ด 4 ใบแยกตามคลัง (ข้อมูลโหลดหลังรู้ผู้ใช้ SetUser - แสดงเฉพาะคลังที่ผู้ใช้มีสิทธิ์ดู)
            Cards = new ObservableCollection<DashboardSummaryCard>
            {
                new DashboardSummaryCard { Kind = "STOCK",   Title = "Stock",       Value = "-", Hint = "RIGHT CLICK : OPEN A STOCK",        GifPath = "pack://application:,,,/Assets/Gifs/GroupCastomer.gif" },
                new DashboardSummaryCard { Kind = "PRODUCT", Title = "Product All", Value = "-", Hint = "RIGHT CLICK : VIEW PRODUCTS",       GifPath = "pack://application:,,,/Assets/Gifs/Product.gif" },
                new DashboardSummaryCard { Kind = "MAX",     Title = "Max Product", Value = "-", Hint = "RIGHT CLICK : VIEW MAX ITEMS",      GifPath = "pack://application:,,,/Assets/Gifs/Forklift.gif" },
                new DashboardSummaryCard { Kind = "MIN",     Title = "Min Product", Value = "-", Hint = "RIGHT CLICK : VIEW MIN ITEMS",      GifPath = "pack://application:,,,/Assets/Gifs/Alert.gif" }
            };

            // โหลดข้อมูลชาร์ตต่างๆ
            LoadPieChartData();
            LoadBarChartData();

            // 🚀 เริ่มทำงานระบบจับเวลาอัปเดตอัตโนมัติ
            SetupRefreshTimer();
        }

        #region === [ Function : Stock Cards ] ===

        private UserSession _user;
        public string LastUpdateText { get => _lastUpdate; set { _lastUpdate = value; OnPropertyChanged(); } }
        private string _lastUpdate = "";

        // เรียกจาก DashboardView หลังรู้ผู้ใช้ -> โหลดการ์ดครั้งแรก
        public async void SetUser(UserSession user)
        {
            _user = user;
            await LoadStockCardsAsync();
        }

        private async System.Threading.Tasks.Task LoadStockCardsAsync()
        {
            List<StockDashboardSummary> data;
            try
            {
                data = await System.Threading.Tasks.Task.Run(() => _dashboardService.GetStockSummaries());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dashboard stock summary error: {ex.Message}");
                return;
            }

            // เห็นเฉพาะคลังที่มีสิทธิ์ (ไม่รู้ผู้ใช้ = เห็นทุกคลัง)
            var visible = data.Where(d => _user == null || _user.CanViewStock(d.Stock)).ToList();

            foreach (var card in Cards)
            {
                Func<StockDashboardSummary, int> count = card.Kind == "MAX" ? (d => d.MaxCount)
                                                       : card.Kind == "MIN" ? (d => d.MinCount)
                                                       : (d => d.Items);
                card.Value = card.Kind == "STOCK" ? visible.Count.ToString("N0") : visible.Sum(count).ToString("N0");

                // อัปเดตแถวในที่เดิม (ไม่ล้างทั้งรายการ หน้าจอจะไม่กระพริบทุก 10 วินาที)
                var ids = visible.Select(v => v.Stock.StkId).ToList();
                for (int i = card.Rows.Count - 1; i >= 0; i--)
                    if (!ids.Contains(card.Rows[i].Stock.StkId)) card.Rows.RemoveAt(i);

                for (int i = 0; i < visible.Count; i++)
                {
                    var v = visible[i];
                    var row = card.Rows.FirstOrDefault(r => r.Stock.StkId == v.Stock.StkId);
                    if (row == null) { row = new DashboardStockRow { Stock = v.Stock }; card.Rows.Insert(Math.Min(i, card.Rows.Count), row); }
                    else row.Stock = v.Stock;
                    row.Count = count(v);
                    row.Right = card.Kind == "STOCK" ? v.Stock.Name : row.Count.ToString("N0");
                }
            }
            LastUpdateText = "LAST UPDATE " + DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }

        #endregion

        #region === [ Function : Auto-Refresh Data or Update Data to Realtime ] ===

        private void SetupRefreshTimer()
        {
            _refreshTimer = new DispatcherTimer();
            // 🕒 เดิมตั้ง 0 วินาที = ยิง Query วนไม่หยุดบน UI Thread (แม้ออกจากหน้า Dashboard แล้ว) ทำให้ระบบค้าง
            // ~30 วินาทีเวลามีการบันทึกข้อมูล CIMS.Parts พร้อมกัน -> รีเฟรชทุก 5 วินาที (Query เบื้องหลัง ไม่ขวางหน้าจอ)
            _refreshTimer.Interval = TimeSpan.FromSeconds(3);
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        // หยุดรีเฟรชเมื่อออกจากหน้า Dashboard (เรียกจาก DashboardView.Unloaded) / เปิดกลับมาเริ่มใหม่
        public void StopRefresh() => _refreshTimer?.Stop();
        public void StartRefresh() { if (_refreshTimer != null && !_refreshTimer.IsEnabled) _refreshTimer.Start(); }

        private bool _isRefreshing;
        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            try
            {
                // ดึงข้อมูลเบื้องหลัง ไม่ให้ UI Thread ต้องรอฐานข้อมูล (รอ Lock ได้โดยหน้าจอไม่ค้าง)
                await LoadStockCardsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dashboard refresh error: {ex.Message}");
            }
            finally { _isRefreshing = false; }
        }

        /// <summary>
        /// ฟังก์ชันศูนย์กลางในการเจาะจงอัปเดตเฉพาะตัวเลข Max และ Min Product แบบไม่ให้หน้าจอกระตุก
        /// </summary>
        private void RefreshDashboardData(List<DashboardSummaryCard> freshCards)
        {
            try
            {
                if (freshCards == null || Cards == null) return;

                // ดึง Dispatcher ของแอปพลิเคชันหลักมาเตรียมอัปเดต UI
                var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

                foreach (var currentCard in Cards)
                {
                    if (currentCard.Title == null) continue;

                    string titleTrimmed = currentCard.Title.Trim();

                    if (titleTrimmed.Equals("Max Product", StringComparison.OrdinalIgnoreCase))
                    {
                        var freshData = freshCards.FirstOrDefault(c => c.Title != null && c.Title.Trim().Equals("Max Product", StringComparison.OrdinalIgnoreCase));
                        if (freshData != null && currentCard.Value != freshData.Value)
                        {
                            // บังคับอัปเดตผ่าน UI Thread เพื่อความชัวร์สูงสุด
                            dispatcher.Invoke(() => {
                                currentCard.Value = freshData.Value;
                            });
                        }
                    }
                    else if (titleTrimmed.Equals("Min Product", StringComparison.OrdinalIgnoreCase))
                    {
                        var freshData = freshCards.FirstOrDefault(c => c.Title != null && c.Title.Trim().Equals("Min Product", StringComparison.OrdinalIgnoreCase));
                        if (freshData != null && currentCard.Value != freshData.Value)
                        {
                            dispatcher.Invoke(() => {
                                currentCard.Value = freshData.Value;
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Dashboard Auto-Refresh Error]: {ex.Message}");
            }
        }

        #endregion

        #region === [ Function : Graph Circle & Stack ] ===

        // กราฟวงกลมแสดงสัดส่วนลูกค้าแสดง Order เฉพาะเดือนปัจจุบัน
        private void LoadPieChartData()
        {
            PieSeriesCollection = new SeriesCollection();
            var customerOrders = _dashboardService.GetMonthlyCustomerOrders();

            foreach (var order in customerOrders)
            {
                PieSeriesCollection.Add(new PieSeries
                {
                    Title = order.CustomerName,
                    Values = new ChartValues<double> { order.OrderAmount },
                    DataLabels = true,
                    PushOut = 2,
                    LabelPoint = chartPoint => $"{chartPoint.Y:N0} ({chartPoint.Participation:P0})",
                    Fill = (Brush)new BrushConverter().ConvertFromString(order.HexColor)
                });
            }
            OnPropertyChanged(nameof(PieSeriesCollection));
        }

        // กราฟแท่งแสดงการเทียบยอด Forecast, Order และ Delivery แสดงตามเดือนที่ import Forecast Order Automation
        private void LoadBarChartData()
        {
            // ทั้งปี (Jan - Dec): แท่ง Order คู่ Delivery + Forecast เป็นจุดวงกลมเล็กคู่กับแท่ง Order ของเดือนนั้น
            var data = _dashboardService.GetYearlyForecastOrder();
            var forecastValues = new ChartValues<double>();
            var orderValues = new ChartValues<double>();
            var deliveryValues = new ChartValues<double>();
            var labels = new string[12];
            for (int m = 1; m <= 12; m++)
            {
                var row = data.FirstOrDefault(d => d.MonthOrder == m);
                forecastValues.Add(row?.ForecastValue ?? 0);
                orderValues.Add(row?.OrderValue ?? 0);
                deliveryValues.Add(row?.DeliveryValue ?? 0);
                labels[m - 1] = new DateTime(2000, m, 1).ToString("MMM", System.Globalization.CultureInfo.InvariantCulture);
            }

            BarSeriesCollection = new SeriesCollection
            {
                new ColumnSeries { Title = "Order", Values = orderValues, Fill = (Brush)new BrushConverter().ConvertFromString("#6A1B9A"), MaxColumnWidth = 26 },
                new ColumnSeries { Title = "Delivery", Values = deliveryValues, Fill = (Brush)new BrushConverter().ConvertFromString("#B39DDB"), MaxColumnWidth = 26 },
                new LineSeries
                {
                    Title = "Forecast", Values = forecastValues, Stroke = Brushes.Transparent, Fill = Brushes.Transparent,
                    PointGeometry = DefaultGeometries.Circle, PointGeometrySize = 11, LineSmoothness = 0,
                    PointForeground = (Brush)new BrushConverter().ConvertFromString("#1E88E5")
                }
            };

            // แกนสูงสุดปรับอัตโนมัติ: เผื่อ ~10% แล้วปัดขึ้นเป็นเลขกลมๆ (เช่น 1,230,000 -> 1,400,000)
            double top = new[] { forecastValues.DefaultIfEmpty(0).Max(), orderValues.DefaultIfEmpty(0).Max(), deliveryValues.DefaultIfEmpty(0).Max() }.Max();
            AxisMax = NiceCeiling(top * 1.1);
            OnPropertyChanged(nameof(AxisMax));

            ChartLabels = labels;
            ValuesFormatter = value => value.ToString("N0");

            OnPropertyChanged(nameof(BarSeriesCollection));
            OnPropertyChanged(nameof(ChartLabels));
        }

        // ค่าสูงสุดของแกน Y (null = ให้กราฟคำนวณเอง ตอนยังไม่มีข้อมูล)
        public double? AxisMax { get; set; }

        private static double? NiceCeiling(double v)
        {
            if (v <= 0) return null;
            double step = Math.Pow(10, Math.Floor(Math.Log10(v)) - 1);   // 1,353,000 -> step 100,000
            return Math.Ceiling(v / step) * step;
        }

        #endregion

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}