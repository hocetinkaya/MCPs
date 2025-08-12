using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;

namespace DateTimeMcpServer
{
    [McpServerToolType]
    public static class DateTimeTools
    {
        [McpServerTool]
        [Description("Şu anki tarih ve saat bilgisini verir")]
        public static string GetCurrentDateTime()
        {
            // 🔴 BREAKPOINT BURAYA KOYABİLİRSİN
            var isDebugMode = Environment.GetEnvironmentVariable("DEBUG_MODE") == "true";
            var now = DateTime.Now;
            
            if (isDebugMode)
            {
                Console.WriteLine($"[DEBUG] GetCurrentDateTime called at {now}");
                // Bu satırda breakpoint koy! ⚡
                return $"🔍 DEBUG: {now:dd.MM.yyyy HH:mm:ss} ({GetTurkishDayName(now.DayOfWeek)}) - Process: {System.Diagnostics.Process.GetCurrentProcess().Id}";
            }
            
            return $"Şu an: {now:dd.MM.yyyy HH:mm:ss} ({now.DayOfWeek} - {GetTurkishDayName(now.DayOfWeek)})";
        }

        [McpServerTool]
        [Description("Sadece bugünün tarihini verir")]
        public static string GetToday()
        {
            var today = DateTime.Today;
            return $"Bugün: {today:dd.MM.yyyy} - {GetTurkishDayName(today.DayOfWeek)}";
        }

        [McpServerTool]
        [Description("Sadece şu anki saati verir")]
        public static string GetCurrentTime()
        {
            var now = DateTime.Now;
            return $"Saat: {now:HH:mm:ss}";
        }

        [McpServerTool]
        [Description("Bu haftanın başlangıç ve bitiş tarihlerini verir")]
        public static string GetThisWeek()
        {
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek + 1); // Pazartesi
            var endOfWeek = startOfWeek.AddDays(6); // Pazar
            
            return $"Bu hafta: {startOfWeek:dd.MM.yyyy} - {endOfWeek:dd.MM.yyyy}";
        }

        [McpServerTool]
        [Description("Bu ayın başlangıç ve bitiş tarihlerini verir")]
        public static string GetThisMonth()
        {
            var today = DateTime.Today;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
            
            return $"Bu ay: {startOfMonth:dd.MM.yyyy} - {endOfMonth:dd.MM.yyyy} ({GetTurkishMonthName(today.Month)} {today.Year})";
        }

        [McpServerTool]
        [Description("İki tarih arasındaki gün farkını hesaplar")]
        public static string CalculateDaysBetween(
            [Description("Başlangıç tarihi (dd.MM.yyyy formatında)")] string startDate,
            [Description("Bitiş tarihi (dd.MM.yyyy formatında)")] string endDate)
        {
            try
            {
                var start = DateTime.ParseExact(startDate, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                var end = DateTime.ParseExact(endDate, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                var difference = (end - start).Days;
                
                return $"{startDate} ile {endDate} arasında {Math.Abs(difference)} gün var.";
            }
            catch (Exception ex)
            {
                return $"Tarih formatı hatası: {ex.Message}. Lütfen dd.MM.yyyy formatında girin.";
            }
        }

        [McpServerTool]
        [Description("Belirtilen gün sonrasının tarihini hesaplar")]
        public static string AddDaysToToday(
            [Description("Eklenecek gün sayısı")] int days)
        {
            var result = DateTime.Today.AddDays(days);
            var dayText = days > 0 ? "sonra" : "önce";
            
            return $"Bugünden {Math.Abs(days)} gün {dayText}: {result:dd.MM.yyyy} - {GetTurkishDayName(result.DayOfWeek)}";
        }

        [McpServerTool]
        [Description("Yılın kaçıncı günü olduğunu söyler")]
        public static string GetDayOfYear()
        {
            var today = DateTime.Today;
            return $"Bugün {today.Year} yılının {today.DayOfYear}. günü. Yıl sonuna {(DateTime.IsLeapYear(today.Year) ? 366 : 365) - today.DayOfYear} gün kaldı.";
        }

        [McpServerTool]
        [Description("Sistem ve UTC zamanını gösterir")]
        public static string GetSystemAndUtcTime()
        {
            var now = DateTime.Now;
            var utc = DateTime.UtcNow;
            
            return $"Sistem Zamanı: {now:dd.MM.yyyy HH:mm:ss}\n" +
                   $"UTC Zamanı: {utc:dd.MM.yyyy HH:mm:ss}\n" +
                   $"Zaman Dilimi: {TimeZoneInfo.Local.DisplayName}";
        }

        private static string GetTurkishDayName(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => "Pazartesi",
                DayOfWeek.Tuesday => "Salı",
                DayOfWeek.Wednesday => "Çarşamba",
                DayOfWeek.Thursday => "Perşembe",
                DayOfWeek.Friday => "Cuma",
                DayOfWeek.Saturday => "Cumartesi",
                DayOfWeek.Sunday => "Pazar",
                _ => dayOfWeek.ToString()
            };
        }

        private static string GetTurkishMonthName(int month)
        {
            return month switch
            {
                1 => "Ocak", 2 => "Şubat", 3 => "Mart", 4 => "Nisan",
                5 => "Mayıs", 6 => "Haziran", 7 => "Temmuz", 8 => "Ağustos",
                9 => "Eylül", 10 => "Ekim", 11 => "Kasım", 12 => "Aralık",
                _ => month.ToString()
            };
        }
    }
}