'use client';

import { useState, useEffect, useCallback } from 'react';
import MarketDataService from '../services/MarketDataService';

interface OptionsChainProps {
  symbol: string;
}

interface OptionData {
  strike: number;
  calls: {
    bid: number;
    ask: number;
    iv: number;
    delta: number;
    gamma: number;
    volume?: number;
  };
  puts: {
    bid: number;
    ask: number;
    iv: number;
    delta: number;
    gamma: number;
    volume?: number;
  };
}

interface OptionsChainData {
  symbol: string;
  timestamp: string;
  strikes: OptionData[];
}

const OptionsChain: React.FC<OptionsChainProps> = ({ symbol }) => {
  const [chainData, setChainData] = useState<OptionsChainData | null>(null);
  const [loading, setLoading] = useState(false);
  const [selectedExpiry, setSelectedExpiry] = useState('30D');
  const [currentStockPrice, setCurrentStockPrice] = useState<number | null>(null);
  const [stockPriceLoading, setStockPriceLoading] = useState(false);

  const fetchCurrentStockPrice = useCallback(async () => {
    setStockPriceLoading(true);
    try {
      const marketService = new MarketDataService('http://localhost:5002');
      const response = await marketService.getStockPrice(symbol);
      
      if (response.success) {
        setCurrentStockPrice(response.price);
      } else {
        console.error('Failed to fetch stock price:', response);
        // Fallback to hardcoded price
        setCurrentStockPrice(getBasePrice(symbol));
      }
    } catch (error) {
      console.error('Error fetching current stock price:', error);
      // Fallback to hardcoded price
      setCurrentStockPrice(getBasePrice(symbol));
    } finally {
      setStockPriceLoading(false);
    }
  }, [symbol]);

  const fetchOptionsChain = useCallback(async () => {
    setLoading(true);
    try {
      // Generate mock strikes inline to avoid circular dependencies
      const basePrice = currentStockPrice || getBasePrice(symbol);
      const daysToExpiry = getDaysFromExpiry(selectedExpiry);
      const timeDecayFactor = Math.max(0.1, daysToExpiry / 30);
      const strikes = [];
      
      // Generate strikes around current price
      for (let i = -10; i <= 10; i++) {
        const strike = Math.round((basePrice + i * 5) * 100) / 100;
        const isITM = i < 0;
        const isOTM = i > 0;
        const distance = Math.abs(i);
        
        // Adjust pricing based on time to expiry  
        const callIntrinsic = Math.max(0, basePrice - strike);
        const putIntrinsic = Math.max(0, strike - basePrice);
        const timeValue = timeDecayFactor * (1.5 + Math.random() * 2);
        
        // Add base premium for all options to ensure visibility
        const basePremium = 0.15 + (timeDecayFactor * 0.3);
        
        strikes.push({
          strike,
          calls: {
            bid: Math.max(0.05, callIntrinsic + timeValue + basePremium - 0.15),
            ask: Math.max(0.10, callIntrinsic + timeValue + basePremium + 0.15),
            iv: 0.15 + distance * 0.02 + (daysToExpiry / 100) * 0.1 + (Math.random() - 0.5) * 0.05,
            delta: isITM ? 0.8 + Math.random() * 0.15 : Math.max(0.05, 0.5 - distance * 0.05),
            gamma: Math.max(0.005, (0.05 - distance * 0.005) * timeDecayFactor),
            volume: Math.floor(Math.random() * 1000 * (timeDecayFactor + 0.2))
          },
          puts: {
            bid: Math.max(0.05, putIntrinsic + timeValue + basePremium - 0.15),
            ask: Math.max(0.10, putIntrinsic + timeValue + basePremium + 0.15),
            iv: 0.18 + distance * 0.025 + (daysToExpiry / 100) * 0.1 + (Math.random() - 0.5) * 0.05,
            delta: isOTM ? -(0.8 + Math.random() * 0.15) : Math.min(-0.05, -0.5 + distance * 0.05),
            gamma: Math.max(0.005, (0.05 - distance * 0.005) * timeDecayFactor),
            volume: Math.floor(Math.random() * 800 * (timeDecayFactor + 0.2))
          }
        });
      }

      const mockData: OptionsChainData = {
        symbol,
        timestamp: new Date().toISOString(),
        strikes
      };
      
      setChainData(mockData);
    } catch (error) {
      console.error('Error fetching options chain:', error);
    } finally {
      setLoading(false);
    }
  }, [symbol, selectedExpiry, currentStockPrice]);

  useEffect(() => {
    fetchCurrentStockPrice();
  }, [fetchCurrentStockPrice]);

  useEffect(() => {
    fetchOptionsChain();
  }, [fetchOptionsChain]);

  const getDaysFromExpiry = (expiry: string): number => {
    const days = parseInt(expiry.replace('D', ''));
    return days;
  };

  const getBasePrice = (symbol: string): number => {
    const prices: { [key: string]: number } = {
      'SPY': 450,
      'QQQ': 380,
      'AAPL': 190,
      'MSFT': 410,
      'TSLA': 250,
      'NVDA': 900
    };
    return prices[symbol] || 100;
  };

  const formatCurrency = (value: number) => {
    return `$${value.toFixed(2)}`;
  };

  const formatPercent = (value: number) => {
    return `${(value * 100).toFixed(1)}%`;
  };

  const formatDelta = (value: number) => {
    return value > 0 ? `+${value.toFixed(3)}` : value.toFixed(3);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-500"></div>
      </div>
    );
  }

  if (!chainData) {
    return (
      <div className="text-center text-gray-500 dark:text-gray-400 py-8">
        No options data available
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Header with stock price, timestamp and expiry selector */}
      <div className="space-y-4 mb-6">
        {/* Stock Price Display */}
        <div className="bg-gray-50 dark:bg-gray-800 rounded-lg p-4">
          <div className="flex items-center justify-between">
            <div>
              <h3 className="text-lg font-semibold text-gray-900 dark:text-white">
                {chainData.symbol} Stock Price
              </h3>
              <div className="flex items-center space-x-4 mt-1">
                <span className="text-2xl font-bold text-blue-600 dark:text-blue-400">
                  {stockPriceLoading ? (
                    <span className="animate-pulse">Loading...</span>
                  ) : (
                    `$${(currentStockPrice || getBasePrice(chainData.symbol)).toFixed(2)}`
                  )}
                </span>
                <span className="text-sm text-green-600 dark:text-green-400">
                  +$2.45 (+0.54%) Today
                </span>
              </div>
            </div>
            <div className="text-right">
              <div className="text-sm text-gray-600 dark:text-gray-400">
                <div><span className="font-medium">Last Updated:</span> {new Date(chainData.timestamp).toLocaleString()}</div>
                <div className="text-xs text-gray-500 mt-1">⚠️ Demo Data</div>
              </div>
            </div>
          </div>
        </div>

        {/* Expiry Selector */}
        <div className="flex items-center justify-between">
          <div className="text-sm text-gray-600 dark:text-gray-400">
            Options Chain for {chainData.symbol}
          </div>
          <div className="flex flex-col items-end space-y-1">
            <label className="text-xs text-gray-600 dark:text-gray-400">Days to Expiry</label>
            <select 
              value={selectedExpiry}
              onChange={(e) => setSelectedExpiry(e.target.value)}
              className="bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 text-sm rounded px-3 py-1"
            >
              <option value="1D">1 Day</option>
              <option value="7D">7 Days</option>
              <option value="14D">14 Days</option>
              <option value="30D">30 Days</option>
              <option value="60D">60 Days</option>
              <option value="90D">90 Days</option>
            </select>
          </div>
        </div>
      </div>

      {/* Options Chain Table */}
      <div className="overflow-x-auto">
        <table className="min-w-full text-sm">
          <thead>
            <tr className="border-b border-gray-200 dark:border-gray-700">
              <th colSpan={6} className="px-3 py-3 text-center font-bold text-lg text-green-800 dark:text-green-200 bg-green-100 dark:bg-green-900/40 border-r border-green-300">
                📈 CALLS - OPTIONS PRICES
              </th>
              <th className="px-3 py-3 text-center font-bold text-gray-900 dark:text-white bg-gray-100 dark:bg-gray-700">
                Strike Price
              </th>
              <th colSpan={6} className="px-3 py-3 text-center font-bold text-lg text-red-800 dark:text-red-200 bg-red-100 dark:bg-red-900/40 border-l border-red-300">
                📉 PUTS - OPTIONS PRICES
              </th>
            </tr>
            <tr className="border-b border-gray-200 dark:border-gray-700 text-xs text-gray-600 dark:text-gray-400">
              <th className="px-2 py-1 font-bold text-green-700 dark:text-green-300">Bid</th>
              <th className="px-2 py-1 font-bold text-green-700 dark:text-green-300">Ask</th>
              <th className="px-2 py-1 font-bold text-green-700 dark:text-green-300">Mark</th>
              <th className="px-2 py-1">IV</th>
              <th className="px-2 py-1">Delta</th>
              <th className="px-2 py-1">Vol</th>
              <th className="px-2 py-1 font-medium">Strike</th>
              <th className="px-2 py-1 font-bold text-red-700 dark:text-red-300">Bid</th>
              <th className="px-2 py-1 font-bold text-red-700 dark:text-red-300">Ask</th>
              <th className="px-2 py-1 font-bold text-red-700 dark:text-red-300">Mark</th>
              <th className="px-2 py-1">IV</th>
              <th className="px-2 py-1">Delta</th>
              <th className="px-2 py-1">Vol</th>
            </tr>
          </thead>
          <tbody>
            {chainData.strikes.map((option) => {
              const currentPrice = currentStockPrice || getBasePrice(chainData.symbol);
              const isITMCall = option.strike < currentPrice;
              const isITMPut = option.strike > currentPrice;
              
              return (
                <tr key={option.strike} className={`border-b border-gray-100 dark:border-gray-800 hover:bg-gray-50 dark:hover:bg-gray-800/50 ${
                  Math.abs(option.strike - currentPrice) <= 10 ? 'bg-yellow-50 dark:bg-yellow-900/20' : ''
                }`}>
                  {/* Calls */}
                  <td className={`px-2 py-2 text-lg font-bold bg-green-50 dark:bg-green-900/30 ${isITMCall ? 'text-green-800 dark:text-green-200' : 'text-green-700 dark:text-green-300'}`}>
                    {formatCurrency(option.calls.bid)}
                  </td>
                  <td className={`px-2 py-2 text-lg font-bold bg-green-50 dark:bg-green-900/30 ${isITMCall ? 'text-green-800 dark:text-green-200' : 'text-green-700 dark:text-green-300'}`}>
                    {formatCurrency(option.calls.ask)}
                  </td>
                  <td className={`px-2 py-2 text-lg font-extrabold bg-green-100 dark:bg-green-900/50 ${isITMCall ? 'text-green-900 dark:text-green-100' : 'text-green-800 dark:text-green-200'}`}>
                    {formatCurrency((option.calls.bid + option.calls.ask) / 2)}
                  </td>
                  <td className="px-2 py-2 text-sm">{formatPercent(option.calls.iv)}</td>
                  <td className="px-2 py-2 text-sm">{formatDelta(option.calls.delta)}</td>
                  <td className="px-2 py-2 text-xs text-gray-500">{option.calls.volume}</td>
                  
                  {/* Strike */}
                  <td className={`px-2 py-2 text-center font-bold ${
                    Math.abs(option.strike - currentPrice) <= 5 
                      ? 'text-blue-600 dark:text-blue-400 bg-blue-50 dark:bg-blue-900/30' 
                      : 'text-gray-900 dark:text-white'
                  }`}>
                    {formatCurrency(option.strike)}
                  </td>
                  
                  {/* Puts */}
                  <td className={`px-2 py-2 text-lg font-bold bg-red-50 dark:bg-red-900/30 ${isITMPut ? 'text-red-800 dark:text-red-200' : 'text-red-700 dark:text-red-300'}`}>
                    {formatCurrency(option.puts.bid)}
                  </td>
                  <td className={`px-2 py-2 text-lg font-bold bg-red-50 dark:bg-red-900/30 ${isITMPut ? 'text-red-800 dark:text-red-200' : 'text-red-700 dark:text-red-300'}`}>
                    {formatCurrency(option.puts.ask)}
                  </td>
                  <td className={`px-2 py-2 text-lg font-extrabold bg-red-100 dark:bg-red-900/50 ${isITMPut ? 'text-red-900 dark:text-red-100' : 'text-red-800 dark:text-red-200'}`}>
                    {formatCurrency((option.puts.bid + option.puts.ask) / 2)}
                  </td>
                  <td className="px-2 py-2 text-sm">{formatPercent(option.puts.iv)}</td>
                  <td className="px-2 py-2 text-sm">{formatDelta(option.puts.delta)}</td>
                  <td className="px-2 py-2 text-xs text-gray-500">{option.puts.volume}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
};

export default OptionsChain;