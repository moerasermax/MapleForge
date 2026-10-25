using System.Text;

namespace Maple.Core.Text;

/// <summary>P192：Java <c>tools.StringUtil.getReadableMillis</c>（英文時間差字串，格式與標點照抄）。</summary>
public static class ReadableTime
{
    public static string Millis(long startMillis, long endMillis)
    {
        var sb = new StringBuilder();
        var elapsedSeconds = (endMillis - startMillis) / 1000.0;
        var secs = ((int)elapsedSeconds) % 60;
        var elapsedMinutes = (int)(elapsedSeconds / 60.0);
        var mins = elapsedMinutes % 60;
        var elapsedHrs = elapsedMinutes / 60;
        var hours = elapsedHrs % 24;
        var days = elapsedHrs / 24;
        if (days > 0)
        {
            var hasHours = hours > 0;
            sb.Append(days).Append(" day" + (days > 1 ? "s" : "") + (hasHours ? ", " : "."));
            if (hasHours)
            {
                var hasMins = mins > 0;
                if (!hasMins)
                {
                    sb.Append("and ");
                }

                sb.Append(hours).Append(" hour" + (hours > 1 ? "s" : "") + (hasMins ? ", " : "."));
                if (hasMins)
                {
                    var hasSecs = secs > 0;
                    if (!hasSecs)
                    {
                        sb.Append("and ");
                    }

                    sb.Append(mins).Append(" minute" + (mins > 1 ? "s" : "") + (hasSecs ? ", " : "."));
                    if (hasSecs)
                    {
                        sb.Append("and ").Append(secs).Append(" second" + (secs > 1 ? "s" : "") + ".");
                    }
                }
            }
        }
        else if (hours > 0)
        {
            var hasMins = mins > 0;
            sb.Append(hours).Append(" hour" + (hours > 1 ? "s" : "") + (hasMins ? ", " : "."));
            if (hasMins)
            {
                var hasSecs = secs > 0;
                if (!hasSecs)
                {
                    sb.Append("and ");
                }

                sb.Append(mins).Append(" minute" + (mins > 1 ? "s" : "") + (hasSecs ? ", " : "."));
                if (hasSecs)
                {
                    sb.Append("and ").Append(secs).Append(" second" + (secs > 1 ? "s" : "") + ".");
                }
            }
        }
        else if (elapsedMinutes > 0)
        {
            var hasSecs = secs > 0;
            sb.Append(elapsedMinutes).Append(" minute" + (elapsedMinutes > 1 ? "s" : "") + (hasSecs ? " " : "."));
            if (hasSecs)
            {
                sb.Append("and ").Append(secs).Append(" second" + (secs > 1 ? "s" : "") + ".");
            }
        }
        else if (elapsedSeconds > 0)
        {
            sb.Append((int)elapsedSeconds).Append(" second" + (elapsedSeconds > 1 ? "s" : "") + ".");
        }
        else
        {
            sb.Append("None.");
        }

        return sb.ToString();
    }
}
