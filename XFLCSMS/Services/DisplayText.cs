namespace XFLCSMS.Services
{
    /// <summary>Readable labels for values that are stored in a compact form ("Inprogress", "Close", ...).</summary>
    public static class DisplayText
    {
        public static string Status(string? status)
        {
            return status == null ? string.Empty : TicketStatus.Name(status);
        }
    }
}
