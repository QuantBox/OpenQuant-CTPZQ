using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using QuantBox.CSharp2CTPZQ;
using QuantBox.Helper.CTPZQ;
using SmartQuant;
using SmartQuant.Data;
using SmartQuant.Execution;
using SmartQuant.FIX;
using SmartQuant.Instruments;
using SmartQuant.Providers;
using System.Collections;

namespace QuantBox.OQ.CTPZQ
{
    partial class CTPZQProvider
    {
        #region 撤单
        private void Cancel(SingleOrder order)
        {
            if (!_bTdConnected)
            {
                EmitError(-1, -1, "交易服务器没有连接，无法撤单");
                tdlog.Error("交易服务器没有连接，无法撤单");
                return;
            }

            Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
            if (_Orders4Cancel.TryGetValue(order, out _Ref2Action))
            {
                lock (_Ref2Action)
                {
                    CZQThostFtdcOrderField __Order;
                    foreach (CZQThostFtdcOrderField _Order in _Ref2Action.Values)
                    {
                        __Order = _Order;
                        //这地方要是过滤下就好了
                        TraderApi.TD_CancelOrder(m_pTdApi, ref __Order);
                    }
                }
            }
        }
        #endregion

        #region 下单与订单分割
        private struct SOrderSplitItem
        {
            public int qty;
            public string szCombOffsetFlag;
        };

