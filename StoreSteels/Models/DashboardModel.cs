using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CIMS.Models
{
    public class DashboardSummaryCard : INotifyPropertyChanged
    {
        private string _title;
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(); // 🚀 เพิ่มตัวนี้เข้ามาสะกิด DataTrigger ของ XAML ด้วยครับ
                }
            }
        }
        public string GifPath { get; set; }

        // การ์ดแยกตามคลัง: STOCK / PRODUCT / MAX / MIN - แถวละ 1 คลัง (คลิกขวาเลือกคลังเข้าไปดู)
        public string Kind { get; set; }
        public string Hint { get; set; }
        public ObservableCollection<DashboardStockRow> Rows { get; } = new ObservableCollection<DashboardStockRow>();

        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(); // 🚀 ส่งสัญญาณบอก TextBlock บน XAML ให้เปลี่ยนเลขทันที
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // 1 คลังบนการ์ด Dashboard
    public class DashboardStockRow : INotifyPropertyChanged
    {
        public StockModel Stock { get; set; }
        public string Code => Stock?.Code;
        public string Name => Stock?.Name;

        private string _right;
        public string Right { get => _right; set { if (_right != value) { _right = value; OnPropertyChanged(); } } }

        public int Count { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ยอดสรุปต่อคลัง (CIMS.vw_StockMonitoring)
    public class StockDashboardSummary
    {
        public StockModel Stock { get; set; }
        public int Items { get; set; }
        public int MaxCount { get; set; }
        public int MinCount { get; set; }
    }

    public class CustomerOrderShare
    {
        public string CustomerName { get; set; }
        public double OrderAmount { get; set; }
        public double Percentage { get; set; }
        public string HexColor { get; set; }
    }

    public class MonthlyForecastOrderCompare
    {
        public string MonthName { get; set; }
        public double ForecastValue { get; set; }
        public double OrderValue { get; set; }

        public double DeliveryValue { get; set; } // 🚀 แท่งใหม่ที่เพิ่มเข้ามา
        public int MonthOrder { get; set; }
    }
}