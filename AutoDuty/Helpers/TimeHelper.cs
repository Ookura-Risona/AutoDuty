using System;

namespace AutoDuty.Helpers
{
    internal static class TimeHelper
    {
        internal static DateTime GetNextDateTimeForHour(int hours) =>
            DateTime.UtcNow.Hour < hours ? 
                DateTime.UtcNow.Date.AddHours(hours) : 
                DateTime.UtcNow.Date.AddDays(1).AddHours(hours);

        internal static DateTime GetLastDateTimeForHour(int hours) =>
            DateTime.UtcNow.Hour > hours ?
                DateTime.UtcNow.Date.AddHours(hours) :
                DateTime.UtcNow.Date.AddDays(-1).AddHours(hours);
    }
}
