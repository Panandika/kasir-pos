using System;
using System.Collections.Generic;

namespace Kasir.Data
{
    public class DatabaseCorruptException : Exception
    {
        public List<string> Errors { get; private set; }

        public DatabaseCorruptException(List<string> errors)
            : base(BuildMessage(errors))
        {
            Errors = errors ?? new List<string>();
        }

        private static string BuildMessage(List<string> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                return "Database validation failed.";
            }
            return "Database validation failed:\n - " + string.Join("\n - ", errors);
        }
    }
}
