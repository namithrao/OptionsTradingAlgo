import { useState, useEffect } from 'react';
import { TradingChart } from './components/TradingChart';
import { SignalRService } from './services/signalr';
import type { MarketData, OptionData } from './services/signalr';
import type { CandlestickData, LineData } from 'lightweight-charts';
import * as Tabs from '@radix-ui/react-tabs';
import { TrendingUp, BarChart3, Settings, Play, Pause } from 'lucide-react';

interface AppState {
  marketData: CandlestickData[];
  optionData: LineData[];
  selectedSymbol: string;
  isConnected: boolean;
  backtestRunning: boolean;
}

function App() {
  const [state, setState] = useState<AppState>({
    marketData: [],
    optionData: [],
    selectedSymbol: 'SPY',
    isConnected: false,
    backtestRunning: false,
  });

  const [signalRService] = useState(new SignalRService());

  useEffect(() => {
    // Initialize SignalR connections
    const initializeConnections = async () => {
      try {
        await signalRService.connectMarketHub('https://localhost:7238');
        
        signalRService.onMarketData((data: MarketData) => {
          const candlestick: CandlestickData = {
            time: data.time as any,
            open: data.open,
            high: data.high,
            low: data.low,
            close: data.close,
          };
          
          setState(prev => ({
            ...prev,
            marketData: [...prev.marketData.slice(-100), candlestick], // Keep last 100 candles
          }));
        });

        signalRService.onOptionData((data: OptionData) => {
          const optionPoint: LineData = {
            time: (Date.now() / 1000) as any,
            value: data.price,
          };
          
          setState(prev => ({
            ...prev,
            optionData: [...prev.optionData.slice(-100), optionPoint],
          }));
        });

        setState(prev => ({ ...prev, isConnected: true }));
        
        // Subscribe to initial symbol
        await signalRService.subscribeToSymbol('SPY');
      } catch (error) {
        console.error('Failed to connect to SignalR:', error);
      }
    };

    initializeConnections();

    return () => {
      signalRService.disconnect();
    };
  }, [signalRService]);

  const handleSymbolChange = async (symbol: string) => {
    if (state.isConnected) {
      await signalRService.unsubscribeFromSymbol(state.selectedSymbol);
      await signalRService.subscribeToSymbol(symbol);
      setState(prev => ({ 
        ...prev, 
        selectedSymbol: symbol,
        marketData: [],
        optionData: []
      }));
    }
  };

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="bg-white shadow-sm border-b">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between items-center py-4">
            <div className="flex items-center space-x-4">
              <TrendingUp className="h-8 w-8 text-blue-600" />
              <h1 className="text-2xl font-bold text-gray-900">Options Trading System</h1>
            </div>
            <div className="flex items-center space-x-4">
              <div className={`h-3 w-3 rounded-full ${state.isConnected ? 'bg-green-500' : 'bg-red-500'}`} />
              <span className="text-sm text-gray-600">
                {state.isConnected ? 'Connected' : 'Disconnected'}
              </span>
            </div>
          </div>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6">
        <Tabs.Root defaultValue="chart" className="space-y-6">
          <Tabs.List className="flex space-x-1 bg-blue-50 p-1 rounded-lg">
            <Tabs.Trigger 
              value="chart"
              className="flex items-center space-x-2 px-4 py-2 rounded-md text-sm font-medium data-[state=active]:bg-white data-[state=active]:shadow-sm"
            >
              <BarChart3 className="h-4 w-4" />
              <span>Chart</span>
            </Tabs.Trigger>
            <Tabs.Trigger 
              value="strategies"
              className="flex items-center space-x-2 px-4 py-2 rounded-md text-sm font-medium data-[state=active]:bg-white data-[state=active]:shadow-sm"
            >
              <Settings className="h-4 w-4" />
              <span>Strategies</span>
            </Tabs.Trigger>
            <Tabs.Trigger 
              value="results"
              className="flex items-center space-x-2 px-4 py-2 rounded-md text-sm font-medium data-[state=active]:bg-white data-[state=active]:shadow-sm"
            >
              <TrendingUp className="h-4 w-4" />
              <span>Results</span>
            </Tabs.Trigger>
          </Tabs.List>

          <Tabs.Content value="chart" className="space-y-6">
            <div className="bg-white rounded-lg shadow p-6">
              <div className="flex items-center justify-between mb-4">
                <div className="flex items-center space-x-4">
                  <label className="text-sm font-medium text-gray-700">Symbol:</label>
                  <select 
                    value={state.selectedSymbol}
                    onChange={(e) => handleSymbolChange(e.target.value)}
                    className="border border-gray-300 rounded-md px-3 py-1 text-sm"
                  >
                    <option value="SPY">SPY</option>
                    <option value="QQQ">QQQ</option>
                    <option value="IWM">IWM</option>
                    <option value="AAPL">AAPL</option>
                    <option value="MSFT">MSFT</option>
                  </select>
                </div>
                <div className="text-sm text-gray-500">
                  {state.marketData.length} data points
                </div>
              </div>
              
              <TradingChart 
                data={state.marketData}
                optionData={state.optionData}
                width={800}
                height={500}
              />
            </div>
          </Tabs.Content>

          <Tabs.Content value="strategies" className="space-y-6">
            <div className="bg-white rounded-lg shadow p-6">
              <h2 className="text-xl font-semibold mb-4">Strategy Configuration</h2>
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                <div className="space-y-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                      Strategy Type
                    </label>
                    <select className="w-full border border-gray-300 rounded-md px-3 py-2">
                      <option value="covered-call">Covered Call</option>
                      <option value="cash-secured-put">Cash-Secured Put</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                      Initial Capital
                    </label>
                    <input 
                      type="number" 
                      defaultValue={100000}
                      className="w-full border border-gray-300 rounded-md px-3 py-2"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                      Delta Range
                    </label>
                    <div className="flex space-x-2">
                      <input 
                        type="number" 
                        defaultValue={0.15} 
                        step={0.01}
                        className="flex-1 border border-gray-300 rounded-md px-3 py-2"
                      />
                      <span className="self-center">to</span>
                      <input 
                        type="number" 
                        defaultValue={0.30} 
                        step={0.01}
                        className="flex-1 border border-gray-300 rounded-md px-3 py-2"
                      />
                    </div>
                  </div>
                </div>
                <div className="space-y-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                      Days to Expiration
                    </label>
                    <input 
                      type="number" 
                      defaultValue={30}
                      className="w-full border border-gray-300 rounded-md px-3 py-2"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">
                      Commission per Contract
                    </label>
                    <input 
                      type="number" 
                      defaultValue={0.65} 
                      step={0.01}
                      className="w-full border border-gray-300 rounded-md px-3 py-2"
                    />
                  </div>
                  <button 
                    className="w-full bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700 flex items-center justify-center space-x-2"
                    onClick={() => setState(prev => ({ ...prev, backtestRunning: !prev.backtestRunning }))}
                  >
                    {state.backtestRunning ? (
                      <>
                        <Pause className="h-4 w-4" />
                        <span>Stop Backtest</span>
                      </>
                    ) : (
                      <>
                        <Play className="h-4 w-4" />
                        <span>Run Backtest</span>
                      </>
                    )}
                  </button>
                </div>
              </div>
            </div>
          </Tabs.Content>

          <Tabs.Content value="results" className="space-y-6">
            <div className="bg-white rounded-lg shadow p-6">
              <h2 className="text-xl font-semibold mb-4">Backtest Results</h2>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
                <div className="text-center p-4 bg-gray-50 rounded-lg">
                  <div className="text-2xl font-bold text-green-600">+12.5%</div>
                  <div className="text-sm text-gray-600">Total Return</div>
                </div>
                <div className="text-center p-4 bg-gray-50 rounded-lg">
                  <div className="text-2xl font-bold text-blue-600">1.23</div>
                  <div className="text-sm text-gray-600">Sharpe Ratio</div>
                </div>
                <div className="text-center p-4 bg-gray-50 rounded-lg">
                  <div className="text-2xl font-bold text-red-600">-5.2%</div>
                  <div className="text-sm text-gray-600">Max Drawdown</div>
                </div>
              </div>
              <div className="mt-6">
                <h3 className="text-lg font-medium mb-2">Recent Trades</h3>
                <div className="overflow-x-auto">
                  <table className="min-w-full table-auto">
                    <thead>
                      <tr className="border-b">
                        <th className="text-left py-2">Date</th>
                        <th className="text-left py-2">Symbol</th>
                        <th className="text-left py-2">Type</th>
                        <th className="text-left py-2">Strike</th>
                        <th className="text-left py-2">P&L</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr className="border-b">
                        <td className="py-2">2024-01-15</td>
                        <td className="py-2">SPY</td>
                        <td className="py-2">Call</td>
                        <td className="py-2">$485</td>
                        <td className="py-2 text-green-600">+$125</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </div>
            </div>
          </Tabs.Content>
        </Tabs.Root>
      </main>
    </div>
  );
}

export default App
