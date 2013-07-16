using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SmartQuant.Data;

using QuantBox.CSharp2CTPZQ;
using System.Reflection;

namespace QuantBox.Helper.CTPZQ
{
    public class CTPZQQuote:Quote
    {
        public CTPZQQuote():base()
        {
        }

        public CTPZQQuote(Quote quote): base(quote)
        {
        }

        public CTPZQQuote(DateTime datetime, double bid, int bidSize, double ask, int askSize)
            : base(datetime, bid, bidSize, ask, askSize)
        {
        }

        public CZQThostFtdcDepthMarketDataField DepthMarketData;
    }
}
