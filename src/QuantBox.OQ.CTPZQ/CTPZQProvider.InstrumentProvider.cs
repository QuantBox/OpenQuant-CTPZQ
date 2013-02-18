using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SmartQuant.Providers;
using SmartQuant.FIX;
using SmartQuant.Instruments;
using QuantBox.CSharp2CTPZQ;
using System.Text.RegularExpressions;

namespace QuantBox.OQ.CTPZQ
{
    public partial class CTPZQProvider : IInstrumentProvider
    {
        public event SecurityDefinitionEventHandler SecurityDefinition;

        public void SendSecurityDefinitionRequest(FIXSecurityDefinitionRequest request)
        {
            lock (this)
            {
                if (!_bTdConnected)
                {
                    EmitError(-1, -1, "交易没有连接，无法获取合约列表");
                    tdlog.Error("交易没有连接，无法获取合约列表");
                    return;
                }

                string symbol = request.ContainsField(EFIXField.Symbol) ? request.Symbol : null;
                string securityType = request.ContainsField(EFIXField.SecurityType) ? request.SecurityType : null;
                string securityExchange = request.ContainsField(EFIXField.SecurityExchange) ? request.SecurityExchange : null;

                #region 过滤
                List<CZQThostFtdcInstrumentField> list = new List<CZQThostFtdcInstrumentField>();
                foreach (CZQThostFtdcInstrumentField inst in _dictInstruments.Values)
                {
                    int flag = 0;
                    if (null == symbol)
                    {
                        ++flag;
                    }
                    else if (inst.InstrumentID.ToUpper().StartsWith(symbol.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityExchange)
                    {
                        ++flag;
                    }
                    else if (inst.ExchangeID.ToUpper().StartsWith(securityExchange.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityType)
                    {
                        ++flag;
                    }
                    else
                    {
                        if (securityType == GetSecurityType(inst))
                        {
                            ++flag;
                        }
                    }
                    
                    if (3==flag)
                    {
                        list.Add(inst);
                    }
                }
                #endregion

                list.Sort(SortCZQThostFtdcInstrumentField);

                //如果查出的数据为0，应当想法立即返回
                if (0==list.Count)
                {
                    FIXSecurityDefinition definition = new FIXSecurityDefinition
                    {
                        SecurityReqID = request.SecurityReqID,
                        //SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = 1//有个除0错误的问题
                    };
                    if (SecurityDefinition != null)
                    {
                        SecurityDefinition(this, new SecurityDefinitionEventArgs(definition));
                    }
                }

                foreach (CZQThostFtdcInstrumentField inst in list)
                {
                    FIXSecurityDefinition definition = new FIXSecurityDefinition
                    {
                        SecurityReqID = request.SecurityReqID,
                        //SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = list.Count
                    };

                    {
                        string securityType2 = GetSecurityType(inst);
                        definition.AddField(EFIXField.SecurityType, securityType2);
                    }
                    {
                        double x = inst.PriceTick;
                        if (x>0.0001)
                        {
                            int i = 0;
                            for (; x - (int)x != 0; ++i)
                            {
                                x = x * 10;
                            }
                            definition.AddField(EFIXField.PriceDisplay, string.Format("F{0}", i));
                            definition.AddField(EFIXField.TickSize, inst.PriceTick);
                        }
                    }

                    definition.AddField(EFIXField.Symbol, GetYahooSymbol(inst.InstrumentID, inst.ExchangeID));
                    definition.AddField(EFIXField.SecurityExchange, inst.ExchangeID);
                    definition.AddField(EFIXField.Currency, "CNY");//Currency.CNY
                    definition.AddField(EFIXField.SecurityDesc, inst.InstrumentName);
                    definition.AddField(EFIXField.Factor, (double)inst.VolumeMultiple);            
                    //还得补全内容

                    if (SecurityDefinition != null)
                    {
                        SecurityDefinition(this, new SecurityDefinitionEventArgs(definition));
                    }
                }
            }
        }

        private static int SortCZQThostFtdcInstrumentField(CZQThostFtdcInstrumentField a1, CZQThostFtdcInstrumentField a2)
        {
            return a1.InstrumentID.CompareTo(a2.InstrumentID);
        }

        /*
         * 上海证券交易所证券代码分配规则
         * http://www.docin.com/p-417422186.html
         * 
         * http://wenku.baidu.com/view/f2e9ddf77c1cfad6195fa706.html
         */
        private string GetSecurityType(CZQThostFtdcInstrumentField inst)
        {
            string securityType = FIXSecurityType.NoSecurityType;
            switch (inst.ProductClass)
            {
                case TZQThostFtdcProductClassType.Futures:
                    securityType = FIXSecurityType.Future;
                    break;
                case TZQThostFtdcProductClassType.Combination:
                    securityType = FIXSecurityType.MultiLegInstrument;//此处是否理解上有不同
                    break;
                case TZQThostFtdcProductClassType.Options:
                    securityType = FIXSecurityType.Option;
                    break;
                case TZQThostFtdcProductClassType.StockA:
                case TZQThostFtdcProductClassType.StockB:
                    securityType = GetSecurityTypeStock(inst.ProductID,inst.InstrumentID);
                    break;
                case TZQThostFtdcProductClassType.ETF:
                case TZQThostFtdcProductClassType.ETFPurRed:
                    securityType = GetSecurityTypeETF(inst.ProductID, inst.InstrumentID);
                    break;
                default:
                    securityType = FIXSecurityType.NoSecurityType;
                    break;
            }
            return securityType;
        }

        /*
        从CTPZQ中遍历出来的所有ID
        GC 6 090002
        SHETF 8 500001
        SHA 6 600000
        SZA 6 000001
        SZBONDS 6 100213
        RC 6 131800
        SZETF 8 150001*/
        private string GetSecurityTypeStock(string ProductID,string InstrumentID)
        {
            string securityType = FIXSecurityType.CommonStock;
            switch (ProductID)
            {
                case "SHA":
                case "SZA":
                    securityType = FIXSecurityType.CommonStock;
                    break;
                case "SHBONDS":
                    {
                        int i = Convert.ToInt32(InstrumentID.Substring(0, 3));
                        if (i == 0)
                        {
                            securityType = FIXSecurityType.Index;
                        }
                        else if (i < 700)
                        {
                            securityType = FIXSecurityType.USTreasuryBond;
                        }
                        else if (i < 800)
                        {
                            securityType = FIXSecurityType.CommonStock;
                        }
                        else
                        {
                            securityType = FIXSecurityType.USTreasuryBond;
                        }
                    }
                    break;
                case "SZBONDS":
                    {
                        int i = Convert.ToInt32(InstrumentID.Substring(0, 2));
                        if (i == 39)
                        {
                            securityType = FIXSecurityType.Index;
                        }
                        else
                        {
                            securityType = FIXSecurityType.USTreasuryBond;
                        }
                    }
                    break;
                case "GC":
                case "RC":
                    securityType = FIXSecurityType.USTreasuryBond;
                    break;
                case "SHETF":
                case "SZETF":
                    securityType = FIXSecurityType.ExchangeTradedFund;
                    break;
                default:
                    securityType = FIXSecurityType.NoSecurityType;
                    break;
            }
            return securityType;
        }

        private string GetSecurityTypeETF(string ProductID, string InstrumentID)
        {
            string securityType = FIXSecurityType.ExchangeTradedFund;
            switch (ProductID)
            {
                case "SHA":
                    securityType = FIXSecurityType.CommonStock;
                    break;
                case "SZA":
                    {
                        int i = Convert.ToInt32(InstrumentID.Substring(0, 2));
                        if (i < 10)
                        {
                            securityType = FIXSecurityType.CommonStock;
                        }
                        else if (i < 15)
                        {
                            securityType = FIXSecurityType.USTreasuryBond;
                        }
                        else if (i < 20)
                        {
                            securityType = FIXSecurityType.ExchangeTradedFund;
                        }
                        else if (i < 30)
                        {
                            securityType = FIXSecurityType.CommonStock;
                        }
                        else if (i < 39)
                        {
                            securityType = FIXSecurityType.CommonStock;
                        }
                        else if (i == 39)
                        {
                            securityType = FIXSecurityType.Index;
                        }
                        else
                        {
                            securityType = FIXSecurityType.NoSecurityType;
                        }
                    }
                    break;
            }
            return securityType;
        }

        private string GetYahooSymbol(string InstrumentID,string ExchangeID)
        {
            return string.Format("{0}.{1}", InstrumentID, ExchangeID.Substring(0, 2));
        }

        private string GetApiSymbol(string Symbol)
        {
            var match = Regex.Match(Symbol, @"(\d+)\.(\w+)");
            if (match.Success)
            {
                var code = match.Groups[1].Value;
                return code;
            }
            return Symbol;
        }

        private string GetApiExchange(string Symbol)
        {
            var match = Regex.Match(Symbol, @"(\d+)\.(\w+)");
            if (match.Success)
            {
                var code = match.Groups[2].Value;
                return code;
            }
            return Symbol;
        }
    }
}
