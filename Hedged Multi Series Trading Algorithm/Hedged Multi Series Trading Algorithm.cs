// ----------------------------------------------------------------------------------------------------------------------------------------------------
//                                                  PROITOTYPE HEDGED MULTI-SERIES FOREX TRADING ALGORITHM V0.1.1
//
//    This is an automated forex trading algorithm. 
//
//    The applications function is:
//    1. Simultaniously initiate long and short positions on EURUSD and USDCHF currency pairs. 
//       For example:
//           BUY EURUSD and BUY EURCHF (long pair)
//           SELL EURUSD and SELL EURCHF (short pair)
//       This initially results in x4 open positions. 
//    2. Under price action either the long pair or the short pair will become profitable. This is because EURUSD and USDCHF are only around 95% inversley 
//       correlated, so during the 5% correlation the two will move in sync. Eventually this will result in one of the positions becomming profitable.
//       During this stage the positions are fully hedged. 
//    3. When either the long pair or short pair hit the profit target the relevant positions are closed and the profit is banked.
//    4. The remaining pair will be closed as soon as possible so a break even flag is set and once the net value is marginally greater than zero the 
//       remaining positions are closed. During this stage the open opsitions are partially hedged but subject to the effects of the 5% of the time the 
//       pairs are correlated.
//
//    Modifications:
//    0.1.1 Where one of the long or short pairs had been closed the status of the ExitAtBreakEven flag was lost if the application is stopped and  
//          restarted. Code was added to the OnStart() method to check to see if buy or sell pairs (but not both) are open. If they are then the application
//          is at step 4 above the ExitAtBreakEven flag is set to true. 
//
// ----------------------------------------------------------------------------------------------------------------------------------------------------


