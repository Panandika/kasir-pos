namespace Kasir.Hardware
{
    public static class BarcodeScanner
    {
        public static bool IsValidScan(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string trimmed = input.Trim();
            return trimmed.Length >= 4 && trimmed.Length <= 20;
        }
    }
}
