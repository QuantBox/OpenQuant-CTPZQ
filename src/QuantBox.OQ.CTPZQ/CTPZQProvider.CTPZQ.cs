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
        private fnOnConnect _fnOnConnect_Holder;
        private fnOnDisconnect _fnOnDisconnect_Holder;
        private fnOnErrRtnOrderAction _fnOnErrRtnOrderAction_Holder;
        private fnOnErrRtnOrderInsert _fnOnErrRtnOrderInsert_Holder;
        private fnOnRspError _fnOnRspError_Holder;
        private fnOnRspOrderAction _fnOnRspOrderAction_Holder;
        private fnOnRspOrderInsert _fnOnRspOrderInsert_Holder;
        private fnOnRspQryDepthMarketData _fnOnRspQryDepthMarketData_Holder;
        private fnOnRspQryInstrument _fnOnRspQryInstrument_Holder;
        private fnOnRspQryInstrumentCommissionRate _fnOnRspQryInstrumentCommissionRate_Holder;
        //private fnOnRspQryInstrumentMarginRate      _fnOnRspQryInstrumentMarginRate_Holder;
        private fnOnRspQryInvestorPosition _fnOnRspQryInvestorPosition_Holder;
        private fnOnRspQryTradingAccount _fnOnRspQryTradingAccount_Holder;
        private fnOnRtnDepthMarketData _fnOnRtnDepthMarketData_Holder;
        private fnOnRtnInstrumentStatus _fnOnRtnInstrumentStatus_Holder;
        private fnOnRtnOrder _fnOnRtnOrder_Holder;
        private fnOnRtnTrade _fnOnRtnTrade_Holder;

        private void InitCallbacks()
        {
            //由于回调函数可能被GC回收，所以用成员变量将回调函数保存下来
            _fnOnConnect_Holder = OnConnect;
            _fnOnDisconnect_Holder = OnDisconnect;
            _fnOnErrRtnOrderAction_Holder = OnErrRtnOrderAction;
            _fnOnErrRtnOrderInsert_Holder = OnErrRtnOrderInsert;
            _fnOnRspError_Holder = OnRspError;
            _fnOnRspOrderAction_Holder = OnRspOrderAction;
            _fnOnRspOrderInsert_Holder = OnRspOrderInsert;
            _fnOnRspQryDepthMarketData_Holder = OnRspQryDepthMarketData;
            _fnOnRspQryInstrument_Holder = OnRspQryInstrument;
            _fnOnRspQryInstrumentCommissionRate_Holder = OnRspQryInstrumentCommissionRate;
            //_fnOnRspQryInstrumentMarginRate_Holder      = OnRspQryInstrumentMarginRate;
            _fnOnRspQryInvestorPosition_Holder = OnRspQryInvestorPosition;
            _fnOnRspQryTradingAccount_Holder = OnRspQryTradingAccount;
            _fnOnRtnDepthMarketData_Holder = OnRtnDepthMarketData;
            _fnOnRtnInstrumentStatus_Holder = OnRtnInstrumentStatus;
            _fnOnRtnOrder_Holder = OnRtnOrder;
            _fnOnRtnTrade_Holder = OnRtnTrade;
        }

        private IntPtr m_pMsgQueue = IntPtr.Zero;   //消息队列指针
        private IntPtr m_pMdApi = IntPtr.Zero;      //行情对象指针
        private IntPtr m_pTdApi = IntPtr.Zero;      //交易对象指针

        //行情有效状态，约定连接上并通过认证为有效
        private volatile bool _bMdConnected = false;
        //交易有效状态，约定连接上，通过认证并进行结算单确认为有效
        private volatile bool _bTdConnected = false;

        //表示用户操作，也许有需求是用户有多个行情，只连接第一个等
        private bool _bWantMdConnect;
        private bool _bWantTdConnect;

        private object _lockMd = new object();
        private object _lockTd = new object();
        private object _lockMsgQueue = new object();

        //记录交易登录成功后的SessionID、FrontID等信息
        private CZQThostFtdcRspUserLoginField _RspUserLogin;

        //记录界面生成的报单，用于定位收到回报消息时所确定的报单,可以多个Ref对应一个Order
        private Dictionary<string, SingleOrder> _OrderRef2Order = new Dictionary<string, SingleOrder>();
        //一个Order可能分拆成多个报单，如可能由平今与平昨，或开新单组合而成
        private Dictionary<SingleOrder, Dictionary<string, CZQThostFtdcOrderField>> _Orders4Cancel
            = new Dictionary<SingleOrder, Dictionary<string, CZQThostFtdcOrderField>>();
        //交易所信息映射到本地信息
        private readonly Dictionary<string, string> _OrderSysID2OrderRef = new Dictionary<string, string>();

        //记录账号的实际持仓，保证以最低成本选择开平
        private DbInMemInvestorPosition _dbInMemInvestorPosition = new DbInMemInvestorPosition();
        //记录合约实际行情，用于向界面通知行情用，这里应当记录AltSymbol
        private Dictionary<string, CZQThostFtdcDepthMarketDataField> _dictDepthMarketData = new Dictionary<string, CZQThostFtdcDepthMarketDataField>();
        //记录合约列表,从实盘合约名到对象的映射
        private Dictionary<string, CZQThostFtdcInstrumentField> _dictInstruments = new Dictionary<string, CZQThostFtdcInstrumentField>();
        private Dictionary<string, string> _dictInstruments2 = new Dictionary<string, string>();
        //记录手续费率,从实盘合约名到对象的映射
        private Dictionary<string, CZQThostFtdcInstrumentCommissionRateField> _dictCommissionRate = new Dictionary<string, CZQThostFtdcInstrumentCommissionRateField>();
        //记录保证金率,从实盘合约名到对象的映射
        //private Dictionary<string, CZQThostFtdcInstrumentMarginRateField> _dictMarginRate = new Dictionary<string, CZQThostFtdcInstrumentMarginRateField>();
        //记录
        private Dictionary<string, DataRecord> _dictAltSymbol2Instrument = new Dictionary<string, DataRecord>();

        //用于行情的时间，只在登录时改动，所以要求开盘时能得到更新
        private int _yyyy;
        private int _MM;
        private int _dd;

        private ServerItem server;
        private AccountItem account;

        #region 合约列表
        private void OnRspQryInstrument(IntPtr pTraderApi, ref CZQThostFtdcInstrumentField pInstrument, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            if (0 == pRspInfo.ErrorID)
            {
                //比较无语，测试平台上会显示很多无效数据，有关期货的还会把正确的数据给覆盖，所以临时这样处理
                if (pInstrument.ProductClass != TZQThostFtdcProductClassType.Futures)
                {
                    string symbol = GetYahooSymbol(pInstrument.InstrumentID, pInstrument.ExchangeID);
                    _dictInstruments[symbol] = pInstrument;

                    // 行情中可能没有交易所信息，这个容器用于容错处理
                    _dictInstruments2[pInstrument.InstrumentID] = symbol;
                }

                if (bIsLast)
                {
                    tdlog.Info("合约列表已经接收完成,共{0}条", _dictInstruments.Count);
                }
            }
            else
            {
                tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryInstrument:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrument:" + pRspInfo.ErrorMsg);
            }
        }
        #endregion

        #region 手续费列表
        private void OnRspQryInstrumentCommissionRate(IntPtr pTraderApi, ref CZQThostFtdcInstrumentCommissionRateField pInstrumentCommissionRate, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            if (0 == pRspInfo.ErrorID)
            {
                _dictCommissionRate[pInstrumentCommissionRate.InstrumentID] = pInstrumentCommissionRate;
                tdlog.Info("已经接收手续费率 {0}", pInstrumentCommissionRate.InstrumentID);

                //通知单例
                CTPZQAPI.GetInstance().FireOnRspQryInstrumentCommissionRate(pInstrumentCommissionRate);
            }
            else
            {
                tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryInstrumentCommissionRate:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrumentCommissionRate:" + pRspInfo.ErrorMsg);
            }
        }
        #endregion

        //#region 保证金率列表
        //private void OnRspQryInstrumentMarginRate(IntPtr pTraderApi, ref CZQThostFtdcInstrumentMarginRateField pInstrumentMarginRate, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        //{
        //    if (0 == pRspInfo.ErrorID)
        //    {
        //        _dictMarginRate[pInstrumentMarginRate.InstrumentID] = pInstrumentMarginRate;
        //        Console.WriteLine("TdApi:{0},已经接收保证金率 {1}",
        //                Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        //                pInstrumentMarginRate.InstrumentID);

        //        Console.WriteLine("{0},{1},{2},{3}", pInstrumentMarginRate.LongMarginRatioByMoney, pInstrumentMarginRate.LongMarginRatioByVolume,
        //            pInstrumentMarginRate.ShortMarginRatioByMoney, pInstrumentMarginRate.ShortMarginRatioByVolume);

        //        //通知单例
        //        CTPZQAPI.GetInstance().FireOnRspQryInstrumentMarginRate(pInstrumentMarginRate);
        //    }
        //    else
        //        EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrumentMarginRate:" + pRspInfo.ErrorMsg);
        //}
        //#endregion

        #region 持仓回报
        private void OnRspQryInvestorPosition(IntPtr pTraderApi, ref CZQThostFtdcInvestorPositionField pInvestorPosition, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            if (0 == pRspInfo.ErrorID)
            {
                _dbInMemInvestorPosition.InsertOrReplace(
                    pInvestorPosition.InstrumentID,
                    pInvestorPosition.PosiDirection,
                    pInvestorPosition.HedgeFlag,
                    pInvestorPosition.PositionDate,
                    pInvestorPosition.Position,
                    pInvestorPosition.LongFrozen,
                    pInvestorPosition.ShortFrozen);
                timerPonstion.Enabled = false;
                timerPonstion.Enabled = true;
            }
            else
            {
                tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryInvestorPosition:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInvestorPosition:" + pRspInfo.ErrorMsg);
            }
        }
        #endregion

        #region 资金回报
        CZQThostFtdcTradingAccountField m_TradingAccount;
        private void OnRspQryTradingAccount(IntPtr pTraderApi, ref CZQThostFtdcTradingAccountField pTradingAccount, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLastt)
        {
            if (0 == pRspInfo.ErrorID)
            {
                m_TradingAccount = pTradingAccount;
                //有资金信息过来了，重新计时
                timerAccount.Enabled = false;
                timerAccount.Enabled = true;
            }
            else
            {
                tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspQryTradingAccount:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryTradingAccount:" + pRspInfo.ErrorMsg);
            }
        }
        #endregion

        #region 错误回调
        private void OnRspError(IntPtr pApi, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            tdlog.Error("nRequestID:{0},ErrorID:{1},OnRspError:{2}", nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
            EmitError(nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        }
        #endregion

        #region 交易所状态
        private void OnRtnInstrumentStatus(IntPtr pTraderApi, ref CZQThostFtdcInstrumentStatusField pInstrumentStatus)
        {
            tdlog.Info("{0},{1},{2},{3}",
                pInstrumentStatus.ExchangeID, pInstrumentStatus.InstrumentID,
                pInstrumentStatus.InstrumentStatus, pInstrumentStatus.EnterReason);

            //通知单例
            CTPZQAPI.GetInstance().FireOnRtnInstrumentStatus(pInstrumentStatus);
        }
        #endregion
    }
}
