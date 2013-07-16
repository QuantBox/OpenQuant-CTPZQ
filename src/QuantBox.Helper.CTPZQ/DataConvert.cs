using QuantBox.CSharp2CTPZQ;
using System.Reflection;

namespace QuantBox.Helper.CTPZQ
{
    public class DataConvert
    {
        static FieldInfo tradeField;
        static FieldInfo quoteField;

        public static bool TryConvert(OpenQuant.API.Trade trade, ref CZQThostFtdcDepthMarketDataField DepthMarketData)
        {
            if (tradeField == null)
            {
                tradeField = typeof(OpenQuant.API.Trade).GetField("trade", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            CTPZQTrade t = tradeField.GetValue(trade) as CTPZQTrade;
            if (null != t)
            {
                DepthMarketData = t.DepthMarketData;
                return true;
            }
            return false;
        }

        public static bool TryConvert(OpenQuant.API.Quote quote, ref CZQThostFtdcDepthMarketDataField DepthMarketData)
        {
            if (quoteField == null)
            {
                quoteField = typeof(OpenQuant.API.Quote).GetField("quote", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            CTPZQQuote q = quoteField.GetValue(quote) as CTPZQQuote;
            if (null != q)
            {
                DepthMarketData = q.DepthMarketData;
                return true;
            }
            return false;
        }
    }
}