        private void Send(NewOrderSingle order)
        {
            if (!_bTdConnected)
            {
                EmitError(-1, -1, "交易服务器没有连接，无法报单");
                tdlog.Error("交易服务器没有连接，无法报单");
                return;
            }

            Instrument inst = InstrumentManager.Instruments[order.Symbol];
            string altSymbol = inst.GetSymbol(this.Name);
            string altExchange = inst.GetSecurityExchange(this.Name);
            string _altSymbol = GetApiSymbol(altSymbol);
            double tickSize = inst.TickSize;

            CZQThostFtdcInstrumentField _Instrument;
            if (_dictInstruments.TryGetValue(altSymbol, out _Instrument))
            {
                //从合约列表中取交易所名与tickSize，不再依赖用户手工设置的参数了
                tickSize = _Instrument.PriceTick;
                _altSymbol = _Instrument.InstrumentID;
                altExchange = _Instrument.ExchangeID;
            }

            //最小变动价格修正
            double price = order.Price;

            //市价修正，如果不连接行情，此修正不执行，得策略层处理
            CZQThostFtdcDepthMarketDataField DepthMarket;
            //如果取出来了，并且为有效的，涨跌停价将不为0
            _dictDepthMarketData.TryGetValue(altSymbol, out DepthMarket);

            //市价单模拟
            if (OrdType.Market == order.OrdType)
            {
                //按买卖调整价格
                if (order.Side == Side.Buy)
                {
                    price = DepthMarket.LastPrice + LastPricePlusNTicks * tickSize;
                }
                else
                {
                    price = DepthMarket.LastPrice - LastPricePlusNTicks * tickSize;
                }
            }

            //没有设置就直接用
            if (tickSize > 0)
            {
                decimal remainder = ((decimal)price % (decimal)tickSize);
                if (remainder != 0)
                {
                    if (order.Side == Side.Buy)
                    {
                        price = Math.Ceiling(price / tickSize) * tickSize;
                    }
                    else
                    {
                        price = Math.Floor(price / tickSize) * tickSize;
                    }
                }
                else
                {
                    //正好能整除，不操作
                }
            }

            if (0 == DepthMarket.UpperLimitPrice
                && 0 == DepthMarket.LowerLimitPrice)
            {
                //涨跌停无效
            }
            else
            {
                //防止价格超过涨跌停
                if (price >= DepthMarket.UpperLimitPrice)
                    price = DepthMarket.UpperLimitPrice;
                else if (price <= DepthMarket.LowerLimitPrice)
                    price = DepthMarket.LowerLimitPrice;
            }

            int YdPosition = 0;
            int TodayPosition = 0;
            int nLongFrozen = 0;
            int nShortFrozen = 0;

            string szCombOffsetFlag;

            _dbInMemInvestorPosition.GetPositions(_altSymbol,
                    TZQThostFtdcPosiDirectionType.Net, HedgeFlagType,
                    out YdPosition, out TodayPosition,
                    out nLongFrozen, out nShortFrozen);


            List<SOrderSplitItem> OrderSplitList = new List<SOrderSplitItem>();
            SOrderSplitItem orderSplitItem;

            //根据 梦翔 与 马不停蹄 的提示，新加在Text域中指定开平标志的功能
            //int nOpenCloseFlag = 0;
            //if (order.Text.StartsWith(OpenPrefix))
            //{
            //    nOpenCloseFlag = 1;
            //}
            //else if (order.Text.StartsWith(ClosePrefix))
            //{
            //    nOpenCloseFlag = -1;
            //}
            //else if (order.Text.StartsWith(CloseTodayPrefix))
            //{
            //    nOpenCloseFlag = -2;
            //}
            //else if (order.Text.StartsWith(CloseYesterdayPrefix))
            //{
            //    nOpenCloseFlag = -3;
            //}

            int leave = (int)order.OrderQty;

            if (leave > 0)
            {
                //开平已经没有意义了
                byte[] bytes = { (byte)TZQThostFtdcOffsetFlagType.Open, (byte)TZQThostFtdcOffsetFlagType.Open };
                szCombOffsetFlag = System.Text.Encoding.Default.GetString(bytes, 0, bytes.Length);

                orderSplitItem.qty = leave;
                orderSplitItem.szCombOffsetFlag = szCombOffsetFlag;
                OrderSplitList.Add(orderSplitItem);

                leave = 0;
            }

            //将第二腿也设置成一样，这样在使用组合时这地方不用再调整
            byte[] bytes2 = { (byte)HedgeFlagType, (byte)HedgeFlagType };
            string szCombHedgeFlag = System.Text.Encoding.Default.GetString(bytes2, 0, bytes2.Length);

            //bool bSupportMarketOrder = SupportMarketOrder.Contains(altExchange);

            string strPrice = string.Format("{0}", price);

            tdlog.Info("Side:{0},Price:{1},LastPrice:{2},Qty:{3},Text:{4},YdPosition:{5},TodayPosition:{6},LongFrozen:{7},ShortFrozen:{8}",
                order.Side, order.Price, DepthMarket.LastPrice, order.OrderQty, order.Text, YdPosition, TodayPosition,
                nLongFrozen, nShortFrozen);

            foreach (SOrderSplitItem it in OrderSplitList)
            {
                int nRet = 0;

                switch (order.OrdType)
                {
                    case OrdType.Limit:
                        nRet = TraderApi.TD_SendOrder(m_pTdApi,
                            _altSymbol,
                            altExchange,
                            order.Side == Side.Buy ? TZQThostFtdcDirectionType.Buy : TZQThostFtdcDirectionType.Sell,
                            it.szCombOffsetFlag,
                            szCombHedgeFlag,
                            it.qty,
                            strPrice,
                            TZQThostFtdcOrderPriceTypeType.LimitPrice,
                            TZQThostFtdcTimeConditionType.GFD,
                            TZQThostFtdcContingentConditionType.Immediately,
                            order.StopPx);
                        break;
                    case OrdType.Market:
                        //if (bSupportMarketOrder)
                        {
                            nRet = TraderApi.TD_SendOrder(m_pTdApi,
                            _altSymbol,
                            altExchange,
                            order.Side == Side.Buy ? TZQThostFtdcDirectionType.Buy : TZQThostFtdcDirectionType.Sell,
                            it.szCombOffsetFlag,
                            szCombHedgeFlag,
                            it.qty,
                            "0",
                            TZQThostFtdcOrderPriceTypeType.AnyPrice,
                            TZQThostFtdcTimeConditionType.IOC,
                            TZQThostFtdcContingentConditionType.Immediately,
                            order.StopPx);
                        }
                        //else
                        //{
                        //    nRet = TraderApi.TD_SendOrder(m_pTdApi,
                        //    _altSymbol,
                        //    altExchange,
                        //    order.Side == Side.Buy ? TZQThostFtdcDirectionType.Buy : TZQThostFtdcDirectionType.Sell,
                        //    it.szCombOffsetFlag,
                        //    szCombHedgeFlag,
                        //    it.qty,
                        //    strPrice,
                        //    TZQThostFtdcOrderPriceTypeType.LimitPrice,
                        //    TZQThostFtdcTimeConditionType.GFD,
                        //    TZQThostFtdcContingentConditionType.Immediately,
                        //    order.StopPx);
                        //}
                        break;
                    default:
                        tdlog.Warn("没有实现{0}", order.OrdType);
                        break;
                }

                if (nRet > 0)
                {
                    _OrderRef2Order.Add(string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, nRet), order as SingleOrder);
                }
            }
        }
        #endregion

