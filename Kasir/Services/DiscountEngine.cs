using System;
using System.Collections.Generic;
using Kasir.Models;

namespace Kasir.Services
{
    /// <summary>
    /// Resolves item-level discounts using first-match-wins priority:
    /// 1. Partner discount (discount_partners table)
    /// 2. Discounts table (rule-based, date-windowed)
    /// 3. Product-level disc_pct
    /// 4. Account config disc_pct (date-windowed)
    ///
    /// Transaction-level and member discounts are applied separately by SalesService.
    /// </summary>
    public class DiscountEngine
    {
        /// <summary>
        /// Resolve the applicable discount for a product.
        /// Returns the first matching discount in priority order.
        /// All percentages are INTEGER × 100 (e.g., 1000 = 10.00%).
        /// </summary>
        public DiscountResult ResolveDiscount(
            Product product,
            string saleDateIso,
            List<Discount> activeDiscounts,
            int partnerDiscPct,
            int accountDiscPct,
            string accountDiscDateStart,
            string accountDiscDateEnd,
            string saleTimeHms = null,
            int qty = 1)
        {
            // Priority 1: Partner discount (vendor-specific per-product)
            if (partnerDiscPct > 0)
            {
                return new DiscountResult
                {
                    DiscPct = partnerDiscPct,
                    Source = "partner"
                };
            }

            // Priority 2: Discounts table (rule-based, date/time-windowed)
            foreach (var disc in activeDiscounts)
            {
                if (disc.IsActive != 1) continue;

                // Check product/department/store-wide match
                bool matches = false;

                if (!string.IsNullOrEmpty(disc.ProductCode) &&
                    disc.ProductCode == product.ProductCode)
                {
                    matches = true;
                }
                else if (!string.IsNullOrEmpty(disc.DeptCode) &&
                    disc.DeptCode == product.DeptCode)
                {
                    matches = true;
                }
                else if (string.IsNullOrEmpty(disc.ProductCode) &&
                    string.IsNullOrEmpty(disc.DeptCode))
                {
                    matches = true; // Store-wide discount
                }

                if (!matches) continue;

                // Check date window
                if (!IsWithinDateWindow(saleDateIso, disc.DateStart, disc.DateEnd))
                {
                    continue;
                }

                // Check time window (with midnight-crossing support)
                if (!IsWithinTimeWindow(saleTimeHms, disc.TimeStart, disc.TimeEnd))
                {
                    continue;
                }

                // Check quantity threshold
                if (disc.MinQty > 0 && qty < disc.MinQty) continue;
                if (disc.MaxQty > 0 && qty > disc.MaxQty) continue;

                long discAmount = 0;
                if (disc.PriceOverride > 0 && product.Price > disc.PriceOverride)
                {
                    // PriceOverride is a per-unit target price; discount = (normal - override) * qty
                    discAmount = ((long)product.Price - disc.PriceOverride) * qty;
                }

                return new DiscountResult
                {
                    DiscPct = disc.DiscPct,
                    Disc2Pct = disc.Disc2Pct,
                    DiscAmount = discAmount,
                    Source = "discounts_table"
                };
            }

            // Priority 3: Product-level disc_pct
            if (product.DiscPct > 0)
            {
                return new DiscountResult
                {
                    DiscPct = product.DiscPct,
                    Source = "product"
                };
            }

            // Priority 4: Account config discount (date-windowed)
            if (accountDiscPct > 0 &&
                IsWithinDateWindow(saleDateIso, accountDiscDateStart, accountDiscDateEnd))
            {
                return new DiscountResult
                {
                    DiscPct = accountDiscPct,
                    Source = "account_config"
                };
            }

            return DiscountResult.None;
        }

        private static bool IsWithinTimeWindow(string currentTimeHms, string startTime, string endTime)
        {
            if (string.IsNullOrEmpty(startTime) && string.IsNullOrEmpty(endTime))
            {
                return true; // No time constraint
            }

            if (string.IsNullOrEmpty(currentTimeHms))
            {
                return true; // No current time provided — skip time check
            }

            bool hasStart = !string.IsNullOrEmpty(startTime);
            bool hasEnd = !string.IsNullOrEmpty(endTime);

            // Midnight crossing (e.g., 22:00:00 to 02:00:00)
            if (hasStart && hasEnd &&
                string.Compare(startTime, endTime, StringComparison.Ordinal) > 0)
            {
                return string.Compare(currentTimeHms, startTime, StringComparison.Ordinal) >= 0 ||
                       string.Compare(currentTimeHms, endTime, StringComparison.Ordinal) <= 0;
            }

            // Normal range: start <= current <= end
            if (hasStart &&
                string.Compare(currentTimeHms, startTime, StringComparison.Ordinal) < 0)
            {
                return false;
            }

            if (hasEnd &&
                string.Compare(currentTimeHms, endTime, StringComparison.Ordinal) > 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsWithinDateWindow(string currentDateIso, string startDate, string endDate)
        {
            if (string.IsNullOrEmpty(startDate) && string.IsNullOrEmpty(endDate))
            {
                return true; // No date constraint
            }

            if (string.IsNullOrEmpty(currentDateIso))
            {
                return false;
            }

            // ISO date strings are lexicographically comparable
            if (!string.IsNullOrEmpty(startDate) &&
                string.Compare(currentDateIso, startDate, StringComparison.Ordinal) < 0)
            {
                return false; // Before start date
            }

            if (!string.IsNullOrEmpty(endDate) &&
                string.Compare(currentDateIso, endDate, StringComparison.Ordinal) > 0)
            {
                return false; // After end date
            }

            return true;
        }
    }
}
