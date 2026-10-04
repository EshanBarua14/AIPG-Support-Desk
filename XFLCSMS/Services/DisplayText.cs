namespace XFLCSMS.Services
{
    /// <summary>Readable labels for values that are stored in a compact form ("Inprogress", "Close", ...).</summary>
    public static class DisplayText
    {
        public static string Status(string? status)
        {
            switch (status)
            {
                case "Inprogress": return "In Progress";
                case "Inqueue": return "In Queue";
                case "Close": return "Closed";
                case null: return string.Empty;
                default: return status;
            }
        }
    }
}