        #region 报单回报
        private void OnRtnOrder(IntPtr pTraderApi, ref CZQThostFtdcOrderField pOrder)
        {
            tdlog.Info("{0},{1},{2},开平{3},价{4},原量{5},成交{6},提交{7},状态{8},前置{9},会话{10},引用{11},报单编号{12},{13}",
                    pOrder.InsertTime, pOrder.InstrumentID, pOrder.Direction, pOrder.CombOffsetFlag, pOrder.LimitPrice,
                    pOrder.VolumeTotalOriginal, pOrder.VolumeTraded, pOrder.OrderSubmitStatus, pOrder.OrderStatus,
                    pOrder.FrontID, pOrder.SessionID, pOrder.OrderRef, pOrder.OrderSysID, pOrder.StatusMsg);

            // 加上这句只是为了在一个账号多会话高频交易时提前过滤
            if (pOrder.SessionID != _RspUserLogin.SessionID || pOrder.FrontID != _RspUserLogin.FrontID)
            {
                return;
            }

            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", pOrder.FrontID, pOrder.SessionID, pOrder.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                order.Text = string.Format("{0}|{1}", order.Text.Substring(0, Math.Min(order.Text.Length, 64)), pOrder.StatusMsg);
                order.OrderID = pOrder.OrderSysID;

                //找到对应的报单回应
                Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
                if (!_Orders4Cancel.TryGetValue(order, out _Ref2Action))
                {
                    //没找到，自己填一个
                    _Ref2Action = new Dictionary<string, CZQThostFtdcOrderField>();
                    _Orders4Cancel[order] = _Ref2Action;
                }

                lock (_Ref2Action)
                {
                    string strSysID = string.Format("{0}:{1}", pOrder.ExchangeID, pOrder.OrderSysID);

                    switch (pOrder.OrderStatus)
                    {
                        case TZQThostFtdcOrderStatusType.AllTraded:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            break;
                        case TZQThostFtdcOrderStatusType.PartTradedQueueing:
                            //只是部分成交，还可以撤单，所以要记录下来
                            _Ref2Action[strKey] = pOrder;
                            break;
                        case TZQThostFtdcOrderStatusType.PartTradedNotQueueing:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            break;
                        case TZQThostFtdcOrderStatusType.NoTradeQueueing:
                            // 用于收到成交回报时定位
                            _OrderSysID2OrderRef[strSysID] = strKey;

                            if (0 == _Ref2Action.Count())
                            {
                                _Ref2Action[strKey] = pOrder;
                                EmitAccepted(order);
                            }
                            else
                            {
                                _Ref2Action[strKey] = pOrder;
                            }
                            break;
                        case TZQThostFtdcOrderStatusType.NoTradeNotQueueing:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            break;
                        case TZQThostFtdcOrderStatusType.Canceled:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            //分析此报单是否结束，如果结束分析整个Order是否结束
                            switch (pOrder.OrderSubmitStatus)
                            {
                                case TZQThostFtdcOrderSubmitStatusType.InsertRejected:
                                    //如果是最后一个的状态，同意发出消息
                                    if (0 == _Ref2Action.Count())
                                        EmitRejected(order, pOrder.StatusMsg);
                                    else
                                        Cancel(order);
                                    break;
                                default:
                                    //如果是最后一个的状态，同意发出消息
                                    if (0 == _Ref2Action.Count())
                                        EmitCancelled(order);
                                    else
                                        Cancel(order);
                                    break;
                            }
                            break;
                        case TZQThostFtdcOrderStatusType.Unknown:
                            switch (pOrder.OrderSubmitStatus)
                            {
                                case TZQThostFtdcOrderSubmitStatusType.InsertSubmitted:
                                    // 有可能头两个报单就这状态，就是报单编号由空变为了有。为空时，也记，没有关系
                                    _OrderSysID2OrderRef[strSysID] = strKey;

                                    //新单，新加入记录以便撤单
                                    if (0 == _Ref2Action.Count())
                                    {
                                        _Ref2Action[strKey] = pOrder;
                                        EmitAccepted(order);
                                    }
                                    else
                                    {
                                        _Ref2Action[strKey] = pOrder;
                                    }
                                    break;
                            }
                            break;
                        case TZQThostFtdcOrderStatusType.NotTouched:
                            //没有处理
                            break;
                        case TZQThostFtdcOrderStatusType.Touched:
                            //没有处理
                            break;
                    }

                    //已经是最后状态了，可以去除了
                    if (0 == _Ref2Action.Count())
                    {
                        _Orders4Cancel.Remove(order);
                    }
                }
            }
            else
            {
                //由第三方软件发出或上次登录时的剩余的单子在这次成交了，先不处理，当不存在
            }
        }
        #endregion

        #region 成交回报

        //用于计算组合成交
        //private Dictionary<SingleOrder, DbInMemTrade> _Orders4Combination = new Dictionary<SingleOrder, DbInMemTrade>();

        private void OnRtnTrade(IntPtr pTraderApi, ref CZQThostFtdcTradeField pTrade)
        {
            tdlog.Info("时{0},合约{1},方向{2},开平{3},价{4},量{5},引用{6},成交编号{7}",
                    pTrade.TradeTime, pTrade.InstrumentID, pTrade.Direction, pTrade.OffsetFlag,
                    pTrade.Price, pTrade.Volume, pTrade.OrderRef, pTrade.TradeID);

            //由于证券比较复杂，此处的持仓计算目前不准
            if (_dbInMemInvestorPosition.UpdateByTrade(pTrade))
            {
            }
            else
            {
                //本地计算更新失败，重查一次
                TraderApi.TD_ReqQryInvestorPosition(m_pTdApi, pTrade.InstrumentID);
            }

            SingleOrder order;
            //找到自己发送的订单，标记成交
            string strSysID = string.Format("{0}:{1}", pTrade.ExchangeID, pTrade.OrderSysID);
            string strKey;
            if (!_OrderSysID2OrderRef.TryGetValue(strSysID, out strKey))
            {
                return;
            }

            //找到自己发送的订单，标记成交
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                double Price = 0;
                int Volume = 0;
                //if (TZQThostFtdcTradeTypeType.CombinationDerived == pTrade.TradeType)
                //{
                //    //组合，得特别处理
                //    DbInMemTrade _trade;//用此对象维护组合对
                //    if (!_Orders4Combination.TryGetValue(order, out _trade))
                //    {
                //        _trade = new DbInMemTrade();
                //        _Orders4Combination[order] = _trade;
                //    }

                //    double Price = 0;
                //    int Volume = 0;
                //    //找到成对交易的，得出价差
                //    if (_trade.OnTrade(ref order, ref pTrade, ref Price, ref Volume))
                //    {
                //        EmitFilled(order, Price, Volume);

                //        //完成使命了，删除
                //        if (_trade.isEmpty())
                //        {
                //            _Orders4Combination.Remove(order);
                //        }
                //    }
                //}
                //else
                {
                    //普通订单，直接通知即可
                    Price = Convert.ToDouble(pTrade.Price);
                    Volume = pTrade.Volume;
                }

                int LeavesQty = (int)order.LeavesQty - Volume;
                EmitFilled(order, Price, Volume);

                //成交完全，清理
                if (LeavesQty <= 0)
                {
                    _OrderRef2Order.Remove(strKey);
                    _OrderSysID2OrderRef.Remove(strSysID);
                    _Orders4Cancel.Remove(order);
                }
            }
        }
        #endregion

        #region 撤单报错
        private void OnRspOrderAction(IntPtr pTraderApi, ref CZQThostFtdcInputOrderActionField pInputOrderAction, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", pInputOrderAction.FrontID, pInputOrderAction.SessionID, pInputOrderAction.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                tdlog.Error("CTPZQ回应：{0},价{1},变化量{2},前置{3},会话{4},引用{5},{6}#{7}",
                        pInputOrderAction.InstrumentID, pInputOrderAction.LimitPrice,
                        pInputOrderAction.VolumeChange,
                        pInputOrderAction.FrontID, pInputOrderAction.SessionID, pInputOrderAction.OrderRef,
                        pRspInfo.ErrorID, pRspInfo.ErrorMsg);

                order.Text = string.Format("{0}|{1}#{2}", order.Text.Substring(0, Math.Min(order.Text.Length, 64)), pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitCancelReject(order, order.OrdStatus, order.Text);
            }
        }

        private void OnErrRtnOrderAction(IntPtr pTraderApi, ref CZQThostFtdcOrderActionField pOrderAction, ref CZQThostFtdcRspInfoField pRspInfo)
        {
            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", pOrderAction.FrontID, pOrderAction.SessionID, pOrderAction.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                tdlog.Error("交易所回应：{0},价{1},变化量{2},前置{3},会话{4},引用{5},{6}#{7}",
                        pOrderAction.InstrumentID, pOrderAction.LimitPrice,
                        pOrderAction.VolumeChange,
                        pOrderAction.FrontID, pOrderAction.SessionID, pOrderAction.OrderRef,
                        pRspInfo.ErrorID, pRspInfo.ErrorMsg);

                order.Text = string.Format("{0}|{1}#{2}", order.Text.Substring(0, Math.Min(order.Text.Length, 64)), pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitCancelReject(order, order.OrdStatus, order.Text);
            }
        }
        #endregion

        #region 下单报错
        private void OnRspOrderInsert(IntPtr pTraderApi, ref CZQThostFtdcInputOrderField pInputOrder, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pInputOrder.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                tdlog.Error("CTP回应：{0},{1},开平{2},价{3},原量{4},引用{5},{6}#{7}",
                        pInputOrder.InstrumentID, pInputOrder.Direction, pInputOrder.CombOffsetFlag, pInputOrder.LimitPrice,
                        pInputOrder.VolumeTotalOriginal,
                        pInputOrder.OrderRef, pRspInfo.ErrorID, pRspInfo.ErrorMsg);

                order.Text = string.Format("{0}|{1}#{2}", order.Text.Substring(0, Math.Min(order.Text.Length, 64)), pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitRejected(order, order.Text);
                //这些地方没法处理混合报单
                //没得办法，这样全撤了状态就唯一了
                //但由于不知道在错单时是否会有报单回报，所以在这查一次，以防重复撤单出错
                //找到对应的报单回应
                Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
                if (_Orders4Cancel.TryGetValue(order, out _Ref2Action))
                {
                    lock (_Ref2Action)
                    {
                        _Ref2Action.Remove(strKey);
                        if (0 == _Ref2Action.Count())
                        {
                            _Orders4Cancel.Remove(order);
                            return;
                        }
                        Cancel(order);
                    }
                }
            }
        }

        private void OnErrRtnOrderInsert(IntPtr pTraderApi, ref CZQThostFtdcInputOrderField pInputOrder, ref CZQThostFtdcRspInfoField pRspInfo)
        {
            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pInputOrder.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                tdlog.Error("交易所回应：{0},{1},开平{2},价{3},原量{4},引用{5},{6}#{7}",
                        pInputOrder.InstrumentID, pInputOrder.Direction, pInputOrder.CombOffsetFlag, pInputOrder.LimitPrice,
                        pInputOrder.VolumeTotalOriginal,
                        pInputOrder.OrderRef, pRspInfo.ErrorID, pRspInfo.ErrorMsg);

                order.Text = string.Format("{0}|{1}#{2}", order.Text.Substring(0, Math.Min(order.Text.Length, 64)), pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitRejected(order, order.Text);
                //没得办法，这样全撤了状态就唯一了
                Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
                if (_Orders4Cancel.TryGetValue(order, out _Ref2Action))
                {
                    lock (_Ref2Action)
                    {
                        _Ref2Action.Remove(strKey);
                        if (0 == _Ref2Action.Count())
                        {
                            _Orders4Cancel.Remove(order);
                            return;
                        }
                        Cancel(order);
                    }
                }
            }
        }
        #endregion
    }
}
