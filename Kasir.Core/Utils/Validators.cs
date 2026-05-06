namespace Kasir.Utils
{
    public static class Validators
    {
        public static bool IsValidProductCode(string code)
        {
            return !string.IsNullOrWhiteSpace(code) && code.Trim().Length > 0;
        }

        public static bool IsValidAmount(long amount)
        {
            return amount >= 0;
        }

        public static bool IsValidDeptCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            string trimmed = code.Trim();
            return trimmed.Length >= 1 && trimmed.Length <= 6;
        }

        public static bool IsValidUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            string trimmed = username.Trim();
            return trimmed.Length >= 1 && trimmed.Length <= 40;
        }
    }
}
