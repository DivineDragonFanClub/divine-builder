using System;

namespace DivineDragon
{
    public static class TimeFormatter
    {
        /// <summary>
        /// Gets a timestamp with relative time in parentheses (e.g., "14:35:22 (2 minutes ago)")
        /// </summary>
        public static string GetRelativeTimeWithTimestamp(DateTime time, bool includeTimestamp = true)
        {
            string relativeTime = GetRelativeTime(time);
            
            if (includeTimestamp)
            {
                return $"{time:HH:mm:ss} ({relativeTime})";
            }
            
            return relativeTime;
        }
        
        /// <summary>
        /// Gets only the relative time string (e.g., "2 minutes ago")
        /// </summary>
        public static string GetRelativeTime(DateTime time)
        {
            var timeSpan = DateTime.Now - time;
            
            if (timeSpan.TotalSeconds < 60)
                return "just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes == 1 ? "" : "s")} ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours == 1 ? "" : "s")} ago";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays == 1 ? "" : "s")} ago";
            
            return time.ToString("MMM dd, yyyy HH:mm");
        }
        
        /// <summary>
        /// Formats a build completion message with timestamp and relative time
        /// </summary>
        public static string FormatBuildCompleteMessage(DateTime completionTime)
        {
            return $"Build complete at {completionTime:HH:mm:ss} ({GetRelativeTime(completionTime)}) ✔";
        }
    }
}