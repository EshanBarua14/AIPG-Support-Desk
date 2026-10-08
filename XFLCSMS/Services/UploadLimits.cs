namespace XFLCSMS.Services
{
    /// <summary>
    /// How much may be attached to a ticket ("Uploads" in appsettings.json). Without a limit one person - or one
    /// script - can fill the disk of the server.
    /// </summary>
    public sealed class UploadLimits
    {
        public UploadLimits(IConfiguration configuration)
        {
            MaxFileMb = Math.Clamp(configuration.GetValue("Uploads:MaxFileMb", 10), 1, 500);
            MaxFilesPerSave = Math.Clamp(configuration.GetValue("Uploads:MaxFilesPerSave", 10), 1, 100);
        }

        /// <summary>Largest single file, in megabytes.</summary>
        public int MaxFileMb { get; }

        /// <summary>Most files that can be attached in one go.</summary>
        public int MaxFilesPerSave { get; }

        public long MaxFileBytes => MaxFileMb * 1024L * 1024L;

        /// <summary>Largest request: every file at its limit, plus room for the text of the form.</summary>
        public long MaxRequestBytes => MaxFileBytes * MaxFilesPerSave + 4 * 1024L * 1024L;
    }

    /// <summary>
    /// Does the content of an uploaded file fit the type its name claims? The name alone proves nothing: anybody can
    /// rename a program to "report.pdf". The check looks at the first bytes, which every one of the allowed formats
    /// fixes. It is a second line of defence - attachments are only ever handed out as downloads, never run or shown
    /// inside a page.
    /// </summary>
    public static class UploadContent
    {
        private static readonly byte[] Pdf = { 0x25, 0x50, 0x44, 0x46, 0x2D };                         // %PDF-
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };                               // docx, xlsx
        private static readonly byte[] Ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };       // doc, xls

        /// <summary>Null when the content fits the extension; otherwise what the file is not, e.g. "not a PDF file".</summary>
        public static async Task<string?> ProblemAsync(Stream content, string extension)
        {
            var head = new byte[4096];
            var read = 0;
            while (read < head.Length)
            {
                var n = await content.ReadAsync(head.AsMemory(read, head.Length - read));
                if (n == 0) { break; }
                read += n;
            }

            return Problem(head.AsSpan(0, read), extension);
        }

        private static string? Problem(ReadOnlySpan<byte> start, string extension)
        {
            switch (extension)
            {
                case ".pdf": return start.StartsWith(Pdf) ? null : "not a PDF file";
                case ".png": return start.StartsWith(Png) ? null : "not a PNG picture";
                case ".jpg":
                case ".jpeg": return start.StartsWith(Jpeg) ? null : "not a JPEG picture";
                case ".docx": return start.StartsWith(Zip) ? null : "not a Word document";
                case ".xlsx": return start.StartsWith(Zip) ? null : "not an Excel workbook";
                // Old Office files. Many systems also write a web page or plain text with the ending .xls or .doc
                // (exports of back-office software do): those open in Excel and Word and are accepted as text.
                case ".doc": return start.StartsWith(Ole) || start.StartsWith(Zip) || LooksLikeText(start) ? null : "not a Word document";
                case ".xls": return start.StartsWith(Ole) || start.StartsWith(Zip) || LooksLikeText(start) ? null : "not an Excel workbook";
                case ".txt":
                case ".csv": return LooksLikeText(start) ? null : "not a text file";
                default: return null;
            }
        }

        /// <summary>Text has no zero bytes - unless it is UTF-16, which says so with its first two bytes.</summary>
        private static bool LooksLikeText(ReadOnlySpan<byte> start)
        {
            if (start.Length >= 2 && ((start[0] == 0xFF && start[1] == 0xFE) || (start[0] == 0xFE && start[1] == 0xFF)))
            {
                return true;
            }

            return start.IndexOf((byte)0) < 0;
        }
    }
}
