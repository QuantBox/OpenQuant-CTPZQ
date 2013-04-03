using System;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;
using QuantBox.CSharp2CTPZQ;
using SmartQuant;
using System.Drawing.Design;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Serialization;
using System.IO;

namespace QuantBox.OQ.CTPZQ
{
    partial class CTPZQProvider
    {
        private const string CATEGORY_ACCOUNT = "Account";
        private const string CATEGORY_BARFACTORY = "Bar Factory";
        private const string CATEGORY_DEBUG = "Debug";
        private const string CATEGORY_EXECUTION = "Settings - Execution";
        private const string CATEGORY_HISTORICAL = "Settings - Historical Data";
        private const string CATEGORY_INFO = "Information";
        private const string CATEGORY_NETWORK = "Settings - Network";
        private const string CATEGORY_STATUS = "Status";
        private const string CATEGORY_OTHER = "Settings - Other";

        //交易所常量定义
        private enum ExchangID
        {
            SSE,
            SZE
        }

        public enum TimeMode
        {
            LocalTime,
            ExchangeTime
        }

        public enum SetTimeMode
        {
            None,
            LoginTime,
            SHFETime,
            DCETime,
            CZCETime,
            FFEXTime
        }

        private const string OpenPrefix = "O|";
        private const string ClosePrefix = "C|";
        private const string CloseTodayPrefix = "T|";
        private const string CloseYesterdayPrefix = "Y|";

        #region 参数设置
        private string _ApiTempPath;
        private TimeMode _TimeMode;
        private ZQTHOST_TE_RESUME_TYPE _ResumeType;
        //private string _SupportMarketOrder;
        //private string _SupportCloseToday;
        //private string _DefaultOpenClosePrefix;

