using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SmartQuant.Data;

using QuantBox.CSharp2CTPZQ;
using System.Reflection;

namespace QuantBox.Helper.CTPZQ
{
    public class CTPZQTrade : Trade
    {
        public CTPZQTrade()
            : base()
        {
        }

        public CTPZQTrade(Trade trade)
            : base(trade)
        {
        }

        public CTPZQTrade(DateTime datetime, double price, int size)
            : base(datetime, price, size)
        {
        }

        public CZQThostFtdcDepthMarketDataField DepthMarketData;
    }
}
