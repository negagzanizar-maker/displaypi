using System.Globalization;
using System.Text;

namespace DisplayControl.Api.Pagination;

public static class CursorPage
{
    public static bool TryReadOffset(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor)) return true;
        if (cursor.Length > 32) return false;
        try
        {
            return int.TryParse(Encoding.ASCII.GetString(Convert.FromBase64String(cursor)),
                NumberStyles.None, CultureInfo.InvariantCulture, out offset) && offset is >= 0 and <= 1_000_000;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static void WriteNext(HttpResponse response, int offset, int limit, int fetchedCount)
    {
        if (fetchedCount > limit)
        {
            response.Headers["X-Next-Cursor"] = Convert.ToBase64String(
                Encoding.ASCII.GetBytes(checked(offset + limit).ToString(CultureInfo.InvariantCulture)));
        }
    }
}
