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
            // A tender amount can never be negative: a negative card/voucher line would
            // "fund" an inflated cash amount and leave a sale whose GL journal cannot
            // balance (#19).
            if (cashAmount < 0 || cardAmount < 0 || voucherAmount < 0)
            {
                return new PaymentValidation { IsValid = false };
            }

            // Each tender is non-negative, so a wrapped (negative) sum means overflow.
            long nonCash = unchecked(cardAmount + voucherAmount);
            if (nonCash < 0)
            {
                return new PaymentValidation { IsValid = false };
            }

            // Card and voucher tenders cannot produce change — you never hand back cash
            // for a card/voucher overpayment. So non-cash tender must not exceed the amount
            // due; if it does it is a data-entry error, not change (F38).
            if (nonCash > totalDue)
            {
                return new PaymentValidation
                {
                    IsValid = false,
                    NonCashOverpayment = nonCash - totalDue,
                    CardAmount = cardAmount,
                    VoucherAmount = voucherAmount
                };
            }

            // Cash must cover whatever the non-cash tender did not.
            long cashRequired = totalDue - nonCash;
            if (cashAmount < cashRequired)
            {
                return new PaymentValidation
                {
                    IsValid = false,
                    Shortfall = cashRequired - cashAmount
                };
            }

            // Change is ONLY the cash overpayment — never from card or voucher.
            long change = cashAmount - cashRequired;

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
        /// Cash still owed after the card and voucher tenders (never negative), rounded up
        /// to whole Rupiah because the tender fields take whole Rupiah only. The
        /// payment screen pre-fills the cash field with this so an untouched pre-fill is
        /// not read as an extra cash tender and shown as change (#19).
        /// </summary>
        public long SuggestedCash(long totalDue, long cardAmount, long voucherAmount)
        {
            long remaining = Math.Max(0, totalDue);
            remaining -= Math.Min(remaining, Math.Max(0, cardAmount));
            remaining -= Math.Min(remaining, Math.Max(0, voucherAmount));
            // Tender fields take whole Rupiah; round a sen remainder up so the
            // suggestion always covers what is owed.
            long sen = remaining % 100;
            return sen == 0 ? remaining : remaining + (100 - sen);
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
        // Amount by which card+voucher tender exceeded the total due (F38). Non-zero only
        // on an invalid result; such overpayment must be corrected, not returned as change.
        public long NonCashOverpayment { get; set; }
        public long CashAmount { get; set; }
        public long CardAmount { get; set; }
        public long VoucherAmount { get; set; }
    }
}
