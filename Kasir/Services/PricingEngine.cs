using Kasir.Models;

namespace Kasir.Services
{
    public class PricingEngine
    {
        /// <summary>
        /// Resolves unit price for a product.
        /// Priority: promo > barcode override > open price override > qty break tiers > customer tier > base price.
        /// All prices are INTEGER × 100 (Rupiah cents).
        /// </summary>
        public int GetUnitPrice(
            Product product,
            int qty,
            int overridePrice = 0,
            int barcodeOverride = 0,
            int promoPrice = 0,
            int customerTier = 0)
        {
            // 1. Promotional price (highest priority)
            if (promoPrice > 0)
            {
                return promoPrice;
            }

            // 2. Barcode price override
            if (barcodeOverride > 0)
            {
                return barcodeOverride;
            }

            // 3. Open price override (cashier can set custom price)
            if (product.OpenPrice == "Y" && overridePrice > 0)
            {
                return overridePrice;
            }

            // 4. Quantity break tiers (check highest tier first)
            if (qty >= product.QtyBreak3 && product.QtyBreak3 > 0 && product.Price3 > 0)
            {
                return product.Price3;
            }

            if (qty >= product.QtyBreak2 && product.QtyBreak2 > 0 && product.Price2 > 0)
            {
                return product.Price2;
            }

            // 5. Customer tier pricing
            if (customerTier > 0)
            {
                int tierPrice = GetCustomerTierPrice(product, customerTier);
                if (tierPrice > 0)
                {
                    return tierPrice;
                }
            }

            // 6. Base retail price
            return product.Price;
        }

        private static int GetCustomerTierPrice(Product product, int tier)
        {
            switch (tier)
            {
                case 1: return product.Price1;
                case 2: return product.Price2;
                case 3: return product.Price3;
                case 4: return product.Price4;
                default: return 0;
            }
        }
    }
}
