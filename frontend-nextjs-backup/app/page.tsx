'use client';

import { useState, useEffect } from 'react';
import { TrendingUp, TrendingDown, Activity, DollarSign } from 'lucide-react';
import OptionsChain from '../components/OptionsChain';
import StrategyBuilder from '../components/StrategyBuilder';
import MarketDataService from '../services/MarketDataService';

export default function Dashboard() {
  const [selectedSymbol, setSelectedSymbol] = useState('SPY');
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    const marketService = new MarketDataService('http://localhost:5002');
    
    marketService.connect().then(() => {
      setConnected(true);
    }).catch((error: Error) => {
      console.error('Failed to connect:', error);
    });

    // Cleanup on unmount
    return () => {
      marketService.disconnect();
    };
  }, []);

  const popularSymbols = ['SPY', 'QQQ', 'AAPL', 'MSFT', 'TSLA', 'NVDA'];

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-gray-900">
      {/* Header */}
      <header className="bg-white dark:bg-gray-800 shadow-sm border-b">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex items-center justify-between h-16">
            <div className="flex items-center">
              <h1 className="text-2xl font-bold text-gray-900 dark:text-white">
                Optx Trading Dashboard
              </h1>
              <div className="ml-4 flex items-center">
                <div className={`w-2 h-2 rounded-full ${connected ? 'bg-green-500' : 'bg-red-500'}`}></div>
                <span className="ml-2 text-sm text-gray-600 dark:text-gray-400">
                  {connected ? 'Connected' : 'Disconnected'}
                </span>
              </div>
            </div>
            <div className="flex items-center space-x-4">
              <select 
                value={selectedSymbol}
                onChange={(e) => setSelectedSymbol(e.target.value)}
                className="bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600 text-gray-900 dark:text-white text-sm rounded-lg focus:ring-blue-500 focus:border-blue-500 block w-full p-2.5"
              >
                {popularSymbols.map(symbol => (
                  <option key={symbol} value={symbol}>{symbol}</option>
                ))}
              </select>
            </div>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        {/* Demo Notice */}
        <div className="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4 mb-6">
          <div className="flex items-center">
            <div className="flex-shrink-0">
              <div className="w-2 h-2 bg-blue-500 rounded-full animate-pulse"></div>
            </div>
            <div className="ml-3">
              <p className="text-sm text-blue-800 dark:text-blue-200">
                <span className="font-medium">Demo Mode:</span> Portfolio values below are sample data for demonstration. 
                Real portfolio integration requires connecting your brokerage API.
              </p>
            </div>
          </div>
        </div>

        {/* Market Overview Cards */}
        <div className="grid grid-cols-1 md:grid-cols-4 gap-6 mb-8">
          <div className="bg-white dark:bg-gray-800 overflow-hidden shadow rounded-lg">
            <div className="p-5">
              <div className="flex items-center">
                <div className="flex-shrink-0">
                  <DollarSign className="h-6 w-6 text-gray-400" />
                </div>
                <div className="ml-5 w-0 flex-1">
                  <dl>
                    <dt className="text-sm font-medium text-gray-500 dark:text-gray-400 truncate">
                      Portfolio Value
                    </dt>
                    <dd className="text-lg font-medium text-gray-900 dark:text-white">
                      $127,482.50
                    </dd>
                  </dl>
                </div>
              </div>
            </div>
          </div>

          <div className="bg-white dark:bg-gray-800 overflow-hidden shadow rounded-lg">
            <div className="p-5">
              <div className="flex items-center">
                <div className="flex-shrink-0">
                  <TrendingUp className="h-6 w-6 text-green-400" />
                </div>
                <div className="ml-5 w-0 flex-1">
                  <dl>
                    <dt className="text-sm font-medium text-gray-500 dark:text-gray-400 truncate">
                      P&L Today
                    </dt>
                    <dd className="text-lg font-medium text-green-600">
                      +$2,431.20
                    </dd>
                  </dl>
                </div>
              </div>
            </div>
          </div>

          <div className="bg-white dark:bg-gray-800 overflow-hidden shadow rounded-lg">
            <div className="p-5">
              <div className="flex items-center">
                <div className="flex-shrink-0">
                  <Activity className="h-6 w-6 text-blue-400" />
                </div>
                <div className="ml-5 w-0 flex-1">
                  <dl>
                    <dt className="text-sm font-medium text-gray-500 dark:text-gray-400 truncate">
                      Portfolio Delta
                    </dt>
                    <dd className="text-lg font-medium text-gray-900 dark:text-white">
                      +127.3
                    </dd>
                  </dl>
                </div>
              </div>
            </div>
          </div>

          <div className="bg-white dark:bg-gray-800 overflow-hidden shadow rounded-lg">
            <div className="p-5">
              <div className="flex items-center">
                <div className="flex-shrink-0">
                  <TrendingDown className="h-6 w-6 text-purple-400" />
                </div>
                <div className="ml-5 w-0 flex-1">
                  <dl>
                    <dt className="text-sm font-medium text-gray-500 dark:text-gray-400 truncate">
                      Portfolio Theta
                    </dt>
                    <dd className="text-lg font-medium text-gray-900 dark:text-white">
                      -$45.20
                    </dd>
                  </dl>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Main Dashboard Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Options Chain */}
          <div className="lg:col-span-2">
            <div className="bg-white dark:bg-gray-800 shadow rounded-lg">
              <div className="px-6 py-4 border-b border-gray-200 dark:border-gray-700">
                <h2 className="text-lg font-medium text-gray-900 dark:text-white">
                  Options Chain - {selectedSymbol}
                </h2>
              </div>
              <div className="p-6">
                <OptionsChain symbol={selectedSymbol} />
              </div>
            </div>
          </div>

          {/* Strategy Builder */}
          <div>
            <div className="bg-white dark:bg-gray-800 shadow rounded-lg">
              <div className="px-6 py-4 border-b border-gray-200 dark:border-gray-700">
                <h2 className="text-lg font-medium text-gray-900 dark:text-white">
                  Strategy Builder
                </h2>
              </div>
              <div className="p-6">
                <StrategyBuilder />
              </div>
            </div>
          </div>
        </div>
      </main>
    </div>
  );
}