        [Category("Settings - Other")]
        [Description("设置API生成临时文件的目录")]
        [Editor(typeof(System.Windows.Forms.Design.FolderNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        [Browsable(false)]
        public string ApiTempPath
        {
            get { return _ApiTempPath; }
            set { _ApiTempPath = value; }
        }


        [Category("Settings - Time")]
        [Description("警告！仅保存行情数据时才用交易所时间。交易时使用交易所时间将导致Bar生成错误")]
        [DefaultValue(TimeMode.LocalTime)]
        public TimeMode DateTimeMode
        {
            get { return _TimeMode; }
            set { _TimeMode = value; }
        }

        [Category("Settings - Time")]
        [Description("修改本地时间。分别是：不修改、登录交易前置机时间、各大交易所时间。以管理员方式运行才有权限")]
        [DefaultValue(SetTimeMode.None)]
        public SetTimeMode SetLocalTimeMode
        {
            get;
            set;
        }

        [Category("Settings - Time")]
        [DefaultValue(0)]
        [Description("修改本地时间时，在取到的时间上添加指定毫秒")]
        public int AddMilliseconds
        {
            get;
            set;
        }

        [Category("Settings - Other")]
        [Description("设置登录后是否接收完整的报单和成交记录")]
        [DefaultValue(ZQTHOST_TE_RESUME_TYPE.ZQTHOST_TERT_QUICK)]
        public ZQTHOST_TE_RESUME_TYPE ResumeType
        {
            get { return _ResumeType; }
            set { _ResumeType = value; }
        }

        [Category("Settings - Order")]
        [Description("设置投机套保标志。Speculation:投机、Arbitrage套利、Hedge套保")]
        [DefaultValue(TZQThostFtdcHedgeFlagType.Speculation)]
        public TZQThostFtdcHedgeFlagType HedgeFlagType
        {
            get;
            set;
        }

        //[Category("Settings - Order")]
        //[Description("支持市价单的交易所")]
        //public string SupportMarketOrder
        //{
        //    get { return _SupportMarketOrder; }
        //}


        //[Category("Settings - Order")]
        //[Description("区分平今与平昨的交易所")]
        //public string SupportCloseToday
        //{
        //    get { return _SupportCloseToday; }
        //}

        //[Category("Settings - Order")]
        //[Description("指定开平，利用Order的Text域开始部分指定开平，“O|”开仓；“C|”智能平仓；“T|”平今仓；“Y|”平昨仓；")]
        //public string DefaultOpenClosePrefix
        //{
        //    get { return _DefaultOpenClosePrefix; }
        //}

        [Category("Settings - Order")]
        [Description("在最新价上调整N跳来模拟市价，超过涨跌停价按涨跌停价报")]
        [DefaultValue(10)]
        public int LastPricePlusNTicks
        {
            get;
            set;
        }

        [Category(CATEGORY_OTHER)]
        [Description("True - 产生OnRtnDepthMarketData事件\nFalse - 不产生OnRtnDepthMarketData事件")]
        [DefaultValue(false)]
        //[Browsable(false)]
        public bool EmitOnRtnDepthMarketData
        {
            get;
            set;
        }

        private BindingList<ServerItem> serversList = new BindingList<ServerItem>();
        [CategoryAttribute("Settings")]
        [Description("服务器信息，只选择第一条登录")]
        public BindingList<ServerItem> Server
        {
            get { return serversList; }
            set { serversList = value; }
        }

        private BindingList<AccountItem> accountsList = new BindingList<AccountItem>();
        [CategoryAttribute("Settings")]
        [Description("账号信息，只选择第一条登录")]
        public BindingList<AccountItem> Account
        {
            get { return accountsList; }
            set { accountsList = value; }
        }

        private BindingList<BrokerItem> brokersList = new BindingList<BrokerItem>();
        [Category("Settings"), Editor(typeof(ServersManagerTypeEditor), typeof(UITypeEditor)),
        Description("点击(...)查看经纪商列表")]
        public BindingList<BrokerItem> Brokers
        {
            get { return brokersList; }
            set { brokersList = value; }
        }

        [CategoryAttribute(CATEGORY_INFO)]
        [Description("插件版本信息")]
        public string Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
        }

        [CategoryAttribute("Settings")]
        [Description("连接到行情。此插件不连接行情时底层对不支持市价的报单不会做涨跌停修正，需策略层处理")]
        [DefaultValue(true)]
        public bool ConnectToMarketData
        {
            get { return _bWantMdConnect; }
            set { _bWantMdConnect = value; }
        }

        [CategoryAttribute("Settings")]
        [Description("连接到交易")]
        [DefaultValue(true)]
        public bool ConnectToTrading
        {
            get { return _bWantTdConnect; }
            set { _bWantTdConnect = value; }
        }

        #endregion
        private void InitSettings()
        {
            ApiTempPath = Framework.Installation.TempDir.FullName;
            ResumeType = ZQTHOST_TE_RESUME_TYPE.ZQTHOST_TERT_QUICK;
            HedgeFlagType = TZQThostFtdcHedgeFlagType.Speculation;

            _bWantMdConnect = true;
            _bWantTdConnect = true;

            //_SupportMarketOrder = ExchangID.DCE.ToString() + ";" + ExchangID.CZCE.ToString() + ";" + ExchangID.CFFEX.ToString() + ";";
            //_SupportCloseToday = ExchangID.SHFE.ToString() + ";";
            //_DefaultOpenClosePrefix = OpenPrefix + ";" + ClosePrefix+";"+CloseTodayPrefix + ";" + CloseYesterdayPrefix;
            LastPricePlusNTicks = 10;

            LoadAccounts();
            LoadServers();

            serversList.ListChanged += ServersList_ListChanged;
            accountsList.ListChanged += AccountsList_ListChanged;
        }

        void ServersList_ListChanged(object sender, ListChangedEventArgs e)
        {
            if (e.ListChangedType == ListChangedType.ItemAdded) {
                serversList[e.NewIndex].Changed += new EventHandler(ServerItem_ListChanged);
            }
            SettingsChanged();
        }

        void AccountsList_ListChanged(object sender, EventArgs e)
        {
            SettingsChanged();
        }

        void ServerItem_ListChanged(object sender, EventArgs e)
        {
            SettingsChanged();
        }

        public void SettingsChanged()
        {
            SaveAccounts();
            SaveServers();
        }

        private string accountsFile = string.Format(@"{0}\CTPZQ.Accounts.xml", Framework.Installation.IniDir);
        void LoadAccounts()
        {
            accountsList.Clear();

            try
            {
                XmlSerializer serializer = new XmlSerializer(accountsList.GetType());
                using (FileStream stream = new FileStream(accountsFile, FileMode.Open))
                {
                    accountsList = (BindingList<AccountItem>)serializer.Deserialize(stream);
                    stream.Close();
                }
            }
            catch (Exception)
            {
            }            
        }

        void SaveAccounts()
        {
            XmlSerializer serializer = new XmlSerializer(accountsList.GetType());
            using (TextWriter writer = new StreamWriter(accountsFile))
            {
                serializer.Serialize(writer, accountsList);
                writer.Close();
            }
        }

        private string serversFile = string.Format(@"{0}\CTPZQ.Servers.xml", Framework.Installation.IniDir);
        void LoadServers()
        {
            serversList.Clear();

            try
            {
                XmlSerializer serializer = new XmlSerializer(serversList.GetType());
                using (FileStream stream = new FileStream(serversFile, FileMode.Open))
                {
                    serversList = (BindingList<ServerItem>)serializer.Deserialize(stream);
                    stream.Close();
                }
            }
            catch (Exception)
            {
            }
        }
        
        void SaveServers()
        {
            XmlSerializer serializer = new XmlSerializer(serversList.GetType());
            using (TextWriter writer = new StreamWriter(serversFile))
            {
                serializer.Serialize(writer, serversList);
                writer.Close();
            }
        }

        private readonly string brokersFile = string.Format(@"{0}\CTPZQ.Brokers.xml", Framework.Installation.IniDir);
        public void LoadBrokers()
        {
            brokersList.Clear();

            try
            {
                XmlSerializer serializer = new XmlSerializer(brokersList.GetType());
                using (FileStream stream = new FileStream(brokersFile, FileMode.Open))
                {
                    brokersList = (BindingList<BrokerItem>)serializer.Deserialize(stream);
                    stream.Close();
                }
            }
            catch (Exception)
            {
            }
        }

        void SaveBrokers()
        {
            XmlSerializer serializer = new XmlSerializer(brokersList.GetType());
            using (TextWriter writer = new StreamWriter(brokersFile+"a"))
            {
                serializer.Serialize(writer, brokersList);
                writer.Close();
            }
        }
    }
}
