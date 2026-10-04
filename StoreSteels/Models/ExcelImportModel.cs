using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CIMS.Models
{
    // 1. สำหรับไฟล์ Template_ForecastOrder.xlsx
    public class ExcelForecastOrderModel
    {
        public string Customer { get; set; }
        public string PartA { get; set; }
        public string PartName { get; set; }
        public int Forecast { get; set; }
        public int Order { get; set; }
        public int Delivery { get; set; }
        public int Workday { get; set; }

    }

    // 2. สำหรับไฟล์ Template_SetMaxMin.xlsx
    public class ExcelMaxMinModel
    {
        public string Customer { get; set; }
        public string PartNo { get; set; }
        public string PartName { get; set; }
        public int DayMax { get; set; }
        public int DayMin { get; set; }
        public int WorkDay { get; set; }
    }
}
