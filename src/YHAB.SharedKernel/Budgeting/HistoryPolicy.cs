namespace YHAB.SharedKernel.Budgeting;

/// <summary>Pure decisions for the bounded user history. System operations never move its cursor.</summary>
public static class HistoryPolicy
{
    public const int Capacity = 50;

    public static int RequestedPosition(int cursor, bool redo) => redo ? checked(cursor + 1) : cursor;

    public static int RestoredCursor(int cursor, bool redo) => redo ? checked(cursor + 1) : checked(cursor - 1);

    public static bool DiscardOnAppend(long position, long cursor)
        => position > cursor || position <= cursor - Capacity + 1;
}