using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Requests;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class HedgedMultiSeriesTradingAlgorithm : Robot
    {
        [Parameter("Source")]
        public DataSeries Source { get; set; }

        [Parameter("Volume", DefaultValue = 130000, MinValue = 1000)]
        public int Volume { get; set; }

        [Parameter("Profit target in pips", DefaultValue = 45, MinValue = 0.01, Step = 0.01)]
        public double TakeProfitInPips { get; set; }

        private Symbol EURUSDSymbol;
        private Symbol USDCHFSymbol;

        private const string EURUSDSymbolCode = "EURUSD";
        private const string USDCHFSymbolCode = "USDCHF";
        private const string BuyLabel = "HMSTA-BUY";
        private const string SellLabel = "HMSTA-SELL";

        private bool ExitAtBreakEven = false;

        protected override void OnStart()
        {
            EURUSDSymbol = Symbols.GetSymbol(EURUSDSymbolCode);
            USDCHFSymbol = Symbols.GetSymbol(USDCHFSymbolCode);

            ExitAtBreakEven = false;

            ///<summary>
            /// This statement is to ensure the ExitAtBreakEven flag is set correctly if the application is restarted in the middle of a trade.
            /// Specifically, if TotalBuyPips or TotalSellPips is greater than the TakeProfitInPip value the profit is taken and the pair closed,
            /// leaving the remaining pair. The ExitAtBreakEven flag is set to true to close the remaining pair off as soon as possible.
            /// 
            /// If the application stops and is estarted the status of the ExitAtBreakEven flag is lost. his check will set the ExitAtBreakEven flag 
            /// to true if any pair ( buy or sell, but not both) is still active.  
            ///</summary>
            if ((Positions.FindAll(BuyLabel).Length > 0 && Positions.FindAll(SellLabel).Length == 0) || (Positions.FindAll(BuyLabel).Length == 0 && Positions.FindAll(SellLabel).Length > 0))
            {
                ExitAtBreakEven = true;
            }

            Print("Break even flag = ", ExitAtBreakEven);

        }

        protected override void OnTick()
        {
            var OpenBuyPositions = Positions.FindAll(BuyLabel);
            var OpenSellPositions = Positions.FindAll(SellLabel);

            ///<summary>
            /// If there are no open positions then wait until the speads are in range (0.4 pips for EURUSD and 0.7 pips for USDCHF) and 
            /// simultaniously execute market orders.   
            /// </summary>
            if (OpenBuyPositions.Length == 0 && OpenSellPositions.Length == 0)
            {
                var EURUSDSpread = (EURUSDSymbol.Ask - EURUSDSymbol.Bid) / EURUSDSymbol.PipSize;
                var USDCHFSpread = (USDCHFSymbol.Ask - USDCHFSymbol.Bid) / USDCHFSymbol.PipSize;


                if (EURUSDSpread <= 0.4 && USDCHFSpread <= 0.7)
                {
                    ExecuteMarketOrder(TradeType.Buy, EURUSDSymbolCode, Volume, BuyLabel, 0, 0);
                    ExecuteMarketOrder(TradeType.Buy, USDCHFSymbolCode, Volume, BuyLabel, 0, 0);

                    ExecuteMarketOrder(TradeType.Sell, EURUSDSymbolCode, Volume, SellLabel, 0, 0);
                    ExecuteMarketOrder(TradeType.Sell, USDCHFSymbolCode, Volume, SellLabel, 0, 0);

                    Print("New set of market orders submitted");
                    ExitAtBreakEven = false;
                }

            }

            if (OpenBuyPositions.Length > 0)
            {
                ///<summary>
                /// If ExitAtBreakEven is true than close all open buy positions when Net profit > 0. Otherwise close all open buy positions when 
                /// the total buy pips are greater than TakeProfitInPips. 
                /// </summary>
                if (ExitAtBreakEven)
                {
                    var NetBuyProfit = OpenBuyPositions.Sum(x => x.NetProfit);

                    if (NetBuyProfit >= 0)
                    {
                        foreach (var position in OpenBuyPositions)
                        {
                            ClosePosition(position);
                        }
                        Print("Buy positions closed at break even");
                        ExitAtBreakEven = false;
                    }
                }
                else
                {
                    var TotalBuyPips = OpenBuyPositions.Sum(x => x.Pips);

                    if (TotalBuyPips >= TakeProfitInPips)
                    {
                        foreach (var position in OpenBuyPositions)
                        {
                            ClosePosition(position);
                        }
                        Print("Buy positions closed at profit target ", TotalBuyPips);
                        ExitAtBreakEven = true;
                    }
                }

            }

            ///<summary>
            /// If ExitAtBreakEven is true than close all open sell positions when Net profit > 0. Otherwise close all open sell positions when 
            /// the total sell pips are greater than TakeProfitInPips. 
            /// </summary>
            if (OpenSellPositions.Length > 0)
            {
                if (ExitAtBreakEven)
                {
                    var NetSellProfit = OpenSellPositions.Sum(x => x.NetProfit);

                    if (NetSellProfit >= 0)
                    {
                        foreach (var position in OpenSellPositions)
                        {
                            ClosePosition(position);
                        }
                        Print("Sell positions closed at break even");
                        ExitAtBreakEven = false;
                    }
                }
                else
                {
                    var TotalSellPips = OpenSellPositions.Sum(x => x.Pips);

                    if (TotalSellPips >= TakeProfitInPips)
                    {
                        foreach (var position in OpenSellPositions)
                        {
                            ClosePosition(position);
                        }
                        Print("Sell positions closed at profit target ", TotalSellPips);
                        ExitAtBreakEven = true;
                    }
                }

            }
        }

        protected override void OnBar()
        {
            ///<summary>
            /// Write stats to the log
            /// </summary>
            var OpenBuyPositions = Positions.FindAll(BuyLabel);

            if (OpenBuyPositions.Length > 0)
            {
                var TotalBuyPips = OpenBuyPositions.Sum(x => x.Pips);
                var NetBuyProfit = OpenBuyPositions.Sum(x => x.NetProfit);

                Print("Total buy pips: ", TotalBuyPips, " |", " Net buy profit: ", NetBuyProfit);
            }

            var OpenSellPositions = Positions.FindAll(SellLabel);

            if (OpenSellPositions.Length > 0)
            {
                var TotalSellPips = OpenSellPositions.Sum(x => x.Pips);
                var NetSellProfit = OpenSellPositions.Sum(x => x.NetProfit);

                Print("Total sell pips: ", TotalSellPips, " |", " Net sell profit: ", NetSellProfit);
            }

        }

        protected override void OnStop()
        {
            Print("Execution stopped");
        }
    }
}
