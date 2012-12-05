using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SmartQuant.Providers;
using SmartQuant.FIX;
using SmartQuant.Instruments;
using QuantBox.CSharp2CTPZQ;

namespace QuantBox.OQ.CTPZQ
{
    public partial class QBProvider : IInstrumentProvider
    {
        public event SecurityDefinitionEventHandler SecurityDefinition;

        public void SendSecurityDefinitionRequest(FIXSecurityDefinitionRequest request)
        {
            lock (this)
            {
                if (!_bTdConnected)
                {
                    this.EmitError(-1,-1,"交易没有连接，无法获取合约列表");
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
                        if (FIXSecurityType.Future == securityType)
                        {
                            if (TZQThostFtdcProductClassType.Futures == inst.ProductClass
                                || TZQThostFtdcProductClassType.EFP == inst.ProductClass)
                            {
                                ++flag;
                            }
                        }
                        else if (FIXSecurityType.MultiLegInstrument == securityType)//理解上是否有问题
                        {
                            if (TZQThostFtdcProductClassType.Combination == inst.ProductClass)
                            {
                                ++flag;
                            }
                        }
                        else if (FIXSecurityType.Option == securityType)
                        {
                            if (TZQThostFtdcProductClassType.Options == inst.ProductClass)
                            {
                                ++flag;
                            }
                        }
                        else if (FIXSecurityType.CommonStock == securityType)
                        {
                            if (TZQThostFtdcProductClassType.StockA == inst.ProductClass
                                || TZQThostFtdcProductClassType.StockB == inst.ProductClass)
                            {
                                ++flag;
                            }
                        }
                        else if (FIXSecurityType.ExchangeTradedFund == securityType)
                        {
                            if (TZQThostFtdcProductClassType.ETF == inst.ProductClass
                                || TZQThostFtdcProductClassType.ETFPurRed == inst.ProductClass)
                            {
                                ++flag;
                            }
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
                        SecurityResponseID = request.SecurityReqID,
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
                        SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = list.Count
                    };

                    {
                        string securityType2;
                        switch (inst.ProductClass)
                        {
                            case TZQThostFtdcProductClassType.Futures:
                                securityType2 = FIXSecurityType.Future;
                                break;
                            case TZQThostFtdcProductClassType.Combination:
                                securityType2 = FIXSecurityType.MultiLegInstrument;//此处是否理解上有不同
                                break;
                            case TZQThostFtdcProductClassType.Options:
                                securityType2 = FIXSecurityType.Option;
                                break;
                            case TZQThostFtdcProductClassType.StockA:
                            case TZQThostFtdcProductClassType.StockB:
                                securityType2 = GetSecurityTypeFromProductID(inst.ProductID);
                                break;
                            case TZQThostFtdcProductClassType.ETF:
                            case TZQThostFtdcProductClassType.ETFPurRed:
                                securityType2 = FIXSecurityType.ExchangeTradedFund;
                                break;
                            default:
                                securityType2 = FIXSecurityType.NoSecurityType;
                                break;
                        }
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

                    definition.AddField(EFIXField.Symbol, inst.InstrumentID);
                    definition.AddField(EFIXField.SecurityExchange, inst.ExchangeID);
                    definition.AddField(EFIXField.Currency, "CNY");//Currency.CNY
                    definition.AddField(EFIXField.SecurityDesc, inst.InstrumentName);
                    definition.AddField(EFIXField.Factor, (double)inst.VolumeMultiple);

                    //try
                    //{
                    //    definition.AddField(EFIXField.MaturityDate, DateTime.ParseExact(inst.ExpireDate, "yyyyMMdd", null));
                    //}
                    //catch (System.Exception ex)
                    //{
                    	
                    //}
                    
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
从CTPZQ中遍历出来的所有ID
GC 6 090002
SHETF 8 500001
SHA 6 600000
SZA 6 000001
SZBONDS 6 100213
RC 6 131800
SZETF 8 150001*/
        private string GetSecurityTypeFromProductID(string ProductID)
        {
            string securityType;
            switch (ProductID)
            {
                case "SHA":
                case "SZA":
                    securityType = FIXSecurityType.CommonStock;
                    break;
                case "SHBONDS":
                case "SZBONDS":
                case "GC":
                case "RC":
                    //此处债券种类太多，想返回一个能在导入对话框显示的居然不行，不想再试了
                    securityType = FIXSecurityType.CommonStock;
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
    }
}
