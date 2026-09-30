using System;
using System.Collections.Generic;

namespace Kasir.Services
{
    // Thrown when a receipt or invoice fails PO / BPB matching. Message is user-facing (Indonesian).
    public class PurchaseValidationException : InvalidOperationException
    {
        public IReadOnlyList<string> Errors { get; }

        public PurchaseValidationException(IReadOnlyList<string> errors)
            : base(string.Join("\n", errors))
        {
            Errors = errors;
        }
    }
}
