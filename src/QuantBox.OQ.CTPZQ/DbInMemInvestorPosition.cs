using System;
using System.Data;
using System.Linq;
using QuantBox.CSharp2CTPZQ;

namespace QuantBox.OQ.CTPZQ
{
    class DbInMemInvestorPosition
    {
        public const string InstrumentID = "InstrumentID";
        public const string PosiDirection = "PosiDirection";
        public const string HedgeFlag = "HedgeFlag";
        public const string PositionDate = "PositionDate";
        public const string Position = "Position";//记录的是实际持仓量，已经按今昨进行了区分
        public const string LongFrozen = "LongFrozen";//记录的是当天买入就被冻结了
        public const string ShortFrozen = "ShortFrozen";

        private DataTable dtInvestorPosition = new DataTable("Position");

        public DbInMemInvestorPosition()
        {
            dtInvestorPosition.Columns.Add(InstrumentID, Type.GetType("System.String"));
            dtInvestorPosition.Columns.Add(PosiDirection, typeof(QuantBox.CSharp2CTPZQ.TZQThostFtdcPosiDirectionType));
            dtInvestorPosition.Columns.Add(HedgeFlag, typeof(QuantBox.CSharp2CTPZQ.TZQThostFtdcHedgeFlagType));
            dtInvestorPosition.Columns.Add(PositionDate, typeof(QuantBox.CSharp2CTPZQ.TZQThostFtdcPositionDateType));
            dtInvestorPosition.Columns.Add(Position, Type.GetType("System.Int32"));
            dtInvestorPosition.Columns.Add(LongFrozen, Type.GetType("System.Int32"));
            dtInvestorPosition.Columns.Add(ShortFrozen, Type.GetType("System.Int32"));
            //因为PositionDate有了区分，所以TodayPosition可以不专门用字段记录

            UniqueConstraint uniqueConstraint = new UniqueConstraint(new DataColumn[] {
                        dtInvestorPosition.Columns[InstrumentID],
                        dtInvestorPosition.Columns[PosiDirection],
                        dtInvestorPosition.Columns[HedgeFlag],
                        dtInvestorPosition.Columns[PositionDate]
                    });
            dtInvestorPosition.Constraints.Add(uniqueConstraint);
        }

        //private int x = 0;

        //查询持仓后调用此函数
        public bool InsertOrReplace(
            string InstrumentID,
            TZQThostFtdcPosiDirectionType PosiDirection,
            TZQThostFtdcHedgeFlagType HedgeFlag,
            TZQThostFtdcPositionDateType PositionDate,
            int volume,
            int nLongFrozen,
            int nShortFrozen)
        {
            lock(this)
            {
                //冲突的可能性大一些，所以要先Update后Insert
                DataRow[] rows = Select(InstrumentID,
                    PosiDirection,
                    HedgeFlag,
                    PositionDate);

                if (rows.Count() == 1)
                {
                    rows[0][Position] = volume;
                    rows[0][LongFrozen] = nLongFrozen;
                    rows[0][ShortFrozen] = nShortFrozen;
                }
                else
                {
                    try
                    {
                        dtInvestorPosition.Rows.Add(
                            InstrumentID,
                            PosiDirection,
                            HedgeFlag,
                            PositionDate,
                            volume,
                            nLongFrozen,
                            nShortFrozen);
                    }
                    catch
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        //只收到成交信息时调用
        public bool InsertOrReplaceForTrade(
            string InstrumentID,
            TZQThostFtdcPosiDirectionType PosiDirection,
            TZQThostFtdcDirectionType Direction,
            TZQThostFtdcHedgeFlagType HedgeFlag,
            TZQThostFtdcPositionDateType PositionDate,
            int volume)
        {
            lock(this)
            {
                // 今天的买入要先冻结
                //冲突的可能性大一些，所以要先Update后Insert
                DataRow[] rows = Select(InstrumentID, PosiDirection, HedgeFlag, PositionDate);

                if (rows.Count() == 1)
                {
                    int vol = (int)rows[0][Position];
                    rows[0][Position] = vol - volume;
                }
                else
                {
                    //假设是新添数据
                    try
                    {
                        if (Direction == TZQThostFtdcDirectionType.Buy)
                        {
                            dtInvestorPosition.Rows.Add(
                                        InstrumentID,
                                        PosiDirection,
                                        HedgeFlag,
                                        PositionDate,
                                        0,
                                        volume,
                                        0);
                        }
                        else
                        {
                            dtInvestorPosition.Rows.Add(
                                        InstrumentID,
                                        PosiDirection,
                                        HedgeFlag,
                                        PositionDate,
                                        0,
                                        0,
                                        volume);
                        }
                    }
                    catch
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public void GetPositions(
            string InstrumentID,
            TZQThostFtdcPosiDirectionType PosiDirection,
            TZQThostFtdcHedgeFlagType HedgeFlag,
            out int YdPosition,
            out int TodayPosition,
            out int nLongFrozen,
            out int nShortFrozen)
        {
            YdPosition = 0;
            TodayPosition = 0;
            nLongFrozen = 0;
            nShortFrozen = 0;

            DataView view = dtInvestorPosition.DefaultView;
            view.RowFilter = string.Format("InstrumentID='{0}' and PosiDirection={1} and HedgeFlag={2}",
                        InstrumentID,
                        (int)PosiDirection,
                        (int)HedgeFlag);

            foreach (DataRowView dr in view)
            {
                int vol = (int)dr[Position];
                int iLongFrozen = (int)dr[LongFrozen];
                int iShortFrozen = (int)dr[ShortFrozen];
                TZQThostFtdcPositionDateType PositionDate1 = (TZQThostFtdcPositionDateType)dr[PositionDate];
                if (TZQThostFtdcPositionDateType.Today == PositionDate1)
                {
                    TodayPosition += vol;
                    nLongFrozen += iLongFrozen;
                    nShortFrozen += iShortFrozen;
                }
                else
                {
                    YdPosition += vol;
                    nLongFrozen += iLongFrozen;
                    nShortFrozen += iShortFrozen;
                }
            }
        }

        public DataRow[] Select(string InstrumentID, TZQThostFtdcPosiDirectionType PosiDirection, TZQThostFtdcHedgeFlagType HedgeFlag, TZQThostFtdcPositionDateType PositionDate)
        {
            return dtInvestorPosition.Select(
                string.Format("InstrumentID='{0}' and PosiDirection={1} and HedgeFlag={2} and PositionDate={3}",
                        InstrumentID,
                        (int)PosiDirection,
                        (int)HedgeFlag,
                        (int)PositionDate));
        }

        public DataRow[] SelectAll()
        {
            return dtInvestorPosition.Select();
        }

        public bool UpdateByTrade(CZQThostFtdcTradeField pTrade)
        {
            lock(this)
            {
                TZQThostFtdcPosiDirectionType PosiDirection = TZQThostFtdcPosiDirectionType.Net;
                TZQThostFtdcPositionDateType PositionDate = TZQThostFtdcPositionDateType.Today;
                TZQThostFtdcHedgeFlagType HedgeFlag = TZQThostFtdcHedgeFlagType.Speculation;

                return InsertOrReplaceForTrade(
                    pTrade.InstrumentID,
                    PosiDirection,
                    pTrade.Direction,
                    HedgeFlag,
                    PositionDate,
                    pTrade.Volume);
            }
        }

        public void Clear()
        {
            lock(this)
            {
                dtInvestorPosition.Clear();
            }
        }

        public void Save()
        {
            //dtInvestorPosition.WriteXml("D:\\1.xml");
        }
    }
}
