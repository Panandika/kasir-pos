using Kasir.Models;

namespace Kasir.Services
{
    public class PricingEngine
    {
        /// <summary>
        /// Resolves unit price for a product.
        /// Priority: promo > open price override > qty break tiers > customer tier > base price.
        /// All prices are INTEGER × 100 (Rupiah cents).
        /// </summary>
        public long GetUnitPrice(
            Product product,
            int qty,
            long overridePrice = 0,
            long promoPrice = 0,
            int customerTier = 0)
        {
            // 1. Promotional price (highest priority)
            if (promoPrice > 0)
            {
                return promoPrice;
            }

            // 2. Open price override (cashier can set custom price)
            if (product.OpenPrice == "Y" && overridePrice > 0)
            {
                return overridePrice;
            }

            // 3. Quantity break tiers (check highest tier first)
            if (qty >= product.QtyBreak3 && product.QtyBreak3 > 0 && product.Price3 > 0)
            {
                return product.Price3;
            }

            if (qty >= product.QtyBreak2 && product.QtyBreak2 > 0 && product.Price2 > 0)
            {
                return product.Price2;
            }

            // 4. Customer tier pricing
            if (customerTier > 0)
            {
                long tierPrice = GetCustomerTierPrice(product, customerTier);
                if (tierPrice > 0)
                {
                    return tierPrice;
                }
            }

            // 5. Base retail price
            return product.Price;
        }

        private static long GetCustomerTierPrice(Product product, int tier)
        {
            switch (tier)
            {
                case 1: return product.Price1;
                // Price2/Price3 are shared with qty-break pricing.
                // Only use as tier price if no qty-break threshold is configured for that tier.
                case 2: return product.QtyBreak2 > 0 ? 0 : product.Price2;
                case 3: return product.QtyBreak3 > 0 ? 0 : product.Price3;
                case 4: return product.Price4;
                default: return 0;
            }
        }
    }
}
