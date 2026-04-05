using System;
using System.Reflection;

namespace Kasir.Utils
{
    public static class AppVersion
    {
        public static bool JustUpdated { get; set; }
        public static string PatchNotes { get; set; }

        public static string Current
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var attr = (AssemblyInformationalVersionAttribute)
                    Attribute.GetCustomAttribute(asm, typeof(AssemblyInformationalVersionAttribute));
                return attr != null ? attr.InformationalVersion : "0.0.0";
            }
        }

        public static bool IsNewerThan(string candidate, string current)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(current))
            {
                return false;
            }

            string[] candidateParts = candidate.Trim().Split('.');
            string[] currentParts = current.Trim().Split('.');

            if (candidateParts.Length < 3 || currentParts.Length < 3)
            {
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                int c, v;
                if (!int.TryParse(candidateParts[i], out c) || !int.TryParse(currentParts[i], out v))
                {
                    return false;
                }
                if (c > v) return true;
                if (c < v) return false;
            }

            return false; // equal = not newer
        }
    }
}
