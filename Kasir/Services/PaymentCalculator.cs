using System;

namespace Kasir.Services
{
    public class PaymentCalculator
    {
        /// <summary>
        /// Calculate change for cash payment.
        /// All amounts are INTEGER × 100 (Rupiah cents).
        /// </summary>
        public long CalculateChange(long tendered, long totalDue)
        {
            if (tendered < totalDue)
            {
                throw new InvalidOperationException(
                    string.Format("Insufficient payment: tendered {0}, due {1}", tendered, totalDue));
            }

            return tendered - totalDue;
        }

        /// <summary>
        /// Calculate credit card processing fee.
        /// feePctX100 is the fee percentage × 100 (e.g., 250 = 2.50%).
        /// </summary>
        public long CalculateCardFee(long amount, int feePctX100)
        {
            if (feePctX100 <= 0) return 0;
            return amount * feePctX100 / 10000;
        }

        /// <summary>
        /// Validate a payment (cash + card + voucher) against total due.
        /// Voucher is deducted first, then cash + card must cover the remainder.
        /// Change is only given from cash (not card or voucher).
        /// </summary>
        public PaymentValidation ValidatePayment(
            long totalDue,
            long cashAmount,
            long cardAmount,
            long voucherAmount)
        {
            long totalPayment = cashAmount + cardAmount + voucherAmount;

            if (totalPayment < totalDue)
            {
                return new PaymentValidation
                {
                    IsValid = false,
                    Shortfall = totalDue - totalPayment
                };
            }

            // Change comes from cash only
            long change = totalPayment - totalDue;

            return new PaymentValidation
            {
                IsValid = true,
                Change = change,
                CashAmount = cashAmount,
                CardAmount = cardAmount,
                VoucherAmount = voucherAmount
            };
        }

        /// <summary>
        /// Calculate loyalty sticker points.
        /// Rp 10,000 = 1 sticker point (floor division).
        /// Amount is INTEGER × 100 (Rupiah cents).
        /// </summary>
        public int CalculateLoyaltyPoints(long totalAmountCents)
        {
            // Rp 10,000 = 1,000,000 cents
            return (int)(totalAmountCents / 1000000);
        }
    }

    public class PaymentValidation
    {
        public bool IsValid { get; set; }
        public long Change { get; set; }
        public long Shortfall { get; set; }
        public long CashAmount { get; set; }
        public long CardAmount { get; set; }
        public long VoucherAmount { get; set; }
    }
}
